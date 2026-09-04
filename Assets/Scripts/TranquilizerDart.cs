using UnityEngine;

public class TranquilizerDart : MonoBehaviour
{
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private float hitRadius = 0.3f;

    private Vector2 velocity;
    private float sleepDelay;
    private float sleepDuration;
    private float lifeTimer;

    public void Launch(Vector2 direction, float speed, float sleepDelaySeconds, float sleepDurationSeconds)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        velocity = dir * speed;
        sleepDelay = sleepDelaySeconds;
        sleepDuration = sleepDurationSeconds;
        lifeTimer = lifetime;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 position = transform.position;
        foreach (MonsterHealth monster in FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            if (monster.IsDead) continue;
            if (Vector2.Distance(position, monster.GamePosition) <= hitRadius)
            {
                monster.PutToSleep(sleepDelay, sleepDuration);
                Destroy(gameObject);
                return;
            }
        }
    }
}
