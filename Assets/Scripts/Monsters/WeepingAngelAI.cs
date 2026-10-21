using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 우는천사형 몬스터. 시간 시스템(VillageMonsterSpawner, GameBalanceConfig.villagePhaseSpawns)으로만 나온다 - 맵 생성 스폰 풀(MonsterSpawnPool)에는 없다.
/// MonsterHealth가 없어 체력이 없고 죽지 않는다. 무기(포획망·마취 다트·폭탄·지뢰)는 MonsterHealth.Active만 훑으므로 이 몬스터에는 아무 효과가 없다.
///
/// - 놀람 전(Ambush): 스폰되면 거리 제한 없이 플레이어에게 달려간다. 자기 위치가 플레이어 시야 안에 처음 들어온 순간(PlayerVision.IsPointVisible)
///   "놀람 완료"로 그 자리에 바로 멈추고 놀람 후로 넘어간다. 배회하지 않는다.
/// - 놀람 후: 플레이어가 weepingAngelChaseRange 안이면 추격(Chase), 밖이면 스폰 자리 주변 배회(Wander, 소리반응형 배회와 같은 방식).
///   추격이든 배회든 자기 위치가 플레이어 시야 안이면 멈추고, 시야 밖일 때만 움직인다.
/// - 접촉: MonsterBrain.UpdateCatch(기존 접촉 처리와 쿨다운)를 그대로 쓴다. 시야 안에서 멈춰 있는 동안은 접촉 판정을 하지 않는다.
///   데미지는 그 순간 플레이어 최대 체력 x weepingAngelCatchDamageRatio다(체력 0 아래로는 GameManager.TakeDamage가 막고, 0이면 기존대로 트럭으로 강제 복귀).
/// - 경고음: 놀람 후 추격하며 움직이는 동안 weepingAngelWarningInterval마다 AudioManager.PlaySfx(SFX 믹서 그룹)로 재생한다. 배회 중·멈춤 중·놀람 전에는 내지 않는다.
///   클립이 비어 있으면 조용히 건너뛴다. 소리 이벤트(SoundEvents)는 내지 않고, 플레이어 소리나 다른 몬스터에도 반응하지 않는다.
/// - 추격 BGM(AudioManager.SetChasing)은 쓰지 않는다(IsChaseState는 항상 false) - 소리는 경고음만 낸다.
/// - 그림은 정지 이미지(bodySprite)다. 애니메이션은 없고 좌우 반전은 공통 코드(UpdateFacing → AnimalFacingFlipper)를 쓴다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class WeepingAngelAI : MonsterBrain
{
    public enum Phase { Ambush, Chase, Wander }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;
    [Tooltip("몸 그림(정지 이미지). 임시 도형이라 나중에 이 칸만 바꾸면 된다.")]
    [SerializeField] private Sprite bodySprite;

    [Header("Capture")]
    [SerializeField] private string monsterTypeName = "우는천사";
    [SerializeField] private GameBalanceConfig config;

    [Header("경고음")]
    [Tooltip("추격하며 다가오는 동안 반복 재생할 경고음. 비워두면 소리 없이 넘어간다.")]
    [SerializeField] private AudioClip warningClip;
    [SerializeField, Range(0f, 1f)] private float warningVolume = 1f;

    [Header("시야 차단 판정 (공통 코드 연결용)")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private Vector3 origin;
    private Phase phase = Phase.Ambush;
    private bool frozen;
    private float wanderWaitTimer;
    private float wanderWaitDuration;
    private float warningTimer;

    /// <summary>지금 단계(놀람 전 / 놀람 후 추격 / 놀람 후 배회).</summary>
    public Phase CurrentPhase => phase;
    /// <summary>놀람을 마쳤는지(자기 위치가 처음으로 플레이어 시야에 들어왔는지).</summary>
    public bool IsSurprised => phase != Phase.Ambush;
    /// <summary>이번 프레임에 플레이어 시야 안이라 멈춰 있는지.</summary>
    public bool IsFrozen => frozen;
    /// <summary>경고음을 낸 횟수(클립이 비어 있어도 낼 차례가 오면 센다). 테스트가 읽는다.</summary>
    public int WarningCount { get; private set; }

    private float Speed => config != null ? config.weepingAngelSpeed : 8.4f;
    private float WanderSpeed => config != null ? config.weepingAngelWanderSpeed : 1.5f;
    private float ChaseRange => config != null ? config.weepingAngelChaseRange : 15f;
    private float WanderRadius => config != null ? config.weepingAngelWanderRadius : 5f;
    private float WanderWaitMin => config != null ? config.weepingAngelWanderWaitMin : 1f;
    private float WanderWaitMax => config != null ? config.weepingAngelWanderWaitMax : 3f;
    private float WarningInterval => config != null ? config.weepingAngelWarningInterval : 1.5f;
    protected override float CatchRange => config != null ? config.weepingAngelCatchRange : 0.7f;
    // 접촉 순간의 최대 체력(클래스·설정이 바뀌어도 그때 값) x 비율. MonsterBrain.HandleCatch가 잡을 때마다 읽는다.
    private float CatchDamageRatio => config != null ? config.weepingAngelCatchDamageRatio : 0.8f;
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
        facingFlipper = visual != null ? visual.GetComponent<AnimalFacingFlipper>() : null;
        visualRenderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
        if (visualRenderer != null && bodySprite != null) visualRenderer.sprite = bodySprite;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = Speed;
        agent.autoBraking = false;
        origin = transform.position;
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
        else Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
    }

    private void Update()
    {
        if (player != null)
        {
            bool visible = PlayerVision.IsVisibleToPlayer(GamePosition);
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            if (phase == Phase.Ambush)
            {
                // 놀람 완료: 처음으로 시야에 들어온 순간. 바로 아래 SetFrozen이 그 자리에 세운다.
                if (visible)
                {
                    if (distanceToPlayer <= ChaseRange) EnterChase();
                    else EnterWander();
                }
            }
            else if (distanceToPlayer <= ChaseRange && phase != Phase.Chase) EnterChase();
            else if (distanceToPlayer > ChaseRange && phase != Phase.Wander) EnterWander();

            SetFrozen(phase != Phase.Ambush && visible);

            // 시야 안에서 멈춰 있는 동안에는 접촉하지 않는다.
            if (!frozen && UpdateCatch(distanceToPlayer)) return;

            if (!frozen && agent.isOnNavMesh) Move();

            UpdateWarning();
        }

        UpdateVisualPosition();
        UpdateFacing();
    }

    private void Move()
    {
        switch (phase)
        {
            case Phase.Ambush:
            case Phase.Chase:
                agent.speed = Speed;
                chaseDestinationTimer += Time.deltaTime;
                if (chaseDestinationTimer >= ChaseDirectionUpdateInterval || (!agent.hasPath && !agent.pathPending))
                {
                    chaseDestinationTimer = 0f;
                    if (NavMesh.SamplePosition(PlayerNavPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
                }
                break;

            case Phase.Wander:
                agent.speed = WanderSpeed;
                if (!agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
                {
                    wanderWaitTimer += Time.deltaTime;
                    if (wanderWaitTimer >= wanderWaitDuration) PickNewWanderDestination();
                }
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

    // 소리반응형(SoundReactiveMonsterAI.PickNewWanderDestination)과 같은 방식: 스폰 자리 주변에서 NavMesh 위 점을 골라 걸어가고, 도착하면 잠시 기다린다.
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

    private void SetFrozen(bool value)
    {
        frozen = value;
        if (!agent.isOnNavMesh) return;
        agent.isStopped = value;
        if (value) agent.velocity = Vector3.zero;
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
