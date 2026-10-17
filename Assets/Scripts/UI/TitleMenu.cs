using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TitleScene의 PLAY / SETTING / EXIT 버튼 처리. 각 Button의 OnClick에 연결해서 쓴다.
/// PLAY는 항상 새 게임으로 시작한다 - 게임 중에 타이틀로 돌아왔다가 다시 PLAY를 누르면, 씬을 넘어 살아있는
/// GameManager의 진행 상태(마일리지, 클래스, 인벤토리 등)를 ResetGame()으로 처음 상태로 되돌린 뒤
/// 시작 클래스 선택(ClassSelectScene)부터 다시 시작한다.
/// </summary>
public class TitleMenu : MonoBehaviour
{
    [SerializeField] private string classSelectSceneName = "ClassSelectScene";

    [Tooltip("SETTING 버튼으로 여닫는 설정 패널. 지금은 빈 틀만 있고 기능은 나중에 추가한다.")]
    [SerializeField] private GameObject settingsPanel;

    private void Awake()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void OnPlay()
    {
        if (!SceneTransitionGuard.TryBeginTransition()) return;
        if (GameManager.Instance != null) GameManager.Instance.ResetGame();
        SceneManager.LoadScene(classSelectSceneName);
    }

    public void OnSetting()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void OnCloseSetting()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
