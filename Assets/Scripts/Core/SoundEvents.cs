using System;
using UnityEngine;

public static class SoundEvents
{
    /// <summary>두 번째 인자는 소리의 강도(intensity, 보통 0~1)다. 소리반응형 몬스터(SoundReactiveMonsterAI)는
    /// 이 값에 비례해 감지 범위와 Investigate 접근 거리를 줄인다 - 1.0(동물 울음소리)은 기준 그대로,
    /// 0.3(플레이어 스프린트 발소리)처럼 낮은 값은 더 가까이서만, 더 짧은 거리만 반응하게 만든다.</summary>
    public static event Action<Vector3, float> OnSoundEmitted;

    public static void Emit(Vector3 position, float intensity = 1f)
    {
        OnSoundEmitted?.Invoke(position, intensity);
    }
}
