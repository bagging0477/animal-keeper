using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>ShelterScene에 놓인 상점 아이템(마취총 탄약/지뢰/폭탄) 진열대 하나를 담당한다.
/// 플레이어가 가까이 오면 공유 팝업(ShopItemPopup)에 이름/효과/가격을 띄우고, E키를 누를 때마다
/// 마일리지가 충분하면 1개씩 구매해 인벤토리에 바로 더해준다.</summary>
public class ShopItemInteractPoint : MonoBehaviour
{
    [SerializeField] private ShopItemKind itemKind;
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private ShopItemPopup popup;

    private Transform player;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange) return;

        int price = GetPrice();

        if (popup != null)
        {
            popup.Show(GetDisplayName(), GetDescription(), $"{price} 마일리지 - E를 눌러 구매", transform);
        }

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        Purchase(price);
    }

    private void Purchase(int price)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        if (itemKind == ShopItemKind.TranquilizerAmmo && gm.TranquilizerAmmo >= gm.MaxTranquilizerAmmo)
        {
            Debug.Log("마취총 탄약이 이미 가득 찼습니다");
            return;
        }

        // 먼저 돈을 낼 수 있는지부터 확인한다 - 그래야 아래에서 지뢰/폭탄 슬롯을 먼저 확보한
        // 뒤 돈을 뗄 때 잔액 부족으로 실패할 일이 없다(인벤토리 슬롯만 공짜로 차지하는 버그 방지).
        if (gm.Mileage < price)
        {
            Debug.Log("마일리지가 부족합니다");
            return;
        }

        bool success = itemKind switch
        {
            ShopItemKind.TranquilizerAmmo => gm.TryPurchaseTranquilizerAmmo(price, config != null ? config.tranquilizerAmmoRefillAmount : 5),
            ShopItemKind.Mine => TrySpendAnd(gm, price, () => gm.TryAddMine()),
            ShopItemKind.Bomb => TrySpendAnd(gm, price, () => gm.TryAddBomb()),
            _ => false
        };

        if (success)
        {
            Debug.Log($"{GetDisplayName()} 구매 완료 (-{price} 마일리지)");
        }
    }

    private static bool TrySpendAnd(GameManager gm, int price, System.Func<bool> tryGiveItem)
    {
        if (!tryGiveItem()) return false;
        gm.TrySpendMileage(price);
        return true;
    }

    private int GetPrice() => itemKind switch
    {
        ShopItemKind.TranquilizerAmmo => config != null ? config.GetShopItemPrice("마취총 탄약") : 0,
        ShopItemKind.Mine => config != null ? config.GetShopItemPrice("지뢰") : 0,
        ShopItemKind.Bomb => config != null ? config.GetShopItemPrice("폭탄") : 0,
        _ => 0
    };

    private string GetDescription() => itemKind switch
    {
        ShopItemKind.TranquilizerAmmo => config != null
            ? $"마취총 탄약 +{config.tranquilizerAmmoRefillAmount}개 (최대 {config.tranquilizerMaxAmmo}개)"
            : "마취총 탄약 충전",
        ShopItemKind.Mine => config != null
            ? $"밟으면 데미지 {config.mineDamage} + {config.mineStunDuration:0.#}초 스턴 (1회용)"
            : "밟으면 데미지 + 스턴 (1회용)",
        ShopItemKind.Bomb => config != null
            ? $"인벤토리에서 선택 후 클릭으로 사용, 범위 {config.bombRadius:0.#} 안 몬스터에게 데미지 {config.bombDamage} + {config.bombStunDuration:0.#}초 스턴 (1회용)"
            : "인벤토리에서 선택 후 클릭으로 사용, 범위 피해 + 스턴 (1회용)",
        _ => ""
    };

    private static string GetDisplayName(ShopItemKind kind) => kind switch
    {
        ShopItemKind.TranquilizerAmmo => "마취총 재장전",
        ShopItemKind.Mine => "지뢰",
        ShopItemKind.Bomb => "폭탄",
        _ => kind.ToString()
    };

    private string GetDisplayName() => GetDisplayName(itemKind);
}
