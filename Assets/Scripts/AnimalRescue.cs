using UnityEngine;
using UnityEngine.InputSystem;

public class AnimalRescue : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;

    public bool IsRescued { get; private set; }

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
    }

    private void Update()
    {
        if (IsRescued || player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            Rescue();
        }
    }

    private void Rescue()
    {
        IsRescued = true;
        Debug.Log($"{name}: 구조 완료");
        gameObject.SetActive(false);
    }
}
