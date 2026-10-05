using UnityEngine;

public class FireBurstEffect2D : MonoBehaviour
{
    private SpriteRenderer image;
    private float duration;
    private float targetScale;
    private float elapsed;
    public void Initialize(SpriteRenderer renderer, float seconds, float scale)
    {
        image = renderer;
        duration = Mathf.Max(0.05f, seconds);
        targetScale = Mathf.Max(0.1f, scale);
        ApplyVisual();
    }
    private void Update()
    {
        if (image == null) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= duration) { Destroy(gameObject); return; }
        ApplyVisual();
    }
    private void ApplyVisual()
    {
        float p = Mathf.Clamp01(elapsed / duration);
        float growth = Mathf.Clamp01(p / 0.25f);
        float size = Mathf.Lerp(0.45f, 1f, growth) * targetScale;
        float flicker = Mathf.FloorToInt(elapsed * 18f) % 2 == 0 ? 1f : 0.95f;
        transform.localScale = new Vector3(size * flicker, size, 1f);
        float fade = 1f - Mathf.Clamp01((p - 0.35f) / 0.65f);
        image.color = new Color(1f, 1f, 1f, fade);
    }
}
