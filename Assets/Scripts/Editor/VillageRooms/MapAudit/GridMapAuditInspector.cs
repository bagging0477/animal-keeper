using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>점검에서 찾은 위반 하나. Pos가 있으면 위반 캡처에 빨간 표시를 그린다(월드 좌표, 2D).</summary>
public sealed class AuditViolation
{
    public string Category;
    public string Message;
    public Vector2? Pos;

    public AuditViolation(string category, string message, Vector2? pos = null) { Category = category; Message = message; Pos = pos; }
}

/// <summary>스폰 직후(씬 로드 콜백 시점 = 모든 Awake 뒤, Start 전) 동물/몬스터/플레이어/트럭의 위치와 모양.</summary>
public sealed class AuditEntity
{
    public string Kind;     // 동물 / 몬스터 / 플레이어 / 트럭
    public string Path;
    public string Prefab;
    public Vector2 Pos;     // 2D 게임 좌표(몬스터는 x, z)
    public Vector2 ShapeCenter;
    public float Radius;    // 0보다 크면 원(동물 CircleCollider2D, 몬스터는 NavMeshAgent 반경 - 2D 충돌체 없음), 아니면 Box
    public Rect Box;
    public int Weight;
    public string Blocker;      // 스폰 순간 가장 깊이 겹친 막는 충돌체(장애물/경계/트럭)
    public float BlockerDepth;  // 그 겹침 깊이(칸, 양수)
    public bool BlockerIsBoundary;
}

public sealed class AuditSnapshot
{
    public readonly List<AuditEntity> Entities = new List<AuditEntity>();

    // 실제 충돌체 모양끼리 얼마나 파고들었는지(Physics2D.Distance). 자기 자신·움직이는 몸체는 뺀다.
    private static void MeasureBlockers(AuditEntity e, Collider2D col, IEnumerable<Collider2D> candidates)
    {
        foreach (Collider2D hit in candidates)
        {
            if (hit == null || hit == col || !GridMapAuditInspector.IsBlocker(hit)) continue;
            ColliderDistance2D d = Physics2D.Distance(col, hit);
            if (!d.isValid || d.distance > -0.01f) continue;
            if (-d.distance > e.BlockerDepth)
            {
                e.BlockerDepth = -d.distance;
                e.Blocker = GridMapAuditInspector.HierarchyPath(hit.transform);
                e.BlockerIsBoundary = hit.transform.name == "MapBoundary";
            }
        }
    }

    /// <summary>sceneLoaded 콜백 안에서 부른다. AI가 움직이기 전의 스폰 위치다.</summary>
    public static AuditSnapshot Take()
    {
        Physics2D.SyncTransforms();
        AuditSnapshot s = new AuditSnapshot();
        GameObject root = GameObject.Find("GeneratedMap");
        if (root != null)
        {
            foreach (AnimalRescue a in root.GetComponentsInChildren<AnimalRescue>(true))
            {
                Collider2D col = a.GetComponent<Collider2D>();
                Vector2 p = a.transform.position;
                AuditEntity e = new AuditEntity
                {
                    Kind = "동물", Path = GridMapAuditInspector.HierarchyPath(a.transform), Prefab = a.SourcePrefab != null ? a.SourcePrefab.name : a.name,
                    Pos = p, ShapeCenter = p, Box = new Rect(p - Vector2.one * 0.4f, Vector2.one * 0.8f), Weight = a.Weight,
                };
                if (col is CircleCollider2D circle)
                {
                    Vector3 sc = circle.transform.lossyScale;
                    e.Radius = circle.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y));
                    e.ShapeCenter = circle.bounds.center;
                    MeasureBlockers(e, col, Physics2D.OverlapCircleAll(e.ShapeCenter, e.Radius));
                }
                else if (col != null)
                {
                    e.Box = GridMapAuditInspector.RectOf(col.bounds);
                    MeasureBlockers(e, col, Physics2D.OverlapBoxAll(e.Box.center, e.Box.size, 0f));
                }
                s.Entities.Add(e);
            }
            foreach (MonsterHealth m in root.GetComponentsInChildren<MonsterHealth>(true))
            {
                Vector2 p = new Vector2(m.transform.position.x, m.transform.position.z);
                UnityEngine.AI.NavMeshAgent agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
                s.Entities.Add(new AuditEntity { Kind = "몬스터", Path = GridMapAuditInspector.HierarchyPath(m.transform), Prefab = m.name, Pos = p, ShapeCenter = p,
                    Radius = agent != null ? agent.radius : 0.4f, Box = new Rect(p - Vector2.one * 0.4f, Vector2.one * 0.8f) });
            }
        }
        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            Collider2D col = player.GetComponent<Collider2D>();
            AuditEntity e = new AuditEntity { Kind = "플레이어", Path = "Player", Pos = player.transform.position, ShapeCenter = player.transform.position,
                Box = col != null ? GridMapAuditInspector.RectOf(col.bounds) : new Rect((Vector2)player.transform.position - Vector2.one * 0.5f, Vector2.one) };
            if (col != null) MeasureBlockers(e, col, Physics2D.OverlapBoxAll(e.Box.center, e.Box.size, 0f));
            s.Entities.Add(e);
        }
        GameObject truck = GameObject.Find("TruckPoint");
        if (truck != null)
        {
            Collider2D col = truck.GetComponent<Collider2D>();
            s.Entities.Add(new AuditEntity { Kind = "트럭", Path = "TruckPoint", Pos = truck.transform.position, ShapeCenter = truck.transform.position,
                Box = col != null ? GridMapAuditInspector.RectOf(col.bounds) : new Rect((Vector2)truck.transform.position, Vector2.zero) });
        }
        return s;
    }

    /// <summary>두 모양이 겹친 깊이(칸, 양수면 겹침). 원-원, 원-상자, 상자-상자.</summary>
    public static float OverlapDepth(AuditEntity a, AuditEntity b)
    {
        if (a.Radius > 0f && b.Radius > 0f) return a.Radius + b.Radius - Vector2.Distance(a.ShapeCenter, b.ShapeCenter);
        if (a.Radius > 0f || b.Radius > 0f)
        {
            AuditEntity c = a.Radius > 0f ? a : b, r = a.Radius > 0f ? b : a;
            Vector2 closest = new Vector2(Mathf.Clamp(c.ShapeCenter.x, r.Box.xMin, r.Box.xMax), Mathf.Clamp(c.ShapeCenter.y, r.Box.yMin, r.Box.yMax));
            return c.Radius - Vector2.Distance(c.ShapeCenter, closest);
        }
        float ox = Mathf.Min(a.Box.xMax, b.Box.xMax) - Mathf.Max(a.Box.xMin, b.Box.xMin), oy = Mathf.Min(a.Box.yMax, b.Box.yMax) - Mathf.Max(a.Box.yMin, b.Box.yMin);
        return Mathf.Min(ox, oy);
    }
}

public sealed class AuditSeedResult
{
    public int Columns, Rows, Seed;
    public readonly List<AuditViolation> Violations = new List<AuditViolation>();
    public readonly List<string> Logs = new List<string>();
    public readonly List<string> SignatureLines = new List<string>();
    public readonly Dictionary<string, double> Stats = new Dictionary<string, double>();
    public readonly List<string> Notes = new List<string>();
    public Rect FloorArea;
    public Vector2 TruckPos, PlayerPos;
    public string TruckRoom;

    public void Add(string category, string message, Vector2? pos = null) => Violations.Add(new AuditViolation(category, message, pos));
    public void Stat(string key, double value) => Stats[key] = Stats.TryGetValue(key, out double v) ? v + value : value;
}

/// <summary>
/// 격자 맵 한 장을 점검한다(읽기만 한다). 생성기 내부 상태는 공개 속성(LastGridPlan/LastGridValidation)과 리플렉션으로 읽고,
/// 나머지는 씬에 만들어진 결과물(타일맵, 충돌체, NavMesh, 스폰된 오브젝트)을 직접 조사한다.
/// </summary>
public static class GridMapAuditInspector
{
    // 카테고리 이름(보고서의 표 순서).
    public const string CFloor = "1 바닥 연결", CNav = "2 NavMesh 경로", CBoundary = "3 경계 틈", CGhostNav = "4a 바닥 밖 NavMesh",
        CObstacleNav = "4b 장애물 칸 NavMesh", CGhostCollider = "4c 그림 없는 충돌체", CStripped = "4d 충돌체만 지워진 장애물", CCorridor = "5 길 폭·포트",
        CSpawnPlace = "6a 스폰 위치", CSpawnOverlap = "6b 스폰 겹침", CTruckClear = "7a 트럭 여유 영역 장애물", CPlayerTruck = "7b 플레이어-트럭 겹침",
        CIds = "8 Id·이름 중복", CTotals = "9 스폰 총량·상한", CDeterminism = "10 같은 시드 재생성", CGenerator = "생성기 자체 검증", CLogs = "콘솔 경고·에러";

    public static readonly string[] Categories = { CFloor, CNav, CBoundary, CGhostNav, CObstacleNav, CGhostCollider, CStripped, CCorridor, CSpawnPlace, CSpawnOverlap,
        CTruckClear, CPlayerTruck, CIds, CTotals, CDeterminism, CGenerator, CLogs };

    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Vector2Int[] Four = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    private static readonly Vector2Int[] Eight = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };

    public sealed class RoomData
    {
        public Transform T;
        public string Type;
        public Vector2Int Grid;
        public HashSet<Vector2Int> Floor = new HashSet<Vector2Int>();
        public GameObject Prefab;
    }

    public static Rect RectOf(Bounds b) => new Rect(b.min.x, b.min.y, b.size.x, b.size.y);

    public static string HierarchyPath(Transform t)
    {
        System.Text.StringBuilder path = new System.Text.StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) path.Insert(0, "/").Insert(0, p.name);
        return path.ToString();
    }

    public static T ReadField<T>(object target, string field)
    {
        object v = target?.GetType().GetField(field, Instance)?.GetValue(target);
        return v is T t ? t : default;
    }

    public static Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    private static Vector2 Center(Vector2Int c) => new Vector2(c.x + 0.5f, c.y + 0.5f);

    // 플레이어를 막는 충돌체: 켜져 있고 트리거가 아니며, 움직이는 몸체(플레이어/동물)가 아닌 것.
    public static bool IsBlocker(Collider2D c)
    {
        if (c == null || !c.enabled || c.isTrigger) return false;
        if (c.GetComponentInParent<AnimalRescue>() != null || c.GetComponentInParent<PlayerMovement>() != null) return false;
        if (c.attachedRigidbody != null && c.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic) return false;
        return true;
    }

    // 방 장애물: 맵 루트 아래(경계 충돌체 제외)의 막는 충돌체(타일맵/복합 충돌체 제외 - 생성기 GetRoomObstacles와 같은 기준).
    public static bool IsObstacle(Collider2D c, Transform mapRoot, Transform boundary)
    {
        if (!IsBlocker(c) || c is TilemapCollider2D || c is CompositeCollider2D) return false;
        if (boundary != null && c.transform == boundary) return false;
        return mapRoot != null && c.transform.IsChildOf(mapRoot);
    }

    public static List<RoomData> ReadRooms(GameObject mapRoot, GridLayoutPlan plan, VillageMapGenerator gen)
    {
        GameObject[] prefabs = ReadField<GameObject[]>(gen, "roomPrefabs") ?? new GameObject[0];
        List<RoomData> rooms = new List<RoomData>();
        foreach (Transform child in mapRoot.transform)
        {
            if (!child.name.StartsWith("Room_")) continue;
            string[] parts = child.name.Split('_');
            Tilemap ground = child.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            RoomData r = new RoomData { T = child };
            if (parts.Length == 3 && int.TryParse(parts[1], out int gx) && int.TryParse(parts[2], out int gy))
            {
                r.Grid = new Vector2Int(gx, gy);
                if (plan != null && plan.Success && gx < plan.Columns && gy < plan.Rows) r.Type = plan.TypeNames[plan.RoomType[gx, gy]];
            }
            r.Type ??= child.name;
            r.Prefab = prefabs.FirstOrDefault(p => p != null && p.name == r.Type);
            Vector2Int offset = new Vector2Int(Mathf.RoundToInt(child.position.x), Mathf.RoundToInt(child.position.y));
            foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                if (ground.HasTile(p)) r.Floor.Add(new Vector2Int(p.x, p.y) + offset);
            rooms.Add(r);
        }
        rooms.Sort((a, b) => string.CompareOrdinal(a.T.name, b.T.name));
        return rooms;
    }

    public static HashSet<Vector2Int> CorridorTiles(GameObject mapRoot)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        Tilemap tm = mapRoot.transform.Find("Corridors")?.GetComponentInChildren<Tilemap>();
        if (tm != null)
            foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
                if (tm.HasTile(p)) cells.Add(new Vector2Int(p.x, p.y));
        return cells;
    }

    private static IEnumerable<Vector2Int> CellsOf(Bounds b)
    {
        for (int y = Mathf.FloorToInt(b.min.y + 0.01f); y <= Mathf.FloorToInt(b.max.y - 0.01f); y++)
            for (int x = Mathf.FloorToInt(b.min.x + 0.01f); x <= Mathf.FloorToInt(b.max.x - 0.01f); x++)
                yield return new Vector2Int(x, y);
    }

    public static bool IsConnected(HashSet<Vector2Int> cells, out int components, out int largest)
    {
        components = 0; largest = 0;
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        foreach (Vector2Int start in cells)
        {
            if (!seen.Add(start)) continue;
            components++;
            int size = 1;
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Vector2Int c = stack.Pop();
                foreach (Vector2Int d in Four)
                {
                    Vector2Int n = c + d;
                    if (cells.Contains(n) && seen.Add(n)) { stack.Push(n); size++; }
                }
            }
            largest = Mathf.Max(largest, size);
        }
        return components <= 1;
    }

    private static Vector3? NavPointNear(Vector2 xy, float radius)
    {
        return NavMesh.SamplePosition(new Vector3(xy.x, 0f, xy.y), out NavMeshHit hit, radius, NavMesh.AllAreas) ? hit.position : (Vector3?)null;
    }

    // 방 바닥 무게중심에 가장 가까운, 장애물 없고 상하좌우가 바닥인 칸 중 NavMesh 위의 점(생성기 RoomNavCenter와 같은 기준).
    private static Vector3? RoomNavCenter(RoomData room, HashSet<Vector2Int> obstacleCells)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2Int c in room.Floor) centroid += Center(c);
        centroid /= Mathf.Max(1, room.Floor.Count);
        foreach (Vector2Int c in room.Floor.Where(c => !obstacleCells.Contains(c) && Four.All(d => room.Floor.Contains(c + d)))
                     .OrderBy(c => (Center(c) - centroid).sqrMagnitude))
        {
            Vector3? p = NavPointNear(Center(c), 0.75f);
            if (p.HasValue) return p;
        }
        return null;
    }

    public static string MeshHash(Mesh mesh)
    {
        if (mesh == null) return "-";
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) { h = (h ^ v) * 1099511628211L; }
            foreach (Vector3 v in mesh.vertices) { Mix(Mathf.RoundToInt(v.x * 1000)); Mix(Mathf.RoundToInt(v.y * 1000)); Mix(Mathf.RoundToInt(v.z * 1000)); }
            foreach (Color c in mesh.colors) Mix(Mathf.RoundToInt(c.a * 1000) * 7 + Mathf.RoundToInt(c.r * 1000));
            foreach (int i in mesh.triangles) Mix(i);
            return h.ToString("x16");
        }
    }

    public static string NavMeshHash(out int vertexCount)
    {
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        vertexCount = tri.vertices.Length;
        unchecked
        {
            long h = 1469598103934665603L;
            // 정점 순서는 내부 타일 순서라 같은 결과면 같다고 보지만, 혹시 몰라 정렬한 좌표로 섞는다.
            foreach (Vector3 v in tri.vertices.OrderBy(v => Mathf.RoundToInt(v.x * 100)).ThenBy(v => Mathf.RoundToInt(v.z * 100)))
            {
                h = (h ^ Mathf.RoundToInt(v.x * 100)) * 1099511628211L;
                h = (h ^ Mathf.RoundToInt(v.z * 100)) * 1099511628211L;
            }
            return $"{vertexCount}v/{tri.indices.Length / 3}t/{h:x16}";
        }
    }

    /// <summary>격자 맵 전체 점검. snapshot은 같은 로드의 sceneLoaded 시점 스냅샷.</summary>
    public static AuditSeedResult Inspect(AuditSnapshot snapshot, int columns, int rows, int seed, IEnumerable<string> logs)
    {
        AuditSeedResult r = new AuditSeedResult { Columns = columns, Rows = rows, Seed = seed };
        r.Logs.AddRange(logs);
        foreach (string l in r.Logs) r.Add(CLogs, l);

        GameObject mapRoot = GameObject.Find("GeneratedMap");
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        if (mapRoot == null || gen == null) { r.Add(CGenerator, "GeneratedMap 또는 VillageMapGenerator가 없다"); return r; }
        GridLayoutPlan plan = gen.LastGridPlan;
        if (plan == null || !plan.Success || mapRoot.transform.Find("Corridors") == null) { r.Add(CGenerator, "격자 맵이 아니다(계획 실패로 한 줄 배치가 됐거나 layoutMode가 Linear)"); return r; }

        Physics2D.SyncTransforms();
        Transform boundary = mapRoot.transform.Find("MapBoundary");
        List<RoomData> rooms = ReadRooms(mapRoot, plan, gen);
        HashSet<Vector2Int> corridorTiles = CorridorTiles(mapRoot);
        HashSet<Vector2Int> mapFloor = new HashSet<Vector2Int>(rooms.SelectMany(x => x.Floor));
        mapFloor.UnionWith(corridorTiles);
        int minX = mapFloor.Min(c => c.x), maxX = mapFloor.Max(c => c.x), minY = mapFloor.Min(c => c.y), maxY = mapFloor.Max(c => c.y);
        r.FloorArea = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        r.Stat("바닥 칸", mapFloor.Count);
        r.Stat("길 칸", corridorTiles.Count);

        List<Collider2D> obstacles = mapRoot.GetComponentsInChildren<Collider2D>().Where(c => IsObstacle(c, mapRoot.transform, boundary)).ToList();
        HashSet<Vector2Int> obstacleCells = new HashSet<Vector2Int>(obstacles.SelectMany(c => CellsOf(c.bounds)));
        r.Stat("장애물 충돌체", obstacles.Count);

        // 생성기 자체 검증(Start에서 카빙 반영 후).
        VillageMapGenerator.GridValidationResult v = gen.LastGridValidation;
        if (v == null) r.Add(CGenerator, "생성기 검증 결과가 없다(Start가 아직 돌지 않았다)");
        else foreach (string p in v.Problems) r.Add(CGenerator, p);
        r.TruckRoom = v?.TruckRoomPrefab;

        // ---- 1. 바닥 연결
        if (!IsConnected(mapFloor, out int comps, out int largest)) r.Add(CFloor, $"바닥(방 + 길)이 {comps}덩어리 (가장 큰 덩어리 {largest}/{mapFloor.Count}칸)");
        // 물리 기준 걸을 수 있는 칸: 0.9 상자가 어떤 장애물에도 닿지 않는 바닥 칸.
        HashSet<Vector2Int> walkPhys = new HashSet<Vector2Int>(mapFloor.Where(c => !Physics2D.OverlapBoxAll(Center(c), new Vector2(0.9f, 0.9f), 0f).Any(col => IsObstacle(col, mapRoot.transform, boundary))));
        if (!IsConnected(walkPhys, out comps, out largest))
        {
            // 작은 덩어리(장애물 사이 1~2칸 틈)는 따로 센다 - 플레이어가 못 들어가는 빈칸일 뿐 길이 끊긴 것은 아니다.
            int stranded = walkPhys.Count - largest;
            r.Stat("장애물에 갇힌 바닥 칸", stranded);
            if (stranded > 8) r.Add(CFloor, $"장애물을 뺀 걸을 수 있는 바닥이 {comps}덩어리 (가장 큰 덩어리 밖 {stranded}칸)");
        }

        // 길 타일과 계획의 길 칸이 같은지.
        HashSet<Vector2Int> planCorridor = new HashSet<Vector2Int>(plan.Corridors.SelectMany(c => c.Cells));
        HashSet<Vector2Int> roomFloor = new HashSet<Vector2Int>(rooms.SelectMany(x => x.Floor));
        planCorridor.ExceptWith(roomFloor);
        if (!planCorridor.SetEquals(corridorTiles)) r.Add(CCorridor, $"Corridors 타일맵({corridorTiles.Count}칸)과 계획의 길 칸({planCorridor.Count}칸)이 다르다");

        // ---- 2. NavMesh 경로: 트럭 → 모든 방 중심, 트럭 → 모든 길 가운데
        Entity(snapshot, "트럭", out AuditEntity truckE);
        Entity(snapshot, "플레이어", out AuditEntity playerE);
        GameObject truckGo = GameObject.Find("TruckPoint");
        GameObject playerGo = GameObject.Find("Player");
        Vector2 truckXY = truckGo != null ? (Vector2)truckGo.transform.position : Vector2.zero;
        r.TruckPos = truckXY;
        r.PlayerPos = playerGo != null ? (Vector2)playerGo.transform.position : Vector2.zero;
        Vector3? start = NavPointNear(truckXY, 8f);
        if (!start.HasValue) r.Add(CNav, "트럭 주변 8칸 안에 NavMesh가 없다", truckXY);
        else
        {
            foreach (RoomData room in rooms)
            {
                Vector3? target = RoomNavCenter(room, obstacleCells);
                NavMeshPath path = new NavMeshPath();
                if (!target.HasValue) { r.Add(CNav, $"{room.T.name}({room.Type}) 안에 NavMesh 지점이 없다", room.Floor.Count > 0 ? Center(room.Floor.First()) : (Vector2?)null); continue; }
                if (!NavMesh.CalculatePath(start.Value, target.Value, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    r.Add(CNav, $"트럭 → {room.T.name}({room.Type}) 중심 경로 {path.status}", new Vector2(target.Value.x, target.Value.z));
            }
            foreach (GridCorridor corridor in plan.Corridors)
            {
                List<Vector2Int> cells = corridor.Cells.Where(corridorTiles.Contains).ToList();
                if (cells.Count == 0) continue;
                Vector2 centroid = cells.Aggregate(Vector2.zero, (s, c) => s + Center(c)) / cells.Count;
                Vector2Int mid = cells.OrderBy(c => (Center(c) - centroid).sqrMagnitude).First();
                Vector3? target = NavPointNear(Center(mid), 1.5f);
                NavMeshPath path = new NavMeshPath();
                if (!target.HasValue) { r.Add(CNav, $"길 {corridor.Edge} 가운데 {mid} 근처에 NavMesh가 없다", Center(mid)); continue; }
                if (!NavMesh.CalculatePath(start.Value, target.Value, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    r.Add(CNav, $"트럭 → 길 {corridor.Edge} 가운데 경로 {path.status}", Center(mid));
            }
        }

        // ---- 3. 경계 틈
        InspectBoundary(r, mapFloor, boundary, mapRoot.transform);

        // ---- 4. 투명 장애물 / 고아 충돌체
        InspectGhosts(r, mapRoot, boundary, rooms, mapFloor, obstacles, obstacleCells);

        // ---- 5. 길 폭·포트
        InspectCorridors(r, gen, plan, mapRoot.transform, boundary, mapFloor, walkPhys);

        // ---- 6. 스폰 위치·겹침, 7. 트럭
        InspectSpawns(r, snapshot, mapRoot.transform, boundary, mapFloor, obstacleCells, gen);
        InspectTruck(r, gen, truckGo, playerGo, snapshot, mapRoot.transform, boundary, mapFloor);

        // ---- 8. Id·이름 중복
        List<string> ids = mapRoot.GetComponentsInChildren<AnimalRescue>(true).Select(a => HierarchyPath(a.transform))
            .Concat(mapRoot.GetComponentsInChildren<MonsterCorpsePickup>(true).Select(m => HierarchyPath(m.transform))).ToList();
        foreach (IGrouping<string, string> g in ids.GroupBy(x => x).Where(g => g.Count() > 1)) r.Add(CIds, $"Id 중복 {g.Key} x{g.Count()}");
        foreach (IGrouping<string, Transform> g in mapRoot.transform.Cast<Transform>().GroupBy(t => t.name).Where(g => g.Count() > 1 && g.Key.StartsWith("Room_")))
            r.Add(CIds, $"방 인스턴스 이름 중복 {g.Key} x{g.Count()}");
        foreach (RoomData room in rooms)
            foreach (IGrouping<string, Transform> g in room.T.Cast<Transform>().GroupBy(t => t.name).Where(g => g.Count() > 1 && g.Any(t => t.GetComponent<AnimalRescue>() != null || t.GetComponent<MonsterHealth>() != null)))
                r.Add(CIds, $"{room.T.name} 안 같은 이름 {g.Key} x{g.Count()}");

        // ---- 9. 스폰 총량·상한
        InspectTotals(r, gen, plan, rooms);

        // ---- 10용 서명
        BuildSignature(r, snapshot, gen, plan, mapRoot, rooms, obstacles);
        return r;
    }

    private static bool Entity(AuditSnapshot s, string kind, out AuditEntity e)
    {
        e = s?.Entities.FirstOrDefault(x => x.Kind == kind);
        return e != null;
    }

    private static void InspectBoundary(AuditSeedResult r, HashSet<Vector2Int> mapFloor, Transform boundary, Transform mapRoot)
    {
        if (boundary == null) { r.Add(CBoundary, "MapBoundary가 없다"); return; }
        Vector2[] insets = { Vector2.zero, new Vector2(-0.45f, -0.45f), new Vector2(0.45f, -0.45f), new Vector2(-0.45f, 0.45f), new Vector2(0.45f, 0.45f) };
        HashSet<Vector2Int> uncovered = new HashSet<Vector2Int>();
        int outsideCells = 0, rays = 0, rayGaps = 0, boxGaps = 0;
        HashSet<Vector2Int> checkedOutside = new HashSet<Vector2Int>();
        foreach (Vector2Int c in mapFloor)
        {
            foreach (Vector2Int d in Eight)
            {
                Vector2Int n = c + d;
                if (mapFloor.Contains(n)) continue;
                if (checkedOutside.Add(n))
                {
                    outsideCells++;
                    foreach (Vector2 o in insets)
                    {
                        if (!Physics2D.OverlapPointAll(Center(n) + o).Any(col => col.transform == boundary && col.enabled && !col.isTrigger))
                        {
                            uncovered.Add(n);
                            break;
                        }
                    }
                }
                // 바닥 칸 가운데에서 바깥 칸 쪽으로 쏜다. 무엇이든 막는 충돌체(경계·장애물)에 닿아야 한다.
                Vector2 dir = ((Vector2)d).normalized;
                float dist = ((Vector2)d).magnitude;
                rays++;
                if (!Physics2D.RaycastAll(Center(c), dir, dist + 0.01f).Any(h => IsBlocker(h.collider)))
                {
                    rayGaps++;
                    r.Add(CBoundary, $"레이캐스트: 바닥 {c}에서 {d} 방향 {dist:F2}칸 안에 막는 충돌체가 없다", Center(n));
                }
                // 플레이어 몸통(1칸) 크기 상자를 1.5칸 밀어 본다.
                if (!Physics2D.BoxCastAll(Center(c), new Vector2(0.98f, 0.98f), 0f, dir, 1.5f).Any(h => IsBlocker(h.collider)))
                {
                    boxGaps++;
                    r.Add(CBoundary, $"플레이어 상자 캐스트: 바닥 {c}에서 {d} 방향으로 1.5칸 동안 막히지 않는다", Center(n));
                }
            }
        }
        foreach (Vector2Int n in uncovered) r.Add(CBoundary, $"바닥에 닿은 바깥 칸 {n}이 경계 충돌체로 덮이지 않았다", Center(n));

        // 빠져나가는 길: 경계로 완전히 막히지 않은 바깥 칸을 따라가 바닥 범위 밖(+3칸)까지 닿는지.
        int minX = mapFloor.Min(c => c.x) - 3, maxX = mapFloor.Max(c => c.x) + 3, minY = mapFloor.Min(c => c.y) - 3, maxY = mapFloor.Max(c => c.y) + 3;
        Dictionary<Vector2Int, bool> blockedCache = new Dictionary<Vector2Int, bool>();
        bool Blocked(Vector2Int cell)
        {
            if (blockedCache.TryGetValue(cell, out bool b)) return b;
            b = insets.All(o => Physics2D.OverlapPointAll(Center(cell) + o).Any(IsBlocker));
            blockedCache[cell] = b;
            return b;
        }
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        foreach (Vector2Int n in checkedOutside) if (!Blocked(n) && seen.Add(n)) queue.Enqueue(n);
        int escapes = 0;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            if (c.x <= minX || c.x >= maxX || c.y <= minY || c.y >= maxY) { escapes++; if (escapes <= 3) r.Add(CBoundary, $"바닥에서 경계 밖 {c}까지 막히지 않은 칸으로 이어진다(탈출 경로)", Center(c)); continue; }
            foreach (Vector2Int d in Four)
            {
                Vector2Int n = c + d;
                if (mapFloor.Contains(n) || seen.Contains(n) || Blocked(n)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        r.Stat("경계 검사 바깥 칸", outsideCells);
        r.Stat("경계 레이캐스트", rays);
        r.Stat("경계 미덮음 칸", uncovered.Count);
        r.Stat("경계 레이 틈", rayGaps);
        r.Stat("경계 상자 틈", boxGaps);
        r.Stat("탈출 경로", escapes);
    }

    private static void InspectGhosts(AuditSeedResult r, GameObject mapRoot, Transform boundary, List<RoomData> rooms, HashSet<Vector2Int> mapFloor,
        List<Collider2D> obstacles, HashSet<Vector2Int> obstacleCells)
    {
        // 4a 바닥 밖 NavMesh: 바닥 범위 +3칸 안의 바닥 아닌 칸 가운데 근처(0.45)에 NavMesh가 있고 그 점이 같은 칸 안이면 누수.
        int minX = mapFloor.Min(c => c.x) - 3, maxX = mapFloor.Max(c => c.x) + 3, minY = mapFloor.Min(c => c.y) - 3, maxY = mapFloor.Max(c => c.y) + 3;
        int ghostNav = 0;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (mapFloor.Contains(c)) continue;
                if (NavMesh.SamplePosition(new Vector3(x + 0.5f, 0f, y + 0.5f), out NavMeshHit hit, 0.45f, NavMesh.AllAreas) &&
                    Mathf.FloorToInt(hit.position.x) == x && Mathf.FloorToInt(hit.position.z) == y)
                {
                    ghostNav++;
                    if (ghostNav <= 10) r.Add(CGhostNav, $"바닥이 아닌 칸 {c}에 NavMesh가 있다 (y={hit.position.y:F2})", Center(c));
                }
            }
        if (ghostNav > 10) r.Add(CGhostNav, $"... 바닥이 아닌 칸의 NavMesh 합계 {ghostNav}칸");
        r.Stat("바닥 밖 NavMesh 칸", ghostNav);

        // NavMesh 높이: 격자 경로는 모든 NavGround가 y = 0이어야 한다(한 줄 경로의 y 오프셋 문제가 격자에 없는지).
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length > 0)
        {
            float minH = tri.vertices.Min(p => p.y), maxH = tri.vertices.Max(p => p.y);
            r.Stat("NavMesh 높이 최소", minH);
            r.Stat("NavMesh 높이 최대", maxH);
            if (minH < -0.5f || maxH > 0.5f) r.Add(CGhostNav, $"NavMesh 정점 높이가 {minH:F2}~{maxH:F2} (0 근처가 아니다 - y 오프셋 문제)");
        }

        // 4b 장애물 칸 위 NavMesh(카빙 반영 후): 장애물 칸 가운데 0.3 안에 같은 칸 안쪽 0.6 상자에 들어오는 NavMesh 점이 있으면 몬스터가 장애물을 지나간다.
        int obstacleNav = 0;
        foreach (Vector2Int c in obstacleCells)
        {
            if (!mapFloor.Contains(c)) continue;
            if (NavMesh.SamplePosition(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f), out NavMeshHit hit, 0.3f, NavMesh.AllAreas) &&
                Mathf.Abs(hit.position.x - (c.x + 0.5f)) < 0.3f && Mathf.Abs(hit.position.z - (c.y + 0.5f)) < 0.3f)
            {
                obstacleNav++;
                if (obstacleNav <= 10) r.Add(CObstacleNav, $"장애물 칸 {c} 위에 NavMesh가 남아 있다", Center(c));
            }
        }
        if (obstacleNav > 10) r.Add(CObstacleNav, $"... 장애물 칸 NavMesh 합계 {obstacleNav}칸");
        r.Stat("장애물 칸 NavMesh", obstacleNav);

        // 4c 그림 없는 충돌체: 장애물 충돌체 자신/자식/부모(방 루트 전까지)에 켜진 SpriteRenderer가 없거나, 있어도 충돌 상자와 겹치지 않으면.
        int orphans = 0, invisibleCells = 0, outsideFloorColliders = 0;
        foreach (Collider2D col in obstacles)
        {
            Transform roomRoot = rooms.FirstOrDefault(x => col.transform.IsChildOf(x.T))?.T;
            List<SpriteRenderer> own = col.GetComponentsInChildren<SpriteRenderer>().ToList();
            for (Transform p = col.transform.parent; p != null && p != roomRoot; p = p.parent)
            {
                SpriteRenderer sr = p.GetComponent<SpriteRenderer>();
                if (sr != null) own.Add(sr);
            }
            Bounds cb = col.bounds;
            cb.Expand(new Vector3(0.1f, 0.1f, 1000f));
            bool visual = own.Any(sr => sr.enabled && sr.sprite != null && sr.gameObject.activeInHierarchy && sr.color.a > 0.05f &&
                                        new Bounds(new Vector3(sr.bounds.center.x, sr.bounds.center.y, cb.center.z), new Vector3(sr.bounds.size.x, sr.bounds.size.y, 1f)).Intersects(cb));
            if (!visual)
            {
                orphans++;
                r.Add(CGhostCollider, $"{HierarchyPath(col.transform)} ({col.GetType().Name} {col.bounds.size.x:F2}x{col.bounds.size.y:F2})에 겹치는 스프라이트가 없다", (Vector2)col.bounds.center);
            }
            // 충돌 칸 중 바닥 밖 칸(경계 밖으로 삐져나온 장애물 - 시각적으로는 바깥 마스크에 가려진다).
            if (CellsOf(col.bounds).Any(c => !mapFloor.Contains(c))) outsideFloorColliders++;
        }
        // 바닥 칸 중 장애물 충돌체가 덮지만 어떤 켜진 스프라이트(장애물·장식)도 덮지 않는 칸.
        foreach (RoomData room in rooms)
        {
            List<Bounds> sprites = room.T.GetComponentsInChildren<SpriteRenderer>().Where(sr => sr.enabled && sr.sprite != null && sr.color.a > 0.05f &&
                sr.GetComponentInParent<AnimalRescue>() == null && sr.GetComponentInParent<MonsterHealth>() == null).Select(sr => sr.bounds).ToList();
            foreach (Collider2D col in obstacles.Where(c => c.transform.IsChildOf(room.T)))
                foreach (Vector2Int c in CellsOf(col.bounds))
                {
                    if (!mapFloor.Contains(c)) continue;
                    Vector2 p = Center(c);
                    if (!sprites.Any(b => p.x >= b.min.x - 0.25f && p.x <= b.max.x + 0.25f && p.y >= b.min.y - 0.25f && p.y <= b.max.y + 0.25f))
                    {
                        invisibleCells++;
                        if (invisibleCells <= 10) r.Add(CGhostCollider, $"바닥 칸 {c}을 {HierarchyPath(col.transform)} 충돌체가 막지만 그 칸을 덮는 스프라이트가 없다(투명 장애물)", p);
                    }
                }
        }
        r.Stat("그림 없는 충돌체", orphans);
        r.Stat("투명 장애물 칸", invisibleCells);
        r.Stat("바닥 밖에 걸친 장애물 충돌체", outsideFloorColliders);

        // 바닥 밖에 있는 장애물 외 충돌체(경계·장애물·트럭·캐릭터 제외)가 있는지.
        foreach (Collider2D col in mapRoot.GetComponentsInChildren<Collider2D>())
        {
            if (!IsBlocker(col) || col.transform == boundary || obstacles.Contains(col)) continue;
            r.Add(CGhostCollider, $"장애물도 경계도 아닌 막는 충돌체 {HierarchyPath(col.transform)} ({col.GetType().Name})", (Vector2)col.bounds.center);
        }

        // 4d 충돌체만 지워지고 그림은 남은 장애물: 프리팹에서 충돌체가 있던 오브젝트가 인스턴스에 남아 있는데 충돌체가 없거나 꺼져 있고 스프라이트는 보인다.
        int stripped = 0;
        foreach (RoomData room in rooms)
        {
            if (room.Prefab == null) continue;
            foreach (Collider2D pc in room.Prefab.GetComponentsInChildren<Collider2D>(true))
            {
                if (pc.isTrigger || pc is TilemapCollider2D || pc is CompositeCollider2D) continue;
                string rel = RelativePath(pc.transform, room.Prefab.transform);
                Transform inst = FindMatching(room.T, room.Prefab.transform, pc.transform);
                if (inst == null) continue; // 통째로 지워짐(트럭 여유 영역) - 그림도 같이 사라졌다
                bool hasCollider = inst.GetComponents<Collider2D>().Any(c => c.enabled && !c.isTrigger);
                if (hasCollider) continue;
                bool visible = inst.GetComponentsInChildren<SpriteRenderer>().Any(sr => sr.enabled && sr.sprite != null);
                for (Transform p = inst.parent; p != null && p != room.T && !visible; p = p.parent)
                    visible = p.TryGetComponent(out SpriteRenderer sr) && sr.enabled && sr.sprite != null;
                if (!visible) continue;
                stripped++;
                r.Add(CStripped, $"{room.T.name}({room.Type})/{rel}: 충돌체는 없는데 스프라이트가 남았다(지나갈 수 있는 장애물)", (Vector2)inst.position);
            }
        }
        r.Stat("충돌체만 지워진 장애물", stripped);
    }

    // 프리팹 안의 target에 해당하는 인스턴스 오브젝트. 같은 이름 형제가 많으므로(Obstacles/Obstacle 여러 개) 이름과 localPosition이 같은 것을 고른다.
    // 지워진 오브젝트가 있으면 형제 순서가 달라지므로 순서는 쓰지 않는다.
    private static Transform FindMatching(Transform instanceRoot, Transform prefabRoot, Transform target)
    {
        List<Transform> chain = new List<Transform>();
        for (Transform p = target; p != null && p != prefabRoot; p = p.parent) chain.Insert(0, p);
        Transform current = instanceRoot;
        foreach (Transform step in chain)
        {
            Transform next = null;
            foreach (Transform child in current)
                if (child.name == step.name && (child.localPosition - step.localPosition).sqrMagnitude < 1e-6f) { next = child; break; }
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    private static string RelativePath(Transform t, Transform root)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(t.name);
        for (Transform p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, "/").Insert(0, p.name);
        return sb.ToString();
    }

    private static void InspectCorridors(AuditSeedResult r, VillageMapGenerator gen, GridLayoutPlan plan, Transform mapRoot, Transform boundary,
        HashSet<Vector2Int> mapFloor, HashSet<Vector2Int> walkPhys)
    {
        // 계획 규칙(GridLayoutPlanner.Validate: 같은 방 인접, 트리, 길 교차, 다른 방 바닥 접촉, 포트 입구 장애물, 길 폭).
        // CreateGridPlanInput이 방 종류 경고를 다시 남기므로 그동안 로그는 GridMapAudit 쪽에서 무시한다.
        GridMapAudit.SuppressLogs = true;
        try
        {
            GridPlanInput input = gen.CreateGridPlanInput();
            foreach (string p in GridLayoutPlanner.Validate(plan, input.Types)) r.Add(CCorridor, "계획 검사: " + p);
        }
        finally { GridMapAudit.SuppressLogs = false; }

        int w = plan.CorridorWidth;
        int onCorridor = 0, onPort = 0, deepPort = 0;
        foreach (GridCorridor corridor in plan.Corridors)
        {
            foreach (Vector2Int c in corridor.Cells)
            {
                Collider2D hit = Physics2D.OverlapBoxAll(Center(c), new Vector2(0.9f, 0.9f), 0f).FirstOrDefault(col => IsObstacle(col, mapRoot, boundary));
                if (hit != null) { onCorridor++; r.Add(CCorridor, $"길 {corridor.Edge} 칸 {c}에 장애물 {HierarchyPath(hit.transform)}", Center(c)); }
            }
            foreach (GridPort port in new[] { corridor.PortA, corridor.PortB })
            {
                foreach (Vector2Int c in port.EntryCells)
                {
                    Collider2D hit = Physics2D.OverlapBoxAll(Center(c), new Vector2(0.9f, 0.9f), 0f).FirstOrDefault(col => IsObstacle(col, mapRoot, boundary));
                    if (hit != null) { onPort++; r.Add(CCorridor, $"포트 입구 {corridor.Edge} ({port.Cell.x},{port.Cell.y}) {port.Side} 칸 {c}에 장애물 {HierarchyPath(hit.transform)}", Center(c)); }
                }
                // 참고: 입구 안쪽 3~5칸(계획이 보지 않는 깊이)에 있는 장애물 수.
                Vector2Int inward = port.Side == GridSide.East ? Vector2Int.left : port.Side == GridSide.West ? Vector2Int.right : port.Side == GridSide.North ? Vector2Int.down : Vector2Int.up;
                foreach (Vector2Int e in port.EdgeCells)
                    for (int k = 2; k <= 4; k++)
                    {
                        Vector2Int c = e + inward * k;
                        if (!mapFloor.Contains(c)) continue;
                        if (Physics2D.OverlapBoxAll(Center(c), new Vector2(0.9f, 0.9f), 0f).Any(col => IsObstacle(col, mapRoot, boundary))) deepPort++;
                    }
            }
            // 물리 기준 폭: 길 칸마다 그 칸을 포함하는 w x w 정사각형이 걸을 수 있는 칸으로만 채워지는지.
            foreach (Vector2Int c in corridor.Cells)
            {
                bool fits = false;
                for (int ox = -(w - 1); ox <= 0 && !fits; ox++)
                    for (int oy = -(w - 1); oy <= 0 && !fits; oy++)
                    {
                        bool all = true;
                        for (int a = 0; a < w && all; a++)
                            for (int b = 0; b < w && all; b++) all = walkPhys.Contains(new Vector2Int(c.x + ox + a, c.y + oy + b));
                        fits = all;
                    }
                if (!fits) { r.Add(CCorridor, $"길 {corridor.Edge}의 {c}에서 실제 걸을 수 있는 폭 {w}을 확보하지 못한다", Center(c)); break; }
            }
        }
        r.Stat("길 위 장애물", onCorridor);
        r.Stat("포트 입구 장애물", onPort);
        r.Stat("포트 안쪽 3~5칸 장애물 칸(참고)", deepPort);
    }

    private static void InspectSpawns(AuditSeedResult r, AuditSnapshot snap, Transform mapRoot, Transform boundary, HashSet<Vector2Int> mapFloor,
        HashSet<Vector2Int> obstacleCells, VillageMapGenerator gen)
    {
        if (snap == null) { r.Add(CSpawnPlace, "스폰 스냅샷이 없다"); return; }
        foreach (AuditEntity e in snap.Entities)
        {
            Vector2Int cell = CellOf(e.Pos);
            if (e.Kind == "트럭") continue;
            if (!mapFloor.Contains(cell)) r.Add(CSpawnPlace, $"{e.Kind} {e.Path}이 바닥이 아닌 칸 {cell}에 스폰", e.Pos);
            else if (obstacleCells.Contains(cell)) r.Add(CSpawnPlace, $"{e.Kind} {e.Path}이 장애물 칸 {cell}에 스폰", e.Pos);
            if (e.Kind == "몬스터")
            {
                if (!NavMesh.SamplePosition(new Vector3(e.Pos.x, 0f, e.Pos.y), out NavMeshHit hit, 0.3f, NavMesh.AllAreas) ||
                    new Vector2(hit.position.x - e.Pos.x, hit.position.z - e.Pos.y).magnitude > 0.05f)
                    r.Add(CSpawnPlace, $"몬스터 {e.Path}이 NavMesh 밖 {e.Pos}에 스폰", e.Pos);
            }
            else if (e.BlockerDepth > 0.01f)
            {
                // 동물/플레이어: 실제 충돌체가 장애물·경계·트럭에 파고든 깊이(Physics2D.Distance).
                string cat = e.Kind == "플레이어" && e.Blocker == "TruckPoint" ? CPlayerTruck : CSpawnPlace;
                r.Add(cat, $"{e.Kind} {e.Path}({e.Prefab}, 반경 {e.Radius:F2})이 {(e.BlockerIsBoundary ? "경계 충돌체" : e.Blocker)}에 {e.BlockerDepth:F2}칸 파고든 채 스폰", e.Pos);
                string maxKey = $"스폰 파고듦 최대({e.Kind})";
                r.Stats[maxKey] = Mathf.Max(r.Stats.TryGetValue(maxKey, out double m) ? (float)m : 0f, e.BlockerDepth);
                r.Stat($"스폰 파고듦 수({e.Kind}{(e.BlockerIsBoundary ? "-경계" : "")})", 1);
            }
        }
        // 몬스터 에이전트가 실제로 NavMesh 위에 있는지(로드 몇 프레임 뒤).
        foreach (NavMeshAgent agent in mapRoot.GetComponentsInChildren<NavMeshAgent>())
            if (agent.enabled && !agent.isOnNavMesh) r.Add(CSpawnPlace, $"몬스터 {HierarchyPath(agent.transform)}의 NavMeshAgent가 NavMesh 위에 있지 않다", new Vector2(agent.transform.position.x, agent.transform.position.z));

        // 서로 겹침(스폰 시점 실제 모양 기준, 0.02칸 이하는 맞닿음으로 본다). 플레이어-트럭은 7b로 따로 센다.
        // 동물-플레이어는 AnimalRescue가 충돌을 끄고, 몬스터는 2D 충돌체가 없어 서로 밀지 않는다 - 그래도 그림이 겹쳐 보이므로 센다.
        List<AuditEntity> list = snap.Entities;
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                AuditEntity a = list[i], b = list[j];
                float depth = AuditSnapshot.OverlapDepth(a, b);
                if (depth <= 0.02f) continue;
                bool playerTruck = (a.Kind == "플레이어" && b.Kind == "트럭") || (a.Kind == "트럭" && b.Kind == "플레이어");
                if (playerTruck) continue;
                r.Add(CSpawnOverlap, $"{a.Kind} {a.Path} ↔ {b.Kind} {b.Path} 스폰 모양이 {depth:F2}칸 겹친다 ({a.Pos} / {b.Pos})", (a.Pos + b.Pos) * 0.5f);
                r.Stat($"겹침 {a.Kind}-{b.Kind}", 1);
                string maxKey = $"겹침 최대 깊이 {a.Kind}-{b.Kind}";
                r.Stats[maxKey] = Mathf.Max(r.Stats.TryGetValue(maxKey, out double m) ? (float)m : 0f, depth);
            }
    }

    private static Rect Shrink(Rect r, float d) => new Rect(r.x + d, r.y + d, Mathf.Max(0f, r.width - 2 * d), Mathf.Max(0f, r.height - 2 * d));

    private static void InspectTruck(AuditSeedResult r, VillageMapGenerator gen, GameObject truck, GameObject player, AuditSnapshot snap, Transform mapRoot,
        Transform boundary, HashSet<Vector2Int> mapFloor)
    {
        BoxCollider2D box = truck != null ? truck.GetComponent<BoxCollider2D>() : null;
        if (box == null) { r.Add(CTruckClear, "TruckPoint 충돌체가 없다"); return; }
        Bounds tb = box.bounds;
        float margin = ReadField<float>(gen, "truckClearanceMarginPerSide");
        Vector2 clearance = (Vector2)tb.size + Vector2.one * margin * 2f;
        foreach (Collider2D c in Physics2D.OverlapBoxAll(tb.center, clearance, 0f).Where(c => IsObstacle(c, mapRoot, boundary)))
            r.Add(CTruckClear, $"트럭 여유 영역({margin}칸) 안에 장애물 {HierarchyPath(c.transform)}이 남아 있다", (Vector2)c.bounds.center);
        foreach (Collider2D c in Physics2D.OverlapBoxAll(tb.center, (Vector2)tb.size * 0.99f, 0f).Where(c => IsBlocker(c) && c != box))
            r.Add(CTruckClear, $"트럭 충돌체가 {HierarchyPath(c.transform)}({(c.transform == boundary ? "경계" : "장애물")})와 겹친다", (Vector2)tb.center);

        // 여유 영역이 모두 바닥이었는지(아니면 생성기가 1.1칸 최소 여유나 더 작은 기준으로 물러났다).
        bool AllFloor(Vector2 size)
        {
            for (int y = Mathf.FloorToInt(tb.center.y - size.y / 2f); y < Mathf.CeilToInt(tb.center.y + size.y / 2f); y++)
                for (int x = Mathf.FloorToInt(tb.center.x - size.x / 2f); x < Mathf.CeilToInt(tb.center.x + size.x / 2f); x++)
                    if (!mapFloor.Contains(new Vector2Int(x, y))) return false;
            return true;
        }
        string tier = AllFloor(clearance) ? "2.5칸 여유" : AllFloor((Vector2)tb.size + Vector2.one * 2.2f) ? "1.1칸 최소 여유" : "여유 없음(물러남)";
        r.Notes.Add("트럭 배치: " + tier);
        r.Stat("트럭 배치 " + tier, 1);

        // 7b 플레이어-트럭 겹침: 스폰 시점과 검사 시점 둘 다.
        AuditEntity sp = snap?.Entities.FirstOrDefault(e => e.Kind == "플레이어"), st = snap?.Entities.FirstOrDefault(e => e.Kind == "트럭");
        if (sp != null && st != null && Shrink(sp.Box, 0.02f).Overlaps(Shrink(st.Box, 0.02f)))
            r.Add(CPlayerTruck, $"스폰 시점에 플레이어 {sp.Box} 와 트럭 {st.Box} 가 겹친다", sp.Pos);
        Collider2D pc = player != null ? player.GetComponent<Collider2D>() : null;
        if (pc != null && Shrink(RectOf(pc.bounds), 0.02f).Overlaps(Shrink(RectOf(tb), 0.02f)))
            r.Add(CPlayerTruck, $"검사 시점(몇 프레임 뒤)에 플레이어 {RectOf(pc.bounds)} 와 트럭 {RectOf(tb)} 가 겹친다", (Vector2)pc.bounds.center);
        if (sp != null && st != null)
        {
            float gapX = Mathf.Abs(sp.Pos.x - st.Pos.x) - (sp.Box.width + st.Box.width) / 2f, gapY = Mathf.Abs(sp.Pos.y - st.Pos.y) - (sp.Box.height + st.Box.height) / 2f;
            r.Stat("플레이어-트럭 틈(칸)", Mathf.Max(gapX, gapY));
        }
    }

    private static void InspectTotals(AuditSeedResult r, VillageMapGenerator gen, GridLayoutPlan plan, List<RoomData> rooms)
    {
        GameBalanceConfig config = ReadField<GameBalanceConfig>(gen, "config");
        int animalTotal = config != null ? config.gridAnimalTotal : 27, monsterTotal = config != null ? config.gridMonsterTotal : 12;
        int animalCap = config != null ? Mathf.Max(1, config.gridMaxAnimalsPerRoom) : 3, monsterCap = config != null ? Mathf.Max(1, config.gridMaxMonstersPerRoom) : 2;
        int freeRadius = ReadField<int>(gen, "monsterFreeRoomRadius");
        bool MonsterAllowed(Vector2Int g) => freeRadius < 0 || Mathf.Max(Mathf.Abs(g.x - plan.TruckCell.x), Mathf.Abs(g.y - plan.TruckCell.y)) > freeRadius;
        int expectedAnimals = Mathf.Min(animalTotal, rooms.Count * animalCap);
        int expectedMonsters = Mathf.Min(monsterTotal, rooms.Count(x => MonsterAllowed(x.Grid)) * monsterCap);
        int animals = 0, monsters = 0;
        foreach (RoomData room in rooms)
        {
            int a = room.T.GetComponentsInChildren<AnimalRescue>(true).Length, m = room.T.GetComponentsInChildren<MonsterHealth>(true).Length;
            animals += a; monsters += m;
            if (a > animalCap) r.Add(CTotals, $"{room.T.name}({room.Type}) 동물 {a}마리 > 방당 상한 {animalCap}");
            if (m > monsterCap) r.Add(CTotals, $"{room.T.name}({room.Type}) 몬스터 {m}마리 > 방당 상한 {monsterCap}");
            if (m > 0 && !MonsterAllowed(room.Grid)) r.Add(CTotals, $"몬스터 금지 방 {room.T.name}({room.Type})에 몬스터 {m}마리");
            if (a == 0) r.Add(CTotals, $"{room.T.name}({room.Type})에 동물이 없다");
        }
        if (animals != expectedAnimals) r.Add(CTotals, $"동물 {animals}마리 (기대 {expectedAnimals} = min(총량 {animalTotal}, 방 {rooms.Count} x 상한 {animalCap}))");
        if (monsters != expectedMonsters) r.Add(CTotals, $"몬스터 {monsters}마리 (기대 {expectedMonsters})");
        r.Stat("동물", animals);
        r.Stat("몬스터", monsters);
        // 겹치지 않는 자리를 못 찾아 생성기가 줄인 수(VillageMapGenerator.LastGridSpawnShortfall). 줄였으면 위 총량 검사에도 걸린다.
        (int a, int m) shortfall = gen.LastGridSpawnShortfall;
        r.Stat("자리 부족으로 줄인 동물", shortfall.a);
        r.Stat("자리 부족으로 줄인 몬스터", shortfall.m);
    }

    private static void BuildSignature(AuditSeedResult r, AuditSnapshot snap, VillageMapGenerator gen, GridLayoutPlan plan, GameObject mapRoot,
        List<RoomData> rooms, List<Collider2D> obstacles)
    {
        List<string> s = r.SignatureLines;
        s.Add("plan " + plan.Signature());
        foreach (RoomData room in rooms) s.Add($"room {room.T.name} {room.Type} {room.T.position}");
        foreach (string o in obstacles.Select(c => $"obstacle {HierarchyPath(c.transform)} {c.bounds.center.x:F3},{c.bounds.center.y:F3}").OrderBy(x => x, System.StringComparer.Ordinal)) s.Add(o);
        if (snap != null)
            foreach (AuditEntity e in snap.Entities.OrderBy(e => e.Kind).ThenBy(e => e.Path, System.StringComparer.Ordinal))
                s.Add($"entity {e.Kind} {e.Path} {e.Prefab} {e.Pos.x:F3},{e.Pos.y:F3} w{e.Weight}");
        Transform mask = mapRoot.transform.Find("OutsideMapMask");
        s.Add("mask " + MeshHash(mask != null ? mask.GetComponent<MeshFilter>()?.sharedMesh : null));
        Transform boundary = mapRoot.transform.Find("MapBoundary");
        if (boundary != null) s.Add("boundary " + string.Join(";", boundary.GetComponents<BoxCollider2D>().Select(b => $"{b.offset.x},{b.offset.y},{b.size.x},{b.size.y}")));
        Transform shadows = mapRoot.transform.Find("WallShadows");
        if (shadows != null) s.Add("shadows " + string.Join(";", shadows.GetComponentsInChildren<ShadowCaster2D>(true).Select(c => c.shapePath?.Length ?? 0)));
        s.Add("corridorTiles " + CorridorTiles(mapRoot).Count);
        s.Add("navmesh " + NavMeshHash(out _));
        Transform band = mapRoot.transform.Find("OutsideFloorBand");
        s.Add("band " + (band != null ? band.GetComponentInChildren<Tilemap>()?.GetUsedTilesCount().ToString() : "-"));
        foreach (RoomData room in rooms)
        {
            Tilemap ground = room.T.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            unchecked
            {
                long h = 17;
                foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
                {
                    TileBase t = ground.GetTile(p);
                    if (t != null) h = h * 31 + (p.x * 73856093 ^ p.y * 19349663 ^ t.name.GetHashCode());
                }
                s.Add($"ground {room.T.name} {h:x}");
            }
        }
    }
}
