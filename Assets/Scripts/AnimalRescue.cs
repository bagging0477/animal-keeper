using UnityEngine;
using UnityEngine.InputSystem;

public class AnimalRescue : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private Vector2 heldOffset = new Vector2(0.4f, 0.4f);
    [SerializeField] private int minWeight = 1;
    [SerializeField] private int maxWeight = 10;

    public bool IsHeld { get; private set; }
    public bool IsCompleted { get; private set; }
    public int Weight { get; private set; }

    private Transform player;

    private void Awake()
    {
        Weight = Random.Range(minWeight, maxWeight + 1); // inclusive

        if (GameManager.Instance != null && GameManager.Instance.IsAnimalRescuedToday(name))
        {
            gameObject.SetActive(false);
        }
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

        if (!IsHeld)
        {
            float distance = Vector2.Distance(transform.position, player.position);
            if (distance > interactionRange) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                PickUp();
            }
        }
    }

    private void LateUpdate()
    {
        if (IsHeld && player != null)
        {
            transform.position = (Vector2)player.position + heldOffset;
        }
    }

    private void PickUp()
    {
        IsHeld = true;
        Debug.Log($"{name}: 플레이어가 동물을 들었다");
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
