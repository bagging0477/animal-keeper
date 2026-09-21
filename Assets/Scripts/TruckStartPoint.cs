using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>TruckScene에서 마을 탐색을 시작하는 지점. 1일 주기(예전 3일 주기의 흔적인 "Day 3에만
/// 보호소" 게이트는 없다)에서는 항상 VillageScene으로만 보낸다 - 여러 번 자유롭게 왕복 가능하다.
/// 보호소로 가는 것은 이 지점이 아니라 별도의 "다음날로" 지점(NextDayPoint)이 전담한다 - 두 목적지를
/// 한 지점에서 토글로 제시했더니, 목표를 막 달성한 순간 우연히 보호소 차례가 걸려 "저절로 보호소로
/// 넘어가는 버그"처럼 보이는 문제가 있었다.</summary>
public class TruckStartPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private string villageSceneName = "VillageScene";
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

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange)
            {
                promptText.text = isDown
                    ? "부상으로 이동할 수 없습니다. 다음 날로 이동해주세요"
                    : "E를 눌러 구조 시작";
            }
        }

        if (!inRange || isDown) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        SceneManager.LoadScene(villageSceneName);
    }
}
