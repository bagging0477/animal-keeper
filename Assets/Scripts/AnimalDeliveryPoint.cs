using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>TruckScene 안의 동물 납품 케이지. 인벤토리에서 현재 선택된(1~5 숫자키) 슬롯이
/// 동물일 때만 E로 납품한다 - 지뢰/폭탄과 동일하게, 선택되지 않은 슬롯의 동물은 이 지점에서
/// 상호작용해도 납품되지 않는다.</summary>
public class AnimalDeliveryPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.5f;
    [SerializeField] private Text promptText;

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

        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (player == null || GameManager.Instance == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange) return;

        bool selectedIsAnimal = GameManager.Instance.GetSlot(GameManager.Instance.SelectedSlotIndex).Type == InventoryItemType.Animal;
        if (selectedIsAnimal) SharedPrompt.Show(promptText, "E를 눌러 동물 납품하기");

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (GameManager.Instance.TryDeliverSelectedAnimal())
        {
            Debug.Log("동물을 납품했습니다");
        }
    }
}
