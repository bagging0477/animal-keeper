using UnityEngine;
using UnityEngine.UI;

public class ShelterSettlement : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Text settlementText;
    [SerializeField] private GameOverPanel gameOverPanel;
    [SerializeField] private DayClearPanel dayClearPanel;
    [SerializeField] private GameClearPanel gameClearPanel;

    private void Awake()
    {
        if (GameManager.Instance == null) return;

        // ClassSelectScene을 거쳐 ShelterScene으로 다시 돌아온 것처럼, 이번 방문에서 이미 한 번
        // 정산했다면 SettleCargo를 또 호출하지 않는다 - 화물이 이미 비워진 채로 다시 정산하면
        // 0마일리지짜리 정산이 한 번 더 기록된다.
        bool isFirstSettle = !GameManager.Instance.HasSettledCargo;
        GameManager.SettlementResult resultValue = isFirstSettle
            ? GameManager.Instance.SettleCargo(config != null ? config.mileagePerWeight : 10)
            : GameManager.Instance.LastSettlementResult;

        if (settlementText != null)
        {
            int rescued = GameManager.Instance.RescuedCount;
            int target = GameManager.Instance.TargetCount;

            // 목표 달성/게임 오버는 여기서 판정하지 않는다(FinishCycle 참고) - 보호소를 하루에
            // 여러 번 들를 수 있으므로, 이 화면은 "지금까지 이번 정산에서 판 화물" 영수증과 "오늘
            // 누적 진행도"만 보여주고, 실제 성패 판정은 "다음 날로"를 눌러야 이루어진다.
            settlementText.text =
                $"기본 정산 ({resultValue.TotalWeight}kg × {resultValue.PricePerWeight})   {resultValue.BaseMileage}\n" +
                $"────────────────\n" +
                $"<b><color=#8BC34A>합계   {resultValue.BaseMileage} 마일리지</color></b>\n\n" +
                $"오늘 목표 진행   {rescued}/{target}";
        }
    }

    /// <summary>"오늘"을 실제로 마감한다 - ShelterScene의 출구(ShelterExitPoint)에서만 호출된다. 목표 미달로
    /// 인한 실패는 TruckScene의 NextDayPoint가 보호소에 들어가기 전에 이미 걸러내므로, 여기 도달하는
    /// 시점에는 사실상 항상 목표를 달성한 상태다 - 그래도 방어적으로 !outcome.TargetMet 분기를 남겨
    /// 게임 오버 패널을 띄우고 다음 사이클로 넘어가지 않게 한다(호출자는 반환값이 false면 씬 전환을
    /// 하지 말아야 한다). 마지막 사이클(GameBalanceConfig.totalCyclesToWin)까지 목표 달성과 함께
    /// 완주했다면 Day 클리어 대신 게임 클리어 화면을 띄우고, 이때도 트럭씬으로 넘어가지 않는다 - 오직
    /// 그 화면의 "다시 시작" 버튼으로만 재개된다.</summary>
    public bool EvaluateCycleEnd()
    {
        if (GameManager.Instance == null) return true;

        GameManager.CycleOutcome outcome = GameManager.Instance.FinishCycle();

        if (outcome.GameWon)
        {
            AudioManager.Instance?.PlayDaySuccess();
            if (gameClearPanel != null) gameClearPanel.Show("10일간의 여정을 무사히 마쳤습니다!");
            return false;
        }

        if (!outcome.TargetMet)
        {
            AudioManager.Instance?.PlayDayFail();
            if (gameOverPanel != null) gameOverPanel.Show();
            return false;
        }

        AudioManager.Instance?.PlayDaySuccess();
        if (dayClearPanel != null) dayClearPanel.Show("Day 클리어!");
        return true;
    }
}
