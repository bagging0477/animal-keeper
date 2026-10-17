using UnityEngine;

/// <summary>폭탄이 터질 때 피해 범위를 보여주는 시각 효과. 화염 스프라이트가 있으면
/// InstantBombUnity 패키지의 FireBurstEffect2D로 화염이 커지며 흔들리다 사라지는 연출을 쓰고,
/// 없으면 원형 텍스처를 코드로 한 번만 만들어 캐싱한 빨간 원으로 대신한다 (AudioManager가 임시
/// 효과음을 절차적으로 만들어 쓰는 것과 같은 방식).</summary>
public class BombExplosionEffect : MonoBehaviour
{
    private const int SortingOrder = 5;

    private static Sprite circleSprite;

    private SpriteRenderer spriteRenderer;
    private float timer;
    private float displayDuration;
    private Color startColor;

    /// <summary>월드 좌표 position을 중심으로 지름 radius*2인 원을 duration초 동안 color로 잠깐 띄운다.</summary>
    public static void Spawn(Vector3 position, float radius, float duration, Color color)
    {
        GameObject go = new GameObject("BombExplosionEffect");
        go.transform.position = position;
        go.AddComponent<BombExplosionEffect>().Show(radius, duration, color);
    }

    /// <summary>월드 좌표 position에 fireSprite 화염을 duration초 동안 띄운다. 화염이 최대로 커졌을 때
    /// 보이는 폭이 피해 지름(radius*2)과 같다. visibleRatio는 스프라이트 캔버스 폭 중 실제 화염 몸통이
    /// 차지하는 비율로, 투명 여백이 있는 이미지에서 캔버스가 아니라 화염 자체를 범위에 맞추기 위해 쓴다.</summary>
    public static void SpawnFire(Vector3 position, float radius, float duration, Sprite fireSprite, float visibleRatio)
    {
        GameObject go = new GameObject("BombFireExplosion");
        go.transform.position = position;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = fireSprite;
        renderer.sortingOrder = SortingOrder;

        float visibleWidth = fireSprite.bounds.size.x * visibleRatio;
        float scale = radius * 2f / Mathf.Max(0.01f, visibleWidth);
        go.AddComponent<FireBurstEffect2D>().Initialize(renderer, duration, scale);
    }

    private void Awake()
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetCircleSprite();
        spriteRenderer.sortingOrder = SortingOrder;
    }

    private void Show(float radius, float duration, Color color)
    {
        transform.localScale = Vector3.one * (radius * 2f);
        displayDuration = duration;
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
