using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>ShelterScene의 유일한 출구 - 여기서 상호작용하는 것 자체가 "오늘"을 마감하는 행동이다
/// (예전에는 별도의 NextDayButton UI가 이 역할을 맡았는데, 출구를 하나로 합치면서 여기로 옮겼다).
/// 목표 달성 여부를 판정해서 성공하면 트럭씬으로 넘어간다. 실패(목표 미달) 자체는 이제 TruckScene의
/// 소파 허브(TruckHubPoint)에서 보호소에 들어가기도 전에 미리 걸러지므로, 여기서 EvaluateCycleEnd()가 실패로
/// 나오는 경우는 정상적인 플레이에서는 일어나지 않는다 - 그래도 ShelterSettlement는 방어적으로 그
/// 경우에도 게임 오버 패널을 띄우고 false를 반환해 씬 전환을 막는다.
/// 마지막 사이클까지 목표 달성과 함께 완주해 게임을 클리어한 뒤에는(IsGameWon) 게임 오버와 동일하게
/// "다시 시작"으로만 재개된다 - EvaluateCycleEnd를 다시 호출하지 않으므로 클리어 화면을 띄운 뒤
/// 이 지점을 다시 눌러도 보너스가 중복 지급되지 않는다.</summary>
public class ShelterExitPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 2.1f;
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private ShelterSettlement shelterSettlement;

    private Transform player;

    // 오늘을 이미 마감해서(FinishCycle) Day 클리어 패널의 확인 버튼을 기다리는 중이면 true. 그 사이 E를 또
    // 누르면 FinishCycle이 한 번 더 불려 다음 사이클이 빈 상태로 판정(=게임 오버)되므로 입력을 막는다.
    private bool awaitingDayClearConfirm;

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

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange && InteractionFocus.TryFocus(this, distance);
        bool gameEnded = GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsGameWon);
        if (awaitingDayClearConfirm) inRange = false;

        // 범위 안에 있는 동안만 매 프레임 갱신한다 - 범위를 벗어나거나 Day 클리어 확인을 기다리는 동안에는
        // 호출이 끊겨서 자막이 저절로 사라진다.
        if (inRange) SubtitleManager.Show(gameEnded ? "E를 눌러 재시작" : "E를 눌러 다음날로", SubtitleManager.WhileInRange);

        if (!inRange) return;

        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (gameEnded)
        {
            if (!SceneTransitionGuard.TryBeginTransition()) return;
            // 클리어/게임 오버 패널이 timeScale을 0으로 멈춰둔 상태다 - 패널 버튼과 똑같이 되돌려야 한다.
            Time.timeScale = 1f;
            GameManager.Instance?.ResetGame();
            SceneManager.LoadScene(truckSceneName);
            return;
        }

        // 목표 달성 여부를 여기서 처음 판정한다(보호소에서 정산을 몇 번 했는지는 상관없다). 결과 패널
        // (Day 클리어/게임 클리어/게임 오버)이 뜨면 ShelterSettlement가 false를 반환하고, 그 패널의 버튼이
        // 다음 씬으로 넘긴다 - 여기서는 패널을 볼 수 있게 머문다.
        bool canProceed = shelterSettlement == null || shelterSettlement.EvaluateCycleEnd();
        if (!canProceed)
        {
            bool endedNow = GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsGameWon);
            awaitingDayClearConfirm = !endedNow;
            if (awaitingDayClearConfirm) SubtitleManager.Hide();
            return;
        }

        if (!SceneTransitionGuard.TryBeginTransition()) return;
        SceneManager.LoadScene(truckSceneName);
    }
}
