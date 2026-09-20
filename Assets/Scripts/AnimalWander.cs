using UnityEngine;

[RequireComponent(typeof(AnimalRescue))]
[RequireComponent(typeof(Rigidbody2D))]
public class AnimalWander : MonoBehaviour
{
    [SerializeField] private float wanderRadius = 1.5f;
    [SerializeField] private float wanderSpeed = 0.8f;
    [SerializeField] private float waitMin = 1f;
    [SerializeField] private float waitMax = 3f;
    [SerializeField] private float stopDistance = 0.1f;
    [SerializeField] private float wanderDestinationClearance = 1f;
    [SerializeField] private int wanderDestinationAttempts = 8;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("애니메이션 - 실제로 이 속도 이상 움직이고 있을 때만 Walk 프레임을 재생한다")]
    [SerializeField] private float minMovingSpeed = 0.05f;

    private AnimalSleep sleep;
    private Rigidbody2D rb;
    private SimpleFrameAnimator frameAnimator;
    private AnimalFacingFlipper facingFlipper;
    private Vector2 origin;
    private Vector2 destination;
    private Vector2 moveDirection;
    private float waitTimer;
    private bool waiting;

    // 실제로 움직였는지 판정용. rb.MovePosition()은 Dynamic Rigidbody2D의 velocity를 갱신하지
    // 않으므로(Unity 공식 동작) rb.linearVelocity로는 절대 감지할 수 없다 - 대신 물리 스텝
    // (FixedUpdate) 사이 실제 위치 변화량을 직접 잰다. Update()가 아니라 FixedUpdate에서 재는 이유:
    // Update()는 FixedUpdate의 고정 주기(0.02초)보다 훨씬 자주 도는 경우가 많은데, 그 사이 물리가
    // 실제로 한 번도 안 갱신됐을 수 있어서 Update() 기준으로 재면 "이번 프레임엔 위치가 그대로네 ->
    // 안 움직이는 중"으로 매 프레임 오판해 애니메이션이 프레임 0으로 계속 리셋되며 사실상 멈춰
    // 보였다. FixedUpdate에서 한 번만 재고 그 결과(isActuallyMoving)를 Update()는 값만 읽게 하면
    // 물리 스텝 사이에는 값이 안정적으로 유지된다.
    private Vector2 lastFixedPosition;
    private bool hasLastFixedPosition;
    private bool isActuallyMoving;

    private void Awake()
    {
        sleep = GetComponent<AnimalSleep>();
        rb = GetComponent<Rigidbody2D>();
        frameAnimator = GetComponent<SimpleFrameAnimator>();
        facingFlipper = GetComponent<AnimalFacingFlipper>();
        // 벽 콜라이더 모서리를 스칠 때 마찰로 걸리는 떨림을 없애기 위해 플레이어와 동일하게 무마찰 재질을 쓴다.
        rb.sharedMaterial = new PhysicsMaterial2D("AnimalNoFriction") { friction = 0f, bounciness = 0f };
        origin = transform.position;
    }

    private void Start()
    {
        PickNewDestination();
    }

    private void Update()
    {
        moveDirection = Vector2.zero;

        if (sleep != null && sleep.IsAsleep)
        {
            UpdateAnimator();
            return;
        }

        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewDestination();
            UpdateAnimator();
            return;
        }

        Vector2 toDestination = destination - rb.position;
        if (toDestination.magnitude <= stopDistance)
        {
            waiting = true;
            waitTimer = Random.Range(waitMin, waitMax);
            UpdateAnimator();
            return;
        }

        moveDirection = toDestination.normalized;
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        // isActuallyMoving은 FixedUpdate가 매 물리 스텝마다 갱신해둔 값을 그대로 읽기만 한다.
        if (frameAnimator != null) frameAnimator.IsMoving = isActuallyMoving;
        if (facingFlipper != null) facingFlipper.SetMoveDirection(moveDirection);
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
        rb.MovePosition(rb.position + moveDirection * wanderSpeed * Time.fixedDeltaTime);
    }

    // 벽/장애물에서 wanderDestinationClearance 이상 떨어진 지점만 목적지로 고른다 - 이게 없으면
    // 무작위 지점이 벽 바로 앞이나 벽 너머로 잡혀서, 벽에 딱 붙을 때까지 걸어가 버린다. 몇 번
    // 시도해도 빈 자리를 못 찾으면 제자리(origin)를 목적지로 둔다.
    private void PickNewDestination()
    {
        waiting = false;
        destination = FindClearWanderPoint();
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

    // Physics2D.OverlapCircle도 이 동물 자신의 콜라이더에 맞을 수 있어서 자기 자신은 제외하고 판정한다.
    private bool IsPointBlocked(Vector2 point)
    {
        Collider2D hit = Physics2D.OverlapCircle(point, wanderDestinationClearance, obstacleMask);
        return hit != null && hit.transform != transform;
    }
}
