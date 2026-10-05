using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>TruckScene(캠핑카) 소파의 허브 지점. E로 상호작용하면 "다음 날로" 버튼이 있는 메뉴를 띄운다 -
/// 하루를 끝내는 동작이라 문(TruckStartPoint)의 "구조 시작"처럼 E 한 번으로 바로 실행되지 않고, 버튼으로
/// 한 번 더 확인받는다. 예전의 "다음 날로" 전용 지점(NextDayPoint)은 이 허브로 통합됐다. 메뉴가 떠 있는
/// 동안에는 GameOverPanel과 같은 방식으로 시간을 멈춰 플레이어가 메뉴를 띄운 채 걸어가 버리지 않게 한다.</summary>
public class TruckHubPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 2.1f;
    [SerializeField] private string shelterSceneName = "ShelterScene";
    [SerializeField] private string promptMessage = "E를 눌러 소파에서 쉬기";
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private Button nextDayButton;
    [SerializeField] private GameOverPanel gameOverPanel;

    private Transform player;
    private bool menuOpen;

    private void Awake()
    {
        if (nextDayButton != null) nextDayButton.onClick.AddListener(OnNextDay);
        if (menuPanel != null) menuPanel.SetActive(false);
    }

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

    private void OnDisable()
    {
        // 메뉴가 열린 채로 씬이 내려가도 다음 씬이 멈춘 시간을 물려받지 않게 한다.
        if (menuOpen)
        {
            menuOpen = false;
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        Keyboard kb = PauseMenu.IsPaused ? null : Keyboard.current;

        if (menuOpen)
        {
            // 연 것과 같은 E로도 닫을 수 있게 한다 - 여는 입력은 아래 분기에서만 처리하므로 같은 프레임에
            // 열리자마자 닫히는 일은 없다.
            if (kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) CloseMenu();
            return;
        }

        if (player == null) return;

        bool isGameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;
        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange && !isGameOver && InteractionFocus.TryFocus(this, distance);

        if (inRange) SubtitleManager.Show(promptMessage, SubtitleManager.WhileInRange);

        if (!inRange) return;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        OpenMenu();
    }

    private void OpenMenu()
    {
        if (menuPanel == null) return;

        menuOpen = true;
        menuPanel.transform.SetAsLastSibling();
        menuPanel.SetActive(true);
        SubtitleManager.Hide();
        Time.timeScale = 0f;
    }

    private void CloseMenu()
    {
        menuOpen = false;
        if (menuPanel != null) menuPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    /// <summary>보호소로 가서 오늘을 마무리한다. 실패(오늘 구조 마릿수 미달)만큼은 ShelterScene에 들어가지
    /// 않고도 이미 확정된 결과이므로 여기서 미리 걸러낸다 - 그래야 보호소까지 걸어 들어갔다가 출구에서야
    /// 게임 오버를 보는 대신, 보호소에 들어가기 전에 곧바로 게임 오버 화면을 볼 수 있다. 목표 달성에
    /// 성공했을 때의 최종 판정(사이클 증가, 체력 회복, 보너스 마일리지)은 여전히 ShelterScene의
    /// 출구(ShelterExitPoint)에서만 일어난다.</summary>
    private void OnNextDay()
    {
        if (!menuOpen) return;
        CloseMenu();

        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
        {
            // 이미 실패가 확정되어 게임 오버 화면이 떠 있는 상태 - 재시작은 GameOverPanel의 버튼 몫이고,
            // FinishCycle을 또 불러 사이클을 헛돌리지도 않는다.
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.RescuedCount < GameManager.Instance.TargetCount)
        {
            GameManager.Instance.FinishCycle();
            if (gameOverPanel != null) gameOverPanel.Show();
            return;
        }

        if (!SceneTransitionGuard.TryBeginTransition()) return;
        SceneManager.LoadScene(shelterSceneName);
    }
}
