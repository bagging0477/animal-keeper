using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// 한 줄 배치(Linear) 회귀 확인용 덤프: 시드 1~N으로 VillageScene을 Play 중에 다시 불러와, 생성 직후(sceneLoaded - Awake는 끝났고
/// Start/Update 전이라 아무것도 움직이기 전) 방 순서·위치, 동물/몬스터/플레이어/트럭 위치, 바깥 마스크 해시, 그 뒤 Random 다음 값을
/// Logs/LinearRegressionDump_{label}.txt에 쓴다. 다른 브랜치(main 등)에서 같은 시드로 뽑은 파일과 diff해 결과가 같은지 본다.
/// 시드는 VillageMapGenerator.EditorLayoutOverride로 넣는다(씬 파일은 건드리지 않는다).
/// 메뉴: Tools > Village Rooms > Run Linear Regression Dump (seeds 1-10)
/// batchmode: -executeMethod LinearRegressionDump.RunBatch -dumpSeeds 10 -dumpLabel main  (끝나면 에디터가 종료된다)
/// </summary>
[InitializeOnLoad]
public static class LinearRegressionDump
{
    private const string KeyActive = "LRD.Active", KeySeeds = "LRD.Seeds", KeyLabel = "LRD.Label", KeyBatch = "LRD.Batch", KeyDone = "LRD.Done";
    private const string LogPrefix = "[LinearDump] ";

    private static IEnumerator routine;

    static LinearRegressionDump()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Rooms/Run Linear Regression Dump (seeds 1-10)")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning(LogPrefix + "Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(10, "branch", false);
    }

    public static void RunBatch()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int seeds = 10;
        string label = "batch";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-dumpSeeds") int.TryParse(args[i + 1], out seeds);
            if (args[i] == "-dumpLabel") label = args[i + 1];
        }
        Begin(seeds, label, true);
    }

    private static void Begin(int seeds, string label, bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetInt(KeySeeds, seeds);
        SessionState.SetString(KeyLabel, label);
        SessionState.SetBool(KeyBatch, batch);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            routine = Run(SessionState.GetInt(KeySeeds, 10), SessionState.GetString(KeyLabel, "branch"));
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(0);
        }
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; return; }
        bool more;
        try { more = routine.MoveNext(); }
        catch (System.Exception e) { Debug.LogException(e); more = false; }
        if (!more)
        {
            EditorApplication.update -= Step;
            routine = null;
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static IEnumerator Run(int seeds, string label)
    {
        StringBuilder sb = new StringBuilder();
        for (int seed = 1; seed <= seeds; seed++)
        {
            VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride
            {
                Mode = MapLayoutMode.Linear, Columns = 3, Rows = 4, Seed = seed,
            };
            bool loaded = false;
            int currentSeed = seed;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) =>
            {
                loaded = true;
                Dump(sb, currentSeed);
            };
            SceneManager.sceneLoaded += onLoaded;
            SceneManager.LoadScene(Path.GetFileNameWithoutExtension(VillageRoomBuilder.ScenePath));
            while (!loaded) yield return null;
            SceneManager.sceneLoaded -= onLoaded;
            yield return null;
        }

        Directory.CreateDirectory("Logs");
        string path = $"Logs/LinearRegressionDump_{label}.txt";
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log(LogPrefix + "덤프: " + Path.GetFullPath(path));
    }

    private static string V(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    private static string PathOf(Transform t)
    {
        StringBuilder path = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) path.Insert(0, "/").Insert(0, p.name);
        return path.ToString();
    }

    private static void Dump(StringBuilder sb, int seed)
    {
        sb.AppendLine($"== 시드 {seed}");
        GameObject mapRoot = GameObject.Find("GeneratedMap");
        if (mapRoot == null) { sb.AppendLine("  GeneratedMap 없음"); return; }

        // 방 순서·위치(계층 순서 = 생성 순서) 및 맵 루트 직속 자식 이름(경계/그림자/마스크 등 구성).
        foreach (Transform child in mapRoot.transform)
            sb.AppendLine(child.Find("Ground") != null ? $"  방 {child.name} @ {V(child.position)}" : $"  구성 {child.name}");

        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Include).OrderBy(a => PathOf(a.transform), System.StringComparer.Ordinal))
            sb.AppendLine($"  동물 {PathOf(a.transform)} @ {V(a.transform.position)}");
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Include).OrderBy(m => PathOf(m.transform), System.StringComparer.Ordinal))
            sb.AppendLine($"  몬스터 {PathOf(m.transform)} @ {V(m.transform.position)}");

        GameObject player = GameObject.Find("Player");
        sb.AppendLine($"  플레이어 @ {(player != null ? V(player.transform.position) : "없음")}");
        GameObject truck = GameObject.Find("TruckPoint");
        sb.AppendLine($"  트럭 @ {(truck != null ? V(truck.transform.position) : "없음")}");

        Transform mask = mapRoot.transform.Find("OutsideMapMask");
        MeshFilter maskFilter = mask != null ? mask.GetComponent<MeshFilter>() : null;
        sb.AppendLine($"  바깥 마스크 해시 {(maskFilter != null && maskFilter.sharedMesh != null ? MeshHash(maskFilter.sharedMesh) : "-")}");
        // 생성이 Random을 같은 횟수만큼 썼는지: 생성 직후 Random 다음 값(상태를 소비하지 않도록 저장했다 되돌린다).
        Random.State state = Random.state;
        sb.AppendLine($"  생성 후 Random 다음 값 {Random.value:F6}");
        Random.state = state;
    }

    // VillageMapGenerationTest.MeshHash와 같은 규칙.
    private static string MeshHash(Mesh mesh)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) { h = (h ^ v) * 1099511628211L; }
            foreach (Vector3 v in mesh.vertices) { Mix(Mathf.RoundToInt(v.x * 1000)); Mix(Mathf.RoundToInt(v.y * 1000)); }
            foreach (Color c in mesh.colors) Mix(Mathf.RoundToInt(c.a * 1000) * 7 + Mathf.RoundToInt(c.r * 1000));
            foreach (int i in mesh.triangles) Mix(i);
            return h.ToString("x16");
        }
    }
}
