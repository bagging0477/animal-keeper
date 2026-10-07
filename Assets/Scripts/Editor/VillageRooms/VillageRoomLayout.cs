using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// VillageScene 방 프리팹을 만드는 텍스트 격자 레이아웃(Layouts/*.txt)을 읽고 검증한다. UnityEngine에 의존하지 않아서
/// VillageRoomBuilder(프리팹 생성)와 Unity 밖의 검사 도구가 같은 규칙을 그대로 쓴다.
///
/// 파일 형식: "키: 값" 머리말 줄들, "---" 한 줄, 그 아래 격자. 격자의 맨 아래 줄 맨 왼쪽 칸이 방 로컬 칸 (-1, -1)이다
/// (기존 방 프리팹의 Ground 원점과 같다). '#'으로 시작하는 줄은 주석. 글자 뜻은 Legend 참고.
/// </summary>
public sealed class VillageRoomLayout
{
    public enum CellKind { Void, Grass, Path, Blocking, Decor }

    public readonly struct Glyph
    {
        public readonly CellKind Kind;
        public readonly string Prop;     // 소품 종류 id (VillageRoomBuilder가 스프라이트로 바꾼다). 바닥만 있는 칸은 null
        public readonly bool OnPath;     // 길 타일 위에 놓이는지(장식만 해당)
        public readonly string Description;

        public Glyph(CellKind kind, string prop, bool onPath, string description)
        {
            Kind = kind; Prop = prop; OnPath = onPath; Description = description;
        }
    }

    // 막는 장애물(Blocking)은 BoxCollider2D가 붙어 GetRoomObstacles가 장애물 칸으로 읽는다. 장식(Decor)은 콜라이더가 없어
    // 그대로 바닥/스폰 가능 칸으로 남는다. 'O'는 2×2 덩어리 하나(큰 바위)로 묶인다.
    public static readonly Dictionary<char, Glyph> Legend = new Dictionary<char, Glyph>
    {
        { '~', new Glyph(CellKind.Void, null, false, "바닥 없음 (맵 바깥)") },
        { '.', new Glyph(CellKind.Grass, null, false, "잔디 바닥") },
        { ':', new Glyph(CellKind.Path, null, false, "길 (잔디 위 돌길 타일)") },

        { 'T', new Glyph(CellKind.Blocking, "Tree1", false, "나무 1 (막힘, 줄기가 이 칸)") },
        { 'Y', new Glyph(CellKind.Blocking, "Tree2", false, "나무 2 (막힘, 줄기가 이 칸)") },
        { 'B', new Glyph(CellKind.Blocking, "Bush", false, "덤불 (막힘)") },
        { 'R', new Glyph(CellKind.Blocking, "Rock", false, "바위 (막힘)") },
        { 'G', new Glyph(CellKind.Blocking, "Gravestone", false, "묘비 (막힘)") },
        { 'C', new Glyph(CellKind.Blocking, "Cross", false, "십자가 (막힘)") },
        { 'S', new Glyph(CellKind.Blocking, "Stele", false, "높은 비석 (막힘)") },
        { 'K', new Glyph(CellKind.Blocking, "Headstone", false, "받침 있는 묘비 (막힘)") },
        { 'U', new Glyph(CellKind.Blocking, "Urn", false, "항아리 (막힘)") },
        { 'W', new Glyph(CellKind.Blocking, "Wall", false, "낮은 돌담 (막힘, 이웃 담과 자동 연결)") },
        { 'O', new Glyph(CellKind.Blocking, "BigRock", false, "큰 바위 2×2 (막힘, 2×2로 묶어 적는다)") },

        { ',', new Glyph(CellKind.Decor, "Weed", false, "잡초 장식 (콜라이더 없음)") },
        { 'r', new Glyph(CellKind.Decor, "Pebble", false, "자갈 장식 (콜라이더 없음)") },
        { 'b', new Glyph(CellKind.Decor, "SmallBush", false, "작은 덤불 장식 (콜라이더 없음)") },
        { ';', new Glyph(CellKind.Decor, "Pebble", true, "길 위 자갈 장식 (콜라이더 없음)") },
    };

    // 다른 방과 이어지는 동/서 가장자리의 세로 범위. 기존 방 4개(A: -1..5, B: -1..18, C: 서 -1..18 동 -1..9, D: -1..7)가
    // 모두 갖고 있는 줄이 y -1..5이므로, 새 방은 이 7줄을 반듯한 입구로 둔다.
    public const int DoorwayMinY = -1;
    public const int DoorwayMaxY = 5;
    // 동/서 가장자리에서 장애물 없이 비워둘 열 수(가장자리 열 포함). 가장자리 바닥 전체 높이에 적용한다.
    public const int DoorwayClearColumns = 2;
    public const float MinSpawnableRatio = 0.4f;

    public readonly string Name;
    public readonly string Source;
    public readonly int Width;
    public readonly int Height;
    private readonly char[,] grid; // [x, y], y=0이 맨 아래 줄

    public const int OriginX = -1;
    public const int OriginY = -1;

    private VillageRoomLayout(string name, string source, char[,] grid)
    {
        Name = name; Source = source; this.grid = grid;
        Width = grid.GetLength(0); Height = grid.GetLength(1);
    }

    public char this[int localX, int localY]
    {
        get
        {
            int gx = localX - OriginX, gy = localY - OriginY;
            if (gx < 0 || gy < 0 || gx >= Width || gy >= Height) return '~';
            return grid[gx, gy];
        }
    }

    public int MinX => OriginX;
    public int MaxX => OriginX + Width - 1;
    public int MinY => OriginY;
    public int MaxY => OriginY + Height - 1;

    public Glyph GlyphAt(int x, int y) => Legend[this[x, y]];
    public bool IsFloor(int x, int y) => GlyphAt(x, y).Kind != CellKind.Void;
    public bool IsBlocking(int x, int y) => GlyphAt(x, y).Kind == CellKind.Blocking;
    public bool IsWalkable(int x, int y) => IsFloor(x, y) && !IsBlocking(x, y);
    public bool HasPathTile(int x, int y) => GlyphAt(x, y).Kind == CellKind.Path || GlyphAt(x, y).OnPath;

    public static VillageRoomLayout Parse(string text, string source)
    {
        string name = null;
        List<string> rows = new List<string>();
        bool inGrid = false;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!inGrid)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                if (trimmed == "---") { inGrid = true; continue; }
                int colon = trimmed.IndexOf(':');
                if (colon <= 0) throw new FormatException($"{source}:{i + 1}: 머리말은 '키: 값' 형식이어야 한다: {trimmed}");
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                if (key == "name") name = value;
                else throw new FormatException($"{source}:{i + 1}: 모르는 머리말 키 '{key}'");
            }
            else
            {
                string row = line.TrimEnd();
                if (row.StartsWith("#")) continue;
                if (row.Length == 0) continue;
                rows.Add(row);
            }
        }

        if (string.IsNullOrEmpty(name)) throw new FormatException($"{source}: 'name:' 머리말이 없다.");
        if (rows.Count == 0) throw new FormatException($"{source}: '---' 아래 격자가 비어 있다.");

        int width = rows[0].Length;
        char[,] grid = new char[width, rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length != width)
                throw new FormatException($"{source}: 격자 {r + 1}번째 줄 길이가 {rows[r].Length}칸이다 (첫 줄은 {width}칸). 모든 줄 길이를 맞춰야 한다.");
            int y = rows.Count - 1 - r;
            for (int x = 0; x < width; x++)
            {
                char c = rows[r][x];
                if (!Legend.ContainsKey(c))
                    throw new FormatException($"{source}: 격자 {r + 1}번째 줄 {x + 1}번째 글자 '{c}'는 범례에 없는 글자다.");
                grid[x, y] = c;
            }
        }
        return new VillageRoomLayout(name, source, grid);
    }

    /// <summary>'O' 글자를 2×2 덩어리로 묶는다(왼쪽 아래 칸 좌표 목록). 2×2로 딱 맞지 않으면 errors에 남긴다.</summary>
    public List<(int x, int y)> GroupBigRocks(List<string> errors)
    {
        List<(int x, int y)> blocks = new List<(int x, int y)>();
        HashSet<(int, int)> claimed = new HashSet<(int, int)>();
        for (int y = MinY; y <= MaxY; y++)
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                if (this[x, y] != 'O' || claimed.Contains((x, y))) continue;
                if (this[x + 1, y] == 'O' && this[x, y + 1] == 'O' && this[x + 1, y + 1] == 'O' &&
                    !claimed.Contains((x + 1, y)) && !claimed.Contains((x, y + 1)) && !claimed.Contains((x + 1, y + 1)))
                {
                    claimed.Add((x, y)); claimed.Add((x + 1, y)); claimed.Add((x, y + 1)); claimed.Add((x + 1, y + 1));
                    blocks.Add((x, y));
                }
                else
                {
                    errors?.Add($"큰 바위 'O' ({x}, {y})가 2×2 덩어리로 묶이지 않는다.");
                }
            }
        }
        return blocks;
    }

    public sealed class Report
    {
        public int FloorCells, BlockingCells, DecorCells, PathCells, EdgeCells, SpawnableCells;
        public float SpawnableRatio;
        public List<int> WestEdgeRows = new List<int>(), EastEdgeRows = new List<int>();
        public List<(int x, int y)> UnreachableWalkable = new List<(int x, int y)>();
        public List<(int x, int y)> NarrowCells = new List<(int x, int y)>();
        public List<(int x, int y)> ProtrudingCells = new List<(int x, int y)>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// 생성기(VillageMapGenerator)와 같은 기준으로 센다: 스폰 가능 칸 = 바닥 - 장애물 칸 - 가장자리(상하좌우 중 하나라도 바닥이
    /// 아닌 칸, PickInteriorCell 기준). 도달 가능 여부는 동/서 입구 칸에서 4방향으로 장애물 없는 바닥만 밟아 퍼뜨려 본다.
    /// </summary>
    public Report Analyze()
    {
        Report report = new Report();
        GroupBigRocks(report.Errors);

        for (int y = MinY; y <= MaxY; y++)
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                if (!IsFloor(x, y)) continue;
                report.FloorCells++;
                Glyph g = GlyphAt(x, y);
                if (g.Kind == CellKind.Blocking) report.BlockingCells++;
                if (g.Kind == CellKind.Decor) report.DecorCells++;
                if (HasPathTile(x, y)) report.PathCells++;
                bool edge = !IsFloor(x + 1, y) || !IsFloor(x - 1, y) || !IsFloor(x, y + 1) || !IsFloor(x, y - 1);
                if (edge) report.EdgeCells++;
                if (!edge && g.Kind != CellKind.Blocking) report.SpawnableCells++;
            }
        }
        report.SpawnableRatio = report.FloorCells > 0 ? (float)report.SpawnableCells / report.FloorCells : 0f;
        if (report.SpawnableRatio < MinSpawnableRatio)
            report.Errors.Add($"스폰 가능 칸이 바닥의 {report.SpawnableRatio:P0}로 {MinSpawnableRatio:P0} 미만이다.");

        // 동/서 가장자리: 생성기는 바닥의 맨 왼쪽/오른쪽 열끼리 맞댄다(GetEdgeRows).
        int westX = int.MaxValue, eastX = int.MinValue;
        for (int y = MinY; y <= MaxY; y++)
            for (int x = MinX; x <= MaxX; x++)
                if (IsFloor(x, y)) { westX = Math.Min(westX, x); eastX = Math.Max(eastX, x); }
        for (int y = MinY; y <= MaxY; y++)
        {
            if (IsFloor(westX, y)) report.WestEdgeRows.Add(y);
            if (IsFloor(eastX, y)) report.EastEdgeRows.Add(y);
        }
        CheckDoorway(report, "서쪽", westX, +1, report.WestEdgeRows);
        CheckDoorway(report, "동쪽", eastX, -1, report.EastEdgeRows);

        // 입구 칸에서 출발해 걸어서 닿는 칸.
        HashSet<(int, int)> reached = new HashSet<(int, int)>();
        Queue<(int x, int y)> queue = new Queue<(int x, int y)>();
        foreach (int y in report.WestEdgeRows) if (IsWalkable(westX, y) && reached.Add((westX, y))) queue.Enqueue((westX, y));
        foreach (int y in report.EastEdgeRows) if (IsWalkable(eastX, y) && reached.Add((eastX, y))) queue.Enqueue((eastX, y));
        while (queue.Count > 0)
        {
            (int x, int y) c = queue.Dequeue();
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = c.x + dx, ny = c.y + dy;
                if (IsWalkable(nx, ny) && reached.Add((nx, ny))) queue.Enqueue((nx, ny));
            }
        }
        for (int y = MinY; y <= MaxY; y++)
            for (int x = MinX; x <= MaxX; x++)
                if (IsWalkable(x, y) && !reached.Contains((x, y))) report.UnreachableWalkable.Add((x, y));
        if (report.UnreachableWalkable.Count > 0)
            report.Errors.Add($"입구에서 걸어서 닿지 않는 칸이 {report.UnreachableWalkable.Count}개 있다: {FormatCells(report.UnreachableWalkable)}");

        // NavMesh는 에이전트 반경(0.5)만큼 바닥 가장자리/장애물에서 깎여서, 폭 1칸 통로는 몬스터가 지나가지 못한다.
        // 걸을 수 있는 칸마다 그 칸을 포함하는 2×2 칸이 전부 걸을 수 있는지 본다.
        for (int y = MinY; y <= MaxY; y++)
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                if (!IsWalkable(x, y)) continue;
                bool inWideArea = false;
                for (int ox = -1; ox <= 0 && !inWideArea; ox++)
                    for (int oy = -1; oy <= 0 && !inWideArea; oy++)
                        inWideArea = IsWalkable(x + ox, y + oy) && IsWalkable(x + ox + 1, y + oy) &&
                                     IsWalkable(x + ox, y + oy + 1) && IsWalkable(x + ox + 1, y + oy + 1);
                if (!inWideArea) report.NarrowCells.Add((x, y));
            }
        }
        // 가장자리 모양: 바닥 칸은 모두 3×3 바닥 덩어리 안에 들어 있어야 한다. 아니면 폭 1~2칸으로 툭 튀어나온 칸이거나
        // 좁은 홈 옆에 남은 조각이라 바닥 윤곽이 지저분해 보인다(위/아래 가장자리의 들쭉날쭉함은 3칸 단위 이상으로 둔다).
        for (int y = MinY; y <= MaxY; y++)
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                if (!IsFloor(x, y)) continue;
                bool inBlock = false;
                for (int ox = -2; ox <= 0 && !inBlock; ox++)
                    for (int oy = -2; oy <= 0 && !inBlock; oy++)
                    {
                        bool all = true;
                        for (int i = 0; i < 3 && all; i++)
                            for (int j = 0; j < 3 && all; j++)
                                all = IsFloor(x + ox + i, y + oy + j);
                        inBlock = all;
                    }
                if (!inBlock) report.ProtrudingCells.Add((x, y));
            }
        }
        if (report.ProtrudingCells.Count > 0)
            report.Errors.Add($"가장자리에서 툭 튀어나온 바닥 칸 {report.ProtrudingCells.Count}개 (3×3 바닥 덩어리에 속하지 않는다): {FormatCells(report.ProtrudingCells)}");

        if (report.NarrowCells.Count > 0)
            report.Warnings.Add($"폭 1칸 통로/구석 칸 {report.NarrowCells.Count}개 (몬스터 NavMesh가 지나가지 못할 수 있다): {FormatCells(report.NarrowCells)}");

        return report;
    }

    private void CheckDoorway(Report report, string side, int edgeX, int inward, List<int> rows)
    {
        HashSet<int> set = new HashSet<int>(rows);
        for (int y = DoorwayMinY; y <= DoorwayMaxY; y++)
            if (!set.Contains(y)) report.Errors.Add($"{side} 가장자리 열(x={edgeX})의 y={y}에 바닥이 없다 - 입구 y {DoorwayMinY}..{DoorwayMaxY}는 모두 바닥이어야 한다.");

        // 생성기는 맞닿는 줄 수가 가장 많은 높이로 이웃 방을 붙이는데, 가장자리가 이웃보다 길면 같은 줄 수가 나오는 높이가 여럿이라
        // 어느 높이에 붙을지 알 수 없다. 가장자리 바닥 전체 높이에 걸쳐 앞쪽 열을 비워둬야 어디에 붙어도 입구가 막히지 않는다.
        foreach (int y in rows)
            for (int i = 0; i < DoorwayClearColumns; i++)
                if (IsBlocking(edgeX + inward * i, y)) report.Errors.Add($"{side} 가장자리 ({edgeX + inward * i}, {y})에 장애물이 있다 - 가장자리 {DoorwayClearColumns}열은 비워둬야 한다.");
        int contiguous = 0, best = 0, prev = int.MinValue;
        foreach (int y in rows) { contiguous = y == prev + 1 ? contiguous + 1 : 1; best = Math.Max(best, contiguous); prev = y; }
        if (best < 5) report.Errors.Add($"{side} 가장자리의 연속 바닥이 {best}줄뿐이다 (5줄 이상 필요).");
        if (rows.Count != best) report.Warnings.Add($"{side} 가장자리 바닥이 중간에 끊겨 있다 ({string.Join(",", rows)}).");
    }

    private static string FormatCells(List<(int x, int y)> cells)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < cells.Count && i < 20; i++) sb.Append(i == 0 ? "" : " ").Append('(').Append(cells[i].x).Append(',').Append(cells[i].y).Append(')');
        if (cells.Count > 20) sb.Append(" ...");
        return sb.ToString();
    }

    public string Summarize(Report r)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"{Name} ({Width}×{Height}, 로컬 x {MinX}..{MaxX}, y {MinY}..{MaxY})");
        sb.AppendLine($"  바닥 {r.FloorCells}칸 / 막는 장애물 {r.BlockingCells} / 장식 {r.DecorCells} / 길 {r.PathCells} / 가장자리 {r.EdgeCells}");
        sb.AppendLine($"  스폰 가능 {r.SpawnableCells}칸 = 바닥의 {r.SpawnableRatio:P1}");
        sb.AppendLine($"  서쪽 가장자리 y: {RangeText(r.WestEdgeRows)} / 동쪽 가장자리 y: {RangeText(r.EastEdgeRows)}");
        sb.AppendLine($"  도달 불가 칸: {r.UnreachableWalkable.Count} / 폭 1칸 칸: {r.NarrowCells.Count} / 튀어나온 칸: {r.ProtrudingCells.Count}");
        foreach (string e in r.Errors) sb.AppendLine("  [오류] " + e);
        foreach (string w in r.Warnings) sb.AppendLine("  [경고] " + w);
        return sb.ToString();
    }

    private static string RangeText(List<int> ys) => ys.Count == 0 ? "없음" : $"{ys[0]}..{ys[ys.Count - 1]} ({ys.Count}줄)";
}
