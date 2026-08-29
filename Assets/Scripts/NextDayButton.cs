using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class NextDayButton : MonoBehaviour
{
    [SerializeField] private string truckSceneName = "TruckScene";

    private void Start()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        GameManager.Instance?.ResetDay();
        SceneManager.LoadScene(truckSceneName);
    }
}
