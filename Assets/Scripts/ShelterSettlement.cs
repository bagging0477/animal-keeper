using UnityEngine;
using UnityEngine.UI;

public class ShelterSettlement : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Text settlementText;
    [SerializeField] private GameOverPanel gameOverPanel;
    [SerializeField] private DayClearPanel dayClearPanel;

    private void Awake()
    {
        if (GameManager.Instance == null) return;

        // 보호소에 도착했으니 TruckScene의 "다음날로" 지점을 다시 쓸 수 있게 푼다 - 한 트럭 방문 동안
        // 반복해서 눌러 CycleCount를 무한정 불리는 것을 막는 가드(NextDayPointAvailable)를 여기서 푼다.
        GameManager.Instance.ReopenNextDayPoint();

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

    /// <summary>"오늘"을 실제로 마감한다 - NextDayButton이 클릭됐을 때만 호출된다. 목표 달성 여부를
    /// 판정해서 성공하면 보너스를 지급하고 사이클을 넘기며, 실패하면 게임 오버 패널을 띄우고 다음
    /// 사이클로 넘어가지 않는다(호출자는 반환값이 false면 씬 전환을 하지 말아야 한다).</summary>
    public bool EvaluateCycleEnd()
    {
        if (GameManager.Instance == null) return true;

        GameManager.CycleOutcome outcome = GameManager.Instance.FinishCycle();

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
