using UnityEngine;

/// <summary>
/// 몸 스프라이트의 프레임에 맞춰 발밑 그림자(자식 "Shadow")의 그림과 위치를 바꾼다. 그림만 바꾸며 게임 로직에는 관여하지 않는다.
/// 프레임은 텍스처 + 텍스처 안의 사각형으로 찾는다 - SpriteSheetAnimator처럼 실행 중에 Sprite.Create로 만든 프레임도 같은 사각형이면 찾는다.
/// 표에 없는 프레임은 프리팹에 놓인 그림자 그대로 둔다. 몸이 좌우 반전(flipX)되면 가로 위치도 반전한다.
/// 표는 에디터 도구(CharacterShadowBuilder)가 프레임마다 보이는 몸의 맨 아랫줄·폭을 재서 채운다.
/// </summary>
public class ShadowFrameMatcher : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        public Texture2D texture;
        public RectInt rect;          // 텍스처 안의 프레임 사각형(왼쪽 아래 원점, 픽셀)
        public Sprite shadowSprite;
        public Vector2 offset;        // 몸 기준 그림자 로컬 위치(반전하지 않았을 때)
    }

    [SerializeField] private SpriteRenderer body;
    [SerializeField] private SpriteRenderer shadow;
    [SerializeField] private Entry[] entries = new Entry[0];

    private Sprite defaultSprite;
    private Vector3 defaultPosition;
    private Sprite lastBodySprite;
    private bool lastFlip;
    private bool initialized;

    private void Awake()
    {
        if (shadow == null) return;
        defaultSprite = shadow.sprite;
        defaultPosition = shadow.transform.localPosition;
        initialized = true;
    }

    private void LateUpdate()
    {
        if (!initialized || body == null) return;
        Sprite sprite = body.sprite;
        if (sprite == lastBodySprite && body.flipX == lastFlip) return;
        lastBodySprite = sprite;
        lastFlip = body.flipX;

        int index = Find(sprite);
        if (index < 0)
        {
            shadow.sprite = defaultSprite;
            shadow.transform.localPosition = new Vector3(lastFlip ? -defaultPosition.x : defaultPosition.x, defaultPosition.y, defaultPosition.z);
            return;
        }
        Entry e = entries[index];
        shadow.sprite = e.shadowSprite;
        shadow.transform.localPosition = new Vector3(lastFlip ? -e.offset.x : e.offset.x, e.offset.y, defaultPosition.z);
    }

    private int Find(Sprite sprite)
    {
        if (sprite == null) return -1;
        Rect r = sprite.rect;
        int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
        for (int i = 0; i < entries.Length; i++)
        {
            Entry e = entries[i];
            if (e.texture == sprite.texture && e.rect.x == x && e.rect.y == y && e.rect.width == w && e.rect.height == h) return i;
        }
        return -1;
    }
}
