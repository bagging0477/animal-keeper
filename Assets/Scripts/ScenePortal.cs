using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>플레이어가 다가가서 E키를 누르면 지정된 씬으로 이동하는 범용 상호작용 지점.
/// ShelterScene ↔ ClassSelectScene처럼 별도 처리 없이 단순히 씬만 전환하면 되는 문/출입구에 쓴다.</summary>
public class ScenePortal : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.4f;
    [SerializeField] private string targetSceneName;
    [SerializeField] private string promptMessage = "E를 눌러 이동";
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
        if (player == null) return;

        SharedPrompt.BeginFrameIfNeeded(promptText);

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (inRange) SharedPrompt.Show(promptText, promptMessage);

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (string.IsNullOrEmpty(targetSceneName)) return;
        if (!SceneTransitionGuard.TryBeginTransition()) return;
        SceneManager.LoadScene(targetSceneName);
    }
}
