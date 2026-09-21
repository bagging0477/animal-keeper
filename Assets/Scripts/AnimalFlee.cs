using UnityEngine;

[RequireComponent(typeof(AnimalRescue))]
[RequireComponent(typeof(Rigidbody2D))]
public class AnimalFlee : MonoBehaviour
{
    private enum State { Wander, Alert, Fleeing }

    [Header("밸런스 설정 (비워두면 기본값 사용)")]
    [SerializeField] private GameBalanceConfig config;

    [Header("평소 배회 (Wander) - 얌전한 동물(코알라, AnimalWander)과 같은 패턴")]
    [SerializeField] private float wanderRadius = 1.5f;
    [SerializeField] private float waitMin = 1f;
    [SerializeField] private float waitMax = 3f;
    [SerializeField] private float wanderStopDistance = 0.1f;
    [SerializeField] private float wanderDestinationClearance = 1f;
    [SerializeField] private int wanderDestinationAttempts = 8;
    [SerializeField] private float stuckRepickDelay = 0.4f;

    [Header("놀람 → 도주 감지")]
    [SerializeField] private float alertRange = 3f;
    [SerializeField] private float loseRange = 5f;
    [SerializeField] private float alertDuration = 0.5f;
    [SerializeField] private float wallLookahead = 0.6f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("애니메이션 - 실제로 이 속도 이상 움직이고 있을 때만 Walk/Flee 프레임을 재생한다")]
    [SerializeField] private float minMovingSpeed = 0.05f;

    private float WanderSpeed => config != null ? config.animalWanderSpeed : 1.0f;
    private float FleeSpeed => config != null ? config.animalFleeSpeed : 3.375f;

    private State state = State.Wander;
    private Transform player;
    private AnimalSleep sleep;
    private Rigidbody2D rb;

    // 두 애니메이터 컴포넌트 중 이 동물 프리팹에 실제로 붙어있는 쪽만 null이 아니게 된다 -
    // SpriteSheetAnimator(레서판다처럼 아직 안 옮긴 동물)는 그리드 슬라이싱 기반, SimpleFrameAnimator
    // (고양이처럼 옮긴 동물)는 미리 슬라이스된 Sprite 배열 기반이다. Unity Animator/AnimatorController는
    // 이 프로젝트에서 반복적으로(코알라, 고양이) Write Defaults/상태 전환 문제를 일으켜 쓰지 않는다.
    private SpriteSheetAnimator spriteAnimator;
    private SimpleFrameAnimator frameAnimator;
    private AnimalFacingFlipper facingFlipper;
    private float alertTimer;
    private Vector2 moveDirection;

    // 평소 배회(Wander) 전용 상태 - AnimalWander(코알라)와 동일한 패턴.
    private Vector2 origin;
    private Vector2 wanderDestination;
    private float waitTimer;
    private bool waiting;
    private float stuckTimer;

    // 실제로 움직였는지 판정용. rb.MovePosition()은 Dynamic Rigidbody2D의 velocity를 갱신하지
    // 않으므로(Unity 공식 동작) rb.linearVelocity로는 절대 감지할 수 없다 - 대신 물리 스텝
    // (FixedUpdate) 사이 실제 위치 변화량을 직접 잰다. Update()가 아니라 FixedUpdate에서 재는 이유:
    // Update()는 프레임마다(보통 FixedUpdate의 고정 주기 0.02초보다 훨씬 자주) 도는데, 물리는 그
    // 사이 실제로 한 번도 안 갱신됐을 수 있어서 Update() 기준으로 재면 "이번 프레임엔 위치가 그대로네
    // -> 안 움직이는 중"으로 매 프레임 오판해 애니메이션이 프레임 0으로 계속 리셋되며 사실상 멈춰
    // 보였다. FixedUpdate에서 한 번만 재고 그 결과(isActuallyMoving)를 Update()는 값만 읽게 하면
    // 물리 스텝 사이에는 값이 안정적으로 유지된다.
    private Vector2 lastFixedPosition;
    private bool hasLastFixedPosition;
    private bool isActuallyMoving;

    /// <summary>Alert(놀람) 또는 Fleeing(도주) 중이면 true. 씬을 나가기 직전 이 동물의 "긴장 상태"를
    /// 저장했다가 돌아왔을 때 복원하는 데 쓰인다.</summary>
    public bool IsAlarmed => state == State.Alert || state == State.Fleeing;

    /// <summary>씬 재진입 시, 나갈 때 Alert/Fleeing 중이었던 동물을 처음부터 도주 상태로 즉시
    /// 복원한다. Wander부터 다시 플레이어를 감지하게 두면 겁먹고 있던 걸 까먹은 것처럼 보인다.
    /// frameAnimator에도 바로 신호를 줘서, 이 호출 직후 아직 Update()가 한 번도 안 돈 프레임에도
    /// (예: VillageMapGenerator가 스폰 직후 바로 상태를 확인하는 경우) 이미 도주 프레임으로 보인다.</summary>
    public void RestoreFleeing()
    {
        state = State.Fleeing;
        alertTimer = 0f;
        if (frameAnimator != null) frameAnimator.IsFleeing = true;
    }

    private void Awake()
    {
        sleep = GetComponent<AnimalSleep>();
        rb = GetComponent<Rigidbody2D>();
        spriteAnimator = GetComponent<SpriteSheetAnimator>();
        frameAnimator = GetComponent<SimpleFrameAnimator>();
        facingFlipper = GetComponent<AnimalFacingFlipper>();
        // 벽 콜라이더 모서리를 스칠 때 마찰로 걸리는 떨림을 없애기 위해 플레이어와 동일하게 무마찰 재질을 쓴다.
        rb.sharedMaterial = new PhysicsMaterial2D("AnimalNoFriction") { friction = 0f, bounciness = 0f };
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

    private void Update()
    {
        moveDirection = Vector2.zero;

        if (player == null) return;
        if (sleep != null && sleep.IsAsleep) return;

        float distance = Vector2.Distance(rb.position, player.position);

        switch (state)
        {
            case State.Wander:
                if (distance <= alertRange)
                {
                    state = State.Alert;
                    alertTimer = alertDuration;
                    break;
                }
                UpdateWander();
                break;

            case State.Alert:
                alertTimer -= Time.deltaTime;
                if (alertTimer <= 0f)
                {
                    state = State.Fleeing;
                    OnStartFleeing();
                }
                break;

            case State.Fleeing:
                if (distance > loseRange)
                {
                    state = State.Wander;
                    // 도망친 자리 근처에서 배회를 다시 시작한다 - 원래 스폰 지점까지 먼 길을
                    // 되돌아가게 하면 부자연스럽다.
                    origin = rb.position;
                    PickNewWanderDestination();
                    break;
                }
                Flee();
                break;
        }

        // isActuallyMoving은 FixedUpdate가 매 물리 스텝마다 갱신해둔 값을 그대로 읽기만 한다.
        if (spriteAnimator != null)
        {
            spriteAnimator.State = state switch
            {
                State.Alert => AnimalAnimState.Alert,
                _ => isActuallyMoving ? AnimalAnimState.Moving : AnimalAnimState.Idle
            };
        }

        if (frameAnimator != null)
        {
            frameAnimator.IsFleeing = state == State.Fleeing && isActuallyMoving;
            frameAnimator.IsMoving = state == State.Wander && isActuallyMoving;
        }

        if (facingFlipper != null) facingFlipper.SetMoveDirection(moveDirection);
    }

    private void UpdateWander()
    {
        if (waiting)
        {
            stuckTimer = 0f;
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewWanderDestination();
            return;
        }

        Vector2 toDestination = wanderDestination - rb.position;
        if (toDestination.magnitude <= wanderStopDistance)
        {
            waiting = true;
            waitTimer = Random.Range(waitMin, waitMax);
            stuckTimer = 0f;
            return;
        }

        // 목적지 자체는 벽에서 떨어져 있어도 거기로 가는 직선 경로 중간에 벽이 있으면 그대로
        // 부딪혀 제자리에 갇힐 수 있다 - Fleeing 때 쓰는 것과 같은 레이캐스트 회피를 재사용한다.
        moveDirection = FindClearDirection(toDestination.normalized);

        // FindClearDirection도 못 피하는 오목한 구석 등으로 실제 전진을 못 하는 상태가 일정 시간
        // 지속되면, 그 자리에서 벽을 향해 계속 시도하는 대신 새 목적지를 다시 뽑아 벗어난다.
        if (moveDirection == Vector2.zero || !isActuallyMoving)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckRepickDelay) PickNewWanderDestination();
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    // 벽/장애물에서 wanderDestinationClearance 이상 떨어진 지점만 목적지로 고른다 - 이게 없으면
    // 무작위 지점이 벽 바로 앞이나 벽 너머로 잡혀서, 벽에 딱 붙을 때까지 걸어가 버린다. 몇 번
    // 시도해도 빈 자리를 못 찾으면(방이 좁거나 wanderRadius가 벽에 거의 걸쳐 있는 경우) 제자리
    // (origin)를 목적지로 둬서 최소한 벽 쪽으로 걸어가지는 않게 한다.
    private void PickNewWanderDestination()
    {
        waiting = false;
        stuckTimer = 0f;
        wanderDestination = FindClearWanderPoint();
    }

    private Vector2 FindClearWanderPoint()
    {
        for (int i = 0; i < wanderDestinationAttempts; i++)
        {
            Vector2 candidate = origin + Random.insideUnitCircle * wanderRadius;
            if (!IsPointBlocked(candidate)) return candidate;
        }
        return origin;
    }

    // Physics2D.OverlapCircle도 Raycast와 마찬가지로 이 동물 자신의 콜라이더에 맞을 수 있어서
    // 자기 자신은 제외하고 판정한다.
    private bool IsPointBlocked(Vector2 point)
    {
        Collider2D hit = Physics2D.OverlapCircle(point, wanderDestinationClearance, obstacleMask);
        return hit != null && hit.transform != transform;
    }

    private void FixedUpdate()
    {
        // 지난 물리 스텝에서 실제로 이동한 거리를 재서, 이번 스텝의 애니메이션 판정에 쓴다(한 스텝
        // 지연되지만 물리 주기가 0.02초라 체감상 차이 없다) - MovePosition을 부른 바로 그 줄에서
        // rb.position을 읽으면 아직 충돌 해석 전(요청한 목표 위치)을 돌려줄 수 있어서 믿을 수 없다.
        if (hasLastFixedPosition)
        {
            float measuredSpeed = (rb.position - lastFixedPosition).magnitude / Time.fixedDeltaTime;
            isActuallyMoving = moveDirection != Vector2.zero && measuredSpeed > minMovingSpeed;
        }
        lastFixedPosition = rb.position;
        hasLastFixedPosition = true;

        if (moveDirection == Vector2.zero) return;
        float speed = state == State.Fleeing ? FleeSpeed : WanderSpeed;
        rb.MovePosition(rb.position + moveDirection * speed * Time.fixedDeltaTime);
    }

    private void Flee()
    {
        Vector2 awayFromPlayer = (rb.position - (Vector2)player.position).normalized;
        if (awayFromPlayer == Vector2.zero) awayFromPlayer = Random.insideUnitCircle.normalized;

        moveDirection = FindClearDirection(awayFromPlayer); // stays zero if boxed in
    }

    // Tries the desired direction first, then increasingly wider angles to
    // either side, so a fleeing animal turns along a wall instead of stopping.
    private Vector2 FindClearDirection(Vector2 desired)
    {
        if (!IsBlocked(desired)) return desired;

        for (float angle = 30f; angle <= 150f; angle += 30f)
        {
            Vector2 right = Quaternion.Euler(0f, 0f, -angle) * desired;
            if (!IsBlocked(right)) return right;

            Vector2 left = Quaternion.Euler(0f, 0f, angle) * desired;
            if (!IsBlocked(left)) return left;
        }

        return Vector2.zero;
    }

    // Physics2D.Raycast (queriesStartInColliders 켜짐)는 이 동물 자신의 콜라이더에도 거리 0으로
    // 맞기 때문에, 자기 자신은 제외하고 판정한다.
    private bool IsBlocked(Vector2 direction)
    {
        RaycastHit2D hit = Physics2D.Raycast(rb.position, direction, wallLookahead, obstacleMask);
        return hit.collider != null && hit.transform != transform;
    }

    protected virtual void OnStartFleeing()
    {
    }
}
