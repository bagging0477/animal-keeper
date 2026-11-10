using TMPro;
using UnityEngine;

/// <summary>마을(VillageScene) HUD의 체력·스태미나 막대 아래에 "오늘 목표  N/M"을 보여 준다(Logs/Tutorial 시안).
/// N은 오늘 트럭에 맡긴 수(GameManager.RescuedCount), M은 오늘 목표(TargetCount). 값을 읽기만 하고, 바뀐 프레임에만 글자를 다시 쓴다.</summary>
public class VillageObjectiveChip : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text valueText;

    private int shownRescued = -1;
    private int shownTarget = -1;

    public const string LabelText = "오늘 목표";

    /// <summary>오른쪽 값 글자.</summary>
    public static string ValueText(int rescued, int target) => $"{rescued}/{target}";

    /// <summary>지금 보이는 값 글자(테스트용).</summary>
    public string CurrentValue => valueText != null ? valueText.text : null;

    private void Start()
    {
        if (label != null) label.text = LabelText;
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        GameManager gm = GameManager.Instance;
        int rescued = gm != null ? gm.RescuedCount : 0;
        int target = gm != null ? gm.TargetCount : GameManager.ComputeTargetCount(1, null);
        if (rescued == shownRescued && target == shownTarget) return;
        shownRescued = rescued;
        shownTarget = target;
        if (valueText != null) valueText.text = ValueText(rescued, target);
    }
}
