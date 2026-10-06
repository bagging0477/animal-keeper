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

    // 1920x1080 기준 좌표. 인벤토리 슬롯 줄(2240x1260 기준 캔버스에서 하단 24 + 높이 165 = 189, 이 캔버스로
    // 환산하면 약 162)의 바로 위에 여백 20을 두고 자막의 아래 끝을 맞춘다. 두 캔버스 모두 Scale With Screen Size
    // (match 0.5)라 화면 비율이 달라도 이 둘의 비율은 일정해서 겹치지 않는다.
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private const float BottomOffset = 182f;
    private const float MaxWidth = 1400f;
    private const float FontSize = 34f;
    private const float FadeInDuration = 0.15f;
    private const float FadeOutDuration = 0.12f;
    private const int CanvasSortingOrder = 500; // HUD(0) 위, 일시정지 메뉴(1000) 아래

    private static SubtitleManager instance;

    // 도메인 리로드를 끈 에디터(Enter Play Mode Options)에서도 이전 Play의 인스턴스 참조가 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

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

        // 흰 글자 + 검은 테두리는 공용 머티리얼(UIFont)에 들어 있다. 여기서 outlineWidth 등을 바꾸면 이 글씨만의
        // 머티리얼 복제본이 생겨 전체 스타일과 따로 놀게 되므로 크기/정렬/색만 정한다.
        label = textGO.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(label);
        label.fontSize = FontSize;
        label.alignment = TextAlignmentOptions.Bottom;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        label.color = Color.white;
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
