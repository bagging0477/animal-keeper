using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class MonsterAI : MonoBehaviour
{
    private enum State { Patrol, Chase }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;

    [Header("Patrol")]
    [SerializeField] private Vector2[] patrolOffsets =
    {
        new Vector2(-1.5f, 0f),
        new Vector2(0f, 1.5f),
        new Vector2(1.5f, 0f)
    };
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float waypointStopDistance = 0.2f;

    [Header("Chase")]
    [SerializeField] private float detectRange = 3f;
    [SerializeField] private float loseRange = 4f;
    [SerializeField] private float chaseSpeed = 3.5f;

    [Header("Capture")]
    [SerializeField] private float catchRange = 0.7f;
    [SerializeField] private string monsterTypeName = "순찰형";
    [SerializeField] private GameBalanceConfig config;

    private NavMeshAgent agent;
    private MonsterHealth health;
    private Transform player;
    private Vector3[] waypoints;
    private int targetIndex;
    private State state = State.Patrol;
    private bool caughtLogged;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = patrolSpeed;

        waypoints = new Vector3[patrolOffsets.Length];
        Vector3 origin = transform.position;
        for (int i = 0; i < patrolOffsets.Length; i++)
        {
            Vector3 point = origin + new Vector3(patrolOffsets[i].x, 0f, patrolOffsets[i].y);
            waypoints[i] = NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : origin;
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

        if (waypoints.Length > 0 && agent.isOnNavMesh)
        {
            agent.SetDestination(waypoints[targetIndex]);
        }
    }

    private void Update()
    {
        if (health != null && health.IsIncapacitated) return;

        if (player != null)
        {
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            if (state == State.Patrol && distanceToPlayer <= detectRange)
            {
                state = State.Chase;
                AudioManager.Instance?.PlayMonsterChaseAlert();
            }
            else if (state == State.Chase && distanceToPlayer > loseRange)
            {
                state = State.Patrol;
                agent.speed = patrolSpeed;
                if (waypoints.Length > 0 && agent.isOnNavMesh) agent.SetDestination(waypoints[targetIndex]);
            }

            if (distanceToPlayer <= catchRange)
            {
                if (!caughtLogged)
                {
                    caughtLogged = true;
                    HandleCatch();
                }
            }
            else
            {
                caughtLogged = false;
            }
        }

        if (!agent.isOnNavMesh)
        {
            // not yet placed on a baked NavMesh (e.g. spawned before the map's NavMesh is baked) - skip pathing this frame
        }
        else if (state == State.Chase && player != null)
        {
            agent.speed = chaseSpeed;
            Vector3 targetPoint = new Vector3(player.position.x, 0f, player.position.y);
            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
        else if (waypoints.Length > 0 && !agent.pathPending && agent.remainingDistance <= waypointStopDistance)
        {
            targetIndex = (targetIndex + 1) % waypoints.Length;
            agent.SetDestination(waypoints[targetIndex]);
        }

        if (visual != null)
        {
            visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }
    }

    private Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);
    private Vector2 PlayerGamePosition => new Vector2(player.position.x, player.position.y);

    private void HandleCatch()
    {
        int damage = config != null ? config.patrolMonsterDamage : 34;
        GameManager.Instance?.TakeDamage(damage, monsterTypeName);
    }
}
