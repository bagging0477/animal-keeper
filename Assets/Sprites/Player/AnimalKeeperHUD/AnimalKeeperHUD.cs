using UnityEngine;
using UnityEngine.UI;

// 체력·스태미나 바 - 다크큐트 시안 C(목줄 이름표). 그림은 Resources/AnimalKeeperHUD/C_*.png.
// 그림 1텍셀 = UI 4단위(Texel). 씬 Canvas 배율(1080p 0.857) x hudScale 0.875 = 0.75라 1080p에서 도트 3px.
// 배치(텍셀): 판 102x32, 줄마다 아이콘 (2,2) 13x13, 틀 (14,4) 66x9, 채움 (16,7) 60x3, 숫자 (82,5) 칸 5x7(4텍셀 간격, 왼쪽 정렬). 줄 간격 14.
[DisallowMultipleComponent]
public class AnimalKeeperHUD : MonoBehaviour
{
    private const float Texel = 4f;
    private const int PanelW = 102, PanelH = 32, RowStep = 14;
    private const int GlyphW = 5, GlyphH = 7, GlyphStep = 4, MaxGlyphs = 4;
    private static readonly Color32 DigitBody = new Color32(237, 227, 211, 255);

    [Range(0f, 100f)] public float healthPercent = 100f;
    [Range(0f, 100f)] public float staminaPercent = 100f;
    public bool smoothChanges = true;
    [Min(1f)] public float changeSpeed = 180f;
    public Vector2 topLeftOffset = new Vector2(24f, 24f);
    [Range(0.5f, 2f)] public float hudScale = 1f;

    [Header("체력 낮음 깜빡임")]
    [Tooltip("체력이 이 비율 이하이면 아이콘·채움·숫자가 깜빡인다.")]
    [Range(0f, 1f)] public float lowHealthRatio = 0.3f;
    [Tooltip("어두운 칸/밝은 칸이 바뀌는 간격(초, 실제 시간).")]
    [Min(0.05f)] public float blinkInterval = 0.4f;
    [Tooltip("깜빡임 밝은 칸에서 체력 숫자 색.")]
    public Color32 lowHealthDigitColor = new Color32(224, 122, 130, 255);

    [Header("판 색 (반투명 없음)")]
    public Color32 panelColor = new Color32(42, 37, 48, 255);
    public Color32 panelOutlineColor = new Color32(47, 47, 46, 255);

    private Image hpFill, staminaFill, hpIcon, staminaIcon;
    private Image[] hpDigits, staminaDigits;
    private Sprite hpFillSprite, hpFillLowSprite, staminaFillSprite, staminaFillTiredSprite;
    private Sprite hpIconSprite, hpIconLowSprite, staminaIconSprite, staminaIconTiredSprite;
    private Sprite[] glyphs, lowGlyphs; // 0~9, %
    private bool tired;
    private RectTransform root;
    private Canvas ownedCanvas;
    private float displayedHp, displayedStamina;
    private float demoTime = -1f;
    private readonly System.Collections.Generic.List<Sprite> ownedSprites = new System.Collections.Generic.List<Sprite>();
    private readonly System.Collections.Generic.List<Texture2D> ownedTextures = new System.Collections.Generic.List<Texture2D>();

    /// <summary>지금 체력 숫자 칸에 그려진 글자(예: "47%").</summary>
    public string HealthLabel { get; private set; } = "";
    /// <summary>지금 스태미나 숫자 칸에 그려진 글자.</summary>
    public string StaminaLabel { get; private set; } = "";
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
        SetRect(root, 0, 0, PanelW * Texel, PanelH * Texel);

        hpFillSprite = LoadSprite("C_fill_hp");
        hpFillLowSprite = LoadSprite("C_fill_hp_low");
        staminaFillSprite = LoadSprite("C_fill_stamina");
        staminaFillTiredSprite = LoadSprite("C_fill_stamina_tired");
        hpIconSprite = LoadSprite("C_icon_hp");
        hpIconLowSprite = LoadSprite("C_icon_hp_low");
        staminaIconSprite = LoadSprite("C_icon_stamina");
        staminaIconTiredSprite = LoadSprite("C_icon_stamina_tired");
        LoadGlyphs("C_digits");

        // 판: 모서리 1텍셀을 뺀 외곽선 + 안쪽 판
        MakeBlock("Panel Outline V", panelOutlineColor, 1, 0, PanelW - 2, PanelH);
        MakeBlock("Panel Outline H", panelOutlineColor, 0, 1, PanelW, PanelH - 2);
        MakeBlock("Panel", panelColor, 1, 1, PanelW - 2, PanelH - 2);

        Sprite frame = LoadSprite("C_frame");
        BuildRow("HP", hpIconSprite, hpFillSprite, frame, 0, out hpIcon, out hpFill, out hpDigits);
        BuildRow("STAMINA", staminaIconSprite, staminaFillSprite, frame, RowStep, out staminaIcon, out staminaFill, out staminaDigits);
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
    // digits 그림(0~9, %가 4텍셀 간격으로 붙은 45x7)을 글자별 5x7로 자른다. 깜빡임용으로 숫자 몸통만 장미색으로 바꾼 복사본도 만든다.
    private void LoadGlyphs(string name)
    {
        glyphs = new Sprite[11];
        lowGlyphs = new Sprite[11];
        Texture2D texture = LoadTexture(name);
        if (texture == null) return;
        Texture2D low = texture;
        if (texture.isReadable)
        {
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 0 && pixels[i].r == DigitBody.r && pixels[i].g == DigitBody.g && pixels[i].b == DigitBody.b) pixels[i] = lowHealthDigitColor;
            low = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            low.SetPixels32(pixels);
            low.Apply(false, true);
            ownedTextures.Add(low);
        }
        else Debug.LogWarning("HUD digits texture is not readable - low health digits keep their color.", this);
        for (int i = 0; i < 11; i++)
        {
            Rect rect = new Rect(i * GlyphStep, 0, GlyphW, GlyphH);
            glyphs[i] = MakeSprite(texture, rect);
            lowGlyphs[i] = low == texture ? glyphs[i] : MakeSprite(low, rect);
        }
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
    private void BuildRow(string name, Sprite icon, Sprite fillSprite, Sprite frame, int y, out Image iconImage, out Image fill, out Image[] digits)
    {
        iconImage = MakeImage(name + " Icon", icon, 2, y + 2, 13, 13);
        MakeImage(name + " Frame", frame, 14, y + 4, 66, 9);
        fill = MakeImage(name + " Fill", fillSprite, 16, y + 7, 60, 3);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        digits = new Image[MaxGlyphs];
        for (int i = 0; i < MaxGlyphs; i++)
            digits[i] = MakeImage(name + " Digit " + i, null, 82 + i * GlyphStep, y + 5, GlyphW, GlyphH);
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
        root.anchoredPosition = new Vector2(topLeftOffset.x, -topLeftOffset.y);
        root.localScale = Vector3.one * hudScale;
        hpFill.fillAmount = displayedHp / 100f;
        staminaFill.fillAmount = displayedStamina / 100f;

        bool low = displayedHp <= lowHealthRatio * 100f;
        IsLowHealthBright = low && Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(0.05f, blinkInterval)) % 2 == 1;
        hpFill.sprite = IsLowHealthBright ? hpFillLowSprite : hpFillSprite;
        hpIcon.sprite = IsLowHealthBright ? hpIconLowSprite : hpIconSprite;
        staminaFill.sprite = tired ? staminaFillTiredSprite : staminaFillSprite;
        staminaIcon.sprite = tired ? staminaIconTiredSprite : staminaIconSprite;

        HealthLabel = Mathf.RoundToInt(displayedHp) + "%";
        StaminaLabel = Mathf.RoundToInt(displayedStamina) + "%";
        DrawDigits(hpDigits, HealthLabel, IsLowHealthBright ? lowGlyphs : glyphs);
        DrawDigits(staminaDigits, StaminaLabel, glyphs);
    }
    private static void DrawDigits(Image[] slots, string label, Sprite[] set)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            int index = i < label.Length ? (label[i] == '%' ? 10 : label[i] - '0') : -1;
            bool show = index >= 0 && index <= 10 && set != null && set[index] != null;
            slots[i].enabled = show;
            if (show) slots[i].sprite = set[index];
        }
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
