using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tools > Monsters > Build Porcupine (sprites + prefabs). 호저(제자리 터렛)와 가시 프리팹을 만든다. 스폰 목록에는 넣지 않는다.
/// 1) Assets/Sprites/Monster/porcupine-monster-32x32-unity/Porcupine32/Frames의 32x32 PNG 18장을 사용방법.txt 3번대로 가져온다:
///    Sprite(Single), PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데. Visual 배율 2 → 1텍셀 = 1/16칸(동물·박쥐와 같은 기준).
/// 2) 호저 프리팹: 루트(NavMeshAgent, MonsterHealth, MonsterCorpsePickup, PorcupineMonsterAI) 아래 Visual(땅 위치, 배율 2, FootSortOrder) 아래
///    Body(SpriteRenderer, SimpleFrameAnimator, AnimalFacingFlipper)와 Shadow를 따로 둔다(박쥐와 같은 짜임).
///    NavMeshAgent·MonsterHealth·MonsterCorpsePickup 값과 머티리얼·정렬 레이어는 늑대(PatrolMonster)에서 복사한다.
///    - 체력: maxHealthOffset = 3 − 늑대 최대 체력(ceil(monsterMaxHealth)) → 최대 체력 3.
///    - 발·그림자: idle 4장의 보이는 몸을 재서 정한다(CharacterShadowBuilder와 같은 규칙: 폭 x 0.75, 높이 = 폭 x 4/15, 맨 윗줄이 몸 맨 아랫줄과 겹침).
///    - 다트 맞힘 원: idle 몸 가운데(땅 위치 기준) 높이, 반경은 몸 반폭과 반높이의 평균.
/// 3) 가시 프리팹: SpriteRenderer(quill-01) + PorcupineQuill(quill-01·02 반복), 배율 2.
/// batchmode: -executeMethod PorcupineAssetBuilder.BuildBatch  (끝나면 에디터가 종료된다)
/// </summary>
public static class PorcupineAssetBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Monsters/PorcupineMonster.prefab";
    public const string QuillPrefabPath = "Assets/Prefabs/Monsters/PorcupineQuill.prefab";
    public const string FramesFolder = "Assets/Sprites/Monster/porcupine-monster-32x32-unity/Porcupine32/Frames";
    private const string WolfPrefabPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    public const float PixelsPerUnit = 32f;
    public const float VisualScale = 2f;
    public const float IdleFrameRate = 5f;   // 사용방법.txt: 대기 4~6 fps
    public const float RaiseFrameRate = 7f;  // 가시 세우기 6~8 fps
    public const float ShootFrameRate = 12f; // 발사 10~12 fps
    public const float QuillFrameRate = 10f; // 탄환 8~12 fps
    public const float QuillHitRadius = 0.45f;
    public const int TargetMaxHealth = 3;

    private static readonly string[] FrameNames =
    {
        "idle-01", "idle-02", "idle-03", "idle-04", "walk-01", "walk-02", "walk-03", "walk-04",
        "raise-01", "raise-02", "raise-03", "shoot-01", "shoot-02", "shoot-03", "hurt-01", "fallen-01", "quill-01", "quill-02",
    };

    /// <summary>idle 4장에서 잰 몸 치수(텍셀, 피벗 = 프레임 가운데 기준). 테스트도 쓴다.</summary>
    public struct BodyMetrics
    {
        public float FootTexels;      // 보이는 맨 아랫줄의 아래 변(가장 흔한 값)
        public float CenterXTexels;   // 보이는 몸 가로 가운데
        public float CenterYTexels;   // 보이는 몸 세로 가운데
        public int Width, Height;     // 보이는 몸 폭·높이(가장 흔한 값)
        public int ShadowW, ShadowH;
    }

    [MenuItem("Tools/Monsters/Build Porcupine (sprites + prefabs)")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        if (!ImportFrames()) return false;
        if (!BuildQuillPrefab()) return false;
        if (!BuildPrefab(out string summary)) return false;
        Debug.Log($"[PorcupineAssetBuilder] 완료: {FramesFolder} {FrameNames.Length}장, {PrefabPath}, {QuillPrefabPath} - {summary}");
        return true;
    }

    public static Sprite Frame(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{FramesFolder}/{name}.png");
    private static Sprite[] Frames(params string[] names) => names.Select(Frame).ToArray();

    // ===================== 그림 가져오기 =====================

    private static bool ImportFrames()
    {
        foreach (string n in FrameNames)
        {
            string path = $"{FramesFolder}/{n}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError($"[PorcupineAssetBuilder] {path}가 없다"); return false; }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            if (Frame(n) == null) { Debug.LogError($"[PorcupineAssetBuilder] {path}를 스프라이트로 가져오지 못했다"); return false; }
        }
        return true;
    }

    // ===================== 몸 치수 =====================

    /// <summary>idle 4장의 보이는 픽셀로 발·가운데·폭을 잰다(파일을 직접 읽는다 - 가져오기 설정과 무관).</summary>
    public static BodyMetrics MeasureIdle()
    {
        List<int> feet = new List<int>(), widths = new List<int>(), heights = new List<int>();
        List<float> centersX = new List<float>(), centersY = new List<float>();
        foreach (string n in new[] { "idle-01", "idle-02", "idle-03", "idle-04" })
        {
            Texture2D t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes($"{FramesFolder}/{n}.png"));
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++)
                {
                    if (t.GetPixel(x, y).a < 0.5f) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            float half = t.width * 0.5f, halfH = t.height * 0.5f;
            feet.Add(Mathf.RoundToInt(minY - halfH));                 // 텍스처는 아래가 0: 맨 아랫줄의 아래 변 = minY
            widths.Add(maxX - minX + 1);
            heights.Add(maxY - minY + 1);
            centersX.Add((minX + maxX + 1) * 0.5f - half);
            centersY.Add((minY + maxY + 1) * 0.5f - halfH);
            Object.DestroyImmediate(t);
        }
        int Mode(List<int> v) => v.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        BodyMetrics m = new BodyMetrics
        {
            FootTexels = Mode(feet),
            Width = Mode(widths),
            Height = Mode(heights),
            CenterXTexels = Mathf.Round(centersX.Average() * 2f) / 2f,
            CenterYTexels = Mathf.Round(centersY.Average() * 2f) / 2f,
        };
        m.ShadowW = Mathf.Max(3, Mathf.RoundToInt(m.Width * 0.75f));
        m.ShadowH = Mathf.Max(1, Mathf.RoundToInt(m.ShadowW * 4f / 15f));
        return m;
    }

    // ===================== 가시 프리팹 =====================

    private static bool BuildQuillPrefab()
    {
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefabPath);
        SpriteRenderer wolfBody = wolf != null ? wolf.transform.Find("Visual")?.GetComponent<SpriteRenderer>() : null;
        if (wolfBody == null) { Debug.LogError("[PorcupineAssetBuilder] 늑대 Visual SpriteRenderer를 찾지 못했다"); return false; }

        GameObject root = new GameObject("PorcupineQuill");
        try
        {
            root.layer = wolfBody.gameObject.layer;
            root.transform.localScale = new Vector3(VisualScale, VisualScale, 1f);
            SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = Frame("quill-01");
            sr.sharedMaterial = wolfBody.sharedMaterial;
            sr.sortingLayerID = wolfBody.sortingLayerID;
            PorcupineQuill quill = root.AddComponent<PorcupineQuill>();
            BodyMetrics m = MeasureIdle();
            SerializedObject q = new SerializedObject(quill);
            SetArray(q, "frames", Frames("quill-01", "quill-02"));
            q.FindProperty("frameRate").floatValue = QuillFrameRate;
            q.FindProperty("hitRadius").floatValue = QuillHitRadius;
            q.FindProperty("wallMask").intValue = ~0;
            // 몸 가운데(가시가 나가는 높이) − 발 = 땅에서 뜬 높이
            q.FindProperty("heightAboveGround").floatValue = (m.CenterYTexels - m.FootTexels) / PixelsPerUnit * VisualScale;
            q.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, QuillPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        return true;
    }

    // ===================== 호저 프리팹 =====================

    private static bool BuildPrefab(out string summary)
    {
        summary = "";
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefabPath);
        PorcupineQuill quillPrefab = AssetDatabase.LoadAssetAtPath<PorcupineQuill>(QuillPrefabPath);
        BodyMetrics m = MeasureIdle();
        Sprite shadowSprite = EnsureShadowSprite(m.ShadowW, m.ShadowH);
        SpriteRenderer wolfBody = wolf != null ? wolf.transform.Find("Visual")?.GetComponent<SpriteRenderer>() : null;
        if (config == null || wolf == null || wolfBody == null || quillPrefab == null || shadowSprite == null)
        {
            Debug.LogError($"[PorcupineAssetBuilder] 준비 실패: config {config != null}, 늑대 {wolf != null}, 가시 {quillPrefab != null}, " +
                           $"그림자 {CharacterShadowBuilder.SpritePath(m.ShadowW, m.ShadowH)} {shadowSprite != null}");
            return false;
        }

        GameObject root = new GameObject("PorcupineMonster");
        try
        {
            root.layer = wolf.layer;
            root.tag = wolf.tag;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            EditorUtility.CopySerialized(wolf.GetComponent<NavMeshAgent>(), agent);
            agent.speed = 0f;

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.gameObject.layer = wolfBody.gameObject.layer;
            visual.localScale = new Vector3(VisualScale, VisualScale, 1f);

            // Body가 첫 자식이어야 MonsterHealth(GetComponentInChildren<SpriteRenderer>)가 시체 색을 몸에 칠한다.
            Transform body = new GameObject("Body").transform;
            body.SetParent(visual, false);
            body.gameObject.layer = visual.gameObject.layer;
            SpriteRenderer bodyRenderer = body.gameObject.AddComponent<SpriteRenderer>();
            bodyRenderer.sprite = Frame("idle-01");
            bodyRenderer.sharedMaterial = wolfBody.sharedMaterial;
            bodyRenderer.sortingLayerID = wolfBody.sortingLayerID;
            bodyRenderer.sortingOrder = wolfBody.sortingOrder;
            SimpleFrameAnimator anim = body.gameObject.AddComponent<SimpleFrameAnimator>();
            body.gameObject.AddComponent<AnimalFacingFlipper>();
            SerializedObject a = new SerializedObject(anim);
            a.FindProperty("idleSprite").objectReferenceValue = Frame("idle-01");
            SetArray(a, "walkFrames", Frames("idle-01", "idle-02", "idle-03", "idle-04")); // 걷지 않으므로 이 칸에 idle 4장(IsMoving을 늘 켬)
            SetArray(a, "fleeFrames", new Sprite[0]);
            a.FindProperty("frameRate").floatValue = IdleFrameRate;
            a.FindProperty("fleeFrameRate").floatValue = 0f;
            SetArray(a, "attack1Frames", new Sprite[0]);
            SetArray(a, "attack2Frames", new Sprite[0]);
            SetArray(a, "hurtFrames", Frames("hurt-01"));
            a.FindProperty("flashWithHurtFrames").boolValue = true; // 피격: hurt-01 + 늑대와 같은 빨간 깜빡임(박쥐와 같다)
            SetArray(a, "deathFrames", Frames("hurt-01", "fallen-01"));
            a.FindProperty("knockedOutSprite").objectReferenceValue = Frame("idle-01");
            a.ApplyModifiedPropertiesWithoutUndo();

            float shadowBottom = m.FootTexels + 1f - m.ShadowH; // 그림자 맨 윗줄이 몸 맨 아랫줄과 겹친다
            Transform shadow = new GameObject("Shadow").transform;
            shadow.SetParent(visual, false);
            shadow.gameObject.layer = visual.gameObject.layer;
            shadow.localPosition = new Vector3(m.CenterXTexels / PixelsPerUnit, shadowBottom / PixelsPerUnit, 0f); // 아래 가운데 피벗
            SpriteRenderer shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = shadowSprite;
            shadowRenderer.sharedMaterial = wolfBody.sharedMaterial;
            shadowRenderer.sortingLayerID = wolfBody.sortingLayerID;
            shadowRenderer.sortingOrder = wolfBody.sortingOrder - 1;
            shadowRenderer.color = Color.white;

            FootSortOrder foot = visual.gameObject.AddComponent<FootSortOrder>();
            SerializedObject f = new SerializedObject(foot);
            f.FindProperty("body").objectReferenceValue = bodyRenderer;
            f.FindProperty("footTexels").floatValue = m.FootTexels;
            f.FindProperty("texelsPerUnit").floatValue = PixelsPerUnit;
            f.FindProperty("useBoundsBottom").boolValue = false;
            f.FindProperty("useWorldOffset").boolValue = false;
            f.FindProperty("isStatic").boolValue = false;
            f.FindProperty("shadow").objectReferenceValue = shadowRenderer;
            f.ApplyModifiedPropertiesWithoutUndo();

            float texel = VisualScale / PixelsPerUnit; // 1텍셀의 월드 크기(칸)
            MonsterHealth health = root.AddComponent<MonsterHealth>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterHealth>(), health);
            SerializedObject h = new SerializedObject(health);
            int wolfMax = Mathf.CeilToInt(config.monsterMaxHealth);
            h.FindProperty("maxHealthOffset").intValue = TargetMaxHealth - wolfMax;
            h.FindProperty("ignoreStunAndSleep").boolValue = false; // 늑대처럼 기절·마취가 통한다
            // 다트 맞힘 원: 몸 가운데 높이(가로 치우침은 PorcupineMonsterAI가 바라보는 쪽에 맞춰 DartHitShift로 옮긴다), 반경 = (반폭 + 반높이) / 2
            h.FindProperty("dartHitOffset").vector2Value = new Vector2(0f, m.CenterYTexels * texel);
            h.FindProperty("dartHitRadius").floatValue = (m.Width + m.Height) * 0.25f * texel;
            h.ApplyModifiedPropertiesWithoutUndo();

            MonsterCorpsePickup corpse = root.AddComponent<MonsterCorpsePickup>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterCorpsePickup>(), corpse);
            SerializedObject c = new SerializedObject(corpse);
            c.FindProperty("inventoryIcon").objectReferenceValue = Frame("idle-01");
            c.ApplyModifiedPropertiesWithoutUndo();

            PorcupineMonsterAI ai = root.AddComponent<PorcupineMonsterAI>();
            MonsterAI wolfAi = wolf.GetComponent<MonsterAI>();
            SerializedObject so = new SerializedObject(ai);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("shadow").objectReferenceValue = shadow;
            SetArray(so, "raiseFrames", Frames("raise-01", "raise-02", "raise-03"));
            so.FindProperty("raiseFrameRate").floatValue = RaiseFrameRate;
            SetArray(so, "shootFrames", Frames("shoot-01", "shoot-02", "shoot-03"));
            so.FindProperty("shootFrameRate").floatValue = ShootFrameRate;
            so.FindProperty("bodyCenterTexels").floatValue = m.CenterXTexels;
            so.FindProperty("texelWorldSize").floatValue = texel;
            so.FindProperty("muzzleHeight").floatValue = m.CenterYTexels * texel;
            so.FindProperty("quillPrefab").objectReferenceValue = quillPrefab;
            so.FindProperty("monsterTypeName").stringValue = "호저";
            so.FindProperty("config").objectReferenceValue = config;
            if (wolfAi != null)
                so.FindProperty("sightBlockingMask").intValue = new SerializedObject(wolfAi).FindProperty("sightBlockingMask").intValue;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            summary = $"발 {m.FootTexels}텍셀, 몸 {m.Width}x{m.Height}텍셀(가운데 {m.CenterXTexels}, {m.CenterYTexels}), 그림자 {m.ShadowW}x{m.ShadowH}, " +
                      $"최대 체력 {wolfMax}{TargetMaxHealth - wolfMax:+0;-0}, 다트 원 (0, {m.CenterYTexels * texel:0.###}) r {(m.Width + m.Height) * 0.25f * texel:0.###}";
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        return true;
    }

    // 그림자 PNG가 없으면 CharacterShadowBuilder와 같은 규칙(색 #534545, 줄 폭 RowWidth, 아래 가운데 피벗, PPU 32, Point)으로 만든다. 있으면 그대로 쓴다.
    private static Sprite EnsureShadowSprite(int w, int h)
    {
        string path = CharacterShadowBuilder.SpritePath(w, h);
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        Directory.CreateDirectory(CharacterShadowBuilder.Folder);
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[w * h];
        for (int i = 0; i < h; i++)
        {
            int rowWidth = CharacterShadowBuilder.RowWidth(w, h, i);
            int start = (w - rowWidth) / 2;
            int y = h - 1 - i; // 텍스처는 아래가 0
            for (int x = 0; x < w; x++) pixels[y * w + x] = x >= start && x < start + rowWidth ? CharacterShadowBuilder.ShadowColor : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void SetArray(SerializedObject so, string name, Sprite[] sprites)
    {
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }
}
