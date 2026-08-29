using UnityEngine;
using UnityEngine.UI;

public class TruckSceneUI : MonoBehaviour
{
    [SerializeField] private Text dayText;
    [SerializeField] private Text targetText;

    private void Start()
    {
        int day = GameManager.Instance != null ? GameManager.Instance.Day : 1;
        int target = GameManager.Instance != null ? GameManager.Instance.TargetCount : 3;
        int rescued = GameManager.Instance != null ? GameManager.Instance.RescuedCount : 0;
        int totalWeight = GameManager.Instance != null ? GameManager.Instance.TotalWeight : 0;

        if (dayText != null) dayText.text = $"Day {day}";
        if (targetText != null) targetText.text = $"적재량: {rescued}/{target}마리, 총 {totalWeight}Kg";
    }
}
