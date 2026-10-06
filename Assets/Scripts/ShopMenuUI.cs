using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 모든 상인(ShopMerchant)이 함께 쓰는 상점 메뉴. 상인이 Open(목록, 설정)을 부르면 그 ShopCatalog의 품목마다
/// 아이콘/이름/설명/가격 버튼을 한 줄씩 그리고, 맨 위에 보유 마일리지를 보여준다. 품목을 클릭하면 1개씩 산다.
/// 목록이 비어 있으면 catalog.emptyMessage("준비 중입니다")만 보인다.
///
/// - SubtitleManager처럼 씬과 무관하게 하나만 존재하며, 첫 Open 때 자기 전용 오버레이 Canvas를 코드로 만든다
///   (씬마다 UI를 배치할 필요가 없다). 글꼴과 머티리얼은 모든 글씨와 같은 UIFont.
/// - 열려 있는 동안은 TruckHubPoint의 메뉴처럼 Time.timeScale을 0으로 멈춰 플레이어가 움직이지 않게 하고,
///   GameplayInput.IsBlocked로 다른 상호작용/공격 입력도 막는다. E 또는 ESC로 닫는다 - PauseMenu는 다른 화면이
///   시간을 멈춰둔 동안 ESC로 열리지 않으므로(먼저 실행되어 timeScale 0을 보고 물러남) 둘이 겹치지 않는다.
/// - 씬이 바뀌면 항상 닫힌다.
/// </summary>
public class ShopMenuUI : MonoBehaviour
{
    private const int CanvasSortingOrder = 400; // HUD(0) 위, 자막(500)/일시정지 메뉴(1000) 아래 - 구매 실패 자막이 메뉴 위에 보인다.
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private const float PanelWidth = 780f;
    private const float RowHeight = 104f;
    private const float IconSize = 76f;
    private const float FailMessageDuration = 2f;
    private const float SuccessMessageDuration = 1.2f;

    private static readonly Color PanelColor = new Color(0.13f, 0.09f, 0.07f, 0.95f);
    private static readonly Color RowColor = new Color(0.32f, 0.22f, 0.16f, 1f);
    private static readonly Color TextColor = new Color(1f, 0.96f, 0.88f, 1f);
    private static readonly Color SubTextColor = new Color(0.86f, 0.8f, 0.7f, 1f);
    private static readonly Color PriceColor = new Color(1f, 0.85f, 0.3f, 1f);
    private static readonly Color UnaffordableColor = new Color(1f, 0.45f, 0.4f, 1f);

    private static ShopMenuUI instance;
    private static int closedFrame = -1;

    public static bool IsOpen => instance != null && instance.isOpen;

    /// <summary>이번 프레임에 메뉴가 닫혔는지. 닫는 데 쓴 E가 같은 프레임에 상인/문 등의 상호작용으로 다시
    /// 이어지지 않게 GameplayInput이 이 프레임까지 입력을 막는다.</summary>
    public static bool ClosedThisFrame => closedFrame == Time.frameCount;

    private class Row
    {
        public ShopCatalogEntry Entry;
        public TextMeshProUGUI PriceText;
    }

    private GameObject panel;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI mileageText;
    private TextMeshProUGUI emptyText;
    private RectTransform rowContainer;
    private readonly List<Row> rows = new List<Row>();
    private GameObject fallbackEventSystem;

    private ShopCatalog catalog;
    private GameBalanceConfig config;
    private bool isOpen;
    private int openedFrame = -1;
    private int lastPurchaseFrame = -1;
    private int shownMileage = int.MinValue;

    public static void Open(ShopCatalog catalog, GameBalanceConfig config)
    {
        if (catalog == null)
        {
            Debug.LogWarning("ShopMenuUI.Open: 상인에 ShopCatalog가 지정되지 않았다.");
            return;
        }
        EnsureInstance().Show(catalog, config);
    }

    public static void Close()
    {
        if (instance != null) instance.Hide();
    }

    private static ShopMenuUI EnsureInstance()
    {
        if (instance != null) return instance;

        GameObject go = new GameObject("ShopMenuUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ShopMenuUI>();
        instance.Build();
        return instance;
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnDestroy()
    {
        // 열린 채로 파괴되어도 멈춘 시간을 남기지 않는다.
        if (isOpen) Time.timeScale = 1f;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Hide();

    private void Show(ShopCatalog newCatalog, GameBalanceConfig newConfig)
    {
        catalog = newCatalog;
        config = newConfig;

        titleText.text = string.IsNullOrEmpty(catalog.title) ? "상점" : catalog.title;
        RebuildRows();

        isOpen = true;
        openedFrame = Time.frameCount;
        shownMileage = int.MinValue;
        panel.SetActive(true);
        SubtitleManager.Hide();
        EnsureEventSystem();
        Time.timeScale = 0f;
        Refresh();
    }

    private void Hide()
    {
        if (!isOpen) return;

        isOpen = false;
        closedFrame = Time.frameCount;
        panel.SetActive(false);
        Time.timeScale = 1f;

        if (fallbackEventSystem != null)
        {
            Destroy(fallbackEventSystem);
            fallbackEventSystem = null;
        }
    }

    private void Update()
    {
        if (!isOpen) return;

        Refresh();

        // 여는 데 쓴 E가 같은 프레임에 곧바로 닫지 않게, 연 프레임은 건너뛴다.
        if (Time.frameCount == openedFrame || PauseMenu.IsPaused) return;
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) Hide();
    }

    private void OnPurchaseClicked(Row row)
    {
        // 같은 프레임에 클릭이 두 번 들어와도(연타/중복 이벤트) 한 번만 처리한다.
        if (!isOpen || lastPurchaseFrame == Time.frameCount) return;
        lastPurchaseFrame = Time.frameCount;

        // 클릭한 버튼이 선택 상태로 남으면 Space/Enter(Submit)로 또 눌린다 - 선택을 바로 풀어 둔다.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        if (ShopPurchase.TryPurchase(config, row.Entry, out string failMessage))
        {
            SubtitleManager.Show($"{row.Entry.displayName} 구매 완료", SuccessMessageDuration);
        }
        else
        {
            SubtitleManager.Show(failMessage, FailMessageDuration);
        }
        Refresh();
    }

    private void Refresh()
    {
        int mileage = GameManager.Instance != null ? GameManager.Instance.Mileage : 0;
        if (mileage == shownMileage) return;
        shownMileage = mileage;

        mileageText.text = $"보유 마일리지  <color=#FFD84D>{mileage}</color>";
        foreach (Row row in rows)
        {
            int price = ShopPurchase.GetPrice(config, row.Entry.kind);
            row.PriceText.text = $"{price} 마일리지";
            row.PriceText.color = mileage >= price ? PriceColor : UnaffordableColor;
        }
    }

    // ---------- UI 구성 ----------

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(transform, false);
        RectTransform panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PanelWidth, 0f);
        panel.GetComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 30, 26);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = CreateText(panel.transform, "Title", 44f, TextAlignmentOptions.Center, TextColor);
        mileageText = CreateText(panel.transform, "Mileage", 30f, TextAlignmentOptions.Center, SubTextColor);

        GameObject container = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
        container.transform.SetParent(panel.transform, false);
        rowContainer = (RectTransform)container.transform;
        VerticalLayoutGroup rowLayout = container.GetComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        emptyText = CreateText(panel.transform, "Empty", 34f, TextAlignmentOptions.Center, TextColor);
        emptyText.GetComponent<LayoutElement>().minHeight = 120f;

        CreateText(panel.transform, "Hint", 24f, TextAlignmentOptions.Center, SubTextColor).text = "E / ESC  닫기";

        panel.SetActive(false);
    }

    private void RebuildRows()
    {
        for (int i = rowContainer.childCount - 1; i >= 0; i--) Destroy(rowContainer.GetChild(i).gameObject);
        rows.Clear();

        if (catalog.entries != null)
        {
            foreach (ShopCatalogEntry entry in catalog.entries)
            {
                if (entry != null) rows.Add(CreateRow(entry));
            }
        }

        bool empty = rows.Count == 0;
        emptyText.gameObject.SetActive(empty);
        emptyText.text = string.IsNullOrEmpty(catalog.emptyMessage) ? "준비 중입니다" : catalog.emptyMessage;
        rowContainer.gameObject.SetActive(!empty);
    }

    private Row CreateRow(ShopCatalogEntry entry)
    {
        GameObject go = new GameObject(entry.displayName, typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(rowContainer, false);
        go.GetComponent<LayoutElement>().preferredHeight = RowHeight;

        Image background = go.GetComponent<Image>();
        background.color = RowColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.75f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 22, 10, 10);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        iconGo.transform.SetParent(go.transform, false);
        LayoutElement iconLayout = iconGo.GetComponent<LayoutElement>();
        iconLayout.minWidth = iconLayout.preferredWidth = IconSize;
        iconLayout.minHeight = iconLayout.preferredHeight = IconSize;
        Image icon = iconGo.GetComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.sprite = entry.icon;
        icon.enabled = entry.icon != null;

        GameObject textColumn = new GameObject("Text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(go.transform, false);
        textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup columnLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 2f;
        columnLayout.childAlignment = TextAnchor.MiddleLeft;
        columnLayout.childControlWidth = columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = true;
        columnLayout.childForceExpandHeight = false;

        CreateText(textColumn.transform, "Name", 32f, TextAlignmentOptions.Left, TextColor).text =
            string.IsNullOrEmpty(entry.displayName) ? entry.kind.ToString() : entry.displayName;
        CreateText(textColumn.transform, "Description", 20f, TextAlignmentOptions.Left, SubTextColor).text =
            ShopPurchase.GetDescription(config, entry.kind);

        TextMeshProUGUI price = CreateText(go.transform, "Price", 28f, TextAlignmentOptions.Right, PriceColor);
        price.GetComponent<LayoutElement>().preferredWidth = 170f;

        Row row = new Row { Entry = entry, PriceText = price };
        button.onClick.AddListener(() => OnPurchaseClicked(row));
        return row;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, float size, TextAlignmentOptions alignment, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(text);
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    // 버튼 클릭에는 EventSystem이 필요하다. 씬에 없을 때만 메뉴가 열려 있는 동안 임시로 만든다 (PauseMenu와 같은 방식).
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        fallbackEventSystem = new GameObject("ShopMenuEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        fallbackEventSystem.transform.SetParent(transform, false);
    }
}
