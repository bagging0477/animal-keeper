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
/// Tools > Village Rooms > Run Grid Day Cycle Test (4 days). VillageScene의 씬 설정(layoutMode) 그대로 Day를 여러 번 넘겨 본다
/// (EditorLayoutOverride를 쓰지 않으므로 기본값이 Grid인지도 함께 확인한다). TruckScene에서 시작해(GameManager가 생긴다) Day마다:
/// 마을 진입 → 동물 하나를 납품한 것으로 표시 → 트럭으로 나갔다가 다시 진입(같은 격자, 납품한 동물은 안 나옴) → GameManager.ResetDay()로 다음 Day.
/// 확인: 매 진입 격자 맵 생성 성공과 생성기 검증(경로·바닥 연결·Id 중복·방당 상한) 통과, 같은 Day 재진입은 같은 계획, Day가 바뀌면 다른 계획과
/// 납품 기록 초기화(모든 동물 활성), 테스트 중 경고/에러 로그 0. 씬 파일은 바꾸지 않는다.
/// 결과: Logs/GridDayCycleTest.txt
/// </summary>
[InitializeOnLoad]
public static class GridDayCycleTest
{
    private const string KeyActive = "GDCT.Active", KeyDone = "GDCT.Done", KeyRunInBackground = "GDCT.RunInBackground";
    private const string TruckScenePath = "Assets/Scenes/TruckScene.unity";
    private const string ReportPath = "Logs/GridDayCycleTest.txt";
    private const int Days = 4;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();

    static GridDayCycleTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Village Rooms/Run Grid Day Cycle Test (4 days)")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[GridDayCycleTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            VillageMapGenerator.EditorLayoutOverride = null;
            nested.Clear();
            logs.Clear();
            Application.logMessageReceived += OnLog;
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            PlayerSettings.runInBackground = SessionState.GetBool(KeyRunInBackground, false);
            SessionState.SetBool(KeyActive, false);
        }
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith("[GridDayCycleTest]")) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    // EditorApplication.update로 직접 돌리므로 "yield return 다른 IEnumerator"는 스택에 쌓아 끝까지 돌린다(GridReentryTest와 같은 방식).
    private static bool MoveNextNested()
    {
        if (nested.Count == 0) nested.Push(routine);
        while (nested.Count > 0)
        {
            IEnumerator top = nested.Peek();
            if (!top.MoveNext()) { nested.Pop(); continue; }
            if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
            return true;
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
            Application.logMessageReceived -= OnLog;
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static IEnumerator Run()
    {
        StringBuilder sb = new StringBuilder();
        List<string> failures = new List<string>();
        sb.AppendLine($"GridDayCycleTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - {Days}일, 씬 설정 그대로(EditorLayoutOverride 없음)");

        for (int i = 0; i < 10; i++) yield return null;
        GameManager gm = GameManager.Instance;
        if (gm == null) { failures.Add("TruckScene에서 GameManager가 생기지 않았다"); Finish(sb, failures); yield break; }
        if (gm.NeedsStartingClassSelection) gm.CompleteStartingClassSelection();

        HashSet<string> signatures = new HashSet<string>();
        for (int day = 0; day < Days; day++)
        {
            int cycle = gm.CycleCount;
            yield return Load("VillageScene");
            VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            GridLayoutPlan plan = gen != null ? gen.LastGridPlan : null;
            if (plan == null || !plan.Success || GameObject.Find("GeneratedMap")?.transform.Find("Corridors") == null)
            {
                failures.Add($"Day {cycle}: 격자 맵이 만들어지지 않았다 (씬 layoutMode가 Grid가 아니거나 계획 실패)");
                break;
            }
            string signature = plan.Signature();
            VillageMapGenerator.GridValidationResult v = gen.LastGridValidation;
            List<AnimalRescue> animals = Animals();
            int active = animals.Count(a => a.gameObject.activeInHierarchy);
            sb.AppendLine($"Day {cycle}: {plan.Columns}x{plan.Rows}, 계획 시드 {plan.Seed}, 방 {v?.Rooms}, 경로 {v?.PathsComplete}/{v?.PathsChecked}, " +
                          $"동물 {v?.Animals}(활성 {active}) 몬스터 {v?.Monsters}, Id 중복 {v?.DuplicateIds}, 방당 상한 초과 {v?.RoomsOverCap}, 트럭 방 {v?.TruckRoomPrefab}");
            if (v == null) failures.Add($"Day {cycle}: 생성기 검증 결과가 없다");
            else foreach (string p in v.Problems) failures.Add($"Day {cycle}: 검증 실패 - {p}");
            if (active != animals.Count) failures.Add($"Day {cycle}: 새 Day인데 비활성 동물 {animals.Count - active}마리 (납품 기록이 남았다)");
            if (!signatures.Add(signature)) failures.Add($"Day {cycle}: 이전 Day와 같은 격자 계획이 나왔다");

            AnimalRescue rescued = animals.FirstOrDefault(a => a.gameObject.activeInHierarchy);
            if (rescued == null) { failures.Add($"Day {cycle}: 활성 동물이 없다"); break; }
            string rescuedId = rescued.Id;
            gm.MarkAnimalRescuedToday(rescuedId);

            yield return Load("TruckScene");
            yield return Load("VillageScene");
            gen = Object.FindAnyObjectByType<VillageMapGenerator>();
            string again = gen?.LastGridPlan != null ? gen.LastGridPlan.Signature() : "";
            List<AnimalRescue> animals2 = Animals();
            bool rescuedBack = animals2.Any(a => a.Id == rescuedId && a.gameObject.activeInHierarchy);
            sb.AppendLine($"  재진입: 같은 계획 {(again == signature ? "O" : "X")}, 납품한 동물({rescuedId}) 재등장 {(rescuedBack ? "예" : "아니오")}, " +
                          $"검증 문제 {gen?.LastGridValidation?.Problems.Count}");
            if (again != signature) failures.Add($"Day {cycle}: 같은 Day 재진입인데 격자 계획이 다르다");
            if (rescuedBack) failures.Add($"Day {cycle}: 납품한 동물 {rescuedId}가 다시 나타났다");
            if (gen?.LastGridValidation != null)
                foreach (string p in gen.LastGridValidation.Problems) failures.Add($"Day {cycle} 재진입: 검증 실패 - {p}");

            // Day 마감(보호소 출구에서 FinishCycle → ResetDay와 같은 상태 변화).
            yield return Load("TruckScene");
            gm.ResetDay();
            if (gm.CycleCount != cycle + 1) failures.Add($"Day {cycle}: ResetDay 뒤 CycleCount가 {gm.CycleCount}");
        }

        sb.AppendLine($"테스트 중 경고/에러 로그: {logs.Count}건");
        foreach (string l in logs.Distinct().Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"경고/에러 로그 {logs.Count}건");
        Finish(sb, failures);
    }

    private static IEnumerator Load(string sceneName)
    {
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => loaded |= s.name == sceneName;
        SceneManager.sceneLoaded += onLoaded;
        SceneManager.LoadScene(sceneName);
        while (!loaded) yield return null;
        SceneManager.sceneLoaded -= onLoaded;
        // AnimalRescue.Id는 Start에서, 생성기 검증(LastGridValidation)은 Start에서 2프레임 뒤에 정해지므로 넉넉히 넘긴다.
        for (int i = 0; i < 5; i++) yield return null;
    }

    private static List<AnimalRescue> Animals()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        return root != null ? root.GetComponentsInChildren<AnimalRescue>(true).ToList() : new List<AnimalRescue>();
    }

    private static void Finish(StringBuilder sb, List<string> failures)
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures.Take(30)) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[GridDayCycleTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }
}
