using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>TruckScene 안의 동물 납품 자리(직원 DeliveryClerk). 인벤토리에서 현재 선택된(숫자키) 슬롯이
/// 동물일 때만 E로 납품한다 - 지뢰/폭탄과 동일하게, 선택되지 않은 슬롯의 동물은 이 지점에서
/// 상호작용해도 납품되지 않는다.
/// 범위 안 안내: 동물 칸을 골랐으면 DeliverPrompt, 동물은 있는데 다른 칸을 골랐으면 ChooseSlotPrompt, 동물이 없으면 아무것도 띄우지 않는다.
/// 납품에 성공하면 직원 그림(clerk)의 받기 동작을 한 번 재생한다.</summary>
public class AnimalDeliveryPoint : MonoBehaviour
{
    public const string DeliverPrompt = "E를 눌러 맡기기";
    public const string ChooseSlotPrompt = "숫자키로 동물 칸을 골라 주세요";

    [SerializeField] private float interactionRange = 2.3f;
    [Tooltip("납품에 성공하면 받기 동작을 재생할 직원 그림. 비워 두면 자식에서 찾는다.")]
    [SerializeField] private DeliveryClerkAnimator clerk;

    private Transform player;

    private void Awake()
    {
        if (clerk == null) clerk = GetComponentInChildren<DeliveryClerkAnimator>();
    }

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
        if (player == null || GameManager.Instance == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange || !InteractionFocus.TryFocus(this, distance)) return;

        bool selectedIsAnimal = GameManager.Instance.GetSlot(GameManager.Instance.SelectedSlotIndex).Type == InventoryItemType.Animal;
        if (selectedIsAnimal) SubtitleManager.Show(DeliverPrompt, SubtitleManager.WhileInRange);
        else if (HasAnyAnimal()) SubtitleManager.Show(ChooseSlotPrompt, SubtitleManager.WhileInRange);

        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (GameManager.Instance.TryDeliverSelectedAnimal())
        {
            Debug.Log("동물을 납품했습니다");
            if (clerk != null) clerk.PlayTake();
        }
    }

    private static bool HasAnyAnimal()
    {
        GameManager gm = GameManager.Instance;
        for (int i = 0; i < gm.InventorySlotCount; i++)
            if (gm.GetSlot(i).Type == InventoryItemType.Animal) return true;
        return false;
    }
}
