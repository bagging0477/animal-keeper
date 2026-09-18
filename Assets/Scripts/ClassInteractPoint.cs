using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>ClassSelectScene에 놓인 직업(스카우트/트래퍼/아처) 스탠드 하나를 담당한다.
/// 플레이어가 가까이 오면 공유 팝업(ClassInfoPopup)에 스탯/가격을 띄우고, E키로 해금(+즉시 장착) 또는
/// 재장착을 처리한다. 현재 장착 중인 클래스는 몸체 스프라이트가 깜빡이는 색으로 표시된다.</summary>
public class ClassInteractPoint : MonoBehaviour
{
    [SerializeField] private PlayerClass playerClass;
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private float interactionRange = 1.6f;
    [SerializeField] private ClassInfoPopup popup;
    [SerializeField] private SpriteRenderer bodySprite;

    private static readonly Color EquippedColorA = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color EquippedColorB = new Color(1f, 0.55f, 0.05f, 1f);

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
        RefreshEquippedVisual();

        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange) return;

        GameManager gm = GameManager.Instance;
        bool unlocked = gm != null && gm.IsClassUnlocked(playerClass);
        bool selected = gm != null && gm.CurrentClass == playerClass;
        bool freeSelection = gm != null && gm.NeedsStartingClassSelection;
        int price = config != null ? config.GetClassUnlockPrice(playerClass) : 0;

        if (popup != null)
        {
            string priceLine;
            if (!unlocked)
            {
                priceLine = freeSelection ? "E를 눌러 선택 (무료)" : $"{price} 마일리지 - E를 눌러 구매 후 장착";
            }
            else
            {
                priceLine = selected ? "현재 장착 중" : "해금됨 - E를 눌러 장착";
            }
            popup.Show(GetClassDisplayName(playerClass), BuildStatsText(playerClass), priceLine, transform);
        }

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame || gm == null) return;

        if (!unlocked)
        {
            bool acquired = freeSelection ? gm.UnlockClassFree(playerClass) : gm.PurchaseClass(playerClass);
            if (acquired) gm.SelectClass(playerClass);
        }
        else if (!selected)
        {
            gm.SelectClass(playerClass);
        }
    }

    private void RefreshEquippedVisual()
    {
        if (bodySprite == null) return;

        bool selected = GameManager.Instance != null && GameManager.Instance.CurrentClass == playerClass;
        if (!selected)
        {
            bodySprite.color = Color.white;
            return;
        }

        float t = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
        bodySprite.color = Color.Lerp(EquippedColorA, EquippedColorB, t);
    }

    private string BuildStatsText(PlayerClass pc)
    {
        int healthPercent = config != null ? config.GetClassMaxHealthPercent(pc) : 100;
        float speedMultiplier = config != null ? config.GetClassMoveSpeedMultiplier(pc) : 1f;
        string speedLine = Mathf.Approximately(speedMultiplier, 1f)
            ? "이동속도: 평균"
            : $"이동속도: 평균의 {speedMultiplier:0.##}배";
        string weaponLine = GameManager.GetWeaponForClass(pc) switch
        {
            WeaponType.Net => "무기: 근접 (포획망)",
            WeaponType.TranquilizerGun => "무기: 원거리 (마취화살)",
            _ => "무기: 없음"
        };

        return $"체력: {healthPercent}%\n{speedLine}\n{weaponLine}";
    }

    private static string GetClassDisplayName(PlayerClass pc) => pc switch
    {
        PlayerClass.Scout => "스카우트",
        PlayerClass.Trapper => "트래퍼",
        PlayerClass.Archer => "아처",
        _ => pc.ToString()
    };
}
