using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class AnimalKeeperHUD : MonoBehaviour
{
    [Range(0f, 100f)] public float healthPercent = 100f;
    [Range(0f, 100f)] public float staminaPercent = 100f;
    public bool smoothChanges = true;
    [Min(1f)] public float changeSpeed = 180f;
    public Vector2 topLeftOffset = new Vector2(24f, 24f);
    [Range(0.5f, 2f)] public float hudScale = 1f;
    private Image hpFill, staminaFill;
    private Text hpText, staminaText;
    private RectTransform root;
    private Canvas ownedCanvas;
    private float displayedHp, displayedStamina;
    private float demoTime = -1f;
    private readonly System.Collections.Generic.List<Sprite> ownedSprites = new System.Collections.Generic.List<Sprite>();

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
        SetRect(root, 0, 0, 440, 140);
        Sprite frame = LoadSprite("bar-empty");
        BuildRow("HP", "heart", "hp-fill", frame, 0f, out hpFill, out hpText);
        BuildRow("STAMINA", "lightning", "stamina-fill", frame, 72f, out staminaFill, out staminaText);
        displayedHp = Mathf.Clamp(healthPercent, 0f, 100f);
        displayedStamina = Mathf.Clamp(staminaPercent, 0f, 100f);
        Refresh();
    }
    private Sprite LoadSprite(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("AnimalKeeperHUD/" + name);
        if (texture == null) { Debug.LogError("Missing HUD texture: " + name, this); return null; }
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        ownedSprites.Add(sprite);
        return sprite;
    }
    private void SetRect(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }
    private Image MakeImage(string name, Sprite sprite, float x, float y, float w, float h)
    {
        Image image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(root, false);
        image.sprite = sprite;
        image.raycastTarget = false;
        SetRect(image.rectTransform, x, y, w, h);
        return image;
    }
    private void BuildRow(string name, string icon, string fillName, Sprite frame, float y, out Image fill, out Text text)
    {
        MakeImage(name + " Icon", LoadSprite(icon), 0, y + 4, 56, 56).preserveAspect = true;
        MakeImage(name + " Frame", frame, 64, y, 256, 64);
        fill = MakeImage(name + " Fill", LoadSprite(fillName), 64 + 18, y + 16, 220, 28);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        text = new GameObject(name + " Percent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline)).GetComponent<Text>();
        text.transform.SetParent(root, false);
        SetRect(text.rectTransform, 332, y, 100, 64);
#if UNITY_2022_2_OR_NEWER
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        text.fontSize = 26;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        Outline outline = text.GetComponent<Outline>();
        outline.effectColor = new Color(0.05f, 0.08f, 0.11f, 1);
        outline.effectDistance = new Vector2(2, -2);
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
        hpText.text = Mathf.RoundToInt(displayedHp) + "%";
        staminaText.text = Mathf.RoundToInt(displayedStamina) + "%";
    }
    public void SetHealthPercent(float value) { demoTime = -1f; healthPercent = Mathf.Clamp(value, 0, 100); }
    public void SetStaminaPercent(float value) { demoTime = -1f; staminaPercent = Mathf.Clamp(value, 0, 100); }
    public void SetHealth(float current, float maximum) { SetHealthPercent(maximum > 0 ? current / maximum * 100f : 0); }
    public void SetStamina(float current, float maximum) { SetStaminaPercent(maximum > 0 ? current / maximum * 100f : 0); }
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
    }
}
