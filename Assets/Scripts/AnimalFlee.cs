using UnityEngine;

[RequireComponent(typeof(AnimalRescue))]
[RequireComponent(typeof(Rigidbody2D))]
public class AnimalFlee : MonoBehaviour
{
    private enum State { Idle, Alert, Fleeing }

    [SerializeField] private float alertRange = 3f;
    [SerializeField] private float loseRange = 5f;
    [SerializeField] private float alertDuration = 0.5f;
    [SerializeField] private float fleeSpeed = 2.5f;
    [SerializeField] private float wallLookahead = 0.6f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    private State state = State.Idle;
    private Transform player;
    private AnimalSleep sleep;
    private Rigidbody2D rb;
    private SpriteSheetAnimator spriteAnimator;
    private AnimalFacingFlipper facingFlipper;
    private float alertTimer;
    private Vector2 fleeDirection;

    /// <summary>Alert(놀람) 또는 Fleeing(도주) 중이면 true. 씬을 나가기 직전 이 동물의 "긴장 상태"를
    /// 저장했다가 돌아왔을 때 복원하는 데 쓰인다.</summary>
    public bool IsAlarmed => state == State.Alert || state == State.Fleeing;

    /// <summary>씬 재진입 시, 나갈 때 Alert/Fleeing 중이었던 동물을 처음부터 도주 상태로 즉시
    /// 복원한다. Idle부터 다시 플레이어를 감지하게 두면 겁먹고 있던 걸 까먹은 것처럼 보인다.</summary>
    public void RestoreFleeing()
    {
        state = State.Fleeing;
        alertTimer = 0f;
    }

    private void Awake()
    {
        sleep = GetComponent<AnimalSleep>();
        rb = GetComponent<Rigidbody2D>();
        spriteAnimator = GetComponent<SpriteSheetAnimator>();
        facingFlipper = GetComponent<AnimalFacingFlipper>();
        // 벽 콜라이더 모서리를 스칠 때 마찰로 걸리는 떨림을 없애기 위해 플레이어와 동일하게 무마찰 재질을 쓴다.
        rb.sharedMaterial = new PhysicsMaterial2D("AnimalNoFriction") { friction = 0f, bounciness = 0f };
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
    }

    private void Update()
    {
        fleeDirection = Vector2.zero;

        if (player == null) return;
        if (sleep != null && sleep.IsAsleep) return;

        float distance = Vector2.Distance(rb.position, player.position);

        switch (state)
        {
            case State.Idle:
                if (distance <= alertRange)
                {
                    state = State.Alert;
                    alertTimer = alertDuration;
                }
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
                    state = State.Idle;
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
                _ => AnimalAnimState.Idle
            };
        }

        if (facingFlipper != null) facingFlipper.SetMoveDirection(fleeDirection);
    }

    private void FixedUpdate()
    {
        if (fleeDirection == Vector2.zero) return;
        rb.MovePosition(rb.position + fleeDirection * fleeSpeed * Time.fixedDeltaTime);
    }

    private void Flee()
    {
        Vector2 awayFromPlayer = (rb.position - (Vector2)player.position).normalized;
        if (awayFromPlayer == Vector2.zero) awayFromPlayer = Random.insideUnitCircle.normalized;

        fleeDirection = FindClearDirection(awayFromPlayer); // stays zero if boxed in
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
