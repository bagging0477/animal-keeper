using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Monsters > Build Weeping Angel (sprite + prefab + phase 2 spawn).
/// 1) 임시 스프라이트를 그려 Assets/Sprites/Monsters/WeepingAngel_Placeholder.png로 저장한다 - 흰 몸통·머리, 옅은 회색 날개, 검은 테두리(40x48px, PPU 32, Point).
/// 2) 늑대 프리팹(PatrolMonster)을 복사해 WeepingAngel 프리팹을 만든다 - NavMeshAgent와 Visual(SpriteRenderer, AnimalFacingFlipper)은 그대로 쓰고,
///    MonsterHealth·MonsterCorpsePickup·MonsterAI·SimpleFrameAnimator는 뺀 뒤 WeepingAngelAI를 붙인다(체력 없음, 애니메이션 없음).
///    그림은 WeepingAngelAI.bodySprite 칸에 넣어 나중에 바꿀 수 있게 한다. 경고음 칸(warningClip)은 비워 둔다.
/// 3) GameBalanceConfig.villagePhaseSpawns를 2구간에 우는천사 1마리(구간 진입 때 1, 간격 없음)로 두고 다른 구간은 비운다.
/// batchmode: -executeMethod WeepingAngelAssetBuilder.BuildBatch  (끝나면 에디터가 종료된다)
/// </summary>
public static class WeepingAngelAssetBuilder
{
    public const string SpritePath = "Assets/Sprites/Monsters/WeepingAngel_Placeholder.png";
    public const string PrefabPath = "Assets/Prefabs/Monsters/WeepingAngel.prefab";
    private const string SourcePrefabPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const int Width = 40, Height = 48, PixelsPerUnit = 32;

    [MenuItem("Tools/Monsters/Build Weeping Angel (sprite + prefab + phase 2 spawn)")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        Sprite sprite = BuildSprite();
        if (sprite == null) { Debug.LogError("[WeepingAngelAssetBuilder] 스프라이트를 만들지 못했다"); return false; }
        GameObject prefab = BuildPrefab(sprite);
        if (prefab == null) { Debug.LogError("[WeepingAngelAssetBuilder] 프리팹을 만들지 못했다"); return false; }
        if (!LinkPhaseSpawn(prefab)) return false;
        Debug.Log($"[WeepingAngelAssetBuilder] 완료: {SpritePath}, {PrefabPath}, 2구간 소환 목록 연결");
        return true;
    }

    // ===================== 스프라이트 =====================

    private static Sprite BuildSprite()
    {
        Color clear = new Color(0f, 0f, 0f, 0f), white = Color.white, wing = new Color(0.86f, 0.88f, 0.92f, 1f), outline = Color.black;
        // 0 빈칸, 1 날개, 2 몸통/머리
        int[,] mask = new int[Width, Height];
        Vector2[] leftWing = { new Vector2(16f, 33f), new Vector2(2f, 42f), new Vector2(3f, 28f), new Vector2(6f, 20f), new Vector2(15f, 17f) };
        Vector2[] rightWing = new Vector2[leftWing.Length];
        for (int i = 0; i < leftWing.Length; i++) rightWing[i] = new Vector2(Width - 1 - leftWing[i].x, leftWing[i].y);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (InPolygon(p, leftWing) || InPolygon(p, rightWing)) mask[x, y] = 1;
                // 머리: 원, 몸통: 아래로 넓어지는 옷자락
                bool head = (p - new Vector2(Width * 0.5f, 39.5f)).sqrMagnitude <= 5.2f * 5.2f;
                float t = Mathf.InverseLerp(4f, 33f, p.y);
                bool body = p.y >= 4f && p.y <= 33f && Mathf.Abs(p.x - Width * 0.5f) <= Mathf.Lerp(10f, 4.5f, t);
                if (head || body) mask[x, y] = 2;
            }
        }

        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                int m = mask[x, y];
                Color c = m == 2 ? white : m == 1 ? wing : clear;
                // 바깥 테두리(빈칸인데 이웃에 그림이 있음)와 날개-몸통 경계(날개인데 이웃이 몸통)를 검게 칠한다.
                if (m == 0 && HasNeighbor(mask, x, y, v => v != 0, diagonal: true)) c = outline;
                else if (m == 1 && HasNeighbor(mask, x, y, v => v == 2, diagonal: false)) c = outline;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
        File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.Refresh(); // 새 폴더(Assets/Sprites/Monsters)도 함께 등록한다
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }

    private static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    private static bool HasNeighbor(int[,] mask, int x, int y, System.Func<int, bool> match, bool diagonal)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (!diagonal && dx != 0 && dy != 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                if (match(mask[nx, ny])) return true;
            }
        }
        return false;
    }

    // ===================== 프리팹 =====================

    private static GameObject BuildPrefab(Sprite sprite)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) AssetDatabase.DeleteAsset(PrefabPath);
        if (!AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath)) return null;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "WeepingAngel";
            Transform visual = root.transform.Find("Visual");
            // 다른 컴포넌트가 요구하는(RequireComponent) 것보다 먼저 뺀다: 시체 줍기·늑대 AI → MonsterHealth.
            Object.DestroyImmediate(root.GetComponent<MonsterCorpsePickup>(), true);
            Object.DestroyImmediate(root.GetComponent<MonsterAI>(), true);
            Object.DestroyImmediate(root.GetComponent<MonsterHealth>(), true);
            if (visual != null) Object.DestroyImmediate(visual.GetComponent<SimpleFrameAnimator>(), true);

            SpriteRenderer renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            if (renderer != null)
            {
                renderer.sprite = sprite;
                renderer.color = Color.white;
            }

            WeepingAngelAI ai = root.AddComponent<WeepingAngelAI>();
            SerializedObject so = new SerializedObject(ai);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("bodySprite").objectReferenceValue = sprite;
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
            so.FindProperty("warningClip").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    // ===================== 2구간 소환 목록 =====================

    private static bool LinkPhaseSpawn(GameObject prefab)
    {
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        if (config == null) { Debug.LogError($"[WeepingAngelAssetBuilder] {ConfigPath}가 없다"); return false; }
        SerializedObject so = new SerializedObject(config);
        SerializedProperty lists = so.FindProperty("villagePhaseSpawns");
        lists.arraySize = 5;
        for (int i = 0; i < 5; i++) lists.GetArrayElementAtIndex(i).FindPropertyRelative("entries").arraySize = 0;
        SerializedProperty entries = lists.GetArrayElementAtIndex(1).FindPropertyRelative("entries");
        entries.arraySize = 1;
        SerializedProperty entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("monsterPrefab").objectReferenceValue = prefab;
        entry.FindPropertyRelative("count").intValue = 1;
        entry.FindPropertyRelative("initialCount").intValue = 1;
        entry.FindPropertyRelative("intervalCount").intValue = 0;
        entry.FindPropertyRelative("intervalSeconds").floatValue = 0f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        return true;
    }
}
