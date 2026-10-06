using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Fonts > Capture Text Preview. 글씨 선명도 확인용으로 UI를 실제 픽셀 해상도(1920x1080, 1280x720)로 렌더링해
/// Logs/UIFontPreview/에 PNG로 남긴다. Game 창의 표시 배율(Scale)과 무관하게 실제 화면에 찍히는 픽셀을 그대로 볼 수 있다.
///
/// - sheet: 자막/인벤토리 숫자/상점/ESC 메뉴/HUD에 쓰는 크기와 색으로 만든 글씨 견본
/// - 각 씬과 UI 프리팹: 저장된 상태 그대로의 Canvas(실행 중에 켜지는 패널은 꺼진 채로 찍힌다)
/// 씬/프리팹은 바꾸거나 저장하지 않는다(Overlay Canvas를 잠깐 카메라 모드로 돌려 찍고 되돌린다).
/// </summary>
public static class UIFontPreviewCapture
{
    private const string OutputFolder = "Logs/UIFontPreview";
    private static readonly Vector2Int[] Resolutions = { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };
    private static readonly string[] PrefabPaths = { "Assets/Resources/PauseMenu.prefab", "Assets/Prefabs/UI/GameOverPanel.prefab" };

    [MenuItem("Tools/Fonts/Capture Text Preview")]
    public static void CaptureFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previousScene = SceneManager.GetActiveScene().path;
        string folder = CaptureAll("editor");
        if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
        EditorUtility.RevealInFinder(folder);
    }

    /// <summary>배치 모드용(-executeMethod UIFontPreviewCapture.BatchCapture -previewTag before).</summary>
    public static void BatchCapture()
    {
        int exitCode = 0;
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-previewTag");
            CaptureAll(i >= 0 && i + 1 < args.Length ? args[i + 1] : "batch");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            exitCode = 1;
        }
        EditorApplication.Exit(exitCode);
    }

    public static string CaptureAll(string tag)
    {
        string folder = Path.Combine(OutputFolder, tag);
        Directory.CreateDirectory(folder);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Canvas sheet = BuildSheet();
        foreach (Vector2Int res in Resolutions) Render(Path.Combine(folder, $"sheet_{res.x}x{res.y}.png"), new[] { sheet }, res);

        foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Canvas[] canvases = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Canvas>(false))
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            if (canvases.Length == 0) continue;
            foreach (Vector2Int res in Resolutions) Render(Path.Combine(folder, $"{scene.name}_{res.x}x{res.y}.png"), canvases, res);
        }

        foreach (string path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Canvas host = CreateCanvas("PrefabHost");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // 실행 중에 켜지는 패널도 보이도록 프리팹 안의 모든 오브젝트를 켠다(저장하지 않는 임시 인스턴스).
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            if (instance.GetComponent<Canvas>() == null) instance.transform.SetParent(host.transform, false);
            Canvas[] canvases = new[] { host }.Concat(instance.GetComponentsInChildren<Canvas>(true).Where(c => c.isRootCanvas)).ToArray();
            foreach (Vector2Int res in Resolutions)
            {
                Render(Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}_{res.x}x{res.y}.png"), canvases, res);
            }
        }

        // 견본에 고정 폰트에 없는 글자가 있었다면 Fallback이 채웠을 수 있다 - 커밋된 상태로 되돌린다.
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.FallbackAssetPath);
        if (fallback != null && fallback.characterTable.Count > 0)
        {
            Debug.LogWarning($"UIFontPreviewCapture: Fallback으로 그린 글자 {fallback.characterTable.Count}개");
            fallback.ClearFontAssetData(true);
            EditorUtility.SetDirty(fallback);
            AssetDatabase.SaveAssetIfDirty(fallback);
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Debug.Log($"UIFontPreviewCapture: {Path.GetFullPath(folder)}");
        return folder;
    }

    // ---------- 견본 ----------

    private static Canvas BuildSheet()
    {
        Canvas canvas = CreateCanvas("FontSheet");
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false);
        var bgRt = (RectTransform)bg.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.22f, 1f);

        var lightPanel = new GameObject("LightPanel", typeof(RectTransform), typeof(Image));
        lightPanel.transform.SetParent(canvas.transform, false);
        var lpRt = (RectTransform)lightPanel.transform;
        lpRt.anchorMin = lpRt.anchorMax = lpRt.pivot = new Vector2(1f, 1f);
        lpRt.anchoredPosition = new Vector2(-40f, -40f);
        lpRt.sizeDelta = new Vector2(560f, 300f);
        lightPanel.GetComponent<Image>().color = new Color(0.96f, 0.9f, 0.78f, 1f);

        Color white = Color.white;
        Color sub = new Color(0.82f, 0.82f, 0.8f, 1f);
        Color price = new Color(1f, 0.85f, 0.3f, 1f);
        Color dark = new Color(0.36f, 0.2f, 0.14f, 1f);

        float y = -40f;
        void Line(string text, float size, Color color)
        {
            AddText(canvas.transform, text, size, color, new Vector2(60f, y), new Vector2(1200f, size * 1.6f), TextAlignmentOptions.TopLeft);
            y -= size * 1.55f + 6f;
        }

        Line("[자막 34] E를 눌러 구조하세요", 34f, white);
        Line("[자막 34] 보유 마일리지가 부족합니다 (120 필요)", 34f, white);
        Line("[상점 제목 44] 잡화 상점", 44f, white);
        Line("[상점 이름 32] 지뢰    폭탄    마취총 탄약", 32f, white);
        Line("[상점 가격 28] 120 마일리지   보유 마일리지  1234", 28f, price);
        Line("[상점 설명 20] 밟으면 데미지 3 + 1.5초 스턴 (1회용) · E / ESC  닫기", 20f, sub);
        Line("[ESC 56] PAUSE  일시정지", 56f, white);
        Line("[HUD 27] 보유 마일리지: 0   적재량 3마리 · 42kg   목표 진행 1/3", 27f, white);
        Line("[HUD 22] 100%   Day 1 / 3", 22f, white);
        Line("[인벤토리 24] 1  2  3  4  5      Q 마취총 3/3", 24f, white);
        Line("[18] 다람쥐 헌 쳇바퀴에 타고파 ABCDEFG abcdefg 0123456789 !?%()[]", 18f, white);
        Line("[14] 다람쥐 헌 쳇바퀴에 타고파 ABCDEFG abcdefg 0123456789 !?%()[]", 14f, white);

        AddText(lightPanel.transform, "다시 시작", 51f, dark, new Vector2(30f, -30f), new Vector2(500f, 80f), TextAlignmentOptions.TopLeft);
        AddText(lightPanel.transform, "1  2  3  4  5", 24f, dark, new Vector2(30f, -130f), new Vector2(500f, 40f), TextAlignmentOptions.TopLeft);
        AddText(lightPanel.transform, "목표를 달성하지 못해 보호소\n운영이 어려워졌습니다", 30f, dark, new Vector2(30f, -180f), new Vector2(500f, 100f), TextAlignmentOptions.TopLeft);
        return canvas;
    }

    private static Canvas CreateCanvas(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.layer = 5;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void AddText(Transform parent, string content, float size, Color color, Vector2 topLeft, Vector2 boxSize,
        TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = topLeft;
        rt.sizeDelta = boxSize;
        var text = go.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(text);
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = content;
    }

    // ---------- 렌더링 ----------

    private static void Render(string file, IList<Canvas> canvases, Vector2Int res)
    {
        var camGo = new GameObject("PreviewCamera");
        camGo.transform.position = new Vector3(100000f, 100000f, -10f); // 씬의 월드 오브젝트가 찍히지 않게 멀리 둔다
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        var rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;

        var saved = new List<(Canvas canvas, RenderMode mode, Camera cam, float plane, float scale, CanvasScaler scaler, bool scalerOn)>();
        foreach (Canvas canvas in canvases)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            saved.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance, canvas.scaleFactor, scaler, scaler != null && scaler.enabled));
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f + saved.Count * 0.01f;
            if (scaler != null && scaler.enabled)
            {
                canvas.scaleFactor = ScaleFactorFor(scaler, res);
                scaler.enabled = false;
            }
        }

        Canvas.ForceUpdateCanvases();
        foreach (Canvas canvas in canvases)
        {
            foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate(true, true);
        }
        Canvas.ForceUpdateCanvases();
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(res.x, res.y, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, res.x, res.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());

        foreach (var s in saved)
        {
            s.canvas.renderMode = s.mode;
            s.canvas.worldCamera = s.cam;
            s.canvas.planeDistance = s.plane;
            if (s.scaler != null) s.scaler.enabled = s.scalerOn;
            s.canvas.scaleFactor = s.scale;
        }
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camGo);
    }

    // CanvasScaler(Scale With Screen Size)가 해당 해상도에서 계산할 배율과 같은 값.
    private static float ScaleFactorFor(CanvasScaler scaler, Vector2Int res)
    {
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;

        Vector2 reference = scaler.referenceResolution;
        switch (scaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand:
                return Mathf.Min(res.x / reference.x, res.y / reference.y);
            case CanvasScaler.ScreenMatchMode.Shrink:
                return Mathf.Max(res.x / reference.x, res.y / reference.y);
            default:
                float logWidth = Mathf.Log(res.x / reference.x, 2f);
                float logHeight = Mathf.Log(res.y / reference.y, 2f);
                return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
        }
    }
}
