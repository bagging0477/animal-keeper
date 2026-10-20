using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class SoundReactiveMonsterAI : MonsterBrain
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

    [Header("추격음")]
    [Tooltip("이 몬스터가 추격할 때 재생할 추격음. 비워두면 AudioManager의 기본 추격음을 쓴다. 다른 몬스터의 추격음과는 따로 겹쳐서 재생된다.")]
    [SerializeField] private AudioClip chaseClip;

    [Header("Chase 시야 차단 판정")]
    [Tooltip("Chase 중 플레이어와의 사이에 이 레이어의 콜라이더(벽, 장애물)가 있으면 시야가 막힌 것으로 취급한다.")]
    [SerializeField] private LayerMask sightBlockingMask = ~0;

    // 실제로 이 정도 이상 속도로 움직이고 있을 때만 Walk 프레임을 재생한다 - NavMeshAgent의
    // velocity는 항상 정확하므로, 동물처럼 FixedUpdate에서 위치 변화량을 직접 잴 필요가 없다.
    private const float MinMovingSpeed = 0.05f;

    private SpriteRenderer visualRenderer;
    private Vector3 origin;
    private State state = State.Wander;
    private float wanderWaitTimer;
    private float wanderWaitDuration;
    private float lookAroundTimer;
    private float sightLostTimer;
    private float chaseDestinationTimer;
    private float chaseTimer;
    private Vector3 lastSeenPlayerPosition;
    private float searchTimer;
    private bool searchLooking;
    private float searchLookDuration;
    private float searchTurnTimer;
    private Vector3 searchCenter;
    // 계층 경로 Id(GeneratedMap/Room_{gx}_{gy}/이름_번호). 늑대 시체(MonsterCorpsePickup)와 같은 규칙이라 같은 Day 재진입 때 같은 값이 나온다.
    private string id;

    private float WanderSpeed => config != null ? config.soundReactiveMonsterWanderSpeed : 2.4f;
    private float ChaseSpeed => config != null ? config.monsterChaseSpeed : 4.3125f;
    private float DetectRange => config != null ? config.soundReactiveMonsterDetectRange : 2.03f;
    private float LoseRange => config != null ? config.soundReactiveMonsterLoseRange : 9f;
    private float SoundHearRange => config != null ? config.soundReactiveMonsterHearRange : 15f;
    // 순찰형(MonsterAI)과 공유하는 chaseGiveUpSightLostDuration 대신, blood 몬스터 전용 값을 쓴다 -
    // 순찰형까지 같이 늘어나지 않도록 분리된 필드.
    private float ChaseGiveUpSightLostDuration => config != null ? config.soundReactiveMonsterChaseGiveUpDuration : 3.5f;
    // 0 이하면 무제한 - 시야를 놓쳐야만 추격을 끝낸다.
    private float MaxChaseDuration => config != null ? config.soundReactiveMonsterMaxChaseDuration : 10f;
    // 탐색(Search) 동작 값은 순찰형과 같은 monsterSearch* 필드를 그대로 읽는다.
    private float SearchLookDurationMin => config != null ? config.monsterSearchLookDurationMin : 2f;
    private float SearchLookDurationMax => config != null ? config.monsterSearchLookDurationMax : 3f;
    private float SearchLookTurnInterval => config != null ? config.monsterSearchLookTurnInterval : 0.6f;
    private float SearchTravelTimeout => config != null ? config.monsterSearchTravelTimeout : 5f;
    private float SearchStepChance => config != null ? config.monsterSearchStepChance : 0.4f;
    private float SearchStepRadius => config != null ? config.monsterSearchStepRadius : 0.8f;
    private float SearchStepSpeed => config != null ? config.monsterSearchStepSpeed : 1.2f;
    private float ChaseDirectionUpdateInterval => config != null ? config.chaseDirectionUpdateInterval : 0.25f;
    protected override float CatchRange => config != null ? config.soundReactiveMonsterCatchRange : 0.7f;
    protected override int CatchDamage => config != null ? config.soundReactiveMonsterDamage : 47;
    protected override LayerMask SightBlockingMask => sightBlockingMask;
    protected override string MonsterTypeName => monsterTypeName;
    protected override Transform Visual => visual;
    protected override AudioClip ChaseClip => chaseClip;
    protected override bool IsChaseState => state == State.Chase;
    protected override bool IsSearchState => state == State.Search;
    protected override bool SearchLooking => searchLooking;
    private float WaypointStopDistance => config != null ? config.monsterWaypointStopDistance : 0.2f;
    private float InvestigateApproachFraction => config != null ? config.soundInvestigateApproachFraction : 0.35f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        frameAnimator = visual != null ? visual.GetComponent<SimpleFrameAnimator>() : null;
        facingFlipper = visual != null ? visual.GetComponent<AnimalFacingFlipper>() : null;
        visualRenderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
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

        // 오늘 이미 죽인 소리반응형이면 다시 나타나지 않는다. 늑대 시체와 같은 "오늘 죽은 몬스터" 기록(SaveMonsterCorpse)을 쓴다 -
        // ResetDay/ResetGame에서 비워진다. 난수 호출 순서가 바뀌지 않게 위의 PickNewWanderDestination 뒤에서 확인한다.
        id = BuildHierarchyId();
        if (GameManager.Instance != null && GameManager.Instance.TryGetMonsterCorpse(id, out _))
        {
            gameObject.SetActive(false);
        }
    }

    // 죽은 채로 마을을 떠나면(씬 언로드) 오늘 죽은 몬스터로 기록한다. 살아 있으면 기록하지 않아 재진입 때 새로 생긴다.
    private void OnDestroy()
    {
        // 씬 언로드 중에는 MonsterHealth가 먼저 파괴됐을 수 있어 Unity의 == null 대신 참조만 본다(MonsterCorpsePickup과 같다).
        if (GameManager.Instance == null || ReferenceEquals(health, null) || !health.IsDead || string.IsNullOrEmpty(id)) return;
        GameManager.Instance.SaveMonsterCorpse(id, transform.position);
    }

    private string BuildHierarchyId()
    {
        var path = new System.Text.StringBuilder(name);
        for (Transform t = transform.parent; t != null; t = t.parent)
        {
            path.Insert(0, "/").Insert(0, t.name);
        }
        return path.ToString();
    }

    private void OnEnable()
    {
        SoundEvents.OnSoundEmitted += HandleSoundEmitted;
    }

    private void OnDisable()
    {
        SoundEvents.OnSoundEmitted -= HandleSoundEmitted;
        AudioManager.Instance?.SetChasing(this, false);
    }

    private void HandleSoundEmitted(Vector3 soundPosition, float intensity)
    {
        if (state == State.Chase) return;
        if (health != null && health.IsIncapacitated) return;

        // 감지 범위 자체가 소리 강도에 비례한다 - 강도 1.0(동물 울음소리)은 기존 감지 범위 그대로,
        // 0.3(스프린트 발소리)처럼 낮은 소리는 그만큼 가까이서만 들린다.
        float clampedIntensity = Mathf.Clamp01(intensity);
        float effectiveHearRange = SoundHearRange * clampedIntensity;

        Vector2 soundGamePos = new Vector2(soundPosition.x, soundPosition.y);
        if (Vector2.Distance(GamePosition, soundGamePos) > effectiveHearRange) return;

        if (!agent.isOnNavMesh) return;

        Vector3 navTarget = new Vector3(soundPosition.x, 0f, soundPosition.y);

        // 소리 지점까지 한 번에 이동하지 않고, 현재 위치에서 그 방향으로 강도에 비례한 만큼만 거리를
        // 좁힌다("확인하러 조금 다가가기"). 같은 소리가 반복되면(동물이 계속 울거나 플레이어가 계속
        // 스프린트하면) HandleSoundEmitted가 또 호출되어 매번 이만큼씩 더 다가가는 식으로 누적된다 -
        // Investigate 중이어도(else if로 막지 않음) 새 소리가 오면 lookAroundTimer가 리셋되며 그
        // 다음 단계로 갱신된다.
        float approachFraction = Mathf.Clamp01(InvestigateApproachFraction * clampedIntensity);
        Vector3 stepTarget = Vector3.Lerp(transform.position, navTarget, approachFraction);

        if (NavMesh.SamplePosition(stepTarget, out NavMeshHit hit, 2f, NavMesh.AllAreas))
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
                searchLooking = false;
                sightLostTimer = 0f;
                chaseTimer = 0f;
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
                // 거리가 너무 벌어졌거나(LoseRange 초과) 벽/장애물에 가려 시야가 막힌 상태가
                // 이 프레임에도 계속되는지 판정한다. 둘 중 하나라도 '완전히 놓친' 상태로 친다.
                bool blocked = IsSightBlocked(GamePosition, PlayerGamePosition);
                bool sightLost = distanceToPlayer > LoseRange || blocked;

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

                // 시야가 계속 확보돼도 추격이 MaxChaseDuration을 넘기면 끝낸다(0이면 무제한).
                chaseTimer += Time.deltaTime;
                bool chaseTimedOut = MaxChaseDuration > 0f && chaseTimer >= MaxChaseDuration;

                if (sightLostTimer >= ChaseGiveUpSightLostDuration || chaseTimedOut)
                {
                    // 순간적으로 스친 정도가 아니라 일정 시간 이상 계속 놓쳤거나 최대 추격 시간이 지났을 때만
                    // 추격을 끝내고, 마지막으로 본 위치로 가서 잠시 수색한다.
                    EnterSearch(lastSeenPlayerPosition);
                }
            }

            if (UpdateCatch(distanceToPlayer)) return;
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

                case State.Search:
                    UpdateSearch();
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

        UpdateVisualPosition();

        if (frameAnimator != null)
        {
            // Chase/Investigate는 모두 평소 배회보다 급박하게 움직이는 상태라 걷기보다
            // 우선하는 프레임(IsFleeing 슬롯 재사용)을 쓰고, Wander 중에는 실제로 속도가 나올 때만
            // Walk, 목적지에 도착해 대기 중이거나 정지해 있으면 Idle로 자연스럽게 떨어진다.
            // 탐색 중 두리번거릴 때는 순찰형과 같이 Idle 프레임 + 좌우 반전으로 "둘러보는" 느낌을 내고,
            // 사이사이 몇 걸음 옮길 때만 Walk 프레임을 쓴다.
            frameAnimator.IsFleeing = state == State.Chase || state == State.Investigate || (state == State.Search && !searchLooking);
            frameAnimator.IsMoving = (state == State.Wander || (state == State.Search && searchLooking)) &&
                agent.velocity.sqrMagnitude > MinMovingSpeed * MinMovingSpeed;
        }

        UpdateFacing();
    }

    private void ReturnToWander()
    {
        state = State.Wander;
        PickNewWanderDestination();
    }

    // 추격 종료 -> 탐색(Search): 순찰형과 같은 방식으로 마지막 목격 위치로 이동한 뒤, 도착하면 제자리에서
    // 좌우로 방향을 바꿔가며 잠시 두리번거리고, 그래도 못 찾으면 배회로 돌아간다. 탐색 중 플레이어가
    // DetectRange 안에 다시 들어오면 Update 맨 위의 발견 분기가 그대로 Chase로 되돌린다.
    private void EnterSearch(Vector3 searchPoint)
    {
        state = State.Search;
        agent.autoBraking = true; // Search는 고정된 한 지점으로 가서 서는 게 맞으므로 다시 켠다.
        agent.speed = ChaseSpeed;
        searchTimer = 0f;
        searchLooking = false;
        if (agent.isOnNavMesh)
        {
            Vector3 destination = NavMesh.SamplePosition(searchPoint, out NavMeshHit searchHit, 2f, NavMesh.AllAreas)
                ? searchHit.position
                : searchPoint;
            agent.SetDestination(destination);
        }
    }

    private void UpdateSearch()
    {
        searchTimer += Time.deltaTime;

        if (!searchLooking)
        {
            // 목적지에 도착했거나, 길이 막혀 끝까지 못 가거나, 너무 오래 걸리면 그 자리에서 두리번거리기 시작한다.
            bool arrived = !agent.pathPending && agent.remainingDistance <= WaypointStopDistance;
            bool stuck = !agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete;
            if (!arrived && !stuck && searchTimer < SearchTravelTimeout) return;

            searchLooking = true;
            searchTimer = 0f;
            searchLookDuration = Random.Range(SearchLookDurationMin, SearchLookDurationMax);
            searchTurnTimer = SearchLookTurnInterval;
            searchCenter = transform.position;
            agent.speed = SearchStepSpeed;
            agent.ResetPath();
            return;
        }

        // 제자리에서 좌우로 고개만 돌리면 너무 기계적이라, 방향을 바꿀 차례마다 일정 확률로 도착 지점
        // 주변(searchCenter 기준이라 점점 멀리 벗어나지 않는다)의 가까운 곳으로 몇 걸음 천천히 옮긴다.
        searchTurnTimer -= Time.deltaTime;
        if (searchTurnTimer <= 0f)
        {
            searchTurnTimer += SearchLookTurnInterval;
            Vector2 stepOffset = Random.insideUnitCircle * SearchStepRadius;
            Vector3 stepPoint = searchCenter + new Vector3(stepOffset.x, 0f, stepOffset.y);
            if (Random.value < SearchStepChance &&
                NavMesh.SamplePosition(stepPoint, out NavMeshHit stepHit, SearchStepRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(stepHit.position);
            }
            else
            {
                agent.ResetPath();
                bool turnLeft = visualRenderer != null && !visualRenderer.flipX; // 지금 바라보는 반대쪽으로 돌아본다
                facingFlipper?.SetMoveDirection(turnLeft ? Vector2.left : Vector2.right);
            }
        }

        if (searchTimer >= searchLookDuration)
        {
            searchLooking = false;
            // 배회 지점은 origin 주변에서 고르므로, 그대로 두면 처음 스폰된 곳까지 걸어서 되돌아간다.
            // 탐색을 마친 이 자리를 새 배회 중심으로 삼아 그 근처에서 이어서 배회하게 한다.
            origin = transform.position;
            ReturnToWander();
        }
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
}
