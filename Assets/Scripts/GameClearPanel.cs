using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>지정된 사이클 수(GameBalanceConfig.totalCyclesToWin)를 목표 달성과 함께 완주했을 때
/// 뜨는 게임 클리어 화면. GameOverPanel과 동일하게 "다시 시작" 버튼을 눌러야만 재개된다 - 자동으로
/// 다음 사이클로 계속 진행되지 않는다.</summary>
public class GameClearPanel : MonoBehaviour
{
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private Button restartButton;
    [SerializeField] private Text messageText;

    private void Awake()
    {
        if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
    }

    public void Show(string message)
    {
        if (messageText != null && !string.IsNullOrEmpty(message)) messageText.text = message;
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        // GameOverPanel과 동일하게, 패널이 떠 있는 동안 플레이어 이동 등이 그대로 먹히지 않도록 멈춘다.
        Time.timeScale = 0f;
    }

    private void OnRestart()
    {
        if (!SceneTransitionGuard.TryBeginTransition()) return;

        Time.timeScale = 1f;
        GameManager.Instance?.ResetGame();
        SceneManager.LoadScene(truckSceneName);
    }
}
