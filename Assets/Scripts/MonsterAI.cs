using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class MonsterAI : MonoBehaviour
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

    [Header("Chase 시야 차단 판정")]
    [Tooltip("Chase 중 플레이어와의 사이에 이 레이어의 콜라이더(벽, 장애물)가 있으면 시야가 막힌 것으로 취급한다.")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    // 실제로 이 정도 이상 속도로 움직이고 있을 때만 Walk 프레임을 재생한다 - NavMeshAgent의
    // velocity는 애니메이션이 필요로 하는 Rigidbody2D.MovePosition 케이스(AnimalWander 등)와
    // 달리 항상 정확하므로, 동물처럼 FixedUpdate에서 위치 변화량을 직접 잴 필요가 없다.
    private const float MinMovingSpeed = 0.05f;

    private NavMeshAgent agent;
    private MonsterHealth health;
    private SimpleFrameAnimator frameAnimator;
    private AnimalFacingFlipper facingFlipper;
    private Transform player;
    private Vector3[] waypoints;
    private int targetIndex;
    private State state = State.Patrol;
    private bool caughtLogged;
    private Vector3 lastKnownPlayerPosition;
    private float searchTimer;
    private float sightLostTimer;
    private float chaseDestinationTimer;
    private bool patrolWaiting;
    private float patrolWaitTimer;

    private float PatrolSpeed => config != null ? config.patrolMonsterPatrolSpeed : 1.6f;
    private float ChaseSpeed => config != null ? config.monsterChaseSpeed : 4.3125f;
    private float DetectRange => config != null ? config.patrolMonsterDetectRange : 4.05f;
    private float LoseRange => config != null ? config.patrolMonsterLoseRange : 10f;
    private float SearchDuration => config != null ? config.monsterSearchDuration : 3f;
    private float ChaseGiveUpSightLostDuration => config != null ? config.chaseGiveUpSightLostDuration : 2f;
    private float ChaseDirectionUpdateInterval => config != null ? config.chaseDirectionUpdateInterval : 0.25f;
    private float CatchRange => config != null ? config.patrolMonsterCatchRange : 0.7f;
    private float WaypointStopDistance => config != null ? config.monsterWaypointStopDistance : 0.2f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        frameAnimator = visual != null ? visual.GetComponent<SimpleFrameAnimator>() : null;
        facingFlipper = visual != null ? visual.GetComponent<AnimalFacingFlipper>() : null;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = PatrolSpeed;

        waypoints = new Vector3[patrolOffsets.Length];
        Vector3 origin = transform.position;
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

    private void Update()
    {
        if (health != null && health.IsIncapacitated) return;

        if (player != null)
        {
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            if (state != State.Chase && distanceToPlayer <= DetectRange)
            {
                state = State.Chase;
                sightLostTimer = 0f;
                chaseDestinationTimer = ChaseDirectionUpdateInterval; // 발견한 그 프레임에 바로 추격을 시작한다.
                // autoBraking은 목적지에 부드럽게 도착하려고 미리 감속하는 기능이다. Chase 중에는
                // 목적지가 플레이어 위치로 계속 갱신되는 "움직이는 표적"이라 속도를 늦출 이유가
                // 없는데, 목적지가 하필 문처럼 좁은 통로 한가운데로 갱신되는 순간 "거의 도착"으로
                // 오판해 감속하면서 연결부 근처에서 순간적으로 멈추는 것처럼 보였다. Chase 중에는 꺼둔다.
                agent.autoBraking = false;
                AudioManager.Instance?.PlayMonsterChaseAlert();
            }
            else if (state == State.Chase)
            {
                // 거리가 너무 벌어졌거나(LoseRange 초과) 벽/장애물에 가려 시야가 막힌 상태가
                // 이 프레임에도 계속되는지 판정한다. 둘 중 하나라도 '완전히 놓친' 상태로 친다.
                bool blocked = IsSightBlocked(GamePosition, PlayerGamePosition);
                bool sightLost = distanceToPlayer > LoseRange || blocked;

                // 놓친 시간은 빠르게 쌓이지만 되찾았을 때는 그 절반 속도로만 줄어들게 해서, 모퉁이를
                // 도는 순간 단 한 프레임만 다시 보여도 지금까지 쌓은 진행이 통째로 0으로 리셋되는 것을
                // 막는다 - 몬스터 자신도 같은 모퉁이를 0.25초 뒤처져 따라 돌기 때문에 완전히 끊기지
                // 않고 아주 잠깐씩 다시 보이는 경우가 흔해서, 즉시 리셋이면 사실상 영원히 못 놓친다.
                sightLostTimer = sightLost
                    ? sightLostTimer + Time.deltaTime
                    : Mathf.Max(0f, sightLostTimer - Time.deltaTime * 2f);

                if (sightLostTimer >= ChaseGiveUpSightLostDuration)
                {
                    // 순간적으로 스친 정도가 아니라 일정 시간 이상 계속 놓쳤을 때만 포기하고,
                    // 마지막으로 본 위치로 가서 잠시 수색한다.
                    state = State.Search;
                    agent.autoBraking = true; // Search는 고정된 한 지점으로 가서 서는 게 맞으므로 다시 켠다.
                    searchTimer = 0f;
                    lastKnownPlayerPosition = new Vector3(player.position.x, 0f, player.position.y);
                    if (agent.isOnNavMesh)
                    {
                        Vector3 destination = NavMesh.SamplePosition(lastKnownPlayerPosition, out NavMeshHit searchHit, 2f, NavMesh.AllAreas)
                            ? searchHit.position
                            : lastKnownPlayerPosition;
                        agent.SetDestination(destination);
                    }
                }
            }
            else if (state == State.Search)
            {
                searchTimer += Time.deltaTime;
                if (searchTimer >= SearchDuration)
                {
                    state = State.Patrol;
                    patrolWaiting = false;
                    agent.speed = PatrolSpeed;
                    if (waypoints.Length > 0 && agent.isOnNavMesh) agent.SetDestination(waypoints[targetIndex]);
                }
            }

            if (distanceToPlayer <= CatchRange)
            {
                if (!caughtLogged)
                {
                    caughtLogged = true;
                    HandleCatch();

                    // HandleCatch()가 플레이어 체력을 0으로 만들면 GameManager가 동기적으로
                    // SceneManager.LoadScene을 호출해 현재 씬(이 몬스터 포함)을 즉시 파괴한다.
                    // 그 상태로 아래 코드(agent/visual 접근)를 계속 실행하면
                    // MissingReferenceException이 난다 - 파괴됐으면 바로 빠져나온다.
                    if (this == null) return;
                }
            }
            else
            {
                caughtLogged = false;
            }
        }

        if (!agent.isOnNavMesh)
        {
            // not yet placed on a baked NavMesh (e.g. spawned before the map's NavMesh is baked) - skip pathing this frame
        }
        else if (state == State.Chase && player != null)
        {
            agent.speed = ChaseSpeed;

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
            agent.speed = ChaseSpeed;
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

        if (visual != null)
        {
            visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }

        if (frameAnimator != null)
        {
            // Chase/Search 중에는 걷기보다 급박한 느낌을 주는 프레임(IsFleeing 슬롯 재사용)을 최우선으로
            // 쓰고, Patrol 중에는 실제로 속도가 나올 때만 Walk, 대기(patrolWaiting) 중이거나 정지해
            // 있으면 Idle로 자연스럽게 떨어진다.
            frameAnimator.IsFleeing = state == State.Chase || state == State.Search;
            frameAnimator.IsMoving = state == State.Patrol && agent.velocity.sqrMagnitude > MinMovingSpeed * MinMovingSpeed;
        }

        // 동물(AnimalWander/AnimalFlee)과 동일한 좌우 반전 컴포넌트를 재사용한다 - agent.velocity의
        // x/z가 이 몬스터의 2D 좌표계(x, z)에서 그대로 가로/세로 이동 성분이므로 별도 변환이 필요 없다.
        // Patrol/Chase/Search 등 상태와 무관하게 매 프레임 실제 이동 방향을 그대로 넘기면, 좌우 성분이
        // 거의 없을 때(위/아래 이동, 정지) AnimalFacingFlipper가 알아서 마지막 방향을 유지해준다.
        if (facingFlipper != null)
        {
            facingFlipper.SetMoveDirection(new Vector2(agent.velocity.x, agent.velocity.z));
        }
    }

    private Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);
    private Vector2 PlayerGamePosition => new Vector2(player.position.x, player.position.y);

    // 문처럼 폭이 좁은 연결부에서는 중심선 하나만으로 시야 차단을 판정하면, 몬스터나 플레이어가
    // 문 한가운데에서 살짝만 벗어나 있어도 선이 문틀 모서리에 걸려 "완전히 막힌 것"으로 잘못
    // 판정된다. 그 상태가 ChaseGiveUpSightLostDuration만큼 이어지면 문을 넘어가는 도중에 갑자기
    // 추격을 포기하는 것처럼 보인다. 중심선 옆으로 살짝 평행하게 옮긴 보조선 두 개를 더 검사해서,
    // 셋 중 하나라도 뚫려 있으면 아직 보인다고 판정한다 - 같은 방 안에서 장애물 뒤에 완전히
    // 숨는 경우는 장애물이 이 보조선 간격보다 훨씬 크므로 여전히 셋 다 막혀서 스텔스에는 영향이 없다.
    private const float SightSampleOffset = 0.3f;

    // 3줄 중 2줄 이상 막히면 "시야 차단"으로 판정한다. 원래는 3줄 전부를 요구했는데, 추격 중인
    // 몬스터도 플레이어와 거의 같은 타이밍에 같은 모퉁이를 돌기 때문에 세 줄이 동시에 완전히
    // 막히는 순간이 실제로는 드물어서 사실상 영원히 시야를 놓치지 않는 문제가 있었다. 2줄만
    // 요구해도 문틀 모서리에 한 줄만 걸리는 흔한 오판(중심선만 잠깐 스치는 경우)은 여전히
    // 걸러진다.
    private bool IsSightBlocked(Vector2 from, Vector2 to)
    {
        int blockedLines = 0;
        if (Physics2D.Linecast(from, to, sightBlockingMask).collider != null) blockedLines++;

        Vector2 delta = to - from;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * SightSampleOffset;
        if (Physics2D.Linecast(from + offset, to + offset, sightBlockingMask).collider != null) blockedLines++;
        if (Physics2D.Linecast(from - offset, to - offset, sightBlockingMask).collider != null) blockedLines++;

        return blockedLines >= 2;
    }

    private void HandleCatch()
    {
        frameAnimator?.PlayRandomAttack();
        int damage = config != null ? config.patrolMonsterDamage : 47;
        GameManager.Instance?.TakeDamage(damage, monsterTypeName);
    }
}
