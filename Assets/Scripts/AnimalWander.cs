using UnityEngine;

[RequireComponent(typeof(AnimalRescue))]
public class AnimalWander : MonoBehaviour
{
    [SerializeField] private float wanderRadius = 1.5f;
    [SerializeField] private float wanderSpeed = 0.8f;
    [SerializeField] private float waitMin = 1f;
    [SerializeField] private float waitMax = 3f;
    [SerializeField] private float stopDistance = 0.1f;

    private AnimalRescue rescue;
    private AnimalSleep sleep;
    private Vector2 origin;
    private Vector2 destination;
    private float waitTimer;
    private bool waiting;

    private void Awake()
    {
        rescue = GetComponent<AnimalRescue>();
        sleep = GetComponent<AnimalSleep>();
        origin = transform.position;
    }

    private void Start()
    {
        PickNewDestination();
    }

    private void Update()
    {
        if (rescue.IsHeld || rescue.IsCompleted) return;
        if (sleep != null && sleep.IsAsleep) return;

        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) PickNewDestination();
            return;
        }

        Vector2 current = transform.position;
        Vector2 toDestination = destination - current;
        if (toDestination.magnitude <= stopDistance)
        {
            waiting = true;
            waitTimer = Random.Range(waitMin, waitMax);
            return;
        }

        transform.position += (Vector3)(toDestination.normalized * wanderSpeed * Time.deltaTime);
    }

    private void PickNewDestination()
    {
        waiting = false;
        destination = origin + Random.insideUnitCircle * wanderRadius;
    }
}
