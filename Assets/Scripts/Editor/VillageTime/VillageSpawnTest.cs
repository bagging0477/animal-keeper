using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Village Time > Run Village Spawn Test. 구간별 추가 몬스터 소환(VillageMonsterSpawner)을 실제 씬 흐름으로 확인한다.
/// 설정 에셋은 건드리지 않는다 - GameManager의 config를 복제본으로 바꿔 끼우고 그 복제본에만 구간 목록을 임시로 채운 뒤, 끝나면 되돌린다.
/// 검사: 목록이 비면 아무것도 소환하지 않음, 구간 진입 시 한 번에 소환, 간격 소환, 총 마릿수에서 멈춤, 상한에서 멈추고 줄면 다시 소환,
/// 소환 위치(시야 밖·최소 거리·트럭 방 제외·막는 충돌체와 겹치지 않음·NavMesh 위), 자리를 못 찾으면 에러 없이 건너뛰고 다시 시도,
/// 같은 Day 재진입에서 중복 소환 없음·처치한 개체가 다시 살아나지 않음, 밀린 구간을 재진입 시작 때 처리, ResetDay/ResetGame 초기화, 일시정지 중 타이머 정지.
/// 프레임 시간은 1/30초 고정(Time.captureDeltaTime). 시간을 빨리 보낼 때만 VillageTime 값을 직접 넣는다(리플렉션).
/// 테스트 동안 몬스터는 기절(Stun)시켜 플레이어를 잡지 않게 한다(AI 컴포넌트는 켜 둔다 - Start의 처치 기록 확인이 그대로 돌게).
/// 결과: Logs/VillageSpawnTest.txt / batchmode: -executeMethod VillageSpawnTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class VillageSpawnTest
{
    // VisionSizeTest가 "VST."를 쓰므로 겹치지 않게 "VSPT."를 쓴다.
    private const string KeyActive = "VSPT.Active", KeyDone = "VSPT.Done", KeyBatch = "VSPT.Batch", KeyPassed = "VSPT.Passed";
    private const string ReportPath = "Logs/VillageSpawnTest.txt";
    private const string WolfPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string SoundPath = "Assets/Prefabs/SoundReactiveMonster.prefab";
    private const float FrameTime = 1f / 30f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();

    private static GameManager gm;
    private static GameBalanceConfig original, clone;
    private static readonly FieldInfo ConfigField = typeof(GameManager).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance);

    // 소환 개체를 처음 본 순간 위치 규칙을 검사한다(이름별 한 번).
    private static readonly HashSet<string> checkedSpawns = new HashSet<string>();
    private static int spotChecks, spotViolations;
    private static bool watchSpawns;

    static VillageSpawnTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Time/Run Village Spawn Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VillageSpawnTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            checkedSpawns.Clear();
            spotChecks = spotViolations = 0;
            watchSpawns = false;
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
        if (gm != null && original != null) ConfigField.SetValue(gm, original);
        if (clone != null) Object.DestroyImmediate(clone);
        clone = null;
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
            if (routine != null) Debug.LogWarning("[VillageSpawnTest] 테스트가 끝나기 전에 Play 모드가 꺼졌다");
            EditorApplication.update -= Step;
            nested.Clear();
            TearDown();
            return;
        }
        // 플레이어가 잡혀 강제 복귀하지 않게 모든 몬스터를 기절시켜 둔다(움직임·추격 없음). 처치·기록 동작은 그대로다.
        foreach (MonsterHealth m in MonsterHealth.Active.ToArray()) if (m != null && !m.IsDead) m.Stun(1000f);
        if (watchSpawns) CheckNewSpawns();
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
        Debug.Log($"[VillageSpawnTest] [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static GameBalanceConfig.VillageSpawnEntry E(GameObject prefab, int count, int initial, int intervalCount, float intervalSeconds) =>
        new GameBalanceConfig.VillageSpawnEntry { monsterPrefab = prefab, count = count, initialCount = initial, intervalCount = intervalCount, intervalSeconds = intervalSeconds };

    private static void SetPhase(int phase, params GameBalanceConfig.VillageSpawnEntry[] entries) =>
        clone.villagePhaseSpawns[phase - 1] = new GameBalanceConfig.VillagePhaseSpawnList { entries = entries };

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"VillageSpawnTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        gm = GameManager.Instance;
        original = gm != null ? gm.Config : null;
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath);
        GameObject sound = AssetDatabase.LoadAssetAtPath<GameObject>(SoundPath);
        VillageMonsterSpawner spawner = gm != null ? gm.GetComponent<VillageMonsterSpawner>() : null;
        if (gm == null || original == null || wolf == null || sound == null || spawner == null)
        {
            Check(false, $"준비 실패: GameManager {gm != null}, config {original != null}, 늑대 {wolf != null}, 소리반응형 {sound != null}, 소환기 {spawner != null}");
            Finish();
            yield break;
        }
        sb.AppendLine($"설정(원본): 상한 {original.villageMonsterCap}, 최소 거리 {original.villageSpawnMinPlayerDistance}, 구간 목록 {original.villagePhaseSpawns?.Length}개");

        // --- 0. 기본값
        sb.AppendLine("\n== 0. 기본 설정");
        Check(original.villagePhaseSpawns != null && original.villagePhaseSpawns.Length == 5, "구간 목록이 5개");
        // 기본 설정: 2구간에 박쥐(예전 우는천사 자리) 1마리(진입 때 1, 간격 없음)만 있고 나머지 구간은 비어 있다.
        GameBalanceConfig.VillageSpawnEntry[] phase2 = original.GetVillagePhaseSpawns(2);
        Check(phase2.Length == 1 && phase2[0].monsterPrefab != null && phase2[0].monsterPrefab.GetComponent<BatMonsterAI>() != null &&
              phase2[0].count == 1 && phase2[0].initialCount == 1 && phase2[0].intervalCount == 0 && phase2[0].intervalSeconds == 0f,
            "2구간 목록: 박쥐 1마리(진입 때 1, 간격 0)");
        // 임시(박쥐 테스트용, 2026-10-24): 1구간에 박쥐 4마리(진입 때 1, 45초마다 1). 되돌리면 1구간도 비어 있어야 한다.
        GameBalanceConfig.VillageSpawnEntry[] phase1 = original.GetVillagePhaseSpawns(1);
        Check(phase1.Length == 0 || phase1.All(e => e.monsterPrefab != null && e.monsterPrefab.GetComponent<BatMonsterAI>() != null),
            $"1구간 목록: 비었거나 임시 박쥐만 ({phase1.Length}개 항목)");
        Check(new[] { 3, 4, 5 }.All(p => original.GetVillagePhaseSpawns(p).Length == 0), "3·4·5구간 목록은 비어 있다");
        Check(original.villageMonsterCap == 60 && Mathf.Approximately(original.villageSpawnMinPlayerDistance, 15f), "상한 60, 최소 거리 15");

        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        RestartClass();

        // 이 테스트는 복제 설정으로 돈다. 먼저 모든 구간 목록을 비워 "목록이 비면 아무것도 하지 않는다"를 본다.
        clone = Object.Instantiate(original);
        clone.name = "VillageSpawnTestConfig";
        for (int p = 1; p <= 5; p++) SetPhase(p);
        ConfigField.SetValue(gm, clone);

        // --- 1. 목록이 비면 아무것도 하지 않는다
        sb.AppendLine("\n== 1. 목록이 비어 있으면 소환하지 않는다");
        yield return Load("TruckScene");
        yield return Load("VillageScene");
        Check(SceneManager.GetActiveScene().name == "VillageScene", $"마을에 들어왔다 ({SceneManager.GetActiveScene().name})");
        int mapMonsters = VillageMonsterSpawner.CountLivingMonsters();
        SetTime(500f);
        yield return WaitFrames(60);
        SetTime(5000f);
        yield return WaitFrames(60);
        Check(gm.VillagePhase == 5, $"5구간까지 진행 (구간 {gm.VillagePhase}, 씬 {SceneManager.GetActiveScene().name})");
        Check(TimedRoot() == null, "GeneratedMap/TimedMonsters가 생기지 않았다");
        Check(VillageMonsterSpawner.CountLivingMonsters() == mapMonsters && spawner.SkippedSpawns == 0 && gm.VillageSpawns.EnteredPhase == 0,
            $"몬스터 수 그대로 {mapMonsters} → {VillageMonsterSpawner.CountLivingMonsters()}, 건너뜀 {spawner.SkippedSpawns}, 기록된 구간 {gm.VillageSpawns.EnteredPhase}");

        // 복제 설정에 임시 목록을 채우고 새 게임으로 시작한다.
        yield return Load("TruckScene");
        gm.ResetGame();
        RestartClass();
        SetPhase(2, E(wolf, 3, 2, 1, 2f));
        SetPhase(3, E(sound, 1, 1, 0, 0f));
        SetPhase(4, E(wolf, 3, 0, 1, 1f));
        SetPhase(5, E(wolf, 2, 2, 0, 0f));
        sb.AppendLine("\n임시 목록: 2구간 늑대 3(처음 2, 2초마다 1) / 3구간 소리반응형 1(처음 1) / 4구간 늑대 3(처음 0, 1초마다 1) / 5구간 늑대 2(처음 2)");

        // --- 2. 구간 진입 소환과 간격 소환
        sb.AppendLine("\n== 2. 구간에 들어갈 때 한 번에, 이후 간격마다");
        yield return Load("VillageScene");
        watchSpawns = true;
        yield return WaitFrames(30);
        Check(Timed().Count == 0 && gm.VillageSpawns.EnteredPhase == 1, $"1구간(목록 없음): 소환 {Timed().Count}, 기록된 구간 {gm.VillageSpawns.EnteredPhase}");
        SetTime(179.95f);
        yield return WaitFrames(6);
        Check(Names().SequenceEqual(new[] { $"{wolf.name}_T2_0_0", $"{wolf.name}_T2_0_1" }), $"2구간 진입: 한 번에 2마리 [{string.Join(", ", Names())}]");
        yield return WaitFrames(70); // 2초 + 여유
        Check(Count(2, 0) == 3, $"2초 뒤 간격 소환으로 3마리 (발급 {Count(2, 0)})");
        yield return WaitFrames(80);
        Check(Count(2, 0) == 3 && Timed().Count == 3, $"총 마릿수(3)에서 멈춘다 (발급 {Count(2, 0)}, 소환 {Timed().Count})");

        SetTime(359.95f);
        yield return WaitFrames(6);
        Check(Count(3, 0) == 1 && Names().Contains($"{sound.name}_T3_0_0"), $"3구간 진입: 소리반응형 1마리 (발급 {Count(3, 0)})");
        yield return WaitFrames(60);
        Check(Count(3, 0) == 1, "간격이 0인 항목은 한 번만 소환한다");

        // --- 3. 일시정지
        sb.AppendLine("\n== 3. ESC 일시정지 중에는 소환 타이머가 멈춘다");
        SetTime(539.95f);
        yield return WaitFrames(3);
        Check(gm.VillagePhase == 4 && Count(4, 0) == 0, $"4구간 진입(처음 0마리): 발급 {Count(4, 0)}");
        yield return WaitFrames(12);
        PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        typeof(PauseMenu).GetMethod("Pause", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        float timer = gm.VillageSpawns.Get(4, 0).IntervalTimer;
        int issued = Count(4, 0);
        yield return WaitFrames(90);
        Check(Time.timeScale == 0f && Mathf.Approximately(gm.VillageSpawns.Get(4, 0).IntervalTimer, timer) && Count(4, 0) == issued,
            $"일시정지 90프레임(3초): 타이머 {timer:F3} → {gm.VillageSpawns.Get(4, 0).IntervalTimer:F3}, 발급 {issued} → {Count(4, 0)}");
        menu.OnResume();
        yield return WaitFrames(30);
        Check(Count(4, 0) == issued + 1, $"재개 뒤 1초 안에 간격 소환 (발급 {issued} → {Count(4, 0)})");

        // --- 4. 상한
        sb.AppendLine("\n== 4. 상한에 닿으면 멈추고, 줄면 다시 소환");
        int living = VillageMonsterSpawner.CountLivingMonsters();
        clone.villageMonsterCap = living;
        int before = Count(4, 0);
        yield return WaitFrames(75);
        Check(Count(4, 0) == before && gm.VillageSpawns.Get(4, 0).Pending > 0, $"상한 {living}: 2.5초 동안 발급 {before} → {Count(4, 0)}, 대기 {gm.VillageSpawns.Get(4, 0).Pending}");
        MonsterHealth victim = MonsterHealth.Active.FirstOrDefault(m => !m.IsDead && !m.transform.IsChildOf(TimedRoot()));
        victim.TakeDamage(9999f);
        yield return WaitFrames(10);
        Check(Count(4, 0) == before + 1 && VillageMonsterSpawner.CountLivingMonsters() == living,
            $"맵 몬스터 1마리 처치 → 1마리 소환 (발급 {Count(4, 0)}, 살아 있는 몬스터 {VillageMonsterSpawner.CountLivingMonsters()}/{living})");
        clone.villageMonsterCap = 60;
        yield return WaitFrames(60);
        Check(Count(4, 0) == 3, $"상한을 풀면 이어서 총 3마리 (발급 {Count(4, 0)})");

        // --- 5. 자리를 못 찾을 때
        sb.AppendLine("\n== 5. 자리를 못 찾으면 에러 없이 건너뛰고 다시 시도");
        clone.villageSpawnMinPlayerDistance = 1000000f;
        int logsBefore = logs.Count;
        SetTime(599.95f);
        yield return WaitFrames(45);
        Check(gm.VillagePhase == 5 && Count(5, 0) == 0 && gm.VillageSpawns.Get(5, 0).Pending == 2 && spawner.SkippedSpawns > 0,
            $"최소 거리 100만: 발급 {Count(5, 0)}, 대기 {gm.VillageSpawns.Get(5, 0).Pending}, 건너뜀 {spawner.SkippedSpawns}회");
        Check(logs.Count == logsBefore, $"건너뛰는 동안 에러 없음 ({logs.Count - logsBefore}건)");
        clone.villageSpawnMinPlayerDistance = 15f;
        yield return WaitFrames(30);
        Check(Count(5, 0) == 2, $"자리가 생기면 다시 시도해 2마리 (발급 {Count(5, 0)})");

        sb.AppendLine($"\n== 6. 소환 위치 규칙 (지금까지 {spotChecks}마리 검사)");
        Check(spotChecks >= 9 && spotViolations == 0, $"시야 밖·최소 거리·트럭 방 제외·막는 충돌체 겹침 없음·NavMesh 위: 위반 {spotViolations}건 / {spotChecks}마리");

        // --- 7. 같은 Day 재진입
        sb.AppendLine("\n== 7. 같은 Day 재진입: 중복 없음, 처치한 개체는 살아나지 않음");
        MonsterHealth killedWolf = Find($"{wolf.name}_T2_0_0"), killedSound = Find($"{sound.name}_T3_0_0");
        killedWolf.TakeDamage(9999f);
        killedSound.TakeDamage(9999f);
        yield return WaitFrames(3);
        List<string> namesBefore = Names();
        int[] issuedBefore = { Count(2, 0), Count(3, 0), Count(4, 0), Count(5, 0) };
        yield return Load("TruckScene");
        yield return WaitFrames(30);
        yield return Load("VillageScene");
        yield return WaitFrames(60);
        List<string> namesAfter = Names();
        Check(namesAfter.SequenceEqual(namesBefore), $"같은 개체만 다시 생겼다 ({namesBefore.Count} → {namesAfter.Count}마리, 중복 {namesAfter.Count - namesAfter.Distinct().Count()})");
        Check(new[] { Count(2, 0), Count(3, 0), Count(4, 0), Count(5, 0) }.SequenceEqual(issuedBefore) && gm.VillageSpawns.EnteredPhase == 5,
            $"발급 수 그대로, 구간 진입 소환을 다시 하지 않음 (기록된 구간 {gm.VillageSpawns.EnteredPhase})");
        MonsterHealth wolfAgain = Find($"{wolf.name}_T2_0_0", includeInactive: true), soundAgain = Find($"{sound.name}_T3_0_0", includeInactive: true);
        Check(wolfAgain != null && wolfAgain.IsDead, $"처치한 늑대는 시체로 돌아온다 (죽음 {wolfAgain?.IsDead})");
        Check(soundAgain != null && !soundAgain.gameObject.activeSelf, $"처치한 소리반응형은 다시 나오지 않는다 (켜짐 {soundAgain?.gameObject.activeSelf})");

        // --- 8. ResetDay + 밀린 구간을 시작할 때 처리
        sb.AppendLine("\n== 8. Day가 바뀌면 초기화, 마을에 들어올 때 현재 구간까지 처리");
        yield return Load("TruckScene");
        gm.ResetDay();
        Check(gm.VillageSpawns.EnteredPhase == 0 && !gm.VillageSpawns.TryGet(2, 0, out _), "ResetDay 뒤 소환 기록이 비었다");
        SetTime(400f); // 테스트 전용: 3구간 시각으로 마을에 들어간다
        yield return Load("VillageScene");
        yield return WaitFrames(10);
        Check(gm.VillagePhase == 3 && gm.VillageSpawns.EnteredPhase == 3, $"들어오자마자 3구간까지 처리 (구간 {gm.VillagePhase}, 기록 {gm.VillageSpawns.EnteredPhase})");
        Check(Count(2, 0) == 2 && Count(3, 0) == 1, $"2구간 처음 2마리 + 3구간 1마리를 한 번씩 (발급 {Count(2, 0)}, {Count(3, 0)})");
        MonsterHealth newDayWolf = Find($"{wolf.name}_T2_0_0");
        Check(newDayWolf != null && !newDayWolf.IsDead, "새 Day에는 어제 처치한 번호의 늑대가 다시 살아 있다(기록 초기화)");

        // --- 9. ResetGame
        sb.AppendLine("\n== 9. 새 게임에서 초기화");
        yield return Load("TruckScene");
        gm.ResetGame();
        Check(gm.VillageSpawns.EnteredPhase == 0 && !gm.VillageSpawns.TryGet(2, 0, out _), "ResetGame 뒤 소환 기록이 비었다");
        RestartClass();
        yield return Load("VillageScene");
        yield return WaitFrames(30);
        Check(Timed().Count == 0 && gm.VillagePhase == 1, $"새 게임 1구간: 소환 {Timed().Count}");

        watchSpawns = false;
        Check(spotViolations == 0, $"재진입·새 Day 소환까지 포함한 위치 규칙: 위반 {spotViolations}건 / {spotChecks}마리");
        Check(!gm.IsGameOver && SceneManager.GetActiveScene().name == "VillageScene", "패배나 강제 복귀가 없었다");
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
        Debug.Log("[VillageSpawnTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 도움 함수 =====================

    private static int Count(int phase, int index) => gm.VillageSpawns.TryGet(phase, index, out VillageSpawnRecord.EntryState s) ? s.Issued : 0;

    private static Transform TimedRoot() => GameObject.Find("GeneratedMap")?.transform.Find(VillageMonsterSpawner.ParentName);

    private static List<MonsterHealth> Timed(bool includeInactive = false)
    {
        Transform root = TimedRoot();
        return root == null ? new List<MonsterHealth>() : root.GetComponentsInChildren<MonsterHealth>(includeInactive).ToList();
    }

    private static List<string> Names() => Timed(true).Select(m => m.name).OrderBy(n => n, System.StringComparer.Ordinal).ToList();

    private static MonsterHealth Find(string name, bool includeInactive = false) => Timed(includeInactive).FirstOrDefault(m => m.name == name);

    // 새로 생긴 소환 개체를 처음 본 순간 위치 규칙을 검사한다(처치 기록으로 시체 자리에 돌아온 개체는 뺀다).
    private static void CheckNewSpawns()
    {
        Transform root = TimedRoot();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (root == null || playerObj == null || gm == null || gm.Config == null) return;
        foreach (MonsterHealth m in root.GetComponentsInChildren<MonsterHealth>(true))
        {
            string key = $"{gm.CycleCount}:{SceneManager.GetActiveScene().handle}:{m.name}";
            if (!checkedSpawns.Add(key)) continue;
            if (gm.TryGetMonsterCorpse($"GeneratedMap/{VillageMonsterSpawner.ParentName}/{m.name}", out _)) continue;
            spotChecks++;
            Vector2 xy = m.GamePosition;
            List<string> bad = new List<string>();
            float distance = Vector2.Distance(xy, playerObj.transform.position);
            if (distance < gm.Config.villageSpawnMinPlayerDistance) bad.Add($"플레이어와 {distance:F1}칸");
            if (PlayerVision.IsVisibleToPlayer(xy)) bad.Add("플레이어 시야 안");
            if (InTruckRoom(xy)) bad.Add("트럭 방");
            float radius = VillageMonsterSpawner.SpawnRadius(m.gameObject);
            Collider2D blocker = Physics2D.OverlapCircleAll(xy, radius).FirstOrDefault(c => !c.isTrigger && c.enabled &&
                c.GetComponentInParent<AnimalRescue>() == null && c.GetComponentInParent<PlayerMovement>() == null &&
                (c.attachedRigidbody == null || c.attachedRigidbody.bodyType != RigidbodyType2D.Dynamic));
            if (blocker != null) bad.Add($"충돌체 {blocker.name}와 겹침");
            NavMeshAgent agent = m.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.isOnNavMesh) bad.Add("NavMesh 밖");
            if (bad.Count > 0)
            {
                spotViolations++;
                sb.AppendLine($"  [위치 위반] {m.name} @ ({xy.x:F2}, {xy.y:F2}): {string.Join(", ", bad)}");
            }
        }
    }

    private static bool InTruckRoom(Vector2 xy)
    {
        TruckPoint truck = Object.FindAnyObjectByType<TruckPoint>();
        GameObject map = GameObject.Find("GeneratedMap");
        if (truck == null || map == null) return false;
        foreach (Transform child in map.transform)
        {
            Tilemap ground = child.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null || child.name == "OutsideFloorBand") continue;
            if (ground.HasTile(ground.WorldToCell(truck.transform.position))) return ground.HasTile(ground.WorldToCell(xy));
        }
        return false;
    }

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
