using UnityEngine;

[RequireComponent(typeof(AnimalRescue))]
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
    private AnimalRescue rescue;
    private float alertTimer;

    private void Awake()
    {
        rescue = GetComponent<AnimalRescue>();
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
        if (player == null) return;

        if (rescue.IsHeld || rescue.IsCompleted)
        {
            state = State.Idle;
            return;
        }

        float distance = Vector2.Distance(transform.position, player.position);

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
    }

    private void Flee()
    {
        Vector2 awayFromPlayer = ((Vector2)transform.position - (Vector2)player.position).normalized;
        if (awayFromPlayer == Vector2.zero) awayFromPlayer = Random.insideUnitCircle.normalized;

        Vector2 direction = FindClearDirection(awayFromPlayer);
        if (direction == Vector2.zero) return; // boxed in, stay put this frame

        transform.position += (Vector3)(direction * fleeSpeed * Time.deltaTime);
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

    private bool IsBlocked(Vector2 direction)
    {
        return Physics2D.Raycast(transform.position, direction, wallLookahead, obstacleMask).collider != null;
    }

    protected virtual void OnStartFleeing()
    {
    }
}
