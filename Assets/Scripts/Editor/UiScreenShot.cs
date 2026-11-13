using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 테스트용: 화면 오버레이 Canvas들을 잠깐 메인 카메라 모드로 돌려 원하는 해상도(예: 1920x1080)로 화면(월드 + UI)을 그리고,
/// 같은 배치에서 UI 그래픽의 화면 영역(픽셀, 왼쪽 아래 원점)을 잰다. Dispose하면 Canvas를 원래대로 되돌린다.
/// PlayerStatusHudTest의 캡처 방식과 같다(CanvasScaler 배율을 해상도에 맞춰 직접 넣는다).
/// </summary>
public sealed class UiScreenShot : IDisposable
{
    public readonly Vector2Int Res;
    public readonly Camera Cam;
    private readonly RenderTexture rt;
    private readonly RenderTexture previousTarget;
    private readonly List<(Canvas canvas, CanvasScaler scaler, bool scalerOn, float scale, float plane, int layer, int order)> saved = new List<(Canvas, CanvasScaler, bool, float, float, int, int)>();
    public readonly List<Canvas> Canvases;
    private readonly List<(Renderer renderer, int order)> loweredRenderers = new List<(Renderer, int)>();

    public UiScreenShot(Vector2Int res)
    {
        Res = res;
        Cam = Camera.main;
        rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        previousTarget = Cam.targetTexture;
        Cam.targetTexture = rt;
        Canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToList();
        for (int i = 0; i < Canvases.Count; i++)
        {
            Canvas c = Canvases[i];
            CanvasScaler scaler = c.GetComponent<CanvasScaler>();
            saved.Add((c, scaler, scaler != null && scaler.enabled, c.scaleFactor, c.planeDistance, c.sortingLayerID, c.sortingOrder));
            float factor = scaler != null && scaler.enabled ? ScaleFactorFor(scaler, res) : 1f;
            if (scaler != null) scaler.enabled = false;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = Cam;
            c.planeDistance = Cam.nearClipPlane + 1f + (Canvases.Count - i) * 0.01f;
            c.scaleFactor = factor;
            // 카메라 모드 Canvas는 월드 스프라이트와 정렬 레이어·순서로 섞인다 - 오버레이처럼 맨 위에 그려지게 가장 위 레이어의 가장 큰 순서로 올린다
            // (같은 레이어 맨 위의 마을 노을빛 VillageNightTint보다도 위. Canvas끼리 순서는 그대로).
            c.sortingLayerID = TopSortingLayer;
            c.sortingOrder = short.MaxValue - Canvases.Count + i;
        }
        // 같은 레이어에서 그보다 위(예: 노을빛 VillageNightTint 32767)인 월드 그림은 캡처 동안만 Canvas 바로 아래로 내린다 - 실제 게임에서 오버레이 UI는 늘 맨 위다.
        int lowest = short.MaxValue - Canvases.Count;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            if (r.sortingLayerID == TopSortingLayer && r.sortingOrder >= lowest && !(r is CanvasRenderer))
            {
                loweredRenderers.Add((r, r.sortingOrder));
                r.sortingOrder = lowest - 1;
            }
        Refresh();
    }

    private static int TopSortingLayer => SortingLayer.layers.OrderBy(l => SortingLayer.GetLayerValueFromID(l.id)).Last().id;

    /// <summary>레이아웃을 다시 계산한다(배치를 바꾼 뒤 부른다).</summary>
    public void Refresh()
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude)) text.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
    }

    public static float ScaleFactorFor(CanvasScaler scaler, Vector2Int res)
    {
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return scaler.scaleFactor;
        Vector2 refRes = scaler.referenceResolution;
        float logW = Mathf.Log(res.x / refRes.x, 2f), logH = Mathf.Log(res.y / refRes.y, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(logW, logH, scaler.matchWidthOrHeight));
    }

    /// <summary>RectTransform의 화면 영역(픽셀, 왼쪽 아래 원점).</summary>
    public Rect ScreenRect(RectTransform t)
    {
        Vector3[] corners = new Vector3[4];
        t.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 c in corners)
        {
            Vector3 s = Cam.WorldToScreenPoint(c);
            min = Vector2.Min(min, s); max = Vector2.Max(max, s);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>화면에 실제로 그려지는 그래픽인지(켜져 있고, 투명하지 않고, 빈 글자가 아님).</summary>
    public static bool IsDrawn(Graphic g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy || g.canvas == null) return false;
        if (g.color.a <= 0.01f) return false;
        if (g is TMP_Text t && string.IsNullOrWhiteSpace(t.text)) return false;
        if (g is Image img && img.sprite == null && !(img.color.a > 0.01f)) return false;
        float alpha = 1f;
        foreach (CanvasGroup cg in g.GetComponentsInParent<CanvasGroup>()) alpha *= cg.alpha;
        return alpha > 0.01f;
    }

    /// <summary>화면 UI(이 캡처가 카메라 모드로 바꾼 Canvas들)의 그려지는 그래픽을 묶음 이름과 함께. exclude가 참인 그래픽은 뺀다.
    /// 묶음 이름 = "Canvas이름/Canvas 바로 아래 오브젝트 이름".</summary>
    public List<(string group, string name, Rect rect)> Graphics(Func<Graphic, bool> exclude)
    {
        var list = new List<(string, string, Rect)>();
        foreach (Graphic g in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude))
        {
            if (!IsDrawn(g) || (exclude != null && exclude(g))) continue;
            Canvas root = g.canvas.rootCanvas;
            if (!Canvases.Contains(root)) continue;
            Transform top = g.transform;
            while (top.parent != null && top.parent != root.transform) top = top.parent;
            list.Add(($"{root.name}/{top.name}", $"{g.transform.parent?.name}/{g.name}", ScreenRect(g.rectTransform)));
        }
        return list;
    }

    /// <summary>하단 자막 칸(상호작용 문구·구간 자막)의 자리 - 글자가 없어도 그 줄 전체를 예약 자리로 본다.</summary>
    public List<Rect> SubtitleSlots()
    {
        var list = new List<Rect>();
        SubtitleManager subtitle = Object.FindAnyObjectByType<SubtitleManager>();
        if (subtitle != null)
            foreach (TMP_Text t in subtitle.GetComponentsInChildren<TMP_Text>(true)) list.Add(ScreenRect(t.rectTransform));
        return list;
    }

    public Texture2D Render()
    {
        Refresh();
        Cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Res.x, Res.y, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Res.x, Res.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    public void Dispose()
    {
        foreach (var s in saved)
        {
            if (s.canvas == null) continue;
            s.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s.canvas.worldCamera = null;
            s.canvas.planeDistance = s.plane;
            s.canvas.sortingLayerID = s.layer;
            s.canvas.sortingOrder = s.order;
            if (s.scaler != null) s.scaler.enabled = s.scalerOn;
            if (s.scaler == null || !s.scalerOn) s.canvas.scaleFactor = s.scale;
        }
        foreach (var lr in loweredRenderers) if (lr.renderer != null) lr.renderer.sortingOrder = lr.order;
        if (Cam != null) Cam.targetTexture = previousTarget;
        rt.Release();
        Object.DestroyImmediate(rt);
        Canvas.ForceUpdateCanvases();
    }

    public static string Fmt(Rect r, int screenH) => $"x {r.xMin:F0}~{r.xMax:F0}, 위에서 y {screenH - r.yMax:F0}~{screenH - r.yMin:F0}";
}
