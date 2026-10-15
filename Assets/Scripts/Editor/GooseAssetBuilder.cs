using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Tools > Animals > Build Goose Animations (slice sheet + clips). 새 거위 동물용 - 프리팹/행동은 아직 없고 애니메이션만 만들어 둔다.
/// 1) 거위 시트(파일 이름은 Seagull Sprite Sheet)를 32px 칸으로 다시 자른다. 이름은 그대로 "Seagull Sprite Sheet_N"(위 행부터 왼쪽→오른쪽, 0부터).
///    자동 슬라이스는 그림 크기에 딱 맞춘 조각마다 피벗이 달라서 재생하면 프레임마다 흔들린다 - 모든 칸을 같은 크기·같은 피벗(칸 가운데)으로 맞춘다.
///    PPU 32, Point 필터(다른 동물 시트와 같다).
/// 2) 행마다 AnimationClip(SpriteRenderer.m_Sprite 키프레임)을 Assets/Animations/Goose에 만든다. 다시 실행하면 같은 파일을 덮어써 GUID가 유지된다.
/// 이 프로젝트의 동물은 SimpleFrameAnimator(Sprite 배열)로 재생하므로, 나중에 거위를 붙일 때는 아래 Rows의 번호를 그대로 배열에 쓰면 된다.
/// batchmode: -executeMethod GooseAssetBuilder.BuildBatch
///
/// Tools > Animals > Build Goose Prefab: 이미 잘라 둔 시트로 거위 프리팹을 만든다(시트는 다시 자르지 않는다).
/// 고양이 프리팹(AnimalFlee)을 복사해 행동 스크립트만 AnimalGoose로 바꾸고(TurtleAssetBuilder와 같은 방식), 프레임/충돌체/소리를 채운 뒤
/// GameBalanceConfig.goosePrefab에 연결한다. 프레임은 대기 0, 걷기 0~7(Goose_Idle 행), 도주 16~23(Goose_FlyFlap 행), 수면 48(Goose_Down).
/// 소리 파일(animal_goose_cry.wav, animal_goose_pickup.wav)이 없으면 그 필드는 비워 둔다. 다시 실행하면 프리팹을 새로 만든다.
/// batchmode: -executeMethod GooseAssetBuilder.BuildPrefabBatch
/// </summary>
public static class GooseAssetBuilder
{
    private const string SheetPath = "Assets/Sprites/Animals/Seagull Sprite Sheet.png";
    private const string ClipFolder = "Assets/Animations/Goose";
    private const string SpritePrefix = "Seagull Sprite Sheet_";

    private const string SourcePrefabPath = "Assets/Prefabs/Animals/AnimalFlee.prefab";
    private const string PrefabPath = "Assets/Prefabs/Animals/AnimalGoose.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const string GooseScriptPath = "Assets/Scripts/AnimalGoose.cs";
    private const string CryClipPath = "Assets/Audio/animal_goose_cry.wav";
    private const string PickupClipPath = "Assets/Audio/animal_goose_pickup.wav";
    private const float ColliderRadius = 0.22f; // 로컬 단위(스케일 2.02 전) - 선 몸통이 칸 가운데 14x13px

    private const int Cell = 32;
    private const float FrameRate = 10f;
    private static readonly Vector2 Pivot = new Vector2(0.5f, 0.5f);

    // 시트 행별 프레임 수(위에서부터)와 클립 이름. 이름은 그림을 보고 붙인 것이라 쓰임새가 정해지면 바꿔도 된다.
    private static readonly (string Name, int Count, bool Loop)[] Rows =
    {
        ("Goose_Idle", 8, true),        // 0~7    서서 대기/걷기
        ("Goose_Look", 8, true),        // 8~15   서서 두리번
        ("Goose_FlyFlap", 8, true),     // 16~23  옆모습 날갯짓
        ("Goose_FlyGlide", 8, true),    // 24~31  날개 들고 나는 모습
        ("Goose_FlyTop", 8, true),      // 32~39  날개 접고 나는 모습(위/아래에서 본 실루엣)
        ("Goose_WingSpread", 8, true),  // 40~47  정면으로 날개 펼침 ↔ 옆모습 번갈아
        ("Goose_Down", 1, false),       // 48     엎드림(기절/수면용 후보)
    };

    [MenuItem("Tools/Animals/Build Goose Animations (slice sheet + clips)")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        SliceSheet();
        Sprite[] frames = LoadFrames();
        int expected = Rows.Sum(r => r.Count);
        if (frames.Length != expected)
        {
            Debug.LogError($"[GooseAssetBuilder] 잘린 프레임 수 {frames.Length}개 (기대 {expected}개)");
            return false;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.CreateFolder("Assets/Animations", "Goose");
        int start = 0;
        foreach ((string clipName, int count, bool loop) in Rows)
        {
            SaveClip(clipName, frames.Skip(start).Take(count).ToArray(), loop);
            start += count;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GooseAssetBuilder] 프레임 {frames.Length}개, 클립 {Rows.Length}개 - {ClipFolder}");
        return true;
    }

    private static void SliceSheet()
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Cell;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        // 이름이 같으면 같은 spriteID를 써서, 이미 이 프레임을 가리키는 참조가 있어도 끊기지 않게 한다.
        Dictionary<string, GUID> existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        List<SpriteRect> rects = new List<SpriteRect>();
        int index = 0;
        for (int row = 0; row < Rows.Length; row++)
        {
            for (int col = 0; col < Rows[row].Count; col++)
            {
                string spriteName = SpritePrefix + index++;
                rects.Add(new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(col * Cell, texture.height - (row + 1) * Cell, Cell, Cell),
                    alignment = SpriteAlignment.Custom,
                    pivot = Pivot,
                    spriteID = existing.TryGetValue(spriteName, out GUID id) ? id : GUID.Generate(),
                });
            }
        }
        provider.SetSpriteRects(rects.ToArray());
        ISpriteNameFileIdDataProvider names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    [MenuItem("Tools/Animals/Build Goose Prefab")]
    public static void BuildPrefab() => TryBuildPrefab();

    public static void BuildPrefabBatch() => EditorApplication.Exit(TryBuildPrefab() ? 0 : 1);

    private static bool TryBuildPrefab()
    {
        Sprite[] frames = LoadFrames();
        int expected = Rows.Sum(r => r.Count);
        if (frames.Length != expected)
        {
            Debug.LogError($"[GooseAssetBuilder] 시트 프레임 수 {frames.Length}개 (기대 {expected}개) - 먼저 Build Goose Animations로 시트를 자른다.");
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) AssetDatabase.DeleteAsset(PrefabPath);
        AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath);

        // 행동 스크립트만 AnimalFlee → AnimalGoose로 바꾼다. AnimalFlee의 직렬화 필드(config, alertRange 등)는 그대로 이어진다.
        // 스크립트를 바꾼 컴포넌트는 다시 불러와야 새 타입으로 잡히므로 한 번 저장하고 다시 연다.
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "AnimalGoose";
            SerializedObject fleeSo = new SerializedObject(root.GetComponent<AnimalFlee>());
            fleeSo.FindProperty("m_Script").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonoScript>(GooseScriptPath);
            fleeSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AudioClip cry = AssetDatabase.LoadAssetAtPath<AudioClip>(CryClipPath);
        AudioClip pickup = AssetDatabase.LoadAssetAtPath<AudioClip>(PickupClipPath);
        if (cry == null) Debug.Log($"[GooseAssetBuilder] {CryClipPath} 없음 - 울음소리 필드를 비워 둔다.");
        if (pickup == null) Debug.Log($"[GooseAssetBuilder] {PickupClipPath} 없음 - 줍기 소리 필드를 비워 둔다(기본 줍기 소리가 난다).");

        root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Sprite idle = frames[0];
            Sprite[] walk = frames.Skip(0).Take(8).ToArray();
            Sprite[] fly = frames.Skip(16).Take(8).ToArray();
            Sprite down = frames[48];

            root.GetComponent<SpriteRenderer>().sprite = idle;

            SerializedObject animator = new SerializedObject(root.GetComponent<SimpleFrameAnimator>());
            animator.FindProperty("idleSprite").objectReferenceValue = idle;
            SetArray(animator.FindProperty("walkFrames"), walk);
            SetArray(animator.FindProperty("fleeFrames"), fly);
            animator.FindProperty("frameRate").floatValue = FrameRate;
            animator.FindProperty("fleeFrameRate").floatValue = 0f;
            animator.FindProperty("knockedOutSprite").objectReferenceValue = down;
            animator.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject goose = new SerializedObject(root.GetComponent<AnimalGoose>());
            goose.FindProperty("cryClip").objectReferenceValue = cry;
            goose.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject sleep = new SerializedObject(root.GetComponent<AnimalSleep>());
            sleep.FindProperty("sleepImmediately").boolValue = true;
            sleep.ApplyModifiedPropertiesWithoutUndo();

            CircleCollider2D collider = root.GetComponent<CircleCollider2D>();
            collider.offset = Vector2.zero;
            collider.radius = ColliderRadius;

            SerializedObject rescue = new SerializedObject(root.GetComponent<AnimalRescue>());
            rescue.FindProperty("pickupSound").objectReferenceValue = pickup;
            rescue.FindProperty("pickupSoundVolume").floatValue = 1f;
            rescue.FindProperty("inventoryIcon").objectReferenceValue = null;
            rescue.FindProperty("sheetFirstFrameIcon").objectReferenceValue = frames[0];
            rescue.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        config.goosePrefab = prefab;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GooseAssetBuilder] {PrefabPath} 완료 - config.goosePrefab 연결");
        return true;
    }

    private static void SetArray(SerializedProperty property, Sprite[] sprites)
    {
        property.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }

    private static Sprite[] LoadFrames() =>
        AssetDatabase.LoadAllAssetRepresentationsAtPath(SheetPath).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1))).ToArray();

    private static void SaveClip(string clipName, Sprite[] sprites, bool loop)
    {
        AnimationClip clip = new AnimationClip { name = clipName, frameRate = FrameRate };
        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        // 마지막 프레임도 한 프레임 길이만큼 보이도록 끝에 같은 스프라이트를 한 번 더 둔다.
        List<ObjectReferenceKeyframe> keys = sprites
            .Select((s, i) => new ObjectReferenceKeyframe { time = i / FrameRate, value = s }).ToList();
        keys.Add(new ObjectReferenceKeyframe { time = sprites.Length / FrameRate, value = sprites[sprites.Length - 1] });
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = $"{ClipFolder}/{clipName}.anim";
        AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existingClip == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return;
        }
        EditorUtility.CopySerialized(clip, existingClip);
        existingClip.name = clipName;
        EditorUtility.SetDirty(existingClip);
    }
}
