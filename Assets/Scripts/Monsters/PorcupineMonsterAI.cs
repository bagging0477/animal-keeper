using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 호저: 움직이지 않는 터렛형 몬스터. 값은 GameBalanceConfig "몬스터 - 호저".
/// - 대기: idle 4장 반복(SimpleFrameAnimator의 walkFrames 칸에 idle을 넣고 IsMoving을 켜 둔다 - 호저는 걷지 않는다).
/// - 플레이어가 시야에 들어오면(몸 가운데에서 porcupineSightRange 안, 사이를 벽·장애물이 막지 않음 - MonsterBrain.IsSightBlocked)
///   가시를 세운다(raise-01~03, 마지막 장 유지 = 예고). 예고 시작부터 porcupineWindupDuration 뒤에 가시를 쏜다.
///   발사 그림(shoot-01~03)은 가운데 장(shoot-02)이 발사 순간에 오도록 shoot-01을 한 장 먼저 시작한다.
/// - 발사: 그 순간 플레이어 방향을 가운데로 porcupineSpreadAngle 부채꼴에 porcupineQuillCount발을 고르게 쏜다(PorcupineQuill).
/// - 다음 발사는 앞 발사에서 porcupineVolleyInterval 뒤다(예고가 그 안에 들어간다). 예고 중에 플레이어가 시야에서 사라지면 예고를 접는다.
/// - 기절·마취(MonsterHealth)는 늑대와 같이 걸린다. 그동안은 예고·발사를 하지 않고, 하던 예고는 접는다.
/// - 피격·쓰러짐·시체·회수는 MonsterHealth·MonsterCorpsePickup이 늑대와 같이 한다. 시체 무게는 porcupineCorpseWeight(ICorpseWeight).
/// - 접촉 피해는 없다(MonsterBrain.UpdateCatch를 부르지 않는다). 추격음도 없다.
/// - 몸 그림은 가운데가 피벗보다 bodyCenterTexels만큼 앞(오른쪽)에 있어서, 좌우로 돌 때 그림자·다트 맞힘 원·가시 나가는 자리를 같이 옮긴다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class PorcupineMonsterAI : MonsterBrain, ICorpseWeight
{
    public enum Phase { Idle, Windup }

    [Header("그림 (PorcupineAssetBuilder가 채운다)")]
    [SerializeField] private Transform visual;
    [SerializeField] private Transform body;
    [SerializeField] private Transform shadow;
    [SerializeField] private Sprite[] raiseFrames;
    [SerializeField] private float raiseFrameRate = 7f;
    [SerializeField] private Sprite[] shootFrames;
    [SerializeField] private float shootFrameRate = 12f;
    [Tooltip("몸 그림 가운데의 피벗 기준 가로 위치(텍셀, 오른쪽을 볼 때). 왼쪽을 보면 반대로 옮긴다.")]
    [SerializeField] private float bodyCenterTexels = 1.5f;
    [Tooltip("몸 그림 1텍셀의 월드 크기(칸) = 1 / PPU x Visual 배율.")]
    [SerializeField] private float texelWorldSize = 1f / 16f;
    [Tooltip("가시가 나가는 높이 = 땅 위치(GamePosition) 기준(칸). 몸 가운데.")]
    [SerializeField] private float muzzleHeight = -0.34f;

    [Header("가시")]
    [SerializeField] private PorcupineQuill quillPrefab;

    [Header("기타")]
    [SerializeField] private string monsterTypeName = "호저";
    [SerializeField] private GameBalanceConfig config;
    [Tooltip("시야를 막는 충돌체의 레이어(늑대와 같다).")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private Phase phase = Phase.Idle;
    private float phaseTimer;
    private float readyTimer;
    private bool shootStarted;
    private Vector2 aimDirection = Vector2.right;
    private float shadowBaseY;
    private Sprite[] heldRaiseFrame;

    private const float TimeEpsilon = 0.0001f;

    public Phase CurrentPhase => phase;
    public MonsterHealth Health => health;
    /// <summary>쏜 횟수(한 번에 여러 발이어도 1).</summary>
    public int VolleysFired { get; private set; }
    /// <summary>마지막으로 쏜 시각(Time.time). 아직 안 쐈으면 음수.</summary>
    public float LastFireTime { get; private set; } = -1f;
    /// <summary>이번 예고를 시작한 시각(Time.time).</summary>
    public float WindupStartTime { get; private set; } = -1f;
    public Vector2 AimDirection => aimDirection;
    public bool FacingLeft => visualRenderer != null && visualRenderer.flipX;
    /// <summary>가시가 나가는 자리(몸 가운데, 2D 좌표).</summary>
    public Vector2 MuzzlePosition => GamePosition + new Vector2(BodyCenterOffsetX, muzzleHeight);
    public int CorpseWeight => config != null ? config.porcupineCorpseWeight : 12;

    private float BodyCenterOffsetX => (FacingLeft ? -bodyCenterTexels : bodyCenterTexels) * texelWorldSize;
    private float SightRange => config != null ? config.porcupineSightRange : 8f;
    private float WindupDuration => config != null ? config.porcupineWindupDuration : 0.8f;
    private float VolleyInterval => config != null ? config.porcupineVolleyInterval : 2.5f;
    private int QuillCount => config != null ? Mathf.Max(1, config.porcupineQuillCount) : 3;
    private float SpreadAngle => config != null ? config.porcupineSpreadAngle : 30f;
    private int QuillDamage => config != null ? config.porcupineQuillDamage : 20;
    private float QuillRange => config != null ? config.porcupineQuillRange : 8f;
    private float QuillSpeed => config != null ? config.porcupineQuillSpeed : 9f;

    protected override LayerMask SightBlockingMask => sightBlockingMask;
    protected override float CatchRange => 0f;
    protected override int CatchDamage => 0;
    protected override string MonsterTypeName => monsterTypeName;
    protected override Transform Visual => visual;
    protected override AudioClip ChaseClip => null;
    protected override bool IsChaseState => false;
    protected override bool IsSearchState => false;
    protected override GameBalanceConfig Config => config;
    protected override void EnterSearchState() { }
    protected override void OnSearchFinished() { }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        frameAnimator = body != null ? body.GetComponent<SimpleFrameAnimator>() : null;
        facingFlipper = body != null ? body.GetComponent<AnimalFacingFlipper>() : null;
        visualRenderer = body != null ? body.GetComponent<SpriteRenderer>() : null;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = 0f; // 제자리형: 길을 찾지 않고 움직이지 않는다
        if (shadow != null) shadowBaseY = shadow.localPosition.y;
        if (raiseFrames != null && raiseFrames.Length > 0) heldRaiseFrame = new[] { raiseFrames[raiseFrames.Length - 1] };
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
        else Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        UpdateVisualPosition();
        ApplyFacingOffsets();
    }

    private void Update()
    {
        UpdateVisualPosition();
        if (readyTimer > 0f) readyTimer -= Time.deltaTime;
        if (health == null || health.IsDead)
        {
            ApplyFacingOffsets();
            return;
        }
        if (frameAnimator != null) frameAnimator.IsMoving = true; // walkFrames 칸 = idle 4장

        if (health.IsIncapacitated || player == null)
        {
            if (phase != Phase.Idle) CancelWindup();
            ApplyFacingOffsets();
            return;
        }

        bool seen = CanSeePlayer();
        if (phase == Phase.Idle)
        {
            if (seen && readyTimer <= TimeEpsilon) BeginWindup();
        }
        else if (!seen)
        {
            CancelWindup();
        }
        else
        {
            Aim();
            phaseTimer += Time.deltaTime;
            float shootLead = shootFrames != null && shootFrames.Length > 1 ? 1f / Mathf.Max(shootFrameRate, 0.01f) : 0f;
            if (!shootStarted && phaseTimer >= WindupDuration - shootLead - TimeEpsilon)
            {
                shootStarted = true;
                frameAnimator?.PlayOnce(shootFrames, shootFrameRate);
            }
            else if (!shootStarted && frameAnimator != null && !frameAnimator.IsHolding && !frameAnimator.IsPlayingHurt)
            {
                frameAnimator.PlayAndHold(heldRaiseFrame, raiseFrameRate); // 예고 중에 맞았으면 피격 그림 뒤 다시 세운 자세로
            }
            if (phaseTimer >= WindupDuration - TimeEpsilon) Fire();
        }

        ApplyFacingOffsets();
    }

    private bool CanSeePlayer()
    {
        Vector2 eye = MuzzlePosition;
        Vector2 target = PlayerGamePosition;
        return Vector2.Distance(eye, target) <= SightRange && !IsSightBlocked(eye, target);
    }

    private void Aim()
    {
        Vector2 to = PlayerGamePosition - MuzzlePosition;
        if (to.sqrMagnitude > 0.0001f) aimDirection = to.normalized;
        facingFlipper?.SetMoveDirection(aimDirection);
    }

    private void BeginWindup()
    {
        phase = Phase.Windup;
        phaseTimer = 0f;
        shootStarted = false;
        WindupStartTime = Time.time;
        Aim();
        frameAnimator?.PlayAndHold(raiseFrames, raiseFrameRate);
    }

    private void CancelWindup()
    {
        phase = Phase.Idle;
        phaseTimer = 0f;
        shootStarted = false;
        frameAnimator?.ReleaseHold();
    }

    private void Fire()
    {
        Aim();
        phase = Phase.Idle;
        phaseTimer = 0f;
        shootStarted = false;
        VolleysFired++;
        LastFireTime = Time.time;
        readyTimer = Mathf.Max(0f, VolleyInterval - WindupDuration);
        if (quillPrefab == null) return;

        Vector2 origin = MuzzlePosition;
        int count = QuillCount;
        for (int i = 0; i < count; i++)
        {
            float angle = count > 1 ? -SpreadAngle * 0.5f + SpreadAngle * i / (count - 1) : 0f;
            Vector2 dir = Rotate(aimDirection, angle);
            PorcupineQuill quill = Instantiate(quillPrefab, new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            quill.Launch(dir, QuillSpeed, QuillRange, QuillDamage, monsterTypeName);
        }
    }

    // 몸 가운데가 바라보는 쪽으로 치우쳐 있으므로, 그림자와 다트 맞힘 원도 같은 쪽으로 옮긴다(박쥐가 DartHitShift로 몸 높이를 따라가는 것과 같다).
    private void ApplyFacingOffsets()
    {
        float x = BodyCenterOffsetX;
        if (shadow != null) shadow.localPosition = new Vector3(x / Mathf.Max(0.0001f, visual != null ? visual.lossyScale.x : 1f), shadowBaseY, 0f);
        if (health != null) health.DartHitShift = new Vector2(x, 0f);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
