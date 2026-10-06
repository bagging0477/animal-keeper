using System.Collections.Generic;
using UnityEngine;

/// <summary>상인 한 명이 파는 품목 목록. 상인(ShopMerchant)마다 이 에셋만 다르게 넣으면 같은 상점 메뉴
/// (ShopMenuUI)가 그 목록으로 그려진다. 품목을 늘리거나 순서/이름/아이콘을 바꿀 때는 이 에셋의 entries만
/// 고치면 된다 - 가격은 여기 두지 않고 GameBalanceConfig.shopItemPrices를 그대로 쓴다. 목록이 비어 있으면
/// emptyMessage만 보인다(패시브 강화 상인처럼 아직 품목이 없는 상인).</summary>
[CreateAssetMenu(menuName = "Animal Keeper/Shop Catalog", fileName = "ShopCatalog")]
public class ShopCatalog : ScriptableObject
{
    [Tooltip("상점 메뉴 맨 위에 보이는 상인/상점 이름")]
    public string title = "상점";

    [Tooltip("판매 품목이 하나도 없을 때 메뉴에 대신 보이는 문구")]
    [TextArea] public string emptyMessage = "준비 중입니다";

    public List<ShopCatalogEntry> entries = new List<ShopCatalogEntry>();
}

[System.Serializable]
public class ShopCatalogEntry
{
    [Tooltip("품목 종류. 구매 효과(인벤토리에 넣기/탄약 충전 등)와 가격(GameBalanceConfig)을 이 값으로 찾는다.")]
    public ShopItemKind kind;

    [Tooltip("메뉴에 보이는 이름")]
    public string displayName;

    [Tooltip("메뉴에 보이는 아이콘. 지뢰/폭탄은 인벤토리 슬롯에도 이 아이콘이 그대로 들어간다.")]
    public Sprite icon;
}
