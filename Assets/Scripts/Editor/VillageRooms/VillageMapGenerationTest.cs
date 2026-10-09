using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

/// <summary>
/// Play 모드에서 VillageScene을 여러 번 다시 불러와 VillageMapGenerator가 만든 맵을 검사한다(생성기 코드는 건드리지 않고 결과만 읽는다).
/// 매 회: 등장한 방, 이웃 방 연결부의 열린 줄 수와 NavMesh 경로, 동물/몬스터 스폰 수와 위치, 플레이어 위치, 콘솔 경고/에러,
/// NavMesh 빌드 시간(생성기와 같은 소스로 NavMeshBuilder를 다시 돌려 잰다)을 기록하고 Logs/VillageMapGenerationTest_{label}.txt에 쓴다.
/// 메뉴: Tools > Village Rooms > Run Map Generation Test (30x)
/// batchmode: -executeMethod VillageMapGenerationTest.RunBatch -mapGenRuns 30 -mapGenLabel baseline  (끝나면 에디터가 종료된다)
/// </summary>
[InitializeOnLoad]
public static class VillageMapGenerationTest
{
    private const string KeyActive = "VMGT.Active", KeyRuns = "VMGT.Runs", KeyLabel = "VMGT.Label", KeyBatch = "VMGT.Batch",
        KeyDone = "VMGT.Done", KeyExit = "VMGT.Exit", KeyGridColumns = "VMGT.GridColumns", KeyGridRows = "VMGT.GridRows",
        KeyRunInBackground = "VMGT.RunInBackground", KeySeedBase = "VMGT.SeedBase";
    private const string LogPrefix = "[MapGenTest] ";
    private const int MinSafeDoorwayWidth = 3; // VillageMapGenerator.MinSafeDoorwayWidth와 같은 기준

    private static Runner runner;

    static VillageMapGenerationTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Rooms/Run Map Generation Test (30x)")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning(LogPrefix + "Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(30, "manual", false);
    }

    // 시드 고정 테스트: 회차 i는 시드 i(1부터)로 만든다. 같은 메뉴를 다시 돌리면 같은 맵들이 나와서 수정 전후를 회차별로 비교할 수 있다
    // (마스크 메시 해시 등). 씬 파일(layoutMode)은 그대로 두고 테스트 중에만 덮어쓴다(VillageMapGenerator.EditorLayoutOverride).
    [MenuItem("Tools/Village Rooms/Run Map Generation Test - Linear (30x, seeds 1-30)")]
    private static void RunLinearSeededFromMenu() => BeginFromMenu(30, "linear_seeded", 0, 0, 1);

    [MenuItem("Tools/Village Rooms/Run Map Generation Test - Grid 3x4 (10x)")]
    private static void RunGrid3x4FromMenu() => BeginFromMenu(10, "grid3x4", 3, 4, 1);

    [MenuItem("Tools/Village Rooms/Run Map Generation Test - Grid 4x5 (10x)")]
    private static void RunGrid4x5FromMenu() => BeginFromMenu(10, "grid4x5", 4, 5, 1);

    private static void BeginFromMenu(int runs, string label, int columns, int rows, int seedBase)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning(LogPrefix + "Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(runs, label, false, columns, rows, seedBase);
    }

    public static void RunBatch()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int runs = 30;
        string label = "batch";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-mapGenRuns") int.TryParse(args[i + 1], out runs);
            if (args[i] == "-mapGenLabel") label = args[i + 1];
        }
        Begin(runs, label, true);
    }

    // gridColumns/gridRows가 0이면 씬 설정(layoutMode) 그대로, 아니면 테스트 중에만 Grid로 덮어쓴다.
    // seedBase가 0이면 회차마다 새 시드, 아니면 회차 i(0부터)를 시드 seedBase + i로 만든다.
    private static void Begin(int runs, string label, bool batch, int gridColumns = 0, int gridRows = 0, int seedBase = 0)
    {
        SessionState.SetInt(KeySeedBase, seedBase);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetInt(KeyRuns, runs);
        SessionState.SetString(KeyLabel, label);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetInt(KeyExit, 1);
        SessionState.SetInt(KeyGridColumns, gridColumns);
        SessionState.SetInt(KeyGridRows, gridRows);
        // 에디터 창이 포커스를 잃으면 Play 모드가 멈추므로 테스트 동안만 켜고 끝나면 원래 값으로 되돌린다.
        SessionState.SetBool(KeyRunInBackground, PlayerSettings.runInBackground);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            int columns = SessionState.GetInt(KeyGridColumns, 0), rows = SessionState.GetInt(KeyGridRows, 0);
            runner = new Runner(SessionState.GetInt(KeyRuns, 30), SessionState.GetString(KeyLabel, "manual"),
                columns > 0 && rows > 0, columns, rows, SessionState.GetInt(KeySeedBase, 0));
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            PlayerSettings.runInBackground = SessionState.GetBool(KeyRunInBackground, false);
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetInt(KeyExit, 1));
        }
    }

    private static void Step()
    {
        if (runner == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; return; }
        bool more;
        try { more = runner.MoveNext(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            more = false;
            runner.Abort(e);
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(KeyDone, true);
            SessionState.SetInt(KeyExit, runner.ExitCode);
            runner = null;
            EditorApplication.ExitPlaymode();
        }
    }

    private sealed class RunResult
    {
        public int Index;
        public List<string> Rooms = new List<string>();
        public List<string> JunctionNotes = new List<string>();
        public List<string> Failures = new List<string>();
        public List<string> Logs = new List<string>();
        public Dictionary<string, int> VariantCells = new Dictionary<string, int>();
        public int FloorCells;
        public double LoadMs;
        public double NavBuildMs;

        // 측정(격자 맵 0단계): 생성기 단계별 시간과, 생성 결과의 크기를 가늠하는 개수들.
        public List<KeyValuePair<string, double>> GenerationTimings = new List<KeyValuePair<string, double>>();
        public int NavCarvers;
        public int ShadowCasters;
        public int ShadowPathPoints;
        public int MaskVertices;
        public int BoundaryBoxes;
        public List<double> FirstFrameMs = new List<double>(); // 로드 직후 프레임들의 실제 프레임 시간(카빙 등 첫 프레임 비용)

        // 격자 배치일 때: 생성기의 개발용 검증 결과(VillageMapGenerator.LastGridValidation).
        public bool IsGrid;
        public int CorridorCells;
        public int PathsChecked, PathsComplete;
        public bool FloorConnected = true, WalkableConnected = true;

        public int? Seed;           // 시드 고정 테스트일 때 이 회차의 시드
        public string MaskHash;     // 바깥 마스크 메시(정점 위치 + 색) 해시 - 수정 전후 같은 시드끼리 비교한다
    }

    private sealed class Runner
    {
        private readonly int runs;
        private readonly string label;
        private readonly bool grid;
        private readonly int gridColumns, gridRows, seedBase;
        private readonly IEnumerator routine;
        private readonly List<RunResult> results = new List<RunResult>();
        private readonly HashSet<string> captured = new HashSet<string>();
        private RunResult current;
        private string abortReason;
        public int ExitCode { get; private set; } = 1;

        public Runner(int runs, string label, bool grid, int gridColumns, int gridRows, int seedBase)
        {
            this.runs = runs;
            this.label = label;
            this.grid = grid;
            this.gridColumns = gridColumns;
            this.gridRows = gridRows;
            this.seedBase = seedBase;
            routine = Run();
        }

        // 회차 i의 덮어쓰기. 격자도 시드 고정도 아니면 null(씬 설정과 새 시드 그대로).
        private VillageMapGenerator.EditorOverride? OverrideFor(int i)
        {
            if (!grid && seedBase == 0) return null;
            return new VillageMapGenerator.EditorOverride
            {
                Mode = grid ? MapLayoutMode.Grid : MapLayoutMode.Linear,
                Columns = grid ? gridColumns : 3,
                Rows = grid ? gridRows : 4,
                Seed = seedBase != 0 ? seedBase + i : (int?)null,
            };
        }

        public bool MoveNext() => routine.MoveNext();

        public void Abort(System.Exception e)
        {
            abortReason = e.ToString();
            Application.logMessageReceived -= OnLog;
            VillageMapGenerator.EditorLayoutOverride = null;
            WriteReport();
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (current == null || type == LogType.Log || condition.StartsWith(LogPrefix)) return;
            current.Logs.Add($"{type}: {condition.Split('\n')[0]}");
        }

        private IEnumerator Run()
        {
            Application.logMessageReceived += OnLog;
            for (int i = 0; i < runs; i++)
            {
                VillageMapGenerator.EditorOverride? runOverride = OverrideFor(i);
                current = new RunResult { Index = i + 1, Seed = runOverride?.Seed };
                ResetGameManagerSeed();
                VillageMapGenerator.EditorLayoutOverride = runOverride;

                bool loaded = false;
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded = true;
                SceneManager.sceneLoaded += onLoaded;
                Stopwatch sw = Stopwatch.StartNew();
                SceneManager.LoadScene(Path.GetFileNameWithoutExtension(VillageRoomBuilder.ScenePath));
                while (!loaded) yield return null;
                current.LoadMs = sw.Elapsed.TotalMilliseconds;
                SceneManager.sceneLoaded -= onLoaded;

                // NavMeshObstacle 카빙과 Destroy가 반영되도록 몇 프레임 기다린다. 그동안의 프레임 시간을 같이 잰다.
                int frame = Time.frameCount;
                while (Time.frameCount < frame + 5)
                {
                    yield return null;
                    current.FirstFrameMs.Add(Time.unscaledDeltaTime * 1000.0);
                }

                Inspect(current);
                results.Add(current);
                Debug.Log(LogPrefix + $"{label} #{current.Index}: {string.Join(" > ", current.Rooms)} / 실패 {current.Failures.Count} / 경고·에러 {current.Logs.Count} / NavMesh {current.NavBuildMs:F1}ms");
            }
            current = null;
            Application.logMessageReceived -= OnLog;
            VillageMapGenerator.EditorLayoutOverride = null;
            WriteReport();
        }

        // VillageScene만 단독으로 Play하면 GameManager가 없어 매번 새 시드가 나온다. 혹시 GameManager가 살아 있으면 같은 Day 동안
        // 같은 시드를 돌려주므로, 테스트에서만 시드를 다시 뽑게 한다.
        private static void ResetGameManagerSeed()
        {
            if (GameManager.Instance == null) return;
            FieldInfo f = typeof(GameManager).GetField("gameSessionSeedInitialized", BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(GameManager.Instance, false);
        }

        private void Inspect(RunResult r)
        {
            GameObject mapRoot = GameObject.Find("GeneratedMap");
            if (mapRoot == null) { r.Failures.Add("GeneratedMap이 없다 (맵 생성 실패)"); return; }

            VillageMapGenerator generator = Object.FindAnyObjectByType<VillageMapGenerator>();
            Transform corridors = mapRoot.transform.Find("Corridors");
            r.IsGrid = corridors != null;
            GridLayoutPlan plan = r.IsGrid && generator != null ? generator.LastGridPlan : null;

            List<Transform> rooms = new List<Transform>();
            foreach (Transform child in mapRoot.transform)
                if (child.name != "OutsideFloorBand" && child.Find("Ground") != null && child.GetComponent<Grid>() != null) rooms.Add(child);
            if (r.IsGrid) rooms.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            else rooms.Sort((a, b) => a.position.x.CompareTo(b.position.x));
            r.Rooms = rooms.Select(t => RoomLabel(t, plan)).ToList();

            // Perlin 바닥 변형: 생성기가 Ground에 칠한 변형 타일(floorVariantTiles) 칸 수를 방 종류별로 센다.
            TileBase[] variants = ReadArray<TileBase>(generator, "floorVariantTiles");
            foreach (Transform room in rooms)
            {
                Tilemap ground = room.Find("Ground").GetComponent<Tilemap>();
                int count = 0;
                foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                    if (variants.Contains(ground.GetTile(p))) count++;
                r.VariantCells[RoomTypeName(room, plan)] = count;
            }

            Dictionary<Transform, HashSet<Vector2Int>> floor = rooms.ToDictionary(t => t, RoomFloor);
            Dictionary<Transform, HashSet<Vector2Int>> obstacleCells = rooms.ToDictionary(t => t, RoomObstacleCells);
            HashSet<Vector2Int> mapFloor = new HashSet<Vector2Int>(floor.Values.SelectMany(c => c));
            if (r.IsGrid)
            {
                Tilemap corridorTiles = corridors.GetComponentInChildren<Tilemap>();
                if (corridorTiles != null)
                    foreach (Vector3Int p in corridorTiles.cellBounds.allPositionsWithin)
                        if (corridorTiles.HasTile(p) && mapFloor.Add(new Vector2Int(p.x, p.y))) r.CorridorCells++;
            }
            r.FloorCells = mapFloor.Count;

            if (r.IsGrid)
            {
                // 격자: 생성기의 개발용 검증(Start에서 카빙 반영 후 실행)으로 트럭→모든 방 경로와 바닥 연결을 확인한다.
                VillageMapGenerator.GridValidationResult v = generator != null ? generator.LastGridValidation : null;
                if (v == null) r.Failures.Add("격자 검증 결과가 없다 (VillageMapGenerator.Start가 돌지 않았다)");
                else
                {
                    r.PathsChecked = v.PathsChecked;
                    r.PathsComplete = v.PathsComplete;
                    r.FloorConnected = v.FloorConnected;
                    r.WalkableConnected = v.WalkableConnected;
                    foreach (string p in v.Problems) r.Failures.Add("격자 검증: " + p);
                    r.JunctionNotes.Add($"격자 {plan?.Columns}x{plan?.Rows}, 연결 {plan?.Edges.Count} (고리 {(plan != null ? GridLayoutPlanner.LoopCount(plan) : 0)}), 길 {r.CorridorCells}칸, " +
                                        $"트럭→방 경로 {v.PathsComplete}/{v.PathsChecked}, 바닥 연결 {(v.FloorConnected ? "O" : "X")}, 걸을 수 있는 바닥 연결 {(v.WalkableConnected ? "O" : "X")}");
                }
            }

            // 연결부(한 줄 배치): 이전 방 맨 오른쪽 열과 다음 방 맨 왼쪽 열이 맞닿는 줄 중 양쪽 모두 장애물이 없는 연속 줄 수.
            List<Vector3> navPoints = rooms.Select(t => RoomNavPoint(floor[t], obstacleCells[t])).ToList();
            for (int i = 1; i < rooms.Count && !r.IsGrid; i++)
            {
                Transform a = rooms[i - 1], b = rooms[i];
                int ax = floor[a].Max(c => c.x), bx = floor[b].Min(c => c.x);
                List<int> open = new List<int>();
                for (int y = floor[a].Min(c => c.y) - 1; y <= floor[a].Max(c => c.y) + 1; y++)
                {
                    Vector2Int ca = new Vector2Int(ax, y), cb = new Vector2Int(bx, y);
                    if (floor[a].Contains(ca) && floor[b].Contains(cb) && !obstacleCells[a].Contains(ca) && !obstacleCells[b].Contains(cb)) open.Add(y);
                }
                int run = 0, best = 0, prev = int.MinValue;
                foreach (int y in open) { run = y == prev + 1 ? run + 1 : 1; best = Mathf.Max(best, run); prev = y; }
                string pair = $"{r.Rooms[i - 1]}→{r.Rooms[i]}";
                r.JunctionNotes.Add($"{pair}: 맞닿은 칸 x {ax}|{bx}, 열린 연속 {best}줄");
                if (bx != ax + 1) r.Failures.Add($"{pair}: 방 사이에 틈이 있다 (x {ax} → {bx})");
                if (best < MinSafeDoorwayWidth) r.Failures.Add($"{pair}: 열린 연속 줄이 {best}줄로 {MinSafeDoorwayWidth}줄 미만");

                NavMeshPath path = new NavMeshPath();
                bool complete = IsFinite(navPoints[i - 1]) && IsFinite(navPoints[i]) &&
                                NavMesh.CalculatePath(navPoints[i - 1], navPoints[i], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!complete) r.Failures.Add($"{pair}: NavMesh 경로가 이어지지 않는다 ({path.status})");
            }

            // 스폰: 방마다 기대 수만큼, 바닥 위 장애물 없는 칸에 있는지.
            int animalsPerRoom = ReadInt(generator, "animalsPerRoom"), monstersPerRoom = ReadInt(generator, "monstersPerRoom");
            bool hasAnimals = ReadArrayLength(generator, "animalPrefabs") > 0, hasMonsters = ReadArrayLength(generator, "monsterPrefabs") > 0;
            foreach (Transform room in rooms)
            {
                string name = room.name.Replace("(Clone)", "");
                AnimalRescue[] animals = room.GetComponentsInChildren<AnimalRescue>(true);
                MonsterHealth[] monsters = room.GetComponentsInChildren<MonsterHealth>(true);
                if (hasAnimals && animals.Length != animalsPerRoom) r.Failures.Add($"{name}: 동물 {animals.Length}마리 (기대 {animalsPerRoom})");
                if (hasMonsters && monsters.Length != monstersPerRoom) r.Failures.Add($"{name}: 몬스터 {monsters.Length}마리 (기대 {monstersPerRoom})");
                foreach (AnimalRescue animal in animals)
                {
                    Vector2Int cell = CellOf(animal.transform.position.x, animal.transform.position.y);
                    if (!floor[room].Contains(cell) || obstacleCells[room].Contains(cell)) r.Failures.Add($"{name}: 동물 {animal.name}이 바닥이 아니거나 장애물 칸 {cell}에 스폰");
                }
                foreach (MonsterHealth monster in monsters)
                {
                    Vector2Int cell = CellOf(monster.transform.position.x, monster.transform.position.z);
                    if (!mapFloor.Contains(cell) || obstacleCells[room].Contains(cell)) r.Failures.Add($"{name}: 몬스터 {monster.name}이 바닥이 아니거나 장애물 칸 {cell}에 스폰");
                }
            }

            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                Vector2Int cell = CellOf(player.transform.position.x, player.transform.position.y);
                bool onObstacle = obstacleCells.Values.Any(s => s.Contains(cell));
                if (!mapFloor.Contains(cell) || onObstacle) r.Failures.Add($"플레이어가 바닥이 아니거나 장애물 칸 {cell}에 스폰");
            }

            InspectTruck(r, generator, player, mapFloor);

            r.NavBuildMs = TimeNavMeshBuild(rooms, corridors != null ? corridors.Find("CorridorNavGround") : null);
            MeasureSizes(r, generator, mapRoot);
            CaptureIfNew(r, mapFloor);
        }

        // 한 줄 배치는 프리팹 이름, 격자 배치는 "Room_x_y:프리팹 이름".
        private static string RoomLabel(Transform room, GridLayoutPlan plan) =>
            plan == null ? room.name.Replace("(Clone)", "") : $"{room.name}:{RoomTypeName(room, plan)}";

        private static string RoomTypeName(Transform room, GridLayoutPlan plan)
        {
            if (plan == null) return room.name.Replace("(Clone)", "");
            string[] parts = room.name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y) && x < plan.Columns && y < plan.Rows)
                return plan.TypeNames[plan.RoomType[x, y]];
            return room.name;
        }

        // 정점 위치(1/1000 단위로 반올림)와 색(1/1000 단위), 삼각형 인덱스를 순서대로 섞은 해시.
        private static string MeshHash(Mesh mesh)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(long v) { h = (h ^ v) * 1099511628211L; }
                foreach (Vector3 v in mesh.vertices) { Mix(Mathf.RoundToInt(v.x * 1000)); Mix(Mathf.RoundToInt(v.y * 1000)); }
                foreach (Color c in mesh.colors) Mix(Mathf.RoundToInt(c.a * 1000) * 7 + Mathf.RoundToInt(c.r * 1000));
                foreach (int i in mesh.triangles) Mix(i);
                return h.ToString("x16");
            }
        }

        private static void MeasureSizes(RunResult r, VillageMapGenerator generator, GameObject mapRoot)
        {
            if (generator != null) r.GenerationTimings = new List<KeyValuePair<string, double>>(generator.LastGenerationTimings);
            r.NavCarvers = mapRoot.GetComponentsInChildren<NavMeshObstacle>(true).Length;
            Transform shadows = mapRoot.transform.Find("WallShadows");
            if (shadows != null)
            {
                ShadowCaster2D[] casters = shadows.GetComponentsInChildren<ShadowCaster2D>(true);
                r.ShadowCasters = casters.Length;
                r.ShadowPathPoints = casters.Sum(c => c.shapePath != null ? c.shapePath.Length : 0);
            }
            Transform mask = mapRoot.transform.Find("OutsideMapMask");
            MeshFilter maskFilter = mask != null ? mask.GetComponent<MeshFilter>() : null;
            r.MaskVertices = maskFilter != null && maskFilter.sharedMesh != null ? maskFilter.sharedMesh.vertexCount : 0;
            r.MaskHash = maskFilter != null && maskFilter.sharedMesh != null ? MeshHash(maskFilter.sharedMesh) : "-";
            Transform boundary = mapRoot.transform.Find("MapBoundary");
            r.BoundaryBoxes = boundary != null ? boundary.GetComponents<BoxCollider2D>().Length : 0;
        }

        // 트럭: NavMesh에서 파였는지(몬스터가 못 지나감), 위에 스폰된 동물/몬스터가 없는지, 플레이어와 겹치지 않는지,
        // 주변 여유 폭 안에 장애물이 남지 않았는지, 여유 폭 중 바닥 비율.
        private static void InspectTruck(RunResult r, VillageMapGenerator generator, GameObject player, HashSet<Vector2Int> mapFloor)
        {
            GameObject truck = GameObject.Find(generator != null ? (string)generator.GetType().GetField("truckPointObjectName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(generator) : "TruckPoint");
            BoxCollider2D box = truck != null ? truck.GetComponent<BoxCollider2D>() : null;
            if (box == null) { r.Failures.Add("트럭(TruckPoint) 콜라이더를 찾지 못했다"); return; }
            Bounds tb = box.bounds;

            int walkableSamples = 0;
            for (float fx = -0.3f; fx <= 0.31f; fx += 0.3f)
                for (float fy = -0.4f; fy <= 0.41f; fy += 0.2f)
                {
                    Vector3 p = new Vector3(tb.center.x + tb.size.x * fx, 0f, tb.center.y + tb.size.y * fy);
                    if (NavMesh.SamplePosition(p, out NavMeshHit hit, 0.05f, NavMesh.AllAreas)) walkableSamples++;
                }
            if (walkableSamples > 0) r.Failures.Add($"트럭 자리에 NavMesh가 남아 있다 (샘플 {walkableSamples}곳) - 몬스터가 트럭을 지나갈 수 있다");

            Bounds near = tb;
            near.Expand(new Vector3(0.5f, 0.5f, 100f));
            foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsSortMode.None))
                if (near.Contains(new Vector3(a.transform.position.x, a.transform.position.y, tb.center.z))) r.Failures.Add($"동물 {a.name}이 트럭 위/바로 옆에 스폰");
            foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None))
                if (near.Contains(new Vector3(m.transform.position.x, m.transform.position.z, tb.center.z))) r.Failures.Add($"몬스터 {m.name}이 트럭 위/바로 옆에 스폰");

            if (player != null)
            {
                Collider2D pc = player.GetComponent<Collider2D>();
                if (pc != null && pc.bounds.Intersects(new Bounds(new Vector3(tb.center.x, tb.center.y, pc.bounds.center.z), new Vector3(tb.size.x, tb.size.y, 100f))))
                {
                    r.Failures.Add("플레이어가 트럭과 겹쳐 스폰");
                    int players = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Count(t => t.name == player.name);
                    r.JunctionNotes.Add($"[겹침 진단] 플레이어 {player.transform.position} 충돌체 {pc.bounds.min}~{pc.bounds.max}, 트럭 {tb.min}~{tb.max}, " +
                                        $"이름이 {player.name}인 오브젝트 {players}개, 씬 {player.scene.name}");
                }
            }

            float margin = generator != null ? (float)generator.GetType().GetField("truckClearanceMarginPerSide", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(generator) : 0f;
            Vector2 clearance = (Vector2)tb.size + Vector2.one * margin * 2f;
            int left = 0;
            foreach (Collider2D c in Physics2D.OverlapBoxAll(tb.center, clearance, 0f))
                if (c != null && c.enabled && !c.isTrigger && c.transform.parent != null && c.transform.parent.name == "Obstacles") left++;
            if (left > 0) r.Failures.Add($"트럭 주변 여유 폭 안에 장애물 {left}개가 남아 있다");

            int cells = 0, floorCells = 0;
            for (int y = Mathf.FloorToInt(tb.center.y - clearance.y / 2f); y < Mathf.CeilToInt(tb.center.y + clearance.y / 2f); y++)
                for (int x = Mathf.FloorToInt(tb.center.x - clearance.x / 2f); x < Mathf.CeilToInt(tb.center.x + clearance.x / 2f); x++)
                {
                    cells++;
                    if (mapFloor.Contains(new Vector2Int(x, y))) floorCells++;
                }
            r.JunctionNotes.Add($"트럭 @({tb.center.x:F1},{tb.center.y:F1}) 여유 폭 {margin}칸 안 바닥 {floorCells}/{cells}칸");
        }

        private static HashSet<Vector2Int> RoomFloor(Transform room)
        {
            Tilemap ground = room.Find("Ground").GetComponent<Tilemap>();
            Vector2Int offset = new Vector2Int(Mathf.RoundToInt(room.position.x), Mathf.RoundToInt(room.position.y));
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                if (ground.HasTile(p)) cells.Add(new Vector2Int(p.x, p.y) + offset);
            return cells;
        }

        // 생성기(GetRoomObstacles)와 같은 기준: 방 안의 트리거가 아닌 2D 충돌체(타일맵 충돌체 제외)가 걸치는 칸.
        private static HashSet<Vector2Int> RoomObstacleCells(Transform room)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (Collider2D col in room.GetComponentsInChildren<Collider2D>())
            {
                if (!col.enabled || col.isTrigger || col is TilemapCollider2D || col is CompositeCollider2D) continue;
                if (col.attachedRigidbody != null && col.attachedRigidbody.bodyType != RigidbodyType2D.Static) continue; // 스폰된 동물 등
                Bounds b = col.bounds;
                for (int y = Mathf.FloorToInt(b.min.y + 0.01f); y <= Mathf.FloorToInt(b.max.y - 0.01f); y++)
                    for (int x = Mathf.FloorToInt(b.min.x + 0.01f); x <= Mathf.FloorToInt(b.max.x - 0.01f); x++)
                        cells.Add(new Vector2Int(x, y));
            }
            return cells;
        }

        private static Vector3 RoomNavPoint(HashSet<Vector2Int> floor, HashSet<Vector2Int> obstacles)
        {
            Vector2 centroid = Vector2.zero;
            foreach (Vector2Int c in floor) centroid += new Vector2(c.x + 0.5f, c.y + 0.5f);
            centroid /= Mathf.Max(1, floor.Count);
            IEnumerable<Vector2Int> candidates = floor.Where(c => !obstacles.Contains(c) &&
                floor.Contains(c + Vector2Int.up) && floor.Contains(c + Vector2Int.down) && floor.Contains(c + Vector2Int.left) && floor.Contains(c + Vector2Int.right))
                .OrderBy(c => (new Vector2(c.x + 0.5f, c.y + 0.5f) - centroid).sqrMagnitude);
            foreach (Vector2Int c in candidates)
                if (NavMesh.SamplePosition(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f), out NavMeshHit hit, 0.75f, NavMesh.AllAreas)) return hit.position;
            return Vector3.positiveInfinity;
        }

        private static Vector2Int CellOf(float x, float y) => new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(y));

        private static bool IsFinite(Vector3 v) => !float.IsInfinity(v.x) && !float.IsNaN(v.x);

        private static int ReadInt(Object target, string field) =>
            target == null ? 0 : (int)(target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) ?? 0);

        private static T[] ReadArray<T>(Object target, string field) where T : Object =>
            (target == null ? null : target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as T[]) ?? new T[0];

        private static int ReadArrayLength(Object target, string field) =>
            target == null ? 0 : ((target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as System.Array)?.Length ?? 0);

        // VillageMapGenerator.BakeNavMesh와 같은 소스/범위/설정으로 NavMeshData를 다시 만들어 시간을 잰다(결과는 버린다). 5회 중앙값.
        private static double TimeNavMeshBuild(List<Transform> rooms, Transform extraNavGround)
        {
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            Bounds bounds = default;
            bool any = false;
            List<Transform> navGrounds = rooms.Select(room => room.Find("NavGround")).ToList();
            if (extraNavGround != null) navGrounds.Add(extraNavGround);
            foreach (Transform navGround in navGrounds)
            {
                MeshFilter filter = navGround != null ? navGround.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = filter.sharedMesh, transform = filter.transform.localToWorldMatrix, area = 0 });
                Vector3 c = filter.transform.TransformPoint(filter.sharedMesh.bounds.center);
                Vector3 e = Vector3.Scale(filter.sharedMesh.bounds.extents, filter.transform.lossyScale) + Vector3.one;
                if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; }
                bounds.Encapsulate(c - e);
                bounds.Encapsulate(c + e);
            }
            if (!any) return -1;
            bounds.Expand(2f);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            List<double> samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                samples.Add(sw.Elapsed.TotalMilliseconds);
                if (data != null) Object.Destroy(data);
            }
            samples.Sort();
            return samples[samples.Count / 2];
        }

        // 아직 캡처하지 않은 방이 처음 등장한 회차의 맵 전체를 위에서 찍는다(시야 연출 없이 잘 보이도록 Global 라이트만 잠시 1로).
        private void CaptureIfNew(RunResult r, HashSet<Vector2Int> mapFloor)
        {
            if (r.Rooms.All(n => captured.Contains(n)) || mapFloor.Count == 0) return;
            foreach (string n in r.Rooms) captured.Add(n);

            Light2D[] globals = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.lightType == Light2D.LightType.Global).ToArray();
            float[] intensities = globals.Select(l => l.intensity).ToArray();
            foreach (Light2D l in globals) l.intensity = 1f;
            try
            {
                Directory.CreateDirectory(VillageRoomBuilder.CaptureFolder);
                int minX = mapFloor.Min(c => c.x), maxX = mapFloor.Max(c => c.x), minY = mapFloor.Min(c => c.y), maxY = mapFloor.Max(c => c.y);
                string file = $"{VillageRoomBuilder.CaptureFolder}/{label}_run{r.Index:00}_map.png";
                VillageRoomBuilder.CaptureArea(new Rect(minX - 1, minY - 1, maxX - minX + 3, maxY - minY + 3), 16, file);
                r.JunctionNotes.Add("캡처: " + file);
            }
            finally
            {
                for (int i = 0; i < globals.Length; i++) globals[i].intensity = intensities[i];
            }
        }

        private void WriteReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"VillageMapGenerationTest [{label}] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - {results.Count}/{runs}회");
            if (abortReason != null) sb.AppendLine("중단됨: " + abortReason);

            VillageMapGenerator generator = Object.FindAnyObjectByType<VillageMapGenerator>();
            GameObject[] pool = generator != null ? (generator.GetType().GetField("roomPrefabs", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(generator) as GameObject[]) : null;
            if (pool != null) sb.AppendLine("roomPrefabs: " + string.Join(", ", pool.Where(p => p != null).Select(p => p.name)));

            sb.AppendLine();
            sb.AppendLine("== 방 등장 횟수");
            foreach (var g in results.SelectMany(x => x.Rooms).GroupBy(n => n).OrderBy(g => g.Key)) sb.AppendLine($"  {g.Key}: {g.Count()}회");

            sb.AppendLine();
            sb.AppendLine("== Perlin 바닥 변형 칸 (방별: 등장 횟수 중 변형이 1칸 이상 칠해진 횟수, 평균 칸 수)");
            foreach (var g in results.SelectMany(x => x.VariantCells).GroupBy(kv => kv.Key).OrderBy(g => g.Key))
                sb.AppendLine($"  {g.Key}: {g.Count(kv => kv.Value > 0)}/{g.Count()}회, 평균 {g.Average(kv => kv.Value):F1}칸");

            int failedRuns = results.Count(x => x.Failures.Count > 0);
            sb.AppendLine();
            sb.AppendLine($"== 검사 실패: {failedRuns}회 / {results.Count}회");
            foreach (var g in results.SelectMany(x => x.Failures).GroupBy(f => f).OrderByDescending(g => g.Count())) sb.AppendLine($"  ({g.Count()}) {g.Key}");

            sb.AppendLine();
            sb.AppendLine($"== 콘솔 경고/에러 (Log 제외): {results.Sum(x => x.Logs.Count)}건");
            foreach (var g in results.SelectMany(x => x.Logs).GroupBy(f => f).OrderByDescending(g => g.Count())) sb.AppendLine($"  ({g.Count()}) {g.Key}");

            sb.AppendLine();
            sb.AppendLine("== NavMesh 빌드 시간 (생성기와 같은 입력, 5회 중앙값)");
            AppendTiming(sb, "전체", results);
            AppendTiming(sb, "새 방 포함", results.Where(x => x.Rooms.Any(n => n.StartsWith("Room_"))).ToList());
            AppendTiming(sb, "기존 방만", results.Where(x => x.Rooms.All(n => !n.StartsWith("Room_"))).ToList());

            List<RunResult> gridRuns = results.Where(x => x.IsGrid).ToList();
            if (gridRuns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"== 격자 검증 ({gridRuns.Count}회)");
                sb.AppendLine($"  트럭→방 중심 NavMesh 경로: {gridRuns.Sum(x => x.PathsComplete)}/{gridRuns.Sum(x => x.PathsChecked)} 성공");
                sb.AppendLine($"  바닥(방 + 길) 한 덩어리: {gridRuns.Count(x => x.FloorConnected)}/{gridRuns.Count}회, " +
                              $"장애물 뺀 걸을 수 있는 바닥 한 덩어리: {gridRuns.Count(x => x.WalkableConnected)}/{gridRuns.Count}회");
                sb.AppendLine($"  길 칸: 평균 {gridRuns.Average(x => x.CorridorCells):F0}, 바닥 칸(방 + 길): 평균 {gridRuns.Average(x => x.FloorCells):F0}");
            }

            AppendMeasurements(sb, results);

            sb.AppendLine();
            sb.AppendLine("== 회차별");
            foreach (RunResult x in results)
            {
                sb.AppendLine($"  #{x.Index:00}{(x.Seed.HasValue ? $" 시드 {x.Seed}" : "")} {string.Join(" > ", x.Rooms)} | 바닥 {x.FloorCells}칸 | 씬 로드 {x.LoadMs:F0}ms | NavMesh {x.NavBuildMs:F1}ms | " +
                              $"마스크 {x.MaskVertices}정점 해시 {x.MaskHash} | 실패 {x.Failures.Count} | 경고·에러 {x.Logs.Count}");
                foreach (string n in x.JunctionNotes) sb.AppendLine("      " + n);
                foreach (string f in x.Failures) sb.AppendLine("      [실패] " + f);
            }

            Directory.CreateDirectory("Logs");
            string path = $"Logs/VillageMapGenerationTest_{label}.txt";
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log(LogPrefix + "보고서: " + Path.GetFullPath(path));
            ExitCode = abortReason == null ? 0 : 1;
        }

        private static string Stat(IEnumerable<double> values, string format = "F1")
        {
            List<double> v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return "-";
            return $"평균 {v.Average().ToString(format)} / 중앙값 {v[v.Count / 2].ToString(format)} / 최대 {v[v.Count - 1].ToString(format)}";
        }

        // 격자 맵 0단계: 생성기 단계별 시간(VillageMapGenerator.LastGenerationTimings)과 생성 결과 크기.
        private static void AppendMeasurements(StringBuilder sb, List<RunResult> set)
        {
            sb.AppendLine();
            sb.AppendLine("== 생성 단계별 시간 (ms, VillageMapGenerator.Awake 안)");
            if (set.Count == 0) { sb.AppendLine("  해당 회차 없음"); return; }
            List<string> steps = set.SelectMany(x => x.GenerationTimings.Select(kv => kv.Key)).Distinct().ToList();
            foreach (string step in steps)
                sb.AppendLine($"  {step}: {Stat(set.Select(x => x.GenerationTimings.Where(kv => kv.Key == step).Sum(kv => kv.Value)), "F2")}");
            sb.AppendLine($"  합계: {Stat(set.Select(x => x.GenerationTimings.Sum(kv => kv.Value)), "F1")}");
            sb.AppendLine($"  합계 / 바닥 1000칸: {Stat(set.Select(x => x.GenerationTimings.Sum(kv => kv.Value) / Mathf.Max(1, x.FloorCells) * 1000.0), "F1")}");

            sb.AppendLine();
            sb.AppendLine("== 생성 결과 크기");
            sb.AppendLine($"  바닥 칸: {Stat(set.Select(x => (double)x.FloorCells), "F0")}");
            sb.AppendLine($"  NavMesh 카버(NavMeshObstacle): {Stat(set.Select(x => (double)x.NavCarvers), "F0")}");
            sb.AppendLine($"  벽 그림자 캐스터: {Stat(set.Select(x => (double)x.ShadowCasters), "F0")}, 꼭짓점 합: {Stat(set.Select(x => (double)x.ShadowPathPoints), "F0")}");
            sb.AppendLine($"  바깥 마스크 정점: {Stat(set.Select(x => (double)x.MaskVertices), "F0")}");
            sb.AppendLine($"  경계 충돌체 상자: {Stat(set.Select(x => (double)x.BoundaryBoxes), "F0")}");
            sb.AppendLine($"  로드 직후 5프레임 중 최대 프레임 시간(ms, 에디터 Play 기준): {Stat(set.Select(x => x.FirstFrameMs.DefaultIfEmpty(0).Max()), "F1")}");
            sb.AppendLine($"  로드 직후 첫 프레임 시간(ms): {Stat(set.Select(x => x.FirstFrameMs.DefaultIfEmpty(0).First()), "F1")}");
        }

        private static void AppendTiming(StringBuilder sb, string title, List<RunResult> set)
        {
            if (set.Count == 0) { sb.AppendLine($"  {title}: 해당 회차 없음"); return; }
            List<double> ms = set.Select(x => x.NavBuildMs).OrderBy(v => v).ToList();
            double perK = set.Average(x => x.NavBuildMs / Mathf.Max(1, x.FloorCells) * 1000.0);
            double loads = set.Average(x => x.LoadMs);
            sb.AppendLine($"  {title} ({set.Count}회): 평균 {ms.Average():F2}ms, 중앙값 {ms[ms.Count / 2]:F2}ms, 최대 {ms[ms.Count - 1]:F2}ms, " +
                          $"바닥 1000칸당 {perK:F2}ms, 평균 바닥 {set.Average(x => x.FloorCells):F0}칸, 평균 씬 로드 {loads:F0}ms");
        }
    }
}
