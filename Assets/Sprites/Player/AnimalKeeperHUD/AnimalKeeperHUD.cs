using UnityEngine;
using UnityEngine.UI;

// 체력·스태미나 바 - 다크큐트 시안 C(목줄 이름표). 그림은 Resources/AnimalKeeperHUD/C_*.png.
// 그림 1텍셀 = UI 4단위(Texel). 1080p 도트 크기 = 4 x 씬 Canvas 배율(0.857) x hudScale. 씬 값 1.1667이면 4px(예전 0.875는 3px).
// 어두운 판은 기본으로 그리지 않는다(showPanel).
// 배치(텍셀): 판 (82 + 늘림)x32, 줄마다 아이콘 (2,2) 13x13, 틀 (14,4) (66 + 늘림)x9, 채움 (16,7) (60 + 늘림)x3. 줄 간격 14.
// 늘림(barExtraTexels, 기본 20 → 판 102·틀 86·채움 80): 틀·채움 그림의 가운데 10텍셀(징 1칸 간격)을 그만큼 되풀이해 늘린다 - 모양·색·도트는 그대로.
// 퍼센트 숫자는 그리지 않는다 - 남은 양은 바 길이로만 보인다(C_digits.png는 남겨 두지만 쓰지 않음).
[DisallowMultipleComponent]
public class AnimalKeeperHUD : MonoBehaviour
{
    private const float Texel = 4f;
    private const int BasePanelW = 82, PanelH = 32, RowStep = 14;
    private const int FrameW = 66, FillW = 60;
    private const int RepeatStart = 20, RepeatPeriod = 10; // 틀·채움 그림에서 10텍셀마다 같은 무늬가 되풀이되는 구간(2~61)의 한 칸

    [Tooltip("바(틀·채움)를 늘리는 길이, 텍셀. 징 간격 10의 배수만 쓴다(아니면 내림). 판도 그만큼 길어진다.")]
    [Min(0)] public int barExtraTexels = 20;

    [Range(0f, 100f)] public float healthPercent = 100f;
    [Range(0f, 100f)] public float staminaPercent = 100f;
    public bool smoothChanges = true;
    [Min(1f)] public float changeSpeed = 180f;
    public Vector2 topLeftOffset = new Vector2(24f, 24f);
    [Range(0.5f, 2f)] public float hudScale = 1f;

    [Header("체력 낮음 깜빡임")]
    [Tooltip("체력이 이 비율 이하이면 아이콘·채움이 깜빡인다.")]
    [Range(0f, 1f)] public float lowHealthRatio = 0.3f;
    [Tooltip("어두운 칸/밝은 칸이 바뀌는 간격(초, 실제 시간).")]
    [Min(0.05f)] public float blinkInterval = 0.4f;

    [Header("판 (반투명 없음)")]
    [Tooltip("켜면 아이콘·바 뒤에 어두운 판(외곽선 포함)을 깐다. 끄면 아이콘·틀·채움만 보인다.")]
    public bool showPanel = false;
    public Color32 panelColor = new Color32(42, 37, 48, 255);
    public Color32 panelOutlineColor = new Color32(47, 47, 46, 255);

    private Image hpFill, staminaFill, hpIcon, staminaIcon;
    private Sprite hpFillSprite, hpFillLowSprite, staminaFillSprite, staminaFillTiredSprite;
    private Sprite hpIconSprite, hpIconLowSprite, staminaIconSprite, staminaIconTiredSprite;
    private bool tired;
    private RectTransform root;
    private Canvas ownedCanvas;
    private float displayedHp, displayedStamina;
    private float demoTime = -1f;
    private readonly System.Collections.Generic.List<Sprite> ownedSprites = new System.Collections.Generic.List<Sprite>();
    private readonly System.Collections.Generic.List<Texture2D> ownedTextures = new System.Collections.Generic.List<Texture2D>();

    /// <summary>탈진 그림(회색 채움·땀방울 발바닥)을 쓰는 중인지.</summary>
    public bool IsTired => tired;
    /// <summary>체력 낮음 깜빡임의 밝은 칸인지.</summary>
    public bool IsLowHealthBright { get; private set; }

    private void Start()
    {
        GameObject canvasObject = new GameObject("AnimalKeeper HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        ownedCanvas = canvasObject.GetComponent<Canvas>();
        ownedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ownedCanvas.sortingOrder = 20;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0f;
        root = new GameObject("HUD", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        int extra = Mathf.Max(0, barExtraTexels) / RepeatPeriod * RepeatPeriod;
        int panelW = BasePanelW + extra;
        SetRect(root, 0, 0, panelW * Texel, PanelH * Texel);

        hpFillSprite = LoadSprite("C_fill_hp", extra);
        hpFillLowSprite = LoadSprite("C_fill_hp_low", extra);
        staminaFillSprite = LoadSprite("C_fill_stamina", extra);
        staminaFillTiredSprite = LoadSprite("C_fill_stamina_tired", extra);
        hpIconSprite = LoadSprite("C_icon_hp");
        hpIconLowSprite = LoadSprite("C_icon_hp_low");
        staminaIconSprite = LoadSprite("C_icon_stamina");
        staminaIconTiredSprite = LoadSprite("C_icon_stamina_tired");

        // 판: 모서리 1텍셀을 뺀 외곽선 + 안쪽 판 (showPanel일 때만)
        if (showPanel)
        {
            MakeBlock("Panel Outline V", panelOutlineColor, 1, 0, panelW - 2, PanelH);
            MakeBlock("Panel Outline H", panelOutlineColor, 0, 1, panelW, PanelH - 2);
            MakeBlock("Panel", panelColor, 1, 1, panelW - 2, PanelH - 2);
        }

        Sprite frame = LoadSprite("C_frame", extra);
        BuildRow("HP", hpIconSprite, hpFillSprite, frame, 0, extra, out hpIcon, out hpFill);
        BuildRow("STAMINA", staminaIconSprite, staminaFillSprite, frame, RowStep, extra, out staminaIcon, out staminaFill);
        displayedHp = Mathf.Clamp(healthPercent, 0f, 100f);
        displayedStamina = Mathf.Clamp(staminaPercent, 0f, 100f);
        Refresh();
    }
    private Texture2D LoadTexture(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("AnimalKeeperHUD/" + name);
        if (texture == null) { Debug.LogError("Missing HUD texture: " + name, this); return null; }
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }
    private Sprite MakeSprite(Texture2D texture, Rect rect)
    {
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
        ownedSprites.Add(sprite);
        return sprite;
    }
    private Sprite LoadSprite(string name)
    {
        Texture2D texture = LoadTexture(name);
        return texture == null ? null : MakeSprite(texture, new Rect(0, 0, texture.width, texture.height));
    }
    // 틀·채움처럼 가로로 되풀이되는 그림: RepeatStart부터 10텍셀 한 칸을 extra/10번 더 끼워 넣어 늘린 복사본을 쓴다.
    private Sprite LoadSprite(string name, int extra)
    {
        Texture2D texture = LoadTexture(name);
        if (texture == null || extra <= 0) return texture == null ? null : MakeSprite(texture, new Rect(0, 0, texture.width, texture.height));
        if (!texture.isReadable) { Debug.LogWarning("HUD texture is not readable - bar keeps its length: " + name, this); return MakeSprite(texture, new Rect(0, 0, texture.width, texture.height)); }
        int w = texture.width, h = texture.height, nw = w + extra;
        Color32[] src = texture.GetPixels32(), dst = new Color32[nw * h];
        int insertAt = RepeatStart + RepeatPeriod; // 이 열 앞에 [RepeatStart, RepeatStart + 10) 칸을 되풀이한다
        for (int y = 0; y < h; y++)
            for (int x = 0; x < nw; x++)
            {
                int sx = x < insertAt ? x : x < insertAt + extra ? RepeatStart + (x - insertAt) % RepeatPeriod : x - extra;
                dst[y * nw + x] = src[y * w + sx];
            }
        Texture2D stretched = new Texture2D(nw, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name + "_x" + nw };
        stretched.SetPixels32(dst);
        stretched.Apply(false, true);
        ownedTextures.Add(stretched);
        return MakeSprite(stretched, new Rect(0, 0, nw, h));
    }
    private void SetRect(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }
    // 위치·크기는 텍셀 단위
    private Image MakeImage(string name, Sprite sprite, int x, int y, int w, int h)
    {
        Image image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(root, false);
        image.sprite = sprite;
        image.raycastTarget = false;
        SetRect(image.rectTransform, x * Texel, y * Texel, w * Texel, h * Texel);
        return image;
    }
    // 판 색은 정점 색(Image.color) 대신 그 색 1x1 그림으로 그린다 - 선형 색 공간에서 정점 색은 1~2단계 어긋난다.
    private void MakeBlock(string name, Color32 color, int x, int y, int w, int h)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(new[] { color });
        texture.Apply(false, true);
        ownedTextures.Add(texture);
        MakeImage(name, MakeSprite(texture, new Rect(0, 0, 1, 1)), x, y, w, h);
    }
    private void BuildRow(string name, Sprite icon, Sprite fillSprite, Sprite frame, int y, int extra, out Image iconImage, out Image fill)
    {
        iconImage = MakeImage(name + " Icon", icon, 2, y + 2, 13, 13);
        MakeImage(name + " Frame", frame, 14, y + 4, FrameW + extra, 9);
        fill = MakeImage(name + " Fill", fillSprite, 16, y + 7, FillW + extra, 3);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
    }
    private void Update()
    {
        if (hpFill == null) return;
        if (demoTime >= 0)
        {
            demoTime += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(demoTime / 5f);
            healthPercent = staminaPercent = 100f * (1f - p);
            if (p >= 1f) demoTime = -1f;
        }
        healthPercent = Mathf.Clamp(healthPercent, 0f, 100f);
        staminaPercent = Mathf.Clamp(staminaPercent, 0f, 100f);
        float delta = Mathf.Max(1f, changeSpeed) * Time.unscaledDeltaTime;
        displayedHp = smoothChanges ? Mathf.MoveTowards(displayedHp, healthPercent, delta) : healthPercent;
        displayedStamina = smoothChanges ? Mathf.MoveTowards(displayedStamina, staminaPercent, delta) : staminaPercent;
        Refresh();
    }
    private void Refresh()
    {
        // 판이 없으면 보이는 왼쪽 위 끝은 아이콘(판 안쪽 2텍셀)이다 - 그만큼 당겨 화면 가장자리 여백(topLeftOffset)을 판이 있을 때와 같게 둔다.
        float inset = showPanel ? 0f : 2f * Texel * hudScale;
        root.anchoredPosition = new Vector2(topLeftOffset.x - inset, -(topLeftOffset.y - inset));
        root.localScale = Vector3.one * hudScale;
        hpFill.fillAmount = displayedHp / 100f;
        staminaFill.fillAmount = displayedStamina / 100f;

        bool low = displayedHp <= lowHealthRatio * 100f;
        IsLowHealthBright = low && Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(0.05f, blinkInterval)) % 2 == 1;
        hpFill.sprite = IsLowHealthBright ? hpFillLowSprite : hpFillSprite;
        hpIcon.sprite = IsLowHealthBright ? hpIconLowSprite : hpIconSprite;
        staminaFill.sprite = tired ? staminaFillTiredSprite : staminaFillSprite;
        staminaIcon.sprite = tired ? staminaIconTiredSprite : staminaIconSprite;
    }
    public void SetHealthPercent(float value) { demoTime = -1f; healthPercent = Mathf.Clamp(value, 0, 100); }
    public void SetStaminaPercent(float value) { demoTime = -1f; staminaPercent = Mathf.Clamp(value, 0, 100); }
    public void SetHealth(float current, float maximum) { SetHealthPercent(maximum > 0 ? current / maximum * 100f : 0); }
    public void SetStamina(float current, float maximum) { SetStaminaPercent(maximum > 0 ? current / maximum * 100f : 0); }
    /// <summary>탈진(스태미나 0 뒤 달리기가 막힌 동안)이면 스태미나 채움·아이콘을 탈진 그림으로 바꾼다.</summary>
    public void SetTired(bool value) { tired = value; }
    [ContextMenu("Demo 100 to 0 (Play Mode)")]
    public void DemoDepletion()
    {
        if (!Application.isPlaying || hpFill == null) return;
        healthPercent = staminaPercent = displayedHp = displayedStamina = 100f;
        demoTime = 0;
        Refresh();
    }
    [ContextMenu("Restore Both to 100 (Play Mode)")]
    public void RestoreBoth() { demoTime = -1f; healthPercent = staminaPercent = 100f; }
    private void OnDisable() { if (ownedCanvas != null) ownedCanvas.gameObject.SetActive(false); }
    private void OnEnable() { if (ownedCanvas != null) ownedCanvas.gameObject.SetActive(true); }
    private void OnDestroy()
    {
        foreach (Sprite sprite in ownedSprites) if (sprite != null) Destroy(sprite);
        foreach (Texture2D texture in ownedTextures) if (texture != null) Destroy(texture);
    }
}
