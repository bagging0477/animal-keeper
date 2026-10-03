using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>보호소 출구에서 오늘 목표를 달성하고 하루를 마감했을 때 뜨는 "Day 클리어" 화면. 확인 버튼을
/// 눌러야 트럭씬으로 넘어간다 - 예전엔 패널을 띄운 바로 그 프레임에 씬을 넘겨서 화면에 보이지도 않았다.
/// 떠 있는 동안은 GameOverPanel처럼 시간을 멈춘다. 이미 FinishCycle로 다음 사이클로 넘어간 상태라,
/// 그 사이에 걸어서 클래스 선택 씬을 오가면 빈 화물로 재정산된 뒤 목표 0마리로 다시 판정되는 등 꼬일 수 있다.</summary>
public class DayClearPanel : MonoBehaviour
{
    [SerializeField] private Button confirmButton;
    [SerializeField] private Text titleText;
    [SerializeField] private string truckSceneName = "TruckScene";

    private void Awake()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
    }

    public void Show(string title)
    {
        if (titleText != null && !string.IsNullOrEmpty(title)) titleText.text = title;
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        Time.timeScale = 0f;
    }

    private void OnConfirm()
    {
        if (!SceneTransitionGuard.TryBeginTransition()) return;

        Time.timeScale = 1f;
        SceneManager.LoadScene(truckSceneName);
    }
}
