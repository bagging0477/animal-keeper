using UnityEngine;
using UnityEngine.AI;

public class MonsterHealth : MonoBehaviour, ISleepable
{
    /// <summary>현재 활성화된 모든 MonsterHealth. 지뢰/폭탄/마취 다트가 매 프레임 FindObjectsByType으로 씬 전체를 훑는 대신
    /// 이 목록만 본다(개체 수가 늘어도 비용이 개체 수에만 비례한다). 순회 중에 대상이 비활성화될 수 있으므로 뒤에서부터 돈다.</summary>
    public static readonly System.Collections.Generic.List<MonsterHealth> Active = new System.Collections.Generic.List<MonsterHealth>();

    // 도메인 리로드를 끈 에디터(Enter Play Mode Options)에서도 이전 플레이의 항목이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveList() => Active.Clear();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Color deadColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    private NavMeshAgent agent;
    private SpriteRenderer spriteRenderer;
    private SimpleFrameAnimator frameAnimator;
    private float stunTimer;
    private float asleepTimer;
    private bool sleepPending;
    private float pendingSleepTimer;
    private float pendingSleepDuration;

    // float인 이유: 트래퍼의 포획망 데미지(netDamage)처럼 1보다 작은 배율로 튜닝된 데미지가
    // 매 히트마다 정수로 반올림되면서 의도한 증가분이 통째로 사라지는 걸 막기 위해서다.
    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsStunned => stunTimer > 0f;
    public bool IsAsleep => asleepTimer > 0f;
    public bool IsIncapacitated => IsDead || IsStunned || IsAsleep;

    // 반올림(RoundToInt)은 .5 값을 짝수로 내림(banker's rounding)하기 때문에, 예를 들어 3 * 1.5 = 4.5가
    // 의도한 "1.5배"보다 적은 4로 내려가 버릴 수 있다. 올림을 써서 배율 상향 의도가 항상 최소한
    // 그대로 반영되게 한다.
    private int MaxHealth => Mathf.CeilToInt(config != null ? config.monsterMaxHealth : 4.5f);

    /// <summary>플레이어와 동일한 2D 좌표계로 변환한 몬스터 위치. 몬스터는 NavMesh(x,z 평면)로 움직이고 x,z를 2D x,y로 매핑해서 렌더링한다.</summary>
    public Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        frameAnimator = GetComponentInChildren<SimpleFrameAnimator>();
        Health = MaxHealth;
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

    public void TakeDamage(float amount)
    {
        if (IsDead || Health <= 0) return;

        Health -= amount;
        Debug.Log($"{gameObject.name} 몬스터가 피격당함! 데미지: {amount:0.#}, 남은 체력: {Mathf.Max(Health, 0):0.#}/{MaxHealth}");

        if (Health <= 0)
        {
            Die();
        }
        else
        {
            frameAnimator?.PlayHurt();
        }
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
        frameAnimator?.PlayDeath();
        if (spriteRenderer != null) spriteRenderer.color = deadColor;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;

        Debug.Log($"{gameObject.name} 몬스터 사망!");
    }
}
