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
        KeyDone = "VMGT.Done", KeyExit = "VMGT.Exit";
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

    private static void Begin(int runs, string label, bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetInt(KeyRuns, runs);
        SessionState.SetString(KeyLabel, label);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetInt(KeyExit, 1);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            runner = new Runner(SessionState.GetInt(KeyRuns, 30), SessionState.GetString(KeyLabel, "manual"));
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
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
    }

    private sealed class Runner
    {
        private readonly int runs;
        private readonly string label;
        private readonly IEnumerator routine;
        private readonly List<RunResult> results = new List<RunResult>();
        private readonly HashSet<string> captured = new HashSet<string>();
        private RunResult current;
        private string abortReason;
        public int ExitCode { get; private set; } = 1;

        public Runner(int runs, string label)
        {
            this.runs = runs;
            this.label = label;
            routine = Run();
        }

        public bool MoveNext() => routine.MoveNext();

        public void Abort(System.Exception e)
        {
            abortReason = e.ToString();
            Application.logMessageReceived -= OnLog;
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
                current = new RunResult { Index = i + 1 };
                ResetGameManagerSeed();

                bool loaded = false;
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded = true;
                SceneManager.sceneLoaded += onLoaded;
                Stopwatch sw = Stopwatch.StartNew();
                SceneManager.LoadScene(Path.GetFileNameWithoutExtension(VillageRoomBuilder.ScenePath));
                while (!loaded) yield return null;
                current.LoadMs = sw.Elapsed.TotalMilliseconds;
                SceneManager.sceneLoaded -= onLoaded;

                // NavMeshObstacle 카빙과 Destroy가 반영되도록 몇 프레임 기다린다.
                int frame = Time.frameCount;
                while (Time.frameCount < frame + 5) yield return null;

                Inspect(current);
                results.Add(current);
                Debug.Log(LogPrefix + $"{label} #{current.Index}: {string.Join(" > ", current.Rooms)} / 실패 {current.Failures.Count} / 경고·에러 {current.Logs.Count} / NavMesh {current.NavBuildMs:F1}ms");
            }
            current = null;
            Application.logMessageReceived -= OnLog;
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

            List<Transform> rooms = new List<Transform>();
            foreach (Transform child in mapRoot.transform)
                if (child.name != "OutsideFloorBand" && child.Find("Ground") != null && child.GetComponent<Grid>() != null) rooms.Add(child);
            rooms.Sort((a, b) => a.position.x.CompareTo(b.position.x));
            r.Rooms = rooms.Select(t => t.name.Replace("(Clone)", "")).ToList();

            // Perlin 바닥 변형: 생성기가 Ground에 칠한 변형 타일(floorVariantTiles) 칸 수를 방별로 센다.
            TileBase[] variants = ReadArray<TileBase>(Object.FindAnyObjectByType<VillageMapGenerator>(), "floorVariantTiles");
            foreach (Transform room in rooms)
            {
                Tilemap ground = room.Find("Ground").GetComponent<Tilemap>();
                int count = 0;
                foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                    if (variants.Contains(ground.GetTile(p))) count++;
                r.VariantCells[room.name.Replace("(Clone)", "")] = count;
            }

            Dictionary<Transform, HashSet<Vector2Int>> floor = rooms.ToDictionary(t => t, RoomFloor);
            Dictionary<Transform, HashSet<Vector2Int>> obstacleCells = rooms.ToDictionary(t => t, RoomObstacleCells);
            HashSet<Vector2Int> mapFloor = new HashSet<Vector2Int>(floor.Values.SelectMany(c => c));
            r.FloorCells = mapFloor.Count;

            // 연결부: 이전 방 맨 오른쪽 열과 다음 방 맨 왼쪽 열이 맞닿는 줄 중 양쪽 모두 장애물이 없는 연속 줄 수.
            List<Vector3> navPoints = rooms.Select(t => RoomNavPoint(floor[t], obstacleCells[t])).ToList();
            for (int i = 1; i < rooms.Count; i++)
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
            VillageMapGenerator generator = Object.FindAnyObjectByType<VillageMapGenerator>();
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

            r.NavBuildMs = TimeNavMeshBuild(rooms);
            CaptureIfNew(r, mapFloor);
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
        private static double TimeNavMeshBuild(List<Transform> rooms)
        {
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            Bounds bounds = default;
            bool any = false;
            foreach (Transform room in rooms)
            {
                Transform navGround = room.Find("NavGround");
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

            sb.AppendLine();
            sb.AppendLine("== 회차별");
            foreach (RunResult x in results)
            {
                sb.AppendLine($"  #{x.Index:00} {string.Join(" > ", x.Rooms)} | 바닥 {x.FloorCells}칸 | 씬 로드 {x.LoadMs:F0}ms | NavMesh {x.NavBuildMs:F1}ms | 실패 {x.Failures.Count} | 경고·에러 {x.Logs.Count}");
                foreach (string n in x.JunctionNotes) sb.AppendLine("      " + n);
                foreach (string f in x.Failures) sb.AppendLine("      [실패] " + f);
            }

            Directory.CreateDirectory("Logs");
            string path = $"Logs/VillageMapGenerationTest_{label}.txt";
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log(LogPrefix + "보고서: " + Path.GetFullPath(path));
            ExitCode = abortReason == null ? 0 : 1;
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
