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
        [Tooltip("체크하면 무기 구매가 아니라 마취화살 탄약을 충전하는 소모품으로 취급된다.")]
        public bool isTranquilizerAmmoRefill = false;
        [Tooltip("체크하면 이 버튼은 클래스 해금/장착 용도로 취급된다. 아직 해금 전이면 클릭 시 마일리지를 " +
            "소모해 해금 후 바로 장착하고, 이미 해금된 상태면 마일리지 소모 없이 장착만 한다.")]
        public bool isClassUnlock = false;
        public PlayerClass unlockClass = PlayerClass.Trapper;
    }

    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private ShopItem[] items;
    [SerializeField] private Text mileageText;

    private int GetPrice(ShopItem item)
    {
        if (item.isClassUnlock) return config != null ? config.GetClassUnlockPrice(item.unlockClass) : 0;
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

        if (item.isClassUnlock)
        {
            if (!GameManager.Instance.IsClassUnlocked(item.unlockClass))
            {
                int unlockPrice = GetPrice(item);
                if (GameManager.Instance.PurchaseClass(item.unlockClass))
                {
                    GameManager.Instance.SelectClass(item.unlockClass);
                    Debug.Log($"{item.itemName} 해금 및 장착 완료 (-{unlockPrice} 마일리지)");
                }
            }
            else
            {
                GameManager.Instance.SelectClass(item.unlockClass);
                Debug.Log($"{item.itemName} 장착");
            }

            RefreshUI();
            return;
        }

        int price = GetPrice(item);
        bool success = item.isTranquilizerAmmoRefill
            ? GameManager.Instance.TryPurchaseTranquilizerAmmo(price, config != null ? config.tranquilizerAmmoRefillAmount : 5)
            : GameManager.Instance.TrySpendMileage(price);

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

            if (item.isClassUnlock)
            {
                bool unlocked = GameManager.Instance != null && GameManager.Instance.IsClassUnlocked(item.unlockClass);
                bool selected = GameManager.Instance != null && GameManager.Instance.CurrentClass == item.unlockClass;
                item.button.interactable = !gameOver && !selected && (unlocked || mileage >= GetPrice(item));
                continue;
            }

            bool ammoFull = item.isTranquilizerAmmoRefill
                && GameManager.Instance != null
                && GameManager.Instance.TranquilizerAmmo >= GameManager.Instance.MaxTranquilizerAmmo;

            item.button.interactable = !gameOver && !ammoFull && mileage >= GetPrice(item);
        }
    }
}
