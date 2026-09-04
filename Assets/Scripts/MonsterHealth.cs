using UnityEngine;
using UnityEngine.AI;

public class MonsterHealth : MonoBehaviour, ISleepable
{
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private Color deadColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    private NavMeshAgent agent;
    private SpriteRenderer spriteRenderer;
    private float stunTimer;
    private float asleepTimer;
    private bool sleepPending;
    private float pendingSleepTimer;
    private float pendingSleepDuration;

    public int Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsStunned => stunTimer > 0f;
    public bool IsAsleep => asleepTimer > 0f;
    public bool IsIncapacitated => IsDead || IsStunned || IsAsleep;

    /// <summary>플레이어와 동일한 2D 좌표계로 변환한 몬스터 위치. 몬스터는 NavMesh(x,z 평면)로 움직이고 x,z를 2D x,y로 매핑해서 렌더링한다.</summary>
    public Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        Health = maxHealth;
    }

    private void Update()
    {
        if (IsDead) return;

        if (sleepPending)
        {
            pendingSleepTimer -= Time.deltaTime;
            if (pendingSleepTimer <= 0f)
            {
                sleepPending = false;
                asleepTimer = pendingSleepDuration;
            }
        }

        if (stunTimer > 0f) stunTimer -= Time.deltaTime;
        if (asleepTimer > 0f) asleepTimer -= Time.deltaTime;

        if (agent != null && agent.isOnNavMesh) agent.isStopped = IsIncapacitated;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead || Health <= 0) return;

        Health -= amount;
        if (Health <= 0) Die();
    }

    public void Stun(float duration)
    {
        if (IsDead || IsAsleep) return;
        stunTimer = Mathf.Max(stunTimer, duration);
    }

    public void PutToSleep(float delay, float duration)
    {
        if (IsDead) return;
        sleepPending = true;
        pendingSleepTimer = delay;
        pendingSleepDuration = duration;
    }

    // 죽어도 씬에서 제거하지 않고 색만 바꿔 시체로 남긴다.
    private void Die()
    {
        IsDead = true;
        if (spriteRenderer != null) spriteRenderer.color = deadColor;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }
}
