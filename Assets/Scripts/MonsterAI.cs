using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class MonsterAI : MonoBehaviour
{
    private enum State { Patrol, Chase, Search }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;

    [Header("Patrol")]
    [SerializeField] private Vector2[] patrolOffsets =
    {
        new Vector2(-1.5f, 0f),
        new Vector2(0f, 1.5f),
        new Vector2(1.5f, 0f)
    };
    [SerializeField] private float waypointStopDistance = 0.2f;

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
    private Vector3 lastKnownPlayerPosition;
    private float searchTimer;

    private float PatrolSpeed => config != null ? config.patrolMonsterPatrolSpeed : 1.6f;
    private float ChaseSpeed => config != null ? config.MonsterChaseSpeed : 4.3f;
    private float DetectRange => config != null ? config.patrolMonsterDetectRange : 4.05f;
    private float LoseRange => config != null ? config.patrolMonsterLoseRange : 5.4f;
    private float SearchDuration => config != null ? config.monsterSearchDuration : 3f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = PatrolSpeed;

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

            if (state != State.Chase && distanceToPlayer <= DetectRange)
            {
                state = State.Chase;
                AudioManager.Instance?.PlayMonsterChaseAlert();
            }
            else if (state == State.Chase && distanceToPlayer > LoseRange)
            {
                // 시야에서 놓쳐도 바로 포기하지 않고, 마지막으로 본 위치로 가서 잠시 수색한다.
                state = State.Search;
                searchTimer = 0f;
                lastKnownPlayerPosition = new Vector3(player.position.x, 0f, player.position.y);
                if (agent.isOnNavMesh && NavMesh.SamplePosition(lastKnownPlayerPosition, out NavMeshHit searchHit, 2f, NavMesh.AllAreas))
                {
                    agent.SetDestination(searchHit.position);
                }
            }
            else if (state == State.Search)
            {
                searchTimer += Time.deltaTime;
                if (searchTimer >= SearchDuration)
                {
                    state = State.Patrol;
                    agent.speed = PatrolSpeed;
                    if (waypoints.Length > 0 && agent.isOnNavMesh) agent.SetDestination(waypoints[targetIndex]);
                }
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
            agent.speed = ChaseSpeed;
            Vector3 targetPoint = new Vector3(player.position.x, 0f, player.position.y);
            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
        else if (state == State.Search)
        {
            agent.speed = ChaseSpeed;
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
