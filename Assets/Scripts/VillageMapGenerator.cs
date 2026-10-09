using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Day가 시작되어 VillageScene이 로드될 때마다 등록된 방 모듈(Tilemap 프리팹) 중 일부를
/// 무작위로 골라 항상 동쪽으로만 한 줄로 딱 붙여 맵을 새로 조립한다 - 방향을 매번
/// 바꾸지 않으므로 절대 겹치거나 이상하게 이어지지 않는다. 플레이어/트럭 지점은 전체
/// 맵의 가로 한가운데에 가장 가까운 방에 스폰된다. 이어붙인 뒤 각 방의 바닥 타일을
/// 기준으로 NavMesh를 다시 구워서, 절차적으로 생성된 위치에서도 몬스터(NavMeshAgent)가
/// 순찰/추격할 수 있게 한다. 방 사이에는 벽이 없어 맵 전체가 하나의 열린 바닥이며, 바닥이 끝나는
/// 가장자리는 보이지 않는 충돌체가 막고 그 바깥은 항상 어둡게 칠해진다. 시야(Light2D)는 그 가장자리에서만
/// 끊기고(BuildWallShadows), 방과 방이 이어지는 통로는 그대로 통과한다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class VillageMapGenerator : MonoBehaviour
{
    [Header("방 모듈 (각 프리팹은 Ground Tilemap이 필요)")]
    [SerializeField] private GameObject[] roomPrefabs;
    [SerializeField] private int minRoomCount = 3;
    [SerializeField] private int maxRoomCount = 4;

    [Header("맵 배치 방식")]
    [Tooltip("Linear: 방을 동쪽으로 한 줄로 이어붙인다(기존 방식). Grid: gridColumns x gridRows 격자에 방을 놓고 길로 잇는다 " +
        "(격자 씬 생성은 아직 구현 전이라 지금은 Grid를 골라도 Linear로 만든다 - 계획은 Tools > Village Rooms > Grid 메뉴로 미리 볼 수 있다).")]
    [SerializeField] private MapLayoutMode layoutMode = MapLayoutMode.Linear;

    [Header("격자 맵 (layoutMode = Grid일 때)")]
    [SerializeField, Min(1)] private int gridColumns = 3;
    [SerializeField, Min(1)] private int gridRows = 4;
    [Tooltip("방과 방을 잇는 길의 폭(칸). 방의 변마다 이 폭의 차선이 들어갈 자리가 있어야 그 변으로 이웃과 이어진다.")]
    [SerializeField, Range(3, 8)] private int corridorWidth = 4;
    [Tooltip("격자 칸 사이 틈(칸). 길이 이 틈 안에서 꺾이므로 corridorWidth + 2 이상으로 둔다.")]
    [SerializeField, Min(0)] private int corridorGap = 6;
    [Tooltip("모든 방을 잇는 최소 연결(스패닝 트리) 밖의 이웃 쌍 중 이 비율만큼 연결을 더해 고리 모양 길을 만든다. 0이면 트리만, 1이면 이웃끼리 전부 연결.")]
    [SerializeField, Range(0f, 1f)] private float extraConnectionRatio = 0.25f;
    [SerializeField] private TruckPlacement truckPlacement = TruckPlacement.Center;
    [Tooltip("트럭이 있는 칸에 놓을 수 있는 방. 트럭 주변을 넓게 비울 수 있고 치워지는 장애물이 적은 방으로 고른다. 비워두면 모든 방을 허용한다.")]
    [SerializeField] private GameObject[] truckRoomPrefabs;
    [Tooltip("방 배정과 연결이 실패했을 때 다시 시도할 횟수.")]
    [SerializeField, Min(1)] private int maxLayoutAttempts = 20;

    [Header("바닥 타일 시각적 변형 (이끼/얼룩 패치) - 비워두면 변형 없이 기본 바닥 타일만 쓴다")]
    [SerializeField] private TileBase[] floorVariantTiles;

    [Header("방마다 무작위 배치할 동물/몬스터")]
    [SerializeField] private GameObject[] animalPrefabs;
    [SerializeField] private int animalsPerRoom = 2;
    [SerializeField] private GameObject[] monsterPrefabs;
    [Tooltip("방 하나에 스폰할 몬스터 수. 방마다 이 수만큼 monsterPrefabs에서 무작위로 뽑는다.")]
    [SerializeField, Min(0)] private int monstersPerRoom = 1;

    [Header("기존 씬 오브젝트 (재배치할 대상)")]
    [SerializeField] private string playerObjectName = "Player";
    [SerializeField] private string truckPointObjectName = "TruckPoint";
    [Tooltip("트럭(캠핑카) 콜라이더 바깥으로 한 변당 이 거리(칸) 안의 방 장애물은 이번 맵에서만 치워서 트럭 주변을 항상 넓게 비워둔다. " +
        "가능하면 이 범위가 전부 바닥인 자리에 트럭을 놓고, 그런 자리가 없는 방에서는 최소 여유 폭(1.1칸) 기준으로 자리를 고른다.")]
    [SerializeField, Min(0f)] private float truckClearanceMarginPerSide = 2.5f;

    [Header("밸런스 설정")]
    [SerializeField] private GameBalanceConfig config;

    [Header("맵 경계 (바닥 타일 바깥에 자동 생성되는 보이지 않는 충돌체, BuildMapBoundary 참고)")]
    [Tooltip("경계 충돌체를 둘 레이어. 동물의 장애물 감지(obstacleMask) 등에 걸리도록 기본값은 Default.")]
    [SerializeField] private string boundaryLayerName = "Default";

    [Header("맵 바깥 어둡게 (BuildOutsideMask 참고)")]
    [Tooltip("조명을 받지 않는 Unlit 머티리얼(Sprite-Unlit-Default). 비워두면 같은 셰이더로 런타임에 만든다.")]
    [SerializeField] private Material outsideMaskMaterial;
    [Tooltip("바닥 타일이 없는 칸(맵 바깥)을 칠할 색. 시야 라이트와 무관하게 항상 이 색으로 보인다.")]
    [SerializeField] private Color outsideMaskColor = Color.black;
    [Tooltip("맵 바닥 범위 바깥으로 마스크를 얼마나 더 넓게 깔지(칸). 카메라가 맵 가장자리에 있어도 화면 끝까지 덮을 만큼.")]
    [SerializeField] private int outsideMaskMargin = 40;
    [Tooltip("맵 가장자리(지나갈 수 있는 바닥의 끝)에서 바깥쪽으로 몇 칸에 걸쳐 점점 어두워질지. 이 범위에는 바닥 타일이 장식으로 더 깔린다(들어갈 수는 없다). 0이면 바닥 경계에서 딱 끊긴다.")]
    [SerializeField, Range(0f, 8f)] private float outsideMaskFadeWidth = 0.5f;
    [Tooltip("바닥 가장자리 안쪽으로 몇 칸 전부터 미리 어두워지기 시작할지. 시야를 막는 벽 그림자(BuildWallShadows)는 바닥 경계에 그대로 있으므로, " +
        "경계에 닿기 전에 바닥이 충분히 어두워져 있으면 부채꼴 시야가 경계에서 칼같이 끊기는 선이 드러나지 않는다. 0이면 경계 바깥에서만 어두워진다. " +
        "부채꼴 시야는 경계 바깥이 그림자라 항상 새까맣기 때문에, 바깥 폭(outsideMaskFadeWidth)을 0으로 두고 이 값만으로 경계에서 완전히 어두워지게 해야 " +
        "경계선이 드러나지 않는다. 마스크는 Floor 레이어에만 그려지므로 캐릭터/장애물은 어두워지지 않는다.")]
    [SerializeField, Range(0f, 4f)] private float outsideMaskInnerFadeWidth = 2f;
    [Tooltip("그라데이션을 칸마다 몇 조각으로 나눠 그릴지. 클수록 부드럽지만 정점이 늘어난다.")]
    [SerializeField, Range(1, 8)] private int outsideMaskFadeSubdivisions = 4;
    [Tooltip("마스크는 바닥(Floor) 위, 캐릭터/장애물(Default) 아래에 그려진다.")]
    [SerializeField] private string outsideMaskSortingLayer = "Floor";
    [SerializeField] private int outsideMaskSortingOrder = 100;
    [SerializeField] private string floorSortingLayer = "Floor";

    private float MonsterMinSpawnDistanceFromPlayer => config != null ? config.monsterMinSpawnDistanceFromPlayer : 6f;
    private float FloorPatchNoiseScale => config != null ? config.floorPatchNoiseScale : 0.15f;
    private float FloorPatchThreshold => config != null ? config.floorPatchThreshold : 0.62f;

    // AnimalRescue.Id(계층 경로 기반)를 실제로 Instantiate하기 전에 미리 계산하기 위한 맵 루트 이름.
    // mapRoot 생성 시 이름과 반드시 같아야 한다.
    private const string MapRootName = "GeneratedMap";

    private class RoomInstance
    {
        public GameObject Root;
        public string PrefabName;
        public List<Vector2Int> FloorCells;
        // 방 프리팹에 배치된 장애물(트리거가 아닌 2D 충돌체)과 그것들이 차지하는 로컬 칸. GetRoomObstacles 참고.
        public List<RoomObstacle> Obstacles;
        public HashSet<Vector2Int> ObstacleCells;
    }

    private class RoomObstacle
    {
        public Collider2D Collider;
        public List<Vector2Int> Cells;
        public readonly List<GameObject> NavCarvers = new List<GameObject>();
    }

    // 이번 맵 생성의 단계별 소요 시간(ms, 생성 순서대로). 측정용 기록일 뿐 생성 결과나 Random 순서에는 영향이 없다 -
    // VillageMapGenerationTest가 읽어 보고서에 남긴다.
    public List<KeyValuePair<string, double>> LastGenerationTimings { get; } = new List<KeyValuePair<string, double>>();
    private System.Diagnostics.Stopwatch generationWatch;
    private double generationLastMarkMs;

    private void MarkGenerationStep(string step)
    {
        double now = generationWatch.Elapsed.TotalMilliseconds;
        LastGenerationTimings.Add(new KeyValuePair<string, double>(step, now - generationLastMarkMs));
        generationLastMarkMs = now;
    }

    private void Awake()
    {
        LastGenerationTimings.Clear();
        generationWatch = System.Diagnostics.Stopwatch.StartNew();
        generationLastMarkMs = 0;

        if (layoutMode == MapLayoutMode.Grid)
        {
            Debug.LogWarning($"{name}: 격자 배치(Grid)는 아직 씬 생성이 구현되지 않아 이번에는 한 줄 배치(Linear)로 만든다.");
        }

        // Reuse the same layout every time VillageScene is re-entered on the same Day (e.g. after
        // a truck-scene round trip); only roll a new one when GameManager reports a new Day.
        if (GameManager.Instance != null)
        {
            Random.InitState(GameManager.Instance.GetVillageMapSeedForToday());
        }

        // Inspector에서 비어 있는 칸이나 바닥(Ground)이 없는 방 프리팹이 섞여 있으면 생성 도중 예외로 맵이 반쯤
        // 만들어진 채 멈춘다 - 쓸 수 없는 항목은 경고만 남기고 이번 생성에서 뺀다(에셋은 건드리지 않는다).
        roomPrefabs = RemoveUnusablePrefabs(roomPrefabs, nameof(roomPrefabs), prefab => GetFloorCells(prefab).Count > 0, "바닥(Ground) 타일이 없다");
        animalPrefabs = RemoveUnusablePrefabs(animalPrefabs, nameof(animalPrefabs));
        monsterPrefabs = RemoveUnusablePrefabs(monsterPrefabs, nameof(monsterPrefabs));

        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            Debug.LogError($"{name}: no usable room prefabs assigned - cannot generate the village map.");
            return;
        }

        MarkGenerationStep("준비");

        GameObject mapRoot = new GameObject(MapRootName);
        List<RoomInstance> placedRooms = BuildRoomChain(mapRoot.transform);
        if (placedRooms.Count == 0) return;
        MarkGenerationStep("방 배치");

        // 방 프리팹에는 벽이 없고 바닥(Ground)만 있다. 방들은 가장자리끼리 딱 붙어 하나의 이어진 바닥이 되며,
        // 그 바깥은 보이지 않는 경계 충돌체가 막고(BuildMapBoundary) 항상 어둡게 칠해진다(BuildOutsideMask).
        HashSet<Vector2Int> mapFloor = new HashSet<Vector2Int>();
        List<MeshFilter> navGroundMeshes = new List<MeshFilter>();
        foreach (RoomInstance room in placedRooms)
        {
            ApplyFloorVariantPatches(room);
            navGroundMeshes.Add(BuildNavGround(room));
            foreach (Vector2Int c in room.FloorCells) mapFloor.Add(ToWorldCell(room, c));

            TilemapRenderer groundRenderer = room.Root.transform.Find("Ground")?.GetComponent<TilemapRenderer>();
            if (groundRenderer != null) groundRenderer.sortingLayerName = floorSortingLayer;
        }
        MarkGenerationStep("바닥 패치·NavGround");
        BakeNavMesh(mapRoot, navGroundMeshes);
        MarkGenerationStep("NavMesh 굽기");

        foreach (RoomInstance room in placedRooms)
        {
            AddObstacleCarvers(room, mapRoot.transform);
        }
        MarkGenerationStep("장애물 카버");

        BuildMapBoundary(mapRoot.transform, mapFloor);
        MarkGenerationStep("경계 충돌체");
        BuildWallShadows(mapRoot.transform, WithObstacleOverhang(mapFloor, placedRooms));
        MarkGenerationStep("벽 그림자");
        BuildOutsideMask(mapRoot.transform, mapFloor, placedRooms);
        MarkGenerationStep("바깥 마스크");

        // Obstacle colliders baked into the room prefabs (and the boundary just built) are live at
        // this point, so make sure Physics2D sees their current transforms before we probe for clear spots.
        Physics2D.SyncTransforms();

        RoomInstance centerRoom = FindCenterRoom(placedRooms);
        Vector2Int spawnCell = PickSafeCell(centerRoom, null) ?? PickCentralCell(centerRoom);
        PositionExistingObject(playerObjectName, ToSpritePosition(centerRoom, spawnCell));

        // 트럭 지점은 1칸짜리 플레이어와 달리 부피가 훨씬 크므로(6배 스케일 모델), 트럭의 실제 콜라이더 크기만큼
        // 장애물/맵 경계와 겹치지 않는 칸을 고른다. 주변 여유 폭(truckClearanceMarginPerSide)까지 전부 바닥인 칸을 먼저 찾고,
        // 없으면 최소 여유 폭(MinTruckClearanceMarginPerSide)으로, 그다음 트럭 자체만 안 겹치는 칸으로, 그래도 없으면
        // 일반 PickSafeCell로 물러난다.
        Vector2 truckFootprint = GetTruckFootprintSize();
        Vector2 truckClearance = truckFootprint + Vector2.one * (truckClearanceMarginPerSide * 2f);
        Vector2 minTruckClearance = truckFootprint + Vector2.one * (MinTruckClearanceMarginPerSide * 2f);
        Vector2Int truckCell = PickSafeCellForFootprint(centerRoom, spawnCell, truckFootprint, mapFloor, truckClearance)
            ?? PickSafeCellForFootprint(centerRoom, spawnCell, truckFootprint, mapFloor, minTruckClearance)
            ?? PickSafeCellForFootprint(centerRoom, spawnCell, truckFootprint, null, Vector2.zero)
            ?? PickSafeCell(centerRoom, spawnCell)
            ?? spawnCell;
        Vector2 truckXY = ToWorldXY(centerRoom, truckCell);
        PositionExistingObject(truckPointObjectName, new Vector3(truckXY.x, truckXY.y, 0f));

        // 트럭 주변은 항상 넓게 비운다 - 여유 폭 안의 장애물은 이 맵 인스턴스에 한해서 치운다(원본 방 프리팹은 그대로 둔다).
        // 치운 자리는 처음 구운 NavMesh에 구멍으로 남아 있으므로 다시 굽는다.
        if (EnsureTruckClearance(placedRooms, truckXY, truckClearance)) RebakeNavMesh(mapRoot, placedRooms);
        AddTruckNavCarver(mapRoot.transform, truckXY, truckFootprint);

        // 플레이어 스폰 칸을 트럭보다 먼저 골랐으므로, 트럭이 플레이어를 덮었으면 트럭 밖의 안전한 칸으로 옮긴다
        // (트럭 콜라이더가 이제 그 자리를 막고 있으므로 PickSafeCell의 겹침 검사가 트럭을 피한다).
        Physics2D.SyncTransforms();
        Vector2 playerXY = ToWorldXY(centerRoom, spawnCell);
        if (Mathf.Abs(playerXY.x - truckXY.x) < truckFootprint.x * 0.5f + 0.5f && Mathf.Abs(playerXY.y - truckXY.y) < truckFootprint.y * 0.5f + 0.5f)
        {
            spawnCell = PickSafeCell(centerRoom, spawnCell) ?? spawnCell;
            PositionExistingObject(playerObjectName, ToSpritePosition(centerRoom, spawnCell));
        }

        // 플레이어 칸과 트럭이 덮는 칸(+ 둘레 TruckSpawnGap)에는 몬스터나 동물이 스폰되지 않도록 제외한다.
        HashSet<Vector2Int> playerAndTruckWorldCells = new HashSet<Vector2Int>(CellsUnderBox(truckXY, truckFootprint + Vector2.one * (TruckSpawnGap * 2f)))
        {
            ToWorldCell(centerRoom, spawnCell)
        };
        Vector2 playerSpawnWorldXY = ToWorldXY(centerRoom, spawnCell);
        MarkGenerationStep("플레이어·트럭 배치");

        foreach (RoomInstance room in placedRooms)
        {
            // 이 방에서 이미 차지된 칸(플레이어/트럭 + 먼저 스폰한 동물/몬스터, 방 로컬 칸). 동물과 몬스터가 같은 칸에 겹쳐
            // 스폰되지 않도록 둘이 함께 쓴다. 트럭이 이웃 방까지 걸쳐 있을 수 있으므로 모든 방에 적용한다.
            Vector2Int roomOffset = ToWorldCell(room, Vector2Int.zero);
            HashSet<Vector2Int> usedCells = new HashSet<Vector2Int>(playerAndTruckWorldCells.Select(c => c - roomOffset));

            // Parented under each room (not the shared mapRoot) so AnimalRescue's hierarchy-path Id
            // stays unique per room - rooms never repeat a prefab, but animalPrefabs can be picked
            // for more than one room, and a shared parent would give those clones identical Ids.
            SpawnAnimal(room, room.Root.transform, usedCells);
            SpawnMonster(room, room.Root.transform, playerSpawnWorldXY, usedCells);
        }
        MarkGenerationStep("동물·몬스터 스폰");
    }

    /// <summary>
    /// 이 생성기의 방 목록과 격자 설정으로 GridLayoutPlanner 입력을 만든다. 같은 프리팹이 목록에 두 번 있어도 종류는 하나로 센다.
    /// 에디터 도구(Grid Preview/Stress Test)가 씬을 열어 부르며, 맵을 만들지 않고 Random도 쓰지 않는다.
    /// </summary>
    public GridPlanInput CreateGridPlanInput()
    {
        GridPlanInput input = new GridPlanInput
        {
            Settings = new GridPlannerSettings
            {
                Columns = gridColumns,
                Rows = gridRows,
                CorridorWidth = corridorWidth,
                CorridorGap = corridorGap,
                ExtraConnectionRatio = extraConnectionRatio,
                TruckPlacement = truckPlacement,
                MaxAttempts = maxLayoutAttempts,
            }
        };
        HashSet<GameObject> truckRooms = new HashSet<GameObject>((truckRoomPrefabs ?? new GameObject[0]).Where(p => p != null));
        HashSet<GameObject> seen = new HashSet<GameObject>();
        foreach (GameObject prefab in roomPrefabs ?? new GameObject[0])
        {
            if (prefab == null || !seen.Add(prefab)) continue;
            GridRoomType type = GridRoomAnalyzer.Analyze(prefab, corridorWidth, truckRooms.Count == 0 || truckRooms.Contains(prefab));
            if (type.FloorCells.Count > 0) input.Types.Add(type);
        }
        return input;
    }

    private GameObject[] RemoveUnusablePrefabs(GameObject[] prefabs, string fieldName, System.Func<GameObject, bool> isUsable = null, string reason = null)
    {
        if (prefabs == null) return new GameObject[0];
        List<GameObject> usable = new List<GameObject>(prefabs.Length);
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null)
            {
                Debug.LogWarning($"{name}: {fieldName}[{i}]이 비어 있어 건너뛴다.");
            }
            else if (isUsable != null && !isUsable(prefab))
            {
                Debug.LogWarning($"{name}: {fieldName}[{i}] '{prefab.name}' - {reason} - 건너뛴다.");
            }
            else
            {
                usable.Add(prefab);
            }
        }
        return usable.ToArray();
    }

    // 각 방 프리팹에 미리 배치해둔 장애물(상자/조각상/항아리/묘비/바위 등)이 차지하는 칸은 코드에 따로 적어두지 않고,
    // 방을 맵에 배치할 때 프리팹 안의 2D 충돌체에서 직접 읽는다(GetRoomObstacles). NavMesh를 구울 때 이 칸들은 바닥에서
    // 빼서 구멍으로 남겨두기 때문에, 장애물은 2D 충돌체뿐 아니라 NavMeshAgent(몬스터) 경로에서도 실제로 피해 다녀야 할
    // 장애물이 된다. 나무/덤불처럼 충돌체가 없는 장식은 자연히 빠지므로 NavMesh에서도 평범한 바닥으로 남는다.
    // 새 방 프리팹은 장애물에 BoxCollider2D(트리거 아님)만 붙여두면 되고 이 스크립트를 고칠 필요가 없다.

    private struct IntRect
    {
        public int MinX, MaxX, MinY, MaxY;
    }

    // 방을 순서대로 만들면서 매번 직전 방의 바운딩 박스 바로 동쪽에 딱 붙는 위치를 계산해
    // 그 자리에 Instantiate한다. 항상 같은 방향(동쪽)으로만 한 줄로 이어붙이기 때문에
    // 절대 서로 겹치지 않고 항상 깔끔하게 이어진다. 방 사이에 벽이 없으므로 맞닿은 가장자리
    // 바닥끼리 그대로 이어져 하나의 열린 공간이 된다.
    private List<RoomInstance> BuildRoomChain(Transform mapRoot)
    {
        List<GameObject> pool = new List<GameObject>(roomPrefabs);
        Shuffle(pool);

        int count = Mathf.Clamp(Random.Range(minRoomCount, maxRoomCount + 1), 1, pool.Count);
        List<GameObject> chain = OrderForDoorwayCompatibility(pool.GetRange(0, count));
        List<RoomInstance> placed = new List<RoomInstance>();

        for (int i = 0; i < chain.Count; i++)
        {
            GameObject prefab = chain[i];
            string prefabName = prefab.name;

            GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, mapRoot);
            List<RoomObstacle> obstacles = GetRoomObstacles(instance);
            RoomInstance room = new RoomInstance
            {
                Root = instance,
                PrefabName = prefabName,
                FloorCells = GetFloorCells(instance),
                Obstacles = obstacles,
                ObstacleCells = new HashSet<Vector2Int>(obstacles.SelectMany(o => o.Cells))
            };

            if (placed.Count > 0)
            {
                RoomInstance previous = placed[placed.Count - 1];
                PlaceAdjacent(previous, room);
            }

            placed.Add(room);
        }

        return placed;
    }

    // NavMesh는 바닥 가장자리에서 에이전트 반경(NavMeshAreas의 agentRadius, 기본 0.5)만큼 안쪽으로 침식되므로,
    // 두 방이 맞닿는 줄이 2줄 이하면 침식 후 실제 통행 가능한 폭이 1.0 이하로 남아 몬스터가 그 연결부를
    // 절대 못 넘어가는 경우가 생긴다(ㄱ자/분리된 방처럼 가장자리 바닥 모양이 서로 잘 안 맞는 조합일
    // 때 특히 그렇다). 무작위로 고른 방들을 순서 그대로 이어붙이기 전에, 방 개수가 몇 안 되는 점을
    // 이용해 가능한 순열을 전부 시도해서 "가장 좁은 연결부가 가장 넓어지는"(bottleneck 최댓값) 배치를
    // 고른다 - 그리디하게 직전 방과만 비교하며 앞에서 좋은 조합을 다 써버리면, 남는 두 방이 최악의
    // 조합으로 묶여 마지막 연결부만 좁아지는 경우가 있었다(A-B, C-D는 잘 맞지만 B-C는 안 맞을 때 등).
    private const int MinSafeDoorwayWidth = 3;

    // 방 개수가 이 값을 넘으면 순열 탐색(n!)이 비싸지므로 예전의 그리디 방식으로 대체한다.
    // 현재 방 프리팹은 4종뿐이라 실질적으로는 항상 순열 탐색이 쓰인다.
    private const int ExhaustiveOrderSearchMaxRooms = 7;

    private static List<GameObject> OrderForDoorwayCompatibility(List<GameObject> rooms)
    {
        if (rooms.Count <= 1) return rooms;
        if (rooms.Count > ExhaustiveOrderSearchMaxRooms) return GreedyOrderForDoorwayCompatibility(rooms);

        List<GameObject> bestOrder = new List<GameObject>(rooms);
        int bestMinOverlap = MinOverlapAlongChain(bestOrder);

        SearchBestOrder(rooms, new bool[rooms.Count], new List<GameObject>(rooms.Count), ref bestOrder, ref bestMinOverlap);

        if (bestMinOverlap < MinSafeDoorwayWidth)
        {
            Debug.LogWarning($"VillageMapGenerator: even the best possible room ordering for this combination leaves a " +
                $"connector only {bestMinOverlap} tile(s) wide (target: {MinSafeDoorwayWidth}+). Monsters may not be able " +
                "to cross it.");
        }

        return bestOrder;
    }

    private static void SearchBestOrder(List<GameObject> pool, bool[] used, List<GameObject> current, ref List<GameObject> bestOrder, ref int bestMinOverlap)
    {
        if (current.Count == pool.Count)
        {
            int minOverlap = MinOverlapAlongChain(current);
            if (minOverlap > bestMinOverlap)
            {
                bestMinOverlap = minOverlap;
                bestOrder = new List<GameObject>(current);
            }
            return;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            if (used[i]) continue;

            used[i] = true;
            current.Add(pool[i]);
            SearchBestOrder(pool, used, current, ref bestOrder, ref bestMinOverlap);
            current.RemoveAt(current.Count - 1);
            used[i] = false;
        }
    }

    private static int MinOverlapAlongChain(List<GameObject> chain)
    {
        if (chain.Count <= 1) return int.MaxValue;
        int min = int.MaxValue;
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            min = Mathf.Min(min, BestEdgeOverlap(chain[i], chain[i + 1]));
        }
        return min;
    }

    private static List<GameObject> GreedyOrderForDoorwayCompatibility(List<GameObject> rooms)
    {
        List<GameObject> remaining = new List<GameObject>(rooms);
        List<GameObject> ordered = new List<GameObject> { remaining[0] };
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            GameObject last = ordered[ordered.Count - 1];
            int bestIndex = 0;
            int bestOverlap = -1;
            for (int i = 0; i < remaining.Count; i++)
            {
                int overlap = BestEdgeOverlap(last, remaining[i]);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestIndex = i;
                }
            }

            if (bestOverlap < MinSafeDoorwayWidth)
            {
                Debug.LogWarning($"VillageMapGenerator: could not find a doorway-compatible next room for " +
                    $"'{last.name}' among the remaining pool (best possible overlap: {bestOverlap} tile(s)). " +
                    "Using the best available match - this connector may be too narrow to cross reliably.");
            }

            ordered.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }

        return ordered;
    }

    // 아직 배치하지 않은 두 프리팹(원본 에셋 참조)만으로, 실제로 동-서로 이어붙였을 때 두 가장자리가
    // 겹칠 수 있는 최대 줄 수를 미리 계산한다. Ground 타일맵 모양은 인스턴스화하거나 배치해도 바뀌지
    // 않으므로, 이 값은 실제로 이어붙였을 때 두 방 바닥이 맞닿는
    // 줄 수와 정확히 같다.
    private static int BestEdgeOverlap(GameObject prevPrefab, GameObject nextPrefab)
    {
        List<Vector2Int> prevCells = GetFloorCells(prevPrefab);
        List<Vector2Int> nextCells = GetFloorCells(nextPrefab);
        if (prevCells.Count == 0 || nextCells.Count == 0) return 0;

        IntRect prevBounds = GetFloorBoundsLocal(prevCells);
        IntRect nextBounds = GetFloorBoundsLocal(nextCells);

        List<int> prevEdgeYs = GetEdgeRows(prevCells, prevBounds.MaxX);
        List<int> nextEdgeYs = GetEdgeRows(nextCells, nextBounds.MinX);

        HashSet<int> prevSet = new HashSet<int>(prevEdgeYs);
        HashSet<int> candidateOffsets = new HashSet<int>();
        foreach (int py in prevEdgeYs)
        {
            foreach (int ny in nextEdgeYs) candidateOffsets.Add(py - ny);
        }

        int best = 0;
        foreach (int offset in candidateOffsets)
        {
            int overlap = 0;
            foreach (int ny in nextEdgeYs)
            {
                if (prevSet.Contains(ny + offset)) overlap++;
            }
            if (overlap > best) best = overlap;
        }
        return best;
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

    // 바닥 전체에 변형 타일을 칸마다 독립적으로(예: 10% 확률) 뿌리면 소금을 뿌린 것처럼
    // 지저분해 보인다. 대신 Perlin Noise 지형에서 일정 임계값(threshold) 이상인 "봉우리"
    // 부분만 잘라내서 칠하면, 그 봉우리 주변 칸들이 자연스럽게 뭉친 패치(이끼/얼룩 덩어리)로
    // 나타난다 - 이웃한 칸끼리는 노이즈 값이 비슷하므로 패치 경계도 매끄럽다.
    // 변형 타일 종류가 여러 개일 때 칸마다 무작위로 종류를 섞으면 한 패치 안에서도 얼룩덜룩
    // 섞여 보이므로, 훨씬 낮은 주파수(noiseScale의 1/4)로 샘플링하는 두 번째 노이즈 필드로
    // "이 패치가 어떤 변형 타일을 쓸지"를 정한다 - 저주파 노이즈는 넓은 영역에 걸쳐 완만하게
    // 변하므로, 같은 패치에 속한 칸들은 거의 항상 같은 변형 타일 종류로 통일된다.
    // offsetX/Y는 Awake()에서 이미 이번 Day의 시드로 초기화된 Random에서 뽑으므로, 패치
    // 위치는 매 Day(또는 방 재생성)마다 달라지고 같은 Day 안에서는 항상 같다.
    private void ApplyFloorVariantPatches(RoomInstance room)
    {
        if (floorVariantTiles == null || floorVariantTiles.Length == 0) return;

        Transform groundTransform = room.Root.transform.Find("Ground");
        Tilemap ground = groundTransform != null ? groundTransform.GetComponent<Tilemap>() : null;
        if (ground == null) return;

        float noiseScale = FloorPatchNoiseScale;
        float threshold = FloorPatchThreshold;
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);
        float variantOffsetX = Random.Range(0f, 1000f);
        float variantOffsetY = Random.Range(0f, 1000f);
        float variantNoiseScale = noiseScale * 0.25f;

        foreach (Vector2Int cell in room.FloorCells)
        {
            float patchNoise = Mathf.PerlinNoise(offsetX + cell.x * noiseScale, offsetY + cell.y * noiseScale);
            if (patchNoise < threshold) continue;

            float variantNoise = Mathf.PerlinNoise(variantOffsetX + cell.x * variantNoiseScale, variantOffsetY + cell.y * variantNoiseScale);
            int variantIndex = Mathf.Clamp(Mathf.FloorToInt(variantNoise * floorVariantTiles.Length), 0, floorVariantTiles.Length - 1);

            ground.SetTile(new Vector3Int(cell.x, cell.y, 0), floorVariantTiles[variantIndex]);
        }
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

    // 방 프리팹 안의 장애물 = 트리거가 아닌 2D 충돌체(바닥 Tilemap의 충돌체는 제외). 방을 원점에 막 Instantiate한
    // 직후(PlaceAdjacent로 옮기기 전)에 불러야 충돌체 범위가 곧 방 로컬 좌표가 된다. 충돌체 범위가 걸치는 칸을 모두
    // 장애물 칸으로 보므로 1칸보다 큰 장애물도 그대로 처리된다(경계에 딱 닿기만 한 칸은 넣지 않는다).
    private static List<RoomObstacle> GetRoomObstacles(GameObject roomInstance)
    {
        Physics2D.SyncTransforms();
        Vector3 origin = roomInstance.transform.position;
        const float edgeEpsilon = 0.01f;

        List<RoomObstacle> obstacles = new List<RoomObstacle>();
        foreach (Collider2D col in roomInstance.GetComponentsInChildren<Collider2D>())
        {
            if (!col.enabled || col.isTrigger || col is TilemapCollider2D || col is CompositeCollider2D) continue;

            Bounds b = col.bounds;
            int minX = Mathf.FloorToInt(b.min.x - origin.x + edgeEpsilon), maxX = Mathf.FloorToInt(b.max.x - origin.x - edgeEpsilon);
            int minY = Mathf.FloorToInt(b.min.y - origin.y + edgeEpsilon), maxY = Mathf.FloorToInt(b.max.y - origin.y - edgeEpsilon);
            List<Vector2Int> cells = new List<Vector2Int>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++) cells.Add(new Vector2Int(x, y));
            }
            obstacles.Add(new RoomObstacle { Collider = col, Cells = cells });
        }
        return obstacles;
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
    // 맵 가장자리에 바로 붙은 칸(PickInteriorCell과 동일 기준)과, 장애물/경계 콜라이더와 겹치는 칸을
    // 모두 피해서 방 한가운데에 가까운 순으로 고르므로, 스폰하자마자 장애물에 끼거나
    // 가장자리에 바짝 붙어 있는 일이 없다. 안전한 칸이 전혀 없으면 null.
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

    // 트럭 지점처럼 1칸을 훨씬 넘는 부피를 가진 오브젝트용 - PickSafeCell의 고정 0.9x0.9 체크 대신
    // 실제 footprintSize만큼의 박스로 겹침을 확인한다. 그만큼 큰 박스는 맵 가장자리 칸에서 경계 충돌체와
    // 자연히 겹치므로, PickSafeCell처럼 "이웃 네 칸이 모두 바닥인지"를 따로 확인할 필요가 없다.
    // mapFloor가 주어지면 clearanceSize 박스가 덮는 칸이 전부 바닥인 칸만 받아들인다(주변 통행 여유 폭 확보).
    private static Vector2Int? PickSafeCellForFootprint(RoomInstance room, Vector2Int? avoid, Vector2 footprintSize,
        HashSet<Vector2Int> mapFloor, Vector2 clearanceSize)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();
        foreach (Vector2Int c in room.FloorCells)
        {
            if (avoid.HasValue && c == avoid.Value) continue;

            Vector2 worldCenter = ToWorldXY(room, c);
            if (Physics2D.OverlapBox(worldCenter, footprintSize, 0f) != null) continue;
            if (mapFloor != null && !IsAreaAllFloor(mapFloor, worldCenter, clearanceSize)) continue;

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

    private static bool IsAreaAllFloor(HashSet<Vector2Int> mapFloor, Vector2 worldCenter, Vector2 size)
    {
        Vector2 min = worldCenter - size * 0.5f;
        Vector2 max = worldCenter + size * 0.5f;
        for (int y = Mathf.FloorToInt(min.y); y < Mathf.CeilToInt(max.y); y++)
        {
            for (int x = Mathf.FloorToInt(min.x); x < Mathf.CeilToInt(max.x); x++)
            {
                if (!mapFloor.Contains(new Vector2Int(x, y))) return false;
            }
        }
        return true;
    }

    // 트럭 지점의 실제 콜라이더 크기(스케일 반영)를 읽어와서 그 부피만큼 여유가 있는 칸에만
    // 스폰되게 한다 - 트럭 크기나 콜라이더를 Inspector에서 나중에 또 조정해도 이 계산이 항상
    // 실제 값을 그대로 따라가므로, 스폰 로직을 다시 손볼 필요가 없다.
    private Vector2 GetTruckFootprintSize()
    {
        GameObject truckPoint = GameObject.Find(truckPointObjectName);
        BoxCollider2D box = truckPoint != null ? truckPoint.GetComponent<BoxCollider2D>() : null;
        if (box == null) return new Vector2(0.9f, 0.9f);

        Vector2 scale = truckPoint.transform.lossyScale;
        return new Vector2(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y));
    }

    // 트럭 주변을 넓게 비울 수 없는 방(좁은 방)에서 물러날 최소 여유 폭 - 플레이어가 몸통 폭만큼 트럭 옆을 지나다닐 수 있는 정도.
    private const float MinTruckClearanceMarginPerSide = 1.1f;

    // 동물/몬스터를 트럭 콜라이더에서 최소 이만큼(칸) 떨어진 칸에만 스폰한다 - 트럭 안이나 바로 옆에 끼어 나타나지 않게.
    private const float TruckSpawnGap = 1f;

    // 트럭 주변 여유 폭 안에 있는 장애물은 이 맵 인스턴스에서만 치운다(원본 방 프리팹은 그대로 유지).
    // 몬스터가 그 자리를 피해 다니도록 심어둔 NavMesh 카버도 짝을 맞춰 같이 지우고, 방의 장애물 목록에서도 빼서
    // 뒤이은 스폰/NavMesh 재생성이 치운 자리를 평범한 바닥으로 다루게 한다. 하나라도 치웠으면 true.
    // 방 장애물 목록(GetRoomObstacles)에 있는 충돌체만 치우므로 맵 경계·트럭·캐릭터 같은 다른 충돌체는 건드리지 않는다.
    private static bool EnsureTruckClearance(List<RoomInstance> rooms, Vector2 truckWorldCenter, Vector2 clearance)
    {
        Dictionary<Collider2D, (RoomInstance room, RoomObstacle obstacle)> obstacleByCollider = new Dictionary<Collider2D, (RoomInstance, RoomObstacle)>();
        foreach (RoomInstance room in rooms)
        {
            foreach (RoomObstacle obstacle in room.Obstacles) obstacleByCollider[obstacle.Collider] = (room, obstacle);
        }

        HashSet<RoomInstance> changedRooms = new HashSet<RoomInstance>();
        Collider2D[] hits = Physics2D.OverlapBoxAll(truckWorldCenter, clearance, 0f);
        foreach (Collider2D hit in hits)
        {
            if (hit == null || !obstacleByCollider.TryGetValue(hit, out var entry)) continue;

            // Destroy는 프레임 끝에 일어나므로, 같은 프레임의 다음 겹침 검사(플레이어 재배치 등)에서 바로 빠지도록 충돌체부터 끈다.
            hit.enabled = false;
            Destroy(hit.gameObject);
            foreach (GameObject carver in entry.obstacle.NavCarvers)
            {
                if (carver != null) Destroy(carver);
            }
            entry.obstacle.NavCarvers.Clear();
            entry.room.Obstacles.Remove(entry.obstacle);
            changedRooms.Add(entry.room);
        }

        foreach (RoomInstance room in changedRooms)
        {
            room.ObstacleCells = new HashSet<Vector2Int>(room.Obstacles.SelectMany(o => o.Cells));
        }
        if (changedRooms.Count > 0)
        {
            Debug.Log($"VillageMapGenerator: 트럭 주변을 비우기 위해 장애물 {hits.Count(h => h != null && obstacleByCollider.ContainsKey(h))}개를 이번 맵에서만 제거했습니다.");
        }
        return changedRooms.Count > 0;
    }

    // 몬스터(NavMeshAgent)는 2D 충돌체를 모르기 때문에, 트럭 콜라이더와 같은 크기로 NavMesh를 파내서 플레이어처럼 트럭을 돌아가게 한다.
    // 카빙은 에이전트 반경만큼 더 넓게 파이므로 몬스터 몸이 트럭에 겹치지도 않는다.
    private static void AddTruckNavCarver(Transform parent, Vector2 truckWorldCenter, Vector2 footprint)
    {
        GameObject carver = new GameObject("TruckNavCarver");
        carver.transform.SetParent(parent, false);
        carver.transform.position = new Vector3(truckWorldCenter.x, 0f, truckWorldCenter.y);
        NavMeshObstacle obstacle = carver.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(footprint.x, 1f, footprint.y);
        obstacle.carveOnlyStationary = true;
        obstacle.carving = true;
    }

    // 장애물을 치운 칸은 NavMesh를 처음 구울 때 구멍으로 빠져 있었다(BuildNavGround) - 바닥 메시를 다시 만들어 다시 굽는다.
    private static void RebakeNavMesh(GameObject mapRoot, List<RoomInstance> rooms)
    {
        List<MeshFilter> meshes = new List<MeshFilter>();
        foreach (RoomInstance room in rooms)
        {
            Transform old = room.Root.transform.Find("NavGround");
            if (old != null)
            {
                old.name = "NavGround (replaced)";
                MeshFilter oldFilter = old.GetComponent<MeshFilter>();
                if (oldFilter != null && oldFilter.sharedMesh != null) Destroy(oldFilter.sharedMesh);
                Destroy(old.gameObject);
            }
            meshes.Add(BuildNavGround(room));
        }
        BakeNavMesh(mapRoot, meshes);
    }

    // 월드 좌표 상자가 덮는 칸(월드 칸 좌표). 경계에 딱 닿기만 한 칸은 넣지 않는다.
    private static IEnumerable<Vector2Int> CellsUnderBox(Vector2 center, Vector2 size)
    {
        const float edgeEpsilon = 0.01f;
        int minX = Mathf.FloorToInt(center.x - size.x * 0.5f + edgeEpsilon), maxX = Mathf.FloorToInt(center.x + size.x * 0.5f - edgeEpsilon);
        int minY = Mathf.FloorToInt(center.y - size.y * 0.5f + edgeEpsilon), maxY = Mathf.FloorToInt(center.y + size.y * 0.5f - edgeEpsilon);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++) yield return new Vector2Int(x, y);
        }
    }

    // 바닥 칸과 상하좌우·대각선으로 맞닿은 바닥 아닌 칸(맵 바깥)마다 보이지 않는 충돌체를 둬서, 플레이어/동물이
    // 바닥 밖으로 나가지 못하게 한다(몬스터는 바닥 모양으로 구운 NavMesh 밖으로 원래 못 나간다). 스프라이트도
    // ShadowCaster2D도 없는 순수 충돌체라 화면에 보이지 않는다(시야를 막는 그림자는 같은 경계를 따라 BuildWallShadows가 따로 만든다). 칸마다 상자를 따로 두면
    // 상자 이음매에 캐릭터가 걸릴 수 있으므로, 맞닿은 칸을 가능한 큰 직사각형으로 합쳐서 상자 수를 줄인다.
    // 맵을 새로 만들 때마다(Day가 바뀌어 VillageScene을 다시 불러올 때마다) 바닥 범위를 기준으로 다시 만들어진다.
    private void BuildMapBoundary(Transform mapRoot, HashSet<Vector2Int> mapFloor)
    {
        HashSet<Vector2Int> edgeCells = new HashSet<Vector2Int>();
        foreach (Vector2Int c in mapFloor)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2Int n = new Vector2Int(c.x + dx, c.y + dy);
                    if (!mapFloor.Contains(n)) edgeCells.Add(n);
                }
            }
        }

        GameObject boundary = new GameObject("MapBoundary");
        boundary.transform.SetParent(mapRoot, false);
        int layer = LayerMask.NameToLayer(boundaryLayerName);
        if (layer >= 0) boundary.layer = layer;

        foreach (RectInt rect in MergeCellsIntoRects(edgeCells))
        {
            BoxCollider2D box = boundary.AddComponent<BoxCollider2D>();
            box.offset = rect.center;
            box.size = rect.size;
        }
    }

    // 시야를 막는 벽 그림자. 이동 불가 구역(경계 충돌체가 놓인 바닥 아닌 칸)이 바닥과 맞닿는 타일 경계 - 즉 경계 충돌체의
    // 안쪽 면 - 을 FloorOutline으로 한 번에 추적해서, 이어진 외곽선(루프)마다 ShadowCaster2D 하나를 둔다. 바닥끼리 맞닿은
    // 방 사이 통로에는 선이 생기지 않으므로 시야 콘이 그대로 건너편까지 닿는다. 예전 벽처럼 칸마다 캐스터를 붙이면 칸
    // 이음매/모서리마다 부드러운 그림자 쐐기가 생겨 빛이 새거나 같은 벽이 통째로 밝아졌는데, 루프 하나로 이어진 선분은
    // 모서리에서 끊기지 않아 그런 틈이 없다. 장애물(Obstacle 프리팹)의 ShadowCaster2D는 건드리지 않는다.
    // 캐스터는 mapRoot 아래에 생기므로, Day가 바뀌어 VillageScene을 다시 불러오면 이전 맵과 함께 사라지고 새 바닥 기준으로 다시 만들어진다.
    private const string WallShadowsName = "WallShadows";

    private static readonly System.Reflection.FieldInfo ShadowShapePathField =
        typeof(ShadowCaster2D).GetField("m_ShapePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    private static readonly System.Reflection.FieldInfo ShadowShapePathHashField =
        typeof(ShadowCaster2D).GetField("m_ShapePathHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    // 나무처럼 칸보다 큰 장애물 스프라이트는 바닥 가장자리 밖으로 삐져나온다. 장애물은 캐릭터와 같은 Default 레이어라 벽 그림자를 받는데,
    // 그림자 외곽선이 바닥 경계에 있으면 밖으로 나간 부분이 그 선을 따라 잘려 보인다. 그래서 방 소품 스프라이트가 덮는 바닥 밖 칸까지
    // 외곽선을 넓힌다 - 넓어지는 칸에는 바닥이 없어 캐릭터가 설 수 없으므로, 그림자로 캐릭터를 가리는 시야 규칙은 그대로다.
    // (그림자 대상 레이어에서 Default를 빼는 방식은 모서리 뒤 몬스터까지 드러나게 해서 쓰지 않는다.)
    // 동물/몬스터를 스폰하기 전에 부르므로 이 시점의 SpriteRenderer는 방 프리팹의 장애물/장식뿐이다.
    private static HashSet<Vector2Int> WithObstacleOverhang(HashSet<Vector2Int> mapFloor, List<RoomInstance> rooms)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>(mapFloor);
        foreach (RoomInstance room in rooms)
        {
            foreach (SpriteRenderer sprite in room.Root.GetComponentsInChildren<SpriteRenderer>())
            {
                Bounds b = sprite.bounds;
                for (int y = Mathf.FloorToInt(b.min.y); y <= Mathf.FloorToInt(b.max.y - 0.001f); y++)
                {
                    for (int x = Mathf.FloorToInt(b.min.x); x <= Mathf.FloorToInt(b.max.x - 0.001f); x++) cells.Add(new Vector2Int(x, y));
                }
            }
        }
        return cells;
    }

    private static void BuildWallShadows(Transform mapRoot, HashSet<Vector2Int> mapFloor)
    {
        // 혹시 같은 맵 루트 아래 이전에 만든 벽 그림자가 남아 있으면(재생성) 먼저 지운다.
        Transform previous = mapRoot.Find(WallShadowsName);
        if (previous != null) Destroy(previous.gameObject);

        if (ShadowShapePathField == null || ShadowShapePathHashField == null)
        {
            Debug.LogError("VillageMapGenerator: 이 URP 버전의 ShadowCaster2D에서 m_ShapePath/m_ShapePathHash를 찾지 못해 벽 그림자를 만들 수 없다.");
            return;
        }

        GameObject root = new GameObject(WallShadowsName);
        root.transform.SetParent(mapRoot, false);

        List<List<Vector2Int>> loops = FloorOutline.TraceLoops(mapFloor);
        for (int i = 0; i < loops.Count; i++)
        {
            List<Vector2Int> loop = loops[i];
            Vector3[] path = new Vector3[loop.Count];
            int hash = 17;
            for (int k = 0; k < loop.Count; k++)
            {
                path[k] = new Vector3(loop[k].x, loop[k].y, 0f);
                hash = unchecked(hash * 31 + loop[k].GetHashCode());
            }

            // 비활성 상태에서 컴포넌트를 붙이고 모양을 넣은 뒤 켜야, Awake가 기본 1x1 사각형 대신 이 외곽선으로 그림자 메시를
            // 처음부터 만든다. 콜라이더/렌더러가 없는 오브젝트라 에디터에서도 모양 공급원(provider)을 자동으로 잡지 않고
            // 이 경로(Shape Editor 방식)를 그대로 쓴다.
            GameObject go = new GameObject($"WallShadow_{i}");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            ShadowCaster2D caster = go.AddComponent<ShadowCaster2D>();
            ShadowShapePathField.SetValue(caster, path);
            ShadowShapePathHashField.SetValue(caster, hash == 0 ? 1 : hash);
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            caster.selfShadows = false;
            go.SetActive(true);
        }
    }

    // 칸 집합을 겹치지 않는 직사각형들로 묶는다 - 아래 줄부터 훑으며 오른쪽으로 최대한 늘린 뒤, 같은 폭으로
    // 위로 최대한 늘린다. 경계 칸은 대부분 가로/세로 한 줄짜리라 거의 그대로 긴 막대 몇 개로 합쳐진다.
    private static List<RectInt> MergeCellsIntoRects(HashSet<Vector2Int> cells)
    {
        List<Vector2Int> ordered = new List<Vector2Int>(cells);
        ordered.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        HashSet<Vector2Int> claimed = new HashSet<Vector2Int>();
        List<RectInt> rects = new List<RectInt>();
        foreach (Vector2Int start in ordered)
        {
            if (claimed.Contains(start)) continue;

            int width = 1;
            while (cells.Contains(new Vector2Int(start.x + width, start.y)) && !claimed.Contains(new Vector2Int(start.x + width, start.y))) width++;

            int height = 1;
            while (true)
            {
                bool rowFree = true;
                for (int i = 0; i < width && rowFree; i++)
                {
                    Vector2Int c = new Vector2Int(start.x + i, start.y + height);
                    rowFree = cells.Contains(c) && !claimed.Contains(c);
                }
                if (!rowFree) break;
                height++;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) claimed.Add(new Vector2Int(start.x + x, start.y + y));
            }
            rects.Add(new RectInt(start.x, start.y, width, height));
        }
        return rects;
    }

    // 바닥 타일이 없는 칸(맵 바깥)을 어둡게 칠하는 마스크. 빛을 가리는(occlude) 그림자가 아니라, 조명을 받지 않는
    // Unlit 머티리얼로 덮어 그릴 뿐이라 맵 안쪽의 시야/그림자 처리에는 영향이 없다.
    // 바닥 경계에서 딱 끊기면 어색하므로, 경계 바깥 outsideMaskFadeWidth칸까지는 바닥과 같은 타일을 장식으로 더 깔고
    // (BuildOutsideFloorBand - 걸어 다닐 수는 없다, 경계 충돌체는 원래 바닥 가장자리에 그대로 있다) 그 위의 마스크를
    // 바깥으로 갈수록 점점 진하게 칠한다. 그 너머(fadeWidth 이상)는 outsideMaskColor로 완전히 덮는다.
    // 시야를 막는 벽 그림자는 바닥 경계에 있어서, 경계까지 바닥이 그대로 밝으면 부채꼴 시야가 그 선에서 칼같이 끊겨 보인다.
    // 그래서 마스크는 경계 안쪽 outsideMaskInnerFadeWidth칸부터 미리 어두워지기 시작해, 경계 안팎을 하나의 곡선으로 지나간다
    // (그림자 위치/시야 차단 규칙은 그대로라 모서리 너머가 더 보이지는 않는다).
    // 바닥 범위를 outsideMaskMargin만큼 넓힌 사각형 안에서, 완전히 덮는 칸은 줄마다 이어지는 구간을 사각형 하나로 만든다.
    private void BuildOutsideMask(Transform mapRoot, HashSet<Vector2Int> mapFloor, List<RoomInstance> rooms)
    {
        if (mapFloor.Count == 0) return;

        // 메시 정점 색은 SpriteRenderer 색과 달리 색 공간 변환을 거치지 않는다. Linear 프로젝트에서는 Inspector에서 고른
        // 색이 그대로 보이도록 미리 linear로 바꿔 넣는다(안 바꾸면 화면에서 더 밝게 보인다).
        Color maskColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? outsideMaskColor.linear : outsideMaskColor;
        float fade = outsideMaskFadeWidth;
        float innerFade = outsideMaskInnerFadeWidth;
        int reach = Mathf.CeilToInt(fade) + 1;
        int innerReach = Mathf.CeilToInt(innerFade) + 1;

        // 점 p에서 가장 가까운 바닥 칸(정사각형)까지의 거리. reach 밖은 fade보다 멀다고 보고 찾지 않는다.
        float DistanceToFloor(Vector2 p)
        {
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y);
            float best = float.MaxValue;
            for (int y = py - reach; y <= py + reach; y++)
            {
                for (int x = px - reach; x <= px + reach; x++)
                {
                    if (!mapFloor.Contains(new Vector2Int(x, y))) continue;
                    float dx = Mathf.Max(x - p.x, 0f, p.x - (x + 1));
                    float dy = Mathf.Max(y - p.y, 0f, p.y - (y + 1));
                    float d = dx * dx + dy * dy;
                    if (d < best) best = d;
                }
            }
            return Mathf.Sqrt(best);
        }

        // 바닥 안쪽의 점 p에서 가장 가까운 바닥 아닌 칸(정사각형)까지의 거리. innerReach 밖은 innerFade보다 멀다고 보고 찾지 않는다.
        float DistanceToOutside(Vector2 p)
        {
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y);
            float best = float.MaxValue;
            for (int y = py - innerReach; y <= py + innerReach; y++)
            {
                for (int x = px - innerReach; x <= px + innerReach; x++)
                {
                    if (mapFloor.Contains(new Vector2Int(x, y))) continue;
                    float dx = Mathf.Max(x - p.x, 0f, p.x - (x + 1));
                    float dy = Mathf.Max(y - p.y, 0f, p.y - (y + 1));
                    float d = dx * dx + dy * dy;
                    if (d < best) best = d;
                }
            }
            return Mathf.Sqrt(best);
        }

        // 바닥 경계 기준 부호 있는 거리(바깥 +, 안쪽 -)로 투명도를 정한다: 안쪽 innerFade = 투명, 바깥 fade = 완전히 어둡게.
        float MaskAlpha(Vector2 p)
        {
            float outside = DistanceToFloor(p);
            float signedDistance = outside > 0f ? outside : -DistanceToOutside(p);
            float span = innerFade + fade;
            if (span <= 0f) return signedDistance > 0f ? maskColor.a : 0f;
            float t = Mathf.Clamp01((signedDistance + innerFade) / span);
            return maskColor.a * t * t * (3f - 2f * t);
        }

        // 그라데이션이 걸치는 바깥 칸들. 칸 가운데가 아니라 칸에서 가장 가까운 지점 기준으로 거르므로 빠지는 칸이 없다.
        HashSet<Vector2Int> band = new HashSet<Vector2Int>();
        if (fade > 0f)
        {
            foreach (Vector2Int c in mapFloor)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        Vector2Int n = new Vector2Int(c.x + dx, c.y + dy);
                        if (mapFloor.Contains(n) || band.Contains(n)) continue;
                        if (DistanceToFloor(new Vector2(n.x + 0.5f, n.y + 0.5f)) - 0.71f < fade) band.Add(n);
                    }
                }
            }
            BuildOutsideFloorBand(mapRoot, band, rooms);
        }

        // 그라데이션이 걸치는 바닥 칸들(경계 안쪽 innerFade 이내).
        List<Vector2Int> innerBand = new List<Vector2Int>();
        if (innerFade > 0f)
        {
            foreach (Vector2Int c in mapFloor)
            {
                if (DistanceToOutside(new Vector2(c.x + 0.5f, c.y + 0.5f)) - 0.71f < innerFade) innerBand.Add(c);
            }
        }

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector2Int c in mapFloor)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.y > maxY) maxY = c.y;
        }
        minX -= outsideMaskMargin; maxX += outsideMaskMargin;
        minY -= outsideMaskMargin; maxY += outsideMaskMargin;

        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();
        for (int y = minY; y <= maxY; y++)
        {
            int x = minX;
            while (x <= maxX)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (mapFloor.Contains(cell) || band.Contains(cell)) { x++; continue; }
                int runStart = x;
                while (x <= maxX && !mapFloor.Contains(new Vector2Int(x, y)) && !band.Contains(new Vector2Int(x, y))) x++;

                int b = vertices.Count;
                vertices.Add(new Vector3(runStart, y, 0f));
                vertices.Add(new Vector3(runStart, y + 1, 0f));
                vertices.Add(new Vector3(x, y + 1, 0f));
                vertices.Add(new Vector3(x, y, 0f));
                for (int i = 0; i < 4; i++) colors.Add(maskColor);
                triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 3);
            }
        }

        // 그라데이션 칸(바깥 band + 안쪽 innerBand)은 outsideMaskFadeSubdivisions x 조각으로 잘게 나눠, 정점마다 바닥 경계까지의
        // 부호 있는 거리로 투명도를 정한다(MaskAlpha). 칸 모양과 상관없이 경계선을 따라 부드럽게 이어지고,
        // 바깥으로 튀어나온 모서리 주변은 자연스럽게 둥글게 어두워진다.
        int sub = Mathf.Max(1, outsideMaskFadeSubdivisions);
        foreach (Vector2Int cell in band.Concat(innerBand))
        {
            int b = vertices.Count;
            for (int j = 0; j <= sub; j++)
            {
                for (int i = 0; i <= sub; i++)
                {
                    Vector2 p = new Vector2(cell.x + (float)i / sub, cell.y + (float)j / sub);
                    vertices.Add(new Vector3(p.x, p.y, 0f));
                    colors.Add(new Color(maskColor.r, maskColor.g, maskColor.b, MaskAlpha(p)));
                }
            }
            for (int j = 0; j < sub; j++)
            {
                for (int i = 0; i < sub; i++)
                {
                    int v0 = b + j * (sub + 1) + i;
                    int v1 = v0 + sub + 1;
                    triangles.Add(v0); triangles.Add(v1); triangles.Add(v1 + 1);
                    triangles.Add(v0); triangles.Add(v1 + 1); triangles.Add(v0 + 1);
                }
            }
        }

        Mesh mesh = new Mesh { name = "OutsideMapMaskMesh" };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();

        GameObject maskGO = new GameObject("OutsideMapMask");
        maskGO.transform.SetParent(mapRoot, false);
        maskGO.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = maskGO.AddComponent<MeshRenderer>();
        if (outsideMaskMaterial == null)
        {
            Debug.LogWarning($"{name}: outsideMaskMaterial이 비어 있어 Sprite-Unlit-Default 셰이더로 머티리얼을 새로 만든다.");
            outsideMaskMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        }
        renderer.sharedMaterial = outsideMaskMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingLayerName = outsideMaskSortingLayer;
        renderer.sortingOrder = outsideMaskSortingOrder;
    }

    // 그라데이션이 걸치는 맵 바깥 칸에 바닥과 같은 타일을 깐다. 순수 장식이라 mapFloor(NavMesh/스폰/경계 계산)에는 넣지
    // 않는다 - 경계 충돌체가 원래 바닥 가장자리에 있으므로 이 칸들은 보이기만 하고 들어갈 수는 없다. 시야 라이트를
    // 받아 바닥처럼 밝아지지만, 위에 덮인 마스크가 바깥으로 갈수록 진해지므로 경계에서부터 점점 어두워져 보인다.
    private void BuildOutsideFloorBand(Transform mapRoot, HashSet<Vector2Int> band, List<RoomInstance> rooms)
    {
        Tilemap template = null;
        Dictionary<TileBase, int> tileCounts = new Dictionary<TileBase, int>();
        foreach (RoomInstance room in rooms)
        {
            Tilemap ground = room.Root.transform.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            if (template == null) template = ground;
            foreach (Vector3Int pos in ground.cellBounds.allPositionsWithin)
            {
                TileBase t = ground.GetTile(pos);
                if (t != null) tileCounts[t] = tileCounts.TryGetValue(t, out int c) ? c + 1 : 1;
            }
        }
        // 이끼/얼룩 변형 타일이 아닌 기본 바닥 타일(가장 많이 쓰인 타일)을 쓴다.
        TileBase floorTile = null;
        int bestCount = 0;
        foreach (KeyValuePair<TileBase, int> kv in tileCounts)
        {
            if (kv.Value > bestCount) { bestCount = kv.Value; floorTile = kv.Key; }
        }
        if (template == null || floorTile == null || band.Count == 0) return;

        GameObject gridGO = new GameObject("OutsideFloorBand");
        gridGO.transform.SetParent(mapRoot, false);
        Grid grid = gridGO.AddComponent<Grid>();
        if (template.layoutGrid != null) grid.cellSize = template.layoutGrid.cellSize;

        GameObject tilemapGO = new GameObject("Ground");
        tilemapGO.transform.SetParent(gridGO.transform, false);
        Tilemap tilemap = tilemapGO.AddComponent<Tilemap>();
        tilemap.tileAnchor = template.tileAnchor;
        tilemap.color = template.color;
        TilemapRenderer renderer = tilemapGO.AddComponent<TilemapRenderer>();
        TilemapRenderer templateRenderer = template.GetComponent<TilemapRenderer>();
        if (templateRenderer != null)
        {
            renderer.sharedMaterial = templateRenderer.sharedMaterial;
            renderer.sortingOrder = templateRenderer.sortingOrder;
            renderer.mode = templateRenderer.mode;
        }
        renderer.sortingLayerName = floorSortingLayer;

        // mapRoot와 Grid는 원점·셀 크기 1이므로 월드 칸 좌표가 그대로 셀 좌표다.
        Vector3Int[] positions = new Vector3Int[band.Count];
        TileBase[] tiles = new TileBase[band.Count];
        int index = 0;
        foreach (Vector2Int cell in band)
        {
            positions[index] = new Vector3Int(cell.x, cell.y, 0);
            tiles[index] = floorTile;
            index++;
        }
        tilemap.SetTiles(positions, tiles);
    }

    private static Vector2Int ToWorldCell(RoomInstance room, Vector2Int localCell)
    {
        Vector3 offset = room.Root.transform.position;
        return new Vector2Int(localCell.x + Mathf.RoundToInt(offset.x), localCell.y + Mathf.RoundToInt(offset.y));
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

    // usedCells: 이 방에서 이미 차지된 칸. 뽑은 칸을 여기에 더해서 같은 자리에 여러 마리가 겹쳐 스폰되지 않게 하고,
    // 뒤이어 스폰하는 몬스터(SpawnMonster)도 같은 집합으로 동물이 있는 칸을 피한다.
    private void SpawnAnimal(RoomInstance room, Transform parent, HashSet<Vector2Int> usedCells)
    {
        if (animalPrefabs == null || animalPrefabs.Length == 0 || room.FloorCells.Count == 0) return;

        for (int i = 0; i < animalsPerRoom; i++)
        {
            GameObject prefab = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
            Vector2Int cell = PickInteriorCell(room, usedCells);
            usedCells.Add(cell);

            // AnimalRescue.Id는 부모 체인의 GameObject 이름으로 만들어지는데, Instantiate는 이름을
            // 자동으로 구분해주지 않아 한 방에서 같은 동물 프리팹이 두 번 뽑히면(animalsPerRoom >= 2)
            // 두 클론이 똑같은 이름 "Prefab(Clone)"을 갖게 되어 Id가 충돌한다 - 한 마리만 납품해도
            // 같은 Id를 가진 다른 한 마리까지 "이미 구조됨"으로 취급되어 재진입 시 함께 사라지는
            // 버그로 이어졌다. 같은 시드에서는 i번째 반복마다 항상 같은 프리팹이 뽑히므로, 방 안에서의
            // 인덱스를 이름에 넣으면 슬롯별로 안정적이면서도 서로 겹치지 않는 Id가 나온다.
            string animalName = $"{prefab.name}_{i}";

            // 실제로 Instantiate하기 전에, 이 동물이 받게 될 AnimalRescue.Id를 미리 계산해서(부모 체인이
            // 결정돼 있으므로 가능) GameManager에 트럭 왕복 전 저장해둔 마지막 위치/상태가 있는지 먼저
            // 확인한다 - 있으면 원래 스폰 칸 대신 그 자리에서 이어서 나타나게 한다.
            string predictedId = $"{MapRootName}/{room.Root.name}/{animalName}";
            Vector3 savedPosition = default;
            bool wasFleeing = false;
            bool hasFieldState = GameManager.Instance != null &&
                GameManager.Instance.TryGetAnimalFieldState(predictedId, out savedPosition, out wasFleeing);

            Vector3 spawnPosition = hasFieldState ? savedPosition : ToSpritePosition(room, cell);
            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity, parent);
            AnimalRescue rescue = instance.GetComponent<AnimalRescue>();
            if (rescue != null) rescue.SourcePrefab = prefab.GetComponent<AnimalRescue>();
            instance.name = animalName;
            Debug.Log($"[FieldState] 조회: {predictedId}, found={hasFieldState}" + (hasFieldState ? $", pos={savedPosition}" : ""));

            if (hasFieldState && wasFleeing)
            {
                AnimalFlee flee = instance.GetComponent<AnimalFlee>();
                if (flee != null) flee.RestoreFleeing();
            }
        }
    }

    private const int MonsterSpawnDistanceRetryCount = 10;

    // 스폰 순간에만 플레이어와의 거리를 강제한다 - 스폰 이후 순찰/배회로 이 범위 안에
    // 들어오는 건 정상적인 몬스터 AI 동작이므로 막지 않는다. minDistance를 만족하는 칸을
    // 찾을 때까지(최대 MonsterSpawnDistanceRetryCount번) 다시 뽑고, 끝내 못 찾으면 그동안
    // 시도한 후보 중 가장 멀리 떨어진 칸에 배치한다(완전히 스폰을 포기하지 않는다).
    private static Vector2Int PickMonsterSpawnCell(RoomInstance room, HashSet<Vector2Int> excludedCells, Vector2 playerSpawnWorldXY, float minDistance)
    {
        Vector2Int bestCell = PickInteriorCell(room, excludedCells);
        float bestDistance = Vector2.Distance(ToWorldXY(room, bestCell), playerSpawnWorldXY);

        for (int attempt = 1; attempt < MonsterSpawnDistanceRetryCount && bestDistance < minDistance; attempt++)
        {
            Vector2Int candidate = PickInteriorCell(room, excludedCells);
            float distance = Vector2.Distance(ToWorldXY(room, candidate), playerSpawnWorldXY);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestCell = candidate;
            }
        }

        return bestCell;
    }

    private void SpawnMonster(RoomInstance room, Transform parent, Vector2 playerSpawnWorldXY, HashSet<Vector2Int> usedCells)
    {
        if (monsterPrefabs == null || monsterPrefabs.Length == 0 || room.FloorCells.Count == 0) return;

        for (int i = 0; i < monstersPerRoom; i++)
        {
            GameObject prefab = monsterPrefabs[Random.Range(0, monsterPrefabs.Length)];
            Vector2Int cell = PickMonsterSpawnCell(room, usedCells, playerSpawnWorldXY, MonsterMinSpawnDistanceFromPlayer);
            usedCells.Add(cell);
            // 같은 방에 같은 몬스터 프리팹이 둘 이상 뽑혀도 MonsterCorpsePickup의 계층 경로 Id가 겹치지 않게 번호를 붙인다
            // (동물의 이름 규칙과 같다).
            SpawnMonsterAt(room, parent, prefab, cell, $"{prefab.name}_{i}");
        }
    }

    private void SpawnMonsterAt(RoomInstance room, Transform parent, GameObject prefab, Vector2Int cell, string instanceName)
    {
        Vector3 desired = ToNavPosition(room, cell);

        // NavMesh baking erodes walkable area inward from walls by the agent radius, so even an
        // "interior" cell's exact center can land just outside the baked mesh. Snap to the
        // nearest real point on the mesh instead of trusting the raw cell position - but keep the
        // search radius small. A wide radius (previously 8) could snap a monster clear across a
        // doorway into a different room, which read as monsters spawning "outside" the map.
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) && IsWithinRoom(room, hit.position))
        {
            Instantiate(prefab, hit.position, Quaternion.identity, parent).name = instanceName;
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

    private static Vector2Int PickInteriorCell(RoomInstance room, HashSet<Vector2Int> excludedCells = null)
    {
        HashSet<Vector2Int> floorSet = new HashSet<Vector2Int>(room.FloorCells);
        HashSet<Vector2Int> obstacleCells = room.ObstacleCells;

        List<Vector2Int> interior = new List<Vector2Int>();
        foreach (Vector2Int c in room.FloorCells)
        {
            if (obstacleCells.Contains(c)) continue;
            if (excludedCells != null && excludedCells.Contains(c)) continue;
            if (floorSet.Contains(c + Vector2Int.up) && floorSet.Contains(c + Vector2Int.down) &&
                floorSet.Contains(c + Vector2Int.left) && floorSet.Contains(c + Vector2Int.right))
            {
                interior.Add(c);
            }
        }

        if (interior.Count > 0) return interior[Random.Range(0, interior.Count)];

        // 제외할 칸(플레이어/트럭 위치)만 아니라면 굳이 완전히 안쪽(interior)이 아니어도 받아들인다 -
        // 방이 아주 작아 interior 후보가 하나도 없는 극단적인 경우의 대비책.
        Vector2Int central = PickCentralCell(room);
        if (excludedCells == null || !excludedCells.Contains(central)) return central;

        foreach (Vector2Int c in room.FloorCells)
        {
            if (!obstacleCells.Contains(c) && !excludedCells.Contains(c)) return c;
        }

        return central; // 이 방의 바닥 전체가 제외 대상뿐인 경우 - 선택의 여지가 없다.
    }

    // 각 장애물 칸 자리에 보이지 않는 NavMeshObstacle을 심어서, 구워둔 NavMesh 위에
    // 실시간으로 구멍을 뚫는다(carving). 굽는 시점에 메시에서 통째로 빼는 방식보다
    // Unity의 표준 카빙 파이프라인을 타므로 벽 근처에서도 훨씬 안정적으로 뚫린다.
    private static void AddObstacleCarvers(RoomInstance room, Transform parent)
    {
        foreach (RoomObstacle roomObstacle in room.Obstacles)
        foreach (Vector2Int c in roomObstacle.Cells)
        {
            GameObject carver = new GameObject("ObstacleNavCarver");
            carver.transform.SetParent(parent, false);
            carver.transform.position = ToNavPosition(room, c);
            roomObstacle.NavCarvers.Add(carver);

            NavMeshObstacle obstacle = carver.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(1f, 1f, 1f);
            // 이 카버는 스폰된 뒤 다시는 움직이지 않는다. carveOnlyStationary를 켜두면 "정지 상태일
            // 때만 카빙"으로 취급해 한 번 카빙한 뒤로는 매 프레임 다시 계산하지 않는다 - false로 두면
            // 매 프레임 "혹시 움직였는지" 다시 확인하며 재계산해서 근처의 좁은 통로(문틈)의 삼각분할이
            // 프레임마다 미세하게 흔들려 몬스터가 그 근처를 지날 때 버벅이는 원인이 될 수 있었다.
            obstacle.carveOnlyStationary = true;
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
        HashSet<Vector2Int> obstacleCells = room.ObstacleCells;

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
    // NavMesh.AddNavMeshData returns a handle that Unity never cleans up on its own - not even when
    // the GameObjects that triggered the bake are destroyed on scene unload. Without tracking and
    // removing the previous instance here, every re-entry into VillageScene (a normal truck
    // round-trip) would stack another full room layout's NavMesh on top of the old one forever.
    private static NavMeshDataInstance activeNavMeshDataInstance;

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

        if (activeNavMeshDataInstance.valid) NavMesh.RemoveNavMeshData(activeNavMeshDataInstance);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null)
        {
            // 몬스터는 NavMesh 없이는 움직이지 못하지만, 맵·플레이어·동물은 정상이므로 게임은 계속 진행한다.
            Debug.LogError("VillageMapGenerator: NavMesh 빌드에 실패했다 - 몬스터가 이동하지 못한다.");
            return;
        }
        activeNavMeshDataInstance = NavMesh.AddNavMeshData(data);
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
