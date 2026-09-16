using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ShelterScene에서 스카우트/트래퍼/아처 3개 클래스를 카드로 보여주고 해금/선택을 담당한다.
/// 카드 UI(테두리, 배경, 텍스트, 잠금 오버레이, 버튼)는 Inspector에서 미리 만들어둘 필요 없이
/// Awake에서 코드로 직접 생성한다 - 씬에는 이 컴포넌트가 붙은 빈 RectTransform 하나만 있으면 된다.</summary>
public class ClassSelectUI : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Vector2 cardSize = new Vector2(230f, 226f);
    [SerializeField] private float cardSpacing = 30f;

    private static readonly PlayerClass[] AllClasses = { PlayerClass.Scout, PlayerClass.Trapper, PlayerClass.Archer };

    private static readonly Color BorderNormalColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color BorderSelectedColor = new Color(1f, 0.82f, 0.15f, 1f);
    private static readonly Color CardBackgroundColor = new Color(0.97f, 0.97f, 0.97f, 1f);
    private static readonly Color LockOverlayColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color ButtonNormalColor = new Color(0.3f, 0.55f, 0.85f, 1f);

    private class CardView
    {
        public PlayerClass PlayerClass;
        public Image Border;
        public GameObject LockOverlay;
        public Text PriceText;
        public Text ActionButtonText;
        public Button ActionButton;
    }

    private readonly List<CardView> cards = new List<CardView>();
    private Font uiFont;

    private void Awake()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCards();
    }

    private void Update()
    {
        RefreshAllCards();
    }

    private void BuildCards()
    {
        RectTransform parent = (RectTransform)transform;
        float totalWidth = cardSize.x * AllClasses.Length + cardSpacing * (AllClasses.Length - 1);
        float startX = -totalWidth * 0.5f + cardSize.x * 0.5f;

        for (int i = 0; i < AllClasses.Length; i++)
        {
            Vector2 anchoredPosition = new Vector2(startX + i * (cardSize.x + cardSpacing), 0f);
            cards.Add(BuildCard(parent, AllClasses[i], anchoredPosition));
        }
    }

    private CardView BuildCard(RectTransform parent, PlayerClass playerClass, Vector2 anchoredPosition)
    {
        const float borderThickness = 4f;

        RectTransform borderRect = CreateRect("ClassCard_" + playerClass, parent, out Image borderImage);
        borderRect.anchoredPosition = anchoredPosition;
        borderRect.sizeDelta = cardSize;
        borderImage.color = BorderNormalColor;

        RectTransform bgRect = CreateRect("Background", borderRect, out Image bgImage);
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(borderThickness, borderThickness);
        bgRect.offsetMax = new Vector2(-borderThickness, -borderThickness);
        bgImage.color = CardBackgroundColor;

        CreateText(bgRect, "Title", GetClassDisplayName(playerClass), 22, FontStyle.Bold, TextAnchor.UpperCenter,
            new Vector2(0f, -8f), new Vector2(cardSize.x - 20f, 26f), new Color(0.1f, 0.1f, 0.1f, 1f));

        CreateText(bgRect, "Stats", BuildStatsText(playerClass), 15, FontStyle.Normal, TextAnchor.UpperLeft,
            new Vector2(0f, -38f), new Vector2(cardSize.x - 36f, 88f), new Color(0.2f, 0.2f, 0.2f, 1f));

        Text priceText = CreateText(bgRect, "Price", "", 15, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0f, -132f), new Vector2(cardSize.x - 20f, 22f), new Color(0.15f, 0.15f, 0.15f, 1f));

        RectTransform buttonRect = CreateRect("ActionButton", bgRect, out Image buttonImage);
        buttonRect.anchorMin = new Vector2(0.5f, 1f);
        buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(0f, -160f);
        buttonRect.sizeDelta = new Vector2(cardSize.x - 40f, 36f);
        buttonImage.color = ButtonNormalColor;
        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        Text buttonText = CreateText(buttonRect, "Text", "", 16, FontStyle.Bold, TextAnchor.MiddleCenter,
            Vector2.zero, buttonRect.sizeDelta, Color.white);

        // 잠금 오버레이는 카드 전체를 덮되 raycastTarget을 꺼서, 잠긴 상태에서도 아래의 구매 버튼 클릭이 그대로 통과되게 한다.
        RectTransform lockRect = CreateRect("LockOverlay", bgRect, out Image lockImage);
        lockRect.anchorMin = Vector2.zero;
        lockRect.anchorMax = Vector2.one;
        lockRect.offsetMin = Vector2.zero;
        lockRect.offsetMax = Vector2.zero;
        lockImage.color = LockOverlayColor;
        lockImage.raycastTarget = false;
        CreateText(lockRect, "LockText", "잠김", 20, FontStyle.Bold, TextAnchor.UpperCenter,
            new Vector2(0f, -70f), new Vector2(cardSize.x - 36f, 32f), Color.white).raycastTarget = false;

        PlayerClass captured = playerClass;
        button.onClick.AddListener(() => OnCardButtonClicked(captured));

        return new CardView
        {
            PlayerClass = playerClass,
            Border = borderImage,
            LockOverlay = lockRect.gameObject,
            PriceText = priceText,
            ActionButtonText = buttonText,
            ActionButton = button
        };
    }

    private static RectTransform CreateRect(string name, Transform parent, out Image image)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        // 새로 만든 RectTransform의 기본 앵커(0,0)는 부모 좌측 하단 기준이라, anchoredPosition을
        // "부모 중심 기준 오프셋"으로 쓰려는 이 스크립트의 계산과 어긋난다. 중앙 앵커/피벗을 기본값으로 둔다.
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        image = go.GetComponent<Image>();
        return rect;
    }

    private Text CreateText(Transform parent, string name, string content, int fontSize, FontStyle style,
        TextAnchor anchor, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Text text = go.GetComponent<Text>();
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.text = content;
        return text;
    }

    private void RefreshAllCards()
    {
        foreach (CardView card in cards) RefreshCard(card);
    }

    private void RefreshCard(CardView card)
    {
        GameManager gm = GameManager.Instance;
        bool unlocked = gm != null && gm.IsClassUnlocked(card.PlayerClass);
        bool selected = gm != null && gm.CurrentClass == card.PlayerClass;
        int mileage = gm != null ? gm.Mileage : 0;
        int price = config != null ? config.GetClassUnlockPrice(card.PlayerClass) : 0;

        card.Border.color = selected ? BorderSelectedColor : BorderNormalColor;
        card.LockOverlay.SetActive(!unlocked);

        if (!unlocked)
        {
            card.PriceText.text = $"{price} 마일리지";
            card.ActionButtonText.text = "구매";
            card.ActionButton.interactable = mileage >= price;
        }
        else
        {
            card.PriceText.text = selected ? "현재 장착 중" : "해금됨";
            card.ActionButtonText.text = selected ? "선택됨" : "선택";
            card.ActionButton.interactable = !selected;
        }
    }

    private void OnCardButtonClicked(PlayerClass playerClass)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        if (!gm.IsClassUnlocked(playerClass))
        {
            gm.PurchaseClass(playerClass);
        }
        else
        {
            gm.SelectClass(playerClass);
        }

        RefreshAllCards();
    }

    private string BuildStatsText(PlayerClass playerClass)
    {
        int healthPercent = config != null ? config.GetClassMaxHealthPercent(playerClass) : 100;
        float speedMultiplier = config != null ? config.GetClassMoveSpeedMultiplier(playerClass) : 1f;
        string speedLine = Mathf.Approximately(speedMultiplier, 1f)
            ? "이동속도: 평균"
            : $"이동속도: 평균의 {speedMultiplier:0.##}배";
        string weaponLine = playerClass switch
        {
            PlayerClass.Trapper => "무기: 근접 (포획망)",
            PlayerClass.Archer => "무기: 원거리 (마취화살)",
            _ => "무기: 없음"
        };

        return $"체력: {healthPercent}%\n{speedLine}\n{weaponLine}";
    }

    private static string GetClassDisplayName(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => "스카우트",
        PlayerClass.Trapper => "트래퍼",
        PlayerClass.Archer => "아처",
        _ => playerClass.ToString()
    };
}
