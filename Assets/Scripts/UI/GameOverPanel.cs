using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameOverPanel : MonoBehaviour
{
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private Button restartButton;

    private void Awake()
    {
        if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
    }

    public void Show()
    {
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        // 패널이 떠 있는 동안에도 PlayerMovement 등은 이 상태를 모르고 계속 입력을 처리하므로,
        // 시간을 멈춰 실질적으로 조작이 먹히지 않게 한다. 재시작 시 반드시 1로 되돌려야 한다 -
        // 안 그러면 다음 씬 전체가 얼어붙은 채로 남는다.
        Time.timeScale = 0f;
    }

    private void OnRestart()
    {
        // 패널이 떠 있는 동안 버튼을 여러 번 눌러도(더블클릭 등) ResetGame()/LoadScene이 중복
        // 실행되지 않게 막는다 - Time.timeScale=0이어도 UI 클릭 자체는 여러 프레임에 걸쳐 계속 들어올 수 있다.
        if (!SceneTransitionGuard.TryBeginTransition()) return;

        Time.timeScale = 1f;
        GameManager.Instance?.ResetGame();
        SceneManager.LoadScene(truckSceneName);
    }
}
