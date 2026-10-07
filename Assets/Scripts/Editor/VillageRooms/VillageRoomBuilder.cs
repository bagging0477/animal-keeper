using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Layouts/*.txt 격자를 읽어 VillageScene용 방 프리팹(Assets/Prefabs/Rooms)을 만들고, VillageScene의 MapGenerator → roomPrefabs에
/// 등록한다. 구조는 기존 방(RoomA_Corridor)을 그대로 따른다: 루트(Grid) 아래 Ground 타일맵, Obstacles(BoxCollider2D가 붙은
/// 막는 장애물), Exits(빈 마커). 여기에 더해 길 타일은 Path 타일맵에, 콜라이더 없는 장식은 Decorations 아래에 둔다 -
/// VillageMapGenerator는 Ground만 바닥으로 읽고 Perlin 변형도 Ground에만 칠하므로, 길이 변형 타일에 덮이지 않는다.
/// 이미 있는 프리팹은 같은 경로에 덮어써서 GUID(씬 참조)가 유지된다.
/// </summary>
public static class VillageRoomBuilder
{
    public const string LayoutFolder = "Assets/Scripts/Editor/VillageRooms/Layouts";
    public const string PrefabFolder = "Assets/Prefabs/Rooms";
    public const string ScenePath = "Assets/Scenes/VillageScene.unity";
    public const string CaptureFolder = "Logs/VillageRoomCaptures";
    private const string TemplatePrefabPath = "Assets/Prefabs/Rooms/RoomA_Corridor.prefab";
    private const string GrassTilePath = "Assets/Tiles/OutdoorFloorGrassTile.asset";
    private const string PathTilePath = "Assets/Tiles/OutdoorFloorStoneVariantTile.asset";

    // 생성기가 런타임에 Ground를 옮기는 정렬 레이어. 길과 납작한 장식은 처음부터 이 레이어에 둔다(Ground=0 위, 바깥 마스크=100 아래).
    private const string FloorSortingLayer = "Floor";
    private const int PathSortingOrder = 1;
    private const int FlatDecorSortingOrder = 5;

    // TX Plant 시트는 PPU 100이라 다른 소품(PPU 32)과 크기를 맞추려면 100/32배로 키운다.
    private const float PlantScale = 100f / 32f;

    private const string OutdoorProps = "Assets/Sprites/Tiles/Outdoor/Props/";
    private const string TxProps = "Assets/Sprites/Tiles/Outdoor/TX Props.png";
    private const string TxPlant = "Assets/Sprites/Tiles/Outdoor/TX Plant.png";
    private const string StoneWall = "Assets/Tiles/StoneWall/";

    private sealed class PropSpec
    {
        public string AssetPath;
        public string[] SpriteNames;   // 여러 개면 칸 좌표로 골라 섞는다(다시 만들어도 같은 결과)
        public float Scale = 1f;
        public bool TrunkOnCell;       // 나무: 스프라이트를 올려서 줄기 밑동이 장애물 칸에 오게 한다
        public bool Flat;              // 납작한 장식(잡초/자갈): Floor 레이어에 깐다
    }

    // TX Props_8(석상)과 같은 그림인 OutdoorStatueSprite는 마을에서 쓰지 않기로 해서 넣지 않는다.
    private static readonly Dictionary<string, PropSpec> Props = new Dictionary<string, PropSpec>
    {
        { "Tree1", new PropSpec { AssetPath = OutdoorProps + "OutdoorTree1Sprite.png", SpriteNames = new[] { "OutdoorTree1Sprite_0" }, TrunkOnCell = true } },
        { "Tree2", new PropSpec { AssetPath = OutdoorProps + "OutdoorTree2Sprite.png", SpriteNames = new[] { "OutdoorTree2Sprite_0" }, TrunkOnCell = true } },
        { "Bush", new PropSpec { AssetPath = OutdoorProps + "OutdoorBushSprite.png", SpriteNames = new[] { "OutdoorBushSprite_0" } } },
        { "Rock", new PropSpec { AssetPath = OutdoorProps + "OutdoorRockSprite.png", SpriteNames = new[] { "OutdoorRockSprite_0" } } },
        { "Gravestone", new PropSpec { AssetPath = OutdoorProps + "OutdoorGravestoneSprite.png", SpriteNames = new[] { "OutdoorGravestoneSprite_0" } } },
        { "Cross", new PropSpec { AssetPath = OutdoorProps + "OutdoorCrossSprite.png", SpriteNames = new[] { "OutdoorCrossSprite_0" } } },
        { "Urn", new PropSpec { AssetPath = OutdoorProps + "OutdoorUrnSprite.png", SpriteNames = new[] { "OutdoorUrnSprite_0" } } },
        { "Stele", new PropSpec { AssetPath = TxProps, SpriteNames = new[] { "TX Props_3", "TX Props_12" } } },
        { "Headstone", new PropSpec { AssetPath = TxProps, SpriteNames = new[] { "TX Props_31", "TX Props_32" } } },
        { "BigRock", new PropSpec { AssetPath = TxProps, SpriteNames = new[] { "TX Props_43" } } },
        { "Pebble", new PropSpec { AssetPath = TxProps, SpriteNames = new[] { "TX Props_44", "TX Props_45", "TX Props_46", "TX Props_47", "TX Props_48", "TX Props_50", "TX Props_51" }, Flat = true } },
        { "Weed", new PropSpec { AssetPath = TxPlant, SpriteNames = Enumerable.Range(9, 14).Select(i => "TX Plant_" + i).ToArray(), Scale = PlantScale, Flat = true } },
        { "SmallBush", new PropSpec { AssetPath = TxPlant, SpriteNames = new[] { "TX Plant_3", "TX Plant_4", "TX Plant_8" }, Scale = PlantScale } },
    };

    // 낮은 돌담: 상하좌우 이웃 담(위=1, 오른쪽=2, 아래=4, 왼쪽=8)에 맞는 StoneWall 조각.
    private static readonly string[] WallPieceByMask =
    {
        "Block", "EndBottom", "EndLeft", "CornerBL", "EndTop", "Vertical", "CornerTL", "TeeRight",
        "EndRight", "CornerBR", "Horizontal", "TeeUp", "CornerTR", "TeeLeft", "TeeDown", "Cross",
    };

    [MenuItem("Tools/Village Rooms/Validate Layouts")]
    public static void ValidateLayoutsMenu()
    {
        foreach (string path in LayoutPaths())
        {
            VillageRoomLayout layout = VillageRoomLayout.Parse(File.ReadAllText(path), path);
            VillageRoomLayout.Report report = layout.Analyze();
            string text = layout.Summarize(report);
            if (report.Ok) Debug.Log(text); else Debug.LogError(text);
        }
    }

    [MenuItem("Tools/Village Rooms/Build Room Prefabs")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll();
    }

    [MenuItem("Tools/Village Rooms/Register Rooms in VillageScene")]
    public static void RegisterMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RegisterInVillageScene(LayoutPaths().Select(p => PrefabPathFor(VillageRoomLayout.Parse(File.ReadAllText(p), p).Name)).ToList());
    }

    [MenuItem("Tools/Village Rooms/Capture Room Previews")]
    public static void CaptureMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CaptureRoomPreviews();
    }

    /// <summary>batchmode 진입점: 레이아웃 검증 → 프리팹 생성 → VillageScene 등록 → 방 미리보기 캡처.</summary>
    public static void BatchBuildRegisterCapture()
    {
        bool ok = false;
        try
        {
            List<string> built = BuildAll();
            ok = built != null;
            if (ok) ok = RegisterInVillageScene(built);
            if (ok) CaptureRoomPreviews();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            ok = false;
        }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>batchmode 진입점: 미리보기 캡처만.</summary>
    public static void BatchCapture()
    {
        bool ok = true;
        try { CaptureRoomPreviews(); }
        catch (System.Exception e) { Debug.LogException(e); ok = false; }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static IEnumerable<string> LayoutPaths() =>
        Directory.GetFiles(LayoutFolder, "*.txt").Select(p => p.Replace('\\', '/')).OrderBy(p => p);

    private static string PrefabPathFor(string roomName) => $"{PrefabFolder}/{roomName}.prefab";

    /// <summary>모든 레이아웃을 검증하고 프리팹을 만든다. 하나라도 검증에 실패하면 아무것도 저장하지 않고 null.</summary>
    public static List<string> BuildAll()
    {
        List<(VillageRoomLayout layout, VillageRoomLayout.Report report)> layouts = new List<(VillageRoomLayout, VillageRoomLayout.Report)>();
        bool allOk = true;
        foreach (string path in LayoutPaths())
        {
            VillageRoomLayout layout = VillageRoomLayout.Parse(File.ReadAllText(path), path);
            VillageRoomLayout.Report report = layout.Analyze();
            string text = layout.Summarize(report);
            if (report.Ok) Debug.Log("[VillageRoomBuilder] " + text);
            else { Debug.LogError("[VillageRoomBuilder] 검증 실패 - 프리팹을 만들지 않는다.\n" + text); allOk = false; }
            layouts.Add((layout, report));
        }
        if (!allOk) return null;

        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
        TileBase grass = AssetDatabase.LoadAssetAtPath<TileBase>(GrassTilePath);
        TileBase pathTile = AssetDatabase.LoadAssetAtPath<TileBase>(PathTilePath);
        if (template == null || grass == null || pathTile == null)
        {
            Debug.LogError($"[VillageRoomBuilder] 템플릿/타일을 찾지 못했다: {TemplatePrefabPath}, {GrassTilePath}, {PathTilePath}");
            return null;
        }

        List<string> saved = new List<string>();
        foreach (var entry in layouts)
        {
            VillageRoomLayout layout = entry.layout;
            GameObject root = BuildRoom(layout, template, grass, pathTile);
            string prefabPath = PrefabPathFor(layout.Name);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            Object.DestroyImmediate(root);
            if (!success) { Debug.LogError($"[VillageRoomBuilder] 프리팹 저장 실패: {prefabPath}"); return null; }
            Debug.Log($"[VillageRoomBuilder] 저장: {prefabPath}");
            saved.Add(prefabPath);
        }
        AssetDatabase.SaveAssets();
        return saved;
    }

    private static GameObject BuildRoom(VillageRoomLayout layout, GameObject template, TileBase grass, TileBase pathTile)
    {
        Tilemap templateGround = template.transform.Find("Ground").GetComponent<Tilemap>();
        TilemapRenderer templateGroundRenderer = templateGround.GetComponent<TilemapRenderer>();
        SpriteRenderer templateObstacleRenderer = template.transform.Find("Obstacles").GetComponentInChildren<SpriteRenderer>();
        BoxCollider2D templateObstacleCollider = template.transform.Find("Obstacles").GetComponentInChildren<BoxCollider2D>();

        GameObject root = new GameObject(layout.Name);
        Grid grid = root.AddComponent<Grid>();
        grid.cellSize = template.GetComponent<Grid>().cellSize;

        Tilemap ground = CreateTilemap(root.transform, "Ground", templateGround, templateGroundRenderer);
        Tilemap path = null;

        Transform obstacles = new GameObject("Obstacles").transform;
        obstacles.SetParent(root.transform, false);
        Transform decorations = new GameObject("Decorations").transform;
        decorations.SetParent(root.transform, false);

        List<string> errors = new List<string>();
        HashSet<(int, int)> bigRockCells = new HashSet<(int, int)>();
        foreach ((int x, int y) block in layout.GroupBigRocks(errors))
        {
            bigRockCells.Add((block.x, block.y)); bigRockCells.Add((block.x + 1, block.y));
            bigRockCells.Add((block.x, block.y + 1)); bigRockCells.Add((block.x + 1, block.y + 1));
            Vector2 center = new Vector2(block.x + 1f, block.y + 1f);
            GameObject rock = CreateProp(obstacles, "Obstacle", Props["BigRock"], block.x, block.y, center, templateObstacleRenderer);
            BoxCollider2D box = rock.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2f, 2f) - Vector2.one * (1f - templateObstacleCollider.size.x);
        }

        for (int y = layout.MinY; y <= layout.MaxY; y++)
        {
            for (int x = layout.MinX; x <= layout.MaxX; x++)
            {
                VillageRoomLayout.Glyph glyph = layout.GlyphAt(x, y);
                if (glyph.Kind == VillageRoomLayout.CellKind.Void) continue;

                ground.SetTile(new Vector3Int(x, y, 0), grass);
                if (layout.HasPathTile(x, y))
                {
                    if (path == null) path = CreatePathTilemap(root.transform, templateGround, templateGroundRenderer);
                    path.SetTile(new Vector3Int(x, y, 0), pathTile);
                }

                Vector2 cellCenter = new Vector2(x + 0.5f, y + 0.5f);
                if (glyph.Kind == VillageRoomLayout.CellKind.Blocking)
                {
                    if (bigRockCells.Contains((x, y))) continue;
                    PropSpec spec = glyph.Prop == "Wall" ? WallSpec(layout, x, y) : Props[glyph.Prop];
                    GameObject obstacle = CreateProp(obstacles, "Obstacle", spec, x, y, cellCenter, templateObstacleRenderer);
                    BoxCollider2D box = obstacle.AddComponent<BoxCollider2D>();
                    box.size = templateObstacleCollider.size;
                    // 스프라이트를 올려 그렸으면 콜라이더는 원래 칸에 남겨둔다 - GetRoomObstacles는 콜라이더 범위로 칸을 읽는다.
                    box.offset = cellCenter - (Vector2)obstacle.transform.localPosition;
                }
                else if (glyph.Kind == VillageRoomLayout.CellKind.Decor)
                {
                    CreateProp(decorations, "Decor", Props[glyph.Prop], x, y, cellCenter, templateObstacleRenderer);
                }
            }
        }

        // 기존 방처럼 출입구 위치를 빈 마커로 남겨둔다(코드에서 쓰지는 않는다).
        Transform exits = new GameObject("Exits").transform;
        exits.SetParent(root.transform, false);
        float doorwayCenterY = (VillageRoomLayout.DoorwayMinY + VillageRoomLayout.DoorwayMaxY + 1) * 0.5f;
        CreateMarker(exits, "Exit_West", new Vector2(layout.MinX + 0.5f, doorwayCenterY));
        CreateMarker(exits, "Exit_East", new Vector2(layout.MaxX + 0.5f, doorwayCenterY));

        ground.CompressBounds();
        if (path != null) path.CompressBounds();
        return root;
    }

    private static Tilemap CreateTilemap(Transform parent, string name, Tilemap template, TilemapRenderer templateRenderer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Tilemap tilemap = go.AddComponent<Tilemap>();
        tilemap.tileAnchor = template.tileAnchor;
        tilemap.orientation = template.orientation;
        tilemap.color = template.color;
        TilemapRenderer renderer = go.AddComponent<TilemapRenderer>();
        renderer.sharedMaterial = templateRenderer.sharedMaterial;
        renderer.sortingLayerID = templateRenderer.sortingLayerID;
        renderer.sortingOrder = templateRenderer.sortingOrder;
        renderer.mode = templateRenderer.mode;
        return tilemap;
    }

    private static Tilemap CreatePathTilemap(Transform parent, Tilemap template, TilemapRenderer templateRenderer)
    {
        Tilemap path = CreateTilemap(parent, "Path", template, templateRenderer);
        TilemapRenderer renderer = path.GetComponent<TilemapRenderer>();
        renderer.sortingLayerName = FloorSortingLayer;
        renderer.sortingOrder = PathSortingOrder;
        path.transform.SetSiblingIndex(1); // Ground 바로 다음
        return path;
    }

    private static GameObject CreateProp(Transform parent, string name, PropSpec spec, int x, int y, Vector2 anchor, SpriteRenderer template)
    {
        string spriteName = spec.SpriteNames[PickIndex(x, y, spec.SpriteNames.Length)];
        Sprite sprite = LoadSprite(spec.AssetPath, spriteName);

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(spec.Scale, spec.Scale, 1f);

        Vector2 position = anchor;
        if (spec.TrunkOnCell)
        {
            // 피벗이 가운데라 그대로 두면 장애물 칸이 수관 한가운데가 되고 줄기는 2칸쯤 아래에 그려진다. 밑동이 칸 바닥 근처에 오도록 올린다.
            float halfHeight = sprite.bounds.extents.y * spec.Scale;
            position.y = y + halfHeight - 0.15f;
        }
        go.transform.localPosition = new Vector3(position.x, position.y, 0f);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = template.sharedMaterial;
        if (spec.Flat)
        {
            renderer.sortingLayerName = FloorSortingLayer;
            renderer.sortingOrder = FlatDecorSortingOrder;
        }
        else
        {
            renderer.sortingLayerID = template.sortingLayerID;
            renderer.sortingOrder = template.sortingOrder;
        }
        return go;
    }

    private static PropSpec WallSpec(VillageRoomLayout layout, int x, int y)
    {
        int mask = (layout[x, y + 1] == 'W' ? 1 : 0) | (layout[x + 1, y] == 'W' ? 2 : 0) |
                   (layout[x, y - 1] == 'W' ? 4 : 0) | (layout[x - 1, y] == 'W' ? 8 : 0);
        string piece = "StoneWall_" + WallPieceByMask[mask];
        return new PropSpec { AssetPath = StoneWall + piece + ".png", SpriteNames = new[] { piece } };
    }

    private static void CreateMarker(Transform parent, string name, Vector2 position)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(position.x, position.y, 0f);
    }

    private static int PickIndex(int x, int y, int count)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663;
            return (int)((uint)h % (uint)count);
        }
    }

    private static readonly Dictionary<string, Sprite[]> SpriteCache = new Dictionary<string, Sprite[]>();

    private static Sprite LoadSprite(string assetPath, string spriteName)
    {
        if (!SpriteCache.TryGetValue(assetPath, out Sprite[] sprites))
        {
            sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().ToArray();
            SpriteCache[assetPath] = sprites;
        }
        Sprite sprite = sprites.FirstOrDefault(s => s.name == spriteName);
        if (sprite == null) throw new System.InvalidOperationException($"스프라이트 '{spriteName}'을(를) {assetPath}에서 찾지 못했다.");
        return sprite;
    }

    /// <summary>VillageScene의 MapGenerator → roomPrefabs 끝에 아직 없는 프리팹만 추가한다(기존 항목은 그대로 둔다).</summary>
    public static bool RegisterInVillageScene(List<string> prefabPaths)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        VillageMapGenerator generator = Object.FindAnyObjectByType<VillageMapGenerator>();
        if (generator == null) { Debug.LogError("[VillageRoomBuilder] VillageScene에서 VillageMapGenerator를 찾지 못했다."); return false; }

        SerializedObject so = new SerializedObject(generator);
        SerializedProperty list = so.FindProperty("roomPrefabs");
        HashSet<Object> existing = new HashSet<Object>();
        for (int i = 0; i < list.arraySize; i++) existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

        int added = 0;
        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogError($"[VillageRoomBuilder] 프리팹이 없다: {path}"); return false; }
            if (existing.Contains(prefab)) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = prefab;
            added++;
        }
        if (added > 0)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        string names = string.Join(", ", Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue?.name ?? "(비어 있음)"));
        Debug.Log($"[VillageRoomBuilder] roomPrefabs {added}개 추가 → 현재 {list.arraySize}개: {names}");
        return true;
    }

    /// <summary>방 프리팹마다 위에서 내려다본 모습을 Logs/VillageRoomCaptures/prefab_*.png로 저장한다(칸당 32px).</summary>
    public static void CaptureRoomPreviews()
    {
        Directory.CreateDirectory(CaptureFolder);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Light2D light = new GameObject("PreviewLight").AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;

        foreach (string prefabPath in Directory.GetFiles(PrefabFolder, "*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Tilemap ground = room.transform.Find("Ground").GetComponent<Tilemap>();
            ground.GetComponent<TilemapRenderer>().sortingLayerName = FloorSortingLayer; // 생성기가 런타임에 하는 것과 같다
            ground.CompressBounds();
            BoundsInt cb = ground.cellBounds;
            string file = $"{CaptureFolder}/prefab_{prefab.name}.png";
            CaptureArea(new Rect(cb.xMin - 1, cb.yMin - 1, cb.size.x + 2, cb.size.y + 2), 32, file);
            Object.DestroyImmediate(room);
            Debug.Log($"[VillageRoomBuilder] 캡처: {file}");
        }
    }

    /// <summary>월드 영역을 칸당 pixelsPerUnit 픽셀로 렌더해 PNG로 저장한다(테스트 하네스도 쓴다).</summary>
    public static void CaptureArea(Rect area, int pixelsPerUnit, string file)
    {
        int width = Mathf.Clamp(Mathf.RoundToInt(area.width * pixelsPerUnit), 16, 8192);
        int height = Mathf.Clamp(Mathf.RoundToInt(area.height * pixelsPerUnit), 16, 8192);

        GameObject camGo = new GameObject("CaptureCamera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = area.height * 0.5f;
        cam.aspect = (float)width / height;
        cam.transform.position = new Vector3(area.center.x, area.center.y, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
    }
}
