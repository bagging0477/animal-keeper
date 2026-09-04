using UnityEngine;

/// <summary>동물이 마취총에 맞았을 때의 수면 상태만 담당한다. 동물은 몬스터와 달리 체력 개념이 없다.</summary>
public class AnimalSleep : MonoBehaviour, ISleepable
{
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
