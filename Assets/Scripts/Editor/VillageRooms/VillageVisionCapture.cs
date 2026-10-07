using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// 시야(Light2D) 진단용 캡처. Play 모드에서 VillageScene을 띄우고, 플레이어를 맵 서쪽 가장자리 근처에 세워 벽 쪽을 바라보게 한 뒤
/// 실제 게임 카메라 크기(orthographic 5.6)로 찍는다. 같은 장면을 바깥 마스크(OutsideMapMask)를 켠 채/끈 채로 찍고,
/// 플레이어 → 벽 너머 방향으로 밝기를 0.25칸 간격으로 재서 Logs/VillageVisionCapture.txt에 남긴다.
/// batchmode: -executeMethod VillageVisionCapture.RunBatch
/// </summary>
[InitializeOnLoad]
public static class VillageVisionCapture
{
    private const string KeyActive = "VVC.Active", KeyDone = "VVC.Done";
    private static IEnumerator routine;

    static VillageVisionCapture()
    {
        EditorApplication.playModeStateChanged += change =>
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
            {
                routine = Run();
                EditorApplication.update += Step;
            }
            else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
            {
                SessionState.SetBool(KeyActive, false);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        };
    }

    [MenuItem("Tools/Village Rooms/Capture Vision Diagnostics")]
    public static void RunBatch()
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Step()
    {
        bool more;
        try { more = routine != null && EditorApplication.isPlaying && routine.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); more = false; }
        if (more) return;
        EditorApplication.update -= Step;
        routine = null;
        SessionState.SetBool(KeyDone, true);
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Run()
    {
        int frame = Time.frameCount;
        while (Time.frameCount < frame + 10) yield return null;

        StringBuilder log = new StringBuilder();
        Directory.CreateDirectory(VillageRoomBuilder.CaptureFolder);

        GameObject mapRoot = GameObject.Find("GeneratedMap");
        GameObject player = GameObject.Find("Player");
        PlayerVision vision = player != null ? player.GetComponent<PlayerVision>() : null;
        Light2D focused = vision != null ? (Light2D)typeof(PlayerVision).GetField("focusedVisionLight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(vision) : null;
        Transform mask = mapRoot != null ? mapRoot.transform.Find("OutsideMapMask") : null;
        if (mapRoot == null || player == null || focused == null) { Debug.LogError("[VisionCapture] 맵/플레이어/시야 라이트를 찾지 못했다."); yield break; }

        // 바닥 칸 모으기
        HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        foreach (Transform room in mapRoot.transform)
        {
            if (room.name == "OutsideFloorBand") continue;
            Tilemap ground = room.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            Vector2Int o = new Vector2Int(Mathf.RoundToInt(room.position.x), Mathf.RoundToInt(room.position.y));
            foreach (Vector3Int p in ground.cellBounds.allPositionsWithin) if (ground.HasTile(p)) floor.Add(new Vector2Int(p.x, p.y) + o);
        }

        // 마스크 메시 정보: 정점 알파 분포(그라데이션 데이터가 실제로 들어 있는지)
        if (mask != null)
        {
            Mesh m = mask.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer mr = mask.GetComponent<MeshRenderer>();
            Color[] cols = m.colors;
            int partial = cols.Count(c => c.a > 0.01f && c.a < 0.99f);
            log.AppendLine($"마스크 메시: 정점 {cols.Length}, 반투명 정점 {partial}, 렌더러 {(mr.enabled ? "켜짐" : "꺼짐")}, 레이어 {mr.sortingLayerName}/{mr.sortingOrder}, 머티리얼 {mr.sharedMaterial?.name} / 셰이더 {mr.sharedMaterial?.shader.name}");
        }
        else log.AppendLine("마스크(OutsideMapMask)가 없다");

        Light2D ambient = (Light2D)typeof(PlayerVision).GetField("ambientVisionLight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(vision);
        foreach (Light2D l in new[] { ambient, focused })
            log.AppendLine($"{l.name}: intensity {l.intensity}, falloff {l.falloffIntensity}, inner {l.pointLightInnerRadius} outer {l.pointLightOuterRadius}, angle {l.pointLightInnerAngle}/{l.pointLightOuterAngle}, shadows {l.shadowsEnabled} softness {l.shadowSoftness}, sortingLayers [{string.Join(",", l.targetSortingLayers.Select(id => SortingLayer.IDToName(id)))}]");

        // 장면: 서쪽 가장자리를 바라보기, 북쪽 가장자리를 바라보기
        int minX = floor.Min(c => c.x);
        int rowY = floor.Where(c => c.x == minX).Select(c => c.y).OrderBy(y => y).ElementAt(floor.Count(c => c.x == minX) / 2);
        int topY = floor.Max(c => c.y);
        int topX = floor.Where(c => c.y == topY).Select(c => c.x).OrderBy(x => x).ElementAt(floor.Count(c => c.y == topY) / 2);
        var sceneList = new List<(string name, Vector2 pos, float angle, Vector2 dir)>
        {
            (name: "west", pos: new Vector2(minX + 3.5f, rowY + 0.5f), angle: 180f, dir: Vector2.left),
            (name: "north", pos: new Vector2(topX + 0.5f, topY - 2.5f), angle: 90f, dir: Vector2.up),
        };

        // 바닥 밖으로 가장 많이 삐져나온 장애물 스프라이트를 아래쪽 바닥에서 올려다보는 장면.
        SpriteRenderer overhang = null;
        int bestOutside = 0;
        foreach (SpriteRenderer sr in mapRoot.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr.transform.parent == null || sr.transform.parent.name != "Obstacles") continue;
            Bounds b = sr.bounds;
            int outside = 0;
            for (int y = Mathf.FloorToInt(b.min.y); y <= Mathf.FloorToInt(b.max.y - 0.001f); y++)
                for (int x = Mathf.FloorToInt(b.min.x); x <= Mathf.FloorToInt(b.max.x - 0.001f); x++)
                    if (!floor.Contains(new Vector2Int(x, y))) outside++;
            if (outside > bestOutside) { bestOutside = outside; overhang = sr; }
        }
        if (overhang != null)
        {
            Vector2 c = overhang.bounds.center;
            Vector2Int start = new Vector2Int(Mathf.FloorToInt(c.x), Mathf.FloorToInt(overhang.bounds.min.y) - 3);
            for (int i = 0; i < 6 && !floor.Contains(start); i++) start.y--;
            log.AppendLine($"삐져나온 장애물: {overhang.sprite.name} @ {c} (바닥 밖 칸 {bestOutside}개)");
            sceneList.Add((name: "overhang", pos: new Vector2(start.x + 0.5f, start.y + 0.5f), angle: 90f, dir: Vector2.up));
        }
        var scenes = sceneList;

        foreach (var s in scenes)
        {
            player.transform.position = new Vector3(s.pos.x, s.pos.y, player.transform.position.z);
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = s.pos; rb.linearVelocity = Vector2.zero; }
            Physics2D.SyncTransforms();
            yield return null; yield return null;

            foreach (bool maskOn in new[] { true, false })
            {
                if (mask != null) mask.GetComponent<MeshRenderer>().enabled = maskOn;
                focused.transform.rotation = Quaternion.Euler(0f, 0f, s.angle - 90f);
                string file = $"{VillageRoomBuilder.CaptureFolder}/vision_{s.name}_{(maskOn ? "mask" : "nomask")}.png";
                Texture2D tex = Render(s.pos, 5.6f, 1280, 720, file);
                // 플레이어에서 바라보는 방향으로 0.25칸마다 밝기(0~255)
                StringBuilder profile = new StringBuilder();
                for (float d = 0f; d <= 6.01f; d += 0.25f)
                {
                    Vector2 w = s.pos + s.dir * d;
                    Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.y));
                    Color c = Sample(tex, s.pos, 5.6f, w);
                    profile.Append($"{d:F2}{(floor.Contains(cell) ? "" : "*")}:{Mathf.RoundToInt(c.grayscale * 255)} ");
                }
                log.AppendLine($"[{s.name} / 마스크 {(maskOn ? "켬" : "끔")}] 거리:밝기 (*=바닥 밖)  {profile}");
                Object.Destroy(tex);
            }
            if (mask != null) mask.GetComponent<MeshRenderer>().enabled = true;
        }

        File.WriteAllText("Logs/VillageVisionCapture.txt", log.ToString(), new UTF8Encoding(false));
        Debug.Log("[VisionCapture] 완료: Logs/VillageVisionCapture.txt");
    }

    private static Texture2D Render(Vector2 center, float orthoSize, int width, int height, string file)
    {
        Camera main = Camera.main;
        GameObject camGo = new GameObject("VisionCaptureCamera");
        Camera cam = camGo.AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.aspect = (float)width / height;
        cam.transform.position = new Vector3(center.x, center.y, -10f);
        cam.transform.rotation = Quaternion.identity;

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.Destroy(rt);
        Object.Destroy(camGo);
        return tex;
    }

    private static Color Sample(Texture2D tex, Vector2 center, float orthoSize, Vector2 world)
    {
        float unitsPerPixel = orthoSize * 2f / tex.height;
        int px = Mathf.RoundToInt(tex.width * 0.5f + (world.x - center.x) / unitsPerPixel);
        int py = Mathf.RoundToInt(tex.height * 0.5f + (world.y - center.y) / unitsPerPixel);
        if (px < 0 || py < 0 || px >= tex.width || py >= tex.height) return Color.magenta;
        return tex.GetPixel(px, py);
    }
}
