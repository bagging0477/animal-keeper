using UnityEngine;

/// <summary>패시브 하나의 데이터. 패시브 강화 상인의 ShopCatalog.passives에 넣으면 상점에 나오고, 사면 이번 판 동안
/// 효과가 유지된다(RunPassives). 가격과 최대 구매 횟수는 다른 밸런스 수치처럼 GameBalanceConfig.passivePrices에서
/// 이 에셋을 찾아 쓴다. 효과는 effectKind/effectValue로만 정하고, 다른 스크립트는 PassiveEffects로 합친 값만 읽는다.</summary>
[CreateAssetMenu(menuName = "Animal Keeper/Passive Definition", fileName = "Passive")]
public class PassiveDefinition : ScriptableObject
{
    [Tooltip("다른 패시브와 겹치지 않는 영문 id (로그/저장용)")]
    public string id;

    [Tooltip("상점 메뉴와 구매 자막에 보이는 이름")]
    public string displayName;

    [Tooltip("상점 메뉴의 설명. 효과 수치(예: 이동속도 +5%)는 effectValue에서 자동으로 뒤에 붙는다.")]
    [TextArea] public string description;

    [Tooltip("상점 메뉴에 보이는 아이콘")]
    public Sprite icon;

    [Tooltip("효과 종류. 같은 종류의 패시브가 여러 개면 PassiveEffects가 합쳐서 돌려준다.")]
    public PassiveEffectKind effectKind;

    [Tooltip("효과 값 (1회 구매당). MoveSpeedMultiplier는 곱하는 배율(1.05 = +5%), ExtraInventorySlots는 늘어나는 칸 수, " +
        "Flashlight는 쓰지 않는다.")]
    public float effectValue = 1f;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>effectValue로 만든 효과 한 줄 (예: "이동속도 +5%").</summary>
    public string EffectSummary => effectKind switch
    {
        PassiveEffectKind.MoveSpeedMultiplier => $"이동속도 {(effectValue - 1f) * 100f:+0.#;-0.#}%",
        PassiveEffectKind.ExtraInventorySlots => $"인벤토리 {effectValue:+0;-0}칸",
        PassiveEffectKind.Flashlight => "플래시라이트",
        _ => ""
    };
}
