using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>G키로 현재 선택된 "일반" 인벤토리 슬롯(1~5)의 아이템을 플레이어가 바라보는 방향
/// 바닥에 내려놓고 슬롯을 비운다. 무기 슬롯(Q)은 GameManager.TryDropSelectedItem이 애초에
/// 일반 슬롯 배열만 다루므로 대상이 되지 않는다 - 클래스에 고정된 장비라 버릴 수 없다.</summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerItemDropper : MonoBehaviour
{
    [SerializeField] private float dropDistance = 1f;

    [Tooltip("동물을 원래 행동 종류(AnimalBehaviorKind)에 맞게 되살리기 위한 프리팹들")]
    [SerializeField] private AnimalRescue droppedWanderAnimalPrefab;
    [SerializeField] private AnimalRescue droppedFleeAnimalPrefab;
    [SerializeField] private AnimalRescue droppedSoundFleeAnimalPrefab;

    [SerializeField] private GadgetPickup droppedMinePickupPrefab;
    [SerializeField] private GadgetPickup droppedBombPickupPrefab;

    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.gKey.wasPressedThisFrame) return;
        if (GameManager.Instance == null) return;

        if (!GameManager.Instance.TryDropSelectedItem(out InventorySlotData dropped)) return;

        Vector3 position = transform.position + (Vector3)(playerMovement.LookDirection * dropDistance);

        switch (dropped.Type)
        {
            case InventoryItemType.Animal:
                AnimalRescue animalPrefab = dropped.AnimalKind switch
                {
                    AnimalBehaviorKind.Flee => droppedFleeAnimalPrefab,
                    AnimalBehaviorKind.SoundFlee => droppedSoundFleeAnimalPrefab,
                    _ => droppedWanderAnimalPrefab
                };
                if (animalPrefab != null)
                {
                    Instantiate(animalPrefab, position, Quaternion.identity).SetWeight(dropped.AnimalWeight);
                }
                break;
            case InventoryItemType.Mine:
                if (droppedMinePickupPrefab != null) Instantiate(droppedMinePickupPrefab, position, Quaternion.identity);
                break;
            case InventoryItemType.Bomb:
                if (droppedBombPickupPrefab != null) Instantiate(droppedBombPickupPrefab, position, Quaternion.identity);
                break;
        }

        Debug.Log("아이템을 바닥에 내려놓았습니다");
    }
}
