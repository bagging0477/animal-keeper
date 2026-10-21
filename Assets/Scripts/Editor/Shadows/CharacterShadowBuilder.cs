using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

/// <summary>
/// 캐릭터 발밑 그림자를 만든다. Tools > Shadows > Build Character Shadows (batchmode: -executeMethod CharacterShadowBuilder.RunBatch)
///
/// 기준은 소리반응형(Blood Monster) 스프라이트에 그려진 그림자다.
/// - 색 #534545(83, 69, 69), 불투명 단색
/// - 폭 15px = 몸 폭 20px x 0.75, 높이 4px
/// - 줄 폭 15·15·13·9: 줄 i의 폭 = W x √(1 − (i/H)²)를 W와 같은 홀짝으로 반올림 (이 식이 그 모양을 그대로 다시 만든다)
/// - 맨 윗줄이 몸 맨 아래 줄과 겹친다
/// 같은 규칙으로 캐릭터마다 몸 폭에 비례한 타원(안티앨리어싱·그라데이션·반투명 픽셀 없음)을 Assets/Sprites/Shadows에 PNG로 만든다.
/// PNG는 피벗이 아래 가운데이고 PPU는 32다(캐릭터 텍셀과 같은 크기).
/// 그 다음 각 프리팹의 몸 스프라이트 옆에 "Shadow" 자식(SpriteRenderer, 몸과 같은 머티리얼·Sorting Layer, Order = 몸 − 1)을 둔다.
/// 위치는 몸이 실제로 보이는 맨 아랫줄(애니메이션에서 가장 흔한 높이)이고, 투명도는 렌더러 색의 알파로만 조절한다(프리팹에서 바꾸면 된다).
/// 플레이어는 몸 전체가 마우스 쪽으로 회전하므로 자식이 아니라 씬에 따로 둔 "PlayerShadow"를 PositionConstraint로 따라가게 한다(회전하지 않는다).
/// 이미 있는 Shadow의 알파 값은 다시 실행해도 유지한다.
/// </summary>
public static class CharacterShadowBuilder
{
    public const string Folder = "Assets/Sprites/Shadows";
    public static readonly Color32 ShadowColor = new Color32(83, 69, 69, 255); // 소리반응형 그림자 색 #534545
    private const float PixelsPerUnit = 32f;

    /// <summary>대상: 프리팹, 몸 렌더러가 붙은 오브젝트("" = 루트), 그림자 크기(px), 위치(몸 텍셀 단위, 피벗 기준), 그림자 배율.</summary>
    private struct Target
    {
        public string Prefab, BodyPath;
        public int W, H;
        public float X, BottomY, BodyPpu, Scale;
        public string Note;
    }

    // BottomY = 몸이 보이는 맨 아랫줄의 아래 변(F) − H + 1 (그림자 맨 윗줄이 그 줄과 겹친다). X = 텍셀 격자 맞춤 보정.
    private static readonly Target[] Targets =
    {
        new Target { Prefab = "Assets/Prefabs/Animals/AnimalWander.prefab", BodyPath = "", W = 14, H = 4, X = 0f, BottomY = -6f - 3f, BodyPpu = 32f, Scale = 1f, Note = "코알라: 몸 18px, 발 −6 (걷기 7프레임 모두)" },
        new Target { Prefab = "Assets/Prefabs/Animals/AnimalFlee.prefab", BodyPath = "", W = 14, H = 4, X = 0f, BottomY = -6.5f - 3f, BodyPpu = 32f, Scale = 1f, Note = "고양이: 몸 19px, 발 −6.5 (최빈, 범위 −7~−5.5)" },
        new Target { Prefab = "Assets/Prefabs/Animals/AnimalSoundFlee.prefab", BodyPath = "", W = 16, H = 4, X = 0f, BottomY = -16f - 3f, BodyPpu = 32f, Scale = 1f, Note = "레서판다: 몸 20px, 발 −16 (32x32 격자 프레임 바닥)" },
        new Target { Prefab = "Assets/Prefabs/Animals/AnimalGoose.prefab", BodyPath = "", W = 10, H = 3, X = 0f, BottomY = -7f - 2f, BodyPpu = 32f, Scale = 1f, Note = "거위: 땅 위 몸 14px(대기·둘러보기), 발 −7" },
        new Target { Prefab = "Assets/Prefabs/Animals/AnimalTurtle.prefab", BodyPath = "", W = 16, H = 4, X = 0f, BottomY = -7f - 3f, BodyPpu = 32f, Scale = 1f, Note = "거북이: 몸 21px, 발 −7 (발 피벗). 숨은 프레임은 ShadowFrameMatcher 표" },
        new Target { Prefab = "Assets/Prefabs/Monsters/PatrolMonster.prefab", BodyPath = "Visual", W = 14, H = 4, X = 0f, BottomY = -6f - 3f, BodyPpu = 16f, Scale = 2f, Note = "늑대: 몸 18px(PPU 16), 발 −6 (최빈)" },
        new Target { Prefab = "Assets/Prefabs/Monsters/WeepingAngel.prefab", BodyPath = "Visual", W = 27, H = 7, X = 0f, BottomY = -21.5f - 6f, BodyPpu = 32f, Scale = 1f, Note = "우는천사: 몸 37px, 발 −21.5" },
        new Target { Prefab = "Assets/Prefabs/DroppedBombPickup.prefab", BodyPath = "", W = 17, H = 5, X = -0.5f, BottomY = -14f - 4f, BodyPpu = 32f, Scale = 1f, Note = "바닥의 폭탄: 몸 23px(중심 −0.5), 바닥 −14" },
    };

    // 플레이어(모든 클래스가 화면에서 약 0.73칸 폭이 되게 배율이 맞춰져 있다): 월드 텍셀 1/32칸 기준 몸 23px → 18x5, 보이는 몸 아래 −0.41칸
    private const int PlayerW = 18, PlayerH = 5;
    private const float PlayerOffsetY = -0.53f;
    private static readonly string[] PlayerScenes = { "Assets/Scenes/VillageScene.unity", "Assets/Scenes/TruckScene.unity", "Assets/Scenes/ShelterScene.unity", "Assets/Scenes/ClassSelectScene.unity" };

    [MenuItem("Tools/Shadows/Build Character Shadows")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log("[CharacterShadowBuilder]\n" + Build());
    }

    public static void RunBatch()
    {
        string report = Build();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CharacterShadowBuilder.txt", report, new UTF8Encoding(false));
        EditorApplication.Exit(0);
    }

    private static string Build()
    {
        StringBuilder sb = new StringBuilder();
        foreach (var size in Targets.Select(t => (t.W, t.H)).Append((PlayerW, PlayerH)).Distinct())
            sb.AppendLine($"이미지 {EnsureShadowSprite(size.Item1, size.Item2)}");
        foreach (Target t in Targets) sb.AppendLine(SetupPrefab(t));
        foreach (string scene in PlayerScenes) sb.AppendLine(SetupPlayerShadow(scene));
        sb.Append(BuildFrameTables());
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>프레임별 그림자 표만 다시 만든다(거북이·거위·레서판다 프리팹만 건드린다). batchmode: -executeMethod CharacterShadowBuilder.RunFrameTablesBatch</summary>
    public static void RunFrameTablesBatch()
    {
        StringBuilder sb = new StringBuilder();
        foreach (Target t in Targets.Where(t => FrameTablePrefabs.Contains(t.Prefab)))
        {
            sb.AppendLine($"이미지 {EnsureShadowSprite(t.W, t.H)}");
            sb.AppendLine(SetupPrefab(t));
        }
        sb.Append(BuildFrameTables());
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CharacterShadowBuilder_frames.txt", sb.ToString(), new UTF8Encoding(false));
        EditorApplication.Exit(0);
    }

    // ===================== 프레임별 그림자 표 (ShadowFrameMatcher) =====================
    // 프레임마다 보이는 몸의 맨 아랫줄(F)과 가로 범위를 재서, 그림자 맨 윗줄이 그 줄과 겹치고(소리반응형과 같은 1줄) 몸 가운데에 오게 한다.
    // - 레서판다: 모든 프레임(대기·경계·달리기). 크기는 16x4 그대로, 높이와 가로 중심만 프레임을 따른다 - 달리기에서 몸이 뛰어올라도 발이 그림자 위에 있다.
    // - 거북이: 숨기 프레임 중 발 높이가 걷기(−7)보다 2px 이상 위인 것. 크기 = 숨은 몸 폭 x 0.75.
    // - 거위: 쓰러짐(Goose_Down) 프레임. 크기 = 누운 몸 폭 x 0.75, 두께는 같은 4/15 비율.
    private static readonly string[] FrameTablePrefabs =
    {
        "Assets/Prefabs/Animals/AnimalSoundFlee.prefab", "Assets/Prefabs/Animals/AnimalTurtle.prefab", "Assets/Prefabs/Animals/AnimalGoose.prefab",
    };

    private struct Frame
    {
        public Texture2D Texture;
        public RectInt Rect;
        public Vector2 PivotPx; // 프레임 안 피벗(픽셀)
        public string Name;
    }

    private static string BuildFrameTables()
    {
        StringBuilder sb = new StringBuilder();
        // 레서판다: SpriteSheetAnimator 설정대로 격자를 자른다(실행 중 Sprite.Create와 같은 사각형, 피벗 가운데)
        {
            string path = FrameTablePrefabs[0];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            SerializedObject so = new SerializedObject(prefab.GetComponent<SpriteSheetAnimator>());
            Texture2D sheet = (Texture2D)so.FindProperty("sheet").objectReferenceValue;
            int fs = so.FindProperty("frameSize").intValue;
            int rowsInSheet = sheet.height / fs;
            List<Frame> frames = new List<Frame>();
            foreach (string prefix in new[] { "idle", "alert", "movement" })
            {
                int row = so.FindProperty(prefix + "Row").intValue, start = so.FindProperty(prefix + "StartColumn").intValue, count = so.FindProperty(prefix + "FrameCount").intValue;
                for (int i = 0; i < count; i++)
                    frames.Add(new Frame { Texture = sheet, Rect = new RectInt((start + i) * fs, (rowsInSheet - 1 - row) * fs, fs, fs), PivotPx = new Vector2(fs * 0.5f, fs * 0.5f), Name = prefix + (start + i) });
            }
            sb.AppendLine(SetupFrameMatcher(path, frames, 16, 4, null));
        }
        // 거북이: 숨기 프레임
        {
            string path = FrameTablePrefabs[1];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            SerializedObject so = new SerializedObject(prefab.GetComponent<AnimalTurtle>());
            SerializedProperty list = so.FindProperty("hideFrames");
            List<Frame> frames = new List<Frame>();
            for (int i = 0; i < list.arraySize; i++) frames.Add(FrameOf((Sprite)list.GetArrayElementAtIndex(i).objectReferenceValue));
            sb.AppendLine(SetupFrameMatcher(path, frames, 0, 0, -7f + 2f));
        }
        // 거위: 쓰러짐 클립의 프레임
        {
            string path = FrameTablePrefabs[2];
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Goose/Goose_Down.anim");
            List<Frame> frames = new List<Frame>();
            foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                foreach (ObjectReferenceKeyframe k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                    if (k.value is Sprite s && !frames.Any(f => f.Name == s.name)) frames.Add(FrameOf(s));
            sb.AppendLine(SetupFrameMatcher(path, frames, 0, 0, null));
        }
        return sb.ToString();
    }

    private static Frame FrameOf(Sprite s) => new Frame
    {
        Texture = s.texture,
        Rect = new RectInt(Mathf.RoundToInt(s.rect.x), Mathf.RoundToInt(s.rect.y), Mathf.RoundToInt(s.rect.width), Mathf.RoundToInt(s.rect.height)),
        PivotPx = s.pivot,
        Name = s.name,
    };

    private static readonly Dictionary<Texture2D, Color32[]> pixelCache = new Dictionary<Texture2D, Color32[]>();

    // 임포트 설정(Read/Write)과 상관없이 PNG 파일을 직접 읽는다. 텍스처 좌표처럼 왼쪽 아래가 (0, 0)이다.
    private static Color32[] Pixels(Texture2D tex, out int width)
    {
        width = tex.width;
        if (pixelCache.TryGetValue(tex, out Color32[] cached)) return cached;
        Texture2D copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
        Color32[] px = copy.GetPixels32();
        Object.DestroyImmediate(copy);
        pixelCache[tex] = px;
        return px;
    }

    // fixedW/fixedH가 0이면 몸 폭 x 0.75, 높이 = 폭 x 4/15로 정한다. onlyIfFeetAbove가 있으면 발이 그보다 위인 프레임만 넣는다.
    private static string SetupFrameMatcher(string prefabPath, List<Frame> frames, int fixedW, int fixedH, float? onlyIfFeetAbove)
    {
        StringBuilder log = new StringBuilder(prefabPath + ": 프레임 표");
        List<ShadowFrameMatcher.Entry> entries = new List<ShadowFrameMatcher.Entry>();
        foreach (Frame f in frames)
        {
            Color32[] px = Pixels(f.Texture, out int texW);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue;
            for (int y = f.Rect.y; y < f.Rect.y + f.Rect.height; y++)
                for (int x = f.Rect.x; x < f.Rect.x + f.Rect.width; x++)
                    if (px[y * texW + x].a > 0) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); }
            if (minY == int.MaxValue) continue;
            float feet = minY - f.Rect.y - f.PivotPx.y;                        // 보이는 몸 맨 아랫줄의 아래 변(피벗 기준, 위가 +)
            float center = (minX + maxX + 1) * 0.5f - f.Rect.x - f.PivotPx.x;  // 보이는 몸 가로 가운데
            int bodyW = maxX - minX + 1;
            if (onlyIfFeetAbove.HasValue && feet < onlyIfFeetAbove.Value) continue; // 기본 그림자로 충분한 프레임
            int w = fixedW > 0 ? fixedW : Mathf.Max(3, Mathf.RoundToInt(bodyW * 0.75f));
            int h = fixedH > 0 ? fixedH : Mathf.Max(3, Mathf.RoundToInt(w * 4f / 15f));
            EnsureShadowSprite(w, h);
            float left = Mathf.Floor(center - w * 0.5f + 0.5f);                // 그림자 왼쪽 변을 몸 텍셀 격자에 맞춘다
            Vector2 offset = new Vector2((left + w * 0.5f) / PixelsPerUnit, (feet - h + 1f) / PixelsPerUnit);
            entries.Add(new ShadowFrameMatcher.Entry { texture = f.Texture, rect = f.Rect, shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath(w, h)), offset = offset });
            log.Append($"\n    {f.Name}: 몸 폭 {bodyW}, 발 {feet:+0.#;-0.#}, 가운데 {center:+0.#;-0.#} → 그림자 {w}x{h} @ ({offset.x:0.#####}, {offset.y:0.#####})");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            SpriteRenderer body = root.GetComponent<SpriteRenderer>();
            SpriteRenderer shadow = root.transform.Find("Shadow").GetComponent<SpriteRenderer>();
            ShadowFrameMatcher matcher = root.GetComponent<ShadowFrameMatcher>();
            if (matcher == null) matcher = root.AddComponent<ShadowFrameMatcher>();
            SerializedObject so = new SerializedObject(matcher);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("shadow").objectReferenceValue = shadow;
            SerializedProperty arr = so.FindProperty("entries");
            arr.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("texture").objectReferenceValue = entries[i].texture;
                e.FindPropertyRelative("rect").rectIntValue = entries[i].rect;
                e.FindPropertyRelative("shadowSprite").objectReferenceValue = entries[i].shadowSprite;
                e.FindPropertyRelative("offset").vector2Value = entries[i].offset;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return log.Append($"\n    → {entries.Count}개 프레임").ToString();
    }

    public static string SpritePath(int w, int h) => $"{Folder}/Shadow_{w}x{h}.png";

    /// <summary>줄 i(위에서부터)의 칠할 폭: W x √(1 − (i/H)²)를 W와 같은 홀짝으로 반올림한다.</summary>
    public static int RowWidth(int w, int h, int i)
    {
        float exact = w * Mathf.Sqrt(Mathf.Max(0f, 1f - (i / (float)h) * (i / (float)h)));
        int lower = Mathf.FloorToInt(exact);
        if ((lower & 1) != (w & 1)) lower--;
        int upper = lower + 2;
        int width = exact - lower <= upper - exact ? lower : upper;
        return Mathf.Clamp(width, 1, w);
    }

    private static string EnsureShadowSprite(int w, int h)
    {
        string path = SpritePath(w, h);
        Directory.CreateDirectory(Folder);
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[w * h];
        for (int i = 0; i < h; i++)
        {
            int rowWidth = RowWidth(w, h, i);
            int start = (w - rowWidth) / 2;
            int y = h - 1 - i; // 텍스처는 아래가 0
            for (int x = 0; x < w; x++) pixels[y * w + x] = x >= start && x < start + rowWidth ? ShadowColor : new Color32(0, 0, 0, 0);
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
        string rows = string.Join("·", Enumerable.Range(0, h).Select(i => RowWidth(w, h, i)));
        return $"{path} ({w}x{h}, 줄 폭 {rows})";
    }

    private static string SetupPrefab(Target t)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(t.Prefab);
        try
        {
            Transform bodyTransform = string.IsNullOrEmpty(t.BodyPath) ? root.transform : root.transform.Find(t.BodyPath);
            SpriteRenderer body = bodyTransform != null ? bodyTransform.GetComponent<SpriteRenderer>() : null;
            if (body == null) return $"{t.Prefab}: 몸 SpriteRenderer({t.BodyPath})를 찾지 못해 건너뜀";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath(t.W, t.H));
            Transform shadow = bodyTransform.Find("Shadow");
            float alpha = 1f;
            if (shadow == null)
            {
                shadow = new GameObject("Shadow").transform;
                shadow.SetParent(bodyTransform, false);
            }
            else if (shadow.TryGetComponent(out SpriteRenderer old)) alpha = old.color.a;
            shadow.gameObject.layer = bodyTransform.gameObject.layer;
            // 몸 텍셀 → 몸 로컬 좌표: 텍셀 / 몸 PPU
            shadow.localPosition = new Vector3(t.X / t.BodyPpu, t.BottomY / t.BodyPpu, 0f);
            shadow.localRotation = Quaternion.identity;
            shadow.localScale = Vector3.one * t.Scale;
            SpriteRenderer sr = shadow.GetComponent<SpriteRenderer>();
            if (sr == null) sr = shadow.gameObject.AddComponent<SpriteRenderer>(); // ??는 Unity의 가짜 null을 거르지 못한다
            sr.sprite = sprite;
            sr.sharedMaterial = body.sharedMaterial;
            sr.sortingLayerID = body.sortingLayerID;
            sr.sortingOrder = body.sortingOrder - 1;
            sr.color = new Color(1f, 1f, 1f, alpha);
            sr.drawMode = SpriteDrawMode.Simple;
            PrefabUtility.SaveAsPrefabAsset(root, t.Prefab);
            return $"{t.Prefab}: Shadow {t.W}x{t.H} @ ({shadow.localPosition.x:0.#####}, {shadow.localPosition.y:0.#####}) 배율 {t.Scale}, Order {sr.sortingOrder} (몸 {body.sortingOrder}), 알파 {alpha} - {t.Note}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static string SetupPlayerShadow(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.CompareTag("Player"))?.gameObject;
        SpriteRenderer body = player != null ? player.GetComponent<SpriteRenderer>() : null;
        if (body == null) return $"{scenePath}: Player SpriteRenderer를 찾지 못해 건너뜀";
        GameObject go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "PlayerShadow");
        float alpha = 1f;
        if (go == null)
        {
            go = new GameObject("PlayerShadow");
            SceneManager.MoveGameObjectToScene(go, scene);
        }
        else if (go.TryGetComponent(out SpriteRenderer old)) alpha = old.color.a;
        go.layer = player.layer;
        Vector3 rest = new Vector3(player.transform.position.x, player.transform.position.y + PlayerOffsetY, 0f);
        go.transform.SetPositionAndRotation(rest, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath(PlayerW, PlayerH));
        sr.sharedMaterial = body.sharedMaterial;
        sr.sortingLayerID = body.sortingLayerID;
        sr.sortingOrder = body.sortingOrder - 1;
        sr.color = new Color(1f, 1f, 1f, alpha);

        PositionConstraint pc = go.GetComponent<PositionConstraint>();
        if (pc == null) pc = go.AddComponent<PositionConstraint>();
        for (int i = pc.sourceCount - 1; i >= 0; i--) pc.RemoveSource(i);
        pc.AddSource(new ConstraintSource { sourceTransform = player.transform, weight = 1f });
        pc.translationAxis = Axis.X | Axis.Y;
        pc.translationAtRest = rest;
        pc.translationOffset = new Vector3(0f, PlayerOffsetY, 0f);
        pc.weight = 1f;
        pc.locked = true;
        pc.constraintActive = true;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"{scenePath}: PlayerShadow {PlayerW}x{PlayerH}, Player를 따라감(오프셋 y {PlayerOffsetY}, 회전 안 함), Order {sr.sortingOrder} (몸 {body.sortingOrder}), 머티리얼 {sr.sharedMaterial?.name}";
    }
}
