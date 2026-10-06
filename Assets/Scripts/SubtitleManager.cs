using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 화면 하단 중앙의 자막(상호작용 안내/알림 문구)을 전담한다. 모든 스크립트는 자막 Text를 직접 만들거나
/// 켜고 끄지 않고 SubtitleManager.Show("문구", 지속시간)만 호출한다.
///
/// - 씬과 무관하게 하나만 존재한다(첫 호출 때 만들어지고 씬이 바뀌어도 유지). 자기 전용 오버레이 Canvas
///   (Scale With Screen Size, 1920x1080)에 그리므로 어느 씬에서든 위치/크기/테두리가 같다.
/// - 자막 칸은 하나뿐이다. 같은 문구를 다시 Show하면 남은 시간만 늘어나고(매 프레임 호출해도 깜빡이지 않는다),
///   다른 문구가 오면 기존 문구를 짧게 페이드 아웃한 뒤 같은 자리에 새 문구를 페이드 인한다.
/// - 페이드와 지속시간은 unscaled time 기준이라 일시정지(Time.timeScale = 0) 중에도 멈추지 않는다.
///
/// 상호작용 범위 안에 있는 동안만 보여야 하는 안내는 매 프레임 Show(문구, WhileInRange)를 호출하면 된다 -
/// 범위를 벗어나 호출이 끊기면 WhileInRange 뒤에 저절로 사라진다.
/// </summary>
public class SubtitleManager : MonoBehaviour
{
    /// <summary>매 프레임 갱신하는 "범위 안에 있는 동안" 안내용 지속시간. 호출이 끊기면 이만큼 뒤에 페이드 아웃한다.</summary>
    public const float WhileInRange = 0.15f;

    // Resources/에 있는 TMP SDF 폰트 에셋 이름(Tools > Subtitles > Build Subtitle Font Asset으로 만든다).
    private const string FontResourceName = "SubtitleFont";
    private const string FallbackOsFontFamily = "Malgun Gothic";
    private const string FallbackOsFontStyle = "Bold";
    private const string DesktopSdfShaderName = "TextMeshPro/Distance Field";

    // 1920x1080 기준 좌표. 인벤토리 슬롯 줄(2240x1260 기준 캔버스에서 하단 24 + 높이 165 = 189, 이 캔버스로
    // 환산하면 약 162)의 바로 위에 여백 20을 두고 자막의 아래 끝을 맞춘다. 두 캔버스 모두 Scale With Screen Size
    // (match 0.5)라 화면 비율이 달라도 이 둘의 비율은 일정해서 겹치지 않는다.
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private const float BottomOffset = 182f;
    private const float MaxWidth = 1400f;
    private const float FontSize = 34f;
    // TMP 테두리는 글자 경계를 중심으로 안팎에 반씩 그려져서, 테두리만 두껍게 하면 흰 획이 가늘어진다. 글자면(Face)을
    // 그만큼 바깥으로 넓혀(FaceDilate) 획 굵기를 유지한 채 테두리가 또렷하게 보이도록 맞췄다.
    public const float OutlineWidth = 0.25f;
    public const float FaceDilate = 0.25f;
    private const float FadeInDuration = 0.15f;
    private const float FadeOutDuration = 0.12f;
    private const int CanvasSortingOrder = 500; // HUD(0) 위, 일시정지 메뉴(1000) 아래

    private static SubtitleManager instance;
    private static TMP_FontAsset sharedFont;
    private static bool sharedFontResolved;

    /// <summary>자막과 같은 한글 TMP 폰트. 다른 UI(상점 메뉴 등)도 글꼴을 맞추려고 이 값을 쓴다.
    /// Resources/SubtitleFont가 없으면 OS 한글 폰트로 한 번만 만들어 재사용하고, 그것도 없으면 null.</summary>
    public static TMP_FontAsset SharedFont
    {
        get
        {
            if (sharedFontResolved) return sharedFont;
            sharedFontResolved = true;

            sharedFont = Resources.Load<TMP_FontAsset>(FontResourceName);
            if (sharedFont == null)
            {
                // 임시 대체: 폰트 파일을 프로젝트에 넣기 전까지는 OS에 설치된 한글 폰트를 실행 중에 읽어 쓴다(게임에 폰트를
                // 포함하지 않는다). Windows가 아니거나 이 폰트가 없으면 TMP 기본 폰트로 남아 한글이 안 보일 수 있다.
                sharedFont = TMP_FontAsset.CreateFontAsset(FallbackOsFontFamily, FallbackOsFontStyle);
                Debug.LogWarning($"SubtitleManager: Resources/{FontResourceName} 폰트 에셋이 없다 - Assets/Fonts에 폰트를 넣고 " +
                    $"Tools > Subtitles > Build Subtitle Font Asset을 실행하자. 그 전까지 OS 폰트 '{FallbackOsFontFamily}'로 대체" +
                    (sharedFont != null ? "한다." : "하려 했지만 찾지 못했다."));
            }
            return sharedFont;
        }
    }

    private TextMeshProUGUI label;
    private string currentText;
    private string pendingText;
    private float pendingDuration;
    private float hideAt;
    private float alpha;

    public static void Show(string text, float duration)
    {
        if (string.IsNullOrEmpty(text)) return;
        EnsureInstance().Enqueue(text, duration);
    }

    public static void Hide()
    {
        if (instance == null) return;
        instance.pendingText = null;
        instance.hideAt = 0f;
    }

    private static SubtitleManager EnsureInstance()
    {
        if (instance != null) return instance;

        GameObject go = new GameObject("SubtitleManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SubtitleManager>();
        instance.Build();
        return instance;
    }

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

        GameObject textGO = new GameObject("SubtitleText", typeof(RectTransform));
        textGO.transform.SetParent(transform, false);
        RectTransform rt = (RectTransform)textGO.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, BottomOffset);
        rt.sizeDelta = new Vector2(MaxWidth, FontSize * 2.6f);

        label = textGO.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = SharedFont;
        if (font != null) label.font = font;
        // 실행 중에 만든 폰트 에셋은 모바일 SDF 셰이더를 쓰는데, 그 셰이더는 OUTLINE_ON 키워드가 없으면 테두리를 그리지
        // 않는다. 테두리가 항상 켜져 있는 데스크톱 SDF 셰이더로 바꾼다(빌더로 만든 폰트 에셋은 처음부터 이 셰이더다).
        Shader sdf = Shader.Find(DesktopSdfShaderName);
        if (sdf != null && label.fontSharedMaterial != null && label.fontSharedMaterial.shader != sdf)
        {
            label.fontSharedMaterial = new Material(label.fontSharedMaterial) { shader = sdf };
        }
        label.fontSize = FontSize;
        label.alignment = TextAlignmentOptions.Bottom;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        label.color = Color.white; // Face
        label.outlineColor = new Color32(0, 0, 0, 255);
        label.outlineWidth = OutlineWidth;
        label.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);
        label.UpdateMeshPadding();
        label.text = string.Empty;
        label.alpha = 0f;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    // 이전 씬에서 떠 있던 안내가 다음 씬으로 넘어오지 않게 한다.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        pendingText = null;
        currentText = null;
        hideAt = 0f;
        alpha = 0f;
        label.text = string.Empty;
        label.alpha = 0f;
    }

    private void Enqueue(string text, float duration)
    {
        float until = Time.unscaledTime + Mathf.Max(0f, duration);
        if (text == currentText && pendingText == null)
        {
            hideAt = Mathf.Max(hideAt, until);
            return;
        }
        pendingText = text;
        pendingDuration = duration;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (pendingText != null)
        {
            // 다른 문구가 떠 있으면 먼저 페이드 아웃한 뒤 같은 자리에서 교체한다.
            if (alpha > 0f && currentText != null)
            {
                alpha = Mathf.MoveTowards(alpha, 0f, dt / FadeOutDuration);
            }
            if (alpha <= 0f || currentText == null)
            {
                currentText = pendingText;
                label.text = currentText;
                hideAt = Time.unscaledTime + Mathf.Max(0f, pendingDuration);
                pendingText = null;
            }
        }
        else if (currentText != null)
        {
            float target = Time.unscaledTime < hideAt ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, target, dt / (target > alpha ? FadeInDuration : FadeOutDuration));
            if (alpha <= 0f && target == 0f)
            {
                currentText = null;
                label.text = string.Empty;
            }
        }

        label.alpha = alpha;
    }
}
