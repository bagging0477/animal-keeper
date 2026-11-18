using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 게임 오버 화면 앞에 잠깐 나오는 장면(밈): 밤 타이틀 그림 위로 트럭이 지나가 화면 밖으로 떠난다. GameOverPanel.Show가 패널 맨 위에 붙인다.
/// - 배경: 예전 밤 타이틀(Resources/AnimalKeeperNight, 커밋 0bd43a2에서 타이틀에 쓰던 그림)의 하늘·구름·언덕·나무·땅·도로 표시·꽃.
///   제목 글씨(title.png)와 버튼 등 UI는 넣지 않는다. 원본 480x300(16:10)을 비율 그대로 화면을 꽉 채우게 키우고 넘치는 곳은 자른다.
/// - 트럭: 같은 세트의 truck-01·02(바퀴 2장)와 전조등·먼지. 왼쪽에서 출발해 점점 빨라지며 오른쪽 화면 밖으로 나간다. 배경은 멈춰 있다.
/// - 패널 내용(제목·문구·다시 시작 버튼)은 그대로 둔 채 위를 덮었다가, 장면이 끝나면 흐려지며 사라진다 → 그 뒤 화면은 예전 게임 오버 화면 그대로.
///   덮는 동안에는 마우스 클릭이 버튼에 닿지 않는다. 아무 키나 클릭으로 건너뛸 수 있다(바로 흐려지기 시작).
/// - 시간: 패널이 Time.timeScale을 0으로 멈추므로 unscaled 시간으로 돈다.
/// </summary>
public class GameOverTruckScene : MonoBehaviour
{
    private const float ArtW = 480f, ArtH = 300f;
    // 트럭 왼쪽 끝 x(그림 좌표) = StartX + Speed·t + Accel·t² → 약 2.4초에 전조등까지 화면 밖
    private const float StartX = 40f, Speed = 60f, Accel = 55f, TruckY = 183f;
    private const float FadeStart = 3.0f, FadeDuration = 0.35f, WheelFps = 8f;
    public const float TotalDuration = FadeStart + FadeDuration;

    private static readonly string[] Layers = { "sky", "clouds", "hills", "far-trees", "near-trees", "ground", "markings", "flowers" };

    /// <summary>지금 재생 중인 장면(없으면 null).</summary>
    public static GameOverTruckScene Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Current = null;

    private CanvasGroup group;
    private RectTransform surface;
    private RawImage truck, headlight;
    private RawImage[] dust;
    private Texture2D[] frames;
    private float startTime, skipAt = -1f;

    /// <summary>시작한 뒤 지난 시간(초, unscaled).</summary>
    public float Elapsed => Time.unscaledTime - startTime;

    /// <summary>트럭 왼쪽 끝 위치(그림 좌표 0~480). 캡처·테스트용.</summary>
    public float TruckX { get; private set; }

    /// <summary>트럭이 화면(그림) 밖으로 다 나갔는지.</summary>
    public bool TruckGone => TruckX - 8f > ArtW;

    /// <summary>배경 그림을 하나라도 못 불러왔는지.</summary>
    public bool MissingArt { get; private set; }

    /// <summary>panel 위에 장면을 붙여 재생한다. 이미 재생 중이면 처음부터 다시 한다.</summary>
    public static GameOverTruckScene Play(RectTransform panel, Color background)
    {
        if (panel == null) return null;
        if (Current != null) Destroy(Current.gameObject);
        var go = new GameObject("GameOverTruckScene", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        go.transform.SetAsLastSibling();
        Current = go.AddComponent<GameOverTruckScene>();
        Current.Build(background);
        return Current;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    private void Build(Color background)
    {
        RectTransform root = (RectTransform)transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        group = gameObject.AddComponent<CanvasGroup>();
        Image cover = gameObject.AddComponent<Image>();
        cover.color = background;
        cover.raycastTarget = true; // 장면 동안 버튼 클릭을 막는다
        gameObject.AddComponent<RectMask2D>(); // 화면을 넘치는 배경·밖으로 나간 트럭을 자른다

        // 원본 480x300을 비율 그대로 화면을 꽉 채우게(넘치면 자름).
        var surfaceGo = new GameObject("NightTitleArt", typeof(RectTransform), typeof(AspectRatioFitter));
        surfaceGo.transform.SetParent(root, false);
        surface = (RectTransform)surfaceGo.transform;
        surface.anchorMin = surface.anchorMax = surface.pivot = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;
        AspectRatioFitter fitter = surfaceGo.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = ArtW / ArtH;

        foreach (string layer in Layers)
        {
            Add(layer, Load(layer), 0f, 0f, ArtW, ArtH);
        }
        headlight = Add("Headlight", Load("headlight"), 0f, 0f, 90f, 33f);
        dust = new RawImage[5];
        for (int i = 0; i < dust.Length; i++) dust[i] = Add("Dust", Texture2D.whiteTexture, 0f, 0f, 4f, 3f);
        frames = new[] { Load("truck-01"), Load("truck-02") };
        truck = Add("Truck", frames[0], StartX - 8f, TruckY, 100f, 65f);

        startTime = Time.unscaledTime;
        Canvas.ForceUpdateCanvases();
        Update();
    }

    private Texture2D Load(string name)
    {
        Texture2D t = Resources.Load<Texture2D>("AnimalKeeperNight/" + name);
        if (t == null) { MissingArt = true; Debug.LogWarning("GameOverTruckScene: 밤 타이틀 그림 없음 " + name); }
        return t;
    }

    private RawImage Add(string name, Texture texture, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(surface, false);
        RawImage img = go.GetComponent<RawImage>();
        img.texture = texture;
        img.raycastTarget = false;
        if (texture == null) img.color = Color.clear;
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = img.rectTransform.pivot = new Vector2(0f, 1f);
        Place(img.rectTransform, x, y, w, h);
        return img;
    }

    // 그림 좌표(왼쪽 위 원점, 480x300)로 놓는다. 원본 AnimalKeeperNight처럼 위치는 원본 픽셀 단위로 반올림.
    private void Place(RectTransform rt, float x, float y, float w, float h)
    {
        float scale = surface.rect.width > 0f ? surface.rect.width / ArtW : 1f;
        rt.anchoredPosition = new Vector2(Mathf.Round(x) * scale, -Mathf.Round(y) * scale);
        rt.sizeDelta = new Vector2(w, h) * scale;
    }

    private void Update()
    {
        float t = Elapsed;
        if (t > 0.2f && skipAt < 0f && t < FadeStart && AnyPress()) skipAt = t;
        float fadeFrom = skipAt >= 0f ? skipAt : FadeStart;
        if (t >= fadeFrom)
        {
            float a = 1f - (t - fadeFrom) / FadeDuration;
            group.alpha = Mathf.Clamp01(a);
            if (a <= 0f) { Destroy(gameObject); return; }
        }

        // 화면 크기가 바뀌어도 맞도록 배경을 매번 다시 놓는다.
        foreach (Transform child in surface)
        {
            if (child.name == "Truck" || child.name == "Headlight" || child.name == "Dust") continue;
            Place((RectTransform)child, 0f, 0f, ArtW, ArtH);
        }

        float x = StartX + Speed * t + Accel * t * t;
        TruckX = x;
        float bounce = Mathf.Round(Mathf.Sin(t * 15f) * 0.8f);
        Place(truck.rectTransform, x - 8f, TruckY + bounce, 100f, 65f);
        Place(headlight.rectTransform, x + 82f, 205f + bounce, 90f, 33f);
        if (frames[0] != null && frames[1] != null) truck.texture = frames[Mathf.FloorToInt(t * WheelFps) % 2];
        for (int i = 0; i < dust.Length; i++)
        {
            float age = Mathf.Repeat(t * 3f + i * 0.21f, 1f);
            dust[i].color = new Color(138f / 255f, 133f / 255f, 145f / 255f, (1f - age) * 0.6f);
            Place(dust[i].rectTransform, x - 8f - age * 35f, 229f - age * 10f, 3f + Mathf.Floor(age * 4f), 2f + Mathf.Floor(age * 3f));
        }
    }

    private static bool AnyPress()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }
}
