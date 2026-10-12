using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Animals > Run Turtle Test. 거북이(AnimalTurtle)를 실제 입력 경로(가상 키보드로 WASD/Shift/E)로 확인한다.
/// 1) 스폰: 격자 3x4 시드 1~30, 4x5 시드 1~10을 맵 점검 도구(AuditSnapshot/GridMapAuditInspector)로 검사 - 총량·상한·Id·스폰 위치/겹침, 거북이 비율.
/// 2) 상태 전환: 막힌 데 없는 직선 길에 거북이 한 마리를 두고 스프린트로 다가가 Wander→Alert→Flee→Hidden을 기록, 숨은 채 E(줍기 실패+자막+쿨다운),
///    멀어지면 Emerge→Wander, 가까이 있어도 최대 숨음 시간 뒤 나오기, 사방이 막혔을 때 바로 숨기, 마취(도주 중/숨은 중/연동 끔), 일시정지.
/// 3) 잡기: 클래스 3종 x 걷기/스프린트 (러닝화 없이/있게)로 정면에서 다가가며 도주 중 E - 걷기는 못 잡고 스프린트는 잡는지.
/// 4) 저장/복원: 맵에서 생긴 거북이를 숨게 한 뒤 트럭에 갔다 오면 같은 자리에서 배회로 시작하는지.
/// 테스트 동안 마을 몬스터와 다른 동물은 끈다(씬 파일은 바꾸지 않는다). 결과: Logs/TurtleTest.txt
/// </summary>
[InitializeOnLoad]
public static class TurtleTest
{
    private const string KeyActive = "TT.Active", KeyDone = "TT.Done";
    private const string ReportPath = "Logs/TurtleTest.txt";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const string RunningShoesPath = "Assets/Settings/Passives/RunningShoes.asset";
    private const float PickupReach = 1.7f; // AnimalRescue.interactionRange(1.8)보다 살짝 안쪽에서 E

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();
    private static List<string> auditLogSink;

    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static readonly PlayerClass[] Classes = { PlayerClass.Scout, PlayerClass.Trapper, PlayerClass.Gunner };

    static TurtleTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Animals/Run Turtle Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[TurtleTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
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
            Application.logMessageReceived += OnLog;
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("TurtleTestKeyboard");
            vmouse = InputSystem.AddDevice<Mouse>("TurtleTestMouse");
            vkb.MakeCurrent();
            vmouse.MakeCurrent();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
        }
    }

    private static void TearDown()
    {
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vkb = null;
        vmouse = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Log && auditLogSink != null) auditLogSink.Add($"{type}: {condition.Split('\n')[0]}");
        if (type == LogType.Log || condition.StartsWith("[TurtleTest]")) return;
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
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            File.WriteAllText(ReportPath, sb?.ToString() ?? e.ToString(), new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            Application.logMessageReceived -= OnLog;
            TearDown();
            routine = null;
            Time.timeScale = 1f;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static StringBuilder sb;
    private static List<string> failures;
    private static GameBalanceConfig config;
    private static int turtleSerial;

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        sb.AppendLine($"TurtleTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"설정: 감지 {config.turtleDetectRadius}, 놀람 {config.turtleAlertDuration}s, 도주 거리 {config.turtleFleeDistance}, 배회 속도 {config.turtleWanderSpeed}, 도주 속도 {config.turtleFleeSpeed}, " +
                      $"숨음 반경 {config.turtleHideRadius}, 나오기 이탈 {config.turtleEmergeDelay}s, 최대 숨음 {config.turtleMaxHiddenTime}s, 마취 연동 {config.turtleSleepInterruptsHiding}, " +
                      $"스폰 확률 {config.turtleSpawnChance}, 무게 {config.turtleMinWeight}~{config.turtleMaxWeight}");

        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        GiveMileage(gm, config.GetClassUnlockPrice(PlayerClass.Trapper) + config.GetClassUnlockPrice(PlayerClass.Gunner));
        gm.PurchaseClass(PlayerClass.Trapper);
        gm.PurchaseClass(PlayerClass.Gunner);

        int turtleSeed = -1;
        yield return SpawnAudit(seed => { if (turtleSeed < 0) turtleSeed = seed; });

        yield return StateScenarios(gm);
        yield return CatchMatrix(gm);
        if (turtleSeed > 0) yield return SaveRestore(gm, turtleSeed);
        else Check(false, "3x4 시드 1~30에 거북이가 한 마리도 없어 저장/복원을 확인하지 못했다");

        sb.AppendLine($"\n== 콘솔 경고/에러 (Log 제외, 스폰 점검 구간 포함): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 경고/에러 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[TurtleTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ================= 1. 스폰 =================

    private static IEnumerator SpawnAudit(System.Action<int> onTurtleSeed)
    {
        sb.AppendLine("\n== 1. 격자 스폰 (맵 점검 도구 GridMapAuditInspector)");
        string[] spawnCategories = { GridMapAuditInspector.CSpawnPlace, GridMapAuditInspector.CSpawnOverlap, GridMapAuditInspector.CIds, GridMapAuditInspector.CTotals };
        foreach ((int cols, int rows, int count) in new[] { (3, 4, 30), (4, 5, 10) })
        {
            int animals = 0, turtles = 0, runs = 0, totalOk = 0;
            float turtleRadius = 0f;
            Dictionary<string, int> violations = spawnCategories.ToDictionary(c => c, c => 0);
            List<string> samples = new List<string>();
            int otherViolations = 0;
            for (int seed = 1; seed <= count; seed++)
            {
                // 다른 시드 맵에서 저장된 동물 위치가 같은 Id로 복원되지 않게 한다(맵 점검 도구는 GameManager 없이 돌아서 이 문제가 없다).
                // 동물은 파괴될 때 위치를 저장하므로, 지금 맵의 동물을 먼저 없앤 뒤에 비운다.
                foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Include)) Object.Destroy(a.gameObject);
                yield return WaitFrames(1);
                GameManager.Instance.ResetDay();
                AuditSnapshot snap = null;
                auditLogSink = new List<string>();
                VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = cols, Rows = rows, Seed = seed };
                yield return LoadWith("VillageScene", () => snap = AuditSnapshot.Take());
                List<string> seedLogs = auditLogSink;
                auditLogSink = null;
                AuditSeedResult res = GridMapAuditInspector.Inspect(snap, cols, rows, seed, seedLogs);
                runs++;

                GameObject root = GameObject.Find("GeneratedMap");
                AnimalRescue[] all = root.GetComponentsInChildren<AnimalRescue>(true);
                int seedTurtles = all.Count(a => a.GetComponent<AnimalTurtle>() != null);
                int monsters = root.GetComponentsInChildren<MonsterHealth>(true).Length;
                animals += all.Length;
                turtles += seedTurtles;
                if (all.Length == config.gridAnimalTotal && monsters == config.gridMonsterTotal) totalOk++;
                if (seedTurtles > 0 && cols == 3) onTurtleSeed(seed);
                AuditEntity turtleEntity = snap.Entities.FirstOrDefault(e => e.Prefab == "AnimalTurtle");
                if (turtleEntity != null) turtleRadius = turtleEntity.Radius;

                foreach (AuditViolation v in res.Violations)
                {
                    if (violations.ContainsKey(v.Category)) { violations[v.Category]++; if (samples.Count < 5) samples.Add($"시드 {seed} [{v.Category}] {v.Message}"); }
                    else otherViolations++;
                }
            }
            sb.AppendLine($"  격자 {cols}x{rows} x{runs}: 동물 합계 {animals} (회당 {(float)animals / runs:0.#}), 거북이 {turtles} ({100f * turtles / Mathf.Max(1, animals):0.0}%), " +
                          $"동물 {config.gridAnimalTotal}·몬스터 {config.gridMonsterTotal} 맞은 회차 {totalOk}/{runs}, 거북이 스폰 반경 {turtleRadius:0.###}");
            sb.AppendLine($"    위반: " + string.Join(", ", violations.Select(kv => $"{kv.Key} {kv.Value}")) + $" | 그 밖의 분류 {otherViolations}");
            foreach (string s in samples) sb.AppendLine("    " + s);
            Check(totalOk == runs && violations.Values.All(v => v == 0) && turtles > 0,
                $"격자 {cols}x{rows}: 총량 {config.gridAnimalTotal}/{config.gridMonsterTotal} 전부 맞음, 스폰 위치·겹침·Id·총량/상한 위반 0, 거북이 등장");
        }
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    // ================= 2. 상태 전환 =================

    private struct Lane { public Vector2 Start, Dir; }

    private static IEnumerator StateScenarios(GameManager gm)
    {
        sb.AppendLine("\n== 2. 상태 전환 (스카우트)");
        gm.SelectClass(PlayerClass.Scout);
        yield return LoadClearVillage();
        Lane lane = FindLane();
        sb.AppendLine($"  직선 길: 시작 ({lane.Start.x:0.0},{lane.Start.y:0.0}) 방향 ({lane.Dir.x},{lane.Dir.y})");
        GameObject player = Player();
        PlayerMovement pm = player.GetComponent<PlayerMovement>();

        // --- 배회 속도: 플레이어에게서 멀리 두고 5초 동안 0.25초 구간 이동 속도의 최댓값을 잰다
        {
            Teleport(player, lane.Start);
            AnimalTurtle wanderer = SpawnTurtle(lane.Start + lane.Dir * 12f);
            yield return WaitFrames(3);
            float maxWander = 0f;
            Vector2 last = wanderer.transform.position;
            float windowStart = Time.time;
            float until = Time.time + 5f;
            while (Time.time < until)
            {
                yield return WaitFrames(1);
                if (Time.time - windowStart < 0.25f) continue;
                maxWander = Mathf.Max(maxWander, Vector2.Distance(last, wanderer.transform.position) / (Time.time - windowStart));
                last = wanderer.transform.position;
                windowStart = Time.time;
            }
            Check(wanderer.State == AnimalTurtle.TurtleState.Wander && maxWander > 0.05f && maxWander <= config.turtleWanderSpeed + 0.02f,
                $"배회 최고 속도 {maxWander:0.000} (설정 {config.turtleWanderSpeed})");
            Object.Destroy(wanderer.gameObject);
        }

        // --- 스프린트로 다가가되 E는 누르지 않는다(2.5칸 앞에서 멈춤): Wander → Alert → Flee → Hidden
        Teleport(player, lane.Start);
        yield return WaitFull(pm);
        AnimalTurtle turtle = SpawnTurtle(lane.Start + lane.Dir * 7f);
        yield return WaitFrames(3);
        List<string> trace = new List<string>();
        yield return Approach(turtle, lane, sprint: true, pressE: false, until: t => t.State == AnimalTurtle.TurtleState.Hidden, trace, timeout: 20f, stopAt: 2.5f);
        sb.AppendLine("  스프린트 접근(E 안 누름): " + string.Join(" → ", trace));
        Check(trace.Select(s => s.Split(' ')[0]).SequenceEqual(new[] { "Wander", "Alert", "Flee", "Hidden" }), "Wander→Alert→Flee→Hidden 순서");

        // --- 숨은 거북이 옆에서 E: 못 줍고 자막, 쿨다운
        Teleport(player, (Vector2)turtle.transform.position - lane.Dir * 1f);
        yield return WaitFrames(3);
        int before = turtle.HiddenMessageCount;
        yield return Press(Key.E);
        yield return Press(Key.E);
        int afterTwo = turtle.HiddenMessageCount;
        yield return WaitRealtime(0.4f); // 자막은 앞 문구가 페이드 아웃된 뒤 바뀐다
        string subtitleAfterFirst = Subtitle();
        yield return WaitRealtime(config.turtleHiddenMessageCooldown + 0.2f);
        yield return Press(Key.E);
        Check(turtle.gameObject.activeSelf && turtle.State == AnimalTurtle.TurtleState.Hidden, "숨은 거북이는 E로 못 줍는다");
        Check(subtitleAfterFirst == "거북이가 등껍질 속에 숨었습니다", $"자막: \"{subtitleAfterFirst}\"");
        Check(afterTwo == before + 1 && turtle.HiddenMessageCount == before + 2,
            $"자막 쿨다운: 바로 두 번 눌러 {afterTwo - before}번, {config.turtleHiddenMessageCooldown}초 뒤 다시 눌러 {turtle.HiddenMessageCount - before}번");

        // --- 멀어지면 이탈 시간 뒤 Emerge → Wander
        Teleport(player, (Vector2)turtle.transform.position - lane.Dir * (config.turtleHideRadius + 3f));
        trace.Clear();
        float t0 = Time.time;
        yield return Watch(turtle, t => t.State == AnimalTurtle.TurtleState.Wander, trace, 10f);
        sb.AppendLine("  숨음 반경 밖으로 이동: " + string.Join(" → ", trace));
        float emergeAt = ParseTime(trace, "Emerge");
        Check(trace.Any(s => s.StartsWith("Emerge")) && turtle.State == AnimalTurtle.TurtleState.Wander && Mathf.Abs(emergeAt - config.turtleEmergeDelay) < 0.3f,
            $"이탈 {emergeAt:0.00}초 뒤 나오기 시작(설정 {config.turtleEmergeDelay}초) → 배회");
        Object.Destroy(turtle.gameObject);

        // --- 가까이 붙어 있어도 최대 숨음 시간 뒤 나온다
        Teleport(player, lane.Start);
        yield return WaitFull(pm);
        turtle = SpawnTurtle(lane.Start + lane.Dir * 7f);
        yield return WaitFrames(3);
        yield return Approach(turtle, lane, sprint: true, pressE: false, until: t => t.State == AnimalTurtle.TurtleState.Hidden, null, timeout: 20f, stopAt: 2.5f);
        Teleport(player, (Vector2)turtle.transform.position - lane.Dir * 2f);
        trace.Clear();
        yield return Watch(turtle, t => t.State == AnimalTurtle.TurtleState.Wander || t.State == AnimalTurtle.TurtleState.Alert, trace, config.turtleMaxHiddenTime + 5f);
        sb.AppendLine($"  숨은 채 플레이어가 2칸 옆에 계속: " + string.Join(" → ", trace));
        float maxAt = ParseTime(trace, "Emerge");
        Check(Mathf.Abs(maxAt - config.turtleMaxHiddenTime) < 0.3f, $"최대 숨음 {maxAt:0.00}초 뒤 나오기(설정 {config.turtleMaxHiddenTime}초)");
        Object.Destroy(turtle.gameObject);

        // --- 사방이 막히면 바로 숨는다
        Vector2 boxCenter = lane.Start + lane.Dir * 7f;
        GameObject box = BuildBox(boxCenter, 0.85f);
        turtle = SpawnTurtle(boxCenter);
        yield return WaitFrames(3);
        Teleport(player, boxCenter - lane.Dir * 3f);
        trace.Clear();
        yield return Watch(turtle, t => t.State == AnimalTurtle.TurtleState.Hidden, trace, 4f);
        sb.AppendLine("  사방이 막힌 자리에서 놀람: " + string.Join(" → ", trace));
        float fleeAt = ParseTime(trace, "Flee"), hideAt = ParseTime(trace, "Hidden");
        // 벽(두께 0.2)에 몸이 닿을 때까지는 움직이고, 닿은 뒤 0.3초 안에 숨어야 한다.
        float gapToWall = 0.85f - 0.1f - config.turtlePrefab.GetComponent<CircleCollider2D>().radius * config.turtlePrefab.transform.localScale.x;
        float allowed = gapToWall / config.turtleFleeSpeed + 0.4f;
        Check(turtle.State == AnimalTurtle.TurtleState.Hidden && hideAt - fleeAt < allowed,
            $"막히면 도주 {hideAt - fleeAt:0.00}초 만에 숨음 (벽까지 {gapToWall:0.00}칸 이동 포함, 기준 {allowed:0.00}초, 에러 없이)");

        // --- 숨은 중 마취: 바로 들 수 있고, 깨면 배회
        AnimalSleep sleep = turtle.GetComponent<AnimalSleep>();
        sleep.PutToSleep(0f, 2f);
        yield return WaitFrames(3);
        Check(sleep.IsAsleep && turtle.CanBePickedUp && turtle.State == AnimalTurtle.TurtleState.Wander, $"숨은 중 마취 → 잠듦 {sleep.IsAsleep}, 들 수 있음 {turtle.CanBePickedUp}, 상태 {turtle.State}");
        Teleport(player, boxCenter - lane.Dir * 9f);
        yield return WaitRealtime(2.3f);
        Check(!sleep.IsAsleep && turtle.State == AnimalTurtle.TurtleState.Wander, $"깬 뒤 상태 {turtle.State}");
        Object.Destroy(turtle.gameObject);

        // --- 마취 연동을 끄면 숨은 중에는 마취가 안 듣는다 (설정을 잠시 바꿨다 되돌림)
        bool savedToggle = config.turtleSleepInterruptsHiding;
        config.turtleSleepInterruptsHiding = false;
        turtle = SpawnTurtle(boxCenter);
        yield return WaitFrames(3);
        Teleport(player, boxCenter - lane.Dir * 3f);
        yield return Watch(turtle, t => t.State == AnimalTurtle.TurtleState.Hidden, null, 4f);
        sleep = turtle.GetComponent<AnimalSleep>();
        sleep.PutToSleep(0f, 2f);
        yield return WaitFrames(3);
        bool stayedHidden = turtle.State == AnimalTurtle.TurtleState.Hidden && !sleep.IsAsleep && !turtle.CanBePickedUp;
        config.turtleSleepInterruptsHiding = savedToggle;
        Check(stayedHidden, $"마취 연동 끔: 숨은 채 그대로 {stayedHidden} (설정 되돌림 {config.turtleSleepInterruptsHiding})");
        Object.Destroy(turtle.gameObject);
        Object.Destroy(box);

        // --- 도주 중 마취 → 바로 주울 수 있다
        Teleport(player, lane.Start);
        yield return WaitFull(pm);
        turtle = SpawnTurtle(lane.Start + lane.Dir * 7f);
        yield return WaitFrames(3);
        yield return Approach(turtle, lane, sprint: false, pressE: false, until: t => t.State == AnimalTurtle.TurtleState.Flee, null, timeout: 4f);
        Vector2 fleeing = turtle.transform.position;
        // --- 일시정지(timeScale 0) 동안은 멈춰 있다
        Time.timeScale = 0f;
        yield return WaitRealtime(0.5f);
        float pausedMove = Vector2.Distance(fleeing, turtle.transform.position);
        AnimalTurtle.TurtleState pausedState = turtle.State;
        Time.timeScale = 1f;
        Check(pausedMove < 0.01f && pausedState == AnimalTurtle.TurtleState.Flee, $"일시정지 0.5초: 이동 {pausedMove:0.000}, 상태 {pausedState}");
        sleep = turtle.GetComponent<AnimalSleep>();
        sleep.PutToSleep(0f, 3f);
        yield return WaitFrames(3);
        Check(turtle.State == AnimalTurtle.TurtleState.Wander && turtle.CanBePickedUp, $"도주 중 마취 → 상태 {turtle.State}, 들 수 있음 {turtle.CanBePickedUp}");
        Teleport(player, (Vector2)turtle.transform.position - lane.Dir * 1f);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        Check(!turtle.gameObject.activeSelf, "잠든 거북이를 E로 주움");
        DeliverHeldAnimals(gm);
    }

    // ================= 3. 잡기 =================

    private static IEnumerator CatchMatrix(GameManager gm)
    {
        sb.AppendLine("\n== 3. 정면 접근 잡기 (출발 7칸, 도주 중 1.7칸 안이면 E)");
        yield return LoadClearVillage();
        Lane lane = FindLane();
        PassiveDefinition shoes = AssetDatabase.LoadAssetAtPath<PassiveDefinition>(RunningShoesPath);
        foreach (bool withShoes in new[] { false, true })
        {
            if (withShoes) gm.TryPurchasePassive(shoes, 0, 1);
            foreach (PlayerClass c in Classes)
            {
                gm.SelectClass(c);
                foreach (bool sprint in new[] { false, true })
                {
                    GameObject player = Player();
                    PlayerMovement pm = player.GetComponent<PlayerMovement>();
                    Teleport(player, lane.Start);
                    yield return WaitFull(pm);
                    AnimalTurtle turtle = SpawnTurtle(lane.Start + lane.Dir * 7f);
                    yield return WaitFrames(3);
                    float playerSpeed = sprint ? pm.SprintSpeed : pm.WalkSpeed;
                    CatchStats stats = new CatchStats();
                    yield return Approach(turtle, lane, sprint, pressE: true,
                        until: t => !t.gameObject.activeSelf || t.State == AnimalTurtle.TurtleState.Hidden, null, 6f, stats);
                    bool caught = !turtle.gameObject.activeSelf;
                    sb.AppendLine($"  {(withShoes ? "러닝화" : "기본")} {c,-7} {(sprint ? "스프린트" : "걷기   ")} 플레이어 {playerSpeed:0.000} / 거북이 도주 {stats.TurtleFleeSpeed:0.000} (설정 {config.turtleFleeSpeed:0.000}) " +
                                  $"| 놀람 순간 거리 {stats.DistanceAtAlert:0.00}, 도주 시작 거리 {stats.DistanceAtFlee:0.00}, 최소 거리 {stats.MinDistance:0.00} → {(caught ? $"잡음 ({stats.CaughtState})" : "못 잡음 (숨음)")}");
                    Check(caught == playerSpeed > config.turtleFleeSpeed, $"{(withShoes ? "러닝화 " : "")}{c} {(sprint ? "스프린트" : "걷기")} {playerSpeed:0.00} {(playerSpeed > config.turtleFleeSpeed ? "> 도주 속도라 잡음" : "<= 도주 속도라 못 잡음")}");
                    if (!caught) Object.Destroy(turtle.gameObject);
                    DeliverHeldAnimals(gm);
                }
            }
        }
        gm.Passives.Clear();
        gm.SelectClass(PlayerClass.Scout);
    }

    private class CatchStats
    {
        public float DistanceAtAlert = -1, DistanceAtFlee = -1, MinDistance = float.MaxValue, TurtleFleeSpeed;
        public string CaughtState = "-";
    }

    // ================= 4. 저장/복원 =================

    private static IEnumerator SaveRestore(GameManager gm, int seed)
    {
        sb.AppendLine($"\n== 4. 같은 Day 재진입 (격자 3x4 시드 {seed})");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = seed };
        yield return Load("VillageScene");
        DisableMonsters();
        AnimalTurtle turtle = GameObject.Find("GeneratedMap").GetComponentsInChildren<AnimalTurtle>().FirstOrDefault();
        if (turtle == null) { Check(false, "맵에 활성 거북이가 없다"); yield break; }
        string id = turtle.GetComponent<AnimalRescue>().Id;
        GameObject player = Player();
        Teleport(player, (Vector2)turtle.transform.position + Vector2.left * 3f);
        List<string> trace = new List<string>();
        yield return Watch(turtle, t => t.State == AnimalTurtle.TurtleState.Hidden, trace, 15f);
        sb.AppendLine($"  {id}: " + string.Join(" → ", trace));
        Vector3 savedPos = turtle.transform.position;
        AnimalTurtle.TurtleState savedState = turtle.State;
        Teleport(player, (Vector2)savedPos + Vector2.left * 12f); // 다시 들어왔을 때 바로 놀라지 않게 - 스폰은 맵 생성기가 정한다
        yield return Load("TruckScene");
        yield return Load("VillageScene");
        DisableMonsters();
        AnimalRescue back = GameObject.Find("GeneratedMap").GetComponentsInChildren<AnimalRescue>(true).FirstOrDefault(a => a.Id == id);
        AnimalTurtle backTurtle = back != null ? back.GetComponent<AnimalTurtle>() : null;
        float moved = back != null ? Vector2.Distance(back.transform.position, savedPos) : -1f;
        Check(backTurtle != null && back.gameObject.activeInHierarchy && moved < 0.05f && backTurtle.State != AnimalTurtle.TurtleState.Hidden && backTurtle.CanBePickedUp,
            $"나갈 때 {savedState} @ ({savedPos.x:0.00},{savedPos.y:0.00}) → 다시 들어와 위치 차이 {moved:0.000}, 상태 {(backTurtle != null ? backTurtle.State.ToString() : "없음")}, 들 수 있음 {(backTurtle != null && backTurtle.CanBePickedUp)}");
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    // ================= 도구 =================

    private static IEnumerator Approach(AnimalTurtle turtle, Lane lane, bool sprint, bool pressE, System.Func<AnimalTurtle, bool> until,
        List<string> trace, float timeout, CatchStats stats = null, float stopAt = 0f)
    {
        GameObject player = Player();
        Key move = lane.Dir.x > 0 ? Key.D : Key.A;
        float start = Time.time;
        AnimalTurtle.TurtleState last = (AnimalTurtle.TurtleState)(-1);
        bool eDown = false;
        Vector2 fleeStartPos = default;
        float fleeStartTime = -1f;
        while (Time.time - start < timeout)
        {
            if (!turtle.gameObject.activeSelf || until(turtle)) break;
            float d = Vector2.Distance(player.transform.position, turtle.transform.position);
            AnimalTurtle.TurtleState s = turtle.State;
            if (s != last)
            {
                trace?.Add($"{s} {Time.time - start:0.00}s (거리 {d:0.00})");
                if (stats != null && s == AnimalTurtle.TurtleState.Alert) stats.DistanceAtAlert = d;
                if (stats != null && s == AnimalTurtle.TurtleState.Flee) { stats.DistanceAtFlee = d; fleeStartPos = turtle.transform.position; fleeStartTime = Time.time; }
                last = s;
            }
            if (stats != null)
            {
                stats.MinDistance = Mathf.Min(stats.MinDistance, d);
                if (s == AnimalTurtle.TurtleState.Flee && fleeStartTime > 0f && Time.time - fleeStartTime > 0.15f)
                    stats.TurtleFleeSpeed = Vector2.Distance(fleeStartPos, turtle.transform.position) / (Time.time - fleeStartTime);
                stats.CaughtState = s.ToString();
            }

            eDown = pressE && d <= PickupReach && turtle.CanBePickedUp && !eDown;
            // stopAt 안으로 들어오면 멈춰 선다 - 거북이가 플레이어보다 느려서, 계속 달리면 지나쳐 버려 도주 방향이 뒤집힌다.
            List<Key> keys = new List<Key>();
            if (d > stopAt) keys.Add(move);
            if (sprint && d > stopAt) keys.Add(Key.LeftShift);
            if (eDown) keys.Add(Key.E);
            InputSystem.QueueStateEvent(vkb, new KeyboardState(keys.ToArray()));
            yield return WaitFrames(1);
        }
        if (turtle.gameObject.activeSelf && trace != null && turtle.State != last) trace.Add($"{turtle.State} {Time.time - start:0.00}s");
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static IEnumerator Watch(AnimalTurtle turtle, System.Func<AnimalTurtle, bool> until, List<string> trace, float timeout)
    {
        float start = Time.time;
        AnimalTurtle.TurtleState last = (AnimalTurtle.TurtleState)(-1);
        while (Time.time - start < timeout && turtle.gameObject.activeSelf)
        {
            if (turtle.State != last)
            {
                last = turtle.State;
                trace?.Add($"{last} {Time.time - start:0.00}s");
                if (until(turtle) && trace?.Count > 1) break;
            }
            if (trace == null && until(turtle)) break;
            yield return WaitFrames(1);
        }
    }

    // trace 항목 "Emerge 3.01s ..."에서 시간을 읽는다.
    private static float ParseTime(List<string> trace, string state)
    {
        string item = trace.FirstOrDefault(s => s.StartsWith(state + " "));
        if (item == null) return -1f;
        string t = item.Split(' ')[1].TrimEnd('s');
        return float.TryParse(t, out float v) ? v : -1f;
    }

    private static AnimalTurtle SpawnTurtle(Vector2 position)
    {
        GameObject go = Object.Instantiate(config.turtlePrefab, position, Quaternion.identity);
        go.name = $"TestTurtle_{turtleSerial++}"; // Id가 겹치면 "이미 구조됨"으로 꺼진다 - 매번 다른 이름
        return go.GetComponent<AnimalTurtle>();
    }

    private static GameObject BuildBox(Vector2 center, float half)
    {
        GameObject root = new GameObject("TurtleTestBox");
        foreach ((Vector2 offset, Vector2 size) in new[]
        {
            (new Vector2(half, 0f), new Vector2(0.2f, half * 2f + 0.4f)), (new Vector2(-half, 0f), new Vector2(0.2f, half * 2f + 0.4f)),
            (new Vector2(0f, half), new Vector2(half * 2f + 0.4f, 0.2f)), (new Vector2(0f, -half), new Vector2(half * 2f + 0.4f, 0.2f)),
        })
        {
            GameObject wall = new GameObject("Wall");
            wall.transform.SetParent(root.transform);
            wall.transform.position = center + offset;
            wall.AddComponent<BoxCollider2D>().size = size;
        }
        return root;
    }

    private static IEnumerator LoadClearVillage()
    {
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        yield return Load("VillageScene");
        VillageMapGenerator.EditorLayoutOverride = null;
        DisableMonsters();
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        yield return WaitFrames(2);
    }

    private static void DisableMonsters()
    {
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);
    }

    // 플레이어가 7칸 + 거북이 도주 4칸 + 여유를 곧게 달릴 수 있는 길: 막는 충돌체가 없고 트럭에서 떨어진 곳.
    private static Lane FindLane()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        Vector2 truck = Object.FindAnyObjectByType<TruckPoint>() is TruckPoint tp ? (Vector2)tp.transform.position : new Vector2(9999, 9999);
        IEnumerable<Vector2> starts = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.GetComponent<AnimalRescue>() != null || t.GetComponent<MonsterHealth>() != null)
            .Select(t => (Vector2)t.position).Append((Vector2)Player().transform.position);
        foreach (Vector2 s in starts)
        {
            foreach (Vector2 dir in new[] { Vector2.right, Vector2.left })
            {
                if (Vector2.Distance(s, truck) < 6f || Vector2.Distance(s + dir * 14f, truck) < 6f) continue;
                bool blocked = Physics2D.OverlapCircleAll(s, 0.9f).Any(GridMapAuditInspector.IsBlocker) ||
                               Physics2D.CircleCastAll(s, 0.9f, dir, 16f).Any(h => GridMapAuditInspector.IsBlocker(h.collider));
                if (!blocked) return new Lane { Start = s, Dir = dir };
            }
        }
        throw new System.Exception("막힌 데 없는 16칸 직선 길을 찾지 못했다");
    }

    private static void DeliverHeldAnimals(GameManager gm)
    {
        for (int i = 0; i < gm.InventorySlotCount; i++)
        {
            if (gm.GetSlot(i).Type != InventoryItemType.Animal) continue;
            gm.SelectSlot(i);
            gm.TryDeliverSelectedAnimal();
        }
    }

    private static GameObject Player() => GameObject.FindGameObjectWithTag("Player");

    private static void Teleport(GameObject player, Vector2 position)
    {
        Vector3 p = new Vector3(position.x, position.y, player.transform.position.z);
        player.transform.position = p;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = position;
    }

    // 스프린트 테스트가 스태미나 부족으로 흐려지지 않게 가득 찰 때까지 기다린다.
    private static IEnumerator WaitFull(PlayerMovement pm)
    {
        float until = Time.realtimeSinceStartup + 10f;
        while (pm.Stamina < pm.MaxStamina - 0.01f && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(2);
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static string Subtitle()
    {
        GameObject go = GameObject.Find("SubtitleManager");
        TextMeshProUGUI label = go != null ? go.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        return label != null ? label.text : "(자막 없음)";
    }

    private static void GiveMileage(GameManager gm, int amount)
    {
        if (amount <= 0) return;
        gm.AddCargo(amount);
        gm.SettleCargo(1);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        yield return WaitForScene(sceneName);
    }

    private static IEnumerator LoadWith(string scene, System.Action onLoaded)
    {
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> h = (s, m) =>
        {
            if (s.name != scene || loaded) return;
            loaded = true;
            onLoaded?.Invoke();
        };
        SceneManager.sceneLoaded += h;
        SceneManager.LoadScene(scene);
        int guard = 0;
        while (!loaded && guard++ < 600) yield return null;
        SceneManager.sceneLoaded -= h;
        yield return WaitFrames(5);
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
