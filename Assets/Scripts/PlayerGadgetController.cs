using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>상점에서 구매하는(현재는 디버그로 기본 지급되는) 1회용 소모품 - 지뢰/폭탄 - 의
/// 설치/사용을 담당한다. 무기(PlayerWeaponController)와 별개로, 클래스와 상관없이 누구나 쓸 수 있다.
/// 숫자키 1~5로 인벤토리 슬롯을 고른 뒤, 마우스 클릭으로 그 슬롯의 지뢰/폭탄을 사용한다 - 선택된
/// 슬롯이 지뢰/폭탄이 아니면(동물이거나 빈 칸이면) 클릭해도 아무 일도 일어나지 않는다.</summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerGadgetController : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private MineTrap minePrefab;

    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (GameManager.Instance == null) return;

        // 클릭은 전투(PlayerWeaponController)에도 쓰이는 흔한 입력이라, 선택된 슬롯이 지뢰/폭탄이
        // 아닐 때마다 실패 로그를 찍으면 콘솔이 도배된다 - 미리 슬롯 종류를 보고 맞을 때만 시도한다.
        switch (GameManager.Instance.GetSlot(GameManager.Instance.SelectedSlotIndex).Type)
        {
            case InventoryItemType.Mine:
                PlaceMine();
                break;
            case InventoryItemType.Bomb:
                UseBomb();
                break;
        }
    }

    private void PlaceMine()
    {
        if (minePrefab == null) return;
        if (!GameManager.Instance.TryUseSelectedMine()) return;

        float distance = config != null ? config.minePlacementDistance : 1.2f;
        Vector3 position = transform.position + (Vector3)(playerMovement.LookDirection * distance);
        Instantiate(minePrefab, position, Quaternion.identity);
    }

    private void UseBomb()
    {
        if (!GameManager.Instance.TryUseSelectedBomb()) return;

        int damage = config != null ? config.bombDamage : 3;
        float stunDuration = config != null ? config.bombStunDuration : 2f;
        float radius = config != null ? config.bombRadius : 3f;
        Vector2 origin = transform.position;

        float effectDuration = config != null ? config.bombEffectDisplayDuration : 0.35f;
        Color effectColor = config != null ? config.bombEffectColor : new Color(1f, 0f, 0f, 0.5f);
        BombExplosionEffect.Spawn(transform.position, radius, effectDuration, effectColor);

        foreach (MonsterHealth monster in FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            if (monster.IsDead) continue;
            if (Vector2.Distance(origin, monster.GamePosition) > radius) continue;

            monster.TakeDamage(damage);
            monster.Stun(stunDuration);

            Debug.Log($"{monster.name}가 폭탄 범위에 맞음! 데미지: {damage}, 스턴: {stunDuration}초");
        }
    }
}
