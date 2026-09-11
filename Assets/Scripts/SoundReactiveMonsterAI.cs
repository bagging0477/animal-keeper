using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
public class SoundReactiveMonsterAI : MonoBehaviour
{
    private enum State { Wander, Investigate, Chase }

    [Header("Visual (2D sprite object this brain drives)")]
    [SerializeField] private Transform visual;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 5f;
    [SerializeField] private float wanderSpeed = 1.5f;
    [SerializeField] private float wanderWaitMin = 1f;
    [SerializeField] private float wanderWaitMax = 3f;
    [SerializeField] private float waypointStopDistance = 0.2f;

    [Header("Investigate")]
    [SerializeField] private float soundHearRange = 5f;
    [SerializeField] private float investigateSpeed = 2f;
    [SerializeField] private float lookAroundDuration = 2f;

    [Header("Chase")]
    [SerializeField] private float detectRange = 3f;
    [SerializeField] private float loseRange = 4f;
    [SerializeField] private float chaseSpeed = 3.5f;

    [Header("Capture")]
    [SerializeField] private float catchRange = 0.7f;
    [SerializeField] private string monsterTypeName = "소리반응형";
    [SerializeField] private int damage = 34;

    private NavMeshAgent agent;
    private MonsterHealth health;
    private Transform player;
    private Vector3 origin;
    private State state = State.Wander;
    private float wanderWaitTimer;
    private float wanderWaitDuration;
    private float lookAroundTimer;
    private bool caughtLogged;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<MonsterHealth>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = wanderSpeed;
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

    private void OnEnable()
    {
        SoundEvents.OnSoundEmitted += HandleSoundEmitted;
    }

    private void OnDisable()
    {
        SoundEvents.OnSoundEmitted -= HandleSoundEmitted;
    }

    private void HandleSoundEmitted(Vector3 soundPosition)
    {
        if (state == State.Chase) return;
        if (health != null && health.IsIncapacitated) return;

        Vector2 soundGamePos = new Vector2(soundPosition.x, soundPosition.y);
        if (Vector2.Distance(GamePosition, soundGamePos) > soundHearRange) return;

        if (!agent.isOnNavMesh) return;

        Vector3 navTarget = new Vector3(soundPosition.x, 0f, soundPosition.y);
        if (NavMesh.SamplePosition(navTarget, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            state = State.Investigate;
            lookAroundTimer = 0f;
            agent.speed = investigateSpeed;
            agent.SetDestination(hit.position);
        }
    }

    private void Update()
    {
        if (health != null && health.IsIncapacitated) return;

        if (player != null)
        {
            float distanceToPlayer = Vector2.Distance(GamePosition, PlayerGamePosition);

            if (state != State.Chase && distanceToPlayer <= detectRange)
            {
                state = State.Chase;
                AudioManager.Instance?.PlayMonsterChaseAlert();
            }
            else if (state == State.Chase && distanceToPlayer > loseRange)
            {
                ReturnToWander();
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

        if (agent.isOnNavMesh) // not yet placed on a baked NavMesh (e.g. spawned before the map's NavMesh is baked) - skip pathing this frame
        {
            switch (state)
            {
                case State.Chase:
                    if (player != null)
                    {
                        agent.speed = chaseSpeed;
                        Vector3 targetPoint = new Vector3(player.position.x, 0f, player.position.y);
                        if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                        {
                            agent.SetDestination(hit.position);
                        }
                    }
                    break;

                case State.Investigate:
                    if (!agent.pathPending && agent.remainingDistance <= waypointStopDistance)
                    {
                        lookAroundTimer += Time.deltaTime;
                        if (lookAroundTimer >= lookAroundDuration)
                        {
                            ReturnToWander();
                        }
                    }
                    break;

                case State.Wander:
                    if (!agent.pathPending && agent.remainingDistance <= waypointStopDistance)
                    {
                        wanderWaitTimer += Time.deltaTime;
                        if (wanderWaitTimer >= wanderWaitDuration)
                        {
                            PickNewWanderDestination();
                        }
                    }
                    break;
            }
        }

        if (visual != null)
        {
            visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }
    }

    private void ReturnToWander()
    {
        state = State.Wander;
        PickNewWanderDestination();
    }

    private void PickNewWanderDestination()
    {
        if (!agent.isOnNavMesh) return;

        agent.speed = wanderSpeed;
        wanderWaitTimer = 0f;
        wanderWaitDuration = Random.Range(wanderWaitMin, wanderWaitMax);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 point = origin + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }

        agent.SetDestination(origin);
    }

    private Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);
    private Vector2 PlayerGamePosition => new Vector2(player.position.x, player.position.y);

    private void HandleCatch()
    {
        GameManager.Instance?.TakeDamage(damage, monsterTypeName);
    }
}
