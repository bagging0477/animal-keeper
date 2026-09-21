using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>1일 주기(예전 3일 주기의 흔적인 "Day 3에만 보호소" 게이트는 없다)에서는 마을 탐색과
/// 보호소 이동을 이 지점 하나가 둘 다 제공해야 한다. E키 하나로 두 목적지를 동시에 표현할 수는
/// 없으므로, GameManager.NextTruckDestinationIsShelter를 상호작용할 때마다 뒤집어서 두 목적지를
/// 번갈아 제시한다 - 방문할 때마다 프롬프트가 "마을로 탐색"/"보호소로 이동" 사이를 오간다.</summary>
public class TruckStartPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private string villageSceneName = "VillageScene";
    [SerializeField] private string shelterSceneName = "ShelterScene";
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

        bool isDown = GameManager.Instance != null && GameManager.Instance.Health <= 0;
        bool nextIsShelter = GameManager.Instance != null && GameManager.Instance.NextTruckDestinationIsShelter;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange)
            {
                promptText.text = isDown
                    ? "부상으로 이동할 수 없습니다. 다음 날로 이동해주세요"
                    : (nextIsShelter ? "E를 눌러 보호소로 이동" : "E를 눌러 구조 시작");
            }
        }

        if (!inRange || isDown) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        SceneManager.LoadScene(nextIsShelter ? shelterSceneName : villageSceneName);
        GameManager.Instance?.ToggleTruckDestination();
    }
}
