using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Tools > Animals > Build Turtle (slice sheet + prefab).
/// 1) Turtle Sprite Sheet를 32px 칸으로 자른다(그림이 있는 칸만, 위 행부터 왼쪽→오른쪽 순서로 turtle_0, turtle_1, ...).
///    PPU 32, Point 필터, 피벗은 몸통 가운데(칸 왼쪽에서 14px, 아래에서 7px) - 충돌체를 오프셋 없이 몸통에 맞추고 좌우 반전도 몸통 기준이 된다.
/// 2) 고양이 프리팹(AnimalFlee)을 복사해 AnimalTurtle 프리팹을 만든다 - 같은 부품(Rigidbody2D, AnimalRescue, AnimalSleep,
///    SimpleFrameAnimator, AnimalFacingFlipper)을 그대로 쓰고 행동 스크립트만 AnimalTurtle로 바꾼다(AnimalFlee의 필드는 이어받는다).
///    줍기 소리는 animal_turtle_pickup.wav, 숨기/나오기 소리는 둘 다 animal_turtle_hide and emerge.wav로 둔다.
/// 3) GameBalanceConfig.turtlePrefab에 연결한다.
/// 프레임 매핑: 걷기/도주 22~25, 기절(잠들기 전) 14, 잠 26~31 반복, 숨기 37~41(나오기는 그 역순). 다시 실행하면 프리팹을 새로 만든다.
/// </summary>
public static class TurtleAssetBuilder
{
    private const string SheetPath = "Assets/Sprites/Animals/Turtle Sprite Sheet (1).png";
    private const string SourcePrefabPath = "Assets/Prefabs/Animals/AnimalFlee.prefab";
    private const string PrefabPath = "Assets/Prefabs/Animals/AnimalTurtle.prefab";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const string TurtleScriptPath = "Assets/Scripts/Animals/AnimalTurtle.cs";
    private const string PickupSoundPath = "Assets/Audio/animal_turtle_pickup.wav";
    private const string ShellSoundPath = "Assets/Audio/animal_turtle_hide and emerge.wav";

    private const int Cell = 32;
    private static readonly Vector2 BodyPivot = new Vector2(14f / Cell, 7f / Cell);
    private const float ColliderRadius = 0.28f; // 로컬 단위(스케일 2.02 전) - 몸통 폭 20px, 높이 13px

    // 시트 행별 프레임 수 (위에서부터). 0 대기(하품), 1 걷기, 2 짧은 대기, 3 잠, 4 고개 내밀기, 5 등껍질로 숨기(0~5) + 등껍질(5~9)
    private static readonly int[] FramesPerRow = { 12, 10, 4, 6, 5, 10 };

    [MenuItem("Tools/Animals/Build Turtle (slice sheet + prefab)")]
    public static void Build()
    {
        SliceSheet();
        Sprite[] frames = LoadFrames();
        if (frames.Length != FramesPerRow.Sum())
        {
            Debug.LogError($"[TurtleAssetBuilder] 잘린 프레임 수 {frames.Length}개 (기대 {FramesPerRow.Sum()}개)");
            return;
        }
        GameObject prefab = BuildPrefab(frames);

        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        config.turtlePrefab = prefab;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TurtleAssetBuilder] {PrefabPath} 완료 - 프레임 {frames.Length}개, config.turtlePrefab 연결");
    }

    private static int RowStart(int row) => FramesPerRow.Take(row).Sum();

    private static Sprite[] Frames(Sprite[] frames, int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(i => frames[i]).ToArray();

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

        // 다시 실행해도 같은 이름이면 같은 spriteID를 써서 프리팹의 참조가 끊기지 않게 한다.
        Dictionary<string, GUID> existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        List<SpriteRect> rects = new List<SpriteRect>();
        int index = 0;
        for (int row = 0; row < FramesPerRow.Length; row++)
        {
            for (int col = 0; col < FramesPerRow[row]; col++)
            {
                string spriteName = $"turtle_{index++}";
                rects.Add(new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(col * Cell, texture.height - (row + 1) * Cell, Cell, Cell),
                    alignment = SpriteAlignment.Custom,
                    pivot = BodyPivot,
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

    private static Sprite[] LoadFrames() =>
        AssetDatabase.LoadAllAssetRepresentationsAtPath(SheetPath).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1))).ToArray();

    private static GameObject BuildPrefab(Sprite[] frames)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) AssetDatabase.DeleteAsset(PrefabPath);
        AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath);

        // 행동 스크립트만 AnimalFlee → AnimalTurtle로 바꾼다. AnimalFlee의 직렬화 필드(config 등)는 그대로 이어진다.
        // 스크립트를 바꾼 컴포넌트는 다시 불러와야 새 타입으로 잡히므로 한 번 저장하고 다시 연다.
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "AnimalTurtle";
            SerializedObject fleeSo = new SerializedObject(root.GetComponent<AnimalFlee>());
            fleeSo.FindProperty("m_Script").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonoScript>(TurtleScriptPath);
            fleeSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // 번호는 turtle_N(시트 전체 순서). 배회와 도주는 같은 걸음 프레임을 쓰고 도주만 더 빠르게 재생한다.
            Sprite idle = frames[RowStart(0)];
            Sprite[] walk = Frames(frames, 22, 25);
            Sprite[] sleep = Frames(frames, 26, 31);
            Sprite[] hide = Frames(frames, 37, 41);
            Sprite[] emerge = hide.Reverse().ToArray();

            root.GetComponent<SpriteRenderer>().sprite = idle;

            SerializedObject animator = new SerializedObject(root.GetComponent<SimpleFrameAnimator>());
            animator.FindProperty("idleSprite").objectReferenceValue = idle;
            SetArray(animator.FindProperty("walkFrames"), walk);
            SetArray(animator.FindProperty("fleeFrames"), walk);
            animator.FindProperty("fleeFrameRate").floatValue = 20f;
            animator.FindProperty("knockedOutSprite").objectReferenceValue = sleep[0];
            SetArray(animator.FindProperty("knockedOutFrames"), sleep);
            animator.FindProperty("knockedOutFrameRate").floatValue = 5f;
            animator.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject turtle = new SerializedObject(root.GetComponent<AnimalTurtle>());
            SetArray(turtle.FindProperty("hideFrames"), hide);
            SetArray(turtle.FindProperty("emergeFrames"), emerge);
            turtle.FindProperty("stunnedSprite").objectReferenceValue = frames[14];
            // 숨기/나오기 소리는 같은 파일을 통째로 쓴다. 볼륨 0.58은 50ms 최대 RMS -14.2dBFS를 다른 효과음 수준(-19dBFS)에 맞춘 값.
            AudioClip shellClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ShellSoundPath);
            foreach (string slot in new[] { "hideSound", "emergeSound" })
            {
                turtle.FindProperty(slot + ".clip").objectReferenceValue = shellClip;
                turtle.FindProperty(slot + ".volume").floatValue = 0.58f;
                turtle.FindProperty(slot + ".playSegment").boolValue = false;
            }
            turtle.ApplyModifiedPropertiesWithoutUndo();

            CircleCollider2D collider = root.GetComponent<CircleCollider2D>();
            collider.offset = Vector2.zero;
            collider.radius = ColliderRadius;

            // 줍기 소리: 파일이 10초짜리(같은 소리가 1초마다 작아지며 반복)라 가장 큰 첫 1초만 재생한다.
            // 볼륨 0.48은 import 설정(Force To Mono + Normalize) 뒤 50ms 최대 RMS -12.6dBFS를 다른 효과음 수준(-19dBFS)에 맞춘 값.
            SerializedObject rescue = new SerializedObject(root.GetComponent<AnimalRescue>());
            rescue.FindProperty("sheetFirstFrameIcon").objectReferenceValue = frames[0];
            AudioClip pickup = AssetDatabase.LoadAssetAtPath<AudioClip>(PickupSoundPath);
            if (pickup != null)
            {
                rescue.FindProperty("pickupSound").objectReferenceValue = pickup;
                rescue.FindProperty("pickupSoundVolume").floatValue = 0.48f;
                rescue.FindProperty("playPickupSoundSegment").boolValue = true;
                rescue.FindProperty("pickupSoundStartTime").floatValue = 0f;
                rescue.FindProperty("pickupSoundEndTime").floatValue = 1f;
            }
            rescue.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    private static void SetArray(SerializedProperty property, Sprite[] sprites)
    {
        property.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }
}
