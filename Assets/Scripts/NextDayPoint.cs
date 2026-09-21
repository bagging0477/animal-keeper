using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>TruckScene 필드에서 보호소로 가는 지점 - 부상으로 마을 탐색이 막혔을 때도 이 지점을 통해
/// 보호소로 가서 회복/정산할 수 있다. "구조시작" 지점(TruckStartPoint)과 목적지를 완전히 분리해서,
/// 목표를 막 달성한 순간 우연히 여기로 넘어와 버리는(예전 토글 방식의 문제) 일이 없게 한다.
/// 목표 달성에 성공했을 때의 최종 판정(사이클 증가, 체력 회복, 보너스 마일리지)은 여전히 ShelterScene의
/// 출구(ShelterExitPoint)에서만 일어나지만, 실패(오늘 구조 마릿수 미달)만큼은 ShelterScene에 들어가지 않고도
/// 이미 확정된 결과이므로 여기서 미리 걸러낸다 - 그래야 보호소까지 걸어 들어갔다가 출구에서야 게임 오버를
/// 보는 대신, 보호소에 들어가기 전에 곧바로 게임 오버 화면을 볼 수 있다.</summary>
public class NextDayPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private string shelterSceneName = "ShelterScene";
    [SerializeField] private Text promptText;
    [SerializeField] private GameOverPanel gameOverPanel;

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

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange) promptText.text = "E를 눌러 보호소로 이동";
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
        {
            // 이미 실패가 확정되어 게임 오버 화면이 떠 있는 상태 - 여기서는 재시작을 처리하지 않는다
            // (그건 GameOverPanel의 버튼 몫이다), 그렇다고 FinishCycle을 또 불러 사이클을 헛돌리지도 않는다.
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
