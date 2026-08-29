using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class NextDayPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
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

        RefreshActive();
    }

    private void Update()
    {
        RefreshActive();
        if (!gameObject.activeSelf) return;
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange) promptText.text = "E를 눌러 다음날로";
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        GameManager.Instance?.AdvanceDay();
    }

    private void RefreshActive()
    {
        bool shouldBeActive = GameManager.Instance == null || GameManager.Instance.Day < GameManager.MaxDay;
        if (gameObject.activeSelf != shouldBeActive)
        {
            gameObject.SetActive(shouldBeActive);
            if (!shouldBeActive && promptText != null) promptText.gameObject.SetActive(false);
        }
    }
}
