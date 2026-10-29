using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tools > Monsters > Build Rhino (sprites + prefab). 코뿔소(돌진형) 프리팹을 만든다. 스폰 목록에는 넣지 않는다.
/// 1) Assets/Sprites/Monster/rhino-monster-48x32-unity/RhinoMonster48/Frames의 48x32 PNG 22장을 README.txt대로 가져온다:
///    Sprite(Single), PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데. Visual 배율 3 → 1텍셀 = 3/32칸(다른 몬스터의 1.5배 크기).
/// 2) 프리팹: 루트(NavMeshAgent, MonsterHealth, MonsterCorpsePickup, RhinoMonsterAI) 아래 Visual(땅 위치, 배율 3, FootSortOrder) 아래
///    Body(SpriteRenderer, SimpleFrameAnimator, AnimalFacingFlipper)와 Shadow(박쥐·호저와 같은 짜임).
///    NavMeshAgent·MonsterHealth·MonsterCorpsePickup 값과 머티리얼·정렬 레이어는 늑대(PatrolMonster)에서 복사한다.
///    - 체력: maxHealthOffset = 7 − 늑대 최대 체력(ceil(monsterMaxHealth)) → 최대 체력 7.
///    - 몸 치수는 idle 4장의 보이는 픽셀로 잰다. 그림자 = 몸 폭 x 0.75, 높이 = 폭 x 4/15, 맨 윗줄이 몸 맨 아랫줄과 겹침(CharacterShadowBuilder 규칙).
///    - 길찾기 반경 = 배율 2일 때 몸 높이의 절반(0.55) x 배율/2. 다트 맞힘 원 = 몸 가운데, 반경 (반폭 + 반높이) / 2. 그림자는 Visual 자식이라 배율을 따라 커진다.
///    - 돌진 벽 판정용 몸 상자 = idle 보이는 몸의 반폭·반높이(RhinoMonsterAI.bodyHalfExtentsTexels).
///    - 그림 연결: walk 6장(걷기), idle 4장(서 있을 때 - fleeFrames 칸), charge·dash·recover는 RhinoMonsterAI가 재생, 피격 hurt-01, 쓰러짐 hurt-01 → fallen-01,
///      기절·마취 중 recover-01.
/// batchmode: -executeMethod RhinoAssetBuilder.BuildBatch  (끝나면 에디터가 종료된다)
/// </summary>
public static class RhinoAssetBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Monsters/RhinoMonster.prefab";
    public const string FramesFolder = "Assets/Sprites/Monster/rhino-monster-48x32-unity/RhinoMonster48/Frames";
    private const string WolfPrefabPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    public const float PixelsPerUnit = 32f;
    public const float VisualScale = 3f; // 처음 2(다른 몬스터와 같은 1도트 = 1/16칸)에서 1.5배로 키움 → 1도트 = 3/32칸
    public const float IdleFrameRate = 5f;     // README: Idle 4-6 fps
    public const float WalkFrameRate = 7f;     // Walk 6-8 fps (피격·쓰러짐도 이 속도)
    public const float ChargeFrameRate = 7f;   // Charge 6-8 fps
    public const float DashFrameRate = 12f;    // Dash 12 fps
    public const float RecoverFrameRate = 5f;  // Recover 4-6 fps
    public const int TargetMaxHealth = 7;

    public static readonly string[] FrameNames =
    {
        "idle-01", "idle-02", "idle-03", "idle-04", "walk-01", "walk-02", "walk-03", "walk-04", "walk-05", "walk-06",
        "charge-01", "charge-02", "charge-03", "charge-04", "dash-01", "dash-02", "dash-03", "dash-04",
        "recover-01", "recover-02", "hurt-01", "fallen-01",
    };

    /// <summary>idle 4장에서 잰 몸 치수(텍셀, 피벗 = 프레임 가운데 기준). 테스트도 쓴다.</summary>
    public struct BodyMetrics
    {
        public float FootTexels, CenterXTexels, CenterYTexels;
        public int Width, Height, ShadowW, ShadowH;
        public float AgentRadius, DartRadius;
    }

    [MenuItem("Tools/Monsters/Build Rhino (sprites + prefab)")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        if (!ImportFrames()) return false;
        if (!BuildPrefab(out string summary)) return false;
        Debug.Log($"[RhinoAssetBuilder] 완료: {FramesFolder} {FrameNames.Length}장, {PrefabPath} - {summary}");
        return true;
    }

    public static Sprite Frame(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{FramesFolder}/{name}.png");
    private static Sprite[] Frames(params string[] names) => names.Select(Frame).ToArray();

    private static bool ImportFrames()
    {
        foreach (string n in FrameNames)
        {
            string path = $"{FramesFolder}/{n}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError($"[RhinoAssetBuilder] {path}가 없다"); return false; }
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
            if (Frame(n) == null) { Debug.LogError($"[RhinoAssetBuilder] {path}를 스프라이트로 가져오지 못했다"); return false; }
        }
        return true;
    }

    /// <summary>idle 4장의 보이는 픽셀로 발·가운데·폭·높이를 잰다(파일을 직접 읽는다).</summary>
    public static BodyMetrics MeasureIdle()
    {
        List<int> feet = new List<int>(), widths = new List<int>(), heights = new List<int>();
        List<float> cx = new List<float>(), cy = new List<float>();
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
            float halfW = t.width * 0.5f, halfH = t.height * 0.5f;
            feet.Add(Mathf.RoundToInt(minY - halfH));
            widths.Add(maxX - minX + 1);
            heights.Add(maxY - minY + 1);
            cx.Add((minX + maxX + 1) * 0.5f - halfW);
            cy.Add((minY + maxY + 1) * 0.5f - halfH);
            Object.DestroyImmediate(t);
        }
        int Mode(List<int> v) => v.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        float texel = VisualScale / PixelsPerUnit;
        BodyMetrics m = new BodyMetrics
        {
            FootTexels = Mode(feet),
            Width = Mode(widths),
            Height = Mode(heights),
            CenterXTexels = Mathf.Round(cx.Average() * 2f) / 2f,
            CenterYTexels = Mathf.Round(cy.Average() * 2f) / 2f,
        };
        m.ShadowW = Mathf.Max(3, Mathf.RoundToInt(m.Width * 0.75f));
        m.ShadowH = Mathf.Max(1, Mathf.RoundToInt(m.ShadowW * 4f / 15f));
        // 길찾기 반경: 배율 2일 때의 값(몸 높이의 절반을 0.05칸 단위로 반올림 = 0.55)을 배율에 비례해 키운다(크기 시험과 같은 비율, 배율 3 → 0.825).
        m.AgentRadius = Mathf.Round(m.Height * 0.5f * (2f / PixelsPerUnit) * 20f) / 20f * (VisualScale / 2f);
        m.DartRadius = (m.Width + m.Height) * 0.25f * texel;
        return m;
    }

    private static bool BuildPrefab(out string summary)
    {
        summary = "";
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefabPath);
        BodyMetrics m = MeasureIdle();
        Sprite shadowSprite = EnsureShadowSprite(m.ShadowW, m.ShadowH);
        SpriteRenderer wolfBody = wolf != null ? wolf.transform.Find("Visual")?.GetComponent<SpriteRenderer>() : null;
        if (config == null || wolf == null || wolfBody == null || shadowSprite == null)
        {
            Debug.LogError($"[RhinoAssetBuilder] 준비 실패: config {config != null}, 늑대 {wolf != null}, 그림자 {shadowSprite != null}");
            return false;
        }
        float texel = VisualScale / PixelsPerUnit;

        GameObject root = new GameObject("RhinoMonster");
        try
        {
            root.layer = wolf.layer;
            root.tag = wolf.tag;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            EditorUtility.CopySerialized(wolf.GetComponent<NavMeshAgent>(), agent);
            agent.radius = Mathf.Max(agent.radius, m.AgentRadius);
            agent.speed = config.rhinoWalkSpeed;

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
            SetArray(a, "walkFrames", Frames("walk-01", "walk-02", "walk-03", "walk-04", "walk-05", "walk-06"));
            SetArray(a, "fleeFrames", Frames("idle-01", "idle-02", "idle-03", "idle-04")); // 서 있을 때(IsFleeing) idle 4장 반복
            a.FindProperty("frameRate").floatValue = WalkFrameRate;
            a.FindProperty("fleeFrameRate").floatValue = IdleFrameRate;
            SetArray(a, "attack1Frames", new Sprite[0]);
            SetArray(a, "attack2Frames", new Sprite[0]);
            SetArray(a, "hurtFrames", Frames("hurt-01"));
            a.FindProperty("flashWithHurtFrames").boolValue = true; // 피격: hurt-01 + 늑대와 같은 빨간 깜빡임(박쥐·호저와 같다)
            SetArray(a, "deathFrames", Frames("hurt-01", "fallen-01"));
            a.FindProperty("knockedOutSprite").objectReferenceValue = Frame("recover-01");
            a.ApplyModifiedPropertiesWithoutUndo();

            float shadowBottom = m.FootTexels + 1f - m.ShadowH;
            Transform shadow = new GameObject("Shadow").transform;
            shadow.SetParent(visual, false);
            shadow.gameObject.layer = visual.gameObject.layer;
            shadow.localPosition = new Vector3(m.CenterXTexels / PixelsPerUnit, shadowBottom / PixelsPerUnit, 0f);
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

            MonsterHealth health = root.AddComponent<MonsterHealth>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterHealth>(), health);
            SerializedObject h = new SerializedObject(health);
            int wolfMax = Mathf.CeilToInt(config.monsterMaxHealth);
            h.FindProperty("maxHealthOffset").intValue = TargetMaxHealth - wolfMax;
            h.FindProperty("ignoreStunAndSleep").boolValue = false; // 늑대처럼 기절·마취가 통한다
            h.FindProperty("dartHitOffset").vector2Value = new Vector2(0f, m.CenterYTexels * texel);
            h.FindProperty("dartHitRadius").floatValue = m.DartRadius;
            h.ApplyModifiedPropertiesWithoutUndo();

            MonsterCorpsePickup corpse = root.AddComponent<MonsterCorpsePickup>();
            EditorUtility.CopySerialized(wolf.GetComponent<MonsterCorpsePickup>(), corpse);
            SerializedObject c = new SerializedObject(corpse);
            c.FindProperty("inventoryIcon").objectReferenceValue = Frame("idle-01");
            c.ApplyModifiedPropertiesWithoutUndo();

            RhinoMonsterAI ai = root.AddComponent<RhinoMonsterAI>();
            MonsterAI wolfAi = wolf.GetComponent<MonsterAI>();
            SerializedObject so = new SerializedObject(ai);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("shadow").objectReferenceValue = shadow;
            SetArray(so, "chargeFrames", Frames("charge-01", "charge-02", "charge-03", "charge-04"));
            so.FindProperty("chargeFrameRate").floatValue = ChargeFrameRate;
            SetArray(so, "dashFrames", Frames("dash-01", "dash-02", "dash-03", "dash-04"));
            so.FindProperty("dashFrameRate").floatValue = DashFrameRate;
            SetArray(so, "recoverFrames", Frames("recover-01", "recover-02"));
            so.FindProperty("recoverFrameRate").floatValue = RecoverFrameRate;
            so.FindProperty("bodyCenterTexels").floatValue = m.CenterXTexels;
            so.FindProperty("bodyCenterYTexels").floatValue = m.CenterYTexels;
            so.FindProperty("texelWorldSize").floatValue = texel;
            so.FindProperty("bodyHalfExtentsTexels").vector2Value = new Vector2(m.Width * 0.5f, m.Height * 0.5f);
            so.FindProperty("monsterTypeName").stringValue = "코뿔소";
            so.FindProperty("config").objectReferenceValue = config;
            if (wolfAi != null)
                so.FindProperty("sightBlockingMask").intValue = new SerializedObject(wolfAi).FindProperty("sightBlockingMask").intValue;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            summary = $"발 {m.FootTexels}텍셀, 몸 {m.Width}x{m.Height}텍셀(가운데 {m.CenterXTexels}, {m.CenterYTexels}), 그림자 {m.ShadowW}x{m.ShadowH}, " +
                      $"길찾기 반경 {agent.radius:0.##}(늑대 {wolf.GetComponent<NavMeshAgent>().radius:0.##}), 최대 체력 {wolfMax}{TargetMaxHealth - wolfMax:+0;-0}, " +
                      $"다트 원 (0, {m.CenterYTexels * texel:0.###}) r {m.DartRadius:0.###}";
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
            int y = h - 1 - i;
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
