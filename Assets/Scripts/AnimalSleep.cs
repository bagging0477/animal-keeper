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

    private float asleepTimer;
    private bool sleepPending;
    private float pendingSleepTimer;
    private float pendingSleepDuration;

    public bool IsAsleep => asleepTimer > 0f;
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

    public void PutToSleep(float delay, float duration)
    {
        sleepPending = true;
        pendingSleepTimer = delay;
        pendingSleepDuration = duration;
    }
}
