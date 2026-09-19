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

    public int Weight { get; private set; }

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

    private void Awake()
    {
        Id = BuildHierarchyId();
        Weight = Random.Range(minWeight, maxWeight + 1); // inclusive
        BehaviorKind = GetComponent<AnimalSoundFlee>() != null ? AnimalBehaviorKind.SoundFlee
            : GetComponent<AnimalFlee>() != null ? AnimalBehaviorKind.Flee
            : AnimalBehaviorKind.Wander;

        if (GameManager.Instance != null && GameManager.Instance.IsAnimalRescuedToday(Id))
        {
            gameObject.SetActive(false);
        }
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

    private void Start()
    {
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
        if (!GameManager.Instance.TryAddAnimal(Id, Weight, BehaviorKind)) return; // 인벤토리가 가득 찼을 때의 로그는 GameManager가 찍는다.

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
        gameObject.SetActive(false);
    }
}
