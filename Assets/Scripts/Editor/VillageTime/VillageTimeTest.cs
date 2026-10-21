using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Village Time > Run Village Time Test. 마을 시간(GameManager.VillageTime/VillagePhase/OnVillagePhaseChanged)을 실제 씬 흐름으로 확인한다.
/// 검사: 마을에서만 흐름(타이틀·클래스 선택·트럭·보호소에서는 그대로), ESC 일시정지 중 멈춤, 같은 Day 재진입에서 이어짐, ResetDay/ResetGame에서 0,
/// 시작 시각 0·180·360·540·600초에서 구간이 바뀌고 이벤트가 한 번씩만 남, 5구간 유지, 큰 시간 점프에도 구간이 맞고 이벤트·자막이 마지막 구간 하나만,
/// 알림은 구간이 바뀔 때만(1구간 시작·재진입·초기화에는 없음), 패배가 일어나지 않음.
/// 프레임 시간은 1/30초로 고정한다(Time.captureDeltaTime). 시간을 빨리 보낼 때만 테스트 안에서 VillageTime 값을 직접 넣는다(리플렉션).
/// 테스트 동안 마을 몬스터는 끈다(씬 파일은 바꾸지 않는다). 결과: Logs/VillageTimeTest.txt
/// batchmode: -executeMethod VillageTimeTest.RunBatch  (끝나면 에디터가 종료된다, 통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class VillageTimeTest
{
    private const string KeyActive = "VTT.Active", KeyDone = "VTT.Done", KeyBatch = "VTT.Batch", KeyPassed = "VTT.Passed";
    private const string ReportPath = "Logs/VillageTimeTest.txt";
    private const float FrameTime = 1f / 30f;
    private const float Epsilon = 0.0005f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();

    // OnVillagePhaseChanged로 받은 (구간, 그 순간의 VillageTime)
    private static readonly List<(int Phase, float Time)> events = new List<(int, float)>();
    // 자막에 새로 올라온 구간 알림 문구(SubtitleManager의 대기/표시 문구를 매 에디터 업데이트마다 본다)
    private static readonly List<string> noticesSeen = new List<string>();
    private static string lastSubtitle;
    private static string[] noticeTexts = new string[0];

    static VillageTimeTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Time/Run Village Time Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VillageTimeTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(false);
    }

    public static void RunBatch() => Begin(true);

    private static void Begin(bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetBool(KeyPassed, false);
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            nested.Clear();
            logs.Clear();
            events.Clear();
            noticesSeen.Clear();
            lastSubtitle = null;
            Application.logMessageReceived += OnLog;
            GameManager.OnVillagePhaseChanged += OnPhaseChanged;
            Time.captureDeltaTime = FrameTime;
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetBool(KeyPassed, false) ? 0 : 1);
        }
    }

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        GameManager.OnVillagePhaseChanged -= OnPhaseChanged;
        // 테스트 중 늘려 둔 시계 표시 시간을 되돌린다(설정 에셋을 바꾼 채로 두지 않는다).
        if (testConfig != null) { testConfig.villageClockFlashDuration = originalFlashDuration; testConfig = null; }
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    private static void OnPhaseChanged(int phase)
    {
        GameManager gm = GameManager.Instance;
        events.Add((phase, gm != null ? gm.VillageTime : -1f));
    }

    private static bool MoveNextNested()
    {
        if (nested.Count == 0) nested.Push(routine);
        while (nested.Count > 0)
        {
            IEnumerator top = nested.Peek();
            if (!top.MoveNext()) { nested.Pop(); continue; }
            if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
            return true;
        }
        return false;
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
        ObserveSubtitle();
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb?.ToString() ?? e.ToString(), new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            TearDown();
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    // ===================== 자막 관찰 =====================

    private static readonly FieldInfo SubtitleInstance = typeof(SubtitleManager).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo SubtitlePending = typeof(SubtitleManager).GetField("pendingText", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SubtitleCurrent = typeof(SubtitleManager).GetField("currentText", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void ObserveSubtitle()
    {
        object sm = SubtitleInstance?.GetValue(null);
        string text = null;
        if (sm is SubtitleManager manager && manager != null)
            text = (SubtitlePending.GetValue(manager) as string) ?? (SubtitleCurrent.GetValue(manager) as string);
        if (text == lastSubtitle) return;
        lastSubtitle = text;
        if (text != null && noticeTexts.Contains(text)) noticesSeen.Add(text);
    }

    private static void ClearObserved()
    {
        events.Clear();
        noticesSeen.Clear();
    }

    // ===================== 본 테스트 =====================

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static string Notice(int phase, GameBalanceConfig config) => config.GetVillagePhaseNotice(phase) ?? "(없음)";

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"VillageTimeTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        GameManager gm = GameManager.Instance;
        GameBalanceConfig config = gm != null ? gm.Config : null;
        if (gm == null || config == null)
        {
            Check(false, "TitleScene에서 GameManager 또는 GameBalanceConfig를 찾지 못했다");
            Finish();
            yield break;
        }
        float[] starts = config.villagePhaseStartSeconds;
        noticeTexts = Enumerable.Range(2, 4).Select(p => config.GetVillagePhaseNotice(p)).Where(t => !string.IsNullOrEmpty(t)).ToArray();
        sb.AppendLine($"설정: 구간 시작 시각 [{string.Join(", ", starts ?? new float[0])}]초, 프레임 시간 {FrameTime:F4}초");
        for (int p = 2; p <= 5; p++) sb.AppendLine($"  {p}구간 문구: {Notice(p, config)}");

        int startHealth = gm.Health;

        // --- 0. 시각 → 구간 계산
        sb.AppendLine("\n== 0. 시각 → 구간 (GameManager.ComputeVillagePhase)");
        Check(starts != null && starts.Length == GameManager.VillagePhaseCount, $"구간 시작 시각이 {GameManager.VillagePhaseCount}개");
        Check(starts != null && starts.SequenceEqual(new[] { 0f, 180f, 360f, 540f, 600f }), "기본값이 0, 180, 360, 540, 600초");
        foreach ((float t, int expected) in new[] { (0f, 1), (179.99f, 1), (180f, 2), (359.99f, 2), (360f, 3), (539.99f, 3), (540f, 4), (599.99f, 4), (600f, 5), (1000f, 5), (100000f, 5) })
        {
            int got = GameManager.ComputeVillagePhase(t, config);
            Check(got == expected, $"{t}초 → {got}구간 (기대 {expected})");
        }
        Check(gm.VillageTime == 0f && gm.VillagePhase == 1, $"시작 상태: 시간 {gm.VillageTime}, 구간 {gm.VillagePhase}");
        VillagePhaseNotice notice = gm.GetComponent<VillagePhaseNotice>();
        Check(notice != null && notice.isActiveAndEnabled, "GameManager에 알림 컴포넌트(VillagePhaseNotice)가 켜져 있다");
        Check(ListenerCount() == 3, $"구간 이벤트 구독 {ListenerCount()}개 (알림 + 몬스터 소환기 + 이 테스트)");

        // --- 0-1. 시계 변환 (VillageClockFlash.FormatClock)
        sb.AppendLine("\n== 0-1. 시계 변환: 실제 1초마다 시계 30초씩 점프, \"HH:MM\"(분 아래 버림)");
        Check(Mathf.Approximately(config.villageClockScale, 30f) && Mathf.Approximately(config.villageClockTickSeconds, 1f) &&
              Mathf.Approximately(config.villageClockFlashDuration, 0.5f) && Mathf.Approximately(config.villageClockFlashFontSize, 120f) &&
              ColorUtility.ToHtmlStringRGB(config.villageClockFlashColor) == "FF8C1A",
            $"설정 기본값: 배율 {config.villageClockScale}, 점프 {config.villageClockTickSeconds}초, 표시 {config.villageClockFlashDuration}초, 크기 {config.villageClockFlashFontSize}, 색 #{ColorUtility.ToHtmlStringRGB(config.villageClockFlashColor)}");
        foreach ((float t, string expected) in new[] { (0f, "00:00"), (1f, "00:00"), (2f, "00:01"), (59.9f, "00:29"), (60f, "00:30"), (180f, "01:30"),
                     (360f, "03:00"), (540f, "04:30"), (600f, "05:00"), (610.4f, "05:05"), (720f, "06:00") })
        {
            string got = VillageClockFlash.FormatClock(t, config);
            Check(got == expected, $"{t}초 → {got} (기대 {expected})");
        }
        bool onlyAtTicks = true;
        double previous = VillageClockFlash.ClockSeconds(0f, config);
        for (int i = 1; i <= 1000; i++) // 0~10초를 0.01초 간격으로
        {
            float t = i * 0.01f;
            double clockNow = VillageClockFlash.ClockSeconds(t, config);
            bool crossedSecond = Mathf.FloorToInt(t + 1e-4f) != Mathf.FloorToInt((i - 1) * 0.01f + 1e-4f);
            if ((clockNow != previous) != crossedSecond || (crossedSecond && clockNow - previous != 30.0)) onlyAtTicks = false;
            previous = clockNow;
        }
        Check(onlyAtTicks, "1초 안에서는 시계 값이 그대로이고, 1초가 지나는 순간에만 시계 30초 점프한다(0~10초, 0.01초 간격)");
        VillageClockFlash clock = gm.GetComponent<VillageClockFlash>();
        Check(clock != null && clock.isActiveAndEnabled, "GameManager에 마을 시계 컴포넌트(VillageClockFlash)가 켜져 있다");

        // --- 1. 마을 밖에서는 흐르지 않는다
        sb.AppendLine("\n== 1. 마을 밖에서는 시간이 흐르지 않는다");
        SetTime(gm, 50f);
        yield return HoldOutside(gm, "TitleScene");
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        yield return HoldOutside(gm, "ClassSelectScene");
        yield return Load("TruckScene");
        yield return HoldOutside(gm, "TruckScene");
        yield return Load("ShelterScene");
        yield return HoldOutside(gm, "ShelterScene");
        Check(events.Count == 0, $"마을 밖에서 구간 이벤트 없음 ({events.Count}회)");
        Check(clock.ShowCount == 0 && !clock.IsShowing, $"타이틀·클래스 선택·트럭·보호소에서는 시계가 뜨지 않는다 (띄운 횟수 {clock.ShowCount})");

        // --- 2. ResetGame (트럭에서)
        sb.AppendLine("\n== 2. ResetGame에서 0");
        yield return Load("TruckScene");
        ClearObserved();
        gm.ResetGame();
        Check(gm.VillageTime == 0f && gm.VillagePhase == 1, $"ResetGame 뒤 시간 {gm.VillageTime}, 구간 {gm.VillagePhase}");
        Check(events.Count == 0, $"ResetGame은 구간 이벤트를 내지 않는다 ({events.Count}회)");
        RestartClass(gm);

        // --- 3. 마을에서 흐른다, 1구간 시작은 알리지 않는다
        sb.AppendLine("\n== 3. 마을에서는 흐른다 (1구간 시작 알림 없음)");
        ClearObserved();
        yield return LoadVillage();
        Check(clock.ShowCount == 1 && clock.IsShowing && clock.TextAtShow == "00:00",
            $"새 게임 첫 진입: 시계 한 번 ({clock.ShowCount}회), 보이는 중 {clock.IsShowing}, \"{clock.TextAtShow}\"");
        yield return FlashEndCheck(clock, config.villageClockFlashDuration, "첫 진입");
        float t0 = gm.VillageTime;
        yield return WaitFrames(30);
        float flowed = gm.VillageTime - t0;
        Check(flowed > 28.5f * FrameTime && flowed < 31.5f * FrameTime, $"30프레임 동안 {flowed:F4}초 흘렀다 (기대 약 {30 * FrameTime:F4})");
        Check(gm.VillagePhase == 1, $"구간 {gm.VillagePhase} (기대 1)");
        Check(events.Count == 0 && noticesSeen.Count == 0, $"1구간 시작 이벤트/알림 없음 (이벤트 {events.Count}, 알림 {noticesSeen.Count})");

        // --- 4. ESC 일시정지
        sb.AppendLine("\n== 4. ESC 일시정지 중에는 멈춘다");
        yield return PauseCheck(gm);

        // --- 4-1. 시계: 일시정지 중에도 끝나고, 마을을 떠나면 남지 않는다
        sb.AppendLine("\n== 4-1. 시계: 일시정지 중에도 실제 시간으로 끝나고, 마을을 떠나면 바로 사라진다");
        yield return Load("TruckScene");
        int shows = clock.ShowCount;
        SceneManager.LoadScene("VillageScene");
        while (SceneManager.GetActiveScene().name != "VillageScene") yield return null;
        PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        typeof(PauseMenu).GetMethod("Pause", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        Check(clock.IsShowing && Time.timeScale == 0f, $"마을 진입 직후 일시정지 (보이는 중 {clock.IsShowing}, timeScale {Time.timeScale})");
        yield return WaitRealtime(0.8f); // 표시 시간(0.5초)보다 긴 실제 시간
        Check(!clock.IsShowing && clock.ShowCount == shows + 1, $"일시정지 중에도 표시 시간이 지나 사라졌다 (보이는 중 {clock.IsShowing}, 띄운 횟수 +{clock.ShowCount - shows})");
        menu.OnResume();

        yield return Load("TruckScene");
        SceneManager.LoadScene("VillageScene");
        while (SceneManager.GetActiveScene().name != "VillageScene") yield return null;
        yield return WaitFrames(1);
        bool shownBeforeLeaving = clock.IsShowing;
        SceneManager.LoadScene("TruckScene");
        while (SceneManager.GetActiveScene().name != "TruckScene") yield return null;
        yield return WaitFrames(1);
        Check(shownBeforeLeaving && !clock.IsShowing && !clock.Label.gameObject.activeInHierarchy,
            $"보이는 중에 마을을 떠나면 트럭에서 바로 사라진다 (떠나기 전 {shownBeforeLeaving}, 트럭에서 {clock.IsShowing})");
        yield return WaitFrames(30);
        Check(!clock.IsShowing, "트럭에 남아 있지 않다");
        yield return LoadVillage(); // 다음 검사를 위해 마을로 돌아온다(플레이어는 상호작용 범위 밖)
        Check(clock.ShowCount == shows + 3, $"마을에 들어올 때마다 한 번씩만 떴다 (이 구간 진입 3번, 띄운 횟수 +{clock.ShowCount - shows})");
        yield return FlashEndCheck(clock, config.villageClockFlashDuration, "다시 들어옴");

        // --- 5. 구간 경계
        sb.AppendLine("\n== 5. 시작 시각에서 구간이 바뀌고 이벤트·알림이 한 번씩");
        for (int p = 2; p <= 5; p++) yield return CrossBoundary(gm, config, p, starts[p - 1]);

        sb.AppendLine("\n== 6. 600초 이후 5구간 유지");
        ClearObserved();
        SetTime(gm, 5000f);
        yield return WaitFrames(30);
        Check(gm.VillagePhase == 5 && events.Count == 0 && noticesSeen.Count == 0, $"5000초: 구간 {gm.VillagePhase}, 이벤트 {events.Count}, 알림 {noticesSeen.Count}");

        // --- 7. 같은 Day 재진입
        sb.AppendLine("\n== 7. 같은 Day 재진입에서 시간이 이어진다 (알림 없음)");
        int cycle = gm.CycleCount;
        yield return Load("TruckScene");
        float atTruck = gm.VillageTime;
        yield return HoldOutside(gm, "TruckScene");
        ClearObserved();
        int showsBeforeReentry = clock.ShowCount;
        yield return LoadVillage();
        Check(clock.ShowCount == showsBeforeReentry + 1 && clock.TextAtShow == VillageClockFlash.FormatClock(atTruck, config) && clock.TextAtShow != "00:00",
            $"재진입: 이어진 시간 {atTruck:F1}초가 시계 \"{clock.TextAtShow}\"로 한 번 보인다 (기대 \"{VillageClockFlash.FormatClock(atTruck, config)}\")");
        yield return WaitFrames(30);
        float back = gm.VillageTime;
        Check(gm.CycleCount == cycle, $"같은 Day (사이클 {gm.CycleCount})");
        Check(back >= atTruck && back - atTruck < 2f, $"트럭에서 {atTruck:F3}초 → 재진입 후 {back:F3}초 (0으로 돌아가지 않고 이어짐)");
        Check(gm.VillagePhase == 5, $"재진입 후 구간 {gm.VillagePhase} (기대 5)");
        Check(events.Count == 0 && noticesSeen.Count == 0, $"재진입에서 이벤트/알림 없음 (이벤트 {events.Count}, 알림 {noticesSeen.Count})");

        // --- 8. ResetDay, 다음 사이클도 같은 구간 길이, 큰 시간 점프
        sb.AppendLine("\n== 8. ResetDay에서 0, 사이클 2도 같은 경계, 큰 시간 점프");
        yield return Load("TruckScene");
        ClearObserved();
        gm.ResetDay();
        Check(gm.CycleCount == cycle + 1, $"사이클 {cycle} → {gm.CycleCount}");
        Check(gm.VillageTime == 0f && gm.VillagePhase == 1, $"ResetDay 뒤 시간 {gm.VillageTime}, 구간 {gm.VillagePhase}");
        Check(events.Count == 0, $"ResetDay는 구간 이벤트를 내지 않는다 ({events.Count}회)");
        yield return LoadVillage();
        yield return WaitFrames(10);
        Check(gm.VillagePhase == 1 && events.Count == 0 && noticesSeen.Count == 0, $"사이클 2 마을 진입: 구간 {gm.VillagePhase}, 이벤트 {events.Count}, 알림 {noticesSeen.Count}");
        yield return CrossBoundary(gm, config, 2, starts[1]);

        ClearObserved();
        SetTime(gm, 550f); // 2구간 → 4구간 (3구간을 한 프레임에 건너뛴다)
        yield return WaitFrames(15);
        Check(gm.VillagePhase == 4, $"550초로 점프: 구간 {gm.VillagePhase} (기대 4)");
        Check(events.Count == 1 && events[0].Phase == 4, $"이벤트 한 번, 4구간 ({Describe(events)})");
        Check(noticesSeen.SequenceEqual(new[] { Notice(4, config) }), $"자막은 4구간 문구 하나만 ([{string.Join(" | ", noticesSeen)}])");

        ClearObserved();
        SetTime(gm, 100000f); // 4구간 → 5구간
        yield return WaitFrames(15);
        Check(gm.VillagePhase == 5 && events.Count == 1 && events[0].Phase == 5, $"100000초로 점프: 구간 {gm.VillagePhase}, 이벤트 ({Describe(events)})");
        Check(noticesSeen.SequenceEqual(new[] { Notice(5, config) }), $"자막은 5구간 문구 하나만 ([{string.Join(" | ", noticesSeen)}])");

        // --- 9. ResetGame (5구간에서)
        sb.AppendLine("\n== 9. 진행된 시간에서 ResetGame → 0");
        SetTime(gm, 700f);
        yield return WaitFrames(3);
        Check(gm.VillagePhase == 5, $"ResetGame 전 구간 {gm.VillagePhase}");
        yield return Load("TruckScene");
        ClearObserved();
        gm.ResetGame();
        Check(gm.VillageTime == 0f && gm.VillagePhase == 1, $"ResetGame 뒤 시간 {gm.VillageTime}, 구간 {gm.VillagePhase}");
        Check(events.Count == 0, $"ResetGame은 구간 이벤트를 내지 않는다 ({events.Count}회)");
        RestartClass(gm);
        // 시계 표시 시간을 2.5초로 늘려(테스트 동안만) 보이는 중에도 1초마다 점프해 올라가는지 본다.
        originalFlashDuration = config.villageClockFlashDuration;
        testConfig = config;
        config.villageClockFlashDuration = 2.5f;
        yield return LoadVillage();
        Check(gm.VillagePhase == 1 && gm.VillageTime > 0f && gm.VillageTime < 2f && events.Count == 0 && noticesSeen.Count == 0,
            $"새 게임 마을: 시간 {gm.VillageTime:F3}, 구간 {gm.VillagePhase}, 이벤트 {events.Count}, 알림 {noticesSeen.Count}");
        List<string> seen = new List<string> { clock.TextAtShow };
        bool consistent = true;
        while (clock.IsShowing)
        {
            string shown = clock.Label.text;
            if (shown != VillageClockFlash.FormatClock(gm.VillageTime, config) && shown != VillageClockFlash.FormatClock(gm.VillageTime - FrameTime, config)) consistent = false;
            if (seen[seen.Count - 1] != shown) seen.Add(shown);
            yield return WaitFrames(1);
        }
        config.villageClockFlashDuration = originalFlashDuration;
        testConfig = null;
        // batch에서는 게임 시간(프레임 1/30초 고정)이 실제 시간보다 빨리 흘러 2.5초 동안 여러 분이 지나간다 - 1분씩 차례로 오르고 매 프레임 계산값과 같으면 된다.
        bool stepsOfOneMinute = seen.Count >= 2 && seen[0] == "00:00";
        for (int i = 1; i < seen.Count && stepsOfOneMinute; i++)
            stepsOfOneMinute = Minutes(seen[i]) == Minutes(seen[i - 1]) + 1;
        Check(stepsOfOneMinute && consistent,
            $"표시 2.5초: 보이는 동안에도 점프 규칙대로 갱신된다 [{string.Join(" → ", seen.Take(6))}{(seen.Count > 6 ? $" … {seen[seen.Count - 1]}" : "")}] (1분씩 차례로, 매 프레임 계산값과 같음 {consistent})");

        // --- 10. 패배 없음
        sb.AppendLine("\n== 10. 시간 때문에 패배가 일어나지 않는다");
        SetTime(gm, 1000000f);
        yield return WaitFrames(60);
        Check(SceneManager.GetActiveScene().name == "VillageScene", $"아주 긴 시간 뒤에도 마을에 있다 ({SceneManager.GetActiveScene().name})");
        Check(!gm.IsGameOver && !gm.IsGameWon, $"게임 오버 {gm.IsGameOver}, 클리어 {gm.IsGameWon}");
        Check(gm.Health == startHealth, $"체력 {gm.Health} (시작 {startHealth})");
        Check(Object.FindObjectsByType<GameOverPanel>(FindObjectsInactive.Exclude).Length == 0, "게임 오버 패널이 떠 있지 않다");

        // --- 11. 시계 위치(다른 UI와 겹침)와 화면 캡처
        sb.AppendLine("\n== 11. 시계 위치: 인벤토리·체력 바·자막·모서리 장식과 겹치지 않음, 진입 직후 캡처");
        yield return Load("TruckScene");
        SetTime(gm, 400f); // 03:20 - 아무 시각이나 보이게
        SceneManager.LoadScene("VillageScene");
        while (SceneManager.GetActiveScene().name != "VillageScene") yield return null;
        yield return WaitFrames(3); // 진입 직후(0.1초) - 아직 완전히 보이는 중
        Check(clock.IsShowing && clock.Label.alpha >= 0.999f, $"캡처 순간 시계 \"{clock.Label.text}\"가 보이는 중");
        Check(clock.Label.rectTransform.localScale == Vector3.one && Mathf.Approximately(clock.Label.fontSize, config.villageClockFlashFontSize) &&
              clock.Label.color.r == config.villageClockFlashColor.r && clock.Label.color.g == config.villageClockFlashColor.g && clock.Label.color.b == config.villageClockFlashColor.b &&
              clock.Label.fontSharedMaterial == UIFont.Font.material,
            $"글자: Scale 1, 크기 {clock.Label.fontSize}, 색 #{ColorUtility.ToHtmlStringRGB(clock.Label.color)}, 공용 폰트 머티리얼(검은 외곽선 {clock.Label.fontSharedMaterial.GetFloat("_OutlineWidth"):F2})");
        Directory.CreateDirectory(CaptureFolder);
        foreach (Vector2Int res in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
        {
            string file = $"{CaptureFolder}/village_entry_clock_{res.x}x{res.y}.png";
            CaptureWithOverlapCheck(file, res, clock);
            sb.AppendLine($"  캡처: {Path.GetFullPath(file)}");
        }

        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");

        Finish();
    }

    private static void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[VillageTimeTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 시계 =====================

    private const string CaptureFolder = "Logs/VillageClock";
    private static float originalFlashDuration;
    private static GameBalanceConfig testConfig;

    // 보이기 시작한 뒤 표시 시간 안에 사라지는지, 마지막 0.15초만 투명해지는지(그 전에는 불투명).
    private static IEnumerator FlashEndCheck(VillageClockFlash clock, float duration, string what)
    {
        float lastShown = clock.ShownSeconds;
        bool opaqueBeforeFade = true, sawFade = false;
        // 표시 시간은 실제 시간(unscaled)이라 프레임 수가 아니라 실제 시간으로 기다린다(batch에서는 프레임이 실제보다 훨씬 빨리 지나간다).
        float until = Time.realtimeSinceStartup + duration + 5f;
        while (clock.IsShowing && Time.realtimeSinceStartup < until)
        {
            lastShown = clock.ShownSeconds;
            float alpha = clock.Label.alpha;
            if (lastShown < duration - 0.15f - 0.001f && alpha < 0.999f) opaqueBeforeFade = false;
            if (alpha > 0f && alpha < 0.999f) sawFade = true;
            yield return WaitFrames(1);
        }
        Check(!clock.IsShowing && !clock.Label.gameObject.activeInHierarchy && lastShown < duration + 0.001f && lastShown >= duration - 0.1f,
            $"{what}: {duration}초 안에 사라졌다 (마지막으로 보인 시각 {lastShown:F3}초)");
        Check(opaqueBeforeFade && sawFade, $"{what}: 마지막 0.15초에만 투명해지며 사라진다 (그 전 불투명 {opaqueBeforeFade}, 페이드 {sawFade})");
    }

    // 메인 카메라와 화면 오버레이 UI를 함께 res 크기로 렌더해 저장한다(오버레이 Canvas를 잠깐 카메라 모드로 돌리고 되돌린다).
    // 같은 배치에서 시계 글자의 실제 화면 영역이 인벤토리·체력 바·자막·모서리 장식과 겹치는지도 검사한다.
    private static void CaptureWithOverlapCheck(string file, Vector2Int res, VillageClockFlash clock)
    {
        Camera cam = Camera.main;
        if (cam == null) { Check(false, "메인 카메라가 없어 캡처하지 못했다"); return; }
        RenderTexture rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture previousTarget = cam.targetTexture;
        cam.targetTexture = rt;

        List<Canvas> canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToList();
        var saved = new List<(Canvas canvas, CanvasScaler scaler, bool scalerOn, float scale, float plane)>();
        for (int i = 0; i < canvases.Count; i++)
        {
            Canvas c = canvases[i];
            CanvasScaler scaler = c.GetComponent<CanvasScaler>();
            saved.Add((c, scaler, scaler != null && scaler.enabled, c.scaleFactor, c.planeDistance));
            float factor = scaler != null && scaler.enabled ? ScaleFactorFor(scaler, res) : 1f;
            if (scaler != null) scaler.enabled = false;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = cam.nearClipPlane + 1f + (canvases.Count - i) * 0.01f; // 앞(높은 sortingOrder)일수록 카메라에 가깝게
            c.scaleFactor = factor;
        }
        Canvas.ForceUpdateCanvases();
        foreach (TMPro.TMP_Text text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Exclude)) text.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        cam.Render(); // 카메라 모드 Canvas의 위치·크기는 렌더하면서 정해지므로 한 번 렌더한 뒤에 잰다

        // 겹침 검사(픽셀 좌표)
        Rect clockRect = ScreenRect(cam, clock.Label.rectTransform, clock.Label.textBounds);
        List<(string name, Rect rect)> others = new List<(string, Rect)>();
        foreach (HealthUI h in Object.FindObjectsByType<HealthUI>(FindObjectsInactive.Exclude))
            foreach (Graphic g in h.GetComponentsInChildren<Graphic>(false)) others.Add(($"체력 바 {g.name}", ScreenRect(cam, g.rectTransform)));
        foreach (InventoryUI inv in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Exclude))
            foreach (Graphic g in inv.GetComponentsInChildren<Graphic>(false)) others.Add(($"인벤토리 {g.name}", ScreenRect(cam, g.rectTransform)));
        foreach (CornerBrackets cb in Object.FindObjectsByType<CornerBrackets>(FindObjectsInactive.Exclude))
            foreach (Image img in cb.GetComponentsInChildren<Image>(false)) others.Add(($"모서리 장식 {cb.name}/{img.name}", ScreenRect(cam, img.rectTransform)));
        SubtitleManager subtitle = Object.FindAnyObjectByType<SubtitleManager>();
        if (subtitle != null)
            foreach (TMPro.TMP_Text t in subtitle.GetComponentsInChildren<TMPro.TMP_Text>(true)) others.Add(("자막 칸", ScreenRect(cam, t.rectTransform)));
        List<string> hits = others.Where(o => o.rect.Overlaps(clockRect)).Select(o => o.name).Distinct().ToList();
        Check(hits.Count == 0, $"{res.x}x{res.y}: 시계 글자 영역 {Fmt(clockRect)}가 다른 UI {others.Count}개(체력 바·인벤토리·모서리 장식·자막 칸)와 겹치지 않는다" +
                               (hits.Count > 0 ? $" - 겹침: {string.Join(", ", hits.Take(5))}" : ""));
        Check(Mathf.Abs(clockRect.center.x - res.x * 0.5f) < 2f && Mathf.Abs(clockRect.center.y - res.y * 0.5f) < res.y * 0.05f,
            $"{res.x}x{res.y}: 화면 가운데 (글자 중심 {clockRect.center.x:F0}, {clockRect.center.y:F0}, 글자 높이 {clockRect.height:F0}px)");

        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res.x, res.y, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, res.x, res.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());

        foreach (var s in saved)
        {
            s.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s.canvas.worldCamera = null;
            s.canvas.planeDistance = s.plane;
            if (s.scaler != null) s.scaler.enabled = s.scalerOn;
            if (s.scaler == null || !s.scalerOn) s.canvas.scaleFactor = s.scale;
        }
        cam.targetTexture = previousTarget;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static int Minutes(string hhmm) => int.Parse(hhmm.Substring(0, hhmm.IndexOf(':'))) * 60 + int.Parse(hhmm.Substring(hhmm.IndexOf(':') + 1));

    private static IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static string Fmt(Rect r) => $"({r.xMin:F0}~{r.xMax:F0}, {r.yMin:F0}~{r.yMax:F0})";

    private static Rect ScreenRect(Camera cam, RectTransform rt, Bounds? localBounds = null)
    {
        Vector3[] corners = new Vector3[4];
        if (localBounds.HasValue)
        {
            Bounds b = localBounds.Value;
            corners[0] = rt.TransformPoint(new Vector3(b.min.x, b.min.y, 0f));
            corners[1] = rt.TransformPoint(new Vector3(b.min.x, b.max.y, 0f));
            corners[2] = rt.TransformPoint(new Vector3(b.max.x, b.max.y, 0f));
            corners[3] = rt.TransformPoint(new Vector3(b.max.x, b.min.y, 0f));
        }
        else rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 c in corners)
        {
            Vector3 s = cam.WorldToScreenPoint(c);
            min = Vector2.Min(min, s);
            max = Vector2.Max(max, s);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // CanvasScaler(Scale With Screen Size)가 그 해상도에서 계산할 배율(UIFontPreviewCapture와 같은 식).
    private static float ScaleFactorFor(CanvasScaler scaler, Vector2Int res)
    {
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;
        Vector2 reference = scaler.referenceResolution;
        switch (scaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand: return Mathf.Min(res.x / reference.x, res.y / reference.y);
            case CanvasScaler.ScreenMatchMode.Shrink: return Mathf.Max(res.x / reference.x, res.y / reference.y);
            default:
                return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(res.x / reference.x, 2f), Mathf.Log(res.y / reference.y, 2f), scaler.matchWidthOrHeight));
        }
    }

    private static string Describe(List<(int Phase, float Time)> list) =>
        list.Count == 0 ? "없음" : string.Join(", ", list.Select(e => $"{e.Phase}구간@{e.Time:F3}초"));

    // 시작 시각 조금 앞에 두고 실제 프레임으로 넘긴다: 처음 그 시각에 닿은 프레임에 이벤트가 한 번 나고 그 구간 문구가 한 번 뜬다.
    private static IEnumerator CrossBoundary(GameManager gm, GameBalanceConfig config, int phase, float start)
    {
        ClearObserved();
        SetTime(gm, start - 0.05f);
        yield return WaitFrames(1);
        bool before = gm.VillagePhase == phase - 1;
        int frames = 0;
        while (gm.VillagePhase != phase && frames < 20) { yield return WaitFrames(1); frames++; }
        yield return WaitFrames(15); // 더 흘러도 이벤트가 다시 나지 않는지, 자막이 올라오는지
        bool atStart = events.Count >= 1 && events[0].Time >= start - Epsilon && events[0].Time < start + FrameTime + Epsilon;
        Check(before && gm.VillagePhase == phase, $"{start}초: {phase - 1}구간 → {gm.VillagePhase}구간");
        Check(events.Count == 1 && events[0].Phase == phase && atStart, $"{start}초: 이벤트 한 번, 시작 시각에 닿은 프레임 ({Describe(events)})");
        Check(noticesSeen.SequenceEqual(new[] { Notice(phase, config) }), $"{start}초: 자막 \"{Notice(phase, config)}\" 한 번 ([{string.Join(" | ", noticesSeen)}])");
    }

    private static IEnumerator PauseCheck(GameManager gm)
    {
        PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        MethodInfo pause = typeof(PauseMenu).GetMethod("Pause", BindingFlags.NonPublic | BindingFlags.Instance);
        if (menu == null || pause == null)
        {
            Check(false, "PauseMenu를 찾지 못해 ESC 일시정지를 재현하지 못했다");
            yield break;
        }
        pause.Invoke(menu, null); // ESC를 눌렀을 때와 같은 경로(PauseMenu.Pause)
        float paused = gm.VillageTime;
        yield return WaitFrames(30);
        Check(PauseMenu.IsPaused && Time.timeScale == 0f, $"일시정지 상태 (IsPaused {PauseMenu.IsPaused}, timeScale {Time.timeScale})");
        Check(Mathf.Abs(gm.VillageTime - paused) < Epsilon, $"일시정지 30프레임 동안 시간 그대로 ({paused:F4} → {gm.VillageTime:F4})");
        menu.OnResume();
        float resumed = gm.VillageTime;
        yield return WaitFrames(30);
        Check(gm.VillageTime - resumed > 28.5f * FrameTime, $"재개 뒤 다시 흐른다 ({resumed:F4} → {gm.VillageTime:F4})");
    }

    private static IEnumerator HoldOutside(GameManager gm, string scene)
    {
        float before = gm.VillageTime;
        int phase = gm.VillagePhase;
        yield return WaitFrames(60);
        Check(SceneManager.GetActiveScene().name == scene && Mathf.Abs(gm.VillageTime - before) < Epsilon && gm.VillagePhase == phase,
            $"{scene}({SceneManager.GetActiveScene().name}) 60프레임: 시간 {before:F3} → {gm.VillageTime:F3}, 구간 {gm.VillagePhase}");
    }

    private static void RestartClass(GameManager gm)
    {
        // ResetGame은 시작 클래스 선택을 다시 요구한다 - 트럭 씬이 클래스 선택으로 돌려보내지 않게 고른 것으로 처리한다.
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
    }

    private static int ListenerCount()
    {
        FieldInfo field = typeof(GameManager).GetField(nameof(GameManager.OnVillagePhaseChanged), BindingFlags.NonPublic | BindingFlags.Static);
        return (field?.GetValue(null) as System.Delegate)?.GetInvocationList().Length ?? -1;
    }

    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);

    // 테스트 전용: 시간을 빨리 보낸다. 구간은 다음 마을 프레임에 GameManager가 이 값으로 계산한다.
    private static void SetTime(GameManager gm, float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });

    private static IEnumerator LoadVillage()
    {
        yield return Load("VillageScene");
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);

        // 플레이어는 트럭 옆에서 시작한다 - 거기 있으면 트럭 안내("E를 눌러 트럭에 타기")가 매 프레임 자막을 차지해 구간 알림을 덮는다.
        // 상호작용 범위 밖(맵 바깥 먼 곳)으로 옮겨 알림만 보이게 한다.
        GameObject player = GameObject.Find("Player");
        if (player == null) { Check(false, "VillageScene에 Player가 없다"); yield break; }
        Vector2 far = new Vector2(100000f, 100000f);
        player.transform.position = new Vector3(far.x, far.y, player.transform.position.z);
        if (player.TryGetComponent(out Rigidbody2D body)) { body.position = far; body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
        yield return WaitFrames(2);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        yield return WaitForScene(sceneName);
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
