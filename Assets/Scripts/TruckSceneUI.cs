using UnityEngine;
using UnityEngine.UI;

public class TruckSceneUI : MonoBehaviour
{
    [SerializeField] private Text dayText;
    [SerializeField] private Text cargoText;
    [SerializeField] private Text targetText;
    [SerializeField] private Image targetFillImage;
    [SerializeField] private Text mileageText;

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        int cycleCount = GameManager.Instance != null ? GameManager.Instance.CycleCount : 1;
        int target = GameManager.Instance != null ? GameManager.Instance.TargetCount : 3;
        int rescued = GameManager.Instance != null ? GameManager.Instance.RescuedCount : 0;
        int totalWeight = GameManager.Instance != null ? GameManager.Instance.TotalWeight : 0;
        int mileage = GameManager.Instance != null ? GameManager.Instance.Mileage : 0;

        // 예전 "Day N / 3" 표기는 3일 주기가 없어지면서 의미가 없어졌다 - 이제는 상한 없이
        // 누적되는 순찰 횟수(CycleCount)만 보여준다.
        if (dayText != null) dayText.text = $"Day {cycleCount}";
        if (cargoText != null) cargoText.text = $"적재량 {rescued}마리 · {totalWeight}kg";
        if (targetText != null) targetText.text = $"목표 진행 {rescued}/{target}";
        if (targetFillImage != null) targetFillImage.fillAmount = target > 0 ? Mathf.Clamp01(rescued / (float)target) : 0f;
        if (mileageText != null) mileageText.text = $"마일리지 {mileage}";
    }
}
