using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(AnimalSleep))]
public class AnimalRescue : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private int minWeight = 1;
    [SerializeField] private int maxWeight = 10;
    [SerializeField] private Text promptText;

    [Tooltip("이 동물을 구조할 때 재생할 픽업음. 비워두면 AudioManager의 기본 픽업음을 사용한다.")]
    [SerializeField] private AudioClip pickupSound;

    [Tooltip("체크하면 pickupSound 전체가 아니라 아래 구간만 잘라서 재생한다.")]
    [SerializeField] private bool playPickupSoundSegment;
    [SerializeField] private float pickupSoundStartTime = 7f;
    [SerializeField] private float pickupSoundEndTime = 8f;

    [Tooltip("인벤토리 슬롯에 표시할 아이콘. 비워두면 이 동물의 Idle 첫 프레임을 자동으로 사용한다.")]
    [SerializeField] private Sprite inventoryIcon;

    public int Weight { get; private set; }

    /// <summary>인벤토리 슬롯에 그릴 아이콘. inventoryIcon이 지정돼 있으면 그것을, 아니면 이 동물의
    /// 애니메이터(SpriteSheetAnimator/SimpleFrameAnimator)에서 Idle 첫 프레임을 자동으로 가져온다.</summary>
    public Sprite InventoryIcon { get; private set; }

    /// <summary>G키로 인벤토리에서 다시 꺼내 바닥에 내려놓을 때, 원래 갖고 있던 무게를 그대로
    /// 물려준다 - 이게 없으면 Awake에서 새로 무작위 굴림을 해서 내려놓을 때마다 무게가 바뀐다.</summary>
    public void SetWeight(int weight) => Weight = weight;

    /// <summary>계층 경로 기반 안정적 식별자. GameObject.name만 쓰면 서로 다른 부모 아래
    /// 이름이 같은 동물(맵에 그룹별로 복제 배치할 때 흔함)이 서로의 구조 기록을 덮어쓸 수 있다.</summary>
    public string Id { get; private set; }

    /// <summary>이 동물이 원래 어떤 행동 프리팹이었는지. 인벤토리에 담을 때 함께 기록해둬야
    /// G로 다시 꺼낼 때 같은 종류(도망/소리 반응 등)로 되살릴 수 있다 - AnimalSoundFlee가
    /// AnimalFlee를 상속하므로 더 구체적인 타입부터 확인한다.</summary>
    public AnimalBehaviorKind BehaviorKind { get; private set; }

    private Transform player;

    // OnDestroy에서 "세계에 남아있던(주운 적 없는) 동물인지"를 gameObject.activeInHierarchy로 판단하지
    // 않고 직접 관리한다 - 씬 언로드로 인한 파괴 시퀀스 도중에는 activeInHierarchy가 실제 상태와 다르게
    // (예: 아직 세계에 남아 활동 중이던 동물인데도 false로) 나올 수 있어서, 저장해야 할 위치를
    // OnDestroy가 조용히 건너뛰는 것처럼 보이는 문제가 있었다.
    private bool isRemovedFromField;

    // Weight/BehaviorKind는 GameObject 이름에 의존하지 않으므로 원래대로 Awake에서 굴린다 - 특히
    // Weight를 Start로 옮기면, PlayerItemDropper가 Instantiate 직후 동기적으로 호출하는 SetWeight()가
    // 먼저 실행된 뒤 나중에 실행되는 Start()의 무작위 굴림이 그 값을 덮어써 버리는 회귀가 생긴다.
    private void Awake()
    {
        Weight = Random.Range(minWeight, maxWeight + 1); // inclusive
        BehaviorKind = GetComponent<AnimalSoundFlee>() != null ? AnimalBehaviorKind.SoundFlee
            : GetComponent<AnimalFlee>() != null ? AnimalBehaviorKind.Flee
            : AnimalBehaviorKind.Wander;
    }

    private string BuildHierarchyId()
    {
        var path = new System.Text.StringBuilder(name);
        for (Transform t = transform.parent; t != null; t = t.parent)
        {
            path.Insert(0, "/").Insert(0, t.name);
        }
        return path.ToString();
    }

    // Id 계산(과 그에 기반한 재구조/보유 여부 판정)은 Awake가 아니라 여기 Start에서 한다 -
    // VillageMapGenerator는 동물을 Instantiate한 "다음"에 이름을 슬롯 인덱스로 바꾸는데(SpawnAnimal
    // 참고), Instantiate는 내부적으로 Awake를 동기적으로 먼저 실행하므로 Awake 시점의 gameObject.name은
    // 아직 바뀌기 전(원래 프리팹 이름)이다. Id를 Awake에서 계산하면 VillageMapGenerator가 미리 계산한
    // predictedId(바뀐 이름 기준)와 절대 일치하지 않아, 저장해둔 위치를 영영 못 찾는 버그로 이어진다.
    // Start는 그 프레임의 모든 Awake(이름 변경 포함)가 끝난 뒤 실행되므로 여기서 계산해야 이름이
    // 최종 확정된 뒤의 값을 쓴다.
    private void Start()
    {
        Id = BuildHierarchyId();

        // 오늘 이미 납품했거나(IsAnimalRescuedToday), 아직 납품 전이지만 인벤토리에 들고 있는 중(IsAnimalCurrentlyHeld)
        // 이면 다시 스폰하지 않는다 - 후자를 빼먹으면 트럭에 들렀다가(납품 안 하고) 마을로 돌아왔을 때
        // 맵이 새로 생성되면서 이미 주운 동물이 또 나타나는 버그가 생긴다.
        if (GameManager.Instance != null &&
            (GameManager.Instance.IsAnimalRescuedToday(Id) || GameManager.Instance.IsAnimalCurrentlyHeld(Id)))
        {
            isRemovedFromField = true;
            gameObject.SetActive(false);
            return;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;

            // 플레이어가 동물과 부딪혀 밀어내지 않고 그냥 겹쳐 지나갈 수 있게, 이 둘의 콜라이더끼리만
            // 물리 충돌을 끈다(레이어 전체를 끄는 게 아니라 이 쌍만 - 동물은 벽/장애물과는 여전히
            // 충돌해야 한다). AnimalRescue는 세 동물 프리팹 모두에 붙어있어 한 곳에서 처리하기 좋다.
            Collider2D myCollider = GetComponent<Collider2D>();
            Collider2D playerCollider = playerObj.GetComponent<Collider2D>();
            if (myCollider != null && playerCollider != null)
            {
                Physics2D.IgnoreCollision(myCollider, playerCollider, true);
            }
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }

        // Procedurally spawned animals aren't wired to the scene's shared prompt Text in the
        // Inspector, so fall back to finding it by name.
        if (promptText == null)
        {
            GameObject promptObj = GameObject.Find("PromptText");
            if (promptObj != null) promptText = promptObj.GetComponent<Text>();
        }

        // 다른 컴포넌트의 Awake(예: SpriteSheetAnimator의 프레임 슬라이싱)가 모두 끝난 뒤인 Start
        // 시점에 골라야 idleFrames가 비어있지 않다.
        InventoryIcon = inventoryIcon != null ? inventoryIcon : ResolveDefaultIcon();
    }

    private Sprite ResolveDefaultIcon()
    {
        SpriteSheetAnimator sheetAnimator = GetComponent<SpriteSheetAnimator>();
        if (sheetAnimator != null && sheetAnimator.IdleFirstFrame != null) return sheetAnimator.IdleFirstFrame;

        SimpleFrameAnimator frameAnimator = GetComponent<SimpleFrameAnimator>();
        if (frameAnimator != null && frameAnimator.IdleSprite != null) return frameAnimator.IdleSprite;

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        return spriteRenderer != null ? spriteRenderer.sprite : null;
    }

    private void Update()
    {
        if (player == null || GameManager.Instance == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (inRange && GameManager.Instance.HasInventorySpace) SharedPrompt.Show(promptText, "E를 눌러 구조하세요");

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            PickUp();
        }
    }

    private void PickUp()
    {
        if (!GameManager.Instance.TryAddAnimal(Id, Weight, BehaviorKind, InventoryIcon)) return; // 인벤토리가 가득 찼을 때의 로그는 GameManager가 찍는다.

        if (pickupSound == null)
        {
            AudioManager.Instance?.PlayPickup();
        }
        else if (playPickupSoundSegment)
        {
            AudioManager.Instance?.PlaySfxSegment(pickupSound, pickupSoundStartTime, pickupSoundEndTime);
        }
        else
        {
            AudioManager.Instance?.PlaySfx(pickupSound);
        }
        Debug.Log($"{name}: 인벤토리에 동물을 담았다");

        // 인벤토리에 데이터(무게, Id)만 옮겨졌으므로 원본 오브젝트는 더 이상 필요 없다 - 세계에서 치운다.
        isRemovedFromField = true;
        gameObject.SetActive(false);
    }

    // VillageScene을 나갈 때(트럭으로 이동하거나 부상으로 강제 복귀할 때) 씬이 언로드되면서 이
    // 오브젝트도 함께 파괴되는데, 그 직전에 Unity가 모든 오브젝트의 OnDestroy를 호출해준다 - 이 시점엔
    // 아직 transform이 살아있으므로 각자 자기 위치를 안전하게 GameManager(DontDestroyOnLoad)에 남길
    // 수 있다. 이미 주웠거나(비활성화된) 동물은 세계 안의 위치가 의미 없으므로 건드리지 않는다.
    private void OnDestroy()
    {
        if (GameManager.Instance == null || isRemovedFromField) return;

        AnimalFlee flee = GetComponent<AnimalFlee>();
        bool wasFleeing = flee != null && flee.IsAlarmed;
        GameManager.Instance.SaveAnimalFieldState(Id, transform.position, wasFleeing);
        Debug.Log($"[FieldState] 저장: {Id} @ {transform.position}, fleeing={wasFleeing}");
    }
}
