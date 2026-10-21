using UnityEngine;

/// <summary>
/// 마을 시간 구간이 바뀌는 순간(GameManager.OnVillagePhaseChanged)에만 자막(SubtitleManager)으로 한 번 알린다.
/// - 1구간 시작은 알리지 않는다(이벤트가 2~5구간으로 바뀔 때만 난다).
/// - 구간은 마을에서 시간이 흐를 때만 바뀌므로, 마을에 다시 들어오거나 Day를 초기화해도 알림이 뜨지 않는다.
/// - 한 프레임에 구간을 여러 개 건너뛰어도 이벤트가 마지막 구간으로 한 번만 나므로 자막도 하나만 뜬다.
/// GameManager가 Awake에서 자기 오브젝트에 붙인다(씬 파일은 바꾸지 않는다).
/// </summary>
public class VillagePhaseNotice : MonoBehaviour
{
    private const float NoticeDuration = 4f;

    private void OnEnable() => GameManager.OnVillagePhaseChanged += Show;

    private void OnDisable() => GameManager.OnVillagePhaseChanged -= Show;

    private static void Show(int phase)
    {
        GameManager gm = GameManager.Instance;
        GameBalanceConfig config = gm != null ? gm.Config : null;
        if (config == null) return;
        SubtitleManager.Show(config.GetVillagePhaseNotice(phase), NoticeDuration);
    }
}
