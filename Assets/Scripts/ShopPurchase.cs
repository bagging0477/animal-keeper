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

    // ---------- 패시브 ----------

    public static int GetPassivePrice(GameBalanceConfig config, PassiveDefinition passive) =>
        config != null && config.TryGetPassivePrice(passive, out GameBalanceConfig.PassivePrice entry) ? entry.price : 0;

    /// <summary>이번 판에 이미 최대 횟수만큼 샀는지.</summary>
    public static bool IsPassiveSoldOut(GameBalanceConfig config, PassiveDefinition passive)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || config == null || !config.TryGetPassivePrice(passive, out GameBalanceConfig.PassivePrice entry)) return false;
        return gm.Passives.GetPurchaseCount(passive) >= entry.maxPurchases;
    }

    public static string GetPassiveDescription(PassiveDefinition passive)
    {
        if (passive == null) return "";
        string effect = passive.EffectSummary;
        if (string.IsNullOrEmpty(passive.description)) return effect;
        return string.IsNullOrEmpty(effect) ? passive.description : $"{passive.description} ({effect})";
    }

    /// <summary>"러닝화를 구매했습니다"처럼 이름 받침에 맞춰 을/를을 붙인다.</summary>
    public static string GetPassivePurchasedMessage(PassiveDefinition passive) =>
        $"{WithObjectParticle(passive.DisplayName)} 구매했습니다";

    /// <summary>패시브를 1회 산다. 구매 횟수와 마일리지를 먼저 확인하고, 마일리지 차감과 효과 적용은
    /// GameManager.TryPurchasePassive가 함께 처리한다(효과는 PassiveEffects로 바로 보인다).</summary>
    public static bool TryPurchasePassive(GameBalanceConfig config, PassiveDefinition passive, out string failMessage)
    {
        failMessage = null;
        GameManager gm = GameManager.Instance;
        if (gm == null || passive == null || config == null || !config.TryGetPassivePrice(passive, out GameBalanceConfig.PassivePrice entry))
        {
            failMessage = "지금은 구매할 수 없습니다";
            return false;
        }

        if (gm.Passives.GetPurchaseCount(passive) >= entry.maxPurchases)
        {
            failMessage = "이미 구매했습니다";
            return false;
        }

        if (gm.Mileage < entry.price)
        {
            failMessage = "마일리지가 부족합니다";
            return false;
        }

        if (!gm.TryPurchasePassive(passive, entry.price, entry.maxPurchases))
        {
            failMessage = "구매하지 못했습니다";
            Debug.LogWarning($"ShopPurchase: 패시브 {passive.name} 구매를 적용하지 못했다 (사전 확인은 통과).");
            return false;
        }

        Debug.Log($"{passive.DisplayName} 패시브 구매 (-{entry.price} 마일리지, {gm.Passives.GetPurchaseCount(passive)}/{entry.maxPurchases})");
        return true;
    }

    private static string WithObjectParticle(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        char last = word[word.Length - 1];
        if (last < '가' || last > '힣') return word + "을(를)";
        return word + ((last - '가') % 28 != 0 ? "을" : "를");
    }
}
