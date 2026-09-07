using System;
using UnityEngine;
using UnityEngine.UI;

public class ShelterShop : MonoBehaviour
{
    [Serializable]
    public class ShopItem
    {
        public string itemName;
        public int price;
        public Button button;
        public WeaponType weaponType = WeaponType.None;
    }

    [SerializeField] private ShopItem[] items;
    [SerializeField] private Text mileageText;

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

        bool success = item.weaponType != WeaponType.None
            ? GameManager.Instance.PurchaseWeapon(item.weaponType, item.price)
            : GameManager.Instance.TrySpendMileage(item.price);

        if (success)
        {
            Debug.Log($"{item.itemName} 구매 완료 (-{item.price} 마일리지)");
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

            item.button.interactable = !gameOver && !alreadyOwned && mileage >= item.price;
        }
    }
}
