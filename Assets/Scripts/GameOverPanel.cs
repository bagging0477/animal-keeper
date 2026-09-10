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
        gameObject.SetActive(true);
    }

    private void OnRestart()
    {
        GameManager.Instance?.ResetGame();
        SceneManager.LoadScene(truckSceneName);
    }
}
