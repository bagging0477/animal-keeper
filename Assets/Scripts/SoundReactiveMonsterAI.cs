using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class SoundReactiveMonsterAI : MonoBehaviour
{
    private enum State { Wander, Investigate, Chase, Search }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 5f;
    [SerializeField] private float wanderWaitMin = 3f;
    [SerializeField] private float wanderWaitMax = 6f;

    [Header("Investigate")]
    [SerializeField] private float investigateSpeed = 2f;
    [SerializeField] private float lookAroundDuration = 2f;

    [Header("Capture")]
    [SerializeField] private string monsterTypeName = "소리반응형";
    [SerializeField] private GameBalanceConfig config;

    [Header("Chase 시야 차단 판정")]
    [Tooltip("Chase 중 플레이어와의 사이에 이 레이어의 콜라이더(벽, 장애물)가 있으면 시야가 막힌 것으로 취급한다.")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    private NavMeshAgent agent;
    private MonsterHealth health;
    private Transform player;
    private Vector3 origin;
    private State state = State.Wander;
    private float wanderWaitTimer;
    private float wanderWaitDuration;
    private float lookAroundTimer;
    private bool caughtLogged;
    private Vector3 lastKnownPlayerPosition;
    private float searchTimer;
    private float sightLostTimer;
    private float chaseDestinationTimer;

    private float WanderSpeed => config != null ? config.soundReactiveMonsterWanderSpeed : 2.4f;
    private float ChaseSpeed => config != null ? config.monsterChaseSpeed : 4.3125f;
    private float DetectRange => config != null ? config.soundReactiveMonsterDetectRange : 2.03f;
    private float LoseRange => config != null ? config.soundReactiveMonsterLoseRange : 9f;
    private float SoundHearRange => config != null ? config.soundReactiveMonsterHearRange : 10.4f;
    private float SearchDuration => config != null ? config.monsterSearchDuration : 3f;
    private float ChaseGiveUpSightLostDuration => config != null ? config.chaseGiveUpSightLostDuration : 2f;
    private float ChaseDirectionUpdateInterval => config != null ? config.chaseDirectionUpdateInterval : 0.25f;
    private float CatchRange => config != null ? config.soundReactiveMonsterCatchRange : 0.7f;
    private float WaypointStopDistance => config != null ? config.monsterWaypointStopDistance : 0.2f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = WanderSpeed;
        origin = transform.position;
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

        PickNewWanderDestination();
    }

    private void OnEnable()
    {
        SoundEvents.OnSoundEmitted += HandleSoundEmitted;
    }

    private void OnDisable()
    {
        SoundEvents.OnSoundEmitted -= HandleSoundEmitted;
    }

    private void HandleSoundEmitted(Vector3 soundPosition)
    {
        if (state == State.Chase) return;
        if (health != null && health.IsIncapacitated) return;

        Vector2 soundGamePos = new Vector2(soundPosition.x, soundPosition.y);
        if (Vector2.Distance(GamePosition, soundGamePos) > SoundHearRange) return;

        if (!agent.isOnNavMesh) return;

        Vector3 navTarget = new Vector3(soundPosition.x, 0f, soundPosition.y);
        if (NavMesh.SamplePosition(navTarget, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            state = State.Investigate;
            lookAroundTimer = 0f;
            agent.speed = investigateSpeed;
            agent.SetDestination(hit.position);
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

                sightLostTimer = sightLost ? sightLostTimer + Time.deltaTime : 0f;

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
                        agent.speed = ChaseSpeed;
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
                    ReturnToWander();
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

        if (agent.isOnNavMesh) // not yet placed on a baked NavMesh (e.g. spawned before the map's NavMesh is baked) - skip pathing this frame
        {
            switch (state)
            {
                case State.Chase:
                    if (player != null)
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
                    break;

                case State.Investigate:
                    if (!agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
                    {
                        lookAroundTimer += Time.deltaTime;
                        if (lookAroundTimer >= lookAroundDuration)
                        {
                            ReturnToWander();
                        }
                    }
                    break;

                case State.Wander:
                    if (!agent.pathPending && agent.remainingDistance <= WaypointStopDistance)
                    {
                        wanderWaitTimer += Time.deltaTime;
                        if (wanderWaitTimer >= wanderWaitDuration)
                        {
                            PickNewWanderDestination();
                        }
                    }
                    break;
            }
        }

        if (visual != null)
        {
            visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }
    }

    private void ReturnToWander()
    {
        state = State.Wander;
        PickNewWanderDestination();
    }

    private void PickNewWanderDestination()
    {
        if (!agent.isOnNavMesh) return;

        agent.speed = WanderSpeed;
        wanderWaitTimer = 0f;
        wanderWaitDuration = Random.Range(wanderWaitMin, wanderWaitMax);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 point = origin + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }

        agent.SetDestination(origin);
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

    private bool IsSightBlocked(Vector2 from, Vector2 to)
    {
        if (Physics2D.Linecast(from, to, sightBlockingMask).collider == null) return false;

        Vector2 delta = to - from;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * SightSampleOffset;
        if (Physics2D.Linecast(from + offset, to + offset, sightBlockingMask).collider == null) return false;
        if (Physics2D.Linecast(from - offset, to - offset, sightBlockingMask).collider == null) return false;

        return true;
    }

    private void HandleCatch()
    {
        int damage = config != null ? config.soundReactiveMonsterDamage : 47;
        GameManager.Instance?.TakeDamage(damage, monsterTypeName);
    }
}
