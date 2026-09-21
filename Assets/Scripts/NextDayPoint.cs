using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>TruckScene 필드에서 보호소를 거치지 않고도 "오늘"을 마무리할 수 있는 지점 - 부상으로
/// 마을 탐색이 막혔을 때 체력을 회복하는 용도로도 쓰인다. 상호작용 시 정산 여부와 무관하게
/// GameManager.ResetDay()를 호출해 사이클을 하나 진행시킨다 - 보호소 정산은 이후 별도로(구조시작
/// 지점에서 보호소를 골라) 선택적으로 하면 된다.
/// 한 번 쓰면 CycleCount를 무한정 불릴 수 없도록 그 즉시 자신을 숨긴다 - 같은 트럭 방문 동안에는
/// 다시 나타나지 않고, 보호소(ShelterScene)를 한 번 다녀와야(GameManager.ReopenNextDayPoint 참고)
/// 다음 트럭 방문에서 다시 쓸 수 있다.</summary>
public class NextDayPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private Text promptText;

    private Transform player;

    private void Start()
    {
        if (GameManager.Instance != null && !GameManager.Instance.NextDayPointAvailable)
        {
            gameObject.SetActive(false);
            return;
        }

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

        GameManager.Instance?.ResetDay();
        GameManager.Instance?.MarkNextDayPointUsed();

        // 이번 트럭 방문 동안은 즉시 숨긴다 - 씬을 다시 로드하지 않는 한 Update()가 더 돌지 않으므로
        // 반복해서 눌러 사이클을 계속 불리는 것을 막는다.
        if (promptText != null) promptText.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }
}
