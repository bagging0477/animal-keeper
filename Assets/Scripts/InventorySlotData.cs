using UnityEngine;

/// <summary>인벤토리 한 칸의 내용물. 같은 종류라도 절대 합쳐지지 않고 슬롯 하나에 1개만 들어가므로,
/// 개수가 아니라 슬롯 배열 자체가 소지량을 나타낸다. 동물 슬롯은 납품(TryDeliverSelectedAnimal)에
/// 필요한 최소한의 정보(무게, 오늘 구조 기록용 Id, 원래 행동 종류)만 값으로 들고 있고, 원본
/// AnimalRescue 컴포넌트/게임오브젝트는 줍는 즉시 비활성화되어 더 이상 참조하지 않는다.</summary>
[System.Serializable]
public struct InventorySlotData
{
    public InventoryItemType Type;
    public string AnimalId;
    public int AnimalWeight;
    public AnimalBehaviorKind AnimalKind;

    /// <summary>슬롯 UI에 그릴 아이콘. 동물은 AnimalRescue.InventoryIcon(수동 지정 또는 Idle 첫 프레임),
    /// 지뢰/폭탄은 GadgetPickup에 지정된 스프라이트에서 온다. 아직 아이콘이 없으면 null로 남아있고,
    /// InventoryUI는 이 경우 기존의 색상 채움만으로 표시한다.</summary>
    public Sprite Icon;

    public static readonly InventorySlotData Empty = new InventorySlotData { Type = InventoryItemType.None };
}
