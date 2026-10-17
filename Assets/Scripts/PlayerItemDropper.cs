using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>G키로 현재 선택된 "일반" 인벤토리 슬롯(숫자키 칸)의 아이템을 플레이어가 바라보는 방향
/// 바닥에 내려놓고 슬롯을 비운다. 무기 슬롯(Q)은 GameManager.TryDropSelectedItem이 애초에
/// 일반 슬롯 배열만 다루므로 대상이 되지 않는다 - 클래스에 고정된 장비라 버릴 수 없다.</summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerItemDropper : MonoBehaviour
{
    [SerializeField] private float dropDistance = 1f;

    [Tooltip("원본 프리팹을 모르는 동물(씬에 직접 배치한 동물 등)을 원래 행동 종류(AnimalBehaviorKind)에 맞게 되살리기 위한 대체 프리팹들. " +
        "맵에서 스폰된 동물은 자기 원본 프리팹(같은 종)으로 되살아난다.")]
    [SerializeField] private AnimalRescue droppedWanderAnimalPrefab;
    [SerializeField] private AnimalRescue droppedFleeAnimalPrefab;
    [SerializeField] private AnimalRescue droppedSoundFleeAnimalPrefab;

    [SerializeField] private GadgetPickup droppedMinePickupPrefab;
    [SerializeField] private GadgetPickup droppedBombPickupPrefab;

    [Tooltip("몬스터 시체(늑대)를 내려놓을 때 만드는 프리팹. 사망 애니메이션 없이 쓰러진 모습으로 놓이고 E로 다시 주울 수 있다.")]
    [SerializeField] private MonsterCorpsePickup droppedMonsterCorpsePrefab;

    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb == null || !kb.gKey.wasPressedThisFrame) return;
        if (GameManager.Instance == null) return;

        if (!GameManager.Instance.TryDropSelectedItem(out InventorySlotData dropped)) return;

        Vector3 position = transform.position + (Vector3)(playerMovement.LookDirection * dropDistance);

        switch (dropped.Type)
        {
            case InventoryItemType.Animal:
                // MonsterCorpse(몬스터 시체)는 걸어다니는 동물로 되살리지 않고, 같은 자리에 쓰러진 시체로 놓는다.
                if (dropped.AnimalKind == AnimalBehaviorKind.MonsterCorpse)
                {
                    DropMonsterCorpse(dropped.AnimalId, position);
                    break;
                }
                AnimalRescue animalPrefab = dropped.AnimalPrefab != null ? dropped.AnimalPrefab
                    : dropped.AnimalKind switch
                    {
                        AnimalBehaviorKind.Flee => droppedFleeAnimalPrefab,
                        AnimalBehaviorKind.SoundFlee => droppedSoundFleeAnimalPrefab,
                        _ => droppedWanderAnimalPrefab
                    };
                if (animalPrefab != null)
                {
                    AnimalRescue droppedAnimal = Instantiate(animalPrefab, position, Quaternion.identity);
                    droppedAnimal.SetWeight(dropped.AnimalWeight);
                    droppedAnimal.SourcePrefab = animalPrefab;
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

    // 몬스터 시체도 동물과 같은 자리(바라보는 방향으로 dropDistance 앞)에 놓는다. 다만 몬스터는 NavMesh(x,z 평면) 위의 오브젝트이고
    // 동물처럼 벽에서 밀어내 줄 2D 충돌체가 없으므로, 플레이어 자리에서 그 지점까지 NavMesh 위로 막히기 전까지만 간다(벽 너머나 벽 속에 놓이지 않게).
    // 시체는 원래 늑대의 Id를 그대로 이어받는다 - 재진입 때 그 Id로 다시 생긴 늑대가 버린 자리의 시체로 돌아간다(MonsterCorpsePickup).
    private void DropMonsterCorpse(string monsterId, Vector2 position)
    {
        if (droppedMonsterCorpsePrefab == null) return;

        Vector3 navTarget = new Vector3(position.x, 0f, position.y);
        Vector3 navFrom = new Vector3(transform.position.x, 0f, transform.position.y);
        if (NavMesh.SamplePosition(navFrom, out NavMeshHit from, CorpseNavSnapRadius, NavMesh.AllAreas) &&
            NavMesh.Raycast(from.position, navTarget, out NavMeshHit edge, NavMesh.AllAreas))
        {
            navTarget = edge.position;
        }
        // NavMesh 표면 높이에 맞춰 둔다 - 높이가 다르면 늑대가 생긴 직후 NavMeshAgent가 표면으로 붙으며 위치가 바뀌어,
        // 여기서 맞춘 시체 그림(Visual 자식)이 같이 밀린다.
        if (NavMesh.SamplePosition(navTarget, out NavMeshHit onMesh, CorpseNavSnapRadius, NavMesh.AllAreas)) navTarget = onMesh.position;

        MonsterCorpsePickup corpse = Instantiate(droppedMonsterCorpsePrefab, navTarget, Quaternion.identity);
        corpse.PlaceDroppedCorpse(monsterId);
    }

    // 플레이어 자리에서 가장 가까운 NavMesh를 찾는 반경 - 몬스터 스폰 스냅(VillageMapGenerator.SpawnMonsterAt)과 같은 값.
    private const float CorpseNavSnapRadius = 1.5f;
}
