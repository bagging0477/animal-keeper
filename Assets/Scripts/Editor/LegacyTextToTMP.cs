using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// 레거시 UnityEngine.UI.Text를 TextMeshProUGUI로 단계별로 바꾸는 도구(batchmode 전용).
///
/// 단계(-tmpStage): 1 PauseMenu/GameOverPanel 프리팹, 2 TitleScene/ClassSelectScene, 3 ShelterScene, 4 TruckScene/VillageScene.
/// 작업(-executeMethod):
///   LegacyTextToTMP.Convert  - 스크립트 필드가 아직 Text 타입일 때 실행한다. Text를 참조하는 필드를 Logs/tmp_relink_stage{N}.txt에
///                              적어 두고, Text를 지운 자리에 TMP를 붙인다(문자열/정렬/색/RectTransform 유지, 공유 폰트·머티리얼,
///                              Auto Size 끔, 1과 float 오차만큼 다른 Scale은 1로).
///   LegacyTextToTMP.Relink   - 스크립트 필드를 TMP_Text로 바꾸고 컴파일한 뒤 실행한다. 적어 둔 필드에 새 TMP를 다시 연결한다.
///   LegacyTextToTMP.Verify   - 레거시 Text 개수, TMP 글자 누락/넘침/겹침을 점검하고 패널별로 1920x1080, 1280x720 PNG를 찍는다
///                              (-tmpTag before|after). 씬/프리팹은 저장하지 않는다.
///   LegacyTextToTMP.Smoke    - Play 모드에서 단계의 화면을 실제로 띄워 예외와 글자 누락 경고를 모은다.
/// 결과는 Logs/TMPConversion/ 아래에 남는다. 끝나면 에디터가 종료된다(실패하면 종료 코드 1).
/// </summary>
[InitializeOnLoad]
public static class LegacyTextToTMP
{
    private const string OutputFolder = "Logs/TMPConversion";
    private const string LegacyTextGuid = "5f7201a12d95ffc409449d95f23cf332";
    private static readonly Vector2Int[] Resolutions = { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };

    // 노란색 프리셋(UIFont SDF - Yellow)을 쓰는 마일리지 표시. 색은 머티리얼이 내므로 정점 색은 흰색으로 둔다.
    private static readonly HashSet<string> MileageTextNames = new HashSet<string> { "MileageLineText" };

    private static readonly Dictionary<int, string[]> StageAssets = new Dictionary<int, string[]>
    {
        { 1, new[] { "Assets/Resources/PauseMenu.prefab", "Assets/Prefabs/UI/GameOverPanel.prefab" } },
        { 2, new[] { "Assets/Scenes/TitleScene.unity", "Assets/Scenes/ClassSelectScene.unity" } },
        { 3, new[] { "Assets/Scenes/ShelterScene.unity" } },
        { 4, new[] { "Assets/Scenes/TruckScene.unity", "Assets/Scenes/VillageScene.unity" } },
    };

    // 캡처 때 실행 중에만 채워지는 빈 글씨에 넣어 보는 견본 문자열(저장하지 않는다). 가장 긴 경우를 고른다.
    private static readonly Dictionary<string, string> SampleTexts = new Dictionary<string, string>
    {
        { "NameText", "트래퍼" },
        { "StatsText", "체력 4 · 이동 속도 5.5\n스태미나 120 · 시야 7.7\n무기: 마취총 · 시작 아이템 지뢰 2" },
        { "PriceText", "1200 마일리지 - E를 눌러 구매 후 장착" },
    };

    private static int Stage => ArgInt("-tmpStage", 0);
    private static string Tag => ArgString("-tmpTag", "after");

    // ===================== Convert =====================

    public static void Convert() => RunBatch(() =>
    {
        int stage = RequireStage();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.MainAssetPath);
        Material yellow = AssetDatabase.LoadAssetAtPath<Material>(UIFontBuilder.YellowPresetPath);
        if (font == null || font.material == null || yellow == null) throw new Exception("공유 TMP 폰트 또는 노란색 프리셋을 찾지 못했다.");

        var report = new StringBuilder($"LegacyTextToTMP.Convert stage {stage} {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        var relink = new List<string>();
        foreach (string assetPath in StageAssets[stage])
        {
            report.AppendLine($"\n== {assetPath}");
            EditAsset(assetPath, true, roots =>
            {
                List<Text> texts = roots.SelectMany(r => r.GetComponentsInChildren<Text>(true)).ToList();
                foreach (Text text in texts)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(text))
                    {
                        report.AppendLine($"  건너뜀(프리팹 인스턴스 - 원본 프리팹에서 바뀐다): {PathOf(text.transform)}");
                        continue;
                    }
                    // 이 Text를 가리키는 직렬화 필드를 모두 적어 둔다(필드 타입을 TMP_Text로 바꾼 뒤 Relink가 다시 연결한다).
                    foreach (Component owner in roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)))
                    {
                        if (owner == null || owner == text || owner is Transform) continue;
                        var so = new SerializedObject(owner);
                        SerializedProperty p = so.GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != text) continue;
                            relink.Add(string.Join("\t", assetPath, IndexPath(owner.transform), owner.GetType().Name,
                                ComponentIndex(owner).ToString(), p.propertyPath, IndexPath(text.transform), PathOf(owner.transform), PathOf(text.transform)));
                        }
                    }
                    report.AppendLine("  " + ConvertOne(text, font, yellow));
                }
            });
        }
        Directory.CreateDirectory(OutputFolder);
        File.WriteAllLines(RelinkPath(stage), relink, new UTF8Encoding(false));
        report.AppendLine($"\n다시 연결할 필드 {relink.Count}개 -> {RelinkPath(stage)}");
        foreach (string line in relink) { string[] c = line.Split('\t'); report.AppendLine($"  {c[2]}.{c[4]} @ {c[6]}  ->  {c[7]}"); }
        WriteReport($"stage{stage}_convert.txt", report);
    });

    private static string ConvertOne(Text text, TMP_FontAsset font, Material yellow)
    {
        GameObject go = text.gameObject;
        var rt = (RectTransform)go.transform;
        // 지우기 전에 값을 모두 꺼내 둔다.
        string content = text.text;
        int fontSize = text.fontSize;
        FontStyle style = text.fontStyle;
        TextAnchor anchor = text.alignment;
        Color color = text.color;
        bool raycast = text.raycastTarget, rich = text.supportRichText, maskable = text.maskable, enabled = text.enabled;
        HorizontalWrapMode hWrap = text.horizontalOverflow;
        VerticalWrapMode vWrap = text.verticalOverflow;
        float lineSpacing = text.lineSpacing;
        bool bestFit = text.resizeTextForBestFit;
        Vector2 aMin = rt.anchorMin, aMax = rt.anchorMax, pos = rt.anchoredPosition, size = rt.sizeDelta, pivot = rt.pivot;
        Vector3 scale = rt.localScale;
        int legacyLines = LegacyLineCount(text);

        Object.DestroyImmediate(text, true);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        bool mileage = MileageTextNames.Contains(go.name);
        tmp.font = font;
        tmp.fontSharedMaterial = mileage ? yellow : font.material;
        tmp.text = content;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;
        // UIFont SDF는 이미 NotoSansKR-Bold로 만든 폰트라 Bold 스타일을 더하면 외곽선과 합쳐 덩어리처럼 보인다 - 폰트 자체 굵기만 쓴다.
        tmp.fontStyle = style == FontStyle.Italic || style == FontStyle.BoldAndItalic ? FontStyles.Italic : FontStyles.Normal;
        tmp.alignment = MapAlignment(anchor);
        tmp.color = mileage ? Color.white : color;
        tmp.raycastTarget = raycast;
        tmp.richText = rich;
        tmp.maskable = maskable;
        tmp.enabled = enabled;
        tmp.textWrappingMode = hWrap == HorizontalWrapMode.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        // 레거시의 세로 Truncate는 줄 높이가 상자보다 크면 그 줄을 통째로 숨긴다. NotoSansKR은 줄 높이가 커서 같은 상자에서
        // 글씨가 사라질 수 있으므로 넘침(Overflow)으로 두고, 넘치는 곳은 Verify가 따로 알려 준다.
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.lineSpacing = (lineSpacing - 1f) * 100f;
        tmp.margin = Vector4.zero;
        tmp.extraPadding = false;

        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos; rt.sizeDelta = size; rt.pivot = pivot;
        bool scaleNoise = (scale - Vector3.one).sqrMagnitude > 0f && Mathf.Abs(scale.x - 1f) < 1e-3f && Mathf.Abs(scale.y - 1f) < 1e-3f && Mathf.Abs(scale.z - 1f) < 1e-3f;
        if (scaleNoise) rt.localScale = Vector3.one;
        EditorUtility.SetDirty(go);

        var notes = new List<string>();
        if (mileage) notes.Add("노란색 프리셋");
        if (bestFit) notes.Add("BestFit 껐음");
        if (style == FontStyle.Bold || style == FontStyle.BoldAndItalic) notes.Add("Bold 뺌");
        if (vWrap == VerticalWrapMode.Truncate) notes.Add("세로 Truncate→Overflow");
        if (scaleNoise) notes.Add($"Scale {scale.x:0.#######}→1");
        else if (scale != Vector3.one) notes.Add($"Scale {scale.x:0.######} 그대로(확인 필요)");
        return $"{PathOf(go.transform)}: \"{OneLine(content)}\" size {fontSize} {style} {anchor}→{tmp.alignment} 줄 {legacyLines}" +
               (notes.Count > 0 ? "  [" + string.Join(", ", notes) + "]" : "");
    }

    private static TextAlignmentOptions MapAlignment(TextAnchor a) => a switch
    {
        TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
        TextAnchor.UpperCenter => TextAlignmentOptions.Top,
        TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
        TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
        TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
        TextAnchor.MiddleRight => TextAlignmentOptions.Right,
        TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
        TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
        _ => TextAlignmentOptions.BottomRight,
    };

    private static int LegacyLineCount(Text text)
    {
        if (string.IsNullOrEmpty(text.text) || text.font == null) return 0;
        var generator = new TextGenerator();
        generator.Populate(text.text, text.GetGenerationSettings(((RectTransform)text.transform).rect.size));
        return generator.lineCount;
    }

    /// <summary>Bold 스타일을 그대로 옮기던 때 바꾼 단계의 TMP에서 Bold를 뺀다(-tmpStage). 이 단계 에셋의 TMP는 모두 변환으로 만든 것이다.</summary>
    public static void ClearBold() => RunBatch(() =>
    {
        int stage = RequireStage();
        var report = new StringBuilder($"LegacyTextToTMP.ClearBold stage {stage} {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        foreach (string assetPath in StageAssets[stage])
        {
            report.AppendLine($"\n== {assetPath}");
            EditAsset(assetPath, true, roots =>
            {
                foreach (TMP_Text t in roots.SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)))
                {
                    if ((t.fontStyle & FontStyles.Bold) == 0) continue;
                    t.fontStyle &= ~FontStyles.Bold;
                    EditorUtility.SetDirty(t);
                    report.AppendLine($"  Bold 뺌: {PathOf(t.transform)} \"{OneLine(t.text)}\"");
                }
            });
        }
        WriteReport($"stage{stage}_clearbold.txt", report);
    });

    // ===================== Relink =====================

    public static void Relink() => RunBatch(() =>
    {
        int stage = RequireStage();
        var report = new StringBuilder($"LegacyTextToTMP.Relink stage {stage} {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        string[] lines = File.Exists(RelinkPath(stage)) ? File.ReadAllLines(RelinkPath(stage), Encoding.UTF8) : Array.Empty<string>();
        int ok = 0;
        var failed = new List<string>();
        foreach (IGrouping<string, string[]> group in lines.Where(l => l.Length > 0).Select(l => l.Split('\t')).GroupBy(c => c[0]))
        {
            EditAsset(group.Key, true, roots =>
            {
                foreach (string[] c in group)
                {
                    string label = $"{c[2]}.{c[4]} @ {c[6]} -> {c[7]}";
                    Transform ownerT = FindByIndexPath(roots, c[1]), targetT = FindByIndexPath(roots, c[5]);
                    Component owner = ownerT == null ? null : ownerT.GetComponents<Component>().Where(x => x != null && x.GetType().Name == c[2]).ElementAtOrDefault(int.Parse(c[3]));
                    TMP_Text target = targetT == null ? null : targetT.GetComponent<TMP_Text>();
                    if (owner == null || target == null) { failed.Add($"{label}: {(owner == null ? "필드 주인" : "TMP")}을 찾지 못함"); continue; }
                    var so = new SerializedObject(owner);
                    SerializedProperty p = so.FindProperty(c[4]);
                    if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) { failed.Add($"{label}: 필드가 없음"); continue; }
                    p.objectReferenceValue = target;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    if (new SerializedObject(owner).FindProperty(c[4]).objectReferenceValue != target) { failed.Add($"{label}: 타입이 맞지 않아 연결 안 됨(필드가 아직 Text?)"); continue; }
                    ok++;
                    report.AppendLine($"  연결: {label}");
                }
            });
        }
        report.AppendLine($"\n연결 {ok}/{lines.Count(l => l.Length > 0)}, 실패 {failed.Count}");
        foreach (string f in failed) report.AppendLine("  실패: " + f);
        WriteReport($"stage{stage}_relink.txt", report);
        if (failed.Count > 0) throw new Exception($"다시 연결하지 못한 필드 {failed.Count}개");
    });

    // ===================== Verify =====================

    public static void Verify() => RunBatch(() =>
    {
        int stage = RequireStage();
        string tag = Tag;
        string folder = Path.Combine(OutputFolder, $"stage{stage}_{tag}");
        Directory.CreateDirectory(folder);
        var report = new StringBuilder($"LegacyTextToTMP.Verify stage {stage} [{tag}] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        int problems = 0;

        // 1) 프로젝트 전체의 레거시 Text 개수(파일에서 직접 센다).
        report.AppendLine("\n== 레거시 Text 개수 (씬/프리팹 파일 기준)");
        int total = 0;
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && (p.EndsWith(".unity") || p.EndsWith(".prefab"))).OrderBy(p => p))
        {
            int n = CountOccurrences(File.ReadAllText(path), $"guid: {LegacyTextGuid}");
            if (n == 0) continue;
            total += n;
            report.AppendLine($"  {n,3}  {path}");
        }
        report.AppendLine($"  합계 {total}");

        // 2) 단계 에셋의 글씨 점검 + 캡처.
        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.FallbackAssetPath);
        foreach (string assetPath in StageAssets[stage])
        {
            report.AppendLine($"\n== {assetPath}");
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            List<Canvas> canvases;
            GameObject[] roots;
            if (assetPath.EndsWith(".prefab"))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Canvas host = UIFontPreviewCapture.CreateCanvas("PrefabHost");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
                if (instance.GetComponent<Canvas>() == null) instance.transform.SetParent(host.transform, false);
                roots = new[] { host.gameObject, instance };
                canvases = new List<Canvas> { host };
                canvases.AddRange(instance.GetComponentsInChildren<Canvas>(true).Where(c => c.isRootCanvas));
            }
            else
            {
                Scene scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
                roots = scene.GetRootGameObjects();
                canvases = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
                    .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToList();
            }

            // 패널별로 찍는다: 저장된 상태 그대로 1장 + 꺼져 있는 패널마다 그 패널만 켠 1장.
            var variants = new List<(string name, GameObject panel)> { ("base", null) };
            foreach (Canvas canvas in canvases)
            {
                foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.activeSelf || !HasText(t)) continue;
                    bool topMostInactive = true;
                    for (Transform a = t.parent; a != null && a != canvas.transform.parent; a = a.parent) if (!a.gameObject.activeSelf) topMostInactive = false;
                    if (topMostInactive) variants.Add((t.name, t.gameObject));
                }
            }
            foreach ((string name, GameObject panel) in variants)
            {
                var restoreActive = new List<(GameObject go, bool active)>();
                var restoreText = new List<(Component c, string text)>();
                if (panel != null)
                {
                    foreach (Transform t in panel.GetComponentsInChildren<Transform>(true)) { restoreActive.Add((t.gameObject, t.gameObject.activeSelf)); t.gameObject.SetActive(true); }
                }
                foreach (Canvas canvas in canvases) FillSamples(canvas, restoreText);
                Canvas.ForceUpdateCanvases();

                problems += CheckTexts(canvases, panel, report, name, fallback);
                foreach (Vector2Int res in Resolutions)
                {
                    UIFontPreviewCapture.Render(Path.Combine(folder, $"{baseName}_{Sanitize(name)}_{res.x}x{res.y}.png"), canvases, res);
                }

                foreach ((Component c, string text) in restoreText) SetText(c, text);
                foreach ((GameObject go, bool active) in Enumerable.Reverse(restoreActive)) go.SetActive(active);
            }
        }

        // 견본 문자열 때문에 Fallback에 글자가 들어갔다면 커밋된 상태로 되돌린다(UIFontPreviewCapture와 같은 처리).
        if (fallback != null && fallback.characterTable.Count > 0)
        {
            report.AppendLine($"\n참고: Fallback 폰트로 그린 글자 {fallback.characterTable.Count}개: {new string(fallback.characterTable.Select(c => (char)c.unicode).ToArray())}");
            fallback.ClearFontAssetData(true);
            EditorUtility.SetDirty(fallback);
            AssetDatabase.SaveAssetIfDirty(fallback);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        report.AppendLine($"\n문제 {problems}건. 캡처: {Path.GetFullPath(folder)}");
        WriteReport($"stage{stage}_verify_{tag}.txt", report);
    });

    private static int CheckTexts(List<Canvas> canvases, GameObject panel, StringBuilder report, string variant, TMP_FontAsset fallback)
    {
        int problems = 0;
        var visible = canvases.SelectMany(c => c.GetComponentsInChildren<TMP_Text>(false)).Where(t => t.enabled && !string.IsNullOrEmpty(t.text)).ToList();
        if (panel != null) visible = visible.Where(t => t.transform.IsChildOf(panel.transform)).ToList();
        foreach (TMP_Text t in visible) t.ForceMeshUpdate(true, true);

        var lines = new List<string>();
        foreach (TMP_Text t in visible)
        {
            var rt = (RectTransform)t.transform;
            var issues = new List<string>();
            string parsed = t.GetParsedText();
            string missing = new string(parsed.Where(ch => !char.IsWhiteSpace(ch) && !t.font.HasCharacter(ch, false, false)).Distinct().ToArray());
            if (missing.Length > 0)
            {
                string notInFallback = new string(missing.Where(ch => fallback == null || !fallback.HasCharacter(ch, false, true)).ToArray());
                issues.Add(notInFallback.Length > 0 ? $"네모로 깨짐 \"{notInFallback}\"" : $"정적 아틀라스에 없음(Fallback이 그림) \"{missing}\"");
            }
            int drawn = 0;
            for (int i = 0; i < t.textInfo.characterCount; i++) if (t.textInfo.characterInfo[i].isVisible) drawn++;
            int expected = parsed.Count(ch => !char.IsWhiteSpace(ch));
            if (drawn < expected) issues.Add($"그려진 글자 {drawn}/{expected}");
            if (t.isTextTruncated) issues.Add("잘림(Truncated)");
            Bounds b = t.textBounds;
            Rect r = rt.rect;
            float outL = r.xMin - b.min.x, outR = b.max.x - r.xMax, outB = r.yMin - b.min.y, outT = b.max.y - r.yMax;
            if (expected > 0 && Mathf.Max(outL, outR) > 1f) issues.Add($"가로 넘침 {Mathf.Max(outL, outR):0.#}");
            if (expected > 0 && Mathf.Max(outB, outT) > 1f) issues.Add($"세로 넘침 {Mathf.Max(outB, outT):0.#}");
            if (issues.Count > 0) problems += issues.Count(s => !s.StartsWith("정적"));
            lines.Add($"  [{variant}] {PathOf(t.transform)} \"{OneLine(t.text)}\" 줄 {t.textInfo.lineCount}, 글자 {drawn}" +
                      (issues.Count > 0 ? "  <-- " + string.Join(", ", issues) : ""));
        }

        // 같은 패널(캔버스 바로 아래 같은 자식) 안에서 글씨 영역이 겹치는지 본다.
        for (int i = 0; i < visible.Count; i++)
        {
            for (int j = i + 1; j < visible.Count; j++)
            {
                TMP_Text a = visible[i], c = visible[j];
                if (TopChild(a.transform) != TopChild(c.transform)) continue;
                Rect ra = CanvasRect(a), rc = CanvasRect(c);
                if (ra.width <= 0 || rc.width <= 0) continue;
                float w = Mathf.Min(ra.xMax, rc.xMax) - Mathf.Max(ra.xMin, rc.xMin), h = Mathf.Min(ra.yMax, rc.yMax) - Mathf.Max(ra.yMin, rc.yMin);
                if (w > 1f && h > 1f)
                {
                    problems++;
                    lines.Add($"  [{variant}] 겹침 {w:0}x{h:0}: {PathOf(a.transform)} / {PathOf(c.transform)}");
                }
            }
        }
        foreach (string l in lines) report.AppendLine(l);
        return problems;
    }

    private static Rect CanvasRect(TMP_Text t)
    {
        Canvas canvas = t.canvas != null ? t.canvas.rootCanvas : null;
        Bounds b = t.textBounds;
        Transform space = canvas != null ? canvas.transform : t.transform.root;
        Vector3 p0 = space.InverseTransformPoint(t.transform.TransformPoint(b.min));
        Vector3 p1 = space.InverseTransformPoint(t.transform.TransformPoint(b.max));
        return Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y));
    }

    private static Transform TopChild(Transform t)
    {
        Canvas root = t.GetComponentInParent<Canvas>(true)?.rootCanvas;
        Transform top = t;
        while (top.parent != null && (root == null || top.parent != root.transform)) top = top.parent;
        return top;
    }

    private static bool HasText(Transform t) => t.GetComponentsInChildren<TMP_Text>(true).Length > 0 || t.GetComponentsInChildren<Text>(true).Length > 0;

    private static void FillSamples(Canvas canvas, List<(Component, string)> restore)
    {
        foreach (Component c in canvas.GetComponentsInChildren<TMP_Text>(true).Cast<Component>().Concat(canvas.GetComponentsInChildren<Text>(true)))
        {
            string current = c is TMP_Text tmp ? tmp.text : ((Text)c).text;
            if (!string.IsNullOrEmpty(current) || !SampleTexts.TryGetValue(c.gameObject.name, out string sample)) continue;
            restore.Add((c, current));
            SetText(c, sample);
        }
    }

    private static void SetText(Component c, string text)
    {
        if (c is TMP_Text tmp) tmp.text = text;
        else if (c is Text legacy) legacy.text = text;
    }

    // ===================== Smoke (Play mode) =====================

    private const string KeyActive = "LTT.Smoke.Active", KeyStage = "LTT.Smoke.Stage", KeyDone = "LTT.Smoke.Done", KeyFailed = "LTT.Smoke.Failed";
    private static IEnumerator smokeRoutine;
    private static readonly List<string> smokeLogs = new List<string>();
    private static readonly StringBuilder smokeReport = new StringBuilder();

    static LegacyTextToTMP()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void Smoke()
    {
        int stage = RequireStageOrExit();
        if (stage == 0) return;
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetBool(KeyFailed, false);
        SessionState.SetInt(KeyStage, stage);
        // 새 게임 상태로 TitleScene에서 시작한다(GameManager와 AudioManager가 여기서 만들어진다).
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            smokeLogs.Clear();
            smokeReport.Clear();
            Application.logMessageReceived += OnSmokeLog;
            smokeRoutine = SmokeRun(SessionState.GetInt(KeyStage, 0));
            EditorApplication.update += SmokeStep;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
            EditorApplication.Exit(SessionState.GetBool(KeyFailed, false) ? 1 : 0);
        }
    }

    private static void OnSmokeLog(string message, string stackTrace, LogType type)
    {
        bool missingGlyph = message.Contains("was not found in the") || message.Contains("character with Unicode value");
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || missingGlyph)
        {
            smokeLogs.Add($"[{type}] {message}" + (type == LogType.Exception ? "\n" + stackTrace : ""));
        }
    }

    private static void SmokeStep()
    {
        if (smokeRoutine == null || !EditorApplication.isPlaying) { EditorApplication.update -= SmokeStep; return; }
        bool more;
        try { more = smokeRoutine.MoveNext(); }
        catch (Exception e) { smokeLogs.Add("[Smoke] " + e); more = false; }
        if (more) return;

        EditorApplication.update -= SmokeStep;
        Application.logMessageReceived -= OnSmokeLog;
        smokeRoutine = null;
        int stage = SessionState.GetInt(KeyStage, 0);
        smokeReport.Insert(0, $"LegacyTextToTMP.Smoke stage {stage} {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        smokeReport.AppendLine($"\n에러/예외/글자 누락 로그 {smokeLogs.Count}건");
        foreach (string l in smokeLogs) smokeReport.AppendLine("  " + l);
        WriteReport($"stage{stage}_smoke.txt", smokeReport);
        SessionState.SetBool(KeyFailed, smokeLogs.Count > 0);
        SessionState.SetBool(KeyDone, true);
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator SmokeRun(int stage)
    {
        yield return WaitFrames(30);
        switch (stage)
        {
            case 1:
            {
                // ESC 메뉴(PauseMenu는 시작할 때 Resources에서 만들어진다)와 설정 창.
                PauseMenu pause = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
                if (pause == null) { smokeLogs.Add("[Smoke] PauseMenu 인스턴스가 없다"); yield break; }
                GameObject menuRoot = PrivateField<GameObject>(pause, "menuRoot");
                if (menuRoot != null) menuRoot.SetActive(true);
                yield return WaitFrames(3);
                ReportTexts("PauseMenu 메뉴", pause.gameObject);
                pause.OnSetting();
                yield return WaitFrames(3);
                ReportTexts("PauseMenu 설정", pause.gameObject);
                pause.OnResume();

                // 게임오버 패널 프리팹을 띄운다(TruckScene에 놓인 것과 같은 프리팹).
                Canvas host = UIFontPreviewCapture.CreateCanvas("SmokeHost");
                var panel = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GameOverPanel.prefab"), host.transform, false);
                panel.GetComponent<GameOverPanel>()?.Show();
                yield return WaitFrames(3);
                ReportTexts("GameOverPanel", panel);
                Time.timeScale = 1f;
                break;
            }
            case 2:
            {
                // TitleScene: 시작 버튼과 설정 창.
                Canvas title = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).FirstOrDefault(c => c.name == "TitleCanvas");
                if (title == null) { smokeLogs.Add("[Smoke] TitleCanvas가 없다"); yield break; }
                ReportTexts("TitleScene 버튼", title.gameObject);
                Transform settings = title.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SettingsPanel");
                if (settings != null)
                {
                    settings.gameObject.SetActive(true);
                    yield return WaitFrames(3);
                    ReportTexts("TitleScene 설정 창", settings.gameObject);
                    settings.gameObject.SetActive(false);
                }

                // ClassSelectScene: 직업 정보 팝업(실제 스탠드 위에 띄운다)과 보유 마일리지.
                SceneManager.LoadScene("ClassSelectScene");
                yield return WaitFrames(30);
                ClassInfoPopup popup = Object.FindAnyObjectByType<ClassInfoPopup>(FindObjectsInactive.Include);
                ClassInteractPoint stand = Object.FindAnyObjectByType<ClassInteractPoint>();
                if (popup == null) { smokeLogs.Add("[Smoke] ClassInfoPopup이 없다"); yield break; }
                for (int i = 0; i < 5; i++)
                {
                    popup.Show(SampleTexts["NameText"], SampleTexts["StatsText"], SampleTexts["PriceText"], stand != null ? stand.transform : popup.transform);
                    yield return null;
                }
                popup.Show(SampleTexts["NameText"], SampleTexts["StatsText"], SampleTexts["PriceText"], stand != null ? stand.transform : popup.transform);
                ReportTexts("ClassSelectScene 직업 팝업", popup.gameObject.scene.GetRootGameObjects().First(r => r.GetComponentInChildren<ClassInfoPopup>(true) != null));
                break;
            }
            default:
                smokeReport.AppendLine($"stage {stage}: 아직 정의하지 않았다");
                break;
        }
        yield return WaitFrames(5);
    }

    private static void ReportTexts(string label, GameObject root)
    {
        smokeReport.AppendLine($"\n== {label}");
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            t.ForceMeshUpdate(true, true);
            int expected = t.GetParsedText().Count(ch => !char.IsWhiteSpace(ch));
            int drawn = 0;
            for (int i = 0; i < t.textInfo.characterCount; i++) if (t.textInfo.characterInfo[i].isVisible) drawn++;
            string mark = drawn < expected ? $"  <-- 그려진 글자 {drawn}/{expected}" : "";
            if (mark.Length > 0) smokeLogs.Add($"[Smoke] {label} {PathOf(t.transform)} 글자 부족 {drawn}/{expected}");
            smokeReport.AppendLine($"  {PathOf(t.transform)} \"{OneLine(t.text)}\" 글자 {drawn}/{expected} 폰트 {t.font?.name} 머티리얼 {t.fontSharedMaterial?.name}{mark}");
        }
        int legacy = root.GetComponentsInChildren<Text>(true).Length;
        if (legacy > 0) smokeReport.AppendLine($"  레거시 Text {legacy}개 남음");
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private static T PrivateField<T>(object target, string name) where T : class =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(target) as T;

    // ===================== 공통 =====================

    private static void RunBatch(Action body)
    {
        int exitCode = 0;
        try { body(); }
        catch (Exception e) { Debug.LogException(e); exitCode = 1; }
        EditorApplication.Exit(exitCode);
    }

    /// <summary>프리팹이면 LoadPrefabContents로, 씬이면 OpenScene으로 열어 body를 실행하고 save면 저장한다.</summary>
    private static void EditAsset(string assetPath, bool save, Action<GameObject[]> body)
    {
        if (assetPath.EndsWith(".prefab"))
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                body(new[] { root });
                if (save) PrefabUtility.SaveAsPrefabAsset(root, assetPath, out bool success);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else
        {
            Scene scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
            body(scene.GetRootGameObjects());
            if (save)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        AssetDatabase.SaveAssets();
    }

    private static string IndexPath(Transform t)
    {
        var parts = new List<string>();
        for (Transform x = t; x != null; x = x.parent) parts.Insert(0, x.GetSiblingIndex().ToString());
        return string.Join("/", parts);
    }

    private static Transform FindByIndexPath(GameObject[] roots, string path)
    {
        int[] idx = path.Split('/').Select(int.Parse).ToArray();
        // 씬은 루트 순서, 프리팹은 루트가 하나다.
        Transform t = roots.Length == 1 ? roots[0].transform : roots.FirstOrDefault(r => r.transform.GetSiblingIndex() == idx[0])?.transform;
        for (int i = 1; t != null && i < idx.Length; i++) t = idx[i] < t.childCount ? t.GetChild(idx[i]) : null;
        return t;
    }

    private static int ComponentIndex(Component c) =>
        Array.IndexOf(c.GetComponents<Component>().Where(x => x != null && x.GetType() == c.GetType()).ToArray(), c);

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (Transform x = t; x != null; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }

    private static string RelinkPath(int stage) => Path.Combine(OutputFolder, $"tmp_relink_stage{stage}.txt");

    private static void WriteReport(string fileName, StringBuilder sb)
    {
        Directory.CreateDirectory(OutputFolder);
        string path = Path.Combine(OutputFolder, fileName);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"LegacyTextToTMP: {Path.GetFullPath(path)}\n{sb}");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string OneLine(string s) => (s ?? "").Replace("\r", "").Replace("\n", "\\n");
    private static string Sanitize(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static int RequireStage()
    {
        int stage = Stage;
        if (!StageAssets.ContainsKey(stage)) throw new Exception("-tmpStage 1~4를 지정해야 한다.");
        return stage;
    }

    private static int RequireStageOrExit()
    {
        int stage = Stage;
        if (StageAssets.ContainsKey(stage)) return stage;
        Debug.LogError("-tmpStage 1~4를 지정해야 한다.");
        EditorApplication.Exit(1);
        return 0;
    }

    private static int ArgInt(string name, int fallback) => int.TryParse(ArgString(name, null), out int v) ? v : fallback;

    private static string ArgString(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
