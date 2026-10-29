using System.Collections.Generic;
using UnityEngine;

/// <summary>호저(PorcupineMonsterAI)가 쏘는 가시. 마취총 다트(TranquilizerDart)와 같은 구조다:
/// - Launch에서 방향·속도·사거리를 받고, 사거리만큼 날아가면 사라진다(마지막 프레임은 남은 거리만큼만 움직여 정확히 사거리에서 멈춘다).
/// - 매 프레임 이번에 지나간 구간 전체를 고정 충돌체(맵 경계, 장애물, 트럭)로 검사한다(StaticBlockerLine - 몬스터 시야·다트와 같은 규칙).
///   벽에 닿으면 그 자리에서 사라진다.
/// - 벽보다 앞에서 플레이어(중심에서 hitRadius 안)를 지나가면 피해를 주고 사라진다. 지나간 구간 전체로 보므로 빨라도 건너뛰지 않는다.
/// 그림은 quill 2장을 반복하고, 날아가는 방향으로 돌린다. 발 기준 정렬은 땅에서 heightAboveGround만큼 떠 있는 것으로 계산한다.</summary>
public class PorcupineQuill : MonoBehaviour
{
    /// <summary>날아가는 중인 가시(테스트가 센다).</summary>
    public static readonly List<PorcupineQuill> Active = new List<PorcupineQuill>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveList() => Active.Clear();

    [SerializeField] private Sprite[] frames;
    [SerializeField] private float frameRate = 10f;
    [Tooltip("플레이어 중심에서 이 거리(칸) 안을 지나가면 맞는다.")]
    [SerializeField] private float hitRadius = 0.45f;
    [Tooltip("가시를 막는 충돌체의 레이어. 트리거와 움직이는 리지드바디는 건너뛰고 고정 충돌체만 벽으로 본다(다트와 같은 규칙).")]
    [SerializeField] private LayerMask wallMask = ~0;
    [Tooltip("땅에서 떠 있는 높이(칸) - 발 기준 정렬(FootSortOrder)에 쓴다. 호저 몸 가운데(발에서 0.41칸 위)에서 나간다.")]
    [SerializeField] private float heightAboveGround = 0.41f;

    private SpriteRenderer spriteRenderer;
    private Transform player;
    private Vector2 velocity;
    private float speed;
    private float range;
    private int damage;
    private string sourceName;
    private float frameTimer;
    private int frameIndex;

    public Vector2 Origin { get; private set; }
    public Vector2 Direction { get; private set; }
    /// <summary>지금까지 날아간 거리(칸).</summary>
    public float Traveled { get; private set; }
    public float Range => range;
    public int Damage => damage;

    private void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    public void Launch(Vector2 direction, float launchSpeed, float launchRange, int launchDamage, string launchSourceName)
    {
        Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        speed = Mathf.Max(0f, launchSpeed);
        range = Mathf.Max(0f, launchRange);
        damage = launchDamage;
        sourceName = launchSourceName;
        velocity = Direction * speed;
        Origin = transform.position;
        Traveled = 0f;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        player = playerObj != null ? playerObj.transform : null;

        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg);
        UpdateSorting();
    }

    private void Update()
    {
        Vector2 from = transform.position;
        float step = Mathf.Min(speed * Time.deltaTime, range - Traveled);
        Vector2 to = from + Direction * step;

        // 벽이 먼저 있으면 구간을 벽까지로 줄인다 - 벽 뒤의 플레이어는 맞지 않는다.
        bool wall = StaticBlockerLine.TryGetFirstHit(from, to, wallMask, out RaycastHit2D wallHit);
        if (wall) to = wallHit.point;

        if (player != null && SegmentDistance(from, to, player.position) <= hitRadius)
        {
            GameManager.Instance?.TakeDamage(damage, sourceName);
            Destroy(gameObject);
            return;
        }

        transform.position = new Vector3(to.x, to.y, transform.position.z);
        Traveled += Vector2.Distance(from, to);

        if (wall || Traveled >= range - 0.0001f)
        {
            Destroy(gameObject);
            return;
        }

        AnimateFrames();
        UpdateSorting();
    }

    private void AnimateFrames()
    {
        if (spriteRenderer == null || frames == null || frames.Length == 0) return;
        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % frames.Length;
        }
        spriteRenderer.sprite = frames[frameIndex];
    }

    private void UpdateSorting()
    {
        if (spriteRenderer != null) spriteRenderer.sortingOrder = FootSortOrder.OrderForFootY(transform.position.y - heightAboveGround);
    }

    private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
        return Vector2.Distance(a + ab * t, p);
    }
}
