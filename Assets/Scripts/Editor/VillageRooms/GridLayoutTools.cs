using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// 격자 맵 계획(GridLayoutPlanner) 확인용 에디터 도구. 씬에 맵을 만들지 않고 계획만 세워서 본다.
/// - Grid Preview: 계획을 위에서 본 PNG로 저장한다(칸당 4px). 칸 경계 = 회색 테두리, 방 바닥 = 종류별 색, 장애물 = 같은 색의 어두운 칸,
///   길 = 회색, 포트(길이 방에 들어가는 차선의 가장 바깥 줄) = 노란색, 트럭 칸 = 빨간 테두리. 같은 이름의 .txt에 칸별 방, 연결, 길을 남긴다.
/// - Stress Test: 여러 격자 크기 x 시드로 계획을 세워 규칙(1단계: 실패·같은 방 인접·개수 차이·연결성·재현, 2단계: 길 교차·다른 방
///   바닥 접촉·포트 입구 장애물·길 폭)을 검사하고, 추가 연결 비율별 막다른 방/고리 수와 길 폭 5 점검을 남긴다.
/// 방 목록과 설정은 VillageScene의 VillageMapGenerator에서 읽는다(씬이 열려 있지 않으면 잠깐 추가로 열었다가 저장하지 않고 닫는다).
/// 결과: Logs/MapPreview/
/// </summary>
public static class GridLayoutTools
{
    public const string OutputFolder = "Logs/MapPreview";
    private const int PixelsPerTile = 4;

    private static readonly Color Background = new Color32(22, 22, 26, 255);
    private static readonly Color CellArea = new Color32(32, 32, 40, 255);
    private static readonly Color CellBorder = new Color32(90, 90, 104, 255);
    private static readonly Color Corridor = new Color32(140, 140, 140, 255);
    private static readonly Color Port = new Color32(255, 210, 63, 255);
    private static readonly Color TruckMark = new Color32(230, 57, 70, 255);
    private static readonly Color[] Palette =
    {
        new Color32(86, 156, 214, 255),   // 파랑
        new Color32(106, 190, 112, 255),  // 초록
        new Color32(214, 170, 82, 255),   // 황토
        new Color32(184, 120, 214, 255),  // 보라
        new Color32(90, 196, 190, 255),   // 청록
        new Color32(222, 120, 150, 255),  // 분홍
        new Color32(170, 170, 120, 255),  // 올리브
        new Color32(140, 140, 220, 255),
        new Color32(220, 140, 90, 255),
    };

    private static readonly (int columns, int rows)[] StressSizes = { (3, 4), (4, 5), (3, 3) };
    private static readonly float[] LoopRatios = { 0.25f, 0.35f, 0.5f };

    [MenuItem("Tools/Village Rooms/Grid/Save Preview PNGs (3x4 seeds 1-5, 4x5 seeds 1-3)")]
    private static void SaveDefaultPreviewsMenu()
    {
        List<string> files = SaveDefaultPreviews();
        EditorUtility.RevealInFinder(files.FirstOrDefault() ?? OutputFolder);
    }

    [MenuItem("Tools/Village Rooms/Grid/Run Stress Test (3x4, 4x5, 3x3 x 30 seeds)")]
    private static void StressTestMenu()
    {
        string path = RunStressTest(30);
        Debug.Log("[GridLayoutTools] 스트레스 테스트: " + Path.GetFullPath(path));
    }

    public static List<string> SaveDefaultPreviews()
    {
        List<string> files = new List<string>();
        files.AddRange(SavePreviews(3, 4, new[] { 1, 2, 3, 4, 5 }));
        files.AddRange(SavePreviews(4, 5, new[] { 1, 2, 3 }));
        return files;
    }

    public static List<string> SavePreviews(int columns, int rows, IEnumerable<int> seeds)
    {
        GridPlanInput input = LoadInputFromVillageScene();
        GridPlannerSettings settings = With(input.Settings, columns, rows, input.Settings.ExtraConnectionRatio);
        Directory.CreateDirectory(OutputFolder);
        List<string> files = new List<string>();
        foreach (int seed in seeds)
        {
            GridLayoutPlan plan = GridLayoutPlanner.Plan(seed, input.Types, settings);
            string basePath = $"{OutputFolder}/grid{columns}x{rows}_seed{seed:00}";
            if (plan.Success) File.WriteAllBytes(basePath + ".png", RenderPlan(plan, input.Types).EncodeToPNG());
            File.WriteAllText(basePath + ".txt", Describe(plan, input.Types, settings), new UTF8Encoding(false));
            files.Add(basePath + ".png");
            Debug.Log($"[GridLayoutTools] {basePath}.png ({(plan.Success ? "성공" : "실패: " + plan.Failure)})");
        }
        return files;
    }

    public static string RunStressTest(int seedsPerSize)
    {
        GridPlanInput input = LoadInputFromVillageScene();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Grid Stress Test {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - 크기별 시드 1..{seedsPerSize}");
        sb.AppendLine($"설정: 길 폭 {input.Settings.CorridorWidth}, 틈 {input.Settings.CorridorGap}, 추가 연결 {input.Settings.ExtraConnectionRatio}, " +
                      $"트럭 {input.Settings.TruckPlacement}, 최대 시도 {input.Settings.MaxAttempts}");
        AppendRoomTypes(sb, input.Types);

        bool allPass = true;
        foreach ((int c, int r) in StressSizes)
        {
            GridPlannerSettings settings = With(input.Settings, c, r, input.Settings.ExtraConnectionRatio);
            Dictionary<string, int> problemCounts = new Dictionary<string, int>();
            int fails = 0, maxSpread = 0, nonDeterministic = 0;
            List<int> attempts = new List<int>(), corridorCells = new List<int>();
            List<double> ms = new List<double>();
            Dictionary<string, int> truckRooms = new Dictionary<string, int>();
            List<string> notes = new List<string>();
            for (int seed = 1; seed <= seedsPerSize; seed++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                GridLayoutPlan plan = GridLayoutPlanner.Plan(seed, input.Types, settings);
                ms.Add(sw.Elapsed.TotalMilliseconds);
                GridLayoutPlan again = GridLayoutPlanner.Plan(seed, input.Types, settings);
                if (plan.Signature() != again.Signature()) { nonDeterministic++; notes.Add($"seed {seed}: 같은 시드 두 번의 결과가 다르다"); }
                if (!plan.Success) { fails++; notes.Add($"seed {seed}: 실패 - {plan.Failure}"); continue; }

                attempts.Add(plan.Attempts);
                corridorCells.Add(plan.Corridors.Sum(x => x.Cells.Count));
                string truckRoom = input.Types[plan.RoomType[plan.TruckCell.x, plan.TruckCell.y]].Name;
                truckRooms[truckRoom] = truckRooms.TryGetValue(truckRoom, out int n) ? n + 1 : 1;
                int[] counts = new int[input.Types.Count];
                for (int y = 0; y < plan.Rows; y++) for (int x = 0; x < plan.Columns; x++) counts[plan.RoomType[x, y]]++;
                maxSpread = Mathf.Max(maxSpread, counts.Max() - counts.Min());

                foreach (string p in GridLayoutPlanner.Validate(plan, input.Types))
                {
                    string kind = p.Split(':')[0];
                    problemCounts[kind] = problemCounts.TryGetValue(kind, out int k) ? k + 1 : 1;
                    notes.Add($"seed {seed}: {p}");
                }
            }
            int Count(string kind) => problemCounts.TryGetValue(kind, out int v) ? v : 0;
            int otherProblems = problemCounts.Where(kv => !new[] { "같은 방 인접", "연결성", "길 교차", "다른 방 바닥 접촉", "포트 입구 장애물", "길 폭" }.Contains(kv.Key)).Sum(kv => kv.Value);
            bool pass = fails == 0 && problemCounts.Count == 0 && maxSpread <= 1 && nonDeterministic == 0;
            allPass &= pass;
            ms.Sort();
            sb.AppendLine();
            sb.AppendLine($"== {c}x{r} ({c * r}칸) - {(pass ? "통과" : "실패")}");
            sb.AppendLine($"  1단계: 계획 실패 {fails}/{seedsPerSize}, 같은 방 인접 {Count("같은 방 인접")}, 종류별 개수 차이 최대 {maxSpread}, " +
                          $"연결 안 됨 {Count("연결성")}, 같은 시드 재현 불일치 {nonDeterministic}");
            sb.AppendLine($"  2단계: 길 교차 {Count("길 교차")}, 다른 방 바닥 접촉 {Count("다른 방 바닥 접촉")}, 포트 입구 장애물 {Count("포트 입구 장애물")}, " +
                          $"길 폭 미달 {Count("길 폭")}, 기타 문제 {otherProblems}");
            if (attempts.Count > 0)
                sb.AppendLine($"  시도 횟수 평균 {attempts.Average():F2} / 최대 {attempts.Max()}, 길 칸 평균 {corridorCells.Average():F0} (최대 {corridorCells.Max()}), " +
                              $"계획 시간 중앙값 {ms[ms.Count / 2]:F2}ms / 최대 {ms[ms.Count - 1]:F2}ms");
            sb.AppendLine("  트럭 칸의 방: " + string.Join(", ", truckRooms.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}회")));
            foreach (string note in notes.Take(20)) sb.AppendLine("  " + note);
        }

        sb.AppendLine();
        sb.AppendLine("== 추가 연결 비율별 막다른 방 / 고리 수 (크기별 시드 평균, 막다른 방 = 연결이 하나뿐인 방, 고리 = 연결 수 - (방 수 - 1))");
        foreach ((int c, int r) in StressSizes)
        {
            List<string> cells = new List<string>();
            foreach (float ratio in LoopRatios)
            {
                GridPlannerSettings settings = With(input.Settings, c, r, ratio);
                List<GridLayoutPlan> plans = Enumerable.Range(1, seedsPerSize).Select(s => GridLayoutPlanner.Plan(s, input.Types, settings)).Where(p => p.Success).ToList();
                int failures = seedsPerSize - plans.Count;
                int corridorProblems = plans.Sum(p => GridLayoutPlanner.ValidateCorridors(p, input.Types).Count);
                cells.Add($"{ratio:0.00}: 막다른 방 {plans.Average(GridLayoutPlanner.DeadEndRooms):F1}, 고리 {plans.Average(GridLayoutPlanner.LoopCount):F1}" +
                          (failures + corridorProblems > 0 ? $" (실패 {failures}, 길 문제 {corridorProblems})" : ""));
            }
            sb.AppendLine($"  {c}x{r} ({c * r}칸) | " + string.Join(" | ", cells));
        }

        sb.AppendLine();
        sb.AppendLine("== 길 폭 5 점검 (폭이 넓어지면 포트가 사라지는 변)");
        GridPlanInput wide = LoadInputFromVillageScene(5);
        foreach (GridRoomType t in wide.Types)
        {
            GridRoomType normal = input.Types.FirstOrDefault(x => x.Name == t.Name);
            string lanes = string.Join("/", t.OpenLanes);
            sb.AppendLine($"  {t.Name}: 폭 5 열린 차선 서/동/남/북 {lanes}" + (normal != null ? $" (폭 {input.Settings.CorridorWidth}: {string.Join("/", normal.OpenLanes)})" : ""));
            foreach (string w in t.Warnings) sb.AppendLine("    경고: " + w);
        }
        // 폭 5는 틈 6에 들어가지 않으므로(폭 + 여백 2) 틈을 7로 넓혀서 계획해 본다.
        GridPlannerSettings wideSettings = With(wide.Settings, 3, 4, wide.Settings.ExtraConnectionRatio);
        wideSettings.CorridorGap = Mathf.Max(wideSettings.CorridorGap, wideSettings.CorridorWidth + 2);
        int wideFails = 0, wideProblemCount = 0;
        for (int seed = 1; seed <= seedsPerSize; seed++)
        {
            GridLayoutPlan p = GridLayoutPlanner.Plan(seed, wide.Types, wideSettings);
            if (!p.Success) wideFails++;
            else wideProblemCount += GridLayoutPlanner.Validate(p, wide.Types).Count;
        }
        sb.AppendLine($"  폭 5, 틈 {wideSettings.CorridorGap}로 3x4 시드 1..{seedsPerSize} 계획: 실패 {wideFails}, 규칙 문제 {wideProblemCount} " +
                      "(포트 없는 변은 연결 후보에서 빠지고 나머지 변으로 이어진다)");

        sb.AppendLine();
        sb.AppendLine(allPass ? "결과: 모든 크기 통과" : "결과: 실패한 크기가 있다");

        Directory.CreateDirectory(OutputFolder);
        string path = $"{OutputFolder}/grid_stress.txt";
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>VillageScene의 VillageMapGenerator 설정으로 계획 입력을 만든다. 씬이 안 열려 있으면 추가로 열었다가 저장하지 않고 닫는다.</summary>
    public static GridPlanInput LoadInputFromVillageScene(int? corridorWidthOverride = null)
    {
        Scene scene = SceneManager.GetSceneByPath(VillageRoomBuilder.ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            VillageMapGenerator generator = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VillageMapGenerator>(true)).FirstOrDefault(g => g != null);
            if (generator == null) throw new System.InvalidOperationException($"{VillageRoomBuilder.ScenePath}에서 VillageMapGenerator를 찾지 못했다.");
            return generator.CreateGridPlanInput(corridorWidthOverride);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GridPlannerSettings With(GridPlannerSettings s, int columns, int rows, float extraRatio) => new GridPlannerSettings
    {
        Columns = columns,
        Rows = rows,
        CorridorWidth = s.CorridorWidth,
        CorridorGap = s.CorridorGap,
        ExtraConnectionRatio = extraRatio,
        TruckPlacement = s.TruckPlacement,
        MaxAttempts = s.MaxAttempts,
    };

    private static void AppendRoomTypes(StringBuilder sb, IReadOnlyList<GridRoomType> types)
    {
        sb.AppendLine("방 종류:");
        foreach (GridRoomType t in types)
        {
            sb.AppendLine($"  {t.Name}: {t.Width}x{t.Height}, 바닥 {t.FloorCells.Count}, 장애물 칸 {t.ObstacleCells.Count}, " +
                          $"열린 차선 서/동/남/북 {string.Join("/", t.OpenLanes)}, 트럭 {(t.TruckAllowed ? "가능" : "-")}");
            foreach (string w in t.Warnings) sb.AppendLine("    경고: " + w);
        }
    }

    private static Texture2D RenderPlan(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types)
    {
        int tilesW = plan.Columns * plan.CellWidth + (plan.Columns - 1) * plan.CorridorGap + 2;
        int tilesH = plan.Rows * plan.CellHeight + (plan.Rows - 1) * plan.CorridorGap + 2;
        int w = tilesW * PixelsPerTile, h = tilesH * PixelsPerTile;
        Color[] pixels = Enumerable.Repeat(Background, w * h).ToArray();

        void Pixel(int px, int py, Color c)
        {
            if (px >= 0 && py >= 0 && px < w && py < h) pixels[py * w + px] = c;
        }
        void FillTile(int tx, int ty, Color c)
        {
            int px0 = (tx + 1) * PixelsPerTile, py0 = (ty + 1) * PixelsPerTile; // 가장자리 1칸 여백
            for (int py = py0; py < py0 + PixelsPerTile; py++)
                for (int px = px0; px < px0 + PixelsPerTile; px++) Pixel(px, py, c);
        }

        // 격자 칸 영역과 칸 경계(1px 테두리). 방이 칸 안에서 차지하는 비율이 보이도록 칸 전체를 옅게 칠하고 테두리를 두른다.
        for (int gy = 0; gy < plan.Rows; gy++)
        {
            for (int gx = 0; gx < plan.Columns; gx++)
            {
                int x0 = gx * (plan.CellWidth + plan.CorridorGap), y0 = gy * (plan.CellHeight + plan.CorridorGap);
                for (int ty = 0; ty < plan.CellHeight; ty++)
                    for (int tx = 0; tx < plan.CellWidth; tx++) FillTile(x0 + tx, y0 + ty, CellArea);
                int px0 = (x0 + 1) * PixelsPerTile, py0 = (y0 + 1) * PixelsPerTile;
                int px1 = px0 + plan.CellWidth * PixelsPerTile - 1, py1 = py0 + plan.CellHeight * PixelsPerTile - 1;
                for (int px = px0; px <= px1; px++) { Pixel(px, py0, CellBorder); Pixel(px, py1, CellBorder); }
                for (int py = py0; py <= py1; py++) { Pixel(px0, py, CellBorder); Pixel(px1, py, CellBorder); }
            }
        }

        // 길
        foreach (GridCorridor corridor in plan.Corridors)
            foreach (Vector2Int c in corridor.Cells) FillTile(c.x, c.y, Corridor);

        // 방 바닥과 장애물
        for (int gy = 0; gy < plan.Rows; gy++)
        {
            for (int gx = 0; gx < plan.Columns; gx++)
            {
                int ti = plan.RoomType[gx, gy];
                GridRoomType t = types[ti];
                Color floor = Palette[ti % Palette.Length];
                Color obstacle = Color.Lerp(floor, Color.black, 0.6f);
                Vector2Int o = plan.RoomOrigin[gx, gy];
                foreach (Vector2Int c in t.FloorCells) FillTile(c.x + o.x, c.y + o.y, t.ObstacleCells.Contains(c) ? obstacle : floor);
            }
        }

        // 포트: 차선의 가장 바깥 바닥 줄
        foreach (GridCorridor corridor in plan.Corridors)
            foreach (GridPort port in new[] { corridor.PortA, corridor.PortB })
                foreach (Vector2Int c in port.EdgeCells) FillTile(c.x, c.y, Port);

        // 트럭 칸 테두리
        {
            int x0 = plan.TruckCell.x * (plan.CellWidth + plan.CorridorGap), y0 = plan.TruckCell.y * (plan.CellHeight + plan.CorridorGap);
            int px0 = x0 * PixelsPerTile + 1, py0 = y0 * PixelsPerTile + 1;
            int px1 = (x0 + plan.CellWidth + 2) * PixelsPerTile - 2, py1 = (y0 + plan.CellHeight + 2) * PixelsPerTile - 2;
            for (int t = 0; t < 2; t++)
            {
                for (int px = px0; px <= px1; px++) { Pixel(px, py0 + t, TruckMark); Pixel(px, py1 - t, TruckMark); }
                for (int py = py0; py <= py1; py++) { Pixel(px0 + t, py, TruckMark); Pixel(px1 - t, py, TruckMark); }
            }
        }

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static string Describe(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types, GridPlannerSettings settings)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"격자 {plan.Columns}x{plan.Rows}, 시드 {plan.Seed}, {(plan.Success ? $"성공 (시도 {plan.Attempts}회)" : "실패: " + plan.Failure)}");
        sb.AppendLine($"칸 {plan.CellWidth}x{plan.CellHeight}, 틈 {plan.CorridorGap}, 길 폭 {plan.CorridorWidth}, 추가 연결 비율 {settings.ExtraConnectionRatio}, 트럭 {settings.TruckPlacement}");
        if (!plan.Success) return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("칸별 방 (위쪽 행부터, [트럭]):");
        for (int y = plan.Rows - 1; y >= 0; y--)
        {
            List<string> row = new List<string>();
            for (int x = 0; x < plan.Columns; x++)
            {
                string n = types[plan.RoomType[x, y]].Name;
                row.Add(new Vector2Int(x, y) == plan.TruckCell ? $"[{n}]" : n);
            }
            sb.AppendLine($"  y={y}: " + string.Join(" | ", row));
        }
        sb.AppendLine();
        sb.AppendLine($"연결 {plan.Edges.Count}개 (트리 {plan.TreeEdges.Count()}, 추가 {plan.ExtraEdges.Count()} = 끝에 +), 막다른 방 {GridLayoutPlanner.DeadEndRooms(plan)}, 고리 {GridLayoutPlanner.LoopCount(plan)}");
        foreach (GridCorridor c in plan.Corridors)
            sb.AppendLine($"  {c.Edge}: {c.PortA.Side} 차선 {c.PortA.LaneStart} → {c.PortB.Side} 차선 {c.PortB.LaneStart}, 길 {c.Cells.Count}칸");
        sb.AppendLine($"  길 칸 합계 {plan.Corridors.Sum(c => c.Cells.Count)}");
        sb.AppendLine();
        sb.AppendLine("색:");
        for (int i = 0; i < types.Count; i++)
        {
            Color32 c = Palette[i % Palette.Length];
            sb.AppendLine($"  #{c.r:X2}{c.g:X2}{c.b:X2} {types[i].Name}");
        }
        sb.AppendLine("  장애물 = 같은 색의 어두운 칸, 길 = 회색, 포트 = 노란색, 칸 경계 = 옅은 회색 테두리, 트럭 칸 = 빨간 테두리");
        List<string> problems = GridLayoutPlanner.Validate(plan, types);
        sb.AppendLine();
        sb.AppendLine(problems.Count == 0 ? "규칙 검사: 통과" : "규칙 검사 문제:\n  " + string.Join("\n  ", problems));
        return sb.ToString();
    }
}
