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

    /// <summary>동물을 내려놓을 때 되살릴 원본 프리팹(AnimalRescue.SourcePrefab). 비어 있으면 AnimalKind로 고른다.</summary>
    public AnimalRescue AnimalPrefab;

    /// <summary>슬롯 UI에 그릴 아이콘. 동물은 AnimalRescue.InventoryIcon(수동 지정 또는 시트 첫 프레임),
    /// 바닥에서 주운 지뢰/폭탄은 GadgetPickup의 스프라이트에서 온다. 시작 지급/상점 구매처럼 아이콘 없이
    /// 들어오면 null로 남고, InventoryUI가 종류별 기본 아이콘(없으면 임시 아이콘)으로 대신 그린다.</summary>
    public Sprite Icon;

    public static readonly InventorySlotData Empty = new InventorySlotData { Type = InventoryItemType.None };
}
