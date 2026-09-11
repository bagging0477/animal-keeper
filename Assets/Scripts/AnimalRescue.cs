using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(AnimalSleep))]
public class AnimalRescue : MonoBehaviour
{
    private const int MaxHeldAnimals = 4;

    private static readonly Vector2[] SlotOffsets =
    {
        new Vector2(0.4f, 0.4f),
        new Vector2(-0.4f, 0.4f),
        new Vector2(0.4f, -0.4f),
        new Vector2(-0.4f, -0.4f),
    };

    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private int minWeight = 1;
    [SerializeField] private int maxWeight = 10;
    [SerializeField] private Text promptText;

    public bool IsHeld { get; private set; }
    public bool IsCompleted { get; private set; }
    public int Weight { get; private set; }

    /// <summary>계층 경로 기반 안정적 식별자. GameObject.name만 쓰면 서로 다른 부모 아래
    /// 이름이 같은 동물(맵에 그룹별로 복제 배치할 때 흔함)이 서로의 구조 기록을 덮어쓸 수 있다.</summary>
    public string Id { get; private set; }

    private Transform player;
    private int heldSlot;

    private void Awake()
    {
        Id = BuildHierarchyId();
        Weight = Random.Range(minWeight, maxWeight + 1); // inclusive

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
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }
    }

    private void Update()
    {
        if (player == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        if (IsHeld) return;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;
        bool canPickUp = inRange && CountHeldAnimals() < MaxHeldAnimals;

        if (canPickUp) SharedPrompt.Show(promptText, "E를 눌러 구조하세요");

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame && CountHeldAnimals() < MaxHeldAnimals)
        {
            PickUp();
        }
    }

    private static int CountHeldAnimals()
    {
        int count = 0;
        foreach (AnimalRescue animal in FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude))
        {
            if (animal.IsHeld) count++;
        }
        return count;
    }

    private void LateUpdate()
    {
        if (IsHeld && player != null)
        {
            transform.position = (Vector2)player.position + SlotOffsets[heldSlot];
        }
    }

    private void PickUp()
    {
        heldSlot = CountHeldAnimals();
        IsHeld = true;
        AudioManager.Instance?.PlayPickup();
        Debug.Log($"{name}: 플레이어가 동물을 들었다 ({heldSlot + 1}/{MaxHeldAnimals})");
    }

    public void CompleteRescue()
    {
        if (IsCompleted || !IsHeld) return;

        IsCompleted = true;
        IsHeld = false;
        Debug.Log("구조 성공");
        gameObject.SetActive(false);
    }

    public void Drop()
    {
        if (!IsHeld) return;

        IsHeld = false;
    }
}
