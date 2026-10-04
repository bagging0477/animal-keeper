using UnityEngine;
using UnityEngine.UI;

// Attach to an empty GameObject in a dedicated title scene, then press Play.
public class AnimalKeeperTitle : MonoBehaviour
{
    [Min(1f)] public float truckSpeed = 52f;
    [Min(0f)] public float followDelay = 2.3f;
    [Min(0.1f)] public float followBlendDuration = 1.5f;
    [Min(1f)] public float wheelFramesPerSecond = 8f;
    public bool enableDust = true;
    public bool enableBounce = true;
    private RectTransform surface;
    private RawImage truck;
    private Texture2D[] frames;
    private RawImage[] dust;
    private float elapsed;
    private readonly System.Collections.Generic.List<ScrollingLayer> layers =
        new System.Collections.Generic.List<ScrollingLayer>();
    private class ScrollingLayer
    {
        public RectTransform[] tiles;
        public float factor;
        public float drift;
    }
    private void Start()
    {
        GameObject canvasObject = new GameObject("AnimalKeeper Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(480, 300);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        surface = new GameObject("Title Surface", typeof(RectTransform), typeof(RectMask2D), typeof(AspectRatioFitter)).GetComponent<RectTransform>();
        surface.SetParent(canvasObject.transform, false);
        surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.pivot = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;
        AspectRatioFitter fitter = surface.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1.6f;
        AddImage("sky", Load("sky"), 0, 0, 480, 300);
        AddLayer("clouds", 0.12f, 2f);
        AddLayer("hills", 0.25f, 0f);
        AddLayer("far-trees", 0.65f, 0f);
        AddLayer("near-trees", 1f, 0f);
        AddImage("ground", Load("ground"), 0, 0, 480, 300);
        AddLayer("markings", 1f, 0f);
        AddLayer("flowers", 1.2f, 0f);
        dust = new RawImage[5];
        for (int i = 0; i < dust.Length; i++)
            dust[i] = AddImage("Dust", Texture2D.whiteTexture, 0, 0, 4, 3);
        frames = new Texture2D[] { Load("truck-01"), Load("truck-02") };
        truck = AddImage("Truck", frames[0], 64, 183, 100, 65);
        AddImage("Title", Load("title"), 0, 35, 480, 120);
        UpdateVisuals();
    }
    private Texture2D Load(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("AnimalKeeperTitle/" + name);
        if (texture == null) Debug.LogError("Missing title texture: " + name, this);
        else { texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; }
        return texture;
    }
    private RawImage AddImage(string name, Texture texture, float x, float y, float w, float h)
    {
        RawImage image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(surface, false);
        image.texture = texture;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        Place(rect, x, y, w, h);
        return image;
    }
    private void Place(RectTransform rect, float x, float y, float w, float h)
    {
        float scale = surface.rect.width > 0f ? surface.rect.width / 480f : 1f;
        rect.anchoredPosition = new Vector2(Mathf.Round(x) * scale, -Mathf.Round(y) * scale);
        rect.sizeDelta = new Vector2(w, h) * scale;
    }
    private void AddLayer(string name, float factor, float drift)
    {
        ScrollingLayer layer = new ScrollingLayer { tiles = new RectTransform[3], factor = factor, drift = drift };
        Texture2D texture = Load(name);
        for (int i = 0; i < 3; i++) layer.tiles[i] = AddImage(name, texture, (i - 1) * 480, 0, 480, 300).rectTransform;
        layers.Add(layer);
    }
    private void Update() { elapsed += Time.unscaledDeltaTime; }
    private void LateUpdate() { if (truck != null) UpdateVisuals(); }
    private void UpdateVisuals()
    {
        float d = Mathf.Max(0, elapsed - followDelay);
        float a = Mathf.Clamp01(d / Mathf.Max(0.1f, followBlendDuration));
        float camera = d < followBlendDuration
            ? truckSpeed * followBlendDuration * (a * a * a - 0.5f * a * a * a * a)
            : truckSpeed * (d - followBlendDuration * 0.5f);
        float x = 72f + truckSpeed * elapsed - camera;
        foreach (ScrollingLayer layer in layers)
        {
            float offset = Mathf.Repeat(camera * layer.factor + elapsed * layer.drift, 480f);
            for (int i = 0; i < 3; i++) Place(layer.tiles[i], (i - 1) * 480f - offset, 0, 480, 300);
        }
        // Reapply stationary layers on resolution changes too.
        foreach (Transform child in surface)
        {
            RawImage image = child.GetComponent<RawImage>();
            if (image == null) continue;
            if (child.name == "sky" || child.name == "ground") Place(image.rectTransform, 0, 0, 480, 300);
            else if (child.name == "Title") Place(image.rectTransform, 0, 35, 480, 120);
        }
        float bounce = enableBounce ? Mathf.Round(Mathf.Sin(elapsed * 15f) * 0.8f) : 0f;
        Place(truck.rectTransform, x - 8f, 183f + bounce, 100, 65);
        truck.texture = frames[Mathf.FloorToInt(elapsed * wheelFramesPerSecond) % 2];
        for (int i = 0; i < dust.Length; i++)
        {
            float age = Mathf.Repeat(elapsed * 3f + i * 0.21f, 1f);
            dust[i].enabled = enableDust;
            dust[i].color = new Color(239f / 255f, 228f / 255f, 188f / 255f, (1f - age) * 0.6f);
            Place(dust[i].rectTransform, x - 8f - age * 35f, 229f - age * 10f, 3 + Mathf.Floor(age * 4f), 2 + Mathf.Floor(age * 3f));
        }
    }
}
