using UnityEngine;

/// <summary>폭탄이 터질 때 피해 범위를 잠깐 빨간 원으로 보여주는 시각 효과. 별도 스프라이트 에셋
/// 없이 원형 텍스처를 코드로 한 번만 만들어 캐싱해 쓴다 (AudioManager가 임시 효과음을
/// 절차적으로 만들어 쓰는 것과 같은 방식).</summary>
public class BombExplosionEffect : MonoBehaviour
{
    [SerializeField] private float displayDuration = 0.35f;
    [SerializeField] private Color color = new Color(1f, 0f, 0f, 0.5f);

    private static Sprite circleSprite;

    private SpriteRenderer spriteRenderer;
    private float timer;
    private Color startColor;

    /// <summary>월드 좌표 position을 중심으로 지름 radius*2인 빨간 원을 잠깐 띄운다.</summary>
    public static void Spawn(Vector3 position, float radius)
    {
        GameObject go = new GameObject("BombExplosionEffect");
        go.transform.position = position;
        go.AddComponent<BombExplosionEffect>().Show(radius);
    }

    private void Awake()
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetCircleSprite();
        spriteRenderer.sortingOrder = 5;
    }

    private void Show(float radius)
    {
        transform.localScale = Vector3.one * (radius * 2f);
        startColor = color;
        spriteRenderer.color = startColor;
        timer = displayDuration;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Color c = startColor;
        c.a = startColor.a * Mathf.Clamp01(timer / displayDuration);
        spriteRenderer.color = c;
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;

        const int size = 128;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                pixels[y * size + x] = dist <= radius ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        // pixelsPerUnit = size, so the sprite's default (unscaled) diameter is exactly 1 world unit -
        // scaling the GameObject by (radius * 2) then makes its world diameter equal 2 * radius.
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return circleSprite;
    }
}
