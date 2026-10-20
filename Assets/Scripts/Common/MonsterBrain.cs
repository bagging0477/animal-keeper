using UnityEngine;
using UnityEngine.AI;

/// <summary>몬스터 두뇌(MonsterAI, SoundReactiveMonsterAI)가 똑같이 쓰던 위치 도우미, 시야 판정, 접촉(잡기) 처리를 모은 기반 클래스.
/// Unity 메시지(Awake/Start/Update 등)는 두지 않는다 - 실행 순서는 각 두뇌가 그대로 정한다. 직렬화 필드도 두지 않는다(프리팹 값은 각 두뇌에 남는다);
/// 각 두뇌의 직렬화 값·설정은 아래 연결 속성으로 받는다.</summary>
public abstract class MonsterBrain : MonoBehaviour
{
    // 두 두뇌가 같은 이름으로 들고 있던 런타임 값(직렬화되지 않는다). 각 두뇌의 Awake/Start가 채운다.
    protected SimpleFrameAnimator frameAnimator;
    protected Transform player;
    protected bool caughtLogged;
    protected NavMeshAgent agent;
    protected MonsterHealth health;
    protected AnimalFacingFlipper facingFlipper;
    // 탐색(Search)에 쓰는 값(직렬화되지 않는다). visualRenderer는 각 두뇌의 Awake가 채운다.
    protected SpriteRenderer visualRenderer;
    protected Vector3 lastSeenPlayerPosition;
    protected float searchTimer;
    protected bool searchLooking;
    protected float searchLookDuration;
    protected float searchTurnTimer;
    protected Vector3 searchCenter;

    // 각 두뇌의 직렬화 필드·설정을 기반 코드에 넘기는 연결
    protected abstract LayerMask SightBlockingMask { get; }
    protected abstract float CatchRange { get; }
    protected abstract int CatchDamage { get; }
    protected abstract string MonsterTypeName { get; }
    protected abstract Transform Visual { get; }
    protected abstract AudioClip ChaseClip { get; }
    protected abstract bool IsChaseState { get; }
    protected abstract bool IsSearchState { get; }
    protected abstract bool SearchLooking { get; }
    protected abstract GameBalanceConfig Config { get; }
    protected abstract float ChaseSpeed { get; }
    protected abstract float WaypointStopDistance { get; }
    /// <summary>EnterSearch가 맨 처음에 부른다 - 두뇌의 상태를 Search로 바꾼다.</summary>
    protected abstract void EnterSearchState();
    /// <summary>탐색 두리번거리기가 끝났을 때 UpdateSearch가 부른다(searchLooking을 끈 바로 뒤) - 각 두뇌의 평소 상태로 돌아간다.</summary>
    protected abstract void OnSearchFinished();

    private float SearchLookDurationMin => Config != null ? Config.monsterSearchLookDurationMin : 2f;
    private float SearchLookDurationMax => Config != null ? Config.monsterSearchLookDurationMax : 3f;
    private float SearchLookTurnInterval => Config != null ? Config.monsterSearchLookTurnInterval : 0.6f;
    private float SearchTravelTimeout => Config != null ? Config.monsterSearchTravelTimeout : 5f;
    private float SearchStepChance => Config != null ? Config.monsterSearchStepChance : 0.4f;
    private float SearchStepRadius => Config != null ? Config.monsterSearchStepRadius : 0.8f;
    private float SearchStepSpeed => Config != null ? Config.monsterSearchStepSpeed : 1.2f;

    // Update는 마취로 무력화되면 맨 앞에서 빠져나가 state가 Chase로 남을 수 있어서, 추격음 보고는
    // 무력화 여부까지 반영해 LateUpdate에서 매 프레임 한다.
    private void LateUpdate()
    {
        bool chasing = IsChaseState && (health == null || !health.IsIncapacitated);
        AudioManager.Instance?.SetChasing(this, chasing, ChaseClip);
    }

    /// <summary>NavMesh(x, z) 위의 몸 위치를 2D 그림(Visual)의 (x, y)로 옮긴다. 각 두뇌의 Update 끝, 애니메이션 갱신 앞에서 부른다.</summary>
    protected void UpdateVisualPosition()
    {
        if (Visual != null)
        {
            Visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }
    }

    /// <summary>각 두뇌의 Update 끝, 애니메이션 갱신 뒤에서 부른다.</summary>
    protected void UpdateFacing()
    {
        // 동물(AnimalWander/AnimalFlee)과 동일한 좌우 반전 컴포넌트를 재사용한다 - agent.velocity의
        // x/z가 이 몬스터의 2D 좌표계(x, z)에서 그대로 가로/세로 이동 성분이므로 별도 변환이 필요 없다.
        // 상태와 무관하게 매 프레임 실제 이동 방향을 그대로 넘기면, 좌우 성분이
        // 거의 없을 때(위/아래 이동, 정지) AnimalFacingFlipper가 알아서 마지막 방향을 유지해준다.
        // 두리번거리며 제자리에 서 있을 때는 UpdateSearch가 방향을 직접 정하므로, 몇 걸음 옮기는 중(hasPath)에만 이동 방향을 따른다.
        if (facingFlipper != null && !(IsSearchState && SearchLooking && !agent.hasPath))
        {
            facingFlipper.SetMoveDirection(new Vector2(agent.velocity.x, agent.velocity.z));
        }
    }

    protected Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);
    protected Vector2 PlayerGamePosition => new Vector2(player.position.x, player.position.y);
    protected Vector3 PlayerNavPosition => new Vector3(player.position.x, 0f, player.position.y);

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
    protected bool IsSightBlocked(Vector2 from, Vector2 to)
    {
        int blockedLines = 0;
        if (IsLineBlocked(from, to)) blockedLines++;

        Vector2 delta = to - from;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * SightSampleOffset;
        if (IsLineBlocked(from + offset, to + offset)) blockedLines++;
        if (IsLineBlocked(from - offset, to - offset)) blockedLines++;

        return blockedLines >= 2;
    }

    // 선이 끝나는 지점은 플레이어 한가운데라 플레이어 자신의 콜라이더(1x1 박스)에 항상 걸린다 - 예전에는
    // 이것 때문에 세 줄이 전부 "막힘"으로 판정되어 추격 내내 시야가 가려진 것으로 취급됐고, 마지막
    // 목격 위치도 추격 시작 지점에서 전혀 갱신되지 않았다. 트리거(아이템/포인트/벽 그림자용 상자 등)와
    // 움직이는 리지드바디(플레이어, 동물)는 건너뛰고, 벽처럼 고정된 콜라이더만 시야를 가린 것으로 친다.
    protected bool IsLineBlocked(Vector2 from, Vector2 to)
    {
        return StaticBlockerLine.IsBlocked(from, to, SightBlockingMask);
    }

    /// <summary>각 두뇌의 Update가 플레이어와의 거리를 잰 바로 그 자리에서 부른다(예전 코드가 있던 자리).
    /// true를 돌려주면 잡기 처리 도중 이 몬스터가 파괴된 것이므로 호출한 Update도 바로 빠져나와야 한다.</summary>
    protected bool UpdateCatch(float distanceToPlayer)
    {
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
                if (this == null) return true;
            }
        }
        else
        {
            caughtLogged = false;
        }
        return false;
    }

    private void HandleCatch()
    {
        frameAnimator?.PlayRandomAttack();
        int damage = CatchDamage;
        GameManager.Instance?.TakeDamage(damage, MonsterTypeName);
    }

    // 추격 포기 -> 탐색(Search): 마지막 목격 위치로 이동한 뒤, 도착하면 제자리에서 좌우로 방향을
    // 바꿔가며 잠시 두리번거리고, 그래도 못 찾으면 각 두뇌의 평소 상태로 돌아간다(OnSearchFinished). 탐색 중 플레이어가 DetectRange
    // 안에 다시 들어오면 각 두뇌의 Update 맨 위 발견 분기가 그대로 Chase로 되돌린다.
    protected void EnterSearch(Vector3 searchPoint)
    {
        EnterSearchState();
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

    protected void UpdateSearch()
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
            OnSearchFinished();
        }
    }
}
