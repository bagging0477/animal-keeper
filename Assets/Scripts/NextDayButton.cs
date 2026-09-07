using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class NextDayButton : MonoBehaviour
{
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private Text buttonLabel;

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
        }
        else
        {
            GameManager.Instance?.ResetDay();
        }

        SceneManager.LoadScene(truckSceneName);
    }
}
