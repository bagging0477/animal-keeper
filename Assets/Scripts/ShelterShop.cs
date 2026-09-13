using System;
using UnityEngine;
using UnityEngine.UI;

public class ShelterShop : MonoBehaviour
{
    [Serializable]
    public class ShopItem
    {
        public string itemName;
        public Button button;
        public WeaponType weaponType = WeaponType.None;
        [Tooltip("체크하면 무기 구매가 아니라 마취총 탄약을 충전하는 소모품으로 취급된다.")]
        public bool isTranquilizerAmmoRefill = false;
    }

    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private ShopItem[] items;
    [SerializeField] private Text mileageText;

    private int GetPrice(ShopItem item)
    {
        if (config != null) return config.GetShopItemPrice(item.itemName);

        Debug.LogWarning($"{name}: GameBalanceConfig not assigned; '{item.itemName}' price defaults to 0.");
        return 0;
    }

    private void Start()
    {
        foreach (ShopItem item in items)
        {
            if (item.button == null) continue;
            ShopItem captured = item;
            item.button.onClick.AddListener(() => Purchase(captured));
        }

        RefreshUI();
    }

    private void Purchase(ShopItem item)
    {
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

        int price = GetPrice(item);
        bool success;
        if (item.isTranquilizerAmmoRefill)
        {
            int refillAmount = config != null ? config.tranquilizerAmmoRefillAmount : 5;
            success = GameManager.Instance.TryPurchaseTranquilizerAmmo(price, refillAmount);
        }
        else if (item.weaponType != WeaponType.None)
        {
            success = GameManager.Instance.PurchaseWeapon(item.weaponType, price);
        }
        else
        {
            success = GameManager.Instance.TrySpendMileage(price);
        }

        if (success)
        {
            Debug.Log($"{item.itemName} 구매 완료 (-{price} 마일리지)");
        }

        RefreshUI();
    }

    private void RefreshUI()
    {
        int mileage = GameManager.Instance != null ? GameManager.Instance.Mileage : 0;
        bool gameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;
        if (mileageText != null) mileageText.text = $"보유 마일리지: {mileage}";

        foreach (ShopItem item in items)
        {
            if (item.button == null) continue;

            bool alreadyOwned = item.weaponType != WeaponType.None
                && GameManager.Instance != null
                && GameManager.Instance.OwnsWeapon(item.weaponType);

            bool ammoFull = item.isTranquilizerAmmoRefill
                && GameManager.Instance != null
                && GameManager.Instance.TranquilizerAmmo >= GameManager.Instance.MaxTranquilizerAmmo;

            item.button.interactable = !gameOver && !alreadyOwned && !ammoFull && mileage >= GetPrice(item);
        }
    }
}
