using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>VillageScene 맵을 어떤 모양으로 조립할지. Linear는 기존 방식(동쪽으로 한 줄), Grid는 열x행 격자에 방을 놓고 길로 잇는다.</summary>
public enum MapLayoutMode { Linear, Grid }

/// <summary>격자 맵에서 트럭(시작 지점)을 둘 칸.</summary>
public enum TruckPlacement { Center, EdgeCenter, Corner }

/// <summary>방의 네 변. 격자에서 이웃 칸과 길로 이어지는 방향.</summary>
public enum GridSide { West = 0, East = 1, South = 2, North = 3 }

/// <summary>격자 계획에 쓰는 방 종류 하나의 데이터. 방 프리팹에서 GridRoomAnalyzer가 만든다.</summary>
public sealed class GridRoomType
{
    public string Name;
    public int MinX, MinY;        // 바닥 바운딩 박스의 로컬 최소 칸
    public int Width, Height;     // 바닥 바운딩 박스 크기(칸)
    public List<Vector2Int> FloorCells = new List<Vector2Int>();
    public HashSet<Vector2Int> ObstacleCells = new HashSet<Vector2Int>();
    // 변마다 corridorWidth 폭 길이 들어갈 수 있는 차선 수(GridRoomAnalyzer 기준). 0이면 그 변으로는 이웃과 잇지 않는다(포트 없음).
    public int[] OpenLanes = new int[4];
    // 변마다 쓸 수 있는 차선의 시작 줄(로컬 칸; 동/서 변은 y, 남/북 변은 x). 차선은 시작 줄부터 corridorWidth줄이다.
    public List<int>[] LaneStarts = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
    // 변마다 줄별 가장 바깥 바닥 칸의 깊이(로컬 칸; 동/서 변은 줄 y의 x, 남/북 변은 줄 x의 y).
    public Dictionary<int, int>[] OuterDepth = { new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>() };
    public List<string> Warnings = new List<string>();
    public bool TruckAllowed;

    public bool IsOpen(GridSide side) => OpenLanes[(int)side] > 0;
}

public sealed class GridPlannerSettings
{
    public int Columns = 3;
    public int Rows = 4;
    public int CorridorWidth = 4;
    public int CorridorGap = 6;
    public float ExtraConnectionRatio = 0.25f;
    public TruckPlacement TruckPlacement = TruckPlacement.Center;
    public int MaxAttempts = 20;
}

/// <summary>GridLayoutPlanner.Plan에 넘길 방 종류와 설정 묶음(VillageMapGenerator.CreateGridPlanInput이 만든다).</summary>
public sealed class GridPlanInput
{
    public List<GridRoomType> Types = new List<GridRoomType>();
    public List<GameObject> Prefabs = new List<GameObject>();   // Types와 같은 순서의 방 프리팹
    public GridPlannerSettings Settings = new GridPlannerSettings();
}

/// <summary>격자의 이웃한 두 칸을 잇는 연결. A가 B의 서쪽이거나 남쪽이다.</summary>
public readonly struct GridEdge
{
    public readonly Vector2Int A, B;
    public readonly bool IsExtra;   // 스패닝 트리 밖에서 고리를 만들려고 더한 연결

    public GridEdge(Vector2Int a, Vector2Int b, bool isExtra) { A = a; B = b; IsExtra = isExtra; }
    public bool Horizontal => A.y == B.y;
    public override string ToString() => $"({A.x},{A.y})-({B.x},{B.y}){(IsExtra ? "+" : "")}";
}

/// <summary>길이 방에 들어가는 자리. 방 한 변의 corridorWidth줄짜리 차선이다.</summary>
public sealed class GridPort
{
    public Vector2Int Cell;
    public GridSide Side;
    public int LaneStart;                                         // 로컬 칸 기준 차선 시작 줄
    public List<Vector2Int> EdgeCells = new List<Vector2Int>();   // 차선 각 줄의 가장 바깥 바닥 칸(월드 칸)
    public List<Vector2Int> EntryCells = new List<Vector2Int>();  // EdgeCells와 그 안쪽 1칸(장애물이 없어야 하는 칸, 월드 칸)
}

/// <summary>연결 하나를 이루는 길(바닥이 없던 칸에 새로 까는 칸들).</summary>
public sealed class GridCorridor
{
    public GridEdge Edge;
    public GridPort PortA, PortB;
    public List<Vector2Int> Cells = new List<Vector2Int>();       // 월드 칸, (y, x) 순 정렬
}

/// <summary>GridLayoutPlanner.Plan의 결과. 씬 오브젝트는 담지 않고, 칸별 방 종류·위치, 연결, 길 칸만 담는다.</summary>
public sealed class GridLayoutPlan
{
    public int Seed;
    public int Columns, Rows;
    public bool Success;
    public string Failure;
    public int Attempts;
    public int CellWidth, CellHeight, CorridorGap;
    public string[] TypeNames;           // types 인덱스 → 방 이름
    public int[,] RoomType;              // [x, y] → types 인덱스
    public Vector2Int[,] RoomOrigin;     // [x, y] → 방 로컬 칸에 더할 월드 칸 오프셋(칸 가운데에 놓는다)
    public Vector2Int TruckCell;
    public List<GridEdge> Edges = new List<GridEdge>();
    public int CorridorWidth;
    public List<GridCorridor> Corridors = new List<GridCorridor>();

    public IEnumerable<GridEdge> TreeEdges => Edges.Where(e => !e.IsExtra);
    public IEnumerable<GridEdge> ExtraEdges => Edges.Where(e => e.IsExtra);

    /// <summary>같은 시드로 두 번 만든 결과가 같은지 비교하는 용도의 요약 문자열.</summary>
    public string Signature()
    {
        if (!Success) return "FAIL:" + Failure;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++) sb.Append(RoomType[x, y]).Append(',');
        sb.Append('|').Append(TruckCell.x).Append(',').Append(TruckCell.y).Append('|');
        foreach (GridEdge e in Edges) sb.Append(e).Append(';');
        sb.Append('|');
        foreach (GridCorridor c in Corridors)
        {
            sb.Append(c.Edge).Append(':').Append(c.PortA.LaneStart).Append('/').Append(c.PortB.LaneStart).Append(':');
            foreach (Vector2Int cell in c.Cells) sb.Append(cell.x).Append(',').Append(cell.y).Append(' ');
            sb.Append(';');
        }
        return sb.ToString();
    }
}

/// <summary>
/// 격자 맵 배치 계획(1단계). 씬·물리·UnityEngine.Random을 쓰지 않는 순수 계산이라 에디터에서 수백 번 돌려도 빠르고,
/// 같은 시드면 항상 같은 결과가 나온다(System.Random만 쓴다 - 한 줄 모드의 UnityEngine.Random 순서에 영향이 없다).
///
/// 1. 트럭 칸을 정한다(TruckPlacement).
/// 2. 방 종류를 칸 수만큼 골고루 담은 주머니에서 칸마다 하나씩 꺼내 배정한다 - 상하좌우 이웃과 같은 종류는 피하고,
///    트럭 칸에는 TruckAllowed 종류만 둔다(백트래킹).
/// 3. 양쪽 변이 모두 열린 이웃 쌍을 후보 연결로 두고, 섞은 순서의 Kruskal로 스패닝 트리를 만들어 모든 방이 이어지게 한다.
///    트리가 안 만들어지면(열린 변이 부족) 2부터 다시 한다(MaxAttempts).
/// 4. 트리 밖 후보 중 ceil(남은 수 x ExtraConnectionRatio)개를 더해 고리를 만든다.
/// 5. 연결마다 양쪽 방의 포트(마주 보는 변의 차선)를 고르고 Z자 길(직선 → 칸 사이 틈에서 한 번 꺾기 → 직선)을 칸으로 계산한다.
///    가로 연결의 꺾이는 구간은 두 칸 사이 세로 틈 안(그 행의 y 범위)에만, 세로 연결은 가로 틈 안(그 열의 x 범위)에만 생기고
///    틈 양쪽에 1칸 이상 여백을 두므로, 길끼리 만나거나 다른 방 바닥에 닿지 않는다(ValidateCorridors가 다시 확인한다).
/// </summary>
public static class GridLayoutPlanner
{
    private const int MaxBacktrackSteps = 200000;

    public static GridLayoutPlan Plan(int seed, IReadOnlyList<GridRoomType> types, GridPlannerSettings settings)
    {
        GridLayoutPlan plan = new GridLayoutPlan
        {
            Seed = seed,
            Columns = Mathf.Max(1, settings.Columns),
            Rows = Mathf.Max(1, settings.Rows),
            CorridorGap = Mathf.Max(0, settings.CorridorGap),
            CorridorWidth = Mathf.Max(1, settings.CorridorWidth),
        };
        if (types == null || types.Count == 0) return Fail(plan, "방 종류가 없다");
        plan.TypeNames = types.Select(t => t.Name).ToArray();
        if (plan.CorridorGap < plan.CorridorWidth + 2)
            return Fail(plan, $"칸 사이 틈({plan.CorridorGap})이 길 폭({plan.CorridorWidth}) + 양쪽 여백 2보다 좁다");
        int cellCount = plan.Columns * plan.Rows;
        if (types.Count == 1 && cellCount > 1) return Fail(plan, "방 종류가 1개뿐이라 이웃한 칸에 같은 방이 올 수밖에 없다");
        if (!types.Any(t => t.TruckAllowed)) return Fail(plan, "트럭을 둘 수 있는 방 종류(TruckAllowed)가 없다");

        plan.CellWidth = types.Max(t => t.Width);
        plan.CellHeight = types.Max(t => t.Height);

        System.Random rng = new System.Random(seed);
        plan.TruckCell = PickTruckCell(plan.Columns, plan.Rows, settings.TruckPlacement, rng);

        for (int attempt = 1; attempt <= Mathf.Max(1, settings.MaxAttempts); attempt++)
        {
            plan.Attempts = attempt;
            int[,] assignment = AssignRooms(plan.Columns, plan.Rows, plan.TruckCell, types, rng);
            if (assignment == null) continue;

            List<GridEdge> candidates = CandidateEdges(plan.Columns, plan.Rows, assignment, types);
            Shuffle(candidates, rng);
            List<GridEdge> tree = new List<GridEdge>(), rest = new List<GridEdge>();
            int[] parent = Enumerable.Range(0, cellCount).ToArray();
            foreach (GridEdge e in candidates)
            {
                int a = Find(parent, Index(e.A, plan.Columns)), b = Find(parent, Index(e.B, plan.Columns));
                if (a != b) { parent[a] = b; tree.Add(e); }
                else rest.Add(e);
            }
            if (tree.Count != cellCount - 1) continue;

            int extraCount = Mathf.CeilToInt(rest.Count * Mathf.Clamp01(settings.ExtraConnectionRatio));
            plan.Edges = tree.Concat(rest.Take(extraCount).Select(e => new GridEdge(e.A, e.B, true))).ToList();
            plan.RoomType = assignment;
            plan.RoomOrigin = RoomOrigins(plan, types);
            plan.Corridors = plan.Edges.Select(e => BuildCorridor(plan, types, e)).ToList();
            plan.Success = true;
            plan.Failure = null;
            return plan;
        }
        return Fail(plan, $"{plan.Attempts}번 시도했지만 모든 방을 잇는 배치를 만들지 못했다");
    }

    /// <summary>계획이 1단계 규칙을 지키는지 검사한다. 문제 목록(비어 있으면 통과).</summary>
    public static List<string> Validate(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types)
    {
        List<string> problems = new List<string>();
        if (!plan.Success) { problems.Add("계획 실패: " + plan.Failure); return problems; }

        int[] counts = new int[types.Count];
        for (int y = 0; y < plan.Rows; y++)
        {
            for (int x = 0; x < plan.Columns; x++)
            {
                int t = plan.RoomType[x, y];
                counts[t]++;
                if (x + 1 < plan.Columns && plan.RoomType[x + 1, y] == t) problems.Add($"같은 방 인접: ({x},{y})-({x + 1},{y}) {types[t].Name}");
                if (y + 1 < plan.Rows && plan.RoomType[x, y + 1] == t) problems.Add($"같은 방 인접: ({x},{y})-({x},{y + 1}) {types[t].Name}");
            }
        }
        int spread = counts.Max() - counts.Min();
        if (spread > 1) problems.Add($"종류별 개수 차이 {spread} ({string.Join(", ", counts)})");

        if (!types[plan.RoomType[plan.TruckCell.x, plan.TruckCell.y]].TruckAllowed)
            problems.Add($"트럭 칸 ({plan.TruckCell.x},{plan.TruckCell.y})에 트럭을 둘 수 없는 방 {types[plan.RoomType[plan.TruckCell.x, plan.TruckCell.y]].Name}");

        int cellCount = plan.Columns * plan.Rows;
        if (plan.TreeEdges.Count() != cellCount - 1) problems.Add($"트리 연결 {plan.TreeEdges.Count()}개 (기대 {cellCount - 1})");
        HashSet<(Vector2Int, Vector2Int)> seen = new HashSet<(Vector2Int, Vector2Int)>();
        foreach (GridEdge e in plan.Edges)
        {
            Vector2Int d = e.B - e.A;
            if (!(d == Vector2Int.right || d == Vector2Int.up)) problems.Add($"이웃이 아닌 연결 {e}");
            if (!seen.Add((e.A, e.B))) problems.Add($"중복 연결 {e}");
            GridSide sa = e.Horizontal ? GridSide.East : GridSide.North, sb = e.Horizontal ? GridSide.West : GridSide.South;
            if (!types[plan.RoomType[e.A.x, e.A.y]].IsOpen(sa) || !types[plan.RoomType[e.B.x, e.B.y]].IsOpen(sb))
                problems.Add($"닫힌 변을 잇는 연결 {e}");
        }

        // 연결성: 트럭 칸에서 연결만 따라 모든 칸에 닿는지.
        HashSet<Vector2Int> reached = new HashSet<Vector2Int> { plan.TruckCell };
        Queue<Vector2Int> queue = new Queue<Vector2Int>(reached);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (GridEdge e in plan.Edges)
            {
                Vector2Int n = e.A == c ? e.B : e.B == c ? e.A : c;
                if (n != c && reached.Add(n)) queue.Enqueue(n);
            }
        }
        if (reached.Count != cellCount) problems.Add($"연결성: 트럭 칸에서 {reached.Count}/{cellCount}칸만 닿는다");
        if (plan.Corridors.Count != plan.Edges.Count) problems.Add($"길 {plan.Corridors.Count}개 (연결 {plan.Edges.Count}개와 다르다)");
        problems.AddRange(ValidateCorridors(plan, types));
        return problems;
    }

    /// <summary>
    /// 길 검사: 길끼리 겹치거나 닿음("길 교차"), 길이 양 끝 방이 아닌 방의 바닥에 닿음("다른 방 바닥 접촉"), 길이 바닥 위에 깔림,
    /// 포트 입구(가장 바깥 줄과 그 안쪽 1칸)에 장애물("포트 입구 장애물"), 길 칸마다 그 칸을 포함한 폭 x 폭 정사각형이 걸을 수 있는
    /// 칸(길 + 양 끝 방 바닥 - 장애물)으로만 채워지는지("길 폭").
    /// </summary>
    public static List<string> ValidateCorridors(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types)
    {
        List<string> problems = new List<string>();
        Dictionary<Vector2Int, Vector2Int> floorOwner = new Dictionary<Vector2Int, Vector2Int>();
        HashSet<Vector2Int> obstacles = new HashSet<Vector2Int>();
        for (int gy = 0; gy < plan.Rows; gy++)
        {
            for (int gx = 0; gx < plan.Columns; gx++)
            {
                GridRoomType t = types[plan.RoomType[gx, gy]];
                Vector2Int o = plan.RoomOrigin[gx, gy];
                foreach (Vector2Int c in t.FloorCells) floorOwner[c + o] = new Vector2Int(gx, gy);
                foreach (Vector2Int c in t.ObstacleCells) obstacles.Add(c + o);
            }
        }

        Dictionary<Vector2Int, int> corridorOf = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < plan.Corridors.Count; i++)
        {
            foreach (Vector2Int c in plan.Corridors[i].Cells)
            {
                if (floorOwner.ContainsKey(c)) problems.Add($"길이 바닥 위에 깔림: {plan.Corridors[i].Edge} {c}");
                if (corridorOf.TryGetValue(c, out int other)) problems.Add($"길 교차: {plan.Corridors[other].Edge}와 {plan.Corridors[i].Edge}가 {c}에서 겹친다");
                else corridorOf[c] = i;
            }
        }

        Vector2Int[] around = { new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        HashSet<(int, int)> touchingPairs = new HashSet<(int, int)>();
        for (int i = 0; i < plan.Corridors.Count; i++)
        {
            GridCorridor corridor = plan.Corridors[i];
            int w = plan.CorridorWidth;
            bool touchedOtherRoom = false;
            foreach (Vector2Int c in corridor.Cells)
            {
                foreach (Vector2Int d in around)
                {
                    Vector2Int n = c + d;
                    if (!touchedOtherRoom && floorOwner.TryGetValue(n, out Vector2Int owner) && owner != corridor.Edge.A && owner != corridor.Edge.B)
                    {
                        problems.Add($"다른 방 바닥 접촉: {corridor.Edge}의 {c}가 칸 ({owner.x},{owner.y})의 바닥에 닿는다");
                        touchedOtherRoom = true;
                    }
                    if (corridorOf.TryGetValue(n, out int j) && j != i && touchingPairs.Add((Mathf.Min(i, j), Mathf.Max(i, j))))
                        problems.Add($"길 교차: {corridor.Edge}와 {plan.Corridors[j].Edge}가 {c} 근처에서 닿는다");
                }
            }

            foreach (GridPort port in new[] { corridor.PortA, corridor.PortB })
                foreach (Vector2Int c in port.EntryCells)
                    if (!floorOwner.ContainsKey(c) || obstacles.Contains(c))
                        problems.Add($"포트 입구 장애물: {corridor.Edge} 칸 ({port.Cell.x},{port.Cell.y}) {port.Side} 차선 {port.LaneStart}의 {c}");

            HashSet<Vector2Int> walkable = new HashSet<Vector2Int>(corridor.Cells);
            foreach (KeyValuePair<Vector2Int, Vector2Int> kv in floorOwner)
                if ((kv.Value == corridor.Edge.A || kv.Value == corridor.Edge.B) && !obstacles.Contains(kv.Key)) walkable.Add(kv.Key);
            foreach (Vector2Int c in corridor.Cells)
            {
                bool fits = false;
                for (int ox = -(w - 1); ox <= 0 && !fits; ox++)
                    for (int oy = -(w - 1); oy <= 0 && !fits; oy++)
                    {
                        bool all = true;
                        for (int a = 0; a < w && all; a++)
                            for (int b = 0; b < w && all; b++) all = walkable.Contains(new Vector2Int(c.x + ox + a, c.y + oy + b));
                        fits = all;
                    }
                if (!fits) { problems.Add($"길 폭: {corridor.Edge}의 {c}에서 폭 {w}을 확보하지 못한다"); break; }
            }
        }
        return problems;
    }

    /// <summary>연결이 하나뿐인 방(막다른 방) 수.</summary>
    public static int DeadEndRooms(GridLayoutPlan plan)
    {
        Dictionary<Vector2Int, int> degree = new Dictionary<Vector2Int, int>();
        foreach (GridEdge e in plan.Edges)
        {
            degree[e.A] = degree.TryGetValue(e.A, out int a) ? a + 1 : 1;
            degree[e.B] = degree.TryGetValue(e.B, out int b) ? b + 1 : 1;
        }
        return degree.Values.Count(d => d == 1);
    }

    /// <summary>독립된 고리 수(연결 수 - (방 수 - 1)). 연결된 그래프에서 트리 밖 연결 하나가 고리 하나를 만든다.</summary>
    public static int LoopCount(GridLayoutPlan plan) => plan.Edges.Count - (plan.Columns * plan.Rows - 1);

    // 한 연결의 길. 가로 연결: A 동쪽 포트 → 동쪽으로 칸 사이 틈의 차선까지 → 위/아래로 B 포트 높이까지 → 동쪽으로 B 서쪽 포트.
    // 세로 연결은 축을 바꿔 같다. 차선 줄마다 가장 바깥 바닥 칸 바로 다음 칸부터 깔므로 들쭉날쭉한 가장자리도 틈 없이 이어진다.
    private static GridCorridor BuildCorridor(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types, GridEdge e)
    {
        bool horizontal = e.Horizontal;
        GridPort pa = PickPort(plan, types, e.A, horizontal ? GridSide.East : GridSide.North);
        GridPort pb = PickPort(plan, types, e.B, horizontal ? GridSide.West : GridSide.South);
        GridRoomType ta = types[plan.RoomType[e.A.x, e.A.y]], tb = types[plan.RoomType[e.B.x, e.B.y]];
        Vector2Int oa = plan.RoomOrigin[e.A.x, e.A.y], ob = plan.RoomOrigin[e.B.x, e.B.y];
        int w = plan.CorridorWidth;
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();

        // 축 이름: along = 길이 나아가는 축(가로 연결이면 x), across = 차선 줄 축(가로 연결이면 y).
        Vector2Int Cell(int along, int across) => horizontal ? new Vector2Int(along, across) : new Vector2Int(across, along);
        int cellSize = horizontal ? plan.CellWidth : plan.CellHeight;
        int aIndex = horizontal ? e.A.x : e.A.y;
        int gapStart = aIndex * (cellSize + plan.CorridorGap) + cellSize;
        int band0 = gapStart + (plan.CorridorGap - w) / 2, band1 = band0 + w - 1;
        int offAlongA = horizontal ? oa.x : oa.y, offAcrossA = horizontal ? oa.y : oa.x;
        int offAlongB = horizontal ? ob.x : ob.y, offAcrossB = horizontal ? ob.y : ob.x;
        Dictionary<int, int> outerA = ta.OuterDepth[(int)pa.Side], outerB = tb.OuterDepth[(int)pb.Side];

        for (int i = 0; i < w; i++)
        {
            int ka = pa.LaneStart + i;
            for (int along = outerA[ka] + offAlongA + 1; along <= band1; along++) cells.Add(Cell(along, ka + offAcrossA));
            int kb = pb.LaneStart + i;
            for (int along = band0; along <= outerB[kb] + offAlongB - 1; along++) cells.Add(Cell(along, kb + offAcrossB));
        }
        int lo = Mathf.Min(pa.LaneStart + offAcrossA, pb.LaneStart + offAcrossB);
        int hi = Mathf.Max(pa.LaneStart + offAcrossA, pb.LaneStart + offAcrossB) + w - 1;
        for (int along = band0; along <= band1; along++)
            for (int across = lo; across <= hi; across++) cells.Add(Cell(along, across));

        return new GridCorridor
        {
            Edge = e,
            PortA = pa,
            PortB = pb,
            Cells = cells.OrderBy(c => c.y).ThenBy(c => c.x).ToList(),
        };
    }

    // 쓸 수 있는 차선 중 가장 바깥 바닥선이 고른 차선(줄마다 깊이 차이가 작은 것)을 먼저, 그다음 변 가운데에 가까운 차선을 고른다.
    // 고른 바닥선이면 길이 방 가장자리를 따라 옆으로 새는 가는 띠 없이 반듯하게 붙는다.
    private static GridPort PickPort(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types, Vector2Int cell, GridSide side)
    {
        GridRoomType t = types[plan.RoomType[cell.x, cell.y]];
        Vector2Int o = plan.RoomOrigin[cell.x, cell.y];
        int w = plan.CorridorWidth;
        bool vertical = side == GridSide.West || side == GridSide.East;
        Dictionary<int, int> outer = t.OuterDepth[(int)side];
        float center = vertical ? t.MinY + (t.Height - 1) * 0.5f : t.MinX + (t.Width - 1) * 0.5f;

        int Spread(int start)
        {
            int min = int.MaxValue, max = int.MinValue;
            for (int k = start; k < start + w; k++) { min = Mathf.Min(min, outer[k]); max = Mathf.Max(max, outer[k]); }
            return max - min;
        }

        int lane = t.LaneStarts[(int)side].OrderBy(Spread).ThenBy(s => Mathf.Abs(s + (w - 1) * 0.5f - center)).ThenBy(s => s).First();
        Vector2Int inward = side == GridSide.East ? Vector2Int.left : side == GridSide.West ? Vector2Int.right : side == GridSide.North ? Vector2Int.down : Vector2Int.up;
        GridPort port = new GridPort { Cell = cell, Side = side, LaneStart = lane };
        for (int k = lane; k < lane + w; k++)
        {
            Vector2Int edge = (vertical ? new Vector2Int(outer[k], k) : new Vector2Int(k, outer[k])) + o;
            port.EdgeCells.Add(edge);
            port.EntryCells.Add(edge);
            port.EntryCells.Add(edge + inward);
        }
        return port;
    }

    private static GridLayoutPlan Fail(GridLayoutPlan plan, string reason)
    {
        plan.Success = false;
        plan.Failure = reason;
        return plan;
    }

    private static Vector2Int PickTruckCell(int columns, int rows, TruckPlacement placement, System.Random rng)
    {
        // 짝수 크기는 가운데가 두 칸이므로 그중 하나를 시드로 고른다.
        int Middle(int size) => size % 2 == 1 ? size / 2 : size / 2 - 1 + rng.Next(2);
        switch (placement)
        {
            case TruckPlacement.Corner:
                return new Vector2Int(0, 0);
            case TruckPlacement.EdgeCenter:
                return new Vector2Int(Middle(columns), 0);
            default:
                return new Vector2Int(Middle(columns), Middle(rows));
        }
    }

    // 종류별 개수를 먼저 정하고(차이 최대 1), 트럭 칸부터 행 우선으로 채운다. 칸마다 남은 개수가 많은 종류부터(같으면 무작위)
    // 상하좌우 이웃과 다른 종류를 시도하고, 막히면 되돌아간다. 실패하면 null.
    private static int[,] AssignRooms(int columns, int rows, Vector2Int truckCell, IReadOnlyList<GridRoomType> types, System.Random rng)
    {
        int cellCount = columns * rows, k = types.Count;
        int[] remaining = new int[k];
        List<int> order = Enumerable.Range(0, k).ToList();
        Shuffle(order, rng);
        for (int i = 0; i < k; i++) remaining[order[i]] = cellCount / k + (i < cellCount % k ? 1 : 0);
        // 칸 수가 종류 수보다 적어 트럭용 종류가 하나도 안 담겼으면, 담긴 종류 하나를 트럭용 종류로 바꾼다.
        if (!Enumerable.Range(0, k).Any(t => types[t].TruckAllowed && remaining[t] > 0))
        {
            int give = Enumerable.Range(0, k).First(t => remaining[t] > 0);
            int take = order.First(t => types[t].TruckAllowed);
            remaining[give]--; remaining[take]++;
        }

        List<Vector2Int> cells = new List<Vector2Int> { truckCell };
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                if (new Vector2Int(x, y) != truckCell) cells.Add(new Vector2Int(x, y));

        int[,] grid = new int[columns, rows];
        for (int x = 0; x < columns; x++) for (int y = 0; y < rows; y++) grid[x, y] = -1;

        int steps = 0;
        bool Fill(int i)
        {
            if (i == cells.Count) return true;
            if (++steps > MaxBacktrackSteps) return false;
            Vector2Int c = cells[i];
            List<int> options = Enumerable.Range(0, k).Where(t => remaining[t] > 0 && (c != truckCell || types[t].TruckAllowed)).ToList();
            Shuffle(options, rng);
            options = options.OrderByDescending(t => remaining[t]).ToList(); // 안정 정렬이라 같은 개수끼리는 섞인 순서가 유지된다
            foreach (int t in options)
            {
                if (SameNeighbour(grid, columns, rows, c, t)) continue;
                grid[c.x, c.y] = t;
                remaining[t]--;
                if (Fill(i + 1)) return true;
                remaining[t]++;
                grid[c.x, c.y] = -1;
            }
            return false;
        }

        return Fill(0) ? grid : null;
    }

    private static bool SameNeighbour(int[,] grid, int columns, int rows, Vector2Int c, int t)
    {
        return (c.x > 0 && grid[c.x - 1, c.y] == t) || (c.x + 1 < columns && grid[c.x + 1, c.y] == t) ||
               (c.y > 0 && grid[c.x, c.y - 1] == t) || (c.y + 1 < rows && grid[c.x, c.y + 1] == t);
    }

    private static List<GridEdge> CandidateEdges(int columns, int rows, int[,] assignment, IReadOnlyList<GridRoomType> types)
    {
        List<GridEdge> edges = new List<GridEdge>();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                GridRoomType here = types[assignment[x, y]];
                if (x + 1 < columns && here.IsOpen(GridSide.East) && types[assignment[x + 1, y]].IsOpen(GridSide.West))
                    edges.Add(new GridEdge(new Vector2Int(x, y), new Vector2Int(x + 1, y), false));
                if (y + 1 < rows && here.IsOpen(GridSide.North) && types[assignment[x, y + 1]].IsOpen(GridSide.South))
                    edges.Add(new GridEdge(new Vector2Int(x, y), new Vector2Int(x, y + 1), false));
            }
        }
        return edges;
    }

    // 방은 칸(CellWidth x CellHeight) 가운데에 놓고, 칸 사이에는 CorridorGap칸 틈을 둔다.
    private static Vector2Int[,] RoomOrigins(GridLayoutPlan plan, IReadOnlyList<GridRoomType> types)
    {
        Vector2Int[,] origins = new Vector2Int[plan.Columns, plan.Rows];
        for (int y = 0; y < plan.Rows; y++)
        {
            for (int x = 0; x < plan.Columns; x++)
            {
                GridRoomType t = types[plan.RoomType[x, y]];
                origins[x, y] = new Vector2Int(
                    x * (plan.CellWidth + plan.CorridorGap) + (plan.CellWidth - t.Width) / 2 - t.MinX,
                    y * (plan.CellHeight + plan.CorridorGap) + (plan.CellHeight - t.Height) / 2 - t.MinY);
            }
        }
        return origins;
    }

    private static int Index(Vector2Int c, int columns) => c.y * columns + c.x;

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
        return i;
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// 방 프리팹(에셋 그대로, Instantiate 없이)에서 GridRoomType을 만든다. 바닥은 Ground 타일맵의 칸, 장애물은 VillageMapGenerator.GetRoomObstacles와
/// 같은 기준(트리거가 아닌 2D 충돌체, 타일맵/복합 충돌체 제외, 경계에 딱 닿기만 한 칸은 빼는 0.01 여유)으로 계산한다.
/// 변마다 열린 차선: 그 변에서 가장 바깥 바닥 칸이 corridorWidth줄 연속으로 있고, 각 줄의 바깥 칸과 그 안쪽 1칸이 장애물이 아니며
/// 방 안의 가장 큰 걸을 수 있는 덩어리에 속하는 자리의 수.
/// </summary>
public static class GridRoomAnalyzer
{
    private const float EdgeEpsilon = 0.01f;
    private const int EntryDepth = 2;

    public static GridRoomType Analyze(GameObject roomPrefab, int corridorWidth, bool truckAllowed)
    {
        GridRoomType type = new GridRoomType { Name = roomPrefab.name, TruckAllowed = truckAllowed };

        Transform groundTransform = roomPrefab.transform.Find("Ground");
        UnityEngine.Tilemaps.Tilemap ground = groundTransform != null ? groundTransform.GetComponent<UnityEngine.Tilemaps.Tilemap>() : null;
        if (ground != null)
        {
            foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                if (ground.HasTile(p)) type.FloorCells.Add(new Vector2Int(p.x, p.y));
        }
        if (type.FloorCells.Count == 0) return type;

        Vector3 origin = roomPrefab.transform.position;
        foreach (Collider2D col in roomPrefab.GetComponentsInChildren<Collider2D>())
        {
            if (!col.enabled || col.isTrigger || col is UnityEngine.Tilemaps.TilemapCollider2D || col is CompositeCollider2D) continue;
            Vector2 min, max;
            if (col is BoxCollider2D box)
            {
                Vector3 center = col.transform.TransformPoint(box.offset) - origin;
                Vector3 scale = col.transform.lossyScale;
                Vector2 half = new Vector2(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y)) * 0.5f;
                min = (Vector2)center - half;
                max = (Vector2)center + half;
            }
            else
            {
                Bounds b = col.bounds;
                min = b.min - origin;
                max = b.max - origin;
            }
            for (int y = Mathf.FloorToInt(min.y + EdgeEpsilon); y <= Mathf.FloorToInt(max.y - EdgeEpsilon); y++)
                for (int x = Mathf.FloorToInt(min.x + EdgeEpsilon); x <= Mathf.FloorToInt(max.x - EdgeEpsilon); x++)
                    type.ObstacleCells.Add(new Vector2Int(x, y));
        }

        type.MinX = type.FloorCells.Min(c => c.x);
        type.MinY = type.FloorCells.Min(c => c.y);
        type.Width = type.FloorCells.Max(c => c.x) - type.MinX + 1;
        type.Height = type.FloorCells.Max(c => c.y) - type.MinY + 1;

        HashSet<Vector2Int> floor = new HashSet<Vector2Int>(type.FloorCells);
        HashSet<Vector2Int> mainArea = LargestWalkableArea(floor, type.ObstacleCells);
        int width = Mathf.Max(1, corridorWidth);
        for (int side = 0; side < 4; side++)
        {
            type.OuterDepth[side] = OuterDepths(floor, (GridSide)side);
            type.LaneStarts[side] = OpenLaneStarts(floor, type.ObstacleCells, mainArea, (GridSide)side, type.OuterDepth[side], width);
            type.OpenLanes[side] = type.LaneStarts[side].Count;
            if (type.OpenLanes[side] == 0)
                type.Warnings.Add($"{type.Name}의 {(GridSide)side} 변에는 폭 {width} 차선(입구 안쪽 {EntryDepth}칸까지 장애물 없음)이 없어 포트 없음으로 처리한다 - 이 변으로는 이웃 방과 잇지 않는다.");
        }
        return type;
    }

    // 변의 줄마다 가장 바깥 바닥 칸의 깊이. 동/서 변은 줄 y → x, 남/북 변은 줄 x → y.
    private static Dictionary<int, int> OuterDepths(HashSet<Vector2Int> floor, GridSide side)
    {
        bool vertical = side == GridSide.West || side == GridSide.East;
        bool outward = side == GridSide.East || side == GridSide.North; // 바깥 = 좌표가 큰 쪽
        Dictionary<int, int> outer = new Dictionary<int, int>();
        foreach (Vector2Int c in floor)
        {
            int key = vertical ? c.y : c.x, depth = vertical ? c.x : c.y;
            if (!outer.TryGetValue(key, out int d) || (outward ? depth > d : depth < d)) outer[key] = depth;
        }
        return outer;
    }

    private static HashSet<Vector2Int> LargestWalkableArea(HashSet<Vector2Int> floor, HashSet<Vector2Int> obstacles)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>(), best = new HashSet<Vector2Int>();
        foreach (Vector2Int start in floor.OrderBy(c => c.y).ThenBy(c => c.x))
        {
            if (obstacles.Contains(start) || visited.Contains(start)) continue;
            HashSet<Vector2Int> area = new HashSet<Vector2Int> { start };
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            stack.Push(start);
            visited.Add(start);
            while (stack.Count > 0)
            {
                Vector2Int c = stack.Pop();
                foreach (Vector2Int d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    Vector2Int n = c + d;
                    if (floor.Contains(n) && !obstacles.Contains(n) && visited.Add(n)) { area.Add(n); stack.Push(n); }
                }
            }
            if (area.Count > best.Count) best = area;
        }
        return best;
    }

    private static List<int> OpenLaneStarts(HashSet<Vector2Int> floor, HashSet<Vector2Int> obstacles, HashSet<Vector2Int> mainArea, GridSide side,
        Dictionary<int, int> outer, int width)
    {
        bool vertical = side == GridSide.West || side == GridSide.East;
        Vector2Int inward = side == GridSide.East ? Vector2Int.left : side == GridSide.West ? Vector2Int.right : side == GridSide.North ? Vector2Int.down : Vector2Int.up;

        List<int> open = new List<int>();
        foreach (int start in outer.Keys.OrderBy(k => k))
        {
            bool ok = true;
            for (int k = start; k < start + width && ok; k++)
            {
                if (!outer.TryGetValue(k, out int depth)) { ok = false; break; }
                Vector2Int entry = vertical ? new Vector2Int(depth, k) : new Vector2Int(k, depth);
                for (int step = 0; step < EntryDepth && ok; step++)
                {
                    Vector2Int c = entry + inward * step;
                    ok = floor.Contains(c) && !obstacles.Contains(c) && mainArea.Contains(c);
                }
            }
            if (ok) open.Add(start);
        }
        return open;
    }
}
