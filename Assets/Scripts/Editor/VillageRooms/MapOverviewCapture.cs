using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Village Rooms > Capture Map Overview (Scene View). Play 중 VillageScene에 생성된 맵(GeneratedMap) 전체가 들어오도록 Scene 뷰를
/// 2D 위에서 보는 시점으로 맞추고, 그 Scene 뷰 카메라로 렌더해 Logs/MapPreview/scene_overview_{시각}.png로 저장한다(칸당 8px).
/// 밤 조명에서도 맵이 보이도록 캡처하는 동안만 Global 2D 라이트 밝기를 1로 올렸다가 되돌린다. 맵 바깥 마스크(검은색)는 그대로 찍힌다.
/// </summary>
public static class MapOverviewCapture
{
    private const int PixelsPerUnit = 8;
    private const int MaxTextureSize = 8192;

    [MenuItem("Tools/Village Rooms/Capture Map Overview (Scene View)")]
    private static void CaptureMenu()
    {
        string file = Capture();
        if (file != null) EditorUtility.RevealInFinder(file);
    }

    [MenuItem("Tools/Village Rooms/Capture Map Overview (Scene View)", true)]
    private static bool CaptureMenuValidate() => EditorApplication.isPlaying;

    /// <summary>저장한 PNG 경로. 생성된 맵이 없으면 null.</summary>
    public static string Capture()
    {
        GameObject mapRoot = GameObject.Find("GeneratedMap");
        if (mapRoot == null)
        {
            Debug.LogWarning("[MapOverviewCapture] GeneratedMap이 없다 - Play 중 VillageScene에서 실행해야 한다.");
            return null;
        }

        TilemapRenderer[] floors = mapRoot.GetComponentsInChildren<TilemapRenderer>()
            .Where(r => r.transform.parent != null && r.transform.parent.name != "OutsideFloorBand").ToArray();
        if (floors.Length == 0) return null;
        Bounds bounds = floors[0].bounds;
        foreach (TilemapRenderer r in floors) bounds.Encapsulate(r.bounds);
        bounds.Expand(new Vector3(4f, 4f, 0f));

        int width = Mathf.Min(MaxTextureSize, Mathf.CeilToInt(bounds.size.x * PixelsPerUnit));
        int height = Mathf.Min(MaxTextureSize, Mathf.CeilToInt(bounds.size.y * PixelsPerUnit));

        SceneView view = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView : EditorWindow.GetWindow<SceneView>();
        view.in2DMode = true;
        view.Frame(bounds, true);
        view.Repaint();

        Camera cam = view.camera;
        Vector3 oldPosition = cam.transform.position;
        Quaternion oldRotation = cam.transform.rotation;
        bool oldOrthographic = cam.orthographic;
        float oldSize = cam.orthographicSize;
        float oldAspect = cam.aspect;
        RenderTexture oldTarget = cam.targetTexture;

        Light2D[] globals = Object.FindObjectsByType<Light2D>().Where(l => l.lightType == Light2D.LightType.Global).ToArray();
        float[] intensities = globals.Select(l => l.intensity).ToArray();
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        string file = $"{GridLayoutTools.OutputFolder}/scene_overview_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        try
        {
            foreach (Light2D l in globals) l.intensity = 1f;
            cam.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, -50f), Quaternion.identity);
            cam.orthographic = true;
            cam.orthographicSize = bounds.extents.y;
            cam.aspect = (float)width / height;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previousActive;

            Directory.CreateDirectory(GridLayoutTools.OutputFolder);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Debug.Log($"[MapOverviewCapture] 저장: {Path.GetFullPath(file)} ({width}x{height})");
        }
        finally
        {
            for (int i = 0; i < globals.Length; i++) globals[i].intensity = intensities[i];
            cam.targetTexture = oldTarget;
            cam.transform.SetPositionAndRotation(oldPosition, oldRotation);
            cam.orthographic = oldOrthographic;
            cam.orthographicSize = oldSize;
            cam.aspect = oldAspect;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            view.Repaint();
        }
        return file;
    }
}
