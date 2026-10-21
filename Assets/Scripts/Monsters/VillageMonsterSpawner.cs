using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>오늘(같은 Day)의 시간 소환 기록. GameManager가 들고 있고 ResetDay/ResetGame에서 비운다.</summary>
public class VillageSpawnRecord
{
    public class EntryState
    {
        /// <summary>번호를 받아 한 번이라도 소환된 수. 번호 0..Issued-1이 이 항목의 개체다(같은 Day 재진입 때 같은 번호·같은 Id로 다시 만든다).</summary>
        public int Issued;
        /// <summary>소환해야 하지만 아직 못 한 수(상한에 닿았거나 자리를 못 찾음). 다음 기회에 다시 시도한다.</summary>
        public int Pending;
        /// <summary>간격 소환 타이머(초). 마을에서 흐르는 시간으로만 늘어난다.</summary>
        public float IntervalTimer;
    }

    /// <summary>"구간에 들어갈 때" 소환을 이미 처리한 가장 높은 구간. 0이면 아직 없다. 같은 구간을 두 번 처리하지 않게 한다.</summary>
    public int EnteredPhase { get; set; }

    private readonly Dictionary<(int phase, int index), EntryState> entries = new Dictionary<(int phase, int index), EntryState>();

    public EntryState Get(int phase, int index)
    {
        if (!entries.TryGetValue((phase, index), out EntryState state))
        {
            state = new EntryState();
            entries[(phase, index)] = state;
        }
        return state;
    }

    public bool TryGet(int phase, int index, out EntryState state) => entries.TryGetValue((phase, index), out state);

    public void Clear()
    {
        EnteredPhase = 0;
        entries.Clear();
    }
}

/// <summary>
/// 마을 시간 구간(GameManager.VillagePhase)에 따라 GameBalanceConfig.villagePhaseSpawns 목록대로 몬스터를 마을에 추가로 소환한다.
/// GameManager가 Awake에서 자기 오브젝트에 붙이고, VillageScene에 있을 때만 동작한다. 목록이 모두 비어 있으면 아무것도 하지 않는다
/// (난수·탐색·오브젝트 생성 없음 - 게임 동작이 이 컴포넌트가 없을 때와 같다).
///
/// - 구간에 들어가면 그 구간 항목마다 initialCount를, 이후 intervalSeconds마다 intervalCount를 소환 대기 수에 더한다(총 count까지).
///   한 프레임에 구간을 여러 개 건너뛰면 건너뛴 구간들까지 모두 처리한다. 타이머는 Time.deltaTime이라 일시정지(timeScale 0) 중에는 멈춘다.
/// - 마을 전체에 살아 있는 몬스터(맵 생성 몬스터 포함)가 villageMonsterCap 이상이면 소환을 멈추고, 처치해서 줄면 이어서 소환한다.
/// - 위치: 트럭 방이 아닌 방 바닥, 플레이어에게서 villageSpawnMinPlayerDistance 이상, 플레이어 시야 밖(PlayerVision.IsVisibleToPlayer),
///   막는 충돌체·다른 몬스터와 겹치지 않음, NavMesh 위. MaxPositionAttempts번 안에 못 찾으면 이번 소환은 건너뛰고 RetryDelaySeconds 뒤 다시 시도한다.
/// - 난수는 이 컴포넌트 전용 System.Random이다. 맵 생성이 쓰는 UnityEngine.Random 상태를 읽거나 바꾸지 않는다.
/// - Id: 맵 몬스터와 같은 계층 경로 규칙(GeneratedMap/TimedMonsters/{프리팹}_T{구간}_{항목}_{번호}). 방과 무관한 고정 부모라 같은 Day 재진입에서도
///   같은 Id가 나오고, 늑대 시체(MonsterCorpsePickup)·소리반응형이 쓰는 "오늘 처치한 몬스터" 기록(GameManager.SaveMonsterCorpse)이 그대로 적용된다.
/// - 같은 Day 재진입: 이미 번호를 받은 개체를 다시 만든다(처치한 개체는 기록대로 시체/숨김, 납품·보유 중인 늑대 시체는 만들지 않음).
///   구간 진입 소환은 EnteredPhase로 한 번만 하고, 재진입 시점의 구간까지 아직 처리하지 않은 구간이 있으면 시작할 때 처리한다.
/// </summary>
public class VillageMonsterSpawner : MonoBehaviour
{
    public const string ParentName = "TimedMonsters";
    private const string MapRootName = "GeneratedMap"; // VillageMapGenerator.MapRootName과 같아야 한다
    private const string VillageSceneName = "VillageScene";
    private const string OutsideFloorBandName = "OutsideFloorBand"; // 맵 바깥 장식 바닥(VillageMapGenerator.BuildOutsideFloorBand) - 들어갈 수 없다
    private const int MaxPositionAttempts = 40;
    private const float RetryDelaySeconds = 0.5f;
    private const float NavSnapRadius = 1f;

    private sealed class FloorArea
    {
        public Tilemap Ground;
        public List<Vector3Int> Cells;
    }

    private readonly List<(int phase, int index, int ordinal)> restoreQueue = new List<(int, int, int)>();
    private List<FloorArea> spawnAreas;
    private Tilemap truckGround;
    private Transform player;
    private Transform spawnParent;
    private System.Random rng;
    private int villageEntries;
    private bool needsBegin;
    private float retryTimer;

    /// <summary>자리를 못 찾아 건너뛴 소환 횟수(이번 마을 진입 동안). 테스트가 읽는다.</summary>
    public int SkippedSpawns { get; private set; }

    /// <summary>이번 마을 진입에서 소환 개체를 둔 부모(GeneratedMap/TimedMonsters). 아직 하나도 소환하지 않았으면 null.</summary>
    public Transform SpawnParent => spawnParent;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameManager.OnVillagePhaseChanged += OnVillagePhaseChanged;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameManager.OnVillagePhaseChanged -= OnVillagePhaseChanged;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        restoreQueue.Clear();
        spawnAreas = null;
        truckGround = null;
        player = null;
        spawnParent = null;
        retryTimer = 0f;
        SkippedSpawns = 0;
        needsBegin = scene.name == VillageSceneName;
    }

    private static GameBalanceConfig ActiveConfig()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.Config == null || !gm.Config.HasAnyVillagePhaseSpawns()) return null;
        return SceneManager.GetActiveScene().name == VillageSceneName ? gm.Config : null;
    }

    private void OnVillagePhaseChanged(int phase)
    {
        GameBalanceConfig config = ActiveConfig();
        if (config == null || needsBegin) return; // 시작 처리(BeginVillage)가 현재 구간까지 한꺼번에 처리한다
        EnterPhasesUpTo(GameManager.Instance.VillageSpawns, config, phase);
    }

    private void Update()
    {
        GameBalanceConfig config = ActiveConfig();
        if (config == null) return;
        GameManager gm = GameManager.Instance;

        if (needsBegin && !BeginVillage(gm, config)) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return; // 일시정지 중에는 타이머도 소환도 멈춘다

        TickIntervals(gm.VillageSpawns, config, dt);
        if (retryTimer > 0f)
        {
            retryTimer -= dt;
            return;
        }
        SpawnPending(gm, config);
    }

    // 마을에 들어온 뒤 첫 Update: 맵을 읽고, 오늘 이미 소환했던 개체를 다시 만들 준비를 하고, 현재 구간까지 밀린 구간 진입 소환을 처리한다.
    private bool BeginVillage(GameManager gm, GameBalanceConfig config)
    {
        GameObject map = GameObject.Find(MapRootName);
        if (map == null) return false;
        needsBegin = false;

        rng = new System.Random(unchecked(gm.GetVillageMapSeedForToday() * 397 + ++villageEntries));
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        player = playerObj != null ? playerObj.transform : null;
        CollectFloors(map.transform);

        VillageSpawnRecord record = gm.VillageSpawns;
        for (int phase = 1; phase <= record.EnteredPhase; phase++)
        {
            GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(phase);
            for (int i = 0; i < entries.Length; i++)
            {
                if (!IsValid(entries[i]) || !record.TryGet(phase, i, out VillageSpawnRecord.EntryState state)) continue;
                for (int ordinal = 0; ordinal < state.Issued; ordinal++)
                {
                    string id = $"{MapRootName}/{ParentName}/{InstanceName(entries[i], phase, i, ordinal)}";
                    // 납품했거나 들고 있는 늑대 시체는 MonsterCorpsePickup이 어차피 감추므로 만들지 않는다.
                    if (gm.IsAnimalRescuedToday(id) || gm.IsAnimalCurrentlyHeld(id)) continue;
                    // 오늘 처치한 개체는 남긴 자리에 만든다 - 늑대는 그 자리 시체로, 소리반응형은 숨김으로 돌아간다(각자의 Start가 기록을 본다).
                    if (gm.TryGetMonsterCorpse(id, out Vector3 corpsePosition)) Create(entries[i].monsterPrefab, corpsePosition, InstanceName(entries[i], phase, i, ordinal));
                    else restoreQueue.Add((phase, i, ordinal));
                }
            }
        }

        EnterPhasesUpTo(record, config, gm.VillagePhase);
        return true;
    }

    private static bool IsValid(GameBalanceConfig.VillageSpawnEntry entry) => entry != null && entry.monsterPrefab != null && entry.count > 0;

    private static int Remaining(GameBalanceConfig.VillageSpawnEntry entry, VillageSpawnRecord.EntryState state) =>
        Mathf.Max(0, entry.count - state.Issued - state.Pending);

    private static void EnterPhasesUpTo(VillageSpawnRecord record, GameBalanceConfig config, int phase)
    {
        for (int p = record.EnteredPhase + 1; p <= phase; p++)
        {
            GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(p);
            for (int i = 0; i < entries.Length; i++)
            {
                if (!IsValid(entries[i])) continue;
                VillageSpawnRecord.EntryState state = record.Get(p, i);
                state.Pending += Mathf.Clamp(entries[i].initialCount, 0, Remaining(entries[i], state));
            }
        }
        if (phase > record.EnteredPhase) record.EnteredPhase = phase;
    }

    private static void TickIntervals(VillageSpawnRecord record, GameBalanceConfig config, float dt)
    {
        for (int phase = 1; phase <= record.EnteredPhase; phase++)
        {
            GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(phase);
            for (int i = 0; i < entries.Length; i++)
            {
                GameBalanceConfig.VillageSpawnEntry entry = entries[i];
                if (!IsValid(entry) || entry.intervalCount <= 0 || entry.intervalSeconds <= 0f) continue;
                VillageSpawnRecord.EntryState state = record.Get(phase, i);
                if (Remaining(entry, state) <= 0) continue;
                state.IntervalTimer += dt;
                while (state.IntervalTimer >= entry.intervalSeconds && Remaining(entry, state) > 0)
                {
                    state.IntervalTimer -= entry.intervalSeconds;
                    state.Pending += Mathf.Min(entry.intervalCount, Remaining(entry, state));
                }
            }
        }
    }

    private void SpawnPending(GameManager gm, GameBalanceConfig config)
    {
        VillageSpawnRecord record = gm.VillageSpawns;
        int living = CountLivingMonsters();
        int cap = config.villageMonsterCap;

        // 먼저 오늘 이미 소환했던 개체(재진입)를 같은 번호로 되살린다.
        while (restoreQueue.Count > 0)
        {
            if (living >= cap) return;
            (int phase, int index, int ordinal) = restoreQueue[0];
            GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(phase);
            if (index >= entries.Length || !IsValid(entries[index])) { restoreQueue.RemoveAt(0); continue; }
            if (!TrySpawn(entries[index], phase, index, ordinal)) return;
            restoreQueue.RemoveAt(0);
            living++;
        }

        for (int phase = 1; phase <= record.EnteredPhase; phase++)
        {
            GameBalanceConfig.VillageSpawnEntry[] entries = config.GetVillagePhaseSpawns(phase);
            for (int i = 0; i < entries.Length; i++)
            {
                if (!IsValid(entries[i]) || !record.TryGet(phase, i, out VillageSpawnRecord.EntryState state)) continue;
                while (state.Pending > 0)
                {
                    if (living >= cap) return;
                    if (!TrySpawn(entries[i], phase, i, state.Issued)) return;
                    state.Pending--;
                    state.Issued++;
                    living++;
                }
            }
        }
    }

    /// <summary>마을에 살아 있는(켜져 있고 죽지 않은) 몬스터 수. 맵 생성 몬스터와 소환 몬스터를 모두 센다.</summary>
    public static int CountLivingMonsters()
    {
        int living = 0;
        foreach (MonsterHealth m in MonsterHealth.Active)
        {
            if (m != null && !m.IsDead) living++;
        }
        return living;
    }

    private bool TrySpawn(GameBalanceConfig.VillageSpawnEntry entry, int phase, int index, int ordinal)
    {
        if (!TryFindSpot(entry.monsterPrefab, out Vector3 navPosition))
        {
            SkippedSpawns++;
            retryTimer = RetryDelaySeconds;
            return false;
        }
        Create(entry.monsterPrefab, navPosition, InstanceName(entry, phase, index, ordinal));
        return true;
    }

    private static string InstanceName(GameBalanceConfig.VillageSpawnEntry entry, int phase, int index, int ordinal) =>
        $"{entry.monsterPrefab.name}_T{phase}_{index}_{ordinal}";

    // VillageMapGenerator.SpawnMonsterAt과 같은 방식: NavMesh에 붙인 자리(x, 0, z)에 회전 없이 만들고 이름에 번호를 붙인다(Id = 계층 경로).
    private void Create(GameObject prefab, Vector3 navPosition, string instanceName)
    {
        Instantiate(prefab, navPosition, Quaternion.identity, SpawnParentTransform()).name = instanceName;
    }

    private Transform SpawnParentTransform()
    {
        if (spawnParent != null) return spawnParent;
        GameObject map = GameObject.Find(MapRootName);
        Transform existing = map != null ? map.transform.Find(ParentName) : null;
        if (existing == null)
        {
            existing = new GameObject(ParentName).transform;
            existing.SetParent(map != null ? map.transform : null, false);
        }
        spawnParent = existing;
        return spawnParent;
    }

    // 방과 길의 바닥(Ground 타일맵)을 모은다. 트럭 지점이 놓인 바닥은 트럭 방으로 보고 후보에서 뺀다.
    private void CollectFloors(Transform mapRoot)
    {
        spawnAreas = new List<FloorArea>();
        truckGround = null;
        TruckPoint truck = FindAnyObjectByType<TruckPoint>();
        foreach (Transform child in mapRoot)
        {
            if (child.name == OutsideFloorBandName) continue;
            Tilemap ground = child.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            if (truck != null && ground.HasTile(ground.WorldToCell(truck.transform.position)))
            {
                truckGround = ground;
                continue;
            }
            List<Vector3Int> cells = new List<Vector3Int>();
            foreach (Vector3Int pos in ground.cellBounds.allPositionsWithin)
            {
                if (ground.HasTile(pos)) cells.Add(pos);
            }
            if (cells.Count > 0) spawnAreas.Add(new FloorArea { Ground = ground, Cells = cells });
        }
    }

    private bool TryFindSpot(GameObject prefab, out Vector3 navPosition)
    {
        navPosition = default;
        if (spawnAreas == null || spawnAreas.Count == 0 || player == null) return false;
        float radius = SpawnRadius(prefab);
        for (int attempt = 0; attempt < MaxPositionAttempts; attempt++)
        {
            FloorArea area = spawnAreas[rng.Next(spawnAreas.Count)];
            Vector3 center = area.Ground.GetCellCenterWorld(area.Cells[rng.Next(area.Cells.Count)]);
            Vector2 xy = new Vector2(center.x, center.y);
            if (!IsAllowedSpot(xy, radius)) continue;
            if (!NavMesh.SamplePosition(new Vector3(xy.x, 0f, xy.y), out NavMeshHit hit, NavSnapRadius, NavMesh.AllAreas)) continue;
            if (!IsAllowedSpot(new Vector2(hit.position.x, hit.position.z), radius)) continue;
            navPosition = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>소환 위치 규칙(2D 게임 좌표): 트럭 방이 아닌 바닥, 플레이어에게서 최소 거리 이상, 플레이어 시야 밖, 막는 충돌체·살아 있는 몬스터와 겹치지 않음.</summary>
    public bool IsAllowedSpot(Vector2 xy, float radius)
    {
        GameBalanceConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null || player == null || spawnAreas == null) return false;
        if (truckGround != null && truckGround.HasTile(truckGround.WorldToCell(xy))) return false;
        bool onFloor = false;
        foreach (FloorArea area in spawnAreas)
        {
            if (area.Ground.HasTile(area.Ground.WorldToCell(xy))) { onFloor = true; break; }
        }
        if (!onFloor) return false;
        if (Vector2.Distance(xy, player.position) < config.villageSpawnMinPlayerDistance) return false;
        if (PlayerVision.IsVisibleToPlayer(xy)) return false;
        foreach (Collider2D c in Physics2D.OverlapCircleAll(xy, radius))
        {
            if (IsBlocker(c)) return false;
        }
        foreach (MonsterHealth m in MonsterHealth.Active)
        {
            if (m != null && !m.IsDead && Vector2.Distance(m.GamePosition, xy) < radius * 2f) return false;
        }
        return true;
    }

    // 막는 충돌체: 트리거가 아니고, 동물·플레이어·움직이는(Dynamic) 몸체가 아닌 것(벽, 장애물, 맵 경계).
    private static bool IsBlocker(Collider2D c)
    {
        if (c == null || !c.enabled || c.isTrigger) return false;
        if (c.GetComponentInParent<AnimalRescue>() != null || c.GetComponentInParent<PlayerMovement>() != null) return false;
        return c.attachedRigidbody == null || c.attachedRigidbody.bodyType != RigidbodyType2D.Dynamic;
    }

    // VillageMapGenerator.SpawnRadius와 같은 기준: 막는 2D 충돌체가 있으면 그 크기, 없으면(몬스터) NavMeshAgent 반경.
    public static float SpawnRadius(GameObject prefab)
    {
        foreach (Collider2D col in prefab.GetComponentsInChildren<Collider2D>(true))
        {
            if (col.isTrigger) continue;
            Vector3 scale = col.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            if (col is CircleCollider2D circle) return circle.radius * maxScale + circle.offset.magnitude * maxScale;
            if (col is CapsuleCollider2D capsule) return 0.5f * Mathf.Max(Mathf.Abs(capsule.size.x * scale.x), Mathf.Abs(capsule.size.y * scale.y)) + capsule.offset.magnitude * maxScale;
            if (col is BoxCollider2D box) return 0.5f * new Vector2(box.size.x * scale.x, box.size.y * scale.y).magnitude + box.offset.magnitude * maxScale;
            return 0.5f;
        }
        NavMeshAgent agent = prefab.GetComponentInChildren<NavMeshAgent>(true);
        return agent != null ? agent.radius : 0.5f;
    }
}
