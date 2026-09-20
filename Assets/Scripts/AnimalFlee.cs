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

    [Header("놀람 → 도주 감지")]
    [SerializeField] private float alertRange = 3f;
    [SerializeField] private float loseRange = 5f;
    [SerializeField] private float alertDuration = 0.5f;
    [SerializeField] private float wallLookahead = 0.6f;
    [SerializeField] private LayerMask obstacleMask = ~0;

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

        if (spriteAnimator != null)
        {
            spriteAnimator.State = state switch
            {
                State.Alert => AnimalAnimState.Alert,
                State.Fleeing => AnimalAnimState.Moving,
                _ => moveDirection != Vector2.zero ? AnimalAnimState.Moving : AnimalAnimState.Idle
            };
        }

        if (frameAnimator != null)
        {
            frameAnimator.IsFleeing = state == State.Fleeing;
            frameAnimator.IsMoving = state == State.Wander && moveDirection != Vector2.zero;
        }

        if (facingFlipper != null) facingFlipper.SetMoveDirection(moveDirection);
    }

    private void UpdateWander()
    {
        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewWanderDestination();
            return;
        }

        Vector2 toDestination = wanderDestination - rb.position;
        if (toDestination.magnitude <= wanderStopDistance)
        {
            waiting = true;
            waitTimer = Random.Range(waitMin, waitMax);
            return;
        }

        moveDirection = toDestination.normalized;
    }

    private void PickNewWanderDestination()
    {
        waiting = false;
        wanderDestination = origin + Random.insideUnitCircle * wanderRadius;
    }

    private void FixedUpdate()
    {
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
