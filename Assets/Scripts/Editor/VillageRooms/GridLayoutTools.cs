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
/// - Grid Preview: 계획을 위에서 본 PNG로 저장한다(칸당 4px). 방 바닥은 종류별 색, 장애물은 같은 색의 어두운 칸, 연결은 선
///   (스패닝 트리 = 흰색, 추가 연결 = 주황색), 트럭 칸은 빨간 테두리. 같은 이름의 .txt에 칸별 방 이름과 연결 목록을 남긴다.
/// - Stress Test: 여러 격자 크기 x 시드로 계획을 세워 1단계 규칙(실패, 같은 방 인접, 종류별 개수 차이, 연결성, 같은 시드 재현)을 검사한다.
/// 방 목록과 설정은 VillageScene의 VillageMapGenerator에서 읽는다(씬이 열려 있지 않으면 잠깐 추가로 열었다가 닫는다).
/// 결과: Logs/MapPreview/
/// </summary>
public static class GridLayoutTools
{
    public const string OutputFolder = "Logs/MapPreview";
    private const int PixelsPerTile = 4;

    private static readonly Color Background = new Color32(22, 22, 26, 255);
    private static readonly Color CellArea = new Color32(36, 36, 44, 255);
    private static readonly Color TreeEdge = new Color32(240, 240, 240, 255);
    private static readonly Color ExtraEdge = new Color32(255, 159, 28, 255);
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
        GridPlannerSettings settings = WithSize(input.Settings, columns, rows);
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
        sb.AppendLine("방 종류:");
        foreach (GridRoomType t in input.Types)
            sb.AppendLine($"  {t.Name}: {t.Width}x{t.Height}, 바닥 {t.FloorCells.Count}, 장애물 칸 {t.ObstacleCells.Count}, " +
                          $"열린 차선 서/동/남/북 {string.Join("/", t.OpenLanes)}, 트럭 {(t.TruckAllowed ? "가능" : "-")}");

        bool allPass = true;
        foreach ((int c, int r) in new[] { (3, 4), (4, 5), (3, 3) })
        {
            GridPlannerSettings settings = WithSize(input.Settings, c, r);
            int fails = 0, sameAdj = 0, maxSpread = 0, notConnected = 0, nonDeterministic = 0, otherProblems = 0;
            List<int> attempts = new List<int>(), extras = new List<int>();
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
                extras.Add(plan.ExtraEdges.Count());
                string truckRoom = input.Types[plan.RoomType[plan.TruckCell.x, plan.TruckCell.y]].Name;
                truckRooms[truckRoom] = truckRooms.TryGetValue(truckRoom, out int n) ? n + 1 : 1;

                int[] counts = new int[input.Types.Count];
                for (int y = 0; y < plan.Rows; y++) for (int x = 0; x < plan.Columns; x++) counts[plan.RoomType[x, y]]++;
                maxSpread = Mathf.Max(maxSpread, counts.Max() - counts.Min());

                foreach (string p in GridLayoutPlanner.Validate(plan, input.Types))
                {
                    if (p.StartsWith("같은 방 인접")) sameAdj++;
                    else if (p.StartsWith("연결성")) notConnected++;
                    else if (!p.StartsWith("종류별 개수 차이")) otherProblems++;
                    notes.Add($"seed {seed}: {p}");
                }
            }
            bool pass = fails == 0 && sameAdj == 0 && maxSpread <= 1 && notConnected == 0 && nonDeterministic == 0 && otherProblems == 0;
            allPass &= pass;
            ms.Sort();
            sb.AppendLine();
            sb.AppendLine($"== {c}x{r} ({c * r}칸) - {(pass ? "통과" : "실패")}");
            sb.AppendLine($"  계획 실패 {fails}/{seedsPerSize}, 같은 방 인접 {sameAdj}, 종류별 개수 차이 최대 {maxSpread}, 연결 안 됨 {notConnected}, " +
                          $"같은 시드 재현 불일치 {nonDeterministic}, 기타 문제 {otherProblems}");
            if (attempts.Count > 0)
                sb.AppendLine($"  시도 횟수 평균 {attempts.Average():F2} / 최대 {attempts.Max()}, 추가 연결 {extras.Min()}~{extras.Max()}개, " +
                              $"계획 시간 중앙값 {ms[ms.Count / 2]:F2}ms / 최대 {ms[ms.Count - 1]:F2}ms");
            sb.AppendLine("  트럭 칸의 방: " + string.Join(", ", truckRooms.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}회")));
            foreach (string note in notes.Take(20)) sb.AppendLine("  " + note);
        }
        sb.AppendLine();
        sb.AppendLine(allPass ? "결과: 모든 크기 통과" : "결과: 실패한 크기가 있다");

        Directory.CreateDirectory(OutputFolder);
        string path = $"{OutputFolder}/grid_stress.txt";
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>VillageScene의 VillageMapGenerator 설정으로 계획 입력을 만든다. 씬이 안 열려 있으면 추가로 열었다가 닫는다(저장하지 않는다).</summary>
    public static GridPlanInput LoadInputFromVillageScene()
    {
        Scene scene = SceneManager.GetSceneByPath(VillageRoomBuilder.ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            VillageMapGenerator generator = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VillageMapGenerator>(true)).FirstOrDefault(g => g != null);
            if (generator == null) throw new System.InvalidOperationException($"{VillageRoomBuilder.ScenePath}에서 VillageMapGenerator를 찾지 못했다.");
            return generator.CreateGridPlanInput();
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GridPlannerSettings WithSize(GridPlannerSettings s, int columns, int rows) => new GridPlannerSettings
    {
        Columns = columns,
        Rows = rows,
        CorridorWidth = s.CorridorWidth,
        CorridorGap = s.CorridorGap,
        ExtraConnectionRatio = s.ExtraConnectionRatio,
        TruckPlacement = s.TruckPlacement,
        MaxAttempts = s.MaxAttempts,
    };

    private static Texture2D RenderPlan(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types)
    {
        int tilesW = plan.Columns * plan.CellWidth + (plan.Columns - 1) * plan.CorridorGap + 2;
        int tilesH = plan.Rows * plan.CellHeight + (plan.Rows - 1) * plan.CorridorGap + 2;
        int w = tilesW * PixelsPerTile, h = tilesH * PixelsPerTile;
        Color[] pixels = Enumerable.Repeat(Background, w * h).ToArray();

        void FillTile(int tx, int ty, Color c)
        {
            int px0 = (tx + 1) * PixelsPerTile, py0 = (ty + 1) * PixelsPerTile; // 가장자리 1칸 여백
            for (int py = py0; py < py0 + PixelsPerTile; py++)
                for (int px = px0; px < px0 + PixelsPerTile; px++)
                    if (px >= 0 && py >= 0 && px < w && py < h) pixels[py * w + px] = c;
        }
        void Dot(float tx, float ty, int radius, Color c)
        {
            int cx = Mathf.RoundToInt((tx + 1) * PixelsPerTile), cy = Mathf.RoundToInt((ty + 1) * PixelsPerTile);
            for (int py = cy - radius; py <= cy + radius; py++)
                for (int px = cx - radius; px <= cx + radius; px++)
                    if (px >= 0 && py >= 0 && px < w && py < h) pixels[py * w + px] = c;
        }

        // 격자 칸 영역
        for (int gy = 0; gy < plan.Rows; gy++)
            for (int gx = 0; gx < plan.Columns; gx++)
                for (int ty = 0; ty < plan.CellHeight; ty++)
                    for (int tx = 0; tx < plan.CellWidth; tx++)
                        FillTile(gx * (plan.CellWidth + plan.CorridorGap) + tx, gy * (plan.CellHeight + plan.CorridorGap) + ty, CellArea);

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

        // 트럭 칸 테두리
        {
            int x0 = plan.TruckCell.x * (plan.CellWidth + plan.CorridorGap), y0 = plan.TruckCell.y * (plan.CellHeight + plan.CorridorGap);
            for (int tx = -1; tx <= plan.CellWidth; tx++) { FillTile(x0 + tx, y0 - 1, TruckMark); FillTile(x0 + tx, y0 + plan.CellHeight, TruckMark); }
            for (int ty = -1; ty <= plan.CellHeight; ty++) { FillTile(x0 - 1, y0 + ty, TruckMark); FillTile(x0 + plan.CellWidth, y0 + ty, TruckMark); }
        }

        // 연결: 두 방 바닥 박스 중심을 잇는 선
        Vector2 Center(Vector2Int cell)
        {
            GridRoomType t = types[plan.RoomType[cell.x, cell.y]];
            Vector2Int o = plan.RoomOrigin[cell.x, cell.y];
            return new Vector2(o.x + t.MinX + t.Width * 0.5f, o.y + t.MinY + t.Height * 0.5f);
        }
        foreach (GridEdge e in plan.Edges.OrderBy(e => e.IsExtra ? 0 : 1)) // 트리 선이 위에 오게
        {
            Vector2 a = Center(e.A), b = Center(e.B);
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * PixelsPerTile);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
                Dot(p.x, p.y, 1, e.IsExtra ? ExtraEdge : TreeEdge);
            }
        }
        foreach (Vector2Int cell in AllCells(plan)) Dot(Center(cell).x, Center(cell).y, 3, cell == plan.TruckCell ? TruckMark : TreeEdge);

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static IEnumerable<Vector2Int> AllCells(GridLayoutPlan plan)
    {
        for (int y = 0; y < plan.Rows; y++)
            for (int x = 0; x < plan.Columns; x++) yield return new Vector2Int(x, y);
    }

    private static string Describe(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types, GridPlannerSettings settings)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"격자 {plan.Columns}x{plan.Rows}, 시드 {plan.Seed}, {(plan.Success ? $"성공 (시도 {plan.Attempts}회)" : "실패: " + plan.Failure)}");
        sb.AppendLine($"칸 {plan.CellWidth}x{plan.CellHeight}, 틈 {plan.CorridorGap}, 추가 연결 비율 {settings.ExtraConnectionRatio}, 트럭 {settings.TruckPlacement}");
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
        sb.AppendLine($"연결 {plan.Edges.Count}개 (트리 {plan.TreeEdges.Count()} = 흰 선, 추가 {plan.ExtraEdges.Count()} = 주황 선):");
        sb.AppendLine("  " + string.Join(" ", plan.Edges));
        sb.AppendLine();
        sb.AppendLine("색:");
        for (int i = 0; i < types.Count; i++)
        {
            Color32 c = Palette[i % Palette.Length];
            sb.AppendLine($"  #{c.r:X2}{c.g:X2}{c.b:X2} {types[i].Name}");
        }
        sb.AppendLine("  장애물 = 같은 색의 어두운 칸, 트럭 칸 = 빨간 테두리와 빨간 점");
        List<string> problems = GridLayoutPlanner.Validate(plan, types);
        sb.AppendLine();
        sb.AppendLine(problems.Count == 0 ? "규칙 검사: 통과" : "규칙 검사 문제:\n  " + string.Join("\n  ", problems));
        return sb.ToString();
    }
}
