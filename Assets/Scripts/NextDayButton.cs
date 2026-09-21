using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class NextDayButton : MonoBehaviour
{
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private Text buttonLabel;
    [SerializeField] private ShelterSettlement shelterSettlement;

    private void Start()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
        RefreshLabel();
    }

    private void Update()
    {
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (buttonLabel == null) return;

        bool gameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;
        buttonLabel.text = gameOver ? "재시작" : "다음 날로";
    }

    private void OnClick()
    {
        bool gameOver = GameManager.Instance != null && GameManager.Instance.IsGameOver;

        if (gameOver)
        {
            GameManager.Instance?.ResetGame();
            SceneManager.LoadScene(truckSceneName);
            return;
        }

        // 오늘을 실제로 마감하는 시점 - 목표 달성 여부를 여기서 처음 판정한다(보호소에 몇 번을
        // 들렀는지는 상관없다). 미달성이면 ShelterSettlement가 게임 오버 패널을 띄우고 false를
        // 반환하므로, 트럭씬으로 넘어가지 않고 그 패널을 볼 수 있게 여기 머문다.
        bool canProceed = shelterSettlement == null || shelterSettlement.EvaluateCycleEnd();
        if (!canProceed) return;

        SceneManager.LoadScene(truckSceneName);
    }
}
