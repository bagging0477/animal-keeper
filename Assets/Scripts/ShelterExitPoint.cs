using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>ShelterScene의 유일한 출구 - 여기서 상호작용하는 것 자체가 "오늘"을 마감하는 행동이다
/// (예전에는 별도의 NextDayButton UI가 이 역할을 맡았는데, 출구를 하나로 합치면서 여기로 옮겼다).
/// 목표 달성 여부를 판정해서 성공하면 트럭씬으로 넘어간다. 실패(목표 미달) 자체는 이제 TruckScene의
/// NextDayPoint에서 보호소에 들어가기도 전에 미리 걸러지므로, 여기서 EvaluateCycleEnd()가 실패로
/// 나오는 경우는 정상적인 플레이에서는 일어나지 않는다 - 그래도 ShelterSettlement는 방어적으로 그
/// 경우에도 게임 오버 패널을 띄우고 false를 반환해 씬 전환을 막는다.
/// 마지막 사이클까지 목표 달성과 함께 완주해 게임을 클리어한 뒤에는(IsGameWon) 게임 오버와 동일하게
/// "다시 시작"으로만 재개된다 - EvaluateCycleEnd를 다시 호출하지 않으므로 클리어 화면을 띄운 뒤
/// 이 지점을 다시 눌러도 보너스가 중복 지급되지 않는다.</summary>
public class ShelterExitPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.4f;
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private ShelterSettlement shelterSettlement;
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

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;
        bool gameEnded = GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsGameWon);

        // 이 프롬프트는 이 지점 전용 Text라 SharedPrompt(여러 지점이 화면에 하나뿐인 공용 문구 UI를
        // 나눠 쓸 때 쓰는 유틸)를 쓰면 안 된다 - 같은 프레임에 다른 지점이 먼저 그 공용 게이트를
        // 건드리면 이 프롬프트의 숨김 호출이 밀려서, 한 번 뜬 뒤로 범위를 벗어나도 계속 화면에
        // 남아있는 버그가 있었다. 대신 매 프레임 자기 범위만 보고 직접 켜고 끈다.
        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange) promptText.text = gameEnded ? "E를 눌러 재시작" : "E를 눌러 다음날로";
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (gameEnded)
        {
            GameManager.Instance?.ResetGame();
            SceneManager.LoadScene(truckSceneName);
            return;
        }

        // 목표 달성 여부를 여기서 처음 판정한다(보호소에서 정산을 몇 번 했는지는 상관없다). 미달성이면
        // ShelterSettlement가 게임 오버 패널을 띄우고 false를 반환하므로, 트럭씬으로 넘어가지 않고
        // 그 패널을 볼 수 있게 여기 머문다.
        bool canProceed = shelterSettlement == null || shelterSettlement.EvaluateCycleEnd();
        if (!canProceed) return;

        SceneManager.LoadScene(truckSceneName);
    }
}
