using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class MonsterAI : MonsterBrain
{
    private enum State { Patrol, Chase, Search }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;

    [Header("Patrol")]
    [SerializeField] private Vector2[] patrolOffsets =
    {
        new Vector2(-1.5f, 0f),
        new Vector2(0f, 1.5f),
        new Vector2(1.5f, 0f)
    };
    [Tooltip("각 순찰 웨이포인트에 도착한 뒤 다음 웨이포인트로 출발하기 전까지 멈춰 서서 대기하는 시간(초) 범위. " +
        "이게 없으면 도착하자마자 바로 다음 지점으로 출발해서 좁은 경로를 쉬지 않고 도는 것처럼 보인다.")]
    [SerializeField] private float patrolWaitMin = 2f;
    [SerializeField] private float patrolWaitMax = 4f;

    [Header("Capture")]
    [SerializeField] private string monsterTypeName = "순찰형";
    [SerializeField] private GameBalanceConfig config;

    [Header("추격음")]
    [Tooltip("이 몬스터가 추격할 때 재생할 추격음. 비워두면 AudioManager의 기본 추격음을 쓴다. 다른 몬스터의 추격음과는 따로 겹쳐서 재생된다.")]
    [SerializeField] private AudioClip chaseClip;

    [Header("Chase 시야 차단 판정")]
    [Tooltip("Chase 중 플레이어와의 사이에 이 레이어의 콜라이더(벽, 장애물)가 있으면 시야가 막힌 것으로 취급한다.")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private Vector3[] waypoints;
    private int targetIndex;
    private State state = State.Patrol;
    private bool patrolWaiting;
    private float patrolWaitTimer;
    private Vector2 homeGamePosition;
    private float chaseBurstTimer;
    private float windedTimer;

    private float PatrolSpeed => config != null ? config.patrolMonsterPatrolSpeed : 1.6f;
    private float DetectRange => config != null ? config.patrolMonsterDetectRange : 4.05f;
    private float LoseRange => config != null ? config.patrolMonsterLoseRange : 10f;
    private float ChaseGiveUpSightLostDuration => config != null ? config.chaseGiveUpSightLostDuration : 2f;
    protected override float CatchRange => config != null ? config.patrolMonsterCatchRange : 0.7f;
    protected override int CatchDamage => config != null ? config.patrolMonsterDamage : 47;
    protected override LayerMask SightBlockingMask => sightBlockingMask;
    protected override string MonsterTypeName => monsterTypeName;
    protected override Transform Visual => visual;
    protected override AudioClip ChaseClip => chaseClip;
    protected override bool IsChaseState => state == State.Chase;
    protected override bool IsSearchState => state == State.Search;
    protected override GameBalanceConfig Config => config;
    protected override void EnterSearchState() => state = State.Search;
    private float LeashRadius => config != null ? config.wolfLeashRadius : 14f;
    private float ChaseBurstDuration => config != null ? config.wolfChaseBurstDuration : 3.5f;
    private float WindedDuration => config != null ? config.wolfWindedDuration : 1.5f;
    private float WindedSpeedMultiplier => config != null ? config.wolfWindedSpeedMultiplier : 0.5f;
    private bool IsWinded => windedTimer > 0f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        frameAnimator = visual != null ? visual.GetComponent<SimpleFrameAnimator>() : null;
        facingFlipper = visual != null ? visual.GetComponent<AnimalFacingFlipper>() : null;
        visualRenderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = PatrolSpeed;

        waypoints = new Vector3[patrolOffsets.Length];
        Vector3 origin = transform.position;
        homeGamePosition = new Vector2(origin.x, origin.z);
        for (int i = 0; i < patrolOffsets.Length; i++)
        {
            Vector3 point = origin + new Vector3(patrolOffsets[i].x, 0f, patrolOffsets[i].y);
            waypoints[i] = NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : origin;
        }
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }

        if (waypoints.Length > 0 && agent.isOnNavMesh)
        {
            agent.SetDestination(waypoints[targetIndex]);
        }
    }

    private void OnDisable()
    {
        AudioManager.Instance?.SetChasing(this, false);
    }

    private void Update()
    {
        if (health != null && health.IsIncapacitated) return;

        if (player != null)
        {
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            // 영역(홈에서 LeashRadius) 밖에 있는 플레이어는 발견하지 않는다 - 영역 경계 바로 밖에 선 플레이어를
            // 집으로 돌아가던 늑대가 다시 발견해 추격과 복귀를 반복하지 않게 한다.
            if (state != State.Chase && distanceToPlayer <= DetectRange && IsPlayerInTerritory)
            {
                state = State.Chase;
                sightLostTimer = 0f;
                chaseBurstTimer = 0f;
                windedTimer = 0f;
                lastSeenPlayerPosition = PlayerNavPosition;
                chaseDestinationTimer = ChaseDirectionUpdateInterval; // 발견한 그 프레임에 바로 추격을 시작한다.
                // autoBraking은 목적지에 부드럽게 도착하려고 미리 감속하는 기능이다. Chase 중에는
                // 목적지가 플레이어 위치로 계속 갱신되는 "움직이는 표적"이라 속도를 늦출 이유가
                // 없는데, 목적지가 하필 문처럼 좁은 통로 한가운데로 갱신되는 순간 "거의 도착"으로
                // 오판해 감속하면서 연결부 근처에서 순간적으로 멈추는 것처럼 보였다. Chase 중에는 꺼둔다.
                agent.autoBraking = false;
            }
            else if (state == State.Chase)
            {
                // 거리가 LoseRange를 넘으면 시야 차단과 달리 깜빡일 일이 없으므로 유예 시간 없이
                // 바로 포기한다 - 추격 속도가 대부분의 플레이어보다 빨라서, "LoseRange 밖에서
                // 2초 더 버티기"까지 요구하면 사실상 영원히 떼어낼 수 없었다.
                if (!IsPlayerInTerritory)
                {
                    // 영역 밖까지 도망친 플레이어는 수색하지 않고 바로 포기한 채 홈으로 돌아간다.
                    ReturnHome();
                }
                else if (distanceToPlayer > LoseRange)
                {
                    EnterSearch(lastSeenPlayerPosition);
                }
                else
                {
                    // 벽/장애물에 가려 시야가 막힌 상태가 이 프레임에도 계속되는지 판정한다.
                    bool sightLost = IsSightBlocked(GamePosition, PlayerGamePosition);

                    // 실제로 보이는 동안의 위치만 기억해둔다 - 포기했을 때 벽 뒤로 숨은 플레이어의
                    // 현재 위치가 아니라 "마지막으로 목격한 곳"으로 가서 찾게 하려는 것이다.
                    if (!sightLost) lastSeenPlayerPosition = PlayerNavPosition;

                    // 놓친 시간은 빠르게 쌓이지만 되찾았을 때는 그 절반 속도로만 줄어들게 해서, 모퉁이를
                    // 도는 순간 단 한 프레임만 다시 보여도 지금까지 쌓은 진행이 통째로 0으로 리셋되는 것을
                    // 막는다 - 몬스터 자신도 같은 모퉁이를 0.25초 뒤처져 따라 돌기 때문에 완전히 끊기지
                    // 않고 아주 잠깐씩 다시 보이는 경우가 흔해서, 즉시 리셋이면 사실상 영원히 못 놓친다.
                    sightLostTimer = sightLost
                        ? sightLostTimer + Time.deltaTime
                        : Mathf.Max(0f, sightLostTimer - Time.deltaTime * 2f);
                }

                if (state == State.Chase && sightLostTimer >= ChaseGiveUpSightLostDuration)
                {
                    // 순간적으로 스친 정도가 아니라 일정 시간 이상 계속 놓쳤을 때만 포기하고,
                    // 마지막으로 본 위치로 가서 잠시 수색한다.
                    EnterSearch(lastSeenPlayerPosition);
                }
            }

            if (UpdateCatch(distanceToPlayer)) return;
        }

        if (!agent.isOnNavMesh)
        {
            // not yet placed on a baked NavMesh (e.g. spawned before the map's NavMesh is baked) - skip pathing this frame
        }
        else if (state == State.Chase && player != null)
        {
            agent.speed = UpdateChaseFatigue();

            // 목적지를 매 프레임 갱신하지 않고 일정 주기로만 갱신해서, 플레이어가 급하게
            // 코너를 꺾었을 때 몬스터가 완벽하게 즉시 따라오지 못하고 살짝 늦게 반응하게 한다.
            chaseDestinationTimer += Time.deltaTime;
            if (chaseDestinationTimer >= ChaseDirectionUpdateInterval)
            {
                chaseDestinationTimer = 0f;
                Vector3 targetPoint = new Vector3(player.position.x, 0f, player.position.y);
                if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
            }
        }
        else if (state == State.Search)
        {
            UpdateSearch();
        }
        else if (waypoints.Length > 0 && !agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
        {
            // 웨이포인트에 도착해도 바로 다음 지점으로 출발하지 않고, 잠시 멈춰 서서 배회하는
            // 느낌을 준다 - Chase/Search 중에는 이 else if 자체가 실행되지 않으므로 대기 중에
            // 플레이어가 나타나면 위쪽 분기에서 바로 Chase로 전환된다.
            if (!patrolWaiting)
            {
                patrolWaiting = true;
                patrolWaitTimer = Random.Range(patrolWaitMin, patrolWaitMax);
            }
            else
            {
                patrolWaitTimer -= Time.deltaTime;
                if (patrolWaitTimer <= 0f)
                {
                    patrolWaiting = false;
                    targetIndex = (targetIndex + 1) % waypoints.Length;
                    agent.SetDestination(waypoints[targetIndex]);
                }
            }
        }

        UpdateVisualPosition();

        if (frameAnimator != null)
        {
            // Chase/Search 중에는 걷기보다 급박한 느낌을 주는 프레임(IsFleeing 슬롯 재사용)을 최우선으로
            // 쓰고, Patrol 중에는 실제로 속도가 나올 때만 Walk, 대기(patrolWaiting) 중이거나 정지해
            // 있으면 Idle로 자연스럽게 떨어진다.
            // 탐색 중 두리번거릴 때는 Idle 프레임 + 좌우 반전으로 "둘러보는" 느낌을 내고, 사이사이 몇 걸음
            // 옮길 때만 Patrol처럼 Walk 프레임을 쓴다.
            // 숨을 고르는 동안에는 급박한 프레임 대신 평소 걷기 프레임을 써서 느려진 것이 눈에 보이게 한다.
            frameAnimator.IsFleeing = (state == State.Chase && !IsWinded) || (state == State.Search && !searchLooking);
            frameAnimator.IsMoving = (state == State.Patrol || (state == State.Chase && IsWinded) || (state == State.Search && searchLooking)) &&
                agent.velocity.sqrMagnitude > MinMovingSpeed * MinMovingSpeed;
        }

        UpdateFacing();
    }

    // 전속력 추격이 ChaseBurstDuration만큼 이어지면 WindedDuration 동안 숨을 고르며 느려진다. 추격 속도가
    // 트래퍼/거너의 스태미나 평균 속도보다 빨라서, 이런 틈이 없으면 첫 스프린트로 LoseRange를 못 넘긴 순간
    // 사실상 영원히 떼어낼 수 없다.
    private float UpdateChaseFatigue()
    {
        if (IsWinded)
        {
            windedTimer -= Time.deltaTime;
            return ChaseSpeed * WindedSpeedMultiplier;
        }

        chaseBurstTimer += Time.deltaTime;
        if (chaseBurstTimer >= ChaseBurstDuration)
        {
            chaseBurstTimer = 0f;
            windedTimer = WindedDuration;
        }
        return ChaseSpeed;
    }

    // 영역 밖으로 나간 플레이어를 포기하고 순찰 경로(홈 주변 웨이포인트)로 걸어서 돌아간다. 도착하면 기존 순찰 대기/순환이
    // 그대로 이어진다.
    private void ReturnHome()
    {
        state = State.Patrol;
        searchLooking = false;
        patrolWaiting = false;
        windedTimer = 0f;
        agent.autoBraking = true;
        agent.speed = PatrolSpeed;
        if (agent.isOnNavMesh && waypoints.Length > 0) agent.SetDestination(waypoints[targetIndex]);
    }

    private bool IsPlayerInTerritory => Vector2.Distance(homeGamePosition, PlayerGamePosition) <= LeashRadius;

    protected override void OnSearchFinished()
    {
        state = State.Patrol;
        patrolWaiting = false;
        agent.speed = PatrolSpeed;
        if (waypoints.Length > 0) agent.SetDestination(waypoints[targetIndex]);
    }
}
