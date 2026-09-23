using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>죽은 몬스터의 시체를 AnimalRescue와 동일한 방식(거리 기반 E키 상호작용)으로 인벤토리에
/// 담을 수 있게 한다. MonsterHealth.Die()가 시체를 씬에 그대로 남겨두는 기존 동작은 건드리지 않고,
/// 그 시체에 한해서만 상호작용을 추가로 얹는다 - 늑대 기반 순찰형 몬스터 전용 컴포넌트이며,
/// 소리반응형(Blood) 몬스터 프리팹에는 붙이지 않아 기존 처치 방식을 그대로 유지한다.
/// 인벤토리 슬롯에는 InventoryItemType.Animal로 담기므로, 그 이후로는 트럭 납품/보호소 무게 정산 등
/// 실제 동물과 완전히 동일한 경로를 그대로 탄다.</summary>
[RequireComponent(typeof(MonsterHealth))]
public class MonsterCorpsePickup : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Text promptText;

    [Tooltip("이 시체를 주울 때 재생할 픽업음. 비워두면 AudioManager의 기본 픽업음을 사용한다.")]
    [SerializeField] private AudioClip pickupSound;

    [Tooltip("인벤토리 슬롯에 표시할 아이콘. 비워두면 Visual 자식의 SimpleFrameAnimator에서 리컬러되지 " +
        "않은 원본 Idle 첫 프레임을 자동으로 가져온다 - 시체 자체는 MonsterHealth.deadColor로 틴트되어 " +
        "보이지만, 그건 SpriteRenderer.color(렌더러 틴트)일 뿐 Sprite 애셋 자체는 그대로라서 아이콘은 " +
        "리컬러 없이 원본 색 그대로 나온다.")]
    [SerializeField] private Sprite inventoryIcon;

    private MonsterHealth health;
    private Transform player;
    private Sprite resolvedIcon;
    private string id;

    private int Weight => config != null ? config.wolfCorpseWeight : 15;

    private void Awake()
    {
        health = GetComponent<MonsterHealth>();
    }

    private void Start()
    {
        id = BuildHierarchyId();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }

        if (promptText == null)
        {
            GameObject promptObj = GameObject.Find("PromptText");
            if (promptObj != null) promptText = promptObj.GetComponent<Text>();
        }

        resolvedIcon = inventoryIcon != null ? inventoryIcon : ResolveDefaultIcon();
    }

    // AnimalRescue.Id와 같은 방식(계층 경로)으로 만든다 - GameManager.TryAddAnimal이 요구하는
    // 문자열 식별자일 뿐, 동물처럼 오늘 구조 기록/재입장 시 재스폰 방지에 쓰이지는 않는다
    // (몬스터는 애초에 필드 상태가 저장되지 않아 트럭 왕복 시 항상 새로 스폰된다).
    private string BuildHierarchyId()
    {
        var path = new System.Text.StringBuilder(name);
        for (Transform t = transform.parent; t != null; t = t.parent)
        {
            path.Insert(0, "/").Insert(0, t.name);
        }
        return path.ToString();
    }

    private Sprite ResolveDefaultIcon()
    {
        SimpleFrameAnimator frameAnimator = GetComponentInChildren<SimpleFrameAnimator>();
        if (frameAnimator != null && frameAnimator.IdleSprite != null) return frameAnimator.IdleSprite;

        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        return spriteRenderer != null ? spriteRenderer.sprite : null;
    }

    private void Update()
    {
        if (!health.IsDead || player == null || GameManager.Instance == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        float distance = Vector2.Distance(health.GamePosition, new Vector2(player.position.x, player.position.y));
        bool inRange = distance <= interactionRange;

        if (inRange && GameManager.Instance.HasInventorySpace) SharedPrompt.Show(promptText, "E를 눌러 사체를 획득하세요");

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            PickUp();
        }
    }

    private void PickUp()
    {
        if (!GameManager.Instance.TryAddAnimal(id, Weight, AnimalBehaviorKind.MonsterCorpse, resolvedIcon)) return; // 인벤토리가 가득 찼을 때의 로그는 GameManager가 찍는다.

        if (pickupSound == null)
        {
            AudioManager.Instance?.PlayPickup();
        }
        else
        {
            AudioManager.Instance?.PlaySfx(pickupSound);
        }
        Debug.Log($"{name}: 몬스터 시체를 인벤토리에 담았다");

        gameObject.SetActive(false);
    }
}
