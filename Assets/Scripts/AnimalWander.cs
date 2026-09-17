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

    private AnimalRescue rescue;
    private AnimalSleep sleep;
    private Rigidbody2D rb;
    private Vector2 origin;
    private Vector2 destination;
    private Vector2 moveDirection;
    private float waitTimer;
    private bool waiting;

    private void Awake()
    {
        rescue = GetComponent<AnimalRescue>();
        sleep = GetComponent<AnimalSleep>();
        rb = GetComponent<Rigidbody2D>();
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

        if (rescue.IsHeld || rescue.IsCompleted) return;
        if (sleep != null && sleep.IsAsleep) return;

        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewDestination();
            return;
        }

        Vector2 toDestination = destination - rb.position;
        if (toDestination.magnitude <= stopDistance)
        {
            waiting = true;
            waitTimer = Random.Range(waitMin, waitMax);
            return;
        }

        moveDirection = toDestination.normalized;
    }

    private void FixedUpdate()
    {
        if (moveDirection == Vector2.zero) return;
        rb.MovePosition(rb.position + moveDirection * wanderSpeed * Time.fixedDeltaTime);
    }

    private void PickNewDestination()
    {
        waiting = false;
        destination = origin + Random.insideUnitCircle * wanderRadius;
    }
}
