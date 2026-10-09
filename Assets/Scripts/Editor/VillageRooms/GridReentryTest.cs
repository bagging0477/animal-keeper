using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Village Rooms > Run Grid Re-entry Test (3x4). 같은 Day에 격자 마을을 다시 들어왔을 때 상태가 이어지는지 실제 흐름대로 확인한다.
/// TruckScene에서 시작해(GameManager가 생긴다) 격자 VillageScene에 들어가고, 동물 하나를 납품한 것으로 표시한 뒤 TruckScene으로 나갔다가
/// 다시 VillageScene에 들어온다. 확인: 같은 격자(계획 서명), 같은 방 이름/종류, 남은 동물이 나갈 때 저장된 위치에 다시 나타남,
/// 납품한 동물은 다시 나오지 않음, 동물 Id 중복 없음. 씬 파일은 바꾸지 않는다(VillageMapGenerator.EditorLayoutOverride로만 Grid를 고른다).
/// 결과: Logs/GridReentryTest.txt
/// </summary>
[InitializeOnLoad]
public static class GridReentryTest
{
    private const string KeyActive = "GRT.Active", KeyDone = "GRT.Done", KeyRunInBackground = "GRT.RunInBackground";
    private const string TruckScenePath = "Assets/Scenes/TruckScene.unity";
    private const string ReportPath = "Logs/GridReentryTest.txt";
    private const float PositionTolerance = 0.25f;

    private static IEnumerator routine;

    static GridReentryTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Rooms/Run Grid Re-entry Test (3x4)")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[GridReentryTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetBool(KeyRunInBackground, PlayerSettings.runInBackground);
        EditorSceneManager.OpenScene(TruckScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            nested.Clear();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            PlayerSettings.runInBackground = SessionState.GetBool(KeyRunInBackground, false);
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
        }
    }

    // EditorApplication.update로 직접 돌리므로 "yield return 다른 IEnumerator"는 여기서 스택에 쌓아 끝까지 돌린다.
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();

    private static bool MoveNextNested()
    {
        if (nested.Count == 0) nested.Push(routine);
        while (nested.Count > 0)
        {
            IEnumerator top = nested.Peek();
            if (!top.MoveNext()) { nested.Pop(); continue; }
            if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
            return true; // 한 프레임 쉰다
        }
        return false;
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(ReportPath, "중단됨: " + e, new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            routine = null;
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private sealed class AnimalSnapshot
    {
        public string Id;
        public bool Active;
        public Vector3 Position;
    }

    private static IEnumerator Run()
    {
        StringBuilder sb = new StringBuilder();
        List<string> failures = new List<string>();
        sb.AppendLine($"GridReentryTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        for (int i = 0; i < 10; i++) yield return null;
        if (GameManager.Instance == null)
        {
            failures.Add("TruckScene에서 GameManager가 생기지 않았다");
            Finish(sb, failures);
            yield break;
        }
        sb.AppendLine($"GameManager Day(CycleCount) {GameManager.Instance.CycleCount}");
        // 새 게임이면 TruckScene이 직업 선택 씬으로 돌려보내므로(TruckSceneStartGate), 시작 직업을 고른 것으로 처리해 둔다.
        if (GameManager.Instance.NeedsStartingClassSelection) GameManager.Instance.CompleteStartingClassSelection();
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = null };

        // 1차 진입
        yield return Load("VillageScene");
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        if (gen == null || gen.LastGridPlan == null || !gen.LastGridPlan.Success)
        {
            failures.Add("1차 진입에서 격자 맵이 만들어지지 않았다");
            Finish(sb, failures);
            yield break;
        }
        string signature1 = gen.LastGridPlan.Signature();
        List<string> rooms1 = RoomList();
        List<AnimalSnapshot> animals1 = Animals();
        sb.AppendLine($"1차 진입: 계획 시드 {gen.LastGridPlan.Seed}, 방 {rooms1.Count}개, 동물 {animals1.Count}마리(활성 {animals1.Count(a => a.Active)})");

        // 동물 하나를 납품한 것으로 표시한다(실제 납품은 인벤토리→트럭 흐름이지만, 결과는 같은 Id 표시다).
        AnimalSnapshot rescued = animals1.FirstOrDefault(a => a.Active);
        if (rescued == null) { failures.Add("활성 동물이 없다"); Finish(sb, failures); yield break; }
        GameManager.Instance.MarkAnimalRescuedToday(rescued.Id);
        sb.AppendLine($"납품 표시: {rescued.Id}");

        // 트럭으로 나갔다가(나갈 때 각 동물이 OnDestroy에서 위치를 저장한다) 다시 들어온다.
        yield return Load("TruckScene");
        Dictionary<string, Vector3> saved = new Dictionary<string, Vector3>();
        foreach (AnimalSnapshot a in animals1)
            if (GameManager.Instance.TryGetAnimalFieldState(a.Id, out Vector3 pos, out bool _)) saved[a.Id] = pos;
        sb.AppendLine($"나갈 때 저장된 동물 위치: {saved.Count}개");

        yield return Load("VillageScene");
        gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        string signature2 = gen != null && gen.LastGridPlan != null ? gen.LastGridPlan.Signature() : "";
        List<string> rooms2 = RoomList();
        List<AnimalSnapshot> animals2 = Animals();
        sb.AppendLine($"2차 진입: 계획 시드 {gen?.LastGridPlan?.Seed}, 방 {rooms2.Count}개, 동물 {animals2.Count}마리(활성 {animals2.Count(a => a.Active)})");

        if (signature1 != signature2) failures.Add("같은 Day 재진입인데 격자 계획(방 배정·연결·길)이 다르다");
        if (!rooms1.SequenceEqual(rooms2)) failures.Add("방 이름/종류 목록이 다르다");
        Dictionary<string, AnimalSnapshot> byId2 = new Dictionary<string, AnimalSnapshot>();
        foreach (IGrouping<string, AnimalSnapshot> g in animals2.GroupBy(a => a.Id))
        {
            if (g.Count() > 1) failures.Add($"Id 중복: {g.Key} ({g.Count()}개)");
            byId2[g.Key] = g.First();
        }

        int restored = 0, checkedCount = 0;
        float worst = 0f;
        foreach (AnimalSnapshot a in animals1)
        {
            if (!byId2.TryGetValue(a.Id, out AnimalSnapshot b)) { failures.Add($"2차 진입에 {a.Id}가 없다"); continue; }
            if (a.Id == rescued.Id)
            {
                if (b.Active) failures.Add($"납품한 동물 {a.Id}가 다시 나타났다");
                continue;
            }
            if (!saved.TryGetValue(a.Id, out Vector3 expected)) { failures.Add($"{a.Id}의 저장 위치가 없다"); continue; }
            checkedCount++;
            float d = Vector2.Distance(expected, b.Position);
            worst = Mathf.Max(worst, d);
            if (d <= PositionTolerance) restored++;
            else failures.Add($"{a.Id}: 저장 위치 {expected}와 다시 나타난 위치 {b.Position}가 {d:F2} 떨어져 있다");
        }
        sb.AppendLine($"위치 복원: {restored}/{checkedCount} (허용 오차 {PositionTolerance}, 가장 큰 차이 {worst:F3})");
        sb.AppendLine($"납품한 동물 재등장: {(byId2.TryGetValue(rescued.Id, out AnimalSnapshot r2) && r2.Active ? "예" : "아니오")}");
        Finish(sb, failures);
    }

    private static IEnumerator Load(string sceneName)
    {
        // 다른 씬이 끼어 로드될 수 있으므로(예: 직업 선택으로 돌려보내기) 목표 씬이 로드될 때만 끝난 것으로 본다.
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded |= s.name == sceneName;
        SceneManager.sceneLoaded += onLoaded;
        SceneManager.LoadScene(sceneName);
        while (!loaded) yield return null;
        SceneManager.sceneLoaded -= onLoaded;
        // AnimalRescue.Id는 Start에서 정해지므로 한 프레임 넘긴다(위치 비교는 동물이 거의 움직이기 전에 한다).
        yield return null;
        yield return null;
    }

    private static List<string> RoomList()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        GridLayoutPlan plan = gen != null ? gen.LastGridPlan : null;
        List<string> names = new List<string>();
        if (root == null || plan == null) return names;
        foreach (Transform child in root.transform)
        {
            string[] parts = child.name.Split('_');
            if (parts.Length == 3 && parts[0] == "Room" && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
                names.Add($"{child.name}:{plan.TypeNames[plan.RoomType[x, y]]}");
        }
        names.Sort(System.StringComparer.Ordinal);
        return names;
    }

    private static List<AnimalSnapshot> Animals()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        if (root == null) return new List<AnimalSnapshot>();
        return root.GetComponentsInChildren<AnimalRescue>(true)
            .Select(a => new AnimalSnapshot { Id = a.Id, Active = a.gameObject.activeInHierarchy, Position = a.transform.position })
            .ToList();
    }

    private static void Finish(StringBuilder sb, List<string> failures)
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures.Take(30)) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[GridReentryTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }
}
