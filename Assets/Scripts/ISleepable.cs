using UnityEngine;

/// <summary>마취총에 맞아 재울 수 있는 대상(몬스터, 동물 등)이 구현하는 인터페이스.</summary>
public interface ISleepable
{
    bool IsAsleep { get; }
    Vector2 GamePosition { get; }
    void PutToSleep(float delay, float duration);
}
