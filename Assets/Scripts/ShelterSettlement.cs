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

    /// <summary>새 게임을 시작할 때(GameManager.ResetGame) 호출해서, 다음 플레이에서
    /// ShelterScene에 처음 들어왔을 때 다시 정산이 일어나게 한다.</summary>
    public static void ResetSettlementState()
    {
        HasSettled = false;
        LastResult = default;
    }

    private void Awake()
    {
        if (GameManager.Instance == null) return;

        // ClassSelectScene을 거쳐 ShelterScene으로 다시 돌아온 것처럼, 이번 게임에서 이미 한 번
        // 정산했다면 SettleCargo를 또 호출하지 않는다 - 화물이 이미 비워진 채로 다시 정산하면
        // 0/0으로 계산되어 목표 미달성 취급이 되고, 최종일이면 잘못된 게임 오버까지 발생한다.
        // 캐시해둔 첫 정산 결과만 다시 그리고, 패널 표시/효과음은 최초 정산 때만 실행한다.
        bool isFirstSettle = !HasSettled;
        GameManager.SettlementResult resultValue;

        if (isFirstSettle)
        {
            int pricePerWeight = config != null ? config.mileagePerWeight : 10;
            int bonusMileage = config != null ? config.targetBonusMileage : 50;
            resultValue = GameManager.Instance.SettleCargo(pricePerWeight, bonusMileage);
            LastResult = resultValue;
            HasSettled = true;
        }
        else
        {
            resultValue = LastResult;
        }

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

        if (!isFirstSettle) return;

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
