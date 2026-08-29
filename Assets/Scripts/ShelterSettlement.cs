using UnityEngine;
using UnityEngine.UI;

public class ShelterSettlement : MonoBehaviour
{
    [SerializeField] private int pricePerWeight = 10;
    [SerializeField] private int bonusMileage = 50;
    [SerializeField] private Text settlementText;

    public static GameManager.SettlementResult LastResult { get; private set; }
    public static bool HasSettled { get; private set; }

    private void Awake()
    {
        if (GameManager.Instance == null) return;

        GameManager.SettlementResult resultValue = GameManager.Instance.SettleCargo(pricePerWeight, bonusMileage);
        LastResult = resultValue;
        HasSettled = true;

        if (settlementText != null)
        {
            string text = $"기본 정산: {resultValue.BaseMileage}마일리지";
            if (resultValue.BonusApplied) text += $" + 목표 달성 보너스: {resultValue.BonusMileage}마일리지";
            text += $" = 총 {resultValue.TotalMileage}";
            settlementText.text = text;
        }
    }
}
