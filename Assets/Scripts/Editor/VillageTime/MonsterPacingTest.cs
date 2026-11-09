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
/// Tools > Village Time > Run Monster Pacing Test. 박람회용 2일 흐름과 날짜별 몬스터 출현 표(GameBalanceConfig.dayMonsterPlans)를 실제 씬 흐름으로 확인한다.
/// 설정 에셋은 읽기만 한다.
/// 검사:
/// 0. 설정: 2일, 목표 3·5마리, 구간 시작 시각과 날짜별 표가 요청한 박람회 설정과 같음(종류별 맵 생성 수, 구간별 추가 소환), 1구간 소환 없음,
///    1일차에는 코뿔소 없음. 요청한 값을 직접 적어 두는 곳은 이 0번뿐이다.
/// 1. 날짜(1·2일차) x 시드(격자 3x4)마다: 맵 생성 종류별 수, 구간별 추가 소환 수와 시점(경계 0.5초 전에는 없고 넘으면 나옴),
///    5구간 간격과 최대치에서 더 늘지 않음, 그날 종류가 아닌 몬스터(1일차 코뿔소)는 맵 생성·시간 소환 모두에서 없음, 5구간 종류는 그날 종류 안.
///    1번의 기대값(수·시각·간격·최대치)은 모두 설정(dayMonsterPlans, villagePhaseStartSeconds)에서 계산한다.
///    첫 시드는 5구간을 실제 시간(설정 간격)으로 기다리고, 나머지 시드는 간격 타이머만 앞당긴다.
/// 2. 1일차 목표 3마리 달성 → Day 클리어, 2일차 목표 5마리 달성 → 게임 클리어 화면(ShelterScene GameClearPanel), 2일차 미달 → 게임 오버.
/// 날짜·시드별 구간 몬스터 수 표를 보고서에 남긴다. 테스트 동안 몬스터는 기절시키고 박쥐(기절하지 않음)는 꺼 둔다 - 꺼진 개체도 센다.
/// 프레임 시간 1/30초 고정(Time.captureDeltaTime). 시간을 빨리 보낼 때만 VillageTime 값을 직접 넣는다(리플렉션).
/// 결과: Logs/MonsterPacingTest.txt / batchmode: -executeMethod MonsterPacingTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class MonsterPacingTest
{
    private const string KeyActive = "MPT.Active", KeyDone = "MPT.Done", KeyBatch = "MPT.Batch", KeyPassed = "MPT.Passed";
    private const string ReportPath = "Logs/MonsterPacingTest.txt";
    private const float FrameTime = 1f / 30f;
    private static readonly int[] Seeds = { 1, 2, 3, 4, 5 };

    private const string WolfPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string SoundPath = "Assets/Prefabs/SoundReactiveMonster.prefab";
    private const string BatPath = "Assets/Prefabs/Monsters/BatMonster.prefab";
    private const string PorcupinePath = "Assets/Prefabs/Monsters/PorcupineMonster.prefab";
    private const string RhinoPath = "Assets/Prefabs/Monsters/RhinoMonster.prefab";

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();
    private static readonly List<string> table = new List<string>();

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject wolf, sound, bat, porcupine, rhino;
    private static string[] kindNames; // 보고서 열 순서: 늑대, 소리반응형, 박쥐, 호저, 코뿔소

    static MonsterPacingTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Time/Run Monster Pacing Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[MonsterPacingTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            table.Clear();
            Application.logMessageReceived += OnLog;
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
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
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
        if (routine == null || !EditorApplication.isPlaying)
        {
            if (routine != null) Debug.LogWarning("[MonsterPacingTest] 테스트가 끝나기 전에 Play 모드가 꺼졌다");
            EditorApplication.update -= Step;
            nested.Clear();
            TearDown();
            return;
        }
        // 플레이어가 잡혀 강제 복귀하지 않게 모든 몬스터를 기절시키고, 기절하지 않는 박쥐는 끈다(꺼진 개체도 수에는 넣는다).
        foreach (MonsterHealth m in MonsterHealth.Active.ToArray())
        {
            if (m == null || m.IsDead) continue;
            if (m.GetComponent<BatMonsterAI>() != null) m.gameObject.SetActive(false);
            else m.Stun(1000f);
        }
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

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        Debug.Log($"[MonsterPacingTest] [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"MonsterPacingTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        gm = GameManager.Instance;
        config = gm != null ? gm.Config : null;
        wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath);
        sound = AssetDatabase.LoadAssetAtPath<GameObject>(SoundPath);
        bat = AssetDatabase.LoadAssetAtPath<GameObject>(BatPath);
        porcupine = AssetDatabase.LoadAssetAtPath<GameObject>(PorcupinePath);
        rhino = AssetDatabase.LoadAssetAtPath<GameObject>(RhinoPath);
        if (gm == null || config == null || new[] { wolf, sound, bat, porcupine, rhino }.Any(p => p == null))
        {
            Check(false, $"준비 실패: GameManager {gm != null}, config {config != null}, 프리팹 {new[] { wolf, sound, bat, porcupine, rhino }.Count(p => p != null)}/5");
            Finish();
            yield break;
        }
        kindNames = new[] { wolf.name, sound.name, bat.name, porcupine.name, rhino.name };

        CheckConfig();

        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        RestartClass();

        foreach (int day in new[] { 1, 2 })
        {
            sb.AppendLine($"\n== 1-{day}. {day}일차 마을: 맵 생성과 구간별 추가 소환 (격자 3x4, 시드 {string.Join(",", Seeds)})");
            foreach (int seed in Seeds) yield return RunVillageDay(day, seed, realWaitPhase5: seed == Seeds[0]);
        }
        VillageMapGenerator.EditorLayoutOverride = null;

        yield return ClearFlow();

        sb.AppendLine("\n== 날짜·시드별 구간 몬스터 수 (누적, 맵 생성 + 시간 소환. 늑/소/박/호/코 = 늑대/소리반응형/박쥐/호저/코뿔소)");
        foreach (string row in table) sb.AppendLine("  " + row);

        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        Finish();
    }

    // ---- 0. 설정: 요청한 표 그대로인지
    private static void CheckConfig()
    {
        sb.AppendLine("\n== 0. 설정 (박람회용 2일, 구간 시작 시각, 날짜별 표)");
        float[] expectedStarts = { 0f, 120f, 240f, 360f, 420f };
        Check(config.villagePhaseStartSeconds != null && config.villagePhaseStartSeconds.SequenceEqual(expectedStarts),
            $"구간 시작 시각 [{string.Join(", ", config.villagePhaseStartSeconds ?? new float[0])}]초 (기대 {string.Join(", ", expectedStarts)})");
        Check(config.totalCyclesToWin == 2, $"전체 날짜 2일 (totalCyclesToWin {config.totalCyclesToWin})");
        Check(GameManager.ComputeTargetCount(1, config) == 3 && GameManager.ComputeTargetCount(2, config) == 5,
            $"목표 마릿수 1일차 {GameManager.ComputeTargetCount(1, config)}, 2일차 {GameManager.ComputeTargetCount(2, config)} (기대 3, 5)");
        Check(config.dayMonsterPlans != null && config.dayMonsterPlans.Length == 2, $"날짜별 표 {config.dayMonsterPlans?.Length}줄 (기대 2)");

        // 요청한 표: 맵 생성 종류별 수, 구간 2~4 진입 소환, 5구간 30초마다 1마리 최대 n마리(그날 종류에서)
        var expectedMap = new Dictionary<int, int[]> { { 1, new[] { 4, 2, 2, 1, 0 } }, { 2, new[] { 4, 2, 2, 2, 2 } } };
        var expectedPhase = new Dictionary<int, GameObject[][]>
        {
            { 1, new[] { new GameObject[0], new[] { wolf }, new[] { sound, bat }, new[] { porcupine } } },
            { 2, new[] { new GameObject[0], new[] { wolf, bat }, new[] { porcupine, sound }, new[] { rhino, wolf } } },
        };
        var expectedPhase5Max = new Dictionary<int, int> { { 1, 3 }, { 2, 4 } };
        foreach (int day in new[] { 1, 2 })
        {
            int[] map = KindCounts(config.GetMapMonsterList(day) ?? new List<GameObject>());
            Check(map.SequenceEqual(expectedMap[day]), $"{day}일차 맵 생성 {Fmt(map)} (기대 {Fmt(expectedMap[day])})");
            for (int phase = 1; phase <= 4; phase++)
            {
                GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(day, phase);
                GameObject[] want = expectedPhase[day][phase - 1];
                bool ok = entries.Length == want.Length && entries.Select((e, i) => e != null && !e.pickFromDayTypes && e.monsterPrefab == want[i] &&
                                                                               e.count == 1 && e.initialCount == 1 && e.intervalCount == 0).All(b => b);
                Check(ok, $"{day}일차 {phase}구간: 진입 때 {(want.Length == 0 ? "없음" : string.Join("·", want.Select(w => Short(w.name) + " 1")))}" +
                          $" (설정 {string.Join(", ", entries.Select(Describe))})");
            }
            GameBalanceConfig.VillageSpawnEntry[] p5 = config.GetVillagePhaseSpawns(day, 5);
            Check(p5.Length == 1 && p5[0].pickFromDayTypes && p5[0].count == expectedPhase5Max[day] && p5[0].initialCount == 0 &&
                  p5[0].intervalCount == 1 && Mathf.Approximately(p5[0].intervalSeconds, 30f),
                $"{day}일차 5구간: 그날 종류에서 30초마다 1마리, 최대 {expectedPhase5Max[day]} (설정 {string.Join(", ", p5.Select(Describe))})");
        }
        List<GameObject> day1Types = config.GetDayMonsterTypes(1);
        Check(!day1Types.Contains(rhino) && day1Types.Count == 4,
            $"1일차에 나올 수 있는 종류: {string.Join(", ", day1Types.Select(t => Short(t.name)))} (코뿔소 없음)");
        Check(config.GetDayMonsterTypes(2).Count == 5, $"2일차에 나올 수 있는 종류 {config.GetDayMonsterTypes(2).Count}가지");
    }

    // ---- 1. 하루치 마을: 진입 → 2·3·4구간 → 5구간 간격 소환
    private static IEnumerator RunVillageDay(int day, int seed, bool realWaitPhase5)
    {
        yield return Load("TruckScene");
        gm.ResetGame();
        RestartClass();
        for (int d = 1; d < day; d++) gm.ResetDay();
        Check(gm.CycleCount == day, $"Day {gm.CycleCount} 준비 (시드 {seed})");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = seed };
        yield return Load("VillageScene");
        yield return WaitFrames(10);
        string tag = $"{day}일차 시드 {seed}";

        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        int[] map = KindCounts(RoomMonsters());
        int[] expectedMap = KindCounts(config.GetMapMonsterList(day));
        Check(map.SequenceEqual(expectedMap) && gen != null && gen.LastGridSpawnShortfall.monsters == 0,
            $"{tag}: 맵 생성 {Fmt(map)} (기대 {Fmt(expectedMap)}, 자리 부족 {gen?.LastGridSpawnShortfall.monsters})");
        Check(TimedMonsters().Count == 0 && gm.VillagePhase == 1, $"{tag}: 1구간에는 추가 소환 없음 ({TimedMonsters().Count}마리)");
        List<string> row = new List<string> { $"Day {day} 시드 {seed}", $"진입 {Fmt(Total())}" };

        // 2~4구간: 경계 0.5초 전에는 그 구간 소환이 없고, 넘으면 표대로 나온다.
        for (int phase = 2; phase <= 4; phase++)
        {
            float start = config.villagePhaseStartSeconds[phase - 1];
            int[] before = KindCounts(TimedMonsters());
            SetTime(start - 0.5f);
            yield return WaitFrames(6);
            Check(gm.VillagePhase == phase - 1 && KindCounts(TimedMonsters()).SequenceEqual(before),
                $"{tag}: {start - 0.5f:F1}초({phase - 1}구간)에는 {phase}구간 소환 전 ({Fmt(KindCounts(TimedMonsters()))})");
            int[] want = Add(before, KindCounts(config.GetVillagePhaseSpawns(day, phase).SelectMany(e => Enumerable.Repeat(e.monsterPrefab, e.initialCount))));
            yield return WaitUntil(() => KindCounts(TimedMonsters()).SequenceEqual(want), 300);
            Check(gm.VillagePhase == phase && KindCounts(TimedMonsters()).SequenceEqual(want),
                $"{tag}: {gm.VillageTime:F1}초({phase}구간) 추가 소환 누적 {Fmt(KindCounts(TimedMonsters()))} (기대 {Fmt(want)})");
            row.Add($"{phase}구간 {Fmt(Total())}");
        }

        // 5구간: 들어갈 때는 0, 그 뒤 설정 간격마다 1마리씩 최대치까지, 그 뒤로는 늘지 않는다.
        GameBalanceConfig.VillageSpawnEntry p5 = config.GetVillagePhaseSpawns(day, 5).FirstOrDefault();
        int max = p5 != null ? p5.count : 0;
        float interval = p5 != null ? p5.intervalSeconds : 30f;
        float phase5Start = config.villagePhaseStartSeconds[4];
        SetTime(phase5Start - 0.5f);
        yield return WaitUntil(() => gm.VillagePhase == 5, 60);
        yield return WaitFrames(3);
        Check(gm.VillagePhase == 5 && Issued5() == 0, $"{tag}: 5구간 진입({gm.VillageTime:F1}초) 때는 소환 없음 (발급 {Issued5()})");
        if (realWaitPhase5)
        {
            // 실제 시간으로 기다린다: k번째는 진입 + k x 간격에 나온다.
            for (int k = 1; k <= max; k++)
            {
                float due = phase5Start + k * interval;
                yield return WaitUntilTime(due - 0.6f);
                Check(Issued5() == k - 1, $"{tag}: {gm.VillageTime:F1}초 - {k}번째 전 (발급 {Issued5()}, 기대 {k - 1})");
                yield return WaitUntilTime(due + 0.6f);
                yield return WaitUntil(() => Phase5Objects().Count == k, 300);
                Check(Issued5() == k && Phase5Objects().Count == k, $"{tag}: {gm.VillageTime:F1}초 - {k}번째 소환 (발급 {Issued5()}, 개체 {Phase5Objects().Count})");
            }
            yield return WaitUntilTime(phase5Start + (max + 1) * interval + 1f);
        }
        else
        {
            // 간격 타이머만 앞당긴다(시점은 첫 시드가 실제 시간으로 본다).
            for (int k = 1; k <= max + 1; k++)
            {
                VillageSpawnRecord.EntryState st = gm.VillageSpawns.Get(5, 0);
                st.IntervalTimer = Mathf.Max(st.IntervalTimer, interval - 0.05f);
                yield return WaitFrames(4);
                int want = Mathf.Min(k, max);
                yield return WaitUntil(() => Phase5Objects().Count == want, 300);
            }
        }
        List<MonsterHealth> p5Objects = Phase5Objects();
        Check(Issued5() == max && p5Objects.Count == max && gm.VillageSpawns.Get(5, 0).Pending == 0,
            $"{tag}: 5구간 최대 {max}마리에서 더 늘지 않음 (발급 {Issued5()}, 개체 {p5Objects.Count}, 대기 {gm.VillageSpawns.Get(5, 0).Pending}, {gm.VillageTime:F1}초)");
        List<GameObject> dayTypes = config.GetDayMonsterTypes(day);
        Check(p5Objects.All(m => dayTypes.Any(t => Kind(m.name) == t.name)),
            $"{tag}: 5구간 종류가 그날 종류 안 ({string.Join(", ", p5Objects.Select(m => Short(Kind(m.name))))})");
        row.Add($"5구간 끝 {Fmt(Total())} (5구간: {string.Join("·", p5Objects.Select(m => Short(Kind(m.name))))})");

        // 그날 종류(맵 생성 + 구간 소환에 적힌 프리팹)가 아닌 몬스터는 맵 생성·시간 소환 모두에서 나오지 않는다(1일차 코뿔소).
        {
            int[] all = Total();
            List<int> absent = Enumerable.Range(0, kindNames.Length).Where(i => !dayTypes.Any(t => t.name == kindNames[i])).ToList();
            Check(absent.All(i => all[i] == 0),
                $"{tag}: 그날 종류가 아닌 몬스터 없음 ({(absent.Count == 0 ? "해당 없음" : string.Join(", ", absent.Select(i => $"{ShortNames[i]} {all[i]}")))})");
        }
        Check(SceneManager.GetActiveScene().name == "VillageScene" && !gm.IsGameOver, $"{tag}: 패배나 강제 복귀 없음");
        table.Add(string.Join(" | ", row));
    }

    // ---- 2. 2일 흐름과 클리어 화면
    private static IEnumerator ClearFlow()
    {
        sb.AppendLine("\n== 2. 2일 흐름: 1일차 3마리 → Day 클리어, 2일차 5마리 → 게임 클리어, 2일차 미달 → 게임 오버");
        yield return Load("TruckScene");
        gm.ResetGame();
        RestartClass();
        Check(gm.CycleCount == 1 && gm.TargetCount == 3 && gm.TotalCyclesToWin == 2, $"1일차 목표 {gm.TargetCount}, 전체 {gm.TotalCyclesToWin}일");

        for (int i = 0; i < 3; i++) gm.AddCargo(5);
        yield return Load("ShelterScene");
        ShelterSettlement settlement = Object.FindAnyObjectByType<ShelterSettlement>();
        bool proceed1 = settlement != null && settlement.EvaluateCycleEnd();
        DayClearPanel dayClear = Object.FindAnyObjectByType<DayClearPanel>(FindObjectsInactive.Include);
        GameClearPanel gameClear = Object.FindAnyObjectByType<GameClearPanel>(FindObjectsInactive.Include);
        Check(settlement != null && !gm.IsGameWon && !gm.IsGameOver && gm.CycleCount == 2 && gm.TargetCount == 5,
            $"1일차 마감: 클리어 {gm.IsGameWon}, 게임 오버 {gm.IsGameOver}, 다음 날 Day {gm.CycleCount} 목표 {gm.TargetCount}");
        Check((dayClear != null && dayClear.gameObject.activeSelf) || proceed1, $"1일차 마감 화면: Day 클리어 패널 {(dayClear != null ? dayClear.gameObject.activeSelf.ToString() : "없음")}");
        Check(gameClear != null && !gameClear.gameObject.activeSelf, "1일차에는 게임 클리어 화면이 뜨지 않는다");
        Time.timeScale = 1f;

        yield return Load("TruckScene");
        for (int i = 0; i < 5; i++) gm.AddCargo(5);
        yield return Load("ShelterScene");
        settlement = Object.FindAnyObjectByType<ShelterSettlement>();
        bool proceed2 = settlement != null && settlement.EvaluateCycleEnd();
        gameClear = Object.FindAnyObjectByType<GameClearPanel>(FindObjectsInactive.Include);
        TMPro.TMP_Text message = gameClear != null ? (TMPro.TMP_Text)typeof(GameClearPanel).GetField("messageText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gameClear) : null;
        Check(!proceed2 && gm.IsGameWon && !gm.IsGameOver && gm.CycleCount == 2, $"2일차 5마리 마감: 게임 클리어 {gm.IsGameWon}, Day {gm.CycleCount} 그대로");
        Check(gameClear != null && gameClear.gameObject.activeInHierarchy && Time.timeScale == 0f,
            $"게임 클리어 화면이 떴다 (켜짐 {gameClear?.gameObject.activeInHierarchy}, timeScale {Time.timeScale})");
        Check(message != null && message.text.Contains("2일"), $"클리어 문구: \"{message?.text}\"");
        Time.timeScale = 1f;

        // 2일차 미달
        yield return Load("TruckScene");
        gm.ResetGame();
        RestartClass();
        gm.ResetDay();
        for (int i = 0; i < 4; i++) gm.AddCargo(5);
        GameManager.CycleOutcome fail = gm.FinishCycle();
        Check(!fail.TargetMet && !fail.GameWon && gm.IsGameOver && !gm.IsGameWon, $"2일차 4/5마리: 게임 오버 {gm.IsGameOver}, 클리어 {gm.IsGameWon}");
        gm.ResetGame();
        RestartClass();
    }

    private static void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[MonsterPacingTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 도움 함수 =====================

    private static Transform MapRoot() => GameObject.Find("GeneratedMap")?.transform;

    // 맵 생성 몬스터: 방(Room_*) 아래. 꺼진 박쥐도 센다.
    private static List<MonsterHealth> RoomMonsters()
    {
        Transform root = MapRoot();
        if (root == null) return new List<MonsterHealth>();
        return root.Cast<Transform>().Where(t => t.name.StartsWith("Room_")).SelectMany(t => t.GetComponentsInChildren<MonsterHealth>(true)).ToList();
    }

    private static List<MonsterHealth> TimedMonsters()
    {
        Transform root = MapRoot()?.Find(VillageMonsterSpawner.ParentName);
        return root == null ? new List<MonsterHealth>() : root.GetComponentsInChildren<MonsterHealth>(true).ToList();
    }

    private static List<MonsterHealth> Phase5Objects() => TimedMonsters().Where(m => m.name.Contains("_T5_")).ToList();

    private static int Issued5() => gm.VillageSpawns.TryGet(5, 0, out VillageSpawnRecord.EntryState s) ? s.Issued : 0;

    private static int[] Total() => Add(KindCounts(RoomMonsters()), KindCounts(TimedMonsters()));

    // 인스턴스 이름("{프리팹}_{번호}", "{프리팹}_T{구간}_{항목}_{번호}")에서 프리팹 이름을 찾는다.
    private static string Kind(string instanceName) =>
        kindNames.Where(k => instanceName.StartsWith(k + "_")).OrderByDescending(k => k.Length).FirstOrDefault() ?? instanceName;

    private static int[] KindCounts(IEnumerable<MonsterHealth> monsters) => KindCountsByName(monsters.Select(m => Kind(m.name)));

    private static int[] KindCounts(IEnumerable<GameObject> prefabs) => KindCountsByName(prefabs.Where(p => p != null).Select(p => p.name));

    private static int[] KindCountsByName(IEnumerable<string> names)
    {
        int[] counts = new int[kindNames.Length];
        foreach (string n in names)
        {
            int i = System.Array.IndexOf(kindNames, n);
            if (i >= 0) counts[i]++;
        }
        return counts;
    }

    private static int[] Add(int[] a, int[] b) => a.Select((v, i) => v + b[i]).ToArray();

    private static readonly string[] ShortNames = { "늑", "소", "박", "호", "코" };

    private static string Short(string prefabName)
    {
        int i = System.Array.IndexOf(kindNames, prefabName);
        return i >= 0 ? ShortNames[i] : prefabName;
    }

    private static string Fmt(int[] c) => $"늑{c[0]} 소{c[1]} 박{c[2]} 호{c[3]} 코{c[4]} = {c.Sum()}";

    private static string Describe(GameBalanceConfig.VillageSpawnEntry e) =>
        e == null ? "null" : $"{(e.pickFromDayTypes ? "그날 종류" : e.monsterPrefab != null ? Short(e.monsterPrefab.name) : "없음")} 총{e.count} 처음{e.initialCount} {e.intervalSeconds}초마다{e.intervalCount}";

    private static void RestartClass()
    {
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
    }

    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);

    private static void SetTime(float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 20f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitUntil(System.Func<bool> done, int maxFrames)
    {
        int target = Time.frameCount + maxFrames;
        float until = Time.realtimeSinceStartup + 60f;
        while (!done() && Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    // 마을 시간이 seconds에 닿을 때까지 실제로 프레임을 돌린다(1/30초 고정이라 30초 = 900프레임).
    private static IEnumerator WaitUntilTime(float seconds)
    {
        float until = Time.realtimeSinceStartup + 600f;
        while (gm.VillageTime < seconds && SceneManager.GetActiveScene().name == "VillageScene" && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
