using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Monsters > Build Bat Monster (sprites + prefab). 예전 우는천사 프리팹 자리(같은 GUID·루트)를 박쥐로 다시 꾸민다.
/// 1) Assets/Sprites/Monster/bat-monster-32-unity/BatMonster32/Frames의 32x32 PNG 16장을 동물과 같은 기준으로 가져온다:
///    Sprite(Single), PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데(사용방법.txt 3번). Visual 배율 2 → 1텍셀 = 1/16칸(1080p 약 6px).
/// 2) 프리팹: 루트(NavMeshAgent, BatMonsterAI, MonsterHealth, MonsterCorpsePickup) 아래 Visual(땅 위치, 배율 2, FootSortOrder) 아래
///    Body(날아다니는 몸: SpriteRenderer, SimpleFrameAnimator, AnimalFacingFlipper)와 Shadow(바닥 그림자)를 따로 둔다(사용방법.txt 7번).
///    MonsterHealth·MonsterCorpsePickup 값은 늑대(PatrolMonster)에서 복사하고, 박쥐만 최대 체력 -1·기절/마취 무시를 켠다.
///    그림자는 몸 폭(날개 접은 웅크림 프레임 14텍셀) x 0.75 → 10x3(CharacterShadowBuilder와 같은 규칙, 거위와 같은 Shadow_10x3.png).
///    그림자 맨 윗줄 = 몸 가운데 아래 14텍셀(비행 프레임의 몸 아랫줄 -10~-12에서 2~4텍셀 띄운 바닥). 발 기준 정렬(FootSortOrder)의 발도 그 줄이다.
/// 3) 시간 소환 목록(villagePhaseSpawns)은 프리팹 GUID와 루트가 그대로라 건드리지 않는다.
/// CharacterShadowBuilder는 박쥐를 대상에서 뺐다(그림자를 몸 자식이 아니라 바닥에 따로 두기 때문) - 그림자는 이 빌더가 맡는다.
/// batchmode: -executeMethod BatMonsterAssetBuilder.BuildBatch  (끝나면 에디터가 종료된다)
/// </summary>
public static class BatMonsterAssetBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Monsters/BatMonster.prefab";
    public const string FramesFolder = "Assets/Sprites/Monster/bat-monster-32-unity/BatMonster32/Frames";
    private const string WolfPrefabPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const float PixelsPerUnit = 32f;
    private const float VisualScale = 2f;
    public const int ShadowW = 10, ShadowH = 3;
    public const float ShadowTopTexels = -14f; // 몸 피벗(가운데) 기준, 그림자 맨 윗줄의 위 변
    public const float FrameRate = 10f, FleeFrameRate = 14f;
    public const float WrapFrameRate = 7f; // 사용방법.txt: wrap 6~8 fps의 가운데
    public static readonly Vector2 DartHitOffset = new Vector2(0f, -0.1f); // 마취 다트 맞힘 원 가운데(땅 위치 기준, 칸)
    public const float DartHitRadius = 0.65f;                              // 마취 다트 맞힘 반경(칸)

    [MenuItem("Tools/Monsters/Build Bat Monster (sprites + prefab)")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        if (!ImportFrames()) return false;
        if (!BuildPrefab()) return false;
        Debug.Log($"[BatMonsterAssetBuilder] 완료: {FramesFolder} 16장 가져오기, {PrefabPath}");
        return true;
    }

    public static Sprite Frame(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{FramesFolder}/{name}.png");
    private static Sprite[] Frames(params string[] names) => names.Select(Frame).ToArray();

    // ===================== 그림 가져오기 =====================

    private static readonly string[] FrameNames =
    {
        "fly-01", "fly-02", "fly-03", "fly-04", "fly-05", "fly-06", "freeze-01", "bite-01", "bite-02", "bite-03", "bite-04",
        "wrap-01", "wrap-02", "wrap-03", "hurt-01", "fallen-01",
    };

    private static bool ImportFrames()
    {
        foreach (string n in FrameNames)
        {
            string path = $"{FramesFolder}/{n}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError($"[BatMonsterAssetBuilder] {path}가 없다"); return false; }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            if (Frame(n) == null) { Debug.LogError($"[BatMonsterAssetBuilder] {path}를 스프라이트로 가져오지 못했다"); return false; }
        }
        return true;
    }

    // ===================== 프리팹 =====================

    private static bool BuildPrefab()
    {
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefabPath);
        Sprite shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CharacterShadowBuilder.SpritePath(ShadowW, ShadowH));
        if (config == null || wolf == null || shadowSprite == null)
        {
            Debug.LogError($"[BatMonsterAssetBuilder] 준비 실패: config {config != null}, 늑대 프리팹 {wolf != null}, 그림자 {CharacterShadowBuilder.SpritePath(ShadowW, ShadowH)} {shadowSprite != null}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "BatMonster";
            Transform visual = root.transform.Find("Visual");
            if (visual == null) { visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false); }

            // 예전 몸(Visual의 SpriteRenderer)에서 머티리얼·정렬 레이어를 이어받는다.
            SpriteRenderer oldRenderer = visual.GetComponent<SpriteRenderer>();
            Material material = oldRenderer != null ? oldRenderer.sharedMaterial : wolf.GetComponentInChildren<SpriteRenderer>(true).sharedMaterial;
            int sortingLayer = oldRenderer != null ? oldRenderer.sortingLayerID : 0;
            int sortingOrder = oldRenderer != null ? oldRenderer.sortingOrder : 2;
            foreach (Transform child in visual.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject, true);
            // 다른 컴포넌트가 요구하는(RequireComponent) SpriteRenderer는 마지막에 뺀다.
            if (visual.GetComponent<AnimalFacingFlipper>() != null) Object.DestroyImmediate(visual.GetComponent<AnimalFacingFlipper>(), true);
            if (visual.GetComponent<SimpleFrameAnimator>() != null) Object.DestroyImmediate(visual.GetComponent<SimpleFrameAnimator>(), true);
            if (oldRenderer != null) Object.DestroyImmediate(oldRenderer, true);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = new Vector3(VisualScale, VisualScale, 1f);

            // Body가 첫 자식이어야 MonsterHealth(GetComponentInChildren<SpriteRenderer>)가 시체 색을 몸에 칠한다.
            Transform body = new GameObject("Body").transform;
            body.SetParent(visual, false);
            body.gameObject.layer = visual.gameObject.layer;
            SpriteRenderer bodyRenderer = body.gameObject.AddComponent<SpriteRenderer>();
            bodyRenderer.sprite = Frame("fly-01");
            bodyRenderer.sharedMaterial = material;
            bodyRenderer.sortingLayerID = sortingLayer;
            bodyRenderer.sortingOrder = sortingOrder;
            SimpleFrameAnimator anim = body.gameObject.AddComponent<SimpleFrameAnimator>();
            body.gameObject.AddComponent<AnimalFacingFlipper>();
            SerializedObject a = new SerializedObject(anim);
            a.FindProperty("idleSprite").objectReferenceValue = Frame("fly-01");
            SetArray(a, "walkFrames", Frames("fly-01", "fly-02", "fly-03", "fly-04", "fly-05", "fly-06"));
            SetArray(a, "fleeFrames", Frames("fly-01", "fly-02", "fly-03", "fly-04", "fly-05", "fly-06"));
            a.FindProperty("frameRate").floatValue = FrameRate;
            a.FindProperty("fleeFrameRate").floatValue = FleeFrameRate;
            SetArray(a, "attack1Frames", Frames("bite-01", "bite-02", "bite-03", "bite-04"));
            SetArray(a, "attack2Frames", new Sprite[0]);
            SetArray(a, "hurtFrames", Frames("hurt-01"));
            a.FindProperty("flashWithHurtFrames").boolValue = true; // 피격: hurt-01 + 늑대와 같은 빨간 깜빡임(0.15초)
            SetArray(a, "deathFrames", Frames("hurt-01", "fallen-01"));
            a.ApplyModifiedPropertiesWithoutUndo();

            Transform shadow = new GameObject("Shadow").transform;
            shadow.SetParent(visual, false);
            shadow.gameObject.layer = visual.gameObject.layer;
            shadow.localPosition = new Vector3(0f, (ShadowTopTexels - ShadowH) / PixelsPerUnit, 0f); // 아래 가운데 피벗
            SpriteRenderer shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = shadowSprite;
            shadowRenderer.sharedMaterial = material;
            shadowRenderer.sortingLayerID = sortingLayer;
            shadowRenderer.sortingOrder = sortingOrder - 1;
            shadowRenderer.color = Color.white;

            FootSortOrder foot = visual.GetComponent<FootSortOrder>();
            if (foot == null) foot = visual.gameObject.AddComponent<FootSortOrder>();
            SerializedObject f = new SerializedObject(foot);
            f.FindProperty("body").objectReferenceValue = bodyRenderer;
            f.FindProperty("footTexels").floatValue = ShadowTopTexels;
            f.FindProperty("texelsPerUnit").floatValue = PixelsPerUnit;
            f.FindProperty("useBoundsBottom").boolValue = false;
            f.FindProperty("useWorldOffset").boolValue = false;
            f.FindProperty("isStatic").boolValue = false;
            f.FindProperty("shadow").objectReferenceValue = shadowRenderer;
            f.ApplyModifiedPropertiesWithoutUndo();

            // 체력·시체: 늑대 값을 그대로 복사하고 박쥐만 다른 값을 켠다.
            MonsterHealth health = root.GetComponent<MonsterHealth>();
            if (health == null) health = root.AddComponent<MonsterHealth>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterHealth>(), health);
            SerializedObject h = new SerializedObject(health);
            h.FindProperty("maxHealthOffset").intValue = -1;
            h.FindProperty("ignoreStunAndSleep").boolValue = true;
            // 다트 맞힘 원: 몸 그림(약 1.1~1.3칸 폭, 웅크림은 땅 위치보다 아래)을 덮게 땅 위치 0.1칸 아래, 반경 0.65.
            h.FindProperty("dartHitOffset").vector2Value = DartHitOffset;
            h.FindProperty("dartHitRadius").floatValue = DartHitRadius;
            h.ApplyModifiedPropertiesWithoutUndo();
            MonsterCorpsePickup corpse = root.GetComponent<MonsterCorpsePickup>();
            if (corpse == null) corpse = root.AddComponent<MonsterCorpsePickup>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterCorpsePickup>(), corpse);
            SerializedObject c = new SerializedObject(corpse);
            c.FindProperty("inventoryIcon").objectReferenceValue = Frame("fly-01");
            c.ApplyModifiedPropertiesWithoutUndo();

            BatMonsterAI ai = root.GetComponent<BatMonsterAI>();
            if (ai == null) ai = root.AddComponent<BatMonsterAI>();
            SerializedObject so = new SerializedObject(ai);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("body").objectReferenceValue = body;
            SetArray(so, "wrapFrames", Frames("wrap-01", "wrap-02", "wrap-03"));
            so.FindProperty("wrapFrameRate").floatValue = WrapFrameRate;
            so.FindProperty("fallenDropTexels").floatValue = 3f;
            so.FindProperty("bodyTexelsPerUnit").floatValue = PixelsPerUnit;
            so.FindProperty("monsterTypeName").stringValue = "박쥐";
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("warningClip").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return true;
    }

    private static void SetArray(SerializedObject so, string name, Sprite[] sprites)
    {
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }
}
