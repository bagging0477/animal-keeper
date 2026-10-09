using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// 길 가장자리 그라데이션 깊이(GameBalanceConfig.gridCorridorFadeDepth)를 1칸과 2칸으로 바꿔 같은 시드의 3x4 격자 맵을 위에서 찍는다.
/// 시드는 1부터 찾아, 넓힌 입구(Forest 남/북, Graveyard 북)로 이어진 길이 있는 첫 시드를 쓴다. 맵 전체와 그 입구 / 트럭 방 포트 주변을
/// 확대해서 찍고, Forest/Graveyard 방 프리팹 단독 모습도 찍는다. 시야 연출 없이 잘 보이도록 Global 라이트만 1로 둔다.
/// 깊이 값은 Play 중 메모리에서만 바꿨다가 되돌린다(에셋은 저장하지 않는다).
/// 결과: Logs/CorridorFadeCaptures/
/// 메뉴: Tools > Village Rooms > Capture Corridor Fade (depth 1 vs 2)
/// </summary>
[InitializeOnLoad]
public static class CorridorFadeCapture
{
    public const string OutputFolder = "Logs/CorridorFadeCaptures";
    private const string KeyActive = "CFC.Active", KeyDone = "CFC.Done";
    private const string LogPrefix = "[CorridorFadeCapture] ";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private static readonly float[] Depths = { 1f, 2f };
    private const int MaxSeedSearch = 40;

    private static IEnumerator routine;

    static CorridorFadeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Rooms/Capture Corridor Fade (depth 1 vs 2)")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning(LogPrefix + "Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
        }
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; return; }
        bool more;
        try { more = routine.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); more = false; }
        if (!more)
        {
            EditorApplication.update -= Step;
            routine = null;
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static IEnumerator LoadSeed(int seed)
    {
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = seed };
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded = true;
        SceneManager.sceneLoaded += onLoaded;
        SceneManager.LoadScene(Path.GetFileNameWithoutExtension(VillageRoomBuilder.ScenePath));
        while (!loaded) yield return null;
        SceneManager.sceneLoaded -= onLoaded;
        for (int i = 0; i < 3; i++) yield return null; // Destroy/카빙 반영
    }

    // 넓힌 입구(Forest 남/북, Graveyard 북)에 붙은 길의 포트.
    private static GridPort WidenedPort(GridLayoutPlan plan)
    {
        if (plan == null || !plan.Success) return null;
        foreach (GridCorridor corridor in plan.Corridors)
            foreach (GridPort port in new[] { corridor.PortA, corridor.PortB })
            {
                string type = plan.TypeNames[plan.RoomType[port.Cell.x, port.Cell.y]];
                if ((type == "Room_Forest" && (port.Side == GridSide.North || port.Side == GridSide.South)) ||
                    (type == "Room_Graveyard" && port.Side == GridSide.North)) return port;
            }
        return null;
    }

    private static IEnumerator Run()
    {
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        float originalDepth = config.gridCorridorFadeDepth;
        List<string> files = new List<string>();
        try
        {
            Directory.CreateDirectory(OutputFolder);
            int seed = 0;
            GridPort widened = null;
            for (int s = 1; s <= MaxSeedSearch && widened == null; s++)
            {
                IEnumerator load = LoadSeed(s);
                while (load.MoveNext()) yield return load.Current;
                widened = WidenedPort(Object.FindAnyObjectByType<VillageMapGenerator>()?.LastGridPlan);
                if (widened != null) seed = s;
            }
            if (seed == 0) { seed = 1; Debug.LogWarning(LogPrefix + $"시드 1..{MaxSeedSearch}에 넓힌 입구로 이어진 길이 없어 시드 1로 찍는다."); }

            foreach (float depth in Depths)
            {
                config.gridCorridorFadeDepth = depth;
                IEnumerator load = LoadSeed(seed);
                while (load.MoveNext()) yield return load.Current;

                VillageMapGenerator generator = Object.FindAnyObjectByType<VillageMapGenerator>();
                GridLayoutPlan plan = generator.LastGridPlan;
                HashSet<Vector2Int> floor = FloorCells();
                string prefix = $"{OutputFolder}/seed{seed:00}_depth{depth:0}";
                WithFullLight(() =>
                {
                    int minX = floor.Min(c => c.x), maxX = floor.Max(c => c.x), minY = floor.Min(c => c.y), maxY = floor.Max(c => c.y);
                    files.Add(Capture(new Rect(minX - 2, minY - 2, maxX - minX + 5, maxY - minY + 5), 8, prefix + "_map.png"));

                    GridPort port = WidenedPort(plan);
                    if (port != null)
                    {
                        string type = plan.TypeNames[plan.RoomType[port.Cell.x, port.Cell.y]];
                        files.Add(Capture(AroundPort(port), 32, $"{prefix}_port_{type}_{port.Side}.png"));
                    }
                    GridPort truckPort = plan.Corridors.SelectMany(c => new[] { c.PortA, c.PortB }).FirstOrDefault(p => p.Cell == plan.TruckCell);
                    if (truckPort != null) files.Add(Capture(AroundPort(truckPort), 32, $"{prefix}_port_truckroom_{truckPort.Side}.png"));
                });
            }

            // 방 프리팹 단독(맵 바깥 멀리 놓고 찍는다).
            foreach (string room in new[] { "Room_Forest", "Room_Graveyard" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Rooms/{room}.prefab");
                GameObject instance = Object.Instantiate(prefab, new Vector3(-2000f, -2000f, 0f), Quaternion.identity);
                Physics2D.SyncTransforms();
                UnityEngine.Tilemaps.Tilemap ground = instance.transform.Find("Ground").GetComponent<UnityEngine.Tilemaps.Tilemap>();
                ground.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>().sortingLayerName = "Floor";
                ground.CompressBounds();
                BoundsInt cb = ground.cellBounds;
                WithFullLight(() => files.Add(Capture(new Rect(-2000f + cb.xMin - 1, -2000f + cb.yMin - 1, cb.size.x + 2, cb.size.y + 2), 32, $"{OutputFolder}/prefab_{room}.png")));
                Object.Destroy(instance);
                yield return null;
            }
            Debug.Log(LogPrefix + $"시드 {seed}, 넓힌 입구 포트 {(widened != null ? "있음" : "없음")}\n" + string.Join("\n", files.Select(Path.GetFullPath)));
        }
        finally
        {
            config.gridCorridorFadeDepth = originalDepth;
        }
    }

    // 포트 가운데에서 길 쪽으로 조금 나간 지점을 중심으로 28x28칸.
    private static Rect AroundPort(GridPort port)
    {
        Vector2 center = Vector2.zero;
        foreach (Vector2Int c in port.EdgeCells) center += new Vector2(c.x + 0.5f, c.y + 0.5f);
        center /= Mathf.Max(1, port.EdgeCells.Count);
        Vector2 outward = port.Side == GridSide.North ? Vector2.up : port.Side == GridSide.South ? Vector2.down : port.Side == GridSide.East ? Vector2.right : Vector2.left;
        center += outward * 3f;
        return new Rect(center.x - 14f, center.y - 14f, 28f, 28f);
    }

    private static HashSet<Vector2Int> FloorCells()
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        GameObject mapRoot = GameObject.Find("GeneratedMap");
        foreach (UnityEngine.Tilemaps.Tilemap tm in mapRoot.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>())
        {
            if (tm.name != "Ground" && tm.name != "Floor") continue;
            if (tm.transform.parent != null && tm.transform.parent.name == "OutsideFloorBand") continue;
            Vector3 o = tm.transform.position;
            foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
                if (tm.HasTile(p)) cells.Add(new Vector2Int(p.x + Mathf.RoundToInt(o.x), p.y + Mathf.RoundToInt(o.y)));
        }
        return cells;
    }

    private static string Capture(Rect area, int pixelsPerUnit, string file)
    {
        VillageRoomBuilder.CaptureArea(area, pixelsPerUnit, file);
        return file;
    }

    private static void WithFullLight(System.Action action)
    {
        Light2D[] globals = Object.FindObjectsByType<Light2D>().Where(l => l.lightType == Light2D.LightType.Global).ToArray();
        float[] intensities = globals.Select(l => l.intensity).ToArray();
        foreach (Light2D l in globals) l.intensity = 1f;
        try { action(); }
        finally { for (int i = 0; i < globals.Length; i++) globals[i].intensity = intensities[i]; }
    }
}
