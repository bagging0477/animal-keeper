using UnityEngine;

/// <summary>상점 품목의 가격/설명/구매 규칙을 한 곳에 모은다. 어느 상인의 메뉴에서 사든 같은 규칙을 탄다.
/// 새 품목 종류(ShopItemKind)를 추가할 때는 여기 GetDescription/TryApply에 효과를 한 줄씩 더하고,
/// GameBalanceConfig.shopItemPrices에 가격을 넣은 뒤 상인의 ShopCatalog에 항목을 추가한다.</summary>
public static class ShopPurchase
{
    public static int GetPrice(GameBalanceConfig config, ShopItemKind kind) =>
        config != null ? config.GetShopItemPrice(kind) : 0;

    public static string GetDescription(GameBalanceConfig config, ShopItemKind kind) => kind switch
    {
        ShopItemKind.TranquilizerAmmo => config != null
            ? $"마취총 탄약 +{config.tranquilizerAmmoRefillAmount}개 (최대 {config.tranquilizerMaxAmmo}개)"
            : "마취총 탄약 충전",
        ShopItemKind.Mine => config != null
            ? $"밟으면 데미지 {config.mineDamage} + {config.mineStunDuration:0.#}초 스턴 (1회용)"
            : "밟으면 데미지 + 스턴 (1회용)",
        ShopItemKind.Bomb => config != null
            ? $"범위 {config.bombRadius:0.#} 안 몬스터에게 데미지 {config.bombDamage} + {config.bombStunDuration:0.#}초 스턴 (1회용)"
            : "범위 피해 + 스턴 (1회용)",
        _ => ""
    };

    /// <summary>1개를 산다. 실패하면 false와 함께 플레이어에게 보여줄 이유를 failMessage로 돌려준다.
    /// 마일리지와 인벤토리 칸을 먼저 모두 확인한 뒤에만 상태를 바꾸므로, 실패한 구매가 마일리지나 칸을
    /// 일부만 써버리는 일은 없다. 마일리지 차감은 GameManager.TrySpendMileage가 0 아래로 내려가지 않게 막는다.</summary>
    public static bool TryPurchase(GameBalanceConfig config, ShopCatalogEntry entry, out string failMessage)
    {
        failMessage = null;
        GameManager gm = GameManager.Instance;
        if (gm == null || entry == null)
        {
            failMessage = "지금은 구매할 수 없습니다";
            return false;
        }

        int price = GetPrice(config, entry.kind);

        switch (entry.kind)
        {
            case ShopItemKind.TranquilizerAmmo:
                if (gm.TranquilizerAmmo >= gm.MaxTranquilizerAmmo)
                {
                    failMessage = "마취총 탄약이 이미 가득 찼습니다";
                    return false;
                }
                break;
            case ShopItemKind.Mine:
            case ShopItemKind.Bomb:
                if (!gm.HasInventorySpace)
                {
                    failMessage = "인벤토리가 가득 찼습니다";
                    return false;
                }
                break;
        }

        if (gm.Mileage < price)
        {
            failMessage = "마일리지가 부족합니다";
            return false;
        }

        bool applied = entry.kind switch
        {
            ShopItemKind.TranquilizerAmmo => gm.TryPurchaseTranquilizerAmmo(price, config != null ? config.tranquilizerAmmoRefillAmount : 5),
            ShopItemKind.Mine => gm.TryAddMine(entry.icon) && gm.TrySpendMileage(price),
            ShopItemKind.Bomb => gm.TryAddBomb(entry.icon) && gm.TrySpendMileage(price),
            _ => false
        };

        if (!applied)
        {
            failMessage = "구매하지 못했습니다";
            Debug.LogWarning($"ShopPurchase: {entry.kind} 구매 효과를 적용하지 못했다 (사전 확인은 통과).");
            return false;
        }

        Debug.Log($"{entry.displayName} 구매 완료 (-{price} 마일리지)");
        return true;
    }
}
