using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>죽은 몬스터의 시체를 AnimalRescue와 동일한 방식(거리 기반 E키 상호작용)으로 인벤토리에
/// 담을 수 있게 한다. MonsterHealth.Die()가 시체를 씬에 그대로 남겨두는 기존 동작은 건드리지 않고,
/// 그 시체에 한해서만 상호작용을 추가로 얹는다 - 늑대 기반 순찰형 몬스터 전용 컴포넌트이며,
/// 소리반응형(Blood) 몬스터 프리팹에는 붙이지 않아 기존 처치 방식을 그대로 유지한다.
/// 인벤토리 슬롯에는 InventoryItemType.Animal로 담기므로, 그 이후로는 트럭 납품/보호소 무게 정산 등
/// 실제 동물과 완전히 동일한 경로를 그대로 탄다.</summary>
[RequireComponent(typeof(MonsterHealth))]
public class MonsterCorpsePickup : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.8f;
    [SerializeField] private GameBalanceConfig config;

    [Tooltip("이 시체를 주울 때 재생할 픽업음. 비워두면 AudioManager의 기본 픽업음을 사용한다.")]
    [SerializeField] private AudioClip pickupSound;

    [Tooltip("pickupSound의 재생 볼륨. 원본 파일마다 녹음 크기가 달라서 다른 효과음과 체감 크기를 맞추는 보정값이다.")]
    [SerializeField, Range(0f, 1f)] private float pickupSoundVolume = 1f;

    [Tooltip("인벤토리 슬롯에 표시할 아이콘. 비워두면 Visual 자식의 SimpleFrameAnimator가 쓰는 스프라이트 " +
        "시트의 첫 번째 프레임을 자동으로 가져온다 - 시체 자체는 MonsterHealth.deadColor로 틴트되어 " +
        "보이지만, 그건 SpriteRenderer.color(렌더러 틴트)일 뿐 Sprite 애셋 자체는 그대로라서 아이콘은 " +
        "리컬러 없이 원본 색 그대로 나온다.")]
    [SerializeField] private Sprite inventoryIcon;

    // inventoryIcon이 비었을 때 쓰는 시트 첫 프레임. OnValidate가 에디터에서 채운다 (InventoryIconUtility 참고).
    [SerializeField, HideInInspector] private Sprite sheetFirstFrameIcon;

    private MonsterHealth health;
    private Transform player;
    private Sprite resolvedIcon;
    private string id;

    // 주워서 필드에서 치웠거나 이미 납품/보유 중이라 감췄는지. 이때는 OnDestroy에서 시체 자리를 남기지 않는다(AnimalRescue와 같다).
    private bool isRemovedFromField;

    // G로 내려놓아 만든 시체인지(PlaceDroppedCorpse). 이때 id는 계층 경로가 아니라 원래 늑대의 Id다.
    private bool isDroppedCorpse;

    // 인벤토리가 가득 찼다는 안내를 이번에 범위에 들어와 이미 띄웠는지(InventoryFullNotice). 살아 있는 늑대는 Update가 먼저 빠져나가 띄우지 않는다.
    private bool fullNoticeShown;

    // 몬스터마다 다른 무게(ICorpseWeight, 예: 호저)가 있으면 그 값, 없으면 늑대 시체 무게(늑대·박쥐).
    // 내려놓은 시체는 주웠을 때의 무게(인벤토리 칸에 담겨 있던 값)를 그대로 이어받는다(droppedWeight).
    private ICorpseWeight corpseWeight;
    private int droppedWeight;
    public int Weight => droppedWeight > 0 ? droppedWeight : corpseWeight != null ? corpseWeight.CorpseWeight : config != null ? config.wolfCorpseWeight : 15;

    /// <summary>이 몬스터를 만든 원래 프리팹. 스폰하는 쪽(VillageMapGenerator, VillageMonsterSpawner, PlayerItemDropper)이 만들자마자 RememberSource로 넣는다
    /// (동물의 AnimalRescue.SourcePrefab과 같다). 주울 때 인벤토리 칸에 담겨, 내려놓으면 같은 프리팹으로 다시 만든다 - 몬스터 종류마다 따로 처리하지 않는다.</summary>
    public GameObject SourcePrefab { get; set; }

    /// <summary>instance가 시체를 남기는 몬스터(MonsterCorpsePickup)면 원래 프리팹을 기억시킨다. 아니면(소리반응형 등) 아무 일도 없다.</summary>
    public static void RememberSource(GameObject instance, GameObject prefab)
    {
        if (instance != null && instance.TryGetComponent(out MonsterCorpsePickup corpse)) corpse.SourcePrefab = prefab;
    }

    private void Awake()
    {
        health = GetComponent<MonsterHealth>();
        corpseWeight = GetComponent<ICorpseWeight>();
    }

    // 같은 Day 재진입 때 늑대는 같은 시드와 같은 스폰 순서로 같은 Id에 다시 생긴다. 오늘 납품했거나 들고 있는 시체의 늑대는
    // 동물처럼 감추고, 처치만 하고 남겨 둔 늑대는 그 자리의 시체로 되돌린다. 처치하지 않은 늑대는 그대로 새로 생긴 채 둔다.
    // G로 내려놓은 시체(PlaceDroppedCorpse)는 이미 시체이고 Id도 받았으므로 이 확인을 건너뛴다.
    private void Start()
    {
        if (!isDroppedCorpse)
        {
            id = BuildHierarchyId();

            GameManager gm = GameManager.Instance;
            if (gm != null && (gm.IsAnimalRescuedToday(id) || gm.IsAnimalCurrentlyHeld(id)))
            {
                isRemovedFromField = true;
                gameObject.SetActive(false);
                return;
            }
            if (gm != null && gm.TryGetMonsterCorpse(id, out Vector3 corpsePosition)) RestoreCorpse(corpsePosition);
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }

        resolvedIcon = inventoryIcon != null ? inventoryIcon : ResolveDefaultIcon();
    }

    /// <summary>PlayerItemDropper가 G로 내려놓은 시체를 만든 직후(Start 전) 부른다. 원래 늑대의 Id를 그대로 이어받고, 사망 애니메이션 없이
    /// 쓰러진 모습으로 지금 자리에 둔다. 마을을 떠날 때 OnDestroy가 이 자리를 그 Id의 시체 자리로 남겨, 재진입 때 원래 늑대가 여기 시체로 돌아온다.</summary>
    public void PlaceDroppedCorpse(string monsterId, int weight = 0)
    {
        isDroppedCorpse = true;
        id = monsterId;
        if (weight > 0) droppedWeight = weight;
        RestoreCorpse(transform.position);
    }

    // 남겨 둔 시체 자리로 옮기고 바로 시체로 만든다. 죽은 늑대는 MonsterAI.Update가 Visual을 따라 옮기지 않으므로 여기서 한 번 맞춘다
    // (MonsterAI와 같은 매핑: NavMesh x,z → 2D x,y). 옮기는 것은 그림 묶음 전체(루트 바로 아래 Visual)다 - 박쥐·호저처럼 그림이
    // Visual 아래 Body에 있으면 Body만 옮길 경우 그림자·몸이 어긋난다. 늑대는 그림이 Visual 자체라 예전과 같다.
    private void RestoreCorpse(Vector3 position)
    {
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh) agent.Warp(position);
        else transform.position = position;

        SimpleFrameAnimator animator = GetComponentInChildren<SimpleFrameAnimator>();
        if (animator != null)
        {
            Transform visual = animator.transform;
            while (visual.parent != null && visual.parent != transform) visual = visual.parent;
            visual.position = new Vector3(transform.position.x, transform.position.z, 0f);
        }

        health.RestoreAsCorpse();
    }

    // AnimalRescue.Id와 같은 방식(계층 경로)으로 만든다 - VillageMapGenerator가 늑대 이름을 방 안 번호로 붙이므로
    // (GeneratedMap/Room_{gx}_{gy}/PatrolMonster_{i}) 같은 Day 재진입 때도 같은 Id가 나와 납품 기록과 시체 자리를 찾을 수 있다.
    private string BuildHierarchyId()
    {
        var path = new System.Text.StringBuilder(name);
        for (Transform t = transform.parent; t != null; t = t.parent)
        {
            path.Insert(0, "/").Insert(0, t.name);
        }
        return path.ToString();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SimpleFrameAnimator frameAnimator = GetComponentInChildren<SimpleFrameAnimator>(true);
        if (frameAnimator == null || frameAnimator.IdleSprite == null) return;

        Sprite found = InventoryIconUtility.FindSheetFirstFrame(frameAnimator.IdleSprite.texture);
        if (found != null) sheetFirstFrameIcon = found;
    }
#endif

    // 시트 첫 프레임을 아직 못 찾은 경우를 위한 대체 경로.
    private Sprite ResolveDefaultIcon()
    {
        if (sheetFirstFrameIcon != null) return sheetFirstFrameIcon;

        SimpleFrameAnimator frameAnimator = GetComponentInChildren<SimpleFrameAnimator>();
        if (frameAnimator != null && frameAnimator.IdleSprite != null) return frameAnimator.IdleSprite;

        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        return spriteRenderer != null ? spriteRenderer.sprite : null;
    }

    private void Update()
    {
        if (!health.IsDead || player == null || GameManager.Instance == null) return;

        float distance = Vector2.Distance(health.GamePosition, new Vector2(player.position.x, player.position.y));
        bool inRange = distance <= interactionRange && InteractionFocus.TryFocus(this, distance);

        if (inRange && GameManager.Instance.HasInventorySpace) SubtitleManager.Show("E를 눌러 사체를 획득하세요", SubtitleManager.WhileInRange);
        InventoryFullNotice.Update(inRange, ref fullNoticeShown);

        if (!inRange) return;

        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            PickUp();
        }
    }

    private void PickUp()
    {
        if (!GameManager.Instance.TryAddMonsterCorpse(id, Weight, resolvedIcon, SourcePrefab)) return; // 인벤토리가 가득 찼을 때의 로그는 GameManager가 찍는다.
        GameManager.Instance.ForgetMonsterCorpse(id);

        if (pickupSound == null)
        {
            AudioManager.Instance?.PlayPickup();
        }
        else
        {
            AudioManager.Instance?.PlaySfx(pickupSound, pickupSoundVolume);
        }
        Debug.Log($"{name}: 몬스터 시체를 인벤토리에 담았다");

        isRemovedFromField = true;
        gameObject.SetActive(false);
    }

    // VillageScene을 나갈 때(트럭으로 이동하거나 부상으로 강제 복귀할 때) 처치만 하고 줍지 않은 시체의 자리를 남긴다.
    // 살아 있는 늑대는 남기지 않는다 - 재진입 때 예전처럼 새로 생긴다.
    private void OnDestroy()
    {
        // 씬 언로드 중에는 같은 오브젝트의 MonsterHealth가 먼저 파괴됐을 수 있어 Unity의 == null 대신 참조만 본다(IsDead는 C# 값이라 읽을 수 있다).
        if (GameManager.Instance == null || isRemovedFromField || ReferenceEquals(health, null) || !health.IsDead) return;
        // Id는 Start에서 정해진다 - 마을을 불러온 바로 그 프레임에 씬을 떠나 Start 전에 파괴되면 아직 null이라 저장할 키가 없다.
        if (string.IsNullOrEmpty(id)) return;

        GameManager.Instance.SaveMonsterCorpse(id, transform.position);
    }
}
