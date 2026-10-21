using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 마을(VillageScene)이 시작될 때마다 마을 경과 시간을 게임 속 시계("HH:MM")로 화면 가운데에 잠깐 크게 보여 준다(항상 보이는 시계는 없다).
/// - 시계 값: 실제 경과 시간(GameManager.VillageTime)을 villageClockTickSeconds 단위로 내림 x villageClockScale(초). 표시할 때 분 아래는 버린다.
///   기본값(1초, 30배)이면 실제 1초마다 시계 30초씩 점프하고, 실제 600초 = 05:00이다. 5시간을 넘어도 계속 올라간다(05:01, 06:00 …).
/// - 표시: villageClockFlashDuration(실제 시간, 일시정지와 무관) 동안 보이고 마지막 0.15초 동안 투명해지며 사라진다. 보이는 동안에도 매 프레임 값을 다시 계산한다.
///   씬을 불러온 프레임(맵 생성으로 길다)은 세지 않고, 한 프레임에 0.1초보다 많이 세지 않는다 - 끊김 한 번에 시계가 안 보이고 지나가지 않게.
///   마을 씬을 떠나면 바로 숨긴다. 입력을 받지 않는 오버레이라 조작과 시간 흐름에 영향이 없다.
/// GameManager가 Awake에서 자기 오브젝트에 붙인다(씬 파일은 바꾸지 않는다). 글자는 공용 TMP 폰트와 공용 머티리얼(검은 외곽선)을 쓰고 색만 바꾼다.
/// </summary>
public class VillageClockFlash : MonoBehaviour
{
    private const string VillageSceneName = "VillageScene";
    private const float FadeOutDuration = 0.15f;
    private const int CanvasSortingOrder = 450; // HUD(0) 위, 자막(500)·일시정지 메뉴(1000) 아래
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private const float TickEpsilon = 1e-4f; // 1/30초씩 쌓인 시간이 0.99999처럼 정수 바로 아래에 머무는 오차를 흡수한다
    // 마을을 불러온 프레임은 맵 생성 때문에 실제로 1초 가까이 걸린다. 그 프레임의 시간을 그대로 세면 화면에 그려지기도 전에
    // 표시 시간이 다 지나 버리므로, 띄운 직후 첫 프레임은 세지 않고 한 프레임에 이 값보다 많이 세지 않는다.
    private const float MaxCountedFrameTime = 0.1f;

    private GameObject canvasObject;
    private TextMeshProUGUI label;
    private bool showing;
    private bool skipNextFrame;
    private float shownSeconds;
    private float duration;

    /// <summary>지금 시계를 보여 주는 중인지.</summary>
    public bool IsShowing => showing;
    /// <summary>보여 주기 시작한 뒤 지난 실제 시간(초, unscaled).</summary>
    public float ShownSeconds => shownSeconds;
    /// <summary>마을에 들어와 시계를 띄운 횟수.</summary>
    public int ShowCount { get; private set; }
    /// <summary>마지막으로 띄운 순간의 시계 문자열.</summary>
    public string TextAtShow { get; private set; }
    /// <summary>시계 글자(테스트·캡처용). 아직 한 번도 띄우지 않았으면 null.</summary>
    public TextMeshProUGUI Label => label;

    /// <summary>마을 경과 시간(실제 초)을 시계 초로 바꾼다: 점프 간격으로 내림한 실제 시간 x 배율.</summary>
    public static double ClockSeconds(float villageSeconds, GameBalanceConfig config)
    {
        double scale = config != null ? config.villageClockScale : 30.0;
        double tick = config != null && config.villageClockTickSeconds > 0f ? config.villageClockTickSeconds : 1.0;
        double ticks = System.Math.Floor(System.Math.Max(0.0, villageSeconds) / tick + TickEpsilon);
        return ticks * tick * scale;
    }

    /// <summary>마을 경과 시간(실제 초)을 "HH:MM" 시계 문자열로 바꾼다(분 아래는 버림).</summary>
    public static string FormatClock(float villageSeconds, GameBalanceConfig config)
    {
        long minutes = (long)System.Math.Floor(ClockSeconds(villageSeconds, config) / 60.0 + 1e-9);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Hide();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == VillageSceneName) Show();
        else Hide();
    }

    private void Show()
    {
        GameManager gm = GameManager.Instance;
        GameBalanceConfig config = gm != null ? gm.Config : null;
        if (config == null)
        {
            Debug.LogWarning($"{name}: GameManager 또는 GameBalanceConfig가 없어 마을 시계를 띄우지 않는다.");
            return;
        }
        if (!EnsureLabel()) return;

        duration = Mathf.Max(0f, config.villageClockFlashDuration);
        shownSeconds = 0f;
        skipNextFrame = true;
        showing = duration > 0f;
        label.color = config.villageClockFlashColor;
        label.fontSize = config.villageClockFlashFontSize;
        label.text = FormatClock(gm.VillageTime, config);
        label.alpha = 1f;
        TextAtShow = label.text;
        ShowCount++;
        canvasObject.SetActive(showing);
    }

    private void Hide()
    {
        showing = false;
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    private void Update()
    {
        if (!showing) return;
        if (SceneManager.GetActiveScene().name != VillageSceneName) { Hide(); return; }

        if (skipNextFrame) skipNextFrame = false; // 불러오는 데 걸린 시간은 세지 않는다
        else shownSeconds += Mathf.Min(Time.unscaledDeltaTime, MaxCountedFrameTime);
        if (shownSeconds >= duration) { Hide(); return; }

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.Config != null) label.text = FormatClock(gm.VillageTime, gm.Config);
        float remaining = duration - shownSeconds;
        label.alpha = remaining < FadeOutDuration ? remaining / FadeOutDuration : 1f;
    }

    private bool EnsureLabel()
    {
        if (label != null) return true;
        if (UIFont.Font == null)
        {
            Debug.LogWarning($"{name}: TMP 기본 폰트(UIFont)가 없어 마을 시계를 띄우지 않는다.");
            return false;
        }

        canvasObject = new GameObject("VillageClockCanvas", typeof(RectTransform));
        canvasObject.layer = 5; // UI
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // GraphicRaycaster를 두지 않는다 - 클릭·입력을 가로채지 않는다.

        GameObject textObject = new GameObject("VillageClockText", typeof(RectTransform));
        textObject.layer = 5;
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rt = (RectTransform)textObject.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(900f, 240f);
        rt.localScale = Vector3.one;

        // 공용 머티리얼(흰 Face + 검은 Outline)을 그대로 쓰고 정점 색만 주황으로 바꾼다 - 머티리얼을 복제하지 않는다(UIFont 참고).
        label = textObject.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(label);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        label.text = string.Empty;
        canvasObject.SetActive(false);
        return true;
    }
}
