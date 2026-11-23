using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > UI > Build Volume Settings. 타이틀 설정 창(TitleScene/Canvas/.../SettingsPanel)의 빈 Content에 소리 크기 조절 두 줄
/// (배경음·효과음 슬라이더 0~100%, 지금 값 %)을 만들고 VolumeSettingsPanel을 연결한다. 다시 실행하면 같은 모양으로 새로 만든다.
/// - 모양: 창(크레딧 창과 같은 크림색 창)·글씨(UIFont SDF - Plain, #293440)·도트 그림 3장(Assets/Sprites/UI/Settings, 팔레트 #293440·#FFFEF1·#FFCC43,
///   타이틀 버튼처럼 모서리 1칸을 비움). 그림 1칸 = 기준 단위 6(타이틀 버튼과 같은 크기).
/// - 그림 가져오기: Sprite, Point 필터, 압축 끔, 밉맵 끔.
/// - 타이틀 메인 카메라에 AudioListener가 없으면 붙인다(설정 창에서 효과음 미리듣기가 들리게).
/// batchmode: -executeMethod VolumeSettingsBuilder.BuildBatch
/// </summary>
public static class VolumeSettingsBuilder
{
    private const string ScenePath = "Assets/Scenes/TitleScene.unity";
    private const string SpriteDir = "Assets/Sprites/UI/Settings/";
    private const string FontPath = "Assets/Fonts/Generated/UIFont SDF.asset";
    private const string PlainMaterialPath = "Assets/Fonts/Generated/UIFont SDF - Plain.mat";
    private const string RootName = "VolumeRows";
    private const float Texel = 6f;           // 그림 1칸 = 기준 단위 6 (타이틀 버튼 40x14칸 → 242.7x65.3)
    private static readonly Color Ink = new Color32(0x29, 0x34, 0x40, 0xFF);

    [MenuItem("Tools/UI/Build Volume Settings")]
    public static void Build()
    {
        ConfigureSprites();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TitleMenu title = Object.FindAnyObjectByType<TitleMenu>(FindObjectsInactive.Include);
        GameObject panel = title != null ? (GameObject)new SerializedObject(title).FindProperty("settingsPanel").objectReferenceValue : null;
        Transform content = panel != null ? panel.transform.Find("Window/Content") : null;
        if (content == null) { Debug.LogError("VolumeSettingsBuilder: SettingsPanel/Window/Content를 찾지 못했다."); return; }

        Transform old = content.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        Sprite track = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "volume_track.png");
        Sprite fill = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "volume_fill.png");
        Sprite knob = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + "volume_knob.png");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Material plain = AssetDatabase.LoadAssetAtPath<Material>(PlainMaterialPath);

        RectTransform root = NewRect(RootName, content);
        Stretch(root);
        VerticalLayoutGroup v = root.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.MiddleCenter;
        v.spacing = 40f;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        (Slider music, TMP_Text musicValue) = Row(root, "MusicRow", "배경음", track, fill, knob, font, plain);
        (Slider sfx, TMP_Text sfxValue) = Row(root, "SfxRow", "효과음", track, fill, knob, font, plain);

        VolumeSettingsPanel vsp = panel.GetComponent<VolumeSettingsPanel>();
        if (vsp == null) vsp = panel.AddComponent<VolumeSettingsPanel>();
        var so = new SerializedObject(vsp);
        so.FindProperty("musicSlider").objectReferenceValue = music;
        so.FindProperty("sfxSlider").objectReferenceValue = sfx;
        so.FindProperty("musicValueText").objectReferenceValue = musicValue;
        so.FindProperty("sfxValueText").objectReferenceValue = sfxValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 타이틀에는 소리를 듣는 AudioListener가 없어(다른 씬 카메라에는 있다) 설정 창에서 효과음 크기를 들어볼 수 없었다 - 메인 카메라에 붙인다.
        Camera cam = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(c => c.CompareTag("MainCamera"));
        if (cam != null && cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        if (cam == null) Debug.LogWarning("VolumeSettingsBuilder: 타이틀 메인 카메라가 없어 AudioListener를 붙이지 못했다.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("VolumeSettingsBuilder: 타이틀 설정 창에 소리 크기 조절을 만들었다.");
    }

    public static void BuildBatch()
    {
        Build();
        EditorApplication.Exit(0);
    }

    private static (Slider, TMP_Text) Row(Transform parent, string name, string label, Sprite track, Sprite fill, Sprite knob, TMP_FontAsset font, Material plain)
    {
        RectTransform row = NewRect(name, parent);
        HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft;
        h.spacing = 32f;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        LayoutElement rowLe = row.gameObject.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 10 * Texel + 20f;

        Text(row, "Label", label, TextAlignmentOptions.MidlineLeft, 180f, font, plain);

        // 슬라이더: 바탕(도트 바) / 채움(노랑) / 손잡이(작은 타이틀 버튼 모양)
        RectTransform sliderRt = NewRect("Slider", row);
        sliderRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Slider slider = sliderRt.gameObject.AddComponent<Slider>();

        RectTransform bg = NewRect("Background", sliderRt);
        MiddleBand(bg, 6 * Texel, 0f, 0f);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        Sliced(bgImg, track);

        RectTransform fillArea = NewRect("Fill Area", sliderRt);
        MiddleBand(fillArea, 6 * Texel, 0f, 0f);
        RectTransform fillRt = NewRect("Fill", fillArea);
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
        Image fillImg = fillRt.gameObject.AddComponent<Image>();
        Sliced(fillImg, fill);
        fillImg.raycastTarget = false;

        // 손잡이 영역: 슬라이더는 손잡이 세로를 이 영역 높이에 맞춰 늘리므로, 영역을 손잡이 높이(10칸)로 둔다. 좌우는 손잡이 반 폭만큼 안쪽.
        RectTransform handleArea = NewRect("Handle Slide Area", sliderRt);
        MiddleBand(handleArea, 10 * Texel, 4 * Texel, 4 * Texel);
        RectTransform handle = NewRect("Handle", handleArea);
        handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(8 * Texel, 0f);
        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = knob;
        handleImg.type = Image.Type.Simple;

        slider.fillRect = fillRt;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 100f; slider.wholeNumbers = true;
        slider.value = 100f;
        ColorBlock cb = slider.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor = Color.white;
        slider.colors = cb;
        Navigation nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;

        TMP_Text value = Text(row, "Value", "100%", TextAlignmentOptions.MidlineRight, 150f, font, plain);
        return (slider, value);
    }

    private static TMP_Text Text(Transform parent, string name, string text, TextAlignmentOptions align, float width, TMP_FontAsset font, Material plain)
    {
        RectTransform rt = NewRect(name, parent);
        LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width;
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        if (plain != null) t.fontSharedMaterial = plain;
        t.text = text;
        t.fontSize = 44f;
        t.color = Ink;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    private static void Sliced(Image img, Sprite s)
    {
        img.sprite = s;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f / Texel;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void MiddleBand(RectTransform rt, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, -height * 0.5f);
        rt.offsetMax = new Vector2(-right, height * 0.5f);
    }

    // 도트 규칙: Sprite·Point 필터·압축 끔·밉맵 끔(모든 플랫폼 기본값을 따른다). 바·채움은 9칸 나눔(테두리 2칸).
    private static void ConfigureSprites()
    {
        foreach ((string file, int border) in new[] { ("volume_track.png", 2), ("volume_fill.png", 2), ("volume_knob.png", 0) })
        {
            string path = SpriteDir + file;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) { Debug.LogError("VolumeSettingsBuilder: 그림이 없다 " + path); continue; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.spriteBorder = new Vector4(border, border, border, border);
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings(platform);
                if (!ps.overridden) continue;
                ps.overridden = false;
                ti.SetPlatformTextureSettings(ps);
            }
            ti.SaveAndReimport();
        }
    }
}
