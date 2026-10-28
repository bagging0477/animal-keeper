using UnityEngine;

/// <summary>트래퍼 포획망을 휘두를 때 그물을 펼쳐 던졌다 거두는 그림(시안 B). PlayerWeaponController.SwingNet이 판정하는 그 프레임에 Play를 부른다.
/// - 방향: Play에 받은 방향(판정에 쓴 aimDirection)으로 재생이 끝날 때까지 고정한다 - 판정은 휘두른 순간 한 번이라, 그 뒤 플레이어가 돌아도 그림이 판정과 어긋나지 않게.
/// - 위치: 매 프레임 follow(플레이어) 위치를 따라간다.
/// - 크기: 플레이어 자식이 아니라 따로 둔 오브젝트(배율 1)라 클래스 배율(트래퍼 0.72)이 곱해지지 않는다. 그림은 PPU 16(1텍셀 = 1/16칸),
///   피벗 = 판정 원점이고 다 펼친 그물의 바깥 끝이 판정 거리 2칸·각도 90°와 맞게 그려져 있다.
/// - 정렬: sortAbove(플레이어 몸)와 같은 정렬 레이어, Order + 1. 플레이어 몸 Order는 FootSortOrder가 LateUpdate에서 바꾸므로 그 뒤에 읽는다.</summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(SpriteRenderer))]
public class NetSwingEffect : MonoBehaviour
{
    [Tooltip("재생 순서대로의 칸. 시안 B: 뭉침 → 펼침 → 다 펼침 x2 → 거둠 → 뭉침(6칸).")]
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(1f)] private float frameRate = 20f;
    [Tooltip("따라갈 대상(플레이어).")]
    [SerializeField] private Transform follow;
    [Tooltip("이 렌더러(플레이어 몸)보다 Order 1 위에 그린다.")]
    [SerializeField] private SpriteRenderer sortAbove;

    private SpriteRenderer spriteRenderer;
    private float startTime;

    /// <summary>재생 중인지(그림이 보이는 동안).</summary>
    public bool IsPlaying { get; private set; }
    /// <summary>한 번 재생하는 시간(칸 수 / frameRate). 시안 B는 6 / 20 = 0.30초.</summary>
    public float Duration => frames != null ? frames.Length / frameRate : 0f;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.enabled = false;
    }

    /// <summary>direction(판정에 쓴 바라보는 방향)으로 처음부터 재생한다. 재생 중에 또 불리면 새 방향으로 처음부터 다시.</summary>
    public void Play(Vector2 direction)
    {
        if (frames == null || frames.Length == 0) return;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        startTime = Time.time;
        IsPlaying = true;
        Show(0);
    }

    // 휘두른 프레임(경과 0초)부터 첫 칸을 보여 주도록 경과 시간은 Play 때의 Time.time에서 잰다(일시정지 중에는 멈춘다).
    // 0.3 x 20 = 6처럼 칸 경계에 딱 걸리는 값이 부동소수점으로 5.9999가 돼 한 프레임 더 보이지 않게 아주 작은 값을 더한다.
    private void LateUpdate()
    {
        if (!IsPlaying) return;
        int index = Mathf.FloorToInt((Time.time - startTime) * frameRate + 0.0001f);
        if (index >= frames.Length)
        {
            IsPlaying = false;
            spriteRenderer.enabled = false;
            return;
        }
        Show(index);
    }

    private void Show(int index)
    {
        spriteRenderer.sprite = frames[index];
        spriteRenderer.enabled = true;
        if (follow != null) transform.position = new Vector3(follow.position.x, follow.position.y, transform.position.z);
        if (sortAbove != null)
        {
            spriteRenderer.sortingLayerID = sortAbove.sortingLayerID;
            spriteRenderer.sortingOrder = sortAbove.sortingOrder + 1;
        }
    }
}
