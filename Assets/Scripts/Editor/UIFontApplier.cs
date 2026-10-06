using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Fonts > Apply UI Font To Scenes And Prefabs. 모든 씬(Assets/Scenes)과 프리팹의 TMP 글씨를 공용 폰트/머티리얼
/// (UIFont SDF와 그 기본 머티리얼)로 맞추고, 글씨 선명도에 영향을 주는 설정을 점검해 Logs/UIFontAudit.txt에 남긴다.
///
/// 고치는 것: 다른 폰트/머티리얼을 쓰는 TMP 글씨, Scale이 1이 아닌 글씨(fontSize와 크기로 옮긴다),
///            Auto Size의 최소/최대가 비어 있거나 너무 넓은 글씨.
/// 보고만 하는 것: 남아 있는 Legacy UI Text, Canvas Render Mode/Canvas Scaler, 소수점 위치/크기의 글씨.
/// </summary>
public static class UIFontApplier
{
    private const string AuditPath = "Logs/UIFontAudit.txt";
    private const float AutoSizeMinRatio = 0.7f;

    [MenuItem("Tools/Fonts/Apply UI Font To Scenes And Prefabs")]
    public static void ApplyFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previousScene = SceneManager.GetActiveScene().path;
        bool ok = ApplyAll();
        if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
        EditorUtility.DisplayDialog("Apply UI Font", ok ? $"완료. 점검 결과: {AuditPath}" : "실패. Console을 확인하자.", "OK");
    }

    public static bool ApplyAll()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.MainAssetPath);
        if (font == null || font.material == null)
        {
            Debug.LogError($"UIFontApplier: {UIFontBuilder.MainAssetPath}이 없다 - 먼저 Tools > Fonts > Rebuild Fonts를 실행하자.");
            return false;
        }

        var audit = new StringBuilder();
        int changedTotal = 0;

        foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            int changed = Process(path, roots, font, audit);
            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            changedTotal += changed;
        }

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                     .Select(AssetDatabase.GUIDToAssetPath)
                     .Where(p => !p.StartsWith("Assets/TextMesh Pro/"))
                     .OrderBy(p => p))
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = Process(path, new[] { root }, font, audit);
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                changedTotal += changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        audit.Insert(0, $"changed components: {changedTotal}\n\n");
        Directory.CreateDirectory(Path.GetDirectoryName(AuditPath));
        File.WriteAllText(AuditPath, audit.ToString());
        Debug.Log($"UIFontApplier: TMP 글씨 {changedTotal}곳을 고쳤다. 점검 결과: {AuditPath}");
        return true;
    }

    private static int Process(string assetPath, IEnumerable<GameObject> roots, TMP_FontAsset font, StringBuilder audit)
    {
        int changed = 0;
        var lines = new List<string>();

        foreach (GameObject root in roots)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string where = HierarchyPath(text.transform);
                int changedBefore = changed;

                if (text.font != font || text.fontSharedMaterial != font.material)
                {
                    text.font = font;
                    text.fontSharedMaterial = font.material;
                    changed++;
                }

                Vector3 scale = text.transform.localScale;
                if (scale != Vector3.one)
                {
                    bool uniform = Mathf.Approximately(scale.x, scale.y) && text.transform.childCount == 0 &&
                                   text.transform is RectTransform;
                    if (uniform)
                    {
                        var rt = (RectTransform)text.transform;
                        float s = scale.x;
                        text.fontSize = Mathf.Round(text.fontSize * s * 10f) / 10f;
                        if (text.enableAutoSizing)
                        {
                            text.fontSizeMin *= s;
                            text.fontSizeMax *= s;
                        }
                        rt.sizeDelta *= s;
                        rt.localScale = Vector3.one;
                        lines.Add($"  scale {scale} -> 1, fontSize {text.fontSize}: {where}");
                        changed++;
                    }
                    else
                    {
                        lines.Add($"  [확인 필요] scale {scale} (균일하지 않거나 자식이 있어 그대로 둠): {where}");
                    }
                }

                if (text.enableAutoSizing)
                {
                    float max = text.fontSizeMax > 0f ? text.fontSizeMax : text.fontSize;
                    float min = Mathf.Max(text.fontSizeMin, Mathf.Round(max * AutoSizeMinRatio));
                    if (!Mathf.Approximately(min, text.fontSizeMin) || !Mathf.Approximately(max, text.fontSizeMax))
                    {
                        text.fontSizeMin = min;
                        text.fontSizeMax = max;
                        lines.Add($"  auto size {min}~{max}: {where}");
                        changed++;
                    }
                }

                if (text.transform is RectTransform r && (HasFraction(r.anchoredPosition) || HasFraction(r.sizeDelta)))
                {
                    lines.Add($"  [소수점] pos {r.anchoredPosition} size {r.sizeDelta}: {where}");
                }

                if (changed > changedBefore)
                {
                    EditorUtility.SetDirty(text);
                    EditorUtility.SetDirty(text.transform);
                }
            }

            foreach (Text legacy in root.GetComponentsInChildren<Text>(true))
            {
                lines.Add($"  [Legacy Text 유지] \"{legacy.text}\": {HierarchyPath(legacy.transform)}");
            }

            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas) continue;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                string scalerInfo = scaler == null
                    ? "Canvas Scaler 없음"
                    : $"{scaler.uiScaleMode}, ref {scaler.referenceResolution.x}x{scaler.referenceResolution.y}, match {scaler.matchWidthOrHeight}";
                lines.Add($"  [Canvas] {canvas.renderMode}, {scalerInfo}: {HierarchyPath(canvas.transform)}");
            }
        }

        audit.AppendLine($"{assetPath} (changed {changed})");
        foreach (string line in lines) audit.AppendLine(line);
        return changed;
    }

    private static bool HasFraction(Vector2 v) =>
        Mathf.Abs(v.x - Mathf.Round(v.x)) > 0.01f || Mathf.Abs(v.y - Mathf.Round(v.y)) > 0.01f;

    private static string HierarchyPath(Transform t)
    {
        var parts = new List<string>();
        for (Transform cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
