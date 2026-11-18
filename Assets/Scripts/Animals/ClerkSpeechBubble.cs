using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 수의사(DeliveryClerk) 머리 위 말풍선(밈). AnimalDeliveryPoint가 동물을 넘긴 순간 Show(직원 그림, 대사)를 부른다.
/// - 자기 화면 오버레이 Canvas(자막과 같은 2240x1260 기준, 자막 500보다 아래 450)에 그리고, 매 프레임 직원 그림 위쪽 끝의 화면 위치를 따라간다.
/// - 그림: GameBalanceConfig.clerkBubbleSprite(9-slice 몸통)·clerkBubbleTailSprite(꼬리). 도트 1칸 = 튜토리얼 안내와 같은 4.67 단위(1080p 4px).
/// - 글씨는 공용 폰트(UIFont)에 색만 어둡게(#2F2F2E) 준다. clerkBubbleDuration 동안 보이고 끝 0.2초에 흐려진다(unscaled 시간).
/// - 다시 부르면 대사를 바꾸고 시간을 처음부터 센다. 일시정지·게임 오버(timeScale 0) 중에는 숨는다. 씬과 함께 사라진다.
/// </summary>
public class ClerkSpeechBubble : MonoBehaviour
{
    private const float PixelUnits = 4.67f;
    private const float FontSize = 45f; // 처음 30에서 1.5배
    private const float ScreenMargin = PixelUnits * 4f; // 화면 가장자리와 띄울 거리
    private const float FadeOut = 0.2f;
    private static readonly Color TextColor = new Color(0x2F / 255f, 0x2F / 255f, 0x2E / 255f, 1f);

    /// <summary>이 씬의 말풍선(없으면 null).</summary>
    public static ClerkSpeechBubble Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Current = null;

    private RectTransform canvasRect, bubble;
    private CanvasGroup group;
    private TextMeshProUGUI label;
    private SpriteRenderer anchor;
    private float shownAt, duration;

    /// <summary>지금 말하는 대사(안 보이면 null).</summary>
    public string Text => group != null && group.alpha > 0f ? label.text : null;

    /// <summary>대사를 보여 준 횟수(이 씬에서).</summary>
    public int ShowCount { get; private set; }

    /// <summary>말풍선 몸통(캡처·테스트용).</summary>
    public RectTransform Bubble => bubble;

    public static void Show(SpriteRenderer speaker, string text)
    {
        if (speaker == null || string.IsNullOrEmpty(text)) return;
        if (Current == null)
        {
            var go = new GameObject("ClerkSpeechBubble");
            Current = go.AddComponent<ClerkSpeechBubble>();
            Current.Build();
        }
        Current.Say(speaker, text);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    private void Build()
    {
        GameBalanceConfig c = GameManager.Instance != null ? GameManager.Instance.Config : null;
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2240f, 1260f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        var bubbleGo = new GameObject("Bubble", typeof(RectTransform));
        bubbleGo.transform.SetParent(transform, false);
        bubble = (RectTransform)bubbleGo.transform;
        bubble.anchorMin = bubble.anchorMax = Vector2.zero;
        bubble.pivot = new Vector2(0.5f, 0f);
        group = bubbleGo.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;
        Image body = bubbleGo.AddComponent<Image>();
        body.sprite = c != null ? c.clerkBubbleSprite : null;
        body.type = Image.Type.Sliced;
        body.pixelsPerUnitMultiplier = 1f / PixelUnits;
        body.raycastTarget = false;
        if (body.sprite == null) body.color = new Color(0.93f, 0.89f, 0.83f, 1f);
        HorizontalLayoutGroup layout = bubbleGo.AddComponent<HorizontalLayoutGroup>();
        int pad = Mathf.RoundToInt(PixelUnits * 7.5f); // 안쪽 여백도 글씨와 같이 1.5배
        layout.padding = new RectOffset(pad, pad, Mathf.RoundToInt(PixelUnits * 4.5f), Mathf.RoundToInt(PixelUnits * 6f));
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        ContentSizeFitter fit = bubbleGo.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(bubble, false);
        label = textGo.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(label);
        label.fontSize = FontSize;
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Midline;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        // 꼬리: 몸통 아래 가운데, 레이아웃에서 빼고(ignoreLayout) 몸통 아래 테두리와 1도트 겹친다.
        Sprite tailSprite = c != null ? c.clerkBubbleTailSprite : null;
        if (tailSprite != null)
        {
            var tailGo = new GameObject("Tail", typeof(RectTransform));
            tailGo.transform.SetParent(bubble, false);
            tailGo.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform tail = (RectTransform)tailGo.transform;
            tail.anchorMin = tail.anchorMax = new Vector2(0.5f, 0f);
            tail.pivot = new Vector2(0.5f, 1f);
            tail.sizeDelta = new Vector2(tailSprite.rect.width, tailSprite.rect.height) * PixelUnits;
            tail.anchoredPosition = new Vector2(0f, PixelUnits);
            Image tailImage = tailGo.AddComponent<Image>();
            tailImage.sprite = tailSprite;
            tailImage.raycastTarget = false;
        }
    }

    private void Say(SpriteRenderer speaker, string text)
    {
        anchor = speaker;
        label.text = text;
        duration = GameManager.Instance != null && GameManager.Instance.Config != null ? GameManager.Instance.Config.clerkBubbleDuration : 2.6f;
        shownAt = Time.unscaledTime;
        ShowCount++;
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubble);
        LateUpdate();
    }

    /// <summary>직원 그림을 따라 자리를 다시 잡는다(매 프레임, 캡처 직전에도 부른다).</summary>
    public void Reposition() => LateUpdate();

    private void LateUpdate()
    {
        if (label == null || string.IsNullOrEmpty(label.text)) return;
        float t = Time.unscaledTime - shownAt;
        group.alpha = t >= duration ? 0f : t <= duration - FadeOut ? 1f : (duration - t) / FadeOut;
        Camera cam = Camera.main;
        // 일시정지·게임 오버(timeScale 0) 화면 위로는 띄우지 않는다.
        if (anchor == null || cam == null || group.alpha <= 0f || Time.timeScale == 0f) { group.alpha = 0f; return; }

        // 직원 그림 위쪽 끝 + 2도트(1/8칸) 위에 꼬리 끝이 오게 한다.
        Bounds b = anchor.bounds;
        Vector3 screen = cam.WorldToScreenPoint(new Vector3(b.center.x, b.max.y + 0.125f, b.center.z));
        Canvas canvas = GetComponent<Canvas>();
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCam, out Vector2 local)) return;
        Vector2 size = canvasRect.rect.size;
        Vector2 pos = local + size * 0.5f; // 왼쪽 아래 기준, 꼬리 끝(직원 머리 위)
        RectTransform tail = bubble.childCount > 1 ? (RectTransform)bubble.GetChild(1) : null;
        float tailHeight = (tail != null ? tail.sizeDelta.y : 0f) - PixelUnits;
        Vector2 box = bubble.rect.size;

        // 몸통은 늘 직원 머리 위(얼굴을 가리지 않음)에 두고, 화면 좌우·위 밖으로 나가면 화면 안으로 민다.
        // 좌우로 밀린 만큼 꼬리를 반대로 옮겨 꼬리 끝은 계속 직원 머리 위를 가리킨다.
        float x = Mathf.Clamp(pos.x, box.x * 0.5f + ScreenMargin, Mathf.Max(box.x * 0.5f + ScreenMargin, size.x - box.x * 0.5f - ScreenMargin));
        float y = Mathf.Min(pos.y + tailHeight, size.y - box.y - ScreenMargin);
        bubble.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        if (tail != null)
        {
            float limit = Mathf.Max(0f, box.x * 0.5f - tail.sizeDelta.x * 0.5f - PixelUnits * 4f);
            tail.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(pos.x - x, -limit, limit)), PixelUnits);
        }
        // 테스트·캡처용: 꼬리 끝이 가리키는 자리와 직원 그림 위쪽 끝(캔버스 단위, 왼쪽 아래 기준)
        SpeakerTop = pos;
    }

    /// <summary>직원 그림 위쪽 끝 바로 위(꼬리 끝이 와야 하는 자리, 캔버스 단위·왼쪽 아래 기준).</summary>
    public Vector2 SpeakerTop { get; private set; }
}
