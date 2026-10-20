using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TruckSceneUI : MonoBehaviour
{
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text cargoText;
    [SerializeField] private TMP_Text targetText;
    [SerializeField] private Image targetFillImage;
    [SerializeField] private TMP_Text mileageText;

    private void Start()
    {
        // 목표 진행 글자·바가 연결되지 않았으면 에러 대신 경고만 남기고 그 부분만 건너뛴다(Refresh의 null 확인).
        // 마일리지는 같은 화면의 MileageBadgeUI가 보여 주므로 mileageText는 비어 있어도 된다.
        if (targetText == null) Debug.LogWarning($"{name}: targetText가 연결되지 않아 목표 진행 글자를 표시하지 않는다.");
        if (targetFillImage == null) Debug.LogWarning($"{name}: targetFillImage가 연결되지 않아 목표 진행 바를 표시하지 않는다.");
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        int cycleCount = GameManager.Instance != null ? GameManager.Instance.CycleCount : 1;
        int target = GameManager.Instance != null ? GameManager.Instance.TargetCount : GameManager.ComputeTargetCount(cycleCount, null);
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
