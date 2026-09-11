using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Tilemaps;

/// <summary>
/// Day가 시작되어 VillageScene이 로드될 때마다 등록된 방 모듈(Tilemap 프리팹) 중 일부를
/// 무작위로 골라 항상 동쪽으로만 한 줄로 딱 붙여 맵을 새로 조립한다 - 방향을 매번
/// 바꾸지 않으므로 절대 겹치거나 이상하게 이어지지 않는다. 플레이어/트럭 지점은 전체
/// 맵의 가로 한가운데에 가장 가까운 방에 스폰된다. 이어붙인 뒤 각 방의 바닥 타일을
/// 기준으로 NavMesh를 다시 구워서, 절차적으로 생성된 위치에서도 몬스터(NavMeshAgent)가
/// 순찰/추격할 수 있게 한다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class VillageMapGenerator : MonoBehaviour
{
    [Header("방 모듈 (각 프리팹은 Ground Tilemap이 필요)")]
    [SerializeField] private GameObject[] roomPrefabs;
    [SerializeField] private int minRoomCount = 3;
    [SerializeField] private int maxRoomCount = 4;

    [Header("방마다 하나씩 무작위 배치할 동물/몬스터")]
    [SerializeField] private GameObject[] animalPrefabs;
    [SerializeField] private GameObject[] monsterPrefabs;

    [Header("기존 씬 오브젝트 (재배치할 대상)")]
    [SerializeField] private string playerObjectName = "Player";
    [SerializeField] private string truckPointObjectName = "TruckPoint";

    private class RoomInstance
    {
        public GameObject Root;
        public string PrefabName;
        public List<Vector2Int> FloorCells;
    }

    private void Awake()
    {
        // Reuse the same layout every time VillageScene is re-entered on the same Day (e.g. after
        // a truck-scene round trip); only roll a new one when GameManager reports a new Day.
        if (GameManager.Instance != null)
        {
            Random.InitState(GameManager.Instance.GetVillageMapSeedForToday());
        }

        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            Debug.LogError($"{name}: no room prefabs assigned - cannot generate the village map.");
            return;
        }

        GameObject mapRoot = new GameObject("GeneratedMap");
        List<RoomInstance> placedRooms = BuildRoomChain(mapRoot.transform);
        if (placedRooms.Count == 0) return;

        List<MeshFilter> navGroundMeshes = new List<MeshFilter>();
        foreach (RoomInstance room in placedRooms)
        {
            // Doorways carved between rooms during chaining added floor tiles after FloorCells
            // was first captured, so re-read the tilemap now to pick those up for the NavMesh.
            room.FloorCells = GetFloorCells(room.Root);
            navGroundMeshes.Add(BuildNavGround(room));
        }
        BakeNavMesh(mapRoot, navGroundMeshes);

        foreach (RoomInstance room in placedRooms)
        {
            AddObstacleCarvers(room, mapRoot.transform);
        }

        // Obstacle colliders baked into the room prefabs are already live at this point, so make
        // sure Physics2D sees their current transforms before we probe for clear spots.
        Physics2D.SyncTransforms();

        RoomInstance centerRoom = FindCenterRoom(placedRooms);
        Vector2Int spawnCell = PickSafeCell(centerRoom, null) ?? PickCentralCell(centerRoom);
        PositionExistingObject(playerObjectName, ToSpritePosition(centerRoom, spawnCell));

        Vector2Int truckCell = PickSafeCell(centerRoom, spawnCell) ?? spawnCell;
        Vector2 truckXY = ToWorldXY(centerRoom, truckCell);
        PositionExistingObject(truckPointObjectName, new Vector3(truckXY.x, truckXY.y, 0f));

        foreach (RoomInstance room in placedRooms)
        {
            SpawnAnimal(room, mapRoot.transform);
            SpawnMonster(room, mapRoot.transform);
        }
    }

    // 각 방 프리팹에 미리 뚫어둔 출구 틈(빈 칸)의 로컬 셀 좌표. 방을 이어붙인 뒤에는 이
    // 원래 틈을 전부 막고, 실제로 맞닿은 경계 전체를 새로 뚫는다(CarveDoorway) - 그래야
    // 두 방이 딱 붙어서 하나의 외곽처럼 보이고, 안 쓰는 출구가 엉뚱한 곳에 구멍으로 남지 않는다.
    private static readonly Dictionary<string, Vector2Int[]> ExitGapCellsByRoom = new Dictionary<string, Vector2Int[]>
    {
        ["RoomA_Corridor"] = new[]
        {
            new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(-1, 2),
            new Vector2Int(16, 0), new Vector2Int(16, 1), new Vector2Int(16, 2),
        },
        ["RoomB_Square"] = new[]
        {
            new Vector2Int(5, -1), new Vector2Int(6, -1),
            new Vector2Int(12, 4), new Vector2Int(12, 5),
        },
        ["RoomC_LShape"] = new[]
        {
            new Vector2Int(2, -1), new Vector2Int(3, -1),
            new Vector2Int(1, 10), new Vector2Int(2, 10),
        },
        ["RoomD_Connected"] = new[]
        {
            new Vector2Int(-1, 1), new Vector2Int(-1, 2),
            new Vector2Int(8, 11), new Vector2Int(9, 11),
        },
    };

    // 각 방 프리팹에 미리 배치해둔 장애물(크레이트)의 로컬 셀 좌표. NavMesh를 구울 때 이
    // 칸들은 바닥에서 빼서 구멍으로 남겨두기 때문에, 장애물은 2D 충돌체뿐 아니라
    // NavMeshAgent(몬스터) 경로에서도 실제로 피해 다녀야 할 장애물이 된다.
    private static readonly Dictionary<string, Vector2Int[]> ObstacleCellsByRoom = new Dictionary<string, Vector2Int[]>
    {
        ["RoomA_Corridor"] = new[] { new Vector2Int(4, 0), new Vector2Int(11, 2) },
        ["RoomB_Square"] = new[]
        {
            new Vector2Int(4, 4), new Vector2Int(5, 4), new Vector2Int(4, 5), new Vector2Int(5, 5),
            new Vector2Int(8, 3), new Vector2Int(8, 6),
        },
        ["RoomC_LShape"] = new[] { new Vector2Int(5, 4) },
        ["RoomD_Connected"] = new Vector2Int[0],
    };

    private struct IntRect
    {
        public int MinX, MaxX, MinY, MaxY;
    }

    // 방을 순서대로 만들면서 매번 직전 방의 바운딩 박스 바로 동쪽에 딱 붙는 위치를 계산해
    // 그 자리에 Instantiate한다. 항상 같은 방향(동쪽)으로만 한 줄로 이어붙이기 때문에
    // 절대 서로 겹치지 않고 항상 깔끔하게 이어진다. 붙인 뒤에는 두 방이 맞닿은 경계
    // 전체(겹치는 구간)를 뚫어서 하나의 연속된 공간처럼 걸어 다닐 수 있게 한다.
    private List<RoomInstance> BuildRoomChain(Transform mapRoot)
    {
        List<GameObject> pool = new List<GameObject>(roomPrefabs);
        Shuffle(pool);

        int count = Mathf.Clamp(Random.Range(minRoomCount, maxRoomCount + 1), 1, pool.Count);
        List<RoomInstance> placed = new List<RoomInstance>();

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = pool[i];
            string prefabName = prefab.name;

            GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, mapRoot);
            RoomInstance room = new RoomInstance
            {
                Root = instance,
                PrefabName = prefabName,
                FloorCells = GetFloorCells(instance)
            };
            SealExitGaps(room, prefabName);

            if (placed.Count > 0)
            {
                RoomInstance previous = placed[placed.Count - 1];
                PlaceAdjacent(previous, room);
                CarveDoorway(previous, room);
            }

            placed.Add(room);
        }

        return placed;
    }

    // 전체 맵(모든 방을 합친 가로 범위)의 한가운데에 가장 가까운 방을 시작 지점으로 고른다.
    private static RoomInstance FindCenterRoom(List<RoomInstance> rooms)
    {
        int minX = int.MaxValue, maxX = int.MinValue;
        foreach (RoomInstance r in rooms)
        {
            IntRect b = GetFloorBoundsWorld(r);
            if (b.MinX < minX) minX = b.MinX;
            if (b.MaxX > maxX) maxX = b.MaxX;
        }
        int mapCenterX = (minX + maxX) / 2;

        RoomInstance best = rooms[0];
        int bestDist = int.MaxValue;
        foreach (RoomInstance r in rooms)
        {
            IntRect b = GetFloorBoundsWorld(r);
            int roomCenterX = (b.MinX + b.MaxX) / 2;
            int dist = Mathf.Abs(roomCenterX - mapCenterX);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = r;
            }
        }
        return best;
    }

    private static void SealExitGaps(RoomInstance room, string prefabName)
    {
        if (!ExitGapCellsByRoom.TryGetValue(prefabName, out Vector2Int[] gaps)) return;

        Tilemap walls = room.Root.transform.Find("Walls").GetComponent<Tilemap>();
        TileBase wallTile = FindAnyTile(walls);
        if (wallTile == null) return;

        foreach (Vector2Int cell in gaps)
        {
            walls.SetTile(new Vector3Int(cell.x, cell.y, 0), wallTile);
        }
        RefreshWallCollider(walls);
    }

    // TilemapCollider2D/CompositeCollider2D don't reliably rebuild their cached collision shape
    // synchronously when tiles are changed via script at runtime - without this, players could
    // still bump into a wall (or walk through empty space) that no longer matches the tiles
    // actually shown. Forcing a rebuild right after every SetTile batch keeps them in sync.
    private static void RefreshWallCollider(Tilemap walls)
    {
        walls.RefreshAllTiles();
        CompositeCollider2D composite = walls.GetComponent<CompositeCollider2D>();
        if (composite != null) composite.GenerateGeometry();
    }

    private static IntRect GetFloorBoundsLocal(List<Vector2Int> cells)
    {
        IntRect b = new IntRect { MinX = int.MaxValue, MaxX = int.MinValue, MinY = int.MaxValue, MaxY = int.MinValue };
        foreach (Vector2Int c in cells)
        {
            if (c.x < b.MinX) b.MinX = c.x;
            if (c.x > b.MaxX) b.MaxX = c.x;
            if (c.y < b.MinY) b.MinY = c.y;
            if (c.y > b.MaxY) b.MaxY = c.y;
        }
        return b;
    }

    private static IntRect GetFloorBoundsWorld(RoomInstance room)
    {
        IntRect local = GetFloorBoundsLocal(room.FloorCells);
        int ox = Mathf.RoundToInt(room.Root.transform.position.x);
        int oy = Mathf.RoundToInt(room.Root.transform.position.y);
        return new IntRect { MinX = local.MinX + ox, MaxX = local.MaxX + ox, MinY = local.MinY + oy, MaxY = local.MaxY + oy };
    }

    // next를 previous 바로 동쪽에 틈 없이 붙이고, 세로축은 두 방 전체가 아니라 실제로
    // 맞닿는 가장자리(previous의 맨 오른쪽 바닥 칸들, next의 맨 왼쪽 바닥 칸들)끼리 겹치는
    // 줄 수가 가장 많아지는 위치로 맞춘다. RoomD처럼 가장자리 바닥이 중간에 끊겨서 두
    // 구간으로 나뉘는 방은 (최소+최대)/2로 중심을 잡으면 그 중심이 실제로는 바닥이 없는
    // 빈 틈을 가리켜서, 겹치는 줄이 하나도 없어 문이 아예 안 뚫리는 경우가 있었다.
    private static void PlaceAdjacent(RoomInstance previous, RoomInstance next)
    {
        IntRect prevWorld = GetFloorBoundsWorld(previous);
        IntRect nextLocal = GetFloorBoundsLocal(next.FloorCells);

        int prevOffsetY = Mathf.RoundToInt(previous.Root.transform.position.y);
        int prevEdgeLocalX = prevWorld.MaxX - Mathf.RoundToInt(previous.Root.transform.position.x);
        List<int> prevEdgeYsWorld = GetEdgeRows(previous.FloorCells, prevEdgeLocalX);
        for (int i = 0; i < prevEdgeYsWorld.Count; i++) prevEdgeYsWorld[i] += prevOffsetY;

        List<int> nextEdgeYsLocal = GetEdgeRows(next.FloorCells, nextLocal.MinX);

        int offsetX = (prevWorld.MaxX + 1) - nextLocal.MinX;
        int offsetY = FindBestOverlapOffset(prevEdgeYsWorld, nextEdgeYsLocal);

        next.Root.transform.position = new Vector3(offsetX, offsetY, 0f);
    }

    // 주어진 로컬 x열에서 실제 바닥 칸이 있는 y좌표 목록 (끊겨 있으면 그대로 여러 구간).
    private static List<int> GetEdgeRows(List<Vector2Int> floorCells, int edgeLocalX)
    {
        List<int> ys = new List<int>();
        foreach (Vector2Int c in floorCells)
        {
            if (c.x == edgeLocalX) ys.Add(c.y);
        }
        return ys;
    }

    // nextYsLocal에 더할 정수 오프셋 중, prevYsWorld와 겹치는 줄 수가 가장 많아지는 값을
    // 찾는다. 두 가장자리가 실제로 몇 줄이든 겹치도록 보장하므로(가능한 경우) 중심점이
    // 빈 틈을 가리키는 문제가 생기지 않는다.
    private static int FindBestOverlapOffset(List<int> prevYsWorld, List<int> nextYsLocal)
    {
        HashSet<int> prevSet = new HashSet<int>(prevYsWorld);
        HashSet<int> candidateOffsets = new HashSet<int>();
        foreach (int py in prevYsWorld)
        {
            foreach (int ny in nextYsLocal) candidateOffsets.Add(py - ny);
        }

        int bestOffset = 0, bestOverlap = -1;
        foreach (int offset in candidateOffsets)
        {
            int overlap = 0;
            foreach (int ny in nextYsLocal)
            {
                if (prevSet.Contains(ny + offset)) overlap++;
            }
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestOffset = offset;
            }
        }

        if (bestOverlap > 0) return bestOffset;

        // 겹치는 오프셋을 못 찾은 경우(이론상 거의 없음)에도 최소한 합리적인 값을 낸다.
        float prevMid = Average(prevYsWorld);
        float nextMid = Average(nextYsLocal);
        return Mathf.RoundToInt(prevMid - nextMid);
    }

    private static float Average(List<int> values)
    {
        if (values.Count == 0) return 0f;
        float sum = 0f;
        foreach (int v in values) sum += v;
        return sum / values.Count;
    }

    // 서로 붙은 동-서 경계에서 겹치는 구간 전체의 벽을 양쪽 다 지우고 바닥을 채워서, 한
    // 칸짜리 문이 아니라 방 폭만큼 넓게 뚫린 통로로 연결한다.
    private static void CarveDoorway(RoomInstance a, RoomInstance b)
    {
        IntRect boundsA = GetFloorBoundsWorld(a);
        IntRect boundsB = GetFloorBoundsWorld(b);

        Transform aRoot = a.Root.transform;
        Transform bRoot = b.Root.transform;
        Tilemap groundA = aRoot.Find("Ground").GetComponent<Tilemap>();
        Tilemap wallsA = aRoot.Find("Walls").GetComponent<Tilemap>();
        Tilemap groundB = bRoot.Find("Ground").GetComponent<Tilemap>();
        Tilemap wallsB = bRoot.Find("Walls").GetComponent<Tilemap>();
        TileBase floorTile = FindAnyTile(groundA) ?? FindAnyTile(groundB);
        if (floorTile == null) return;

        Vector2Int offsetA = new Vector2Int(Mathf.RoundToInt(aRoot.position.x), Mathf.RoundToInt(aRoot.position.y));
        Vector2Int offsetB = new Vector2Int(Mathf.RoundToInt(bRoot.position.x), Mathf.RoundToInt(bRoot.position.y));

        // ㄱ자/분리된 방은 바운딩 박스 안에 바닥이 없는 빈 칸(움푹 들어간 부분)이 있을 수
        // 있어서, 겹치는 세로 범위 전체를 그냥 뚫으면 그 빈 칸에도 문을 내서 벽 밖에 바닥이
        // 붕 뜨거나 벽이 없는 자리가 생겼다. 두 방 모두 그 줄의 맞닿는 칸에 실제 바닥
        // 타일이 있을 때만 뚫는다.
        HashSet<Vector2Int> floorSetA = new HashSet<Vector2Int>(a.FloorCells);
        HashSet<Vector2Int> floorSetB = new HashSet<Vector2Int>(b.FloorCells);

        int overlapMinY = Mathf.Max(boundsA.MinY, boundsB.MinY);
        int overlapMaxY = Mathf.Min(boundsA.MaxY, boundsB.MaxY);
        int borderXForA = boundsA.MaxX + 1;
        int borderXForB = boundsB.MinX - 1;

        for (int y = overlapMinY; y <= overlapMaxY; y++)
        {
            Vector2Int aEdgeCellLocal = new Vector2Int(boundsA.MaxX - offsetA.x, y - offsetA.y);
            Vector2Int bEdgeCellLocal = new Vector2Int(boundsB.MinX - offsetB.x, y - offsetB.y);
            if (!floorSetA.Contains(aEdgeCellLocal) || !floorSetB.Contains(bEdgeCellLocal)) continue;

            OpenCell(wallsA, groundA, floorTile, new Vector2Int(borderXForA, y) - offsetA);
            OpenCell(wallsB, groundB, floorTile, new Vector2Int(borderXForB, y) - offsetB);
        }

        RefreshWallCollider(wallsA);
        RefreshWallCollider(wallsB);
    }

    private static void OpenCell(Tilemap walls, Tilemap ground, TileBase floorTile, Vector2Int localCell)
    {
        Vector3Int pos = new Vector3Int(localCell.x, localCell.y, 0);
        walls.SetTile(pos, null);
        if (ground.GetTile(pos) == null) ground.SetTile(pos, floorTile);
    }

    private static TileBase FindAnyTile(Tilemap tilemap)
    {
        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
        {
            TileBase t = tilemap.GetTile(pos);
            if (t != null) return t;
        }
        return null;
    }

    private static List<Vector2Int> GetFloorCells(GameObject room)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        Transform groundTransform = room.transform.Find("Ground");
        if (groundTransform == null) return cells;

        Tilemap tilemap = groundTransform.GetComponent<Tilemap>();
        foreach (Vector3Int pos in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(pos)) cells.Add(new Vector2Int(pos.x, pos.y));
        }
        return cells;
    }

    private static Vector2Int PickCentralCell(RoomInstance room)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2Int c in room.FloorCells) centroid += new Vector2(c.x + 0.5f, c.y + 0.5f);
        centroid /= room.FloorCells.Count;

        Vector2Int best = room.FloorCells[0];
        float bestDistanceSqr = float.MaxValue;
        foreach (Vector2Int c in room.FloorCells)
        {
            float d = (new Vector2(c.x + 0.5f, c.y + 0.5f) - centroid).sqrMagnitude;
            if (d < bestDistanceSqr)
            {
                bestDistanceSqr = d;
                best = c;
            }
        }
        return best;
    }

    // 플레이어/트럭 지점처럼 실제 충돌체가 있는 오브젝트를 놓을 안전한 칸을 고른다.
    // 벽에 바로 붙은 칸(PickInteriorCell과 동일 기준)과, 장애물 콜라이더와 겹치는 칸을
    // 모두 피해서 방 한가운데에 가까운 순으로 고르므로, 스폰하자마자 장애물에 끼거나
    // 벽에 바짝 붙어 있는 일이 없다. 안전한 칸이 전혀 없으면 null.
    private static Vector2Int? PickSafeCell(RoomInstance room, Vector2Int? avoid)
    {
        HashSet<Vector2Int> floorSet = new HashSet<Vector2Int>(room.FloorCells);
        List<Vector2Int> candidates = new List<Vector2Int>();
        foreach (Vector2Int c in room.FloorCells)
        {
            if (avoid.HasValue && c == avoid.Value) continue;
            if (!floorSet.Contains(c + Vector2Int.up) || !floorSet.Contains(c + Vector2Int.down) ||
                !floorSet.Contains(c + Vector2Int.left) || !floorSet.Contains(c + Vector2Int.right))
            {
                continue;
            }

            Vector2 worldCenter = ToWorldXY(room, c);
            if (Physics2D.OverlapBox(worldCenter, new Vector2(0.9f, 0.9f), 0f) != null) continue;

            candidates.Add(c);
        }

        if (candidates.Count == 0) return null;

        Vector2 centroid = Vector2.zero;
        foreach (Vector2Int c in room.FloorCells) centroid += new Vector2(c.x + 0.5f, c.y + 0.5f);
        centroid /= room.FloorCells.Count;

        Vector2Int best = candidates[0];
        float bestDistanceSqr = float.MaxValue;
        foreach (Vector2Int c in candidates)
        {
            float d = (new Vector2(c.x + 0.5f, c.y + 0.5f) - centroid).sqrMagnitude;
            if (d < bestDistanceSqr)
            {
                bestDistanceSqr = d;
                best = c;
            }
        }
        return best;
    }

    // 2D 게임 좌표(x, y) - 스프라이트를 쓰는 오브젝트(플레이어, 동물, 트럭 지점)용.
    private static Vector2 ToWorldXY(RoomInstance room, Vector2Int cell)
    {
        Vector2 roomOffset = room.Root.transform.position;
        return roomOffset + new Vector2(cell.x + 0.5f, cell.y + 0.5f);
    }

    private static Vector3 ToSpritePosition(RoomInstance room, Vector2Int cell)
    {
        Vector2 xy = ToWorldXY(room, cell);
        return new Vector3(xy.x, xy.y, 0f);
    }

    // 몬스터는 NavMeshAgent로 x,z 평면을 이동하고 x,z를 2D x,y로 매핑해서 그리므로
    // 실제로 스폰할 때는 2D y를 z에 넣어야 한다 (MonsterAI/MonsterHealth의 GamePosition 참고).
    private static Vector3 ToNavPosition(RoomInstance room, Vector2Int cell)
    {
        Vector2 xy = ToWorldXY(room, cell);
        return new Vector3(xy.x, 0f, xy.y);
    }

    private static void PositionExistingObject(string objectName, Vector3 worldPosition)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null)
        {
            Debug.LogWarning($"VillageMapGenerator: could not find '{objectName}' to reposition.");
            return;
        }
        go.transform.position = worldPosition;
    }

    private void SpawnAnimal(RoomInstance room, Transform parent)
    {
        if (animalPrefabs == null || animalPrefabs.Length == 0 || room.FloorCells.Count == 0) return;

        GameObject prefab = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
        Vector2Int cell = PickInteriorCell(room);
        Instantiate(prefab, ToSpritePosition(room, cell), Quaternion.identity, parent);
    }

    private void SpawnMonster(RoomInstance room, Transform parent)
    {
        if (monsterPrefabs == null || monsterPrefabs.Length == 0 || room.FloorCells.Count == 0) return;

        GameObject prefab = monsterPrefabs[Random.Range(0, monsterPrefabs.Length)];
        Vector2Int cell = PickInteriorCell(room);
        Vector3 desired = ToNavPosition(room, cell);

        // NavMesh baking erodes walkable area inward from walls by the agent radius, so even an
        // "interior" cell's exact center can land just outside the baked mesh. Snap to the
        // nearest real point on the mesh instead of trusting the raw cell position - but keep the
        // search radius small. A wide radius (previously 8) could snap a monster clear across a
        // doorway into a different room, which read as monsters spawning "outside" the map.
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) && IsWithinRoom(room, hit.position))
        {
            Instantiate(prefab, hit.position, Quaternion.identity, parent);
        }
        else
        {
            Debug.LogWarning($"{name}: no in-room NavMesh found near {desired} in {room.Root.name}; skipping monster spawn there.");
        }
    }

    // 스냅된 좌표가 이 방의 실제 바닥 범위(2D 바운딩 박스 기준) 안에 있는지 확인한다.
    // NavMesh 스냅이 문틈을 넘어 옆방으로 튀는 걸 막기 위한 마지막 안전장치.
    private static bool IsWithinRoom(RoomInstance room, Vector3 navPosition)
    {
        IntRect bounds = GetFloorBoundsWorld(room);
        float x = navPosition.x, y = navPosition.z;
        return x >= bounds.MinX - 0.5f && x <= bounds.MaxX + 1.5f && y >= bounds.MinY - 0.5f && y <= bounds.MaxY + 1.5f;
    }

    private static Vector2Int PickInteriorCell(RoomInstance room)
    {
        HashSet<Vector2Int> floorSet = new HashSet<Vector2Int>(room.FloorCells);
        HashSet<Vector2Int> obstacleCells = ObstacleCellsByRoom.TryGetValue(room.PrefabName, out Vector2Int[] obs)
            ? new HashSet<Vector2Int>(obs)
            : new HashSet<Vector2Int>();

        List<Vector2Int> interior = new List<Vector2Int>();
        foreach (Vector2Int c in room.FloorCells)
        {
            if (obstacleCells.Contains(c)) continue;
            if (floorSet.Contains(c + Vector2Int.up) && floorSet.Contains(c + Vector2Int.down) &&
                floorSet.Contains(c + Vector2Int.left) && floorSet.Contains(c + Vector2Int.right))
            {
                interior.Add(c);
            }
        }

        if (interior.Count > 0) return interior[Random.Range(0, interior.Count)];
        return PickCentralCell(room);
    }

    // 각 장애물 칸 자리에 보이지 않는 NavMeshObstacle을 심어서, 구워둔 NavMesh 위에
    // 실시간으로 구멍을 뚫는다(carving). 굽는 시점에 메시에서 통째로 빼는 방식보다
    // Unity의 표준 카빙 파이프라인을 타므로 벽 근처에서도 훨씬 안정적으로 뚫린다.
    private static void AddObstacleCarvers(RoomInstance room, Transform parent)
    {
        if (!ObstacleCellsByRoom.TryGetValue(room.PrefabName, out Vector2Int[] cells)) return;

        foreach (Vector2Int c in cells)
        {
            GameObject carver = new GameObject("ObstacleNavCarver");
            carver.transform.SetParent(parent, false);
            carver.transform.position = ToNavPosition(room, c);

            NavMeshObstacle obstacle = carver.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(1f, 1f, 1f);
            obstacle.carveOnlyStationary = false;
            obstacle.carving = true;
        }
    }

    // 방의 실제 바닥 타일 모양 그대로 얇은 3D 메시를 만들어 NavMesh를 구울 지면으로 쓴다.
    // (경계 사각형 대신 실제 타일 모양을 써서 ㄱ자/분리된 방 사이 빈 공간으로 몬스터가
    // 새지 않게 한다.) 장애물 칸은 굽는 시점부터 아예 빼서 첫 프레임부터 확실히 막고,
    // AddObstacleCarvers의 NavMeshObstacle 카빙이 (특히 벽에 붙은 칸처럼 애매한 경우를)
    // 한 번 더 보강한다 - 둘 중 하나만으로는 벽 근처에서 완전히 안 뚫리는 경우가 있었다.
    private static MeshFilter BuildNavGround(RoomInstance room)
    {
        HashSet<Vector2Int> obstacleCells = ObstacleCellsByRoom.TryGetValue(room.PrefabName, out Vector2Int[] obs)
            ? new HashSet<Vector2Int>(obs)
            : new HashSet<Vector2Int>();

        List<Vector3> vertices = new List<Vector3>(room.FloorCells.Count * 4);
        List<int> triangles = new List<int>(room.FloorCells.Count * 6);

        foreach (Vector2Int c in room.FloorCells)
        {
            if (obstacleCells.Contains(c)) continue;

            float x0 = c.x, x1 = c.x + 1f, z0 = c.y, z1 = c.y + 1f;
            int b = vertices.Count;
            vertices.Add(new Vector3(x0, 0f, z0));
            vertices.Add(new Vector3(x0, 0f, z1));
            vertices.Add(new Vector3(x1, 0f, z1));
            vertices.Add(new Vector3(x1, 0f, z0));
            triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
            triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 3);
        }

        GameObject navGround = new GameObject("NavGround");
        navGround.transform.SetParent(room.Root.transform, false);

        Mesh mesh = new Mesh { name = "NavGroundMesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter = navGround.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = navGround.AddComponent<MeshRenderer>();
        renderer.enabled = false;
        return filter;
    }

    // NavMeshSurface's automatic geometry collection (CollectObjects.Children / .Volume) proved
    // unreliable here in testing - it consistently baked only one room's worth of mesh no matter
    // how the collection mode was configured. Building the sources by hand with the lower-level
    // NavMeshBuilder API sidesteps that entirely: each room's NavGround mesh is handed to the
    // baker directly, so nothing needs to be "found" by the auto-collector.
    private static void BakeNavMesh(GameObject mapRoot, List<MeshFilter> navGroundMeshes)
    {
        Bounds bounds = default;
        bool any = false;
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

        foreach (MeshFilter filter in navGroundMeshes)
        {
            if (filter == null || filter.sharedMesh == null) continue;

            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = filter.sharedMesh,
                transform = filter.transform.localToWorldMatrix,
                area = 0
            });

            Bounds worldBounds = filter.sharedMesh.bounds;
            Vector3 c = filter.transform.TransformPoint(worldBounds.center);
            Vector3 e = Vector3.Scale(worldBounds.extents, filter.transform.lossyScale) + Vector3.one;
            if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; }
            bounds.Encapsulate(c - e);
            bounds.Encapsulate(c + e);
        }

        if (!any) return;
        bounds.Expand(2f);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        NavMesh.AddNavMeshData(data);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
