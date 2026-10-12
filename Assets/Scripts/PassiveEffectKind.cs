/// <summary>패시브(PassiveDefinition) 효과 종류. 효과를 합치는 규칙은 RunPassives, 조회는 PassiveEffects 한 곳에 있다.</summary>
public enum PassiveEffectKind
{
    /// <summary>플레이어 기본 이동속도 배율 (여러 개면 곱한다).</summary>
    MoveSpeedMultiplier,

    /// <summary>인벤토리 추가 칸 수 (여러 개면 더한다).</summary>
    ExtraInventorySlots,

    /// <summary>플래시라이트 보유 (하나라도 있으면 보유).</summary>
    Flashlight
}
