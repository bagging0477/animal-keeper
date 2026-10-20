using UnityEngine;

public class TranquilizerDart : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.3f;
    [Tooltip("다트를 막는 충돌체의 레이어. 이 중 트리거와 움직이는 리지드바디는 건너뛰고 고정 충돌체만 벽으로 본다(몬스터 시야와 같은 규칙).")]
    [SerializeField] private LayerMask wallMask = ~0;

    private Vector2 velocity;
    private float sleepDelay;
    private float sleepDuration;
    private float monsterDamage;
    private float lifeTimer;

    /// <summary>range(사거리)와 speed로부터 남은 비행 시간을 역산해서, GameBalanceConfig의
    /// tranquilizerRange 하나만 바꿔도 실제로 다트가 도달하는 거리가 그만큼 늘어나게 한다.
    /// damage는 몬스터에 맞았을 때만 수면과 함께 준다(동물에게는 주지 않는다).</summary>
    public void Launch(Vector2 direction, float speed, float range, float sleepDelaySeconds, float sleepDurationSeconds, float damage)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        velocity = dir * speed;
        sleepDelay = sleepDelaySeconds;
        sleepDuration = sleepDurationSeconds;
        monsterDamage = damage;
        lifeTimer = speed > 0f ? range / speed : 0f;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void Update()
    {
        // 이번 프레임에 지나간 구간 전체를 고정 충돌체(맵 경계, 장애물, 트럭)로 검사한다 - 빠르게 날아도 얇은 벽을 건너뛰지 않고,
        // 총구가 벽 안에 있으면 거리 0에서 걸려 바로 사라진다. 판정 규칙은 몬스터 시야와 같다(StaticBlockerLine).
        Vector2 from = transform.position;
        Vector2 to = from + velocity * Time.deltaTime;
        if (StaticBlockerLine.TryGetFirstHit(from, to, wallMask, out RaycastHit2D wall))
        {
            transform.position = new Vector3(wall.point.x, wall.point.y, transform.position.z);
            Destroy(gameObject);
            return;
        }
        transform.position = new Vector3(to.x, to.y, transform.position.z);

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 position = to;

        for (int i = MonsterHealth.Active.Count - 1; i >= 0; i--)
        {
            MonsterHealth monster = MonsterHealth.Active[i];
            if (monster.IsDead) continue;
            if (TryHit(monster, position))
            {
                monster.TakeDamage(monsterDamage);
                return;
            }
        }

        for (int i = AnimalSleep.Active.Count - 1; i >= 0; i--)
        {
            if (TryHit(AnimalSleep.Active[i], position)) return;
        }
    }

    private bool TryHit(ISleepable target, Vector2 dartPosition)
    {
        if (Vector2.Distance(dartPosition, target.GamePosition) > hitRadius) return false;
        // 맞힘 반경(hitRadius) 안이어도 다트와 대상 사이에 벽이 있으면(얇은 벽 바로 뒤) 맞지 않는다.
        if (StaticBlockerLine.IsBlocked(dartPosition, target.GamePosition, wallMask)) return false;

        target.PutToSleep(sleepDelay, sleepDuration);
        Destroy(gameObject);
        return true;
    }
}
