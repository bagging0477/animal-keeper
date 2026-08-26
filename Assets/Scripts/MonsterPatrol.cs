using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MonsterPatrol : MonoBehaviour
{
    private enum State { Patrol, Chase }

    [Header("Patrol")]
    [SerializeField] private Vector2[] patrolOffsets =
    {
        new Vector2(-1.5f, 0f),
        new Vector2(0f, 1.5f),
        new Vector2(1.5f, 0f)
    };
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float waypointThreshold = 0.05f;

    [Header("Chase")]
    [SerializeField] private float detectRange = 3f;
    [SerializeField] private float loseRange = 4f;
    [SerializeField] private float chaseSpeed = 3f;

    private Rigidbody2D rb;
    private Transform player;
    private Vector2[] waypoints;
    private int targetIndex;
    private State state = State.Patrol;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        waypoints = new Vector2[patrolOffsets.Length];
        Vector2 origin = rb.position;
        for (int i = 0; i < patrolOffsets.Length; i++)
        {
            waypoints[i] = origin + patrolOffsets[i];
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
    }

    private void FixedUpdate()
    {
        if (player != null)
        {
            float distanceToPlayer = Vector2.Distance(rb.position, player.position);

            if (state == State.Patrol && distanceToPlayer <= detectRange)
            {
                state = State.Chase;
            }
            else if (state == State.Chase && distanceToPlayer > loseRange)
            {
                state = State.Patrol;
            }
        }

        if (state == State.Chase)
        {
            Chase();
        }
        else
        {
            Patrol();
        }
    }

    private void Patrol()
    {
        if (waypoints.Length == 0) return;

        Vector2 target = waypoints[targetIndex];
        Vector2 newPos = Vector2.MoveTowards(rb.position, target, patrolSpeed * Time.fixedDeltaTime);
        rb.MovePosition(newPos);

        if (Vector2.Distance(newPos, target) <= waypointThreshold)
        {
            targetIndex = (targetIndex + 1) % waypoints.Length;
        }
    }

    private void Chase()
    {
        Vector2 newPos = Vector2.MoveTowards(rb.position, player.position, chaseSpeed * Time.fixedDeltaTime);
        rb.MovePosition(newPos);
    }
}
