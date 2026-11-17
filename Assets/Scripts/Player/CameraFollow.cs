using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.08f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [Tooltip("목표 위치와 이 거리(유닛) 이상 떨어져 있으면 따라가지 않고 즉시 붙는다. 씬 시작 시 맵 생성기가 플레이어를 " +
        "스폰 지점으로 옮기거나 순간이동했을 때, 카메라가 원점에서 맵을 가로질러 휙 날아가는 화면(멀미 유발)을 막는다.")]
    [SerializeField, Min(0f)] private float snapDistance = 4f;

    private Vector3 velocity;
    private bool hasSnapped;

    // 화면 흔들림(HitFeel). 따라가기 계산은 흔들림을 뺀 위치로 하고, 마지막에 흔들림만 더한다.
    private const float MaxShakeAmplitude = 0.12f;
    private Vector3 shakeOffset;
    private float shakeAmplitude, shakeDuration, shakeRemaining, shakeFrequency, shakeStepTimer;
    private Vector2 shakeDirection;

    /// <summary>지금 흔들림이 남아 있는지(테스트용).</summary>
    public bool IsShaking => shakeRemaining > 0f;

    /// <summary>세기 amplitude(칸)로 duration초 동안 흔든다(선형으로 줄어듦). 이미 흔들리는 중이면 지금 남은 세기와 비교해 큰 쪽만 쓴다(더하지 않는다).</summary>
    public void AddShake(float amplitude, float duration, float frequency)
    {
        if (amplitude <= 0f || duration <= 0f) return;
        float current = shakeRemaining > 0f ? shakeAmplitude * (shakeRemaining / shakeDuration) : 0f;
        if (Mathf.Min(amplitude, MaxShakeAmplitude) < current) return;
        shakeAmplitude = Mathf.Min(amplitude, MaxShakeAmplitude);
        shakeDuration = duration;
        shakeRemaining = duration;
        shakeFrequency = Mathf.Max(1f, frequency);
        shakeStepTimer = 0f;
    }

    private void OnDisable()
    {
        // 꺼질 때(테스트가 카메라를 직접 놓을 때 등) 흔들림을 남기지 않는다.
        transform.position -= shakeOffset;
        shakeOffset = Vector3.zero;
        shakeRemaining = 0f;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 current = transform.position - shakeOffset;
        Vector3 desiredPosition = target.position + offset;

        if (!hasSnapped || (desiredPosition - current).sqrMagnitude >= snapDistance * snapDistance)
        {
            hasSnapped = true;
            current = desiredPosition;
            velocity = Vector3.zero;
        }
        else
        {
            current = Vector3.SmoothDamp(current, desiredPosition, ref velocity, smoothTime);
        }

        shakeOffset = NextShakeOffset();
        transform.position = current + shakeOffset;
    }

    private Vector3 NextShakeOffset()
    {
        if (shakeRemaining <= 0f) return Vector3.zero;
        // 일시정지(timeScale 0) 중에는 흔들린 채 멈춰 있지 않게 바로 끝낸다. 평소에는 게임 시간으로 줄인다(프레임 속도와 무관하게 같은 길이).
        if (Time.timeScale <= 0f) { shakeRemaining = 0f; return Vector3.zero; }
        float dt = Time.deltaTime;
        shakeRemaining -= dt;
        if (shakeRemaining <= 0f) return Vector3.zero;
        shakeStepTimer -= dt;
        if (shakeStepTimer <= 0f)
        {
            shakeStepTimer += 1f / shakeFrequency;
            float a = HitFeel.Next01() * Mathf.PI * 2f;
            shakeDirection = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        float strength = shakeAmplitude * (shakeRemaining / shakeDuration);
        return new Vector3(shakeDirection.x * strength, shakeDirection.y * strength, 0f);
    }
}
