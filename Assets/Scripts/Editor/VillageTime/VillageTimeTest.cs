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
        float t0 = gm.VillageTime;
        yield return WaitFrames(30);
        float flowed = gm.VillageTime - t0;
        Check(flowed > 28.5f * FrameTime && flowed < 31.5f * FrameTime, $"30프레임 동안 {flowed:F4}초 흘렀다 (기대 약 {30 * FrameTime:F4})");
        Check(gm.VillagePhase == 1, $"구간 {gm.VillagePhase} (기대 1)");
        Check(events.Count == 0 && noticesSeen.Count == 0, $"1구간 시작 이벤트/알림 없음 (이벤트 {events.Count}, 알림 {noticesSeen.Count})");

        // --- 4. ESC 일시정지
        sb.AppendLine("\n== 4. ESC 일시정지 중에는 멈춘다");
        yield return PauseCheck(gm);

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
        yield return LoadVillage();
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
        yield return LoadVillage();
        Check(gm.VillagePhase == 1 && gm.VillageTime > 0f && gm.VillageTime < 2f && events.Count == 0 && noticesSeen.Count == 0,
            $"새 게임 마을: 시간 {gm.VillageTime:F3}, 구간 {gm.VillagePhase}, 이벤트 {events.Count}, 알림 {noticesSeen.Count}");

        // --- 10. 패배 없음
        sb.AppendLine("\n== 10. 시간 때문에 패배가 일어나지 않는다");
        SetTime(gm, 1000000f);
        yield return WaitFrames(60);
        Check(SceneManager.GetActiveScene().name == "VillageScene", $"아주 긴 시간 뒤에도 마을에 있다 ({SceneManager.GetActiveScene().name})");
        Check(!gm.IsGameOver && !gm.IsGameWon, $"게임 오버 {gm.IsGameOver}, 클리어 {gm.IsGameWon}");
        Check(gm.Health == startHealth, $"체력 {gm.Health} (시작 {startHealth})");
        Check(Object.FindObjectsByType<GameOverPanel>(FindObjectsInactive.Exclude).Length == 0, "게임 오버 패널이 떠 있지 않다");

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
