using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>VillageScene 안에서 트럭으로 돌아가는 지점. 인벤토리 내용과 무관하게 E 한 번으로
/// 바로 TruckScene으로 넘어간다 - 구조한 동물의 실제 납품은 TruckScene 안의 케이지
/// (AnimalDeliveryPoint)에서 슬롯을 선택해 따로 처리한다.</summary>
public class TruckPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.8f;
    [SerializeField] private string truckSceneName = "TruckScene";
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

        if (inRange) SharedPrompt.Show(promptText, "E를 눌러 트럭에 타기");

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        SceneManager.LoadScene(truckSceneName);
    }
}
