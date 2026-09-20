using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>G키로 인벤토리에서 꺼내 바닥에 내려놓은 지뢰/폭탄 하나. 몬스터를 노리고 설치하는
/// MineTrap(작동 중인 지뢰)과는 다른, 아직 인벤토리에 들어가지 않은 "줍는 물건" 상태를 나타낸다.
/// AnimalRescue의 줍기 상호작용과 같은 패턴(범위 안에서 E)을 재사용한다.</summary>
public class GadgetPickup : MonoBehaviour
{
    [SerializeField] private InventoryItemType itemType = InventoryItemType.Mine;
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private Text promptText;

    [Tooltip("인벤토리 슬롯에 표시할 아이콘. 비워두면 이 오브젝트의 SpriteRenderer 스프라이트를 자동으로 사용한다. 아직 아이콘이 없다면 비워두면 되고, 그 경우 슬롯은 기존처럼 색상 채움만으로 표시된다.")]
    [SerializeField] private Sprite icon;

    private Transform player;
    private Sprite resolvedIcon;

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

        if (promptText == null)
        {
            GameObject promptObj = GameObject.Find("PromptText");
            if (promptObj != null) promptText = promptObj.GetComponent<Text>();
        }

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        resolvedIcon = icon != null ? icon : (spriteRenderer != null ? spriteRenderer.sprite : null);
    }

    private void Update()
    {
        if (player == null || GameManager.Instance == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (inRange && GameManager.Instance.HasInventorySpace) SharedPrompt.Show(promptText, "E를 눌러 줍기");

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        bool picked = itemType == InventoryItemType.Mine
            ? GameManager.Instance.TryAddMine(resolvedIcon)
            : GameManager.Instance.TryAddBomb(resolvedIcon);

        if (picked) gameObject.SetActive(false);
    }
}
