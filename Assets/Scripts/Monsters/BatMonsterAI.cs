using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 박쥐 몬스터(예전 우는천사 자리). 시간 시스템(VillageMonsterSpawner, GameBalanceConfig.villagePhaseSpawns)으로만 나온다 - 맵 생성 스폰 풀(MonsterSpawnPool)에는 없다.
///
/// - 놀람 전(Ambush): 스폰되면 거리 제한 없이 플레이어에게 날아간다. 자기 위치가 플레이어 시야 안에 처음 들어온 순간(PlayerVision.IsVisibleToPlayer)
///   "놀람 완료"로 그 자리에 바로 멈추고 놀람 후로 넘어간다.
/// - 놀람 후: 플레이어가 batChaseRange 안이면 추격(Chase), 밖이면 스폰 자리 주변 배회(Wander). 자기 위치가 플레이어 시야 안이면 멈추고(굳음), 시야 밖일 때만 움직인다.
/// - 맞으면(MonsterHealth.Damaged) 도망(Flee): 그 자리에서 플레이어 반대쪽으로 batFleeDistance(거위 도망 거리 x 0.5)만큼 거위 도망 속도로 난다.
///   도망 중에는 시야 안이어도 멈추지 않고, 접촉도 하지 않는다. 도망 중에 또 맞으면 맞은 자리에서 다시 같은 거리만큼 도망간다. 무적 시간은 없다.
///   도망이 끝나면 다시 플레이어를 찾아 다가오고(Return: 거리 제한 없이 플레이어 쪽으로, 시야 안이면 멈춤) 추격 거리 안에 들어오면 평소대로 돌아간다.
/// - 체력: MonsterHealth(늑대와 같은 공통 최대 체력 - 1, 프리팹 maxHealthOffset -1). 무기 데미지는 늑대와 같다. 기절·마취에는 걸리지 않는다(맞으면 도망가야 하므로).
///   체력 0이면 늑대처럼 쓰러진 시체가 되고 MonsterCorpsePickup으로 회수한다. 쓰러지면 몸이 바닥(그림자 위)으로 내려앉는다.
/// - 접촉: MonsterBrain.UpdateCatch를 그대로 쓴다. 굳은 동안·도망 중에는 접촉하지 않는다. 데미지 = 그 순간 플레이어 최대 체력 x batCatchDamageRatio.
/// - 경고음: 놀람 후 추격하며 움직이는 동안 batWarningInterval마다(클립이 비어 있으면 조용히 건너뜀).
/// - 그림: Visual(땅 위치, 배율 2) 아래 Body(날아다니는 몸, SimpleFrameAnimator·AnimalFacingFlipper)와 Shadow(바닥 그림자)가 따로 있다.
///   비행 = walk/flee 프레임, 굳음 = freezeFrames 정지, 물기 = attack 프레임, 피격·쓰러짐 = hurt/death 프레임.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(MonsterHealth))]
public class BatMonsterAI : MonsterBrain
{
    public enum Phase { Ambush, Chase, Wander, Flee, Return }

    [Header("Visual (2D sprite object this brain drives)")]
    [Tooltip("땅 위치를 따라가는 그림 뿌리(배율 2). 자식으로 Body(몸)와 Shadow(바닥 그림자)를 둔다.")]
    [SerializeField] private Transform visual;
    [Tooltip("날아다니는 몸(SpriteRenderer + SimpleFrameAnimator + AnimalFacingFlipper).")]
    [SerializeField] private Transform body;
    [Tooltip("굳음(쳐다볼 때) 동안 멈춰 보여 줄 프레임.")]
    [SerializeField] private Sprite[] freezeFrames;
    [Tooltip("쓰러지면 몸을 이만큼(몸 텍셀, 아래로) 내려 그림자 위에 눕힌다.")]
    [SerializeField] private float fallenDropTexels = 3f;
    [SerializeField] private float bodyTexelsPerUnit = 32f;

    [Header("Capture")]
    [SerializeField] private string monsterTypeName = "박쥐";
    [SerializeField] private GameBalanceConfig config;

    [Header("경고음")]
    [Tooltip("추격하며 다가오는 동안 반복 재생할 경고음. 비워두면 소리 없이 넘어간다.")]
    [SerializeField] private AudioClip warningClip;
    [SerializeField, Range(0f, 1f)] private float warningVolume = 1f;

    [Header("시야 차단 판정 (공통 코드 연결용)")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private const int FleeDirectionCount = 12; // 플레이어 반대 방향부터 30도씩 좌우로 번갈아 본다(거위와 같은 방식)

    private Vector3 origin;
    private Phase phase = Phase.Ambush;
    private bool frozen;
    private bool holdingFreezePose;
    private bool bodyLowered;
    private Vector3 bodyRestLocalPosition;
    private float wanderWaitTimer;
    private float wanderWaitDuration;
    private float warningTimer;
    private Vector2 fleeStart;
    private float fleeTimer;
    private bool fleeNeedsStart;

    /// <summary>지금 단계(놀람 전 / 추격 / 배회 / 도망 / 다시 찾아옴).</summary>
    public Phase CurrentPhase => phase;
    /// <summary>놀람을 마쳤는지(자기 위치가 처음으로 플레이어 시야에 들어왔는지).</summary>
    public bool IsSurprised => phase != Phase.Ambush;
    /// <summary>이번 프레임에 플레이어 시야 안이라 멈춰 있는지(굳음).</summary>
    public bool IsFrozen => frozen;
    /// <summary>맞아서 도망가는 중인지.</summary>
    public bool IsFleeing => phase == Phase.Flee;
    /// <summary>도망을 시작한 횟수(맞을 때마다 1). 테스트가 읽는다.</summary>
    public int FleeCount { get; private set; }
    /// <summary>마지막 도망을 시작한 자리(2D).</summary>
    public Vector2 FleeStart => fleeStart;
    /// <summary>경고음을 낸 횟수(클립이 비어 있어도 낼 차례가 오면 센다). 테스트가 읽는다.</summary>
    public int WarningCount { get; private set; }
    public MonsterHealth Health => health;

    private float Speed => config != null ? config.batSpeed : 8.4f;
    private float WanderSpeed => config != null ? config.batWanderSpeed : 1.5f;
    private float ChaseRange => config != null ? config.batChaseRange : 15f;
    private float WanderRadius => config != null ? config.batWanderRadius : 5f;
    private float WanderWaitMin => config != null ? config.batWanderWaitMin : 1f;
    private float WanderWaitMax => config != null ? config.batWanderWaitMax : 3f;
    private float WarningInterval => config != null ? config.batWarningInterval : 1.5f;
    /// <summary>도망 속도 = 거위 도망 속도.</summary>
    public float FleeSpeed => config != null ? config.gooseFleeSpeed : 7.17255f;
    /// <summary>도망 거리 = 거위 도망 거리 x batFleeDistanceRatio.</summary>
    public float FleeDistance => config != null ? config.BatFleeDistance : 12f;
    protected override float CatchRange => config != null ? config.batCatchRange : 0.7f;
    private float CatchDamageRatio => config != null ? config.batCatchDamageRatio : 0.8f;
    protected override int CatchDamage => Mathf.RoundToInt(GameManager.MaxHealth * CatchDamageRatio);
    protected override LayerMask SightBlockingMask => sightBlockingMask;
    protected override string MonsterTypeName => monsterTypeName;
    protected override Transform Visual => visual;
    protected override AudioClip ChaseClip => null;
    protected override bool IsChaseState => false;
    protected override bool IsSearchState => false;
    protected override GameBalanceConfig Config => config;
    // 탐색(Search)은 쓰지 않는다.
    protected override void EnterSearchState() { }
    protected override void OnSearchFinished() { }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        if (body == null && visual != null) body = visual.Find("Body");
        facingFlipper = body != null ? body.GetComponent<AnimalFacingFlipper>() : null;
        visualRenderer = body != null ? body.GetComponent<SpriteRenderer>() : null;
        frameAnimator = body != null ? body.GetComponent<SimpleFrameAnimator>() : null;
        if (body != null) bodyRestLocalPosition = body.localPosition;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = Speed;
        agent.autoBraking = false;
        origin = transform.position;
        health.Damaged += OnDamaged;
    }

    private void OnDestroy()
    {
        if (health != null) health.Damaged -= OnDamaged;
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
        else Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
    }

    private void Update()
    {
        if (health.IsDead)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            ReleaseFreezePose();
            LowerBodyOnce();
            UpdateVisualPosition();
            return;
        }

        if (player != null)
        {
            bool visible = PlayerVision.IsVisibleToPlayer(GamePosition);
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            if (phase == Phase.Flee)
            {
                if (fleeNeedsStart) BeginFlee();
                if (ShouldStopFleeing()) EnterReturn();
            }

            if (phase == Phase.Ambush)
            {
                // 놀람 완료: 처음으로 시야에 들어온 순간. 바로 아래 SetFrozen이 그 자리에 세운다.
                if (visible)
                {
                    if (distanceToPlayer <= ChaseRange) EnterChase();
                    else EnterWander();
                }
            }
            else if (phase == Phase.Return)
            {
                if (distanceToPlayer <= ChaseRange) EnterChase();
            }
            else if (phase != Phase.Flee)
            {
                if (distanceToPlayer <= ChaseRange && phase != Phase.Chase) EnterChase();
                else if (distanceToPlayer > ChaseRange && phase != Phase.Wander) EnterWander();
            }

            // 도망 중에는 시야 안이어도 굳지 않는다.
            SetFrozen(phase != Phase.Ambush && phase != Phase.Flee && visible);

            // 굳은 동안·도망 중에는 접촉하지 않는다.
            if (!frozen && phase != Phase.Flee && UpdateCatch(distanceToPlayer)) return;

            if (!frozen && agent.isOnNavMesh) Move();

            UpdateWarning();
        }

        UpdateAnimation();
        UpdateVisualPosition();
        UpdateFacing();
    }

    // ===================== 이동 =====================

    private void Move()
    {
        switch (phase)
        {
            case Phase.Ambush:
            case Phase.Chase:
            case Phase.Return:
                agent.speed = Speed;
                agent.isStopped = false;
                chaseDestinationTimer += Time.deltaTime;
                if (chaseDestinationTimer >= ChaseDirectionUpdateInterval || (!agent.hasPath && !agent.pathPending))
                {
                    chaseDestinationTimer = 0f;
                    if (NavMesh.SamplePosition(PlayerNavPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
                }
                break;

            case Phase.Wander:
                agent.speed = WanderSpeed;
                agent.isStopped = false;
                if (!agent.hasPath && !agent.pathPending) PickNewWanderDestination();
                else if (!agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
                {
                    wanderWaitTimer += Time.deltaTime;
                    if (wanderWaitTimer >= wanderWaitDuration) PickNewWanderDestination();
                }
                break;

            case Phase.Flee:
                agent.speed = FleeSpeed;
                agent.isStopped = false;
                break;
        }
    }

    private void EnterChase()
    {
        phase = Phase.Chase;
        agent.autoBraking = false;
        chaseDestinationTimer = ChaseDirectionUpdateInterval; // 들어온 그 프레임에 바로 목적지를 잡는다
        warningTimer = WarningInterval;                     // 움직이기 시작하면 바로 한 번 울린다
    }

    private void EnterWander()
    {
        phase = Phase.Wander;
        agent.autoBraking = true;
        PickNewWanderDestination();
    }

    // 도망이 끝나면 다시 플레이어를 찾아 다가온다(거리 제한 없음). 추격 거리 안에 들어오면 Update가 Chase로 돌린다.
    private void EnterReturn()
    {
        phase = Phase.Return;
        agent.autoBraking = false;
        chaseDestinationTimer = ChaseDirectionUpdateInterval;
    }

    // 소리반응형과 같은 방식: 스폰 자리 주변에서 NavMesh 위 점을 골라 걸어가고, 도착하면 잠시 기다린다.
    private void PickNewWanderDestination()
    {
        wanderWaitTimer = 0f;
        wanderWaitDuration = Random.Range(WanderWaitMin, WanderWaitMax);
        if (!agent.isOnNavMesh) return;
        agent.speed = WanderSpeed;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * WanderRadius;
            Vector3 point = origin + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, WanderRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }
        agent.SetDestination(origin);
    }

    // 굳으면 길을 지운다(다시 움직일 때 Move가 목적지를 새로 잡는다) - 멈춤이 확실해진다.
    private void SetFrozen(bool value)
    {
        frozen = value;
        if (!agent.isOnNavMesh) return;
        if (value)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            if (agent.hasPath) agent.ResetPath();
            chaseDestinationTimer = ChaseDirectionUpdateInterval;
        }
    }

    // ===================== 도망 =====================

    private void OnDamaged(float amount)
    {
        if (health.IsDead) return;
        // 이번 프레임 Update에서 맞은 자리 기준으로 새로 도망을 시작한다(도망 중에 또 맞아도 같다).
        phase = Phase.Flee;
        fleeNeedsStart = true;
        FleeCount++;
        fleeStart = GamePosition;
        if (agent.isOnNavMesh) BeginFlee();
    }

    private void BeginFlee()
    {
        fleeNeedsStart = false;
        fleeStart = GamePosition;
        fleeTimer = 0f;
        frozen = false;
        agent.autoBraking = true;
        agent.speed = FleeSpeed;
        if (!agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.SetDestination(PickFleeDestination());
    }

    // 플레이어 반대 방향부터 30도씩 좌우로 번갈아 보며, NavMesh 위로 도망 거리만큼 곧게 갈 수 있는 곳을 먼저 고른다.
    // 끝까지 곧게 못 가면 가장 멀리 갈 수 있는 방향의 막힌 곳 바로 앞을 고른다.
    private Vector3 PickFleeDestination()
    {
        Vector3 from = transform.position;
        Vector2 away = player != null ? GamePosition - PlayerGamePosition : Vector2.right;
        if (away.sqrMagnitude < 0.0001f) away = Vector2.right;
        away.Normalize();
        float baseAngle = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg;

        Vector3 best = from;
        float bestReach = -1f;
        for (int i = 0; i < FleeDirectionCount; i++)
        {
            int step = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
            float angle = (baseAngle + step * (360f / FleeDirectionCount)) * Mathf.Deg2Rad;
            Vector3 target = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * FleeDistance;
            if (!NavMesh.Raycast(from, target, out NavMeshHit hit, NavMesh.AllAreas))
            {
                if (NavMesh.SamplePosition(target, out NavMeshHit end, 0.5f, NavMesh.AllAreas)) return end.position;
                return target;
            }
            float reach = Vector3.Distance(from, hit.position);
            if (reach > bestReach) { bestReach = reach; best = hit.position; }
        }
        return best;
    }

    // 도망 거리만큼 곧게 멀어졌거나, 목적지에 닿았거나(막혀서 더 못 감), 너무 오래 걸리면 멈춘다.
    private bool ShouldStopFleeing()
    {
        if (fleeNeedsStart) return false;
        fleeTimer += Time.deltaTime;
        if ((GamePosition - fleeStart).magnitude >= FleeDistance - 0.05f) return true;
        if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= WaypointStopDistance && fleeTimer > 0.1f) return true;
        return fleeTimer >= FleeDistance / Mathf.Max(0.1f, FleeSpeed) * 2f + 1f;
    }

    // ===================== 그림 =====================

    private void UpdateAnimation()
    {
        if (frameAnimator == null) return;
        frameAnimator.IsMoving = true; // 박쥐는 멈춰도 날갯짓을 한다(대기 = 비행 프레임)
        frameAnimator.IsFleeing = phase == Phase.Flee;
        if (frozen && !holdingFreezePose && freezeFrames != null && freezeFrames.Length > 0)
        {
            frameAnimator.PlayAndHold(freezeFrames);
            holdingFreezePose = true;
        }
        else if (!frozen) ReleaseFreezePose();
    }

    private void ReleaseFreezePose()
    {
        if (!holdingFreezePose) return;
        holdingFreezePose = false;
        frameAnimator?.ReleaseHold();
    }

    private void LowerBodyOnce()
    {
        if (bodyLowered || body == null) return;
        bodyLowered = true;
        body.localPosition = bodyRestLocalPosition + new Vector3(0f, -fallenDropTexels / Mathf.Max(1f, bodyTexelsPerUnit), 0f);
    }

    private void UpdateWarning()
    {
        if (phase != Phase.Chase || frozen) return;
        warningTimer += Time.deltaTime;
        if (warningTimer < WarningInterval) return;
        warningTimer -= WarningInterval;
        WarningCount++;
        if (warningClip != null) AudioManager.Instance?.PlaySfx(warningClip, warningVolume);
    }
}
