using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 장애물(나무·덤불)과 플레이어·동물이 겹칠 때 앞뒤가 어떻게 보이는지 찍는다. 빈 씬에 Room_Forest를 놓고, 나무/덤불 하나를 골라
/// 발 높이를 바꿔 가며 캐릭터를 세운 뒤(발 기준 정렬 FootSortOrder 적용) 위에서 렌더한다. 씬·에셋은 저장하지 않는다.
/// 같은 배치를 장애물 가림 반투명(ObstacleOccluderFade) 없이/있이 찍는다.
/// 결과: Logs/Overlap/{off|on}_{tree|bush}_{n}.png
/// batchmode: -executeMethod ObstacleOverlapCapture.RunBatch
/// </summary>
public static class ObstacleOverlapCapture
{
    private const string OutputFolder = "Logs/Overlap";
    private const string ForestPath = "Assets/Prefabs/Rooms/Room_Forest.prefab";
    private static readonly string[] AnimalPaths =
    {
        "Assets/Prefabs/Animals/AnimalFlee.prefab", "Assets/Prefabs/Animals/AnimalGoose.prefab", "Assets/Prefabs/Animals/AnimalTurtle.prefab",
    };
    private const float PlayerFootWorldOffset = -0.41f; // VillageMapGenerator.PlayerFootWorldOffset과 같다

    [MenuItem("Tools/Village Rooms/Capture Obstacle Overlap")]
    public static void Run() => Capture();

    public static void RunBatch()
    {
        int code = 0;
        try { Capture(); }
        catch (System.Exception e) { Debug.LogException(e); code = 1; }
        EditorApplication.Exit(code);
    }

    private static void Capture()
    {
        Directory.CreateDirectory(OutputFolder);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ForestPath));
        FootSortOrder.ApplyStatic(room);
        // 실제 맵처럼 바닥 타일맵을 바닥 정렬 레이어(가장 아래)로 내린다(VillageMapGenerator floorSortingLayer).
        foreach (UnityEngine.Tilemaps.TilemapRenderer t in room.GetComponentsInChildren<UnityEngine.Tilemaps.TilemapRenderer>(true))
            t.sortingLayerID = SortingLayer.layers[0].id;

        GameObject lightGo = new GameObject("Global Light");
        Light2D light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;

        Camera cam = new GameObject("Camera").AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(0, 0, -10);

        SpriteRenderer[] props = room.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.sprite != null && r.transform.parent != null && r.transform.parent.name == "Obstacles").ToArray();
        SpriteRenderer tree = props.Where(r => r.sprite.name.Contains("Tree")).OrderBy(r => r.transform.position.x + r.transform.position.y).First();
        SpriteRenderer bush = props.Where(r => r.sprite.name.Contains("Bush")).OrderBy(r => r.transform.position.x + r.transform.position.y).First();
        Debug.Log($"[ObstacleOverlapCapture] tree {tree.sprite.name} at {tree.transform.position}, bush {bush.sprite.name} at {bush.transform.position}");

        Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Player/player_base.png");
        GameObject player = new GameObject("Player");
        SpriteRenderer playerBody = player.AddComponent<SpriteRenderer>();
        playerBody.sprite = playerSprite;
        playerBody.sharedMaterial = tree.sharedMaterial;
        FootSortOrder playerSort = FootSortOrder.AttachWorldOffset(player, playerBody, PlayerFootWorldOffset, null);

        GameObject[] animals = AnimalPaths.Select(p => (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p))).ToArray();
        FootSortOrder[] animalSorts = animals.Select(a => a.GetComponentInChildren<FootSortOrder>(true)).ToArray();
        var resolve = typeof(FootSortOrder).GetMethod("Resolve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (FootSortOrder f in animalSorts) if (f != null) resolve.Invoke(f, null);

        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>("Assets/Settings/GameBalanceConfig.asset");
        ObstacleOccluderFade fade = null;
        foreach (bool fadeOn in new[] { false, true })
        {
        if (fadeOn) fade = ObstacleOccluderFade.Attach(room, playerBody, config);
        foreach ((string label, SpriteRenderer prop, float[] feet) in new[]
        {
            ("tree", tree, new[] { -0.3f, 0.3f, 1.0f, 2.0f, 3.0f }),
            ("bush", bush, new[] { -0.3f, 0.2f, 0.6f, 1.0f }),
        })
        {
            Bounds b = prop.bounds;
            cam.orthographicSize = Mathf.Max(2.2f, b.size.y * 0.75f);
            cam.transform.position = new Vector3(b.center.x, b.center.y, -10);
            for (int i = 0; i < feet.Length; i++)
            {
                float footY = b.min.y + feet[i];
                float[] xs = { b.center.x - b.extents.x * 0.6f, b.center.x - b.extents.x * 0.2f, b.center.x + b.extents.x * 0.2f, b.center.x + b.extents.x * 0.6f };
                player.transform.position = new Vector3(xs[0], footY - PlayerFootWorldOffset, 0);
                playerSort.Apply();
                for (int a = 0; a < animals.Length; a++)
                {
                    FootSortOrder f = animalSorts[a];
                    float offset = f != null ? f.FootY - animals[a].transform.position.y : 0f;
                    animals[a].transform.position = new Vector3(xs[a + 1], footY - offset, 0);
                    if (f != null) f.Apply();
                }
                if (fade != null) fade.SnapNow();
                string file = $"{OutputFolder}/{(fadeOn ? "on" : "off")}_{label}_{i}_foot{feet[i]:+0.0;-0.0}.png";
                Render(cam, file, 900, 900);
                Debug.Log($"[ObstacleOverlapCapture] {file} propOrder {prop.sortingOrder} playerOrder {playerBody.sortingOrder} animalOrders {string.Join(",", animalSorts.Select(s => s != null ? s.GetComponent<SpriteRenderer>()?.sortingOrder.ToString() ?? "?" : "-"))}");
            }
        }
        }
    }

    private static void Render(Camera cam, string file, int w, int h)
    {
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
