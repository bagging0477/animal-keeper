using UnityEngine;
using UnityEngine.UI;

public class ShelterSettlement : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Text settlementText;
    [SerializeField] private GameOverPanel gameOverPanel;
    [SerializeField] private DayClearPanel dayClearPanel;

    public static GameManager.SettlementResult LastResult { get; private set; }
    public static bool HasSettled { get; private set; }

    private void Awake()
    {
        if (GameManager.Instance == null) return;

        int pricePerWeight = config != null ? config.mileagePerWeight : 10;
        int bonusMileage = config != null ? config.targetBonusMileage : 50;
        GameManager.SettlementResult resultValue = GameManager.Instance.SettleCargo(pricePerWeight, bonusMileage);
        LastResult = resultValue;
        HasSettled = true;

        if (settlementText != null)
        {
            string bonusLine = resultValue.BonusApplied
                ? $"목표 달성 보너스   <color=#FFD54F>+{resultValue.BonusMileage}</color>"
                : "목표 달성 보너스   +0 (미달성)";

            string text =
                $"기본 정산 ({resultValue.TotalWeight}kg × {resultValue.PricePerWeight})   {resultValue.BaseMileage}\n" +
                $"{bonusLine}\n" +
                $"────────────────\n" +
                $"<b><color=#8BC34A>합계   {resultValue.TotalMileage} 마일리지</color></b>";

            if (GameManager.Instance.IsGameOver)
            {
                text += "\n\n<b><color=#E53935>목표를 달성하지 못해 보호소 운영이 어려워졌습니다. 게임 오버</color></b>";
            }

            settlementText.text = text;
        }

        if (GameManager.Instance.IsGameOver)
        {
            AudioManager.Instance?.PlayDayFail();
            if (gameOverPanel != null) gameOverPanel.Show();
        }
        else if (GameManager.Instance.Day >= GameManager.MaxDay && resultValue.BonusApplied)
        {
            AudioManager.Instance?.PlayDaySuccess();
            if (dayClearPanel != null) dayClearPanel.Show("Day 클리어!");
        }
    }
}
