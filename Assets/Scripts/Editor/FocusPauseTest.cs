using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > UI > Run Focus Pause Test. 게임 창이 포커스를 잃었다 돌아올 때(PauseMenu.OnApplicationFocus/OnApplicationPause) 동작을 확인한다.
/// 실제 Unity 메시지와 같은 경로로 PauseMenu 오브젝트에 OnApplicationFocus(false) → (잠시 뒤) OnApplicationFocus(true)를 보낸다.
///  - 트럭·마을 평상시: 일시정지 메뉴가 열리고(시간 0, 입력 막힘) 돌아와도 열린 채다. [계속하기]로 다시 움직인다.
///  - 튜토리얼 멈춤 화면·첫 만남 카드·도감·게임오버(트럭·마을)·승리(보호소): 그 화면은 그대로, 일시정지 메뉴는 겹쳐 열리지 않는다.
///  - 최소화(OnApplicationPause(true))도 같다. 타이틀·직업 선택·보호소 평상시에는 열지 않는다.
/// 결과: Logs/FocusPauseTest.txt / batchmode: -executeMethod FocusPauseTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class FocusPauseTest
{
    private const string KeyActive = "FPT.Active", KeyDone = "FPT.Done", KeyBatch = "FPT.Batch", KeyPassed = "FPT.Passed";
    private const string ReportPath = "Logs/FocusPauseTest.txt";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;

    static FocusPauseTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/UI/Run Focus Pause Test")]
    public static void Run() => Begin(false);

    public static void RunBatch() => Begin(true);

    private static void Begin(bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetBool(KeyPassed, false);
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity", OpenSceneMode.Single);
        EditorTestState.ClearStaleSingletons();
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !SessionState.GetBool(KeyDone, false))
        {
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 30f;
            nested.Clear();
            routine = RunTest();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetBool(KeyPassed, false) ? 0 : 1);
        }
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; Restore(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e) { Debug.LogException(e); failures?.Add("예외: " + e.Message); sb?.AppendLine("예외: " + e); more = false; }
        if (more) return;
        bool passed = failures != null && failures.Count == 0;
        sb.AppendLine();
        sb.AppendLine(passed ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        if (!passed) foreach (string f in failures) sb.AppendLine("  - " + f);
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[FocusPauseTest] {(passed ? "통과" : "실패")} - {ReportPath}");
        SessionState.SetBool(KeyPassed, passed);
        EditorApplication.update -= Step;
        Restore();
        SessionState.SetBool(KeyDone, true);
        EditorApplication.ExitPlaymode();
    }

    private static void Restore()
    {
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        VillageMapGenerator.EditorLayoutOverride = null;
    }

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

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator WaitFrames(int n) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }
    private static IEnumerator WaitScene(string name)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }

    private static GameManager GM => GameManager.Instance;
    private static GameObject PauseMenuObject => Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include)?.gameObject;
    private static bool MenuVisible
    {
        get
        {
            PauseMenu pm = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
            GameObject root = pm != null ? (GameObject)typeof(PauseMenu).GetField("menuRoot", Any).GetValue(pm) : null;
            return root != null && root.activeInHierarchy;
        }
    }

    // 창 포커스를 잃었다가(몇 프레임 동안) 돌아온다 - Unity가 보내는 메시지와 같은 이름으로 보낸다.
    private static IEnumerator LoseAndRegainFocus(bool minimize = false)
    {
        GameObject go = PauseMenuObject;
        if (minimize) go.SendMessage("OnApplicationPause", true, SendMessageOptions.DontRequireReceiver);
        else go.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
        yield return WaitFrames(5);
        if (minimize) go.SendMessage("OnApplicationPause", false, SendMessageOptions.DontRequireReceiver);
        go.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
        yield return WaitFrames(5);
    }

    private static string Now() => $"씬 {SceneManager.GetActiveScene().name}, 일시정지 {PauseMenu.IsPaused}, 메뉴 보임 {MenuVisible}, timeScale {Time.timeScale}, 입력 막힘 {GameplayInput.IsBlocked}";

    private static IEnumerator ExpectPause(string label, bool minimize = false)
    {
        Check(Time.timeScale > 0f && !PauseMenu.IsPaused, $"{label}: 포커스를 잃기 전 움직이는 중 ({Now()})");
        yield return LoseAndRegainFocus(minimize);
        Check(PauseMenu.IsPaused && MenuVisible && Time.timeScale == 0f && GameplayInput.IsBlocked, $"{label}: 돌아오면 일시정지 메뉴가 열린 채다 ({Now()})");
        Object.FindAnyObjectByType<PauseMenu>().OnResume();
        yield return WaitFrames(3);
        Check(!PauseMenu.IsPaused && Time.timeScale == 1f, $"{label}: [계속하기]로 다시 움직인다 ({Now()})");
    }

    private static IEnumerator ExpectNoPause(string label, System.Func<bool> otherScreenStillUp)
    {
        if (PauseMenu.IsPaused) { Check(false, $"{label}: 시작 전에 이미 일시정지 중"); Object.FindAnyObjectByType<PauseMenu>().OnResume(); yield return WaitFrames(2); }
        float ts = Time.timeScale;
        yield return LoseAndRegainFocus();
        Check(!PauseMenu.IsPaused && !MenuVisible && Time.timeScale == ts && (otherScreenStillUp == null || otherScreenStillUp()),
            $"{label}: 일시정지 메뉴를 겹쳐 띄우지 않고 그 화면 그대로 ({Now()})");
    }

    // 새 게임: ResetGame 뒤 몇 프레임 기다려 튜토리얼·카드·도감이 "시작 직업 선택 대기"를 보고 초기화하게 한 다음 직업을 고른다(실제 게임에서도 직업 선택 씬을 거친다).
    private static IEnumerator NewGame(PlayerClass pc)
    {
        GM.ResetGame();
        yield return WaitFrames(3);
        GM.UnlockClassFree(pc);
        GM.SelectClass(pc);
        GM.CompleteStartingClassSelection();
    }

    private static IEnumerator LoadVillage()
    {
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        float until = Time.realtimeSinceStartup + 20f;
        while (GameObject.Find("GeneratedMap") == null && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    private static void Calm()
    {
        foreach (VillageMonsterSpawner sp in Object.FindObjectsByType<VillageMonsterSpawner>(FindObjectsInactive.Exclude)) sp.enabled = false;
        foreach (MonsterHealth m in MonsterHealth.Active.ToList()) if (m != null) m.gameObject.SetActive(false);
    }

    private static IEnumerator RunTest()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine("창 포커스 복귀 테스트 (" + System.DateTime.Now + ")");
        yield return WaitFrames(10);
        // 평상시 검사는 튜토리얼 멈춤 화면·첫 만남 카드를 끈 채로 하고, 그 화면을 보는 검사에서만 켠다
        TutorialGuide.IntrosSuppressed = true;
        MonsterEncounterCard.Suppressed = true;

        sb.AppendLine("\n== 타이틀·직업 선택(열지 않음)");
        yield return ExpectNoPause("타이틀", null);
        GM.ResetGame();
        SceneManager.LoadScene("ClassSelectScene");
        yield return WaitScene("ClassSelectScene");
        yield return ExpectNoPause("직업 선택", null);

        sb.AppendLine("\n== 트럭");
        yield return NewGame(PlayerClass.Scout);
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        yield return ExpectPause("트럭 평상시");
        yield return ExpectPause("트럭 최소화", true);
        BestiaryUI best = BestiaryUI.EnsureInstance();
        best.Open();
        yield return WaitFrames(3);
        yield return ExpectNoPause("트럭 도감", () => best.IsOpen);
        best.Close();
        yield return WaitFrames(3);
        // 튜토리얼 멈춤 화면(새 게임 트럭 2단계)
        TutorialGuide.IntrosSuppressed = false;
        yield return NewGame(PlayerClass.Scout);
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        float until = Time.realtimeSinceStartup + 10f;
        while (!(TutorialGuide.Instance != null && TutorialGuide.Instance.IsIntroActive) && Time.realtimeSinceStartup < until) yield return null;
        Check(TutorialGuide.Instance != null && TutorialGuide.Instance.IsIntroActive, "트럭 튜토리얼 멈춤 화면이 떴다(2단계)");
        yield return ExpectNoPause("트럭 튜토리얼 멈춤 화면", () => TutorialGuide.Instance.IsIntroActive);
        TutorialGuide.IntrosSuppressed = true;
        typeof(TutorialGuide).GetMethod("EndIntro", Any)?.Invoke(TutorialGuide.Instance, null);
        yield return WaitFrames(3);
        if (TutorialGuide.Instance.IsIntroActive) { Time.timeScale = 1f; }
        // 목표 미달 게임오버(트럭)
        TruckHubPoint hub = Object.FindAnyObjectByType<TruckHubPoint>();
        typeof(TruckHubPoint).GetField("menuOpen", Any).SetValue(hub, true);
        typeof(TruckHubPoint).GetMethod("OnConfirmFail", Any).Invoke(hub, null);
        yield return WaitFrames(5);
        Check(GM.IsGameOver && Time.timeScale == 0f, "트럭 목표 미달 게임오버 화면");
        yield return ExpectNoPause("트럭 게임오버", () => GM.IsGameOver);

        sb.AppendLine("\n== 마을");
        yield return NewGame(PlayerClass.Trapper);
        Time.timeScale = 1f;
        yield return LoadVillage();
        Calm();
        yield return WaitFrames(5);
        yield return ExpectPause("마을 평상시");
        yield return ExpectPause("마을 최소화", true);
        // 첫 만남 카드
        MonsterEncounterCard.Suppressed = false;
        MonsterEncounterCard card = MonsterEncounterCard.EnsureInstance();
        yield return WaitFrames(3); // 카드 UI는 첫 Update에서 만든다
        GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        Sprite portrait = wolfPrefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        typeof(MonsterEncounterCard).GetMethod("Open", Any).Invoke(card, new object[] { MonsterKind.Wolf, portrait });
        yield return WaitFrames(3);
        Check(card.IsShowing && Time.timeScale == 0f, "마을 첫 만남 카드가 떠서 시간을 멈췄다");
        yield return ExpectNoPause("마을 첫 만남 카드", () => card.IsShowing);
        typeof(MonsterEncounterCard).GetMethod("Close", Any).Invoke(card, null);
        MonsterEncounterCard.Suppressed = true;
        yield return WaitFrames(3);
        // 튜토리얼 멈춤 화면(마을 3단계)
        TutorialGuide.IntrosSuppressed = false;
        yield return NewGame(PlayerClass.Trapper);
        yield return LoadVillage();
        Calm();
        until = Time.realtimeSinceStartup + 10f;
        while (!(TutorialGuide.Instance != null && TutorialGuide.Instance.IsIntroActive) && Time.realtimeSinceStartup < until) yield return null;
        Check(TutorialGuide.Instance.IsIntroActive, "마을 튜토리얼 멈춤 화면이 떴다");
        yield return ExpectNoPause("마을 튜토리얼 멈춤 화면", () => TutorialGuide.Instance.IsIntroActive);
        TutorialGuide.IntrosSuppressed = true;
        typeof(TutorialGuide).GetMethod("EndIntro", Any)?.Invoke(TutorialGuide.Instance, null);
        yield return WaitFrames(3);
        // 체력 0 게임오버(마을)
        Time.timeScale = 1f;
        GM.TakeDamage(100000, "테스트");
        yield return WaitFrames(5);
        Check(GM.IsGameOver && Time.timeScale == 0f, "마을 체력 0 게임오버 화면");
        yield return ExpectNoPause("마을 게임오버", () => GM.IsGameOver);

        sb.AppendLine("\n== 보호소(열지 않음)·승리 화면");
        Time.timeScale = 1f;
        yield return NewGame(PlayerClass.Scout);
        SceneManager.LoadScene("ShelterScene");
        yield return WaitScene("ShelterScene");
        yield return ExpectNoPause("보호소 평상시", null);
        // 마지막 날 목표 달성 → 출구 판정(승리 화면)
        for (int d = 1; d < GM.TotalCyclesToWin; d++) { for (int i = 0; i < GM.TargetCount; i++) GM.AddCargo(1); GM.FinishCycle(); }
        for (int i = 0; i < GM.TargetCount; i++) GM.AddCargo(1);
        Object.FindAnyObjectByType<ShelterSettlement>().EvaluateCycleEnd();
        yield return WaitFrames(5);
        Check(GM.IsGameWon && Time.timeScale == 0f, "승리 화면");
        yield return ExpectNoPause("승리 화면", () => GM.IsGameWon);
        Time.timeScale = 1f;
    }
}
