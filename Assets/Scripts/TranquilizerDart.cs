using UnityEngine;

public class TranquilizerDart : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.3f;

    private Vector2 velocity;
    private float sleepDelay;
    private float sleepDuration;
    private float lifeTimer;

    /// <summary>range(사거리)와 speed로부터 남은 비행 시간을 역산해서, GameBalanceConfig의
    /// tranquilizerRange 하나만 바꿔도 실제로 다트가 도달하는 거리가 그만큼 늘어나게 한다.</summary>
    public void Launch(Vector2 direction, float speed, float range, float sleepDelaySeconds, float sleepDurationSeconds)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        velocity = dir * speed;
        sleepDelay = sleepDelaySeconds;
        sleepDuration = sleepDurationSeconds;
        lifeTimer = speed > 0f ? range / speed : 0f;

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
            if (TryHit(monster, position)) return;
        }

        foreach (AnimalSleep animal in FindObjectsByType<AnimalSleep>(FindObjectsInactive.Exclude))
        {
            if (TryHit(animal, position)) return;
        }
    }

    private bool TryHit(ISleepable target, Vector2 dartPosition)
    {
        if (Vector2.Distance(dartPosition, target.GamePosition) > hitRadius) return false;

        target.PutToSleep(sleepDelay, sleepDuration);
        Destroy(gameObject);
        return true;
    }
}
