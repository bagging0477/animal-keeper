using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 코뿔소: 돌진형 몬스터. 값은 GameBalanceConfig "몬스터 - 코뿔소". 장애물을 부수지 않는다.
/// - 배회(Wander): 처음 자리에서 rhinoWanderRadius 안의 지점으로 rhinoWalkSpeed로 걷고(walk 6장), 도착하면 잠시 선다(idle 4장 -
///   SimpleFrameAnimator의 fleeFrames 칸에 idle을 넣고 서 있을 때 IsFleeing을 켠다).
/// - 발견: 몸 가운데에서 rhinoDetectRange 안이고 사이를 벽·장애물이 막지 않으면(MonsterBrain.IsSightBlocked) 차징한다.
/// - 차징(Charge): 제자리에서 플레이어를 보며 charge-01~04(마지막 장 유지). rhinoChargeDuration 뒤 그 순간의 플레이어 방향으로 돌진 방향을 고정한다.
/// - 돌진(Dash): dash-01~04 반복, rhinoDashSpeed로 rhinoDashDistance만큼 일직선. 도중에 방향을 바꾸지 않는다.
///   몸 가운데에서 rhinoDashHitRadius 안에 플레이어가 들어오면 rhinoDashDamage(돌진 한 번에 한 번).
///   몸 가장자리(보이는 몸 상자)가 고정 충돌체에 닿거나 몸 가운데가 NavMesh 가장자리에 닿으면(DashFreeDistance) 그 자리에서 멈추고
///   rhinoWallStunDuration 동안 기절한다 - 몸이 커서 몸 가운데 줄로만 보면 벽에 파묻힌 뒤에야 멈추기 때문이다.
/// - 멈칫(Recover): 다 달리면 recover-01~02(마지막 장 유지)로 rhinoRecoverDuration 동안 서 있다가 다시 배회한다.
/// - 기절·마취(MonsterHealth)는 늑대와 같이 걸리고, 차징·돌진·멈칫 중이어도 걸리면 그 자리에서 멈춘다. 풀리면 배회부터 다시 한다.
/// - 피격·쓰러짐·시체·회수·내려놓기는 MonsterHealth·MonsterCorpsePickup이 다른 몬스터와 같이 한다. 시체 무게는 rhinoCorpseWeight(ICorpseWeight).
/// - 몸 그림 가운데가 피벗보다 bodyCenterTexels만큼 앞(오른쪽)에 있어 좌우로 돌 때 그림자·다트 맞힘 원·몸 가운데를 같이 옮긴다(호저와 같다).
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class RhinoMonsterAI : MonsterBrain, ICorpseWeight
{
    public enum State { Wander, Charge, Dash, Recover }
    public enum DashEnd { None, Distance, Wall, Incapacitated }

    [Header("그림 (RhinoAssetBuilder가 채운다)")]
    [SerializeField] private Transform visual;
    [SerializeField] private Transform body;
    [SerializeField] private Transform shadow;
    [SerializeField] private Sprite[] chargeFrames;
    [SerializeField] private float chargeFrameRate = 7f;
    [SerializeField] private Sprite[] dashFrames;
    [SerializeField] private float dashFrameRate = 12f;
    [SerializeField] private Sprite[] recoverFrames;
    [SerializeField] private float recoverFrameRate = 5f;
    [Tooltip("몸 그림 가운데의 피벗 기준 가로 위치(텍셀, 오른쪽을 볼 때). 왼쪽을 보면 반대로 옮긴다.")]
    [SerializeField] private float bodyCenterTexels;
    [Tooltip("몸 그림 가운데의 피벗 기준 세로 위치(텍셀).")]
    [SerializeField] private float bodyCenterYTexels;
    [Tooltip("몸 그림 1텍셀의 월드 크기(칸) = 1 / PPU x Visual 배율.")]
    [SerializeField] private float texelWorldSize = 1f / 16f;
    [Tooltip("보이는 몸(idle)의 반폭·반높이(텍셀). 돌진 벽 판정에 쓰는 몸 상자 = 몸 가운데 ± 이 값.")]
    [SerializeField] private Vector2 bodyHalfExtentsTexels = new Vector2(14.5f, 9f);

    [Header("기타")]
    [SerializeField] private string monsterTypeName = "코뿔소";
    [SerializeField] private GameBalanceConfig config;
    [Tooltip("시야를 막는 충돌체의 레이어(늑대와 같다). 돌진이 부딪히는 벽도 같은 레이어로 본다.")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private State state = State.Wander;
    private float stateTimer;
    private Vector3 origin;
    private bool wanderWaiting;
    private float wanderWaitTimer;
    private float shadowBaseY;
    private Sprite[] heldChargeFrame, heldRecoverFrame;
    private bool hitThisDash;
    private bool wasIncapacitated;

    private const float TimeEpsilon = 0.0001f;

    public State CurrentState => state;
    public MonsterHealth Health => health;
    public float ChargeStartTime { get; private set; } = -1f;
    public float DashStartTime { get; private set; } = -1f;
    public float DashEndTime { get; private set; } = -1f;
    public float RecoverStartTime { get; private set; } = -1f;
    public Vector2 DashStartPosition { get; private set; }
    public Vector2 DashDirection { get; private set; } = Vector2.right;
    public float DashTraveled { get; private set; }
    public DashEnd LastDashEnd { get; private set; }
    public int DashesStarted { get; private set; }
    public int DashHits { get; private set; }
    public bool FacingLeft => visualRenderer != null && visualRenderer.flipX;
    /// <summary>몸 그림 가운데(2D 좌표). 발견·돌진 피해의 기준.</summary>
    public Vector2 BodyCenter => GamePosition + new Vector2(BodyCenterOffsetX, bodyCenterYTexels * texelWorldSize);
    public int CorpseWeight => config != null ? config.rhinoCorpseWeight : 25;
    /// <summary>돌진 벽 판정에 쓰는 몸 상자의 반폭·반높이(칸).</summary>
    public Vector2 BodyHalfExtents => bodyHalfExtentsTexels * texelWorldSize;

    private float BodyCenterOffsetX => (FacingLeft ? -bodyCenterTexels : bodyCenterTexels) * texelWorldSize;

    /// <summary>땅 위치 ground에서 바라보는 쪽(facingLeft)일 때 몸 상자 가운데.</summary>
    public Vector2 BodyCenterAt(Vector2 ground, bool facingLeft) =>
        ground + new Vector2((facingLeft ? -bodyCenterTexels : bodyCenterTexels) * texelWorldSize, bodyCenterYTexels * texelWorldSize);

    private const float WallSkin = 0.02f;
    private static readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>();
    private static readonly List<Collider2D> startOverlaps = new List<Collider2D>();

    /// <summary>땅 위치 ground에서 dir로 최대 max칸 돌진할 때, 몸 가장자리가 벽에 닿기 전까지 갈 수 있는 거리.
    /// 몸 상자(BodyCenterAt ± BodyHalfExtents)를 고정 충돌체(장애물·벽·트럭 - 트리거와 움직이는 리지드바디는 빼고, 다트·시야와 같은 규칙)에 대고 밀어 본다.
    /// 시작할 때 이미 겹친 벽은 그쪽으로 다가갈 때만 막힌 것으로 본다(벽에 붙어 서 있다가 반대쪽으로 돌진할 수 있게). 몸 가운데가 NavMesh 밖으로 나가는 것도 막는다.
    /// blocked = 벽에 닿아 max보다 덜 가는지.</summary>
    public float DashFreeDistance(Vector2 ground, Vector2 dir, float max, out bool blocked)
    {
        blocked = false;
        bool facingLeft = Mathf.Abs(dir.x) > 0.001f ? dir.x < 0f : FacingLeft;
        Vector2 center = BodyCenterAt(ground, facingLeft);
        Vector2 size = BodyHalfExtents * 2f;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(sightBlockingMask);

        startOverlaps.Clear();
        Physics2D.OverlapBox(center, size, 0f, filter, startOverlaps);
        for (int i = startOverlaps.Count - 1; i >= 0; i--)
        {
            Collider2D c = startOverlaps[i];
            if (c.attachedRigidbody != null && c.attachedRigidbody.bodyType != RigidbodyType2D.Static) { startOverlaps.RemoveAt(i); continue; }
            // 겹친 벽이 몸 가운데에서 돌진 방향 쪽에 있으면 바로 막힌다.
            if (Vector2.Dot(c.ClosestPoint(center) - center, dir) > 0.001f) { blocked = true; return 0f; }
        }

        float free = max;
        castHits.Clear();
        int n = Physics2D.BoxCast(center, size, 0f, dir, filter, castHits, max + WallSkin);
        for (int i = 0; i < n; i++)
        {
            RaycastHit2D h = castHits[i];
            if (h.rigidbody != null && h.rigidbody.bodyType != RigidbodyType2D.Static) continue;
            if (startOverlaps.Contains(h.collider)) continue;
            float d = Mathf.Max(0f, h.distance - WallSkin);
            if (d < free) { free = d; blocked = true; }
        }

        if (agent != null && agent.isOnNavMesh)
        {
            Vector3 from3 = new Vector3(ground.x, transform.position.y, ground.y);
            if (NavMesh.Raycast(from3, from3 + new Vector3(dir.x, 0f, dir.y) * free, out NavMeshHit edge, NavMesh.AllAreas))
            {
                free = new Vector2(edge.position.x - ground.x, edge.position.z - ground.y).magnitude;
                blocked = true;
            }
        }
        return free;
    }
    private float WalkSpeed => config != null ? config.rhinoWalkSpeed : 1f;
    private float WanderRadius => config != null ? config.rhinoWanderRadius : 4f;
    private float WanderWaitMin => config != null ? config.rhinoWanderWaitMin : 1.5f;
    private float WanderWaitMax => config != null ? config.rhinoWanderWaitMax : 3f;
    private float DetectRange => config != null ? config.rhinoDetectRange : 6f;
    private float ChargeDuration => config != null ? config.rhinoChargeDuration : 1f;
    private float DashSpeed => config != null ? config.rhinoDashSpeed : 9f;
    private float DashDistance => config != null ? config.rhinoDashDistance : 8f;
    private int DashDamage => config != null ? config.rhinoDashDamage : 60;
    private float DashHitRadius => config != null ? config.rhinoDashHitRadius : 1f;
    private float RecoverDuration => config != null ? config.rhinoRecoverDuration : 1.5f;
    private float WallStunDuration => config != null ? config.rhinoWallStunDuration : 2f;

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
        agent.speed = WalkSpeed;
        origin = transform.position;
        if (shadow != null) shadowBaseY = shadow.localPosition.y;
        if (chargeFrames != null && chargeFrames.Length > 0) heldChargeFrame = new[] { chargeFrames[chargeFrames.Length - 1] };
        if (recoverFrames != null && recoverFrames.Length > 0) heldRecoverFrame = new[] { recoverFrames[recoverFrames.Length - 1] };
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
        else Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        StartWaiting();
        UpdateVisualPosition();
        ApplyFacingOffsets();
    }

    private void Update()
    {
        if (health == null || health.IsDead)
        {
            UpdateVisualPosition();
            ApplyFacingOffsets();
            return;
        }

        // 기절·마취: 하던 차징·돌진·멈칫을 그 자리에서 멈추고, 풀리면 배회부터 다시 한다.
        if (health.IsIncapacitated)
        {
            if (state != State.Wander)
            {
                if (state == State.Dash) EndDash(DashEnd.Incapacitated);
                ToWander();
            }
            wasIncapacitated = true;
            UpdateVisualPosition();
            ApplyFacingOffsets();
            return;
        }
        if (wasIncapacitated)
        {
            wasIncapacitated = false;
            StartWaiting();
        }

        switch (state)
        {
            case State.Wander: UpdateWander(); break;
            case State.Charge: UpdateCharge(); break;
            case State.Dash: UpdateDash(); break;
            case State.Recover: UpdateRecover(); break;
        }

        UpdateVisualPosition();
        if (frameAnimator != null)
        {
            bool walking = state == State.Wander && agent.velocity.sqrMagnitude > MinMovingSpeed * MinMovingSpeed;
            frameAnimator.IsMoving = walking;
            frameAnimator.IsFleeing = state == State.Wander && !walking; // fleeFrames 칸 = idle 4장
        }
        if (state == State.Wander) UpdateFacing();
        ApplyFacingOffsets();
    }

    // ===================== 배회 =====================

    private void UpdateWander()
    {
        if (player != null && CanSeePlayer())
        {
            BeginCharge();
            return;
        }
        if (!agent.isOnNavMesh) return;
        agent.speed = WalkSpeed;
        if (wanderWaiting)
        {
            wanderWaitTimer -= Time.deltaTime;
            if (wanderWaitTimer <= 0f) PickWanderDestination();
        }
        else if (!agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
        {
            StartWaiting();
        }
    }

    private void StartWaiting()
    {
        wanderWaiting = true;
        wanderWaitTimer = Random.Range(WanderWaitMin, WanderWaitMax);
        StopAgent();
    }

    private void PickWanderDestination()
    {
        wanderWaiting = false;
        Vector2 offset = Random.insideUnitCircle * WanderRadius;
        Vector3 point = origin + new Vector3(offset.x, 0f, offset.y);
        if (agent.isOnNavMesh && NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
        else StartWaiting();
    }

    private bool CanSeePlayer()
    {
        Vector2 eye = BodyCenter;
        Vector2 target = PlayerGamePosition;
        return Vector2.Distance(eye, target) <= DetectRange && !IsSightBlocked(eye, target);
    }

    // ===================== 차징 =====================

    private void BeginCharge()
    {
        state = State.Charge;
        stateTimer = 0f;
        ChargeStartTime = Time.time;
        StopAgent();
        FacePlayer();
        frameAnimator?.PlayAndHold(chargeFrames, chargeFrameRate);
    }

    private void UpdateCharge()
    {
        FacePlayer();
        stateTimer += Time.deltaTime;
        if (frameAnimator != null && !frameAnimator.IsHolding && !frameAnimator.IsPlayingHurt)
            frameAnimator.PlayAndHold(heldChargeFrame, chargeFrameRate); // 차징 중에 맞았으면 피격 그림 뒤 다시 차징 자세로
        if (stateTimer >= ChargeDuration - TimeEpsilon) BeginDash();
    }

    private void FacePlayer()
    {
        if (player == null) return;
        Vector2 to = PlayerGamePosition - GamePosition;
        facingFlipper?.SetMoveDirection(to);
    }

    // ===================== 돌진 =====================

    private void BeginDash()
    {
        Vector2 to = PlayerGamePosition - GamePosition;
        DashDirection = to.sqrMagnitude > 0.0001f ? to.normalized : (FacingLeft ? Vector2.left : Vector2.right);
        facingFlipper?.SetMoveDirection(DashDirection);
        state = State.Dash;
        stateTimer = 0f;
        DashStartTime = Time.time;
        DashStartPosition = GamePosition;
        DashTraveled = 0f;
        LastDashEnd = DashEnd.None;
        hitThisDash = false;
        DashesStarted++;
        StopAgent();
        frameAnimator?.PlayLoop(dashFrames, dashFrameRate);
    }

    private void UpdateDash()
    {
        if (frameAnimator != null && !frameAnimator.IsLooping && !frameAnimator.IsPlayingHurt) frameAnimator.PlayLoop(dashFrames, dashFrameRate);

        float step = Mathf.Min(DashSpeed * Time.deltaTime, DashDistance - DashTraveled);
        Vector2 from = GamePosition;

        // 앞길: 몸 가장자리(몸 상자)가 고정 충돌체(장애물·벽·트럭)에 닿거나 몸 가운데가 NavMesh 가장자리에 닿으면 그 자리에서 멈춘다. 장애물은 부수지 않는다.
        float free = DashFreeDistance(from, DashDirection, step, out bool wall);
        Vector2 to = from + DashDirection * free;
        Vector3 from3 = transform.position;
        Vector3 to3 = new Vector3(to.x, from3.y, to.y);

        Vector3 delta = to3 - from3;
        if (agent.isOnNavMesh) agent.Move(delta);
        else transform.position += delta;
        DashTraveled += new Vector2(delta.x, delta.z).magnitude;

        if (!hitThisDash && player != null && Vector2.Distance(BodyCenter, PlayerGamePosition) <= DashHitRadius)
        {
            hitThisDash = true;
            DashHits++;
            GameManager.Instance?.TakeDamage(DashDamage, monsterTypeName);
            if (this == null) return; // 플레이어가 쓰러져 씬이 바뀌었으면 바로 빠져나온다(MonsterBrain.UpdateCatch와 같다)
        }

        if (wall)
        {
            EndDash(DashEnd.Wall);
            ToWander();
            health.Stun(WallStunDuration);
            return;
        }
        if (DashTraveled >= DashDistance - 0.0001f)
        {
            EndDash(DashEnd.Distance);
            BeginRecover();
        }
    }

    private void EndDash(DashEnd reason)
    {
        LastDashEnd = reason;
        DashEndTime = Time.time;
        StopAgent();
        frameAnimator?.ReleaseHold();
    }

    // ===================== 멈칫 =====================

    private void BeginRecover()
    {
        state = State.Recover;
        stateTimer = 0f;
        RecoverStartTime = Time.time;
        StopAgent();
        frameAnimator?.PlayAndHold(recoverFrames, recoverFrameRate);
    }

    private void UpdateRecover()
    {
        stateTimer += Time.deltaTime;
        if (frameAnimator != null && !frameAnimator.IsHolding && !frameAnimator.IsPlayingHurt)
            frameAnimator.PlayAndHold(heldRecoverFrame, recoverFrameRate);
        if (stateTimer >= RecoverDuration - TimeEpsilon)
        {
            ToWander();
            PickWanderDestination();
        }
    }

    private void ToWander()
    {
        state = State.Wander;
        stateTimer = 0f;
        frameAnimator?.ReleaseHold();
        StartWaiting();
    }

    private void StopAgent()
    {
        if (!agent.isOnNavMesh) return;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    // 몸 가운데가 바라보는 쪽으로 치우쳐 있으므로 그림자와 다트 맞힘 원도 같은 쪽으로 옮긴다(호저와 같다).
    private void ApplyFacingOffsets()
    {
        float x = BodyCenterOffsetX;
        if (shadow != null) shadow.localPosition = new Vector3(x / Mathf.Max(0.0001f, visual != null ? visual.lossyScale.x : 1f), shadowBaseY, 0f);
        if (health != null) health.DartHitShift = new Vector2(x, 0f);
    }
}
