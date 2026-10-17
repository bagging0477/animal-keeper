using UnityEngine;

/// <summary>동물이 마취총에 맞았을 때의 수면 상태만 담당한다. 동물은 몬스터와 달리 체력 개념이 없다.</summary>
public class AnimalSleep : MonoBehaviour, ISleepable
{
    /// <summary>현재 활성화된 모든 AnimalSleep. 지뢰/폭탄/마취 다트가 매 프레임 FindObjectsByType으로 씬 전체를 훑는 대신
    /// 이 목록만 본다(개체 수가 늘어도 비용이 개체 수에만 비례한다). 순회 중에 대상이 비활성화될 수 있으므로 뒤에서부터 돈다.</summary>
    public static readonly System.Collections.Generic.List<AnimalSleep> Active = new System.Collections.Generic.List<AnimalSleep>();

    // 도메인 리로드를 끈 에디터(Enter Play Mode Options)에서도 이전 플레이의 항목이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveList() => Active.Clear();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    /// <summary>0보다 크면 맞은 뒤 넘겨받은 delay 대신 이 시간 동안 쓰러지는 동작을 한 다음 잠든다. 거위 전용 - AnimalGoose가 Awake에서
    /// GameBalanceConfig.gooseDownDuration으로 정한다. 다른 동물은 0 그대로라 예전처럼 delay 뒤에 잠든다.</summary>
    public float DownDuration { get; set; }

    private float asleepTimer;
    private bool sleepPending;
    private float pendingSleepTimer;
    private float pendingSleepDuration;

    public bool IsAsleep => asleepTimer > 0f;

    /// <summary>마취에 맞았지만 아직 잠들기 전(sleepDelay 동안)인지. 거북이는 이 동안 기절 프레임에 멈춰 있다.</summary>
    public bool IsSleepPending => sleepPending;
    public Vector2 GamePosition => transform.position;

    private void Update()
    {
        if (sleepPending)
        {
            pendingSleepTimer -= Time.deltaTime;
            if (pendingSleepTimer <= 0f)
            {
                sleepPending = false;
                asleepTimer = pendingSleepDuration;
            }
        }

        if (asleepTimer > 0f) asleepTimer -= Time.deltaTime;
    }

    /// <summary>잠들어 있거나 잠들 예정이면 바로 깨운다(숨은 거북이처럼 마취가 듣지 않는 상태용).</summary>
    public void WakeUp()
    {
        sleepPending = false;
        asleepTimer = 0f;
    }

    public void PutToSleep(float delay, float duration)
    {
        if (DownDuration > 0f)
        {
            // 이미 잠들어 있으면 다시 쓰러지지 않고 기절 시간만 새로 채운다. 쓰러지는 중에 다시 맞으면 무시한다(시간이 늘어나지 않는다).
            if (IsAsleep)
            {
                asleepTimer = duration;
                return;
            }
            if (sleepPending) return;
            delay = DownDuration;
        }

        sleepPending = true;
        pendingSleepTimer = delay;
        pendingSleepDuration = duration;
    }
}
