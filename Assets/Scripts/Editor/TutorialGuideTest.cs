using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Tutorial > Run Tutorial Guide Test. 1일차 튜토리얼 안내(TutorialGuide, Logs/Tutorial/notes.md 9단계)를 실제 씬 흐름으로 확인한다.
/// 검사: 단계 문구의 글자가 모두 글꼴에 있음, 1일차에 1단계부터 순서대로 뜨고 행동하면(이동·시선·줍기·도구·트럭·맡기기·소파·보호소) 다음 단계로 넘어감,
/// 안내가 떠 있는 동안 게임이 멈추지 않음(timeScale 1, 마을 시간이 흐름), 메뉴가 열려 시간이 멈추면 숨음, 2일차에는 안 나옴,
/// 다시 시작하면 1단계부터 다시 나옴, 이미 한 행동(줍기·맡기기·도구 사용)은 건너뜀.
/// 키·마우스는 가상 장치로 누른다. 몬스터는 끄거나 기절시켜 둔다. 프레임 시간 1/30초 고정.
/// 결과: Logs/TutorialGuideTest.txt / batchmode: -executeMethod TutorialGuideTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class TutorialGuideTest
{
    private const string KeyActive = "TGT.Active", KeyDone = "TGT.Done", KeyBatch = "TGT.Batch", KeyPassed = "TGT.Passed";
    private const string ReportPath = "Logs/TutorialGuideTest.txt";
    private const float FrameTime = 1f / 30f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();
    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static GameManager gm;
    private static TutorialGuide guide;
    private static int pausedWhileShowing;
    private static int framesShowing;

    static TutorialGuideTest() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

    [MenuItem("Tools/Tutorial/Run Tutorial Guide Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[TutorialGuideTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(false);
    }

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
            nested.Clear();
            logs.Clear();
            pausedWhileShowing = framesShowing = 0;
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = FrameTime;
            // 에디터는 Game 뷰에 포커스가 없으면 입력을 게임에 보내지 않는다 - 테스트 동안만 모든 입력이 게임으로 가게 한다.
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("TutorialGuideTestKeyboard");
            vmouse = InputSystem.AddDevice<Mouse>("TutorialGuideTestMouse");
            vkb.MakeCurrent();
            vmouse.MakeCurrent();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetBool(KeyPassed, false) ? 0 : 1);
        }
    }

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        VillageMapGenerator.EditorLayoutOverride = null;
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vkb = null;
        vmouse = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
        // 안내가 보이는 프레임마다 게임이 멈춰 있지 않은지 센다
        if (guide != null && guide.IsShowing)
        {
            framesShowing++;
            if (Time.timeScale == 0f || PauseMenu.IsPaused) pausedWhileShowing++;
        }
        bool more = false;
        try
        {
            if (nested.Count == 0) nested.Push(routine);
            while (nested.Count > 0)
            {
                IEnumerator top = nested.Peek();
                if (!top.MoveNext()) { nested.Pop(); continue; }
                if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
                more = true;
                break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            Finish();
            TearDown();
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static void Finish()
    {
        if (sb == null) return;
        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[TutorialGuideTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    private static string Now() => guide.IsShowing ? $"{guide.CurrentStep}단계 \"{guide.CurrentText}\"" : "(안내 없음)";

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"TutorialGuideTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);
        gm = GameManager.Instance;
        guide = TutorialGuide.Instance;
        if (gm == null || guide == null) { Check(false, $"준비 실패: GameManager {gm != null}, TutorialGuide {guide != null}"); yield break; }

        // --- 0. 글꼴
        sb.AppendLine("\n== 0. 안내 문구의 글자가 모두 글꼴에 있다");
        var texts = Enumerable.Range(1, TutorialGuide.StepCount).Select(s => TutorialGuide.StepText(s, gm)).ToList();
        texts.AddRange(new[] { TutorialGuide.DoneChipText, "가방에 쏙!", $"안내 {TutorialGuide.StepCount}/{TutorialGuide.StepCount}", "[Q]로 무기를 들고 클릭!",
                               "다 모았어요! 트럭으로 돌아가 [E]로 타요", "가방이 찼어요! 트럭으로 돌아가 [E] (필요 없으면 [G]로 내려놓기)" });
        string all = string.Concat(texts.Select(t => Regex.Replace(t, @"[\[\]]", "")));
        bool fontOk = UIFont.Font.HasCharacters(all, out uint[] missing, true, true);
        string missingText = missing == null ? "" : string.Concat(missing.Select(c => char.ConvertFromUtf32((int)c)));
        Check(fontOk, $"글자 {all.Distinct().Count()}종 중 없는 글자: [{missingText}]");

        // --- 1. 클래스 선택
        sb.AppendLine("\n== 1. 1일차 순서대로 (클래스 선택 → 트럭 → 마을 → 트럭 → 보호소)");
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        Check(guide.IsShowing && guide.CurrentStep == 1 && Time.timeScale == 1f, $"클래스 선택 씬: {Now()}");
        CheckPanel();
        RestartClass();
        yield return WaitFrames(3);
        Check(guide.IsDone(1), "시작 클래스를 고르면 1단계 끝");

        // --- 2. 트럭
        yield return LoadScene("TruckScene");
        Check(guide.IsShowing && guide.CurrentStep == 2 && guide.CurrentText.Contains($"{gm.TargetCount}마리"), $"트럭: {Now()}");

        // --- 3. 마을: 이동 + 시선
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        yield return LoadScene("VillageScene");
        List<GameObject> monsters = DisableMonsters();
        Check(guide.IsDone(2) && guide.IsShowing && guide.CurrentStep == 3, $"마을 진입: 2단계 끝, {Now()}");
        float t0 = gm.VillageTime;
        yield return WaitFrames(30);
        Check(gm.VillageTime - t0 > 0.9f && Time.timeScale == 1f && guide.IsShowing, $"안내가 떠 있는 30프레임 동안 마을 시간 {gm.VillageTime - t0:F2}초 흐름(게임이 멈추지 않음)");
        yield return Hold(Key.D, 10);
        Check(!guide.IsDone(3), $"조금 걷기만 하면 아직 3단계 ({Now()})");
        yield return Hold(Key.D, 20);
        yield return LookAt(new Vector2(0.5f, 0.95f));
        yield return LookAt(new Vector2(0.95f, 0.5f));
        yield return WaitFrames(2);
        Check(guide.IsDone(3), $"2칸 넘게 걷고 마우스로 시선을 돌리면 3단계 끝 ({Now()})");

        // --- 4. 동물 발견 → 줍기
        AnimalRescue animal = NearestAnimal();
        Check(guide.CurrentText != null && guide.transform.Find("TutorialHint/Step").GetComponentInChildren<TMP_Text>().text == TutorialGuide.DoneChipText,
            $"끝난 단계는 잠깐 \"{TutorialGuide.DoneChipText}\"로 바뀜");
        yield return Teleport(animal.transform.position + new Vector3(0.2f, 0f, 0f));
        yield return WaitRealtime(0.8f); // "완료!" 0.6초(실제 시간) + 시야 확인 0.25초
        yield return WaitFrames(2);
        Check(guide.IsShowing && guide.CurrentStep == 4, $"동물이 시야에 들어옴: {Now()}");
        for (int i = 0; i < 5 && gm.CarriedAnimalCount == 0; i++)
        {
            yield return Teleport(animal.transform.position + new Vector3(0.2f, 0f, 0f));
            yield return Press(Key.E);
        }
        yield return WaitFrames(2);
        Check(guide.IsDone(4) && guide.IsShowing && guide.CurrentText == "가방에 쏙!", $"E로 줍기: 4단계 끝, {Now()}");
        yield return WaitRealtime(2.2f);
        yield return WaitFrames(2);
        Check(!guide.IsShowing, $"\"가방에 쏙!\" 뒤 몬스터를 아직 못 봤고 가방도 여유 있음: {Now()}");

        // --- 5. 몬스터 발견 → 도구 사용
        GameObject wolf = monsters.FirstOrDefault(m => m.GetComponent<MonsterAI>() != null);
        wolf.SetActive(true);
        wolf.GetComponent<MonsterHealth>().Stun(1000f);
        Vector2 wp = wolf.GetComponent<MonsterHealth>().GamePosition;
        yield return Teleport(new Vector3(wp.x - 1.5f, wp.y, 0f));
        yield return LookAt(new Vector2(0.95f, 0.5f));
        yield return WaitRealtime(0.35f);
        yield return WaitFrames(2);
        Check(guide.IsShowing && guide.CurrentStep == 5 && guide.CurrentText.Contains("지뢰·폭탄"), $"몬스터가 시야에 들어옴(스카우트): {Now()}");
        int mine = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Mine);
        yield return Press(Key.Digit1 + mine);
        yield return Click();
        Check(guide.IsDone(5) && gm.GetSlot(mine).Type == InventoryItemType.None, $"{mine + 1}번 지뢰 칸을 고르고 클릭(설치): 5단계 끝, {Now()}");
        wolf.SetActive(false);

        // --- 6. 가방이 참
        while (gm.HasInventorySpace) gm.TryAddAnimal($"TutorialTest/{gm.CarriedAnimalCount}", 5, AnimalBehaviorKind.Flee, null);
        yield return WaitRealtime(0.7f);
        yield return WaitFrames(2);
        Check(guide.IsShowing && guide.CurrentStep == 6 && guide.CurrentText.StartsWith("가방이 찼어요"), $"가방이 참: {Now()}");

        // --- 7·8. 트럭: 모두 맡기기 → 소파
        VillageMapGenerator.EditorLayoutOverride = null;
        yield return LoadScene("TruckScene");
        Check(guide.IsDone(6) && guide.IsShowing && guide.CurrentStep == 7 && guide.CurrentText == "수의사에게 [E]로 모두 맡기기", $"동물을 든 채 트럭: 6단계 끝, {Now()}");
        AnimalDeliveryPoint clerk = Object.FindAnyObjectByType<AnimalDeliveryPoint>();
        yield return Teleport(clerk.transform.position + new Vector3(-1.2f, -0.2f, 0f));
        yield return Press(Key.E);
        // 가방(동물 칸)이 목표보다 작으면 한 번에 다 못 맡긴다 - 모자란 수는 앞서 다녀온 순찰에서 맡긴 것으로 친다.
        int bagDelivered = gm.RescuedCount;
        while (gm.RescuedCount < gm.TargetCount) gm.AddCargo(5);
        if (gm.RescuedCount > bagDelivered) sb.AppendLine($"  (참고) 가방으로 {bagDelivered}마리 맡김, 목표 {gm.TargetCount}까지 {gm.RescuedCount - bagDelivered}마리는 앞 순찰분으로 채움");
        yield return WaitRealtime(0.7f);
        yield return WaitFrames(2);
        Check(guide.IsDone(7) && gm.RescuedCount >= gm.TargetCount && guide.IsShowing && guide.CurrentStep == 8, $"E로 모두 맡김(구조 {gm.RescuedCount}/{gm.TargetCount}): {Now()}");
        TruckHubPoint hub = Object.FindAnyObjectByType<TruckHubPoint>();
        yield return Teleport(hub.transform.position + new Vector3(0f, -0.5f, 0f));
        yield return Press(Key.E);
        Check(hub.IsMenuOpen && Time.timeScale == 0f && !guide.IsShowing, $"소파 메뉴가 열려 시간이 멈춘 동안은 안내를 숨김 ({Now()})");
        ((Button)typeof(TruckHubPoint).GetField("nextDayButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hub)).onClick.Invoke();
        yield return WaitForScene("ShelterScene");

        // --- 9. 보호소
        Check(guide.IsDone(8) && guide.IsShowing && guide.CurrentStep == 9, $"보호소: 8단계 끝, {Now()}");
        Check(pausedWhileShowing == 0, $"안내가 보인 {framesShowing}프레임 중 게임이 멈춰 있던 프레임 {pausedWhileShowing}");

        // --- 2일차
        sb.AppendLine("\n== 2. 2일차에는 안 나옴");
        ShelterSettlement settlement = Object.FindAnyObjectByType<ShelterSettlement>();
        settlement.EvaluateCycleEnd();
        Time.timeScale = 1f;
        yield return WaitFrames(3);
        Check(gm.CycleCount == 2 && !guide.IsShowing, $"하루 마감 → Day {gm.CycleCount}: {Now()}");
        yield return LoadScene("TruckScene");
        Check(!guide.IsShowing && guide.CurrentStep == 0, $"2일차 트럭: {Now()}");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 2 };
        yield return LoadScene("VillageScene");
        DisableMonsters();
        yield return WaitFrames(30);
        Check(!guide.IsShowing, $"2일차 마을: {Now()}");

        // --- 다시 시작
        sb.AppendLine("\n== 3. 다시 시작하면 1단계부터 다시, 이미 한 행동은 건너뜀");
        VillageMapGenerator.EditorLayoutOverride = null;
        yield return LoadScene("TruckScene");
        gm.ResetGame(); // 게임 오버·클리어 화면의 "다시 시작"과 같은 초기화
        yield return LoadScene("ClassSelectScene");
        Check(guide.IsShowing && guide.CurrentStep == 1 && Enumerable.Range(1, TutorialGuide.StepCount).All(s => !guide.IsDone(s)), $"새 게임 클래스 선택: {Now()}, 끝난 단계 없음");
        RestartClass();
        yield return LoadScene("TruckScene");
        gm.AddCargo(4);                                                          // 이미 한 마리 맡김
        gm.TryAddAnimal("TutorialTest/held", 5, AnimalBehaviorKind.Flee, null);  // 이미 한 마리 들고 있음
        yield return WaitFrames(3);
        Check(guide.IsDone(4) && guide.IsDone(7) && guide.CurrentStep == 2, $"이미 줍고 맡긴 상태: 4·7단계는 뜨기 전에 끝, 지금 {Now()}");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        yield return LoadScene("VillageScene");
        monsters = DisableMonsters();
        // 몬스터를 보기 전에 지뢰를 먼저 써 본다
        mine = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Mine);
        yield return Press(Key.Digit1 + mine);
        yield return Click();
        Check(guide.IsDone(5) && guide.CurrentStep == 3, $"몬스터를 보기 전에 지뢰 사용: 5단계는 뜨기 전에 끝, 지금 {Now()}");
        yield return Hold(Key.D, 30);
        yield return LookAt(new Vector2(0.5f, 0.95f));
        yield return LookAt(new Vector2(0.05f, 0.5f));
        animal = NearestAnimal();
        yield return Teleport(animal.transform.position + new Vector3(0.2f, 0f, 0f));
        wolf = monsters.FirstOrDefault(m => m.GetComponent<MonsterAI>() != null);
        wolf.SetActive(true);
        wolf.GetComponent<MonsterHealth>().Stun(1000f);
        yield return WaitRealtime(0.8f);
        yield return WaitFrames(2);
        Check(guide.IsDone(3) && guide.CurrentStep != 4 && guide.CurrentStep != 5, $"동물·몬스터가 보여도 끝난 4·5단계는 안 뜸: {Now()}");
        wolf.SetActive(false);
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    // 패널이 화면 위 가운데에 있고 시안 색감의 도트 그림을 쓰는지
    private static void CheckPanel()
    {
        Transform panel = guide.transform.Find("TutorialHint");
        RectTransform rt = panel as RectTransform;
        Image img = panel != null ? panel.GetComponent<Image>() : null;
        Canvas canvas = guide.GetComponent<Canvas>();
        Check(rt != null && rt.anchorMin == new Vector2(0.5f, 1f) && rt.anchoredPosition.y < 0f && img != null && img.sprite != null &&
              img.sprite.texture.filterMode == FilterMode.Point && img.type == Image.Type.Sliced && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
            $"패널: 위 가운데 {rt?.anchoredPosition}, 크기 {rt?.rect.size}, 그림 {img?.sprite?.name}({img?.sprite?.texture.filterMode}), Canvas 정렬 {canvas.sortingOrder}");
    }

    private static List<GameObject> DisableMonsters()
    {
        List<GameObject> list = MonsterHealth.Active.Where(m => m != null).Select(m => m.gameObject).ToList();
        foreach (GameObject m in list) m.SetActive(false);
        return list;
    }

    private static AnimalRescue NearestAnimal()
    {
        Vector2 p = GameObject.FindGameObjectWithTag("Player").transform.position;
        return Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)
            .OrderBy(a => a.BehaviorKind == AnimalBehaviorKind.Wander ? 0 : 1).ThenBy(a => Vector2.Distance(a.transform.position, p)).First();
    }

    private static void RestartClass()
    {
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
    }

    private static IEnumerator Teleport(Vector3 at)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        at.z = player.transform.position.z;
        player.transform.position = at;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
        yield return WaitFrames(3);
    }

    // 화면 비율 위치(0~1)로 가상 마우스를 옮긴다 - 플레이어 시선(부채꼴 시야)이 그쪽을 본다.
    private static IEnumerator LookAt(Vector2 viewport)
    {
        Camera cam = Camera.main;
        Vector2 pos = new Vector2(viewport.x * cam.pixelWidth, viewport.y * cam.pixelHeight);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos });
        yield return WaitFrames(3);
    }

    private static IEnumerator Click()
    {
        Vector2 pos = vmouse.position.ReadValue();
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 1 });
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 0 });
        yield return WaitFrames(2);
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static IEnumerator Hold(Key key, int frames)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(frames);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(3);
    }

    private static IEnumerator LoadScene(string name)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(name);
        yield return WaitForScene(name);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 20f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitForScene(string name)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(10);
    }
}
