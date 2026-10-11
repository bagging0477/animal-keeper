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
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 격자 맵 전체 점검(읽기 전용 - 게임 코드·프리팹·씬은 바꾸지 않는다). 결과는 Logs/MapAudit/에 남는다.
/// Tools > Village Rooms > Map Audit 메뉴:
///   1 Bulk Seeds: 3x4 시드 1~200, 4x5 시드 1~100, 3x3 시드 1~50을 시드마다 두 번 만들어 규칙 위반과 재생성 결과 차이를 찾는다(bulk.txt).
///     3x4 시드 1~20, 4x5 시드 1~6은 같은 영역·배율로 찍어 모음 이미지로 합치고, 위반이 나온 시드는 위치를 표시해 따로 찍는다.
///   2 Leak: 같은 시드로 씬을 100번 다시 불러오기 / 같은 씬에서 생성기만 100번 다시 만들기 - 오브젝트·메시·NavMesh·메모리 수를 1/50/100번째에 잰다(leak.txt).
///   3 Flow: TruckScene에서 시작해 Day 30번, 트럭 왕복 복원, 빠른 씬 전환, 일시정지·전환 중 생성, 격자 실패 → 한 줄 대체(flow.txt).
///   5 Views: 플레이어 카메라 크기로 길-방 접점·방 모서리·맵 경계를 찍는다(views/).
/// 생성기의 시드·격자 크기는 VillageMapGenerator.EditorLayoutOverride(에디터 전용 훅)로만 정한다.
/// </summary>
[InitializeOnLoad]
public static class GridMapAudit
{
    public const string OutDir = "Logs/MapAudit";
    private const string VillageScenePath = "Assets/Scenes/VillageScene.unity", TruckScenePath = "Assets/Scenes/TruckScene.unity";
    private const string KeyActive = "GMA.Active", KeyQueue = "GMA.Queue", KeyStage = "GMA.Stage", KeyRunInBackground = "GMA.RunInBackground", KeyPending = "GMA.Pending";
    private const string Prefix = "[MapAudit] ";

    public static bool SuppressLogs;
    private static List<string> logSink;
    private static readonly List<string> stageLogs = new List<string>();
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static bool exitProbe;
    private static readonly List<string> exitLogs = new List<string>();

    static GridMapAudit()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // Play 종료 직후 스크립트 재컴파일(도메인 리로드)이 끼면 delayCall이 사라지므로, 다시 로드된 뒤 남은 단계를 이어서 시작한다.
        if (SessionState.GetBool(KeyActive, false) && SessionState.GetBool(KeyPending, false))
            EditorApplication.delayCall += StartNextStage;
    }

    [MenuItem("Tools/Village Rooms/Map Audit/Run All (bulk, leak, flow, views)")]
    private static void RunAll() => Begin("bulk,leak,flow,views");
    [MenuItem("Tools/Village Rooms/Map Audit/0 Smoke (3x4 seeds 1-3)")]
    private static void RunSmoke() => Begin("smoke");
    [MenuItem("Tools/Village Rooms/Map Audit/1 Bulk Seeds (3x4 x200, 4x5 x100, 3x3 x50)")]
    private static void RunBulk() => Begin("bulk");
    [MenuItem("Tools/Village Rooms/Map Audit/2 Leak and Residue (100x)")]
    private static void RunLeak() => Begin("leak");
    [MenuItem("Tools/Village Rooms/Map Audit/3 Day and Scene Flow")]
    private static void RunFlow() => Begin("flow");
    [MenuItem("Tools/Village Rooms/Map Audit/5 Player-view Captures")]
    private static void RunViews() => Begin("views");

    private static void Begin(string queue)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning(Prefix + "Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(OutDir);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyQueue, queue);
        SessionState.SetBool(KeyRunInBackground, PlayerSettings.runInBackground);
        StartNextStage();
    }

    private static void StartNextStage()
    {
        // EnteredEditMode 직후의 delayCall은 아직 Play 전환이 끝나지 않은 프레임일 수 있다 - 끝날 때까지 다음 프레임으로 미룬다.
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += StartNextStage;
            return;
        }
        SessionState.SetBool(KeyPending, false);
        string queue = SessionState.GetString(KeyQueue, "");
        if (string.IsNullOrEmpty(queue))
        {
            PlayerSettings.runInBackground = SessionState.GetBool(KeyRunInBackground, false);
            SessionState.SetBool(KeyActive, false);
            VillageMapGenerator.EditorLayoutOverride = null;
            File.WriteAllText($"{OutDir}/done.txt", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Debug.Log(Prefix + "모든 단계 끝 - " + Path.GetFullPath(OutDir));
            return;
        }
        string stage = queue.Split(',')[0];
        SessionState.SetString(KeyStage, stage);
        EditorSceneManager.OpenScene(stage == "flow" ? TruckScenePath : VillageScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        string stage = SessionState.GetString(KeyStage, "");
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            AppendCrossSession($"[{stage}] Play 시작 직후");
            stageLogs.Clear();
            exitLogs.Clear();
            exitProbe = false;
            Application.logMessageReceived += OnLog;
            nested.Clear();
            routine = stage switch
            {
                "smoke" => Bulk(true),
                "bulk" => Bulk(false),
                "leak" => Leak(),
                "flow" => Flow(),
                "views" => Views(),
                _ => null,
            };
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            Application.logMessageReceived -= OnLog;
            VillageMapGenerator.EditorLayoutOverride = null;
            if (exitProbe)
            {
                File.AppendAllText($"{OutDir}/flow.txt", "\n== F. 맵 생성 직후(검증 코루틴 전) Play 종료\n" +
                    $"  종료 중 경고/에러 {exitLogs.Count}건\n" + string.Join("\n", exitLogs.Select(l => "    " + l)) + "\n", new UTF8Encoding(false));
                exitProbe = false;
            }
            AppendCrossSession($"[{stage}] Play 종료 뒤(에디터 모드)");
            string queue = SessionState.GetString(KeyQueue, "");
            int comma = queue.IndexOf(',');
            SessionState.SetString(KeyQueue, comma < 0 ? "" : queue.Substring(comma + 1));
            SessionState.SetBool(KeyPending, true);
            EditorApplication.delayCall += StartNextStage;
        }
    }

    // 도메인 리로드가 꺼져 있으므로 Play 사이에 남는 static 값을 Play 시작/종료 때마다 기록한다.
    private static void AppendCrossSession(string when)
    {
        FieldInfo f = typeof(VillageMapGenerator).GetField("activeNavMeshDataInstance", BindingFlags.NonPublic | BindingFlags.Static);
        NavMeshDataInstance inst = f != null ? (NavMeshDataInstance)f.GetValue(null) : default;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        int navData = Resources.FindObjectsOfTypeAll<NavMeshData>().Length;
        string line = $"{System.DateTime.Now:HH:mm:ss} {when}: activeNavMeshDataInstance.valid={inst.valid}, NavMesh 정점 {tri.vertices.Length}, NavMeshData 객체 {navData}, " +
                      $"EditorLayoutOverride={(VillageMapGenerator.EditorLayoutOverride.HasValue ? "설정됨" : "null")}, MonsterHealth.Active {MonsterHealth.Active.Count}, AnimalSleep.Active {AnimalSleep.Active.Count}\n";
        File.AppendAllText($"{OutDir}/static_cross_session.txt", line, new UTF8Encoding(false));
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith(Prefix) || SuppressLogs) return;
        string line = $"{type}: {condition.Split('\n')[0]}";
        if (exitProbe) { exitLogs.Add(line); return; }
        stageLogs.Add(line);
        logSink?.Add(line);
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
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            File.AppendAllText($"{OutDir}/aborted.txt", $"{System.DateTime.Now} [{SessionState.GetString(KeyStage, "")}] 중단: {e}\n", new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            routine = null;
            nested.Clear();
            EditorApplication.ExitPlaymode();
        }
    }

    private static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    private static IEnumerator Load(string scene, int settleFrames, System.Action onLoaded = null)
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
        if (!loaded) throw new System.Exception($"{scene} 로드를 600프레임 동안 기다렸지만 끝나지 않았다");
        for (int i = 0; i < settleFrames; i++) yield return null;
    }

    private static void SetOverride(int columns, int rows, int? seed)
    {
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = columns, Rows = rows, Seed = seed };
    }

    // 격자 크기별 고정 캡처 영역(방 칸 크기·틈은 계획에서 읽는다 - 같은 크기면 모든 시드가 같은 영역).
    private static Rect MapArea(GridLayoutPlan plan)
    {
        float w = plan.Columns * plan.CellWidth + (plan.Columns - 1) * plan.CorridorGap;
        float h = plan.Rows * plan.CellHeight + (plan.Rows - 1) * plan.CorridorGap;
        return new Rect(-4f, -4f, w + 8f, h + 8f);
    }

    // ===================================================================== 1. 대량 생성
    private static IEnumerator Bulk(bool smoke)
    {
        (int c, int r, int n)[] sizes = smoke ? new[] { (3, 4, 3) } : new[] { (3, 4, 200), (4, 5, 100), (3, 3, 50) };
        string imgDir = $"{OutDir}/maps";
        Directory.CreateDirectory(imgDir);
        List<AuditSeedResult> all = new List<AuditSeedResult>();
        List<string> determinism = new List<string>();
        Dictionary<string, List<string>> sheets = new Dictionary<string, List<string>>();
        List<string> violationImages = new List<string>();
        int spawnImages = 0;
        System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();

        foreach ((int cols, int rows, int count) in sizes)
        {
            for (int seed = 1; seed <= count; seed++)
            {
                AuditSnapshot snap = null;
                logSink = new List<string>();
                SetOverride(cols, rows, seed);
                yield return Load("VillageScene", 5, () => snap = AuditSnapshot.Take());
                List<string> logsA = logSink;
                logSink = null;
                AuditSeedResult res = GridMapAuditInspector.Inspect(snap, cols, rows, seed, logsA);
                all.Add(res);

                GridLayoutPlan plan = Object.FindAnyObjectByType<VillageMapGenerator>()?.LastGridPlan;
                bool sheet = (cols == 3 && rows == 4 && seed <= 20) || (cols == 4 && rows == 5 && seed <= 6);
                List<AuditViolation> located = res.Violations.Where(v => v.Pos.HasValue && v.Category != GridMapAuditInspector.CLogs).ToList();
                // 빨간 테두리·위반 캡처는 구조 문제(스폰 위치·겹침, 콘솔 로그 제외)가 있는 시드. 스폰 문제만 있는 시드는 처음 6장만 찍는다.
                bool flagged = res.Violations.Any(v => v.Category != GridMapAuditInspector.CLogs && v.Category != GridMapAuditInspector.CSpawnPlace && v.Category != GridMapAuditInspector.CSpawnOverlap);
                bool spawnOnly = !flagged && res.Violations.Any(v => v.Category == GridMapAuditInspector.CSpawnPlace || v.Category == GridMapAuditInspector.CSpawnOverlap);
                bool structural = flagged;
                if (spawnOnly && spawnImages++ < 6) flagged = true;
                if (plan != null && plan.Success && (sheet || (flagged && violationImages.Count < 80)))
                {
                    Rect area = MapArea(plan);
                    List<(Vector2, Color)> markers = new List<(Vector2, Color)> { (res.TruckPos, Color.yellow), (res.PlayerPos, Color.cyan) };
                    if (sheet)
                    {
                        string file = $"{imgDir}/sheet_{cols}x{rows}_seed{seed:000}.png";
                        GridMapAuditImages.CaptureMap(area, 4, file, $"{cols}x{rows} #{seed}{(structural ? " !" : "")}", markers, structural);
                        string key = $"{cols}x{rows}";
                        if (!sheets.ContainsKey(key)) sheets[key] = new List<string>();
                        sheets[key].Add(file);
                    }
                    if (flagged)
                    {
                        string file = $"{imgDir}/violation_{cols}x{rows}_seed{seed:000}.png";
                        GridMapAuditImages.CaptureMap(area, 8, file, $"{cols}x{rows} #{seed} !", markers.Concat(located.Select(v => (v.Pos.Value, Color.red))), true);
                        violationImages.Add(file);
                    }
                }

                // 같은 시드를 한 번 더 만들어 결과가 완전히 같은지.
                AuditSnapshot snap2 = null;
                logSink = new List<string>();
                SetOverride(cols, rows, seed);
                yield return Load("VillageScene", 5, () => snap2 = AuditSnapshot.Take());
                logSink = null;
                SuppressLogs = true;
                AuditSeedResult res2;
                try { res2 = GridMapAuditInspector.Inspect(snap2, cols, rows, seed, new string[0]); }
                finally { SuppressLogs = false; }
                string diff = FirstDiff(res.SignatureLines, res2.SignatureLines);
                if (diff != null)
                {
                    res.Add(GridMapAuditInspector.CDeterminism, "두 번째 생성과 다르다: " + diff);
                    determinism.Add($"{cols}x{rows} 시드 {seed}: {diff}");
                }

                if (seed % 10 == 0 || seed == count)
                    Debug.Log(Prefix + $"{cols}x{rows} {seed}/{count} ({total.Elapsed.TotalMinutes:F1}분) - 위반 시드 {all.Count(x => x.Columns == cols && x.Rows == rows && x.Violations.Count > 0)}");
            }
        }
        VillageMapGenerator.EditorLayoutOverride = null;

        foreach (KeyValuePair<string, List<string>> kv in sheets)
            GridMapAuditImages.ContactSheet(kv.Value, kv.Key == "3x4" ? 5 : 3, $"{OutDir}/contact_{kv.Key}.png", $"GRID {kv.Key} SEEDS 1-{kv.Value.Count}");

        WriteBulkReport(all, determinism, violationImages, total.Elapsed.TotalMinutes, smoke);
    }

    private static string FirstDiff(List<string> a, List<string> b)
    {
        for (int i = 0; i < Mathf.Max(a.Count, b.Count); i++)
        {
            string x = i < a.Count ? a[i] : "(없음)", y = i < b.Count ? b[i] : "(없음)";
            if (x != y) return $"줄 {i}: [{Trim(x)}] ≠ [{Trim(y)}]";
        }
        return null;
    }

    private static string Trim(string s) => s.Length > 160 ? s.Substring(0, 160) + "…" : s;

    private static void WriteBulkReport(List<AuditSeedResult> all, List<string> determinism, List<string> images, double minutes, bool smoke)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"GridMapAudit 대량 생성 점검 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - {all.Count}개 시드, {minutes:F1}분");
        foreach (IGrouping<string, AuditSeedResult> size in all.GroupBy(x => $"{x.Columns}x{x.Rows}"))
        {
            List<AuditSeedResult> set = size.ToList();
            sb.AppendLine();
            sb.AppendLine($"== {size.Key}: 시드 {set.Min(x => x.Seed)}~{set.Max(x => x.Seed)} ({set.Count}개)");
            sb.AppendLine("  항목 | 위반 시드 수 | 위반 건수 | 위반 시드");
            foreach (string cat in GridMapAuditInspector.Categories)
            {
                List<AuditSeedResult> bad = set.Where(x => x.Violations.Any(v => v.Category == cat)).ToList();
                int n = set.Sum(x => x.Violations.Count(v => v.Category == cat));
                sb.AppendLine($"  {cat} | {bad.Count} | {n} | {(bad.Count == 0 ? "-" : string.Join(",", bad.Select(x => x.Seed).Take(60)) + (bad.Count > 60 ? " …" : ""))}");
            }
            sb.AppendLine("  통계(시드 합계 / 최소~최대):");
            foreach (string key in set.SelectMany(x => x.Stats.Keys).Distinct().OrderBy(k => k))
            {
                List<double> vals = set.Select(x => x.Stats.TryGetValue(key, out double v) ? v : 0).ToList();
                sb.AppendLine($"    {key}: 합 {vals.Sum():0.##}, {vals.Min():0.###}~{vals.Max():0.###}, 0 아닌 시드 {vals.Count(v => v != 0)}");
            }
            sb.AppendLine("  트럭 방: " + string.Join(", ", set.GroupBy(x => x.TruckRoom ?? "?").OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
        }

        sb.AppendLine();
        sb.AppendLine($"== 같은 시드 재생성 차이: {determinism.Count}건");
        foreach (string d in determinism.Take(50)) sb.AppendLine("  " + d);

        sb.AppendLine();
        sb.AppendLine("== 콘솔 경고/에러 (모든 시드, 내용별)");
        foreach (IGrouping<string, string> g in all.SelectMany(x => x.Logs).GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l, @"-?\d+(\.\d+)?", "#")).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  ({g.Count()}) {Trim(g.First())}");

        sb.AppendLine();
        sb.AppendLine("== 시드별 위반 (콘솔 로그 제외, 시드·항목마다 최대 6건)");
        foreach (AuditSeedResult x in all)
        {
            List<AuditViolation> vs = x.Violations.Where(v => v.Category != GridMapAuditInspector.CLogs).ToList();
            if (vs.Count == 0) continue;
            sb.AppendLine($"  {x.Columns}x{x.Rows} 시드 {x.Seed} (트럭 방 {x.TruckRoom}, {string.Join(", ", x.Notes)}): {vs.Count}건");
            foreach (IGrouping<string, AuditViolation> g in vs.GroupBy(v => v.Category))
            {
                foreach (AuditViolation v in g.Take(6)) sb.AppendLine($"      [{v.Category}] {v.Message}");
                if (g.Count() > 6) sb.AppendLine($"      [{g.Key}] ... 외 {g.Count() - 6}건");
            }
        }

        sb.AppendLine();
        sb.AppendLine("== 그림");
        sb.AppendLine("  모음: Logs/MapAudit/contact_3x4.png, contact_4x5.png (노란 네모 = 트럭, 하늘색 = 플레이어, 빨간 테두리 = 위반 있는 시드)");
        foreach (string f in images) sb.AppendLine("  위반: " + f);

        File.WriteAllText($"{OutDir}/{(smoke ? "smoke" : "bulk")}.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log(Prefix + "대량 점검 보고서: " + Path.GetFullPath($"{OutDir}/{(smoke ? "smoke" : "bulk")}.txt"));
    }

    // ===================================================================== 2. 반복 생성(누수·잔여물)
    private static string Metrics(string label)
    {
        int sceneObjects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
        Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>().Where(m => !EditorUtility.IsPersistent(m)).ToArray();
        string meshGroups = string.Join(", ", meshes.GroupBy(m => string.IsNullOrEmpty(m.name) ? "(이름 없음)" : m.name).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} {g.Count()}"));
        int materials = Resources.FindObjectsOfTypeAll<Material>().Count(m => !EditorUtility.IsPersistent(m));
        int textures = Resources.FindObjectsOfTypeAll<Texture>().Count(t => !EditorUtility.IsPersistent(t));
        int navData = Resources.FindObjectsOfTypeAll<NavMeshData>().Length;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        int navSources = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include).Count(f => f.name.Contains("NavGround"));
        int obstacles = Object.FindObjectsByType<NavMeshObstacle>(FindObjectsInactive.Include).Length;
        int casters = Object.FindObjectsByType<ShadowCaster2D>(FindObjectsInactive.Include).Length;
        int castersAll = Resources.FindObjectsOfTypeAll<ShadowCaster2D>().Length;
        long gc = System.GC.GetTotalMemory(true);
        return $"  {label} | 씬 오브젝트 {sceneObjects} | 런타임 Mesh {meshes.Length} ({meshGroups}) | 런타임 Material {materials} | 런타임 Texture {textures} | " +
               $"NavMeshData {navData} | NavMesh 정점 {tri.vertices.Length} | NavGround 소스 {navSources} | NavMeshObstacle {obstacles} | ShadowCaster2D 씬 {casters} / 메모리 전체 {castersAll} | " +
               $"GC 힙 {gc / 1048576.0:F1}MB | Mono 사용 {Profiler.GetMonoUsedSizeLong() / 1048576.0:F1}MB | 총 할당 {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1}MB";
    }

    private static IEnumerator Leak()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"GridMapAudit 반복 생성 점검 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} (3x4, 시드 1 고정)");
        int[] marks = { 1, 50, 100 };

        sb.AppendLine();
        sb.AppendLine("== A. VillageScene을 100번 다시 불러오기(실제 게임과 같은 경로 - LoadScene이 안 쓰는 에셋을 내린다)");
        SetOverride(3, 4, 1);
        for (int i = 1; i <= 100; i++)
        {
            yield return Load("VillageScene", 3);
            if (marks.Contains(i)) sb.AppendLine(Metrics($"{i,3}번째"));
        }

        sb.AppendLine();
        sb.AppendLine("== B. 같은 씬에서 GeneratedMap을 지우고 생성기를 복제해 100번 다시 만들기(씬 언로드 정리 없이 생성기 자체가 지우는지)");
        yield return Load("VillageScene", 5);
        sb.AppendLine(Metrics("  0번째(새로 불러온 직후)"));
        for (int i = 1; i <= 100; i++)
        {
            GameObject root = GameObject.Find("GeneratedMap");
            if (root != null) Object.DestroyImmediate(root);
            VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            GameObject clone = Object.Instantiate(gen.gameObject);
            clone.name = gen.gameObject.name;
            Object.DestroyImmediate(gen.gameObject);
            yield return Frames(3);
            if (marks.Contains(i)) sb.AppendLine(Metrics($"{i,3}번째"));
        }
        AsyncOperation op = Resources.UnloadUnusedAssets();
        while (!op.isDone) yield return null;
        sb.AppendLine(Metrics("Resources.UnloadUnusedAssets 뒤"));
        VillageMapGenerator.EditorLayoutOverride = null;

        sb.AppendLine();
        sb.AppendLine($"이 단계 콘솔 경고/에러: {stageLogs.Count}건");
        foreach (IGrouping<string, string> g in stageLogs.GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l, @"-?\d+(\.\d+)?", "#")).OrderByDescending(g => g.Count()).Take(20))
            sb.AppendLine($"  ({g.Count()}) {Trim(g.First())}");
        File.WriteAllText($"{OutDir}/leak.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log(Prefix + "반복 생성 보고서: " + Path.GetFullPath($"{OutDir}/leak.txt"));
    }

    // ===================================================================== 3. 흐름
    private static string MapSignature()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        if (root == null || gen == null) return "(맵 없음)";
        StringBuilder sb = new StringBuilder(gen.LastGridPlan != null ? gen.LastGridPlan.Signature() : "-");
        foreach (Collider2D c in root.GetComponentsInChildren<Collider2D>().Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<AnimalRescue>() == null)
                     .OrderBy(c => GridMapAuditInspector.HierarchyPath(c.transform), System.StringComparer.Ordinal))
            sb.Append('|').Append(GridMapAuditInspector.HierarchyPath(c.transform)).Append('@').Append(c.bounds.center.x.ToString("F2")).Append(',').Append(c.bounds.center.y.ToString("F2"));
        return sb.ToString();
    }

    private static int CountByName(string name) =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Count(t => t.name == name && t.parent == null);

    private static int DontDestroyRoots()
    {
        GameObject probe = new GameObject("GMA_Probe");
        Object.DontDestroyOnLoad(probe);
        int n = probe.scene.GetRootGameObjects().Length - 1;
        Object.DestroyImmediate(probe);
        return n;
    }

    private static IEnumerator Flow()
    {
        StringBuilder sb = new StringBuilder();
        List<string> failures = new List<string>();
        sb.AppendLine($"GridMapAudit 흐름 점검 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} (씬 설정 그대로, EditorLayoutOverride 없음)");
        VillageMapGenerator.EditorLayoutOverride = null;
        yield return Frames(10);
        GameManager gm = GameManager.Instance;
        if (gm == null) { sb.AppendLine("TruckScene에서 GameManager가 생기지 않았다 - 중단"); File.WriteAllText($"{OutDir}/flow.txt", sb.ToString()); yield break; }
        if (gm.NeedsStartingClassSelection) gm.CompleteStartingClassSelection();

        // ---- A. Day 30번
        sb.AppendLine();
        sb.AppendLine("== A. Day 30번 연속 (진입 → 30프레임 → 동물 1마리 납품 처리 → 트럭 왕복 → 재진입 비교 → ResetDay)");
        Dictionary<string, int> planSeen = new Dictionary<string, int>();
        int dayFail = 0;
        float maxRestoreError = 0f;
        for (int day = 0; day < 30; day++)
        {
            int cycle = gm.CycleCount;
            int failBefore = failures.Count;
            AuditSnapshot first = null;
            yield return Load("VillageScene", 5, () => first = AuditSnapshot.Take());
            VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            GridLayoutPlan plan = gen != null ? gen.LastGridPlan : null;
            if (plan == null || !plan.Success) { failures.Add($"Day {cycle}: 격자 맵이 만들어지지 않았다"); break; }
            string planSig = plan.Signature();
            string mapSig = MapSignature();
            if (planSeen.TryGetValue(planSig, out int prevDay)) failures.Add($"Day {cycle}: Day {prevDay}과 같은 격자 계획");
            planSeen[planSig] = cycle;
            foreach (string p in gen.LastGridValidation?.Problems ?? new List<string> { "검증 결과 없음" }) failures.Add($"Day {cycle}: 생성기 검증 - {p}");
            List<AnimalRescue> animals = GameObject.Find("GeneratedMap").GetComponentsInChildren<AnimalRescue>(true).ToList();
            if (animals.Any(a => !a.gameObject.activeInHierarchy)) failures.Add($"Day {cycle}: 새 Day 첫 진입인데 비활성 동물 {animals.Count(a => !a.gameObject.activeInHierarchy)}마리");

            yield return Frames(30); // 동물 AI가 조금 움직이게 둔다
            AnimalRescue rescued = animals.FirstOrDefault(a => a != null && a.gameObject.activeInHierarchy);
            string rescuedId = rescued != null ? rescued.Id : null;
            if (rescuedId != null) gm.MarkAnimalRescuedToday(rescuedId);
            Dictionary<string, Vector3> before = animals.Where(a => a != null && a.gameObject.activeInHierarchy && a.Id != rescuedId).ToDictionary(a => a.Id, a => a.transform.position);

            yield return Load("TruckScene", 3);
            AuditSnapshot second = null;
            yield return Load("VillageScene", 5, () => second = AuditSnapshot.Take());
            gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            if (gen?.LastGridPlan?.Signature() != planSig) failures.Add($"Day {cycle}: 재진입 격자 계획이 다르다");
            if (MapSignature() != mapSig) failures.Add($"Day {cycle}: 재진입 맵(장애물 배치 포함)이 다르다");
            List<AnimalRescue> again = GameObject.Find("GeneratedMap").GetComponentsInChildren<AnimalRescue>(true).ToList();
            if (rescuedId != null && again.Any(a => a.Id == rescuedId && a.gameObject.activeInHierarchy)) failures.Add($"Day {cycle}: 납품한 동물 {rescuedId}가 다시 나왔다");
            foreach (KeyValuePair<string, Vector3> kv in before)
            {
                AuditEntity e = second?.Entities.FirstOrDefault(x => x.Kind == "동물" && x.Path == kv.Key);
                if (e == null) { failures.Add($"Day {cycle}: 동물 {kv.Key}가 재진입 후 없다"); continue; }
                bool saved = gm.TryGetAnimalFieldState(kv.Key, out Vector3 savedPos, out _);
                float err = Vector2.Distance(e.Pos, saved ? (Vector2)savedPos : (Vector2)kv.Value);
                maxRestoreError = Mathf.Max(maxRestoreError, err);
                if (!saved) failures.Add($"Day {cycle}: 동물 {kv.Key}의 위치가 저장되지 않았다");
                else if (err > 0.01f) failures.Add($"Day {cycle}: 동물 {kv.Key}가 저장 위치 {savedPos}가 아닌 {e.Pos}에 복원 (차이 {err:F3})");
                float drift = Vector2.Distance(e.Pos, kv.Value);
                if (drift > 1.5f) failures.Add($"Day {cycle}: 동물 {kv.Key}가 떠날 때 위치 {kv.Value}에서 {drift:F2}칸 떨어진 곳에 복원");
            }
            int activeAgain = again.Count(a => a.gameObject.activeInHierarchy);
            sb.AppendLine($"  Day {cycle}: 계획 시드 {plan.Seed}, 트럭 방 {gen?.LastGridValidation?.TruckRoomPrefab}, 동물 {animals.Count} → 재진입 활성 {activeAgain} (납품 1), 실패 {failures.Count - failBefore}");
            if (failures.Count > failBefore) dayFail++;

            yield return Load("TruckScene", 3);
            gm.ResetDay();
            if (gm.CycleCount != cycle + 1) failures.Add($"Day {cycle}: ResetDay 뒤 CycleCount {gm.CycleCount}");
        }
        sb.AppendLine($"  결과: 실패한 Day {dayFail}/30, 서로 다른 계획 {planSeen.Count}개, 재진입 위치 복원 최대 오차 {maxRestoreError:F4}칸");

        // ---- B. 빠른 씬 전환
        sb.AppendLine();
        sb.AppendLine("== B. 빠른 씬 전환");
        int ddolBefore = DontDestroyRoots();
        for (int i = 0; i < 10; i++)
        {
            SceneManager.LoadScene("TruckScene"); yield return null;
            SceneManager.LoadScene("VillageScene"); yield return null;
            SceneManager.LoadScene("ShelterScene"); yield return null;
        }
        yield return Load("TruckScene", 5);
        sb.AppendLine($"  1프레임 간격 Truck→Village→Shelter x10 뒤 TruckScene: GeneratedMap {CountByName("GeneratedMap")}, VillageMapGenerator {Object.FindObjectsByType<VillageMapGenerator>().Length}, " +
                      $"NavMesh 정점 {NavMesh.CalculateTriangulation().vertices.Length}, DontDestroyOnLoad 루트 {ddolBefore} → {DontDestroyRoots()}");
        if (CountByName("GeneratedMap") > 0) failures.Add("빠른 전환 뒤 TruckScene에 GeneratedMap이 남았다");

        for (int i = 0; i < 10; i++)
        {
            SceneManager.LoadScene("VillageScene"); yield return null;
            SceneManager.LoadScene("TruckScene"); yield return null;
        }
        yield return Load("VillageScene", 5);
        int maps = CountByName("GeneratedMap"), players = CountByName("Player"), gens = Object.FindObjectsByType<VillageMapGenerator>().Length;
        VillageMapGenerator g2 = Object.FindAnyObjectByType<VillageMapGenerator>();
        sb.AppendLine($"  Village↔Truck 1프레임 간격 x10 뒤 VillageScene: GeneratedMap {maps}, Player {players}, 생성기 {gens}, 검증 문제 {g2?.LastGridValidation?.Problems.Count.ToString() ?? "결과 없음"}");
        if (maps != 1 || players != 1 || gens != 1) failures.Add($"빠른 전환 뒤 VillageScene에 GeneratedMap {maps} / Player {players} / 생성기 {gens}");

        // 같은 프레임 두 번 LoadScene(가드 없이 직접).
        int villageLoads = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> counter = (s, m) => { if (s.name == "VillageScene") villageLoads++; };
        SceneManager.sceneLoaded += counter;
        SceneManager.LoadScene("VillageScene");
        SceneManager.LoadScene("VillageScene");
        yield return Frames(8);
        SceneManager.sceneLoaded -= counter;
        sb.AppendLine($"  같은 프레임에 LoadScene(VillageScene) 두 번: VillageScene 로드 {villageLoads}번, GeneratedMap {CountByName("GeneratedMap")}, 생성기 {Object.FindObjectsByType<VillageMapGenerator>().Length}");
        if (CountByName("GeneratedMap") != 1) failures.Add($"같은 프레임 두 번 로드 뒤 GeneratedMap {CountByName("GeneratedMap")}개");
        SceneManager.LoadScene("VillageScene");
        SceneManager.LoadScene("ShelterScene");
        yield return Frames(8);
        sb.AppendLine($"  같은 프레임에 Village → Shelter: 활성 씬 {SceneManager.GetActiveScene().name}, GeneratedMap {CountByName("GeneratedMap")}, NavMesh 정점 {NavMesh.CalculateTriangulation().vertices.Length}");
        bool g1 = SceneTransitionGuard.TryBeginTransition(), g2b = SceneTransitionGuard.TryBeginTransition();
        sb.AppendLine($"  SceneTransitionGuard: 같은 프레임 첫 호출 {g1}, 두 번째 {g2b}");
        yield return Load("TruckScene", 3); // 가드는 sceneLoaded에서 다시 열린다
        sb.AppendLine($"  TruckScene 로드 뒤 가드 다시 열림: {SceneTransitionGuard.TryBeginTransition()}");
        yield return Load("TruckScene", 3);

        // ---- C. 일시정지
        sb.AppendLine();
        sb.AppendLine("== C. 일시정지(Time.timeScale = 0)");
        Time.timeScale = 0f;
        yield return Load("VillageScene", 6);
        VillageMapGenerator gp = Object.FindAnyObjectByType<VillageMapGenerator>();
        sb.AppendLine($"  timeScale 0에서 VillageScene 로드: 로드 뒤 timeScale {Time.timeScale}(PauseMenu가 sceneLoaded에서 1로 되돌림), 맵 {CountByName("GeneratedMap")}, 검증 {(gp?.LastGridValidation == null ? "결과 없음" : gp.LastGridValidation.Problems.Count + "건")}");
        yield return Load("VillageScene", 0, () => Time.timeScale = 0f);
        yield return Frames(8);
        gp = Object.FindAnyObjectByType<VillageMapGenerator>();
        GameObject truck = GameObject.Find("TruckPoint");
        bool truckOnNav = truck != null && NavMesh.SamplePosition(new Vector3(truck.transform.position.x, 0f, truck.transform.position.y), out NavMeshHit th, 0.05f, NavMesh.AllAreas);
        sb.AppendLine($"  로드 직후(Awake 뒤, Start 전) timeScale 0으로 멈춤: 8프레임 뒤 timeScale {Time.timeScale}, 검증 {(gp?.LastGridValidation == null ? "결과 없음" : gp.LastGridValidation.Problems.Count + "건")}, " +
                      $"트럭 자리 NavMesh 파임 {(!truckOnNav ? "O" : "X(카빙 미반영)")}");
        if (gp?.LastGridValidation == null) failures.Add("timeScale 0에서 생성기 검증 코루틴이 돌지 않았다");
        else foreach (string p in gp.LastGridValidation.Problems) failures.Add("timeScale 0 검증: " + p);
        Time.timeScale = 1f;

        // ---- D. 생성 직후 씬 전환
        sb.AppendLine();
        sb.AppendLine("== D. 맵 생성 직후 씬 전환");
        int logsBefore = stageLogs.Count;
        bool switched = false;
        yield return Load("VillageScene", 0, () => { SceneManager.LoadScene("TruckScene"); switched = true; });
        yield return Frames(8);
        sb.AppendLine($"  Village sceneLoaded 콜백 안에서(Start 전) TruckScene 로드: 전환 {switched}, 활성 씬 {SceneManager.GetActiveScene().name}, GeneratedMap {CountByName("GeneratedMap")}, " +
                      $"NavMesh 정점 {NavMesh.CalculateTriangulation().vertices.Length}, 새 경고/에러 {stageLogs.Count - logsBefore}");
        logsBefore = stageLogs.Count;
        yield return Load("VillageScene", 1);
        SceneManager.LoadScene("ShelterScene");
        yield return Frames(8);
        sb.AppendLine($"  Village 로드 1프레임 뒤(검증 코루틴 대기 중) ShelterScene 로드: 활성 씬 {SceneManager.GetActiveScene().name}, GeneratedMap {CountByName("GeneratedMap")}, 새 경고/에러 {stageLogs.Count - logsBefore}");
        foreach (string l in stageLogs.Skip(logsBefore).Distinct().Take(5)) sb.AppendLine("    " + l);

        // ---- E. 격자 실패 → 한 줄 대체
        sb.AppendLine();
        sb.AppendLine("== E. 격자 계획을 일부러 실패시켜 한 줄 배치 대체 확인 (생성기 복제본의 필드만 바꾼다 - 씬·프리팹은 그대로)");
        yield return ForcedFallback(sb, failures, "트럭 방 목록에 방이 아닌 프리팹만", gen => SetField(gen, "truckRoomPrefabs", new[] { GridMapAuditInspector.ReadField<GameObject[]>(gen, "animalPrefabs")[0] }));
        yield return ForcedFallback(sb, failures, "방 프리팹 1종만", gen => SetField(gen, "roomPrefabs", new[] { GridMapAuditInspector.ReadField<GameObject[]>(gen, "roomPrefabs")[1] }));
        yield return ForcedFallback(sb, failures, "계획 재시도 1번 + 길 폭 30(모든 변 포트 없음)", gen =>
        {
            SetField(gen, "maxLayoutAttempts", 1);
            GameBalanceConfig cfg = GridMapAuditInspector.ReadField<GameBalanceConfig>(gen, "config");
            if (cfg != null)
            {
                GameBalanceConfig copy = Object.Instantiate(cfg);   // 에셋은 건드리지 않고 복사본만 바꾼다
                copy.gridCorridorWidth = 30;
                copy.gridCorridorGap = 32;
                SetField(gen, "config", copy);
            }
        });

        sb.AppendLine();
        sb.AppendLine($"이 단계 콘솔 경고/에러: {stageLogs.Count}건 (E의 의도한 에러 포함)");
        foreach (IGrouping<string, string> g in stageLogs.GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l, @"-?\d+(\.\d+)?", "#")).OrderByDescending(g => g.Count()).Take(25))
            sb.AppendLine($"  ({g.Count()}) {Trim(g.First())}");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 실패 없음" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures.Take(60)) sb.AppendLine("  " + f);
        File.WriteAllText($"{OutDir}/flow.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log(Prefix + "흐름 보고서: " + Path.GetFullPath($"{OutDir}/flow.txt"));

        // ---- F. 생성 직후 Play 종료(검증 코루틴이 아직 기다리는 중). 종료 중 로그는 EnteredEditMode에서 덧붙인다.
        yield return Load("VillageScene", 1);
        exitProbe = true;
    }

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

    private static IEnumerator ForcedFallback(StringBuilder sb, List<string> failures, string how, System.Action<VillageMapGenerator> mutate)
    {
        yield return Load("VillageScene", 5);
        GameObject root = GameObject.Find("GeneratedMap");
        if (root != null) Object.DestroyImmediate(root);
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        gen.gameObject.SetActive(false);
        GameObject clone = Object.Instantiate(gen.gameObject);
        clone.name = gen.gameObject.name;
        Object.DestroyImmediate(gen.gameObject);
        VillageMapGenerator cg = clone.GetComponent<VillageMapGenerator>();
        mutate(cg);
        int logsBefore = stageLogs.Count;
        TryRun(() => clone.SetActive(true), out string exception);
        yield return Frames(6);

        List<string> newLogs = stageLogs.Skip(logsBefore).ToList();
        GameObject map = GameObject.Find("GeneratedMap");
        bool isGrid = map != null && map.transform.Find("Corridors") != null;
        List<Transform> rooms = map != null ? map.transform.Cast<Transform>().Where(t => t.Find("Ground") != null && t.GetComponent<Grid>() != null && t.name != "OutsideFloorBand").ToList() : new List<Transform>();
        HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        foreach (Transform room in rooms)
        {
            UnityEngine.Tilemaps.Tilemap ground = room.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
            Vector2Int o = new Vector2Int(Mathf.RoundToInt(room.position.x), Mathf.RoundToInt(room.position.y));
            foreach (Vector3Int p in ground.cellBounds.allPositionsWithin) if (ground.HasTile(p)) floor.Add(new Vector2Int(p.x, p.y) + o);
        }
        GameObject player = GameObject.Find("Player"), truck = GameObject.Find("TruckPoint");
        bool playerOnFloor = player != null && floor.Contains(GridMapAuditInspector.CellOf(player.transform.position));
        bool truckOnFloor = truck != null && floor.Contains(GridMapAuditInspector.CellOf(truck.transform.position));
        int paths = 0, pathsOk = 0;
        if (truck != null && NavMesh.SamplePosition(new Vector3(truck.transform.position.x, 0f, truck.transform.position.y), out NavMeshHit start, 8f, NavMesh.AllAreas))
        {
            foreach (Transform room in rooms)
            {
                paths++;
                Vector2Int o = new Vector2Int(Mathf.RoundToInt(room.position.x), Mathf.RoundToInt(room.position.y));
                UnityEngine.Tilemaps.Tilemap ground = room.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
                Vector3 c = ground.cellBounds.center + new Vector3(o.x, o.y, 0f);
                if (NavMesh.SamplePosition(new Vector3(c.x, 0f, c.y), out NavMeshHit hit, 6f, NavMesh.AllAreas))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) pathsOk++;
                }
            }
        }
        int animals = map != null ? map.GetComponentsInChildren<AnimalRescue>(true).Length : 0, monsters = map != null ? map.GetComponentsInChildren<MonsterHealth>(true).Length : 0;
        bool connected = GridMapAuditInspector.IsConnected(floor, out int comps, out _);
        bool planFailed = cg.LastGridPlan != null && !cg.LastGridPlan.Success;
        sb.AppendLine($"  [{how}] 계획 실패 {planFailed} ({cg.LastGridPlan?.Failure}), 맵 {(map == null ? "없음" : isGrid ? "격자" : "한 줄")}, 방 {rooms.Count}개({string.Join(", ", rooms.Select(t => t.name))}), " +
                      $"바닥 연결 {connected}({comps}덩어리), 플레이어 바닥 위 {playerOnFloor}, 트럭 바닥 위 {truckOnFloor}, 트럭→방 경로 {pathsOk}/{paths}, 동물 {animals}, 몬스터 {monsters}, " +
                      $"예외 {(exception ?? "없음")}, 새 경고/에러 {newLogs.Count}");
        foreach (string l in newLogs.Distinct().Take(6)) sb.AppendLine("      " + Trim(l));
        if (!planFailed) failures.Add($"[{how}] 격자 계획이 실패하지 않았다");
        if (map == null || isGrid) failures.Add($"[{how}] 한 줄 배치로 대체되지 않았다");
        if (exception != null) failures.Add($"[{how}] 예외: {exception}");
        if (!connected || !playerOnFloor || !truckOnFloor || pathsOk != paths) failures.Add($"[{how}] 대체 맵 이상: 연결 {connected}, 플레이어 {playerOnFloor}, 트럭 {truckOnFloor}, 경로 {pathsOk}/{paths}");
    }

    private static void TryRun(System.Action action, out string error)
    {
        error = null;
        try { action(); }
        catch (System.Exception e) { error = e.GetType().Name + ": " + e.Message; }
    }

    // ===================================================================== 5. 플레이어 시점 캡처
    private static IEnumerator Views()
    {
        string dir = $"{OutDir}/views";
        Directory.CreateDirectory(dir);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"GridMapAudit 플레이어 시점 캡처 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} (3x4, 게임 카메라 orthographic 5.6, 1280x720, 시야 라이트 그대로)");
        List<(string kind, int seed, Vector2 pos, string what)> spots = new List<(string, int, Vector2, string)>();

        foreach (int seed in new[] { 1, 2, 3 })
        {
            SetOverride(3, 4, seed);
            yield return Load("VillageScene", 5);
            VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            GridLayoutPlan plan = gen.LastGridPlan;
            GameObject mapRoot = GameObject.Find("GeneratedMap");
            List<GridMapAuditInspector.RoomData> rooms = GridMapAuditInspector.ReadRooms(mapRoot, plan, gen);
            HashSet<Vector2Int> floor = new HashSet<Vector2Int>(rooms.SelectMany(r => r.Floor));
            floor.UnionWith(GridMapAuditInspector.CorridorTiles(mapRoot));

            List<(string, int, Vector2, string)> local = new List<(string, int, Vector2, string)>();
            // 길-방 접점: 연결마다 A쪽 포트 가장 바깥 줄 가운데에서 길 쪽 1칸.
            foreach (GridCorridor c in plan.Corridors.Take(seed == 1 ? 2 : seed == 2 ? 2 : 1))
            {
                Vector2Int edge = c.PortA.EdgeCells[c.PortA.EdgeCells.Count / 2];
                Vector2Int outward = c.PortA.Side == GridSide.East ? Vector2Int.right : c.PortA.Side == GridSide.West ? Vector2Int.left : c.PortA.Side == GridSide.North ? Vector2Int.up : Vector2Int.down;
                local.Add(("junction", seed, new Vector2(edge.x + 0.5f, edge.y + 0.5f) + (Vector2)outward, $"길 {c.Edge} 칸 ({c.PortA.Cell.x},{c.PortA.Cell.y}) {c.PortA.Side} 포트"));
            }
            // 방 모서리: 방마다 다른 모서리(남서/남동/북서/북동 순서)에서 가장 가까운 바닥 칸.
            Vector2[] corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            foreach ((GridMapAuditInspector.RoomData room, int i) in rooms.Where(r => r.Grid != plan.TruckCell).Take(seed == 3 ? 1 : 2).Select((r, i) => (r, i + seed)))
            {
                Vector2 k = corners[i % 4];
                float x = Mathf.Lerp(room.Floor.Min(c => c.x), room.Floor.Max(c => c.x) + 1, k.x), y = Mathf.Lerp(room.Floor.Min(c => c.y), room.Floor.Max(c => c.y) + 1, k.y);
                Vector2Int cell = room.Floor.OrderBy(c => (new Vector2(c.x + 0.5f, c.y + 0.5f) - new Vector2(x, y)).sqrMagnitude).First();
                local.Add(("corner", seed, new Vector2(cell.x + 0.5f, cell.y + 0.5f), $"{room.T.name}({room.Type}) {(k.x == 0 ? "서" : "동")}{(k.y == 0 ? "남" : "북")}쪽 모서리"));
            }
            // 맵 경계: 바닥 범위의 서/동/남/북 끝 칸과(시드 1) 길 옆면.
            List<(string, Vector2Int)> edges = new List<(string, Vector2Int)>
            {
                ("서쪽 끝", floor.OrderBy(c => c.x).ThenBy(c => Mathf.Abs(c.y - 50)).First()),
                ("동쪽 끝", floor.OrderByDescending(c => c.x).ThenBy(c => Mathf.Abs(c.y - 50)).First()),
                ("남쪽 끝", floor.OrderBy(c => c.y).ThenBy(c => Mathf.Abs(c.x - 50)).First()),
                ("북쪽 끝", floor.OrderByDescending(c => c.y).ThenBy(c => Mathf.Abs(c.x - 50)).First()),
            };
            if (seed == 1)
            {
                HashSet<Vector2Int> corridor = GridMapAuditInspector.CorridorTiles(mapRoot);
                Vector2Int side = corridor.Where(c => !floor.Contains(c + Vector2Int.up) || !floor.Contains(c + Vector2Int.right)).OrderBy(c => c.x + c.y).ElementAtOrDefault(corridor.Count / 8);
                edges.Add(("길 옆면", side));
            }
            foreach ((string what, Vector2Int c) in edges.Take(seed == 1 ? 5 : 0))
                local.Add(("boundary", seed, new Vector2(c.x + 0.5f, c.y + 0.5f), what));

            // 몬스터를 끄고(플레이어가 맞아 강제 복귀하지 않게) 지점마다 플레이어와 카메라를 옮겨 찍는다.
            foreach (MonsterHealth m in mapRoot.GetComponentsInChildren<MonsterHealth>()) m.gameObject.SetActive(false);
            GameObject player = GameObject.Find("Player");
            Camera cam = Camera.main;
            foreach ((string kind, int s, Vector2 pos, string what) in local)
            {
                Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                player.transform.position = new Vector3(pos.x, pos.y, player.transform.position.z);
                if (rb != null) { rb.position = pos; rb.linearVelocity = Vector2.zero; }
                yield return Frames(4);
                cam.transform.position = new Vector3(pos.x, pos.y, cam.transform.position.z);
                yield return Frames(2);
                int n = spots.Count(x => x.kind == kind) + 1;
                string file = $"{dir}/{kind}_{n}_seed{s}.png";
                GridMapAuditImages.CaptureGameCamera(file, $"{kind.ToUpperInvariant()} {n} #{s}");
                spots.Add((kind, s, pos, what));
                sb.AppendLine($"  {file}: {what} @({pos.x:F1},{pos.y:F1})");
            }
        }
        VillageMapGenerator.EditorLayoutOverride = null;
        File.WriteAllText($"{OutDir}/views.txt", sb.ToString(), new UTF8Encoding(false));
        Debug.Log(Prefix + "시점 캡처: " + Path.GetFullPath(dir));
    }
}
