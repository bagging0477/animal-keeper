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
/// Tools > Tutorial > Run Tutorial Guide Test. 1일차 튜토리얼(TutorialGuide, 9단계 - 멈춤 화면 + 대상 모양 노란 외곽선 + 작은 알림)을 실제 씬 흐름으로 확인한다.
/// 검사: 문구·이름표 글자가 모두 글꼴에 있음 / 단계가 시작되면 게임과 마을 시간(해질녘)이 멈추고 화면이 어두워지며 가운데 카드와 "[Space] 또는 클릭으로 시작"이 뜸 /
/// 그 단계의 대상(월드·UI)마다 지금 그림 모양을 딴 도트 1칸 노란 외곽선(네모 테두리 없음, 실제 그림으로 모양에 붙은 띠인지 판정), 대상은 어둠 없이 밝음,
/// 화면 밖 대상은 그쪽을 가리키는 가장자리 화살표, 그림 프레임·뒤집기가 바뀌면 외곽선도 따라감 / Space·클릭으로 다시 움직이고 그 키·클릭은 게임에 새지 않으며 외곽선은 사라짐 /
/// 다시 움직이면 작은 알림만 남고 다른 화면 UI·하단 자막 칸과 겹치지 않음(1920x1080·1440x1080·1280x1024·2560x1080, 석상 정보 팝업 앞 포함) /
/// 일시정지 메뉴·소파 메뉴·소파 확인 창이 열리면 숨음 / 행동하면 다음 단계로 / 2일차에는 안 나옴 / 다시 시작하면 1단계부터 / 이미 한 행동은 건너뜀(멈추지도 않음).
/// 그림: Logs/Tutorial/applied_v3/tutorial_v3.png (9단계 멈춤 화면 - 1920x1080을 반으로 줄여 4칸씩, 마지막 줄은 외곽선 부분 x4 확대).
/// 키·마우스는 가상 장치로 누른다. 몬스터는 끄거나 기절시켜 둔다. 프레임 시간 1/30초 고정.
/// 결과: Logs/TutorialGuideTest.txt / batchmode: -executeMethod TutorialGuideTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class TutorialGuideTest
{
    private const string KeyActive = "TGT.Active", KeyDone = "TGT.Done", KeyBatch = "TGT.Batch", KeyPassed = "TGT.Passed";
    private const string ReportPath = "Logs/TutorialGuideTest.txt";
    private const string SheetPath = "Logs/Tutorial/applied_v3/tutorial_v3.png";
    private const float FrameTime = 1f / 30f;
    private const int TileW = 960, TileH = 540, SheetCols = 4;
    private static readonly Vector2Int ShotRes = new Vector2Int(1920, 1080);
    private static readonly Vector2Int[] NoticeResolutions = { new Vector2Int(1920, 1080), new Vector2Int(1440, 1080), new Vector2Int(1280, 1024), new Vector2Int(2560, 1080) };

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static int checks;
    private static readonly List<string> logs = new List<string>();
    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static GameManager gm;
    private static TutorialGuide guide;
    private static readonly List<(string label, Texture2D tex)> tiles = new List<(string, Texture2D)>();

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
            tiles.Clear();
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
        foreach (var t in tiles) if (t.tex != null) Object.DestroyImmediate(t.tex);
        tiles.Clear();
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
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
            try { SaveSheet(); } catch (System.Exception e) { Debug.LogException(e); failures?.Add("그림 저장 실패: " + e.Message); }
            Finish();
            TearDown();
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static void Finish()
    {
        if (sb == null) return;
        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        checks++;
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? $"결과: 통과 ({checks}/{checks})" : $"결과: 실패 {failures.Count}건 (통과 {checks - failures.Count}/{checks})");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[TutorialGuideTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    private static string Now() =>
        guide.IsIntroShowing ? $"멈춤 {guide.CurrentStep}단계 \"{guide.CurrentText}\"" :
        guide.IsNoticeShowing ? $"알림 {guide.CurrentStep}단계 \"{guide.CurrentText}\"" : "(안내 없음)";

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        checks = 0;
        sb.AppendLine($"TutorialGuideTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);
        gm = GameManager.Instance;
        guide = TutorialGuide.Instance;
        if (gm == null || guide == null) { Check(false, $"준비 실패: GameManager {gm != null}, TutorialGuide {guide != null}"); yield break; }

        // --- 0. 글꼴
        sb.AppendLine("\n== 0. 안내 문구·이름표의 글자가 모두 글꼴에 있다");
        var texts = Enumerable.Range(1, TutorialGuide.StepCount).Select(s => TutorialGuide.StepText(s, gm)).ToList();
        texts.AddRange(new[] { TutorialGuide.DoneChipText, TutorialGuide.StartPromptText, "가방에 쏙!", $"안내 {TutorialGuide.StepCount}/{TutorialGuide.StepCount}", "[Q]로 무기를 들고 클릭!",
                               "다 모았어요! 트럭으로 돌아가 [E]로 타요", "가방이 찼어요! 트럭으로 돌아가 [E] (필요 없으면 [G]로 내려놓기)" });
        texts.AddRange(TutorialGuide.HighlightLabelTexts);
        string all = string.Concat(texts.Select(t => Regex.Replace(t, @"[\[\]]", "")));
        bool fontOk = UIFont.Font.HasCharacters(all, out uint[] missing, true, true);
        string missingText = missing == null ? "" : string.Concat(missing.Select(c => char.ConvertFromUtf32((int)c)));
        Check(fontOk, $"글자 {all.Distinct().Count()}종 중 없는 글자: [{missingText}]");

        // --- 1. 클래스 선택
        sb.AppendLine("\n== 1. 1일차 순서대로 (클래스 선택 → 트럭 → 마을 → 트럭 → 보호소)");
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        yield return ExpectIntro(1, "클래스 선택 씬");
        ClassInteractPoint[] statues = Object.FindObjectsByType<ClassInteractPoint>(FindObjectsInactive.Exclude);
        CheckHighlights("1단계", statues.Select(s => (string)null).ToArray(), statues.Select(s => ((GameObject)s.gameObject, (RectTransform)null)).ToArray(), "1단계 멈춤: 석상",
            null, false, ("석상", 1, new Vector2(0.5f, 0.82f)));
        CheckCardStyle();
        yield return Dismiss(true, "1단계");
        yield return CheckNotice("1단계 시작 지점", true, "1단계 알림");
        foreach (ClassInteractPoint cp in statues)
        {
            yield return Teleport(cp.transform.position + new Vector3(0f, -0.8f, 0f));
            yield return WaitRealtime(0.4f); // 알림 자리는 0.25초마다 다시 본다
            yield return WaitFrames(2);
            yield return CheckNotice($"1단계 {cp.name} 앞(직업 정보 팝업)", false, null);
        }
        RestartClass();
        yield return WaitFrames(3);
        Check(guide.IsDone(1), "시작 클래스를 고르면 1단계 끝");

        // --- 2. 트럭
        yield return LoadScene("TruckScene");
        yield return ExpectIntro(2, "트럭");
        Check(guide.CurrentText.Contains($"{gm.TargetCount}마리"), $"2단계 문구에 목표 {gm.TargetCount}마리");
        CheckHighlights("2단계", new[] { "문", "오늘 목표" },
            new[] { ((GameObject)Object.FindAnyObjectByType<TruckStartPoint>().gameObject, (RectTransform)null), ((GameObject)null, (RectTransform)GameObject.Find("TopInfoPanel").transform) }, "2단계 멈춤: 문(월드)·오늘 목표(UI)");
        yield return Dismiss(false, "2단계");
        yield return CheckNotice("2단계 트럭", false, null);

        // --- 3. 마을: 멈춤 동안 마을 시간·해질녘이 멈춤
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        yield return LoadScene("VillageScene");
        List<GameObject> monsters = DisableMonsters();
        Check(guide.IsDone(2), "마을 진입: 2단계 끝");
        yield return ExpectIntro(3, "마을 진입");
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        CheckHighlights("3단계", new[] { "나" }, new[] { (player, (RectTransform)null) }, "3단계 멈춤: 플레이어");
        float t0 = gm.VillageTime, dusk0 = PlayerVision.Current.AppliedDuskProgress, r0 = AmbientRadius();
        Vector3 pos0 = player.transform.position;
        yield return Hold(Key.D, 30); // 멈춘 동안 걷기 키를 눌러도
        Check(Time.timeScale == 0f && guide.IsIntroActive && gm.VillageTime == t0 && PlayerVision.Current.AppliedDuskProgress == dusk0 && AmbientRadius() == r0 &&
              Vector3.Distance(player.transform.position, pos0) < 0.001f,
            $"멈춘 33프레임 동안: timeScale {Time.timeScale}, 마을 시간 {t0:F3} → {gm.VillageTime:F3}, 해질녘 진행도 {dusk0:F4} → {PlayerVision.Current.AppliedDuskProgress:F4}, 시야 반경 {r0:F2} → {AmbientRadius():F2}, 플레이어 이동 {Vector3.Distance(player.transform.position, pos0):F3}");
        yield return Dismiss(true, "3단계");
        float t1 = gm.VillageTime;
        yield return WaitFrames(30);
        Check(gm.VillageTime - t1 > 0.9f && Time.timeScale == 1f && guide.IsNoticeShowing && !guide.IsIntroShowing,
            $"다시 움직인 30프레임: 마을 시간 {gm.VillageTime - t1:F2}초 흐름, 작은 알림만 보임 ({Now()})");
        yield return CheckNotice("3단계 마을", true, "3단계 알림");
        // 일시정지 메뉴가 열리면 숨는다
        yield return Press(Key.Escape);
        Check(PauseMenu.IsPaused && !guide.IsShowing, $"ESC 일시정지 메뉴가 열리면 안내를 숨김 ({Now()})");
        yield return Press(Key.Escape);
        yield return WaitFrames(2);
        Check(!PauseMenu.IsPaused && guide.IsNoticeShowing, $"메뉴를 닫으면 알림이 다시 보임 ({Now()})");
        yield return Hold(Key.D, 10);
        Check(!guide.IsDone(3), $"조금 걷기만 하면 아직 3단계 ({Now()})");
        yield return Hold(Key.D, 20);
        yield return LookAt(new Vector2(0.5f, 0.95f));
        yield return LookAt(new Vector2(0.95f, 0.5f));
        yield return WaitFrames(2);
        Check(guide.IsDone(3), $"2칸 넘게 걷고 마우스로 시선을 돌리면 3단계 끝 ({Now()})");
        Check(guide.IsNoticeShowing && guide.transform.Find("TutorialNotice/Step").GetComponentInChildren<TMP_Text>().text == TutorialGuide.DoneChipText,
            $"끝난 단계는 알림이 잠깐 \"{TutorialGuide.DoneChipText}\"로 바뀜");

        // --- 4. 동물 발견 → 줍기
        AnimalRescue animal = NearestAnimal();
        yield return Teleport(animal.transform.position + new Vector3(0.2f, 0f, 0f));
        yield return WaitRealtime(0.8f); // "완료!" 0.6초(실제 시간) + 시야 확인 0.25초
        yield return WaitFrames(4);
        yield return ExpectIntro(4, "동물이 시야에 들어옴");
        AnimalRescue near = NearestVisibleAnimal();
        CheckHighlights("4단계", new[] { "동물" }, new[] { ((GameObject)near.gameObject, (RectTransform)null) }, "4단계 멈춤: 가장 가까운 동물",
            null, false, ("동물", 0, new Vector2(0.5f, 0.5f)));
        CheckFollowsFrame("4단계 동물", near.gameObject);
        yield return Dismiss(true, "4단계");
        for (int i = 0; i < 5 && gm.CarriedAnimalCount == 0; i++)
        {
            yield return Teleport(animal.transform.position + new Vector3(0.2f, 0f, 0f));
            yield return Press(Key.E);
        }
        yield return WaitFrames(2);
        Check(guide.IsDone(4) && guide.IsNoticeShowing && guide.CurrentText == "가방에 쏙!", $"E로 줍기: 4단계 끝, {Now()}");
        yield return WaitRealtime(2.2f);
        yield return WaitFrames(2);
        Check(!guide.IsShowing && Time.timeScale == 1f, $"\"가방에 쏙!\" 뒤 몬스터를 아직 못 봤고 가방도 여유 있음: {Now()}, 알림도 사라짐");

        // --- 5. 몬스터 발견 → 도구 사용 (멈춤을 푸는 클릭은 도구로 새지 않는다)
        GameObject wolf = monsters.FirstOrDefault(m => m.GetComponent<MonsterAI>() != null);
        wolf.SetActive(true);
        wolf.GetComponent<MonsterHealth>().Stun(1000f);
        Vector2 wp = wolf.GetComponent<MonsterHealth>().GamePosition;
        yield return Teleport(new Vector3(wp.x - 1.5f, wp.y, 0f));
        yield return LookAt(new Vector2(0.95f, 0.5f));
        yield return WaitRealtime(0.35f);
        yield return WaitFrames(4);
        yield return ExpectIntro(5, "몬스터가 시야에 들어옴(스카우트)");
        Check(guide.CurrentText.Contains("지뢰·폭탄"), $"5단계 문구: {guide.CurrentText}");
        int mine = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Mine);
        InventoryUI inv = Object.FindAnyObjectByType<InventoryUI>();
        RectTransform[] toolRects = Enumerable.Range(0, gm.InventorySlotCount).Where(i => gm.GetSlot(i).Type == InventoryItemType.Mine || gm.GetSlot(i).Type == InventoryItemType.Bomb).Select(inv.SlotRect).ToArray();
        CheckHighlights("5단계", new[] { "몬스터", "지뢰·폭탄 칸" }, new[] { (wolf, (RectTransform)null) }.Concat(toolRects.Select(r => ((GameObject)null, r))).ToArray(),
            "5단계 멈춤: 처음 본 몬스터(월드)·지뢰·폭탄 칸(UI)", "지뢰·폭탄 칸", false, ("인벤토리 칸", 1, new Vector2(0.15f, 0.85f)));
        yield return Press(Key.Digit1 + mine); // 멈춘 동안 숫자 키는 막힌다
        int selectedWhilePaused = gm.SelectedSlotIndex;
        yield return Dismiss(false, "5단계");
        Check(!guide.IsDone(5) && gm.GetSlot(mine).Type == InventoryItemType.Mine, $"멈춤을 푼 클릭은 도구를 쓰지 않음(지뢰 그대로, 5단계 아직), 멈춘 동안 고른 칸 {selectedWhilePaused + 1}");
        yield return Press(Key.Digit1 + mine);
        yield return Click();
        Check(guide.IsDone(5) && gm.GetSlot(mine).Type == InventoryItemType.None, $"{mine + 1}번 지뢰 칸을 고르고 클릭(설치): 5단계 끝, {Now()}");
        wolf.SetActive(false);

        // --- 6. 가방이 참
        while (gm.HasInventorySpace) gm.TryAddAnimal($"TutorialTest/{gm.CarriedAnimalCount}", 5, AnimalBehaviorKind.Flee, null);
        yield return WaitRealtime(0.7f);
        yield return WaitFrames(4);
        yield return ExpectIntro(6, "가방이 참");
        Check(guide.CurrentText.StartsWith("가방이 찼어요"), $"6단계 문구: {guide.CurrentText}");
        InventoryUI bag = Object.FindAnyObjectByType<InventoryUI>();
        int noIcon = Enumerable.Range(0, gm.InventorySlotCount).Count(i => gm.GetSlot(i).Type == InventoryItemType.Animal && gm.GetSlot(i).Icon == null);
        sb.AppendLine($"  (참고) 이 테스트가 아이콘 없이(TryAddAnimal icon=null) 넣은 동물 칸 {noIcon}개 - InventoryUI 임시 아이콘(마젠타·검정 체크무늬)으로 그려짐");
        CheckHighlights("6단계", new[] { "트럭" }, new[] { (GameObject.Find("TruckPoint"), (RectTransform)null) }, "6단계 멈춤: 트럭(화면 밖이면 그쪽 가장자리 화살표)", null, true);
        yield return Dismiss(true, "6단계");
        yield return CheckNotice("6단계 마을", true, "6단계 알림");

        // --- 7·8. 트럭: 소파 메뉴·확인 창에서 숨음 → 모두 맡기기 → 소파
        VillageMapGenerator.EditorLayoutOverride = null;
        yield return LoadScene("TruckScene");
        Check(guide.IsDone(6), "동물을 든 채 트럭: 6단계 끝");
        yield return ExpectIntro(7, "동물을 든 채 트럭");
        Check(guide.CurrentText == "수의사에게 [E]로 모두 맡기기", $"7단계 문구: {guide.CurrentText}");
        AnimalDeliveryPoint clerk = Object.FindAnyObjectByType<AnimalDeliveryPoint>();
        CheckHighlights("7단계", new[] { "수의사" }, new[] { (clerk.gameObject, (RectTransform)null) }, "7단계 멈춤: 수의사",
            null, false, ("수의사", 0, new Vector2(0.5f, 0.55f)));
        yield return Dismiss(false, "7단계");
        yield return CheckNotice("7단계 트럭", true, "7단계 알림");
        TruckHubPoint hub = Object.FindAnyObjectByType<TruckHubPoint>();
        yield return Teleport(hub.transform.position + new Vector3(0f, -0.5f, 0f));
        yield return Press(Key.E);
        Check(hub.IsMenuOpen && Time.timeScale == 0f && !guide.IsShowing, $"소파 메뉴가 열리면 안내를 숨김 ({Now()})");
        ((Button)typeof(TruckHubPoint).GetField("nextDayButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hub)).onClick.Invoke();
        yield return WaitFrames(2);
        Check(hub.IsConfirmOpen && !guide.IsShowing, $"목표 미달 소파 확인 창이 열리면 안내를 숨김 ({Now()})");
        yield return Press(Key.E); // 확인 창의 E = 취소
        yield return WaitFrames(2);
        Check(!hub.IsMenuOpen && !hub.IsConfirmOpen && Time.timeScale == 1f && guide.IsNoticeShowing && guide.CurrentStep == 7, $"확인 창을 닫으면 알림이 다시 보임 ({Now()})");
        yield return Teleport(clerk.transform.position + new Vector3(-1.2f, -0.2f, 0f));
        yield return Press(Key.E);
        // 가방(동물 칸)이 목표보다 작으면 한 번에 다 못 맡긴다 - 모자란 수는 앞서 다녀온 순찰에서 맡긴 것으로 친다.
        int bagDelivered = gm.RescuedCount;
        while (gm.RescuedCount < gm.TargetCount) gm.AddCargo(5);
        if (gm.RescuedCount > bagDelivered) sb.AppendLine($"  (참고) 가방으로 {bagDelivered}마리 맡김, 목표 {gm.TargetCount}까지 {gm.RescuedCount - bagDelivered}마리는 앞 순찰분으로 채움");
        yield return WaitRealtime(0.7f);
        yield return WaitFrames(4);
        Check(guide.IsDone(7) && gm.RescuedCount >= gm.TargetCount, $"E로 모두 맡김(구조 {gm.RescuedCount}/{gm.TargetCount}): 7단계 끝");
        yield return ExpectIntro(8, "목표 달성");
        CheckHighlights("8단계", new[] { "소파" }, new[] { (GameObject.Find("Sofa"), (RectTransform)null) }, "8단계 멈춤: 소파 그림");
        yield return Dismiss(true, "8단계");
        yield return CheckNotice("8단계 트럭", false, null);
        yield return Teleport(hub.transform.position + new Vector3(0f, -0.5f, 0f));
        yield return Press(Key.E);
        Check(hub.IsMenuOpen && Time.timeScale == 0f && !guide.IsShowing, $"소파 메뉴가 열린 동안은 안내를 숨김 ({Now()})");
        ((Button)typeof(TruckHubPoint).GetField("nextDayButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hub)).onClick.Invoke();
        yield return WaitForScene("ShelterScene");

        // --- 9. 보호소
        Check(guide.IsDone(8), "보호소: 8단계 끝");
        yield return ExpectIntro(9, "보호소");
        CheckHighlights("9단계", new[] { "출구" }, new[] { (Object.FindAnyObjectByType<ShelterExitPoint>().gameObject, (RectTransform)null) }, "9단계 멈춤: 출구");
        yield return Dismiss(false, "9단계");
        yield return CheckNotice("9단계 보호소", true, "9단계 알림");

        // --- 2일차
        sb.AppendLine("\n== 2. 2일차에는 안 나옴 (멈추지도 않음)");
        ShelterSettlement settlement = Object.FindAnyObjectByType<ShelterSettlement>();
        settlement.EvaluateCycleEnd();
        Time.timeScale = 1f;
        yield return WaitFrames(3);
        Check(gm.CycleCount == 2 && !guide.IsShowing && !guide.IsIntroActive, $"하루 마감 → Day {gm.CycleCount}: {Now()}");
        yield return LoadScene("TruckScene");
        yield return WaitFrames(10);
        Check(!guide.IsShowing && !guide.IsIntroActive && guide.CurrentStep == 0 && Time.timeScale == 1f, $"2일차 트럭: {Now()}, timeScale {Time.timeScale}");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 2 };
        yield return LoadScene("VillageScene");
        DisableMonsters();
        float d2 = gm.VillageTime;
        yield return WaitFrames(30);
        Check(!guide.IsShowing && !guide.IsIntroActive && Time.timeScale == 1f && gm.VillageTime - d2 > 0.9f, $"2일차 마을: {Now()}, 마을 시간 {gm.VillageTime - d2:F2}초 흐름");

        // --- 다시 시작
        sb.AppendLine("\n== 3. 다시 시작하면 1단계부터 다시, 이미 한 행동은 건너뜀");
        VillageMapGenerator.EditorLayoutOverride = null;
        yield return LoadScene("TruckScene");
        gm.ResetGame(); // 게임 오버·클리어 화면의 "다시 시작"과 같은 초기화
        yield return LoadScene("ClassSelectScene");
        Check(Enumerable.Range(1, TutorialGuide.StepCount).All(s => !guide.IsDone(s)), "새 게임: 끝난 단계 없음");
        yield return ExpectIntro(1, "새 게임 클래스 선택");
        yield return Dismiss(true, "새 게임 1단계");
        RestartClass();
        yield return LoadScene("TruckScene");
        gm.AddCargo(4);                                                          // 이미 한 마리 맡김
        gm.TryAddAnimal("TutorialTest/held", 5, AnimalBehaviorKind.Flee, null);  // 이미 한 마리 들고 있음
        yield return WaitFrames(3);
        Check(guide.IsDone(4) && guide.IsDone(7) && guide.CurrentStep == 2, $"이미 줍고 맡긴 상태: 4·7단계는 뜨기 전에 끝, 지금 {Now()}");
        yield return ExpectIntro(2, "새 게임 트럭");
        yield return Dismiss(false, "새 게임 2단계");
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        yield return LoadScene("VillageScene");
        monsters = DisableMonsters();
        yield return ExpectIntro(3, "새 게임 마을");
        yield return Dismiss(true, "새 게임 3단계");
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
        int introFrames = 0;
        for (int i = 0; i < 40; i++) { if (guide.IsIntroActive || Time.timeScale == 0f) introFrames++; yield return null; }
        yield return WaitRealtime(0.8f);
        yield return WaitFrames(2);
        Check(guide.IsDone(3) && guide.CurrentStep != 4 && guide.CurrentStep != 5 && introFrames == 0 && !guide.IsIntroActive && Time.timeScale == 1f,
            $"동물·몬스터가 보여도 끝난 4·5단계는 안 뜨고 멈추지도 않음 (멈춘 프레임 {introFrames}): {Now()}");
        wolf.SetActive(false);
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    // ===================== 검사 도우미 =====================

    private static float AmbientRadius()
    {
        var l = (UnityEngine.Rendering.Universal.Light2D)typeof(PlayerVision).GetField("ambientVisionLight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(PlayerVision.Current);
        return l.pointLightOuterRadius;
    }

    // 단계 step의 멈춤 화면이 떠 있는지: 게임 멈춤, 어둠·카드·시작 안내.
    private static IEnumerator ExpectIntro(int step, string what)
    {
        float until = Time.realtimeSinceStartup + 3f;
        while (!guide.IsIntroActive && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(2);
        Transform intro = guide.transform.Find("TutorialIntro");
        int dimCells = intro != null && intro.Find("Dim/Shade").gameObject.activeInHierarchy ? 1 : 0;
        string prompt = guide.CardRect != null ? string.Concat(guide.CardRect.Find("Prompt").GetComponentsInChildren<TMP_Text>().Select(t => t.text)) : "";
        Check(guide.IsIntroActive && guide.IsIntroShowing && guide.CurrentStep == step && Time.timeScale == 0f && !guide.IsNoticeShowing && dimCells > 0 &&
              prompt.Contains("Space") && prompt.Contains("클릭으로 시작") && TutorialGuide.BlocksInput && GameplayInput.IsBlocked,
            $"{what}: {step}단계가 시작되며 멈춤 - {Now()}, timeScale {Time.timeScale}, 어둠 {dimCells}, 카드 아래 \"{prompt}\", 게임 입력 막힘 {GameplayInput.IsBlocked}");
    }

    // Space(useSpace) 또는 클릭으로 멈춤을 풀고 작은 알림이 남는지.
    private static IEnumerator Dismiss(bool useSpace, string what)
    {
        yield return WaitRealtime(0.35f); // 멈춘 직후 0.3초는 넘어가지 않는다
        if (useSpace) yield return Press(Key.Space);
        else yield return Click();
        yield return WaitFrames(2);
        int outlines = guide.GetComponentsInChildren<Image>().Count(i => i.transform.parent != null && (i.transform.parent.name == "Outline" || i.transform.parent.name == "Mask") || i.name == "Arrow");
        Check(!guide.IsIntroActive && !guide.IsIntroShowing && Time.timeScale == 1f && (guide.IsNoticeShowing || guide.CurrentText == null) && !GameplayInput.IsBlocked && outlines == 0,
            $"{what}: {(useSpace ? "Space" : "클릭")}으로 다시 움직임 - timeScale {Time.timeScale}, {Now()}, 남은 외곽선·화살표 {outlines}");
    }

    // 강조: 이름표가 기대와 같고, 대상마다 그 모양(월드: 지금 스프라이트, UI: 그 Image 그림)을 딴 조각과 노란 외곽선이 있다.
    // 실제 그림으로: 외곽선이 대상 모양에 딱 붙은 도트 1칸 띠이고(네모 테두리가 아님), 대상은 어둠에 안 덮이고 그대로 밝으며, 그 밖은 어둡다.
    // 화면 밖 대상은 가장자리 화살표가 대상 쪽을 가리킨다. 그림 칸(멈춤 화면)과, zooms가 있으면 외곽선 부분 x4 확대 칸을 찍는다.
    private static void CheckHighlights(string tileLabel, string[] labels, (GameObject world, RectTransform ui)[] targets, string what, string mergedUiLabel = null, bool allowOffscreen = false,
        params (string name, int view, Vector2 anchor)[] zooms)
    {
        using (var shot = new UiScreenShot(ShotRes))
        {
            guide.RefreshLayoutNow();
            shot.Refresh();
            var hs = guide.Highlights.ToList();
            var problems = new List<string>();
            Rect screen = new Rect(0, 0, ShotRes.x, ShotRes.y);

            // 1) 대상마다 모양 조각이 그 그림 그대로(월드: 바닥 그림자를 뺀 켜진 스프라이트마다, UI: 그 Image 그림)
            int arrows = 0;
            foreach (var t in targets)
            {
                if (t.world != null)
                {
                    List<SpriteRenderer> srs = ShapeRenderers(t.world);
                    var hv = hs.FirstOrDefault(h => h.Sources.Any(s => s is SpriteRenderer r && srs.Contains(r)) || h.Sources.Contains(t.world.transform));
                    if (hv == null) { problems.Add($"{t.world.name} 강조 없음"); continue; }
                    if (srs.Any(r => r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.Contains("TutorialSilhouette"))))
                        problems.Add($"{t.world.name} 원래 재질이 바뀜");
                    if (hv.Offscreen)
                    {
                        Rect tr = WorldScreenRect(shot.Cam, t.world);
                        if (!allowOffscreen) { problems.Add($"{t.world.name} 화면 밖으로 처리됨"); continue; }
                        arrows++;
                        bool active = hv.Arrow != null && hv.Arrow.gameObject.activeInHierarchy;
                        Rect ar = active ? shot.ScreenRect(hv.Arrow) : default;
                        Vector2 pointing = active ? (Vector2)hv.Arrow.TransformDirection(Vector3.right) : Vector2.zero;
                        float ang = Vector2.Angle(pointing, tr.center - ar.center);
                        bool inside = ar.xMin >= 0 && ar.yMin >= 0 && ar.xMax <= ShotRes.x && ar.yMax <= ShotRes.y;
                        if (!active || ang > 12f || !inside || hv.Shapes.Count > 0 || hv.Outlines.Count > 0)
                            problems.Add($"{t.world.name} 화면 밖 화살표: 켜짐 {active}, 대상 쪽과 각도 {ang:F1}도, 화면 안 {inside}, 모양 조각 {hv.Shapes.Count}");
                        sb.AppendLine($"    {t.world.name}: 화면 밖(가운데 {tr.center:F0}) → 화살표 {UiScreenShot.Fmt(ar, ShotRes.y)}, 대상 쪽과 각도 {ang:F1}도");
                        continue;
                    }
                    foreach (SpriteRenderer r in srs)
                        if (!hv.Shapes.Any(s => s.sprite == r.sprite)) problems.Add($"{t.world.name}/{r.name} 모양 조각이 지금 그림({r.sprite.name})이 아님");
                    sb.AppendLine($"    {t.world.name}: 모양 {hv.Shapes.Count}조각 [{string.Join(", ", hv.Sources.Select(s => s is SpriteRenderer r ? $"{r.name}:{r.sprite.name}" : s.name))}], 외곽선 {hv.Outlines.Count}조각, 두께 {hv.DotUnits * guide.GetComponent<Canvas>().scaleFactor:F1}px");
                }
                else
                {
                    Image img = t.ui.GetComponent<Image>();
                    var hv = hs.FirstOrDefault(h => (img != null && h.Sources.Contains(img)) || h.Sources.Contains(t.ui));
                    if (hv == null) { problems.Add($"{t.ui.name} 강조 없음"); continue; }
                    if (!hv.Shapes.Any(s => s.sprite == (img != null ? img.sprite : null))) problems.Add($"{t.ui.name} 모양 조각이 그 UI 그림이 아님");
                    sb.AppendLine($"    {t.ui.name}: UI 그림 {(img != null && img.sprite != null ? img.sprite.name + "(" + img.type + ")" : "없음(네모 칸)")}, 외곽선 {hv.Outlines.Count}조각, 두께 {hv.DotUnits * guide.GetComponent<Canvas>().scaleFactor:F1}px");
                }
            }
            var wantLabels = labels.Where(l => l != null).OrderBy(l => l).ToList();
            var gotLabels = hs.Select(h => h.Label).Where(l => !string.IsNullOrEmpty(l)).OrderBy(l => l).ToList();
            int wantViews = mergedUiLabel != null ? targets.Count(t => t.world != null) + 1 : targets.Length;
            Check(problems.Count == 0 && wantLabels.SequenceEqual(gotLabels) && hs.Count == wantViews,
                $"{what}: 강조 {hs.Count}개(기대 {wantViews}) 이름표 [{string.Join(", ", gotLabels)}] (기대 [{string.Join(", ", wantLabels)}])" +
                (problems.Count > 0 ? " - " + string.Join("; ", problems) : arrows > 0 ? " - 화면 밖 대상은 그쪽을 가리키는 화살표" : " - 대상마다 지금 그림 모양 그대로"));

            // 2) 네모 테두리가 없다: 외곽선 조각은 모두 대상 그림의 실루엣(그림 없는 네모는 그림 없는 UI 칸·스프라이트 없는 대상뿐), 노랑 9조각 틀 없음
            var outlineImgs = guide.transform.Find("TutorialIntro/Outline").GetComponentsInChildren<Image>().Where(i => i.enabled).ToList();
            var shapeSprites = new HashSet<Sprite>(hs.SelectMany(h => h.Shapes).Select(s => s.sprite));
            bool noFrame = guide.GetComponentsInChildren<Image>(true).All(i => i.sprite == null || !i.sprite.name.Contains("Frame")) &&
                           outlineImgs.All(o => shapeSprites.Contains(o.sprite) && o.color == TutorialGuide.HighlightYellow);
            Check(noFrame, $"{tileLabel} 네모 테두리 없음: 외곽선 조각 {outlineImgs.Count}개가 모두 대상 그림 실루엣(노랑 #{ColorUtility.ToHtmlStringRGB(TutorialGuide.HighlightYellow)}) [{string.Join(", ", shapeSprites.Select(s => s != null ? s.name : "그림 없는 칸"))}]");

            // 3) 실제 그림: 보통(on) · 멈춤 화면 끔(off) · 어둠을 마젠타로 칠한 판정용(mask, 카드·이름표 숨김)
            Texture2D on = shot.Render();
            GameObject introGo = guide.transform.Find("TutorialIntro").gameObject;
            introGo.SetActive(false);
            Texture2D off = shot.Render();
            introGo.SetActive(true);
            guide.RefreshLayoutNow();
            List<Rect> labelRects = hs.Where(h => h.LabelRect.gameObject.activeSelf).Select(h => shot.ScreenRect(h.LabelRect)).ToList();
            Rect card = shot.ScreenRect(guide.CardRect);
            Texture2D mask = RenderMask(shot, hs, -1);
            Color32[] mp = mask.GetPixels32(), onp = on.GetPixels32(), offp = off.GetPixels32();
            AnalyzeOutlines(tileLabel, hs, shot);

            // 어둠: 마젠타 자리(대상·외곽선·카드·이름표 밖)가 멈춤 화면을 껐을 때보다 확실히 어둡다. 대상 모양 자리는 그대로 밝다.
            double dimOn = 0, dimOff = 0, shapeOn = 0, shapeOff = 0; int nd = 0, ns = 0;
            var shapeRegions = hs.Where(h => !h.Offscreen).Select(h => ScreenOf(h.Bounds)).ToList();
            for (int y = 0; y < ShotRes.y; y += 3)
                for (int x = 0; x < ShotRes.x; x += 3)
                {
                    var p = new Vector2(x, y);
                    if (card.Contains(p) || labelRects.Any(r => r.Contains(p))) continue;
                    int i = y * ShotRes.x + x;
                    if (IsMagenta(mp[i])) { dimOn += Luma(onp[i]); dimOff += Luma(offp[i]); nd++; }
                    else if (!IsYellow(mp[i]) && shapeRegions.Any(r => r.Contains(p))) { shapeOn += Luma(onp[i]); shapeOff += Luma(offp[i]); ns++; }
                }
            float dOn = nd > 0 ? (float)(dimOn / nd) : -1f, dOff = nd > 0 ? (float)(dimOff / nd) : -1f;
            float sOn = ns > 0 ? (float)(shapeOn / ns) : -1f, sOff = ns > 0 ? (float)(shapeOff / ns) : -1f;
            Check(nd > 0 && dOff > 0f && (dOn < dOff * 0.5f || (dOff < 0.12f && dOn <= dOff)),
                $"{tileLabel} 어둠: 대상 밖 밝기 {dOn:F3} (멈춤 화면을 끄면 {dOff:F3}) - 절반 아래(원래 0.12 아래로 거의 검은 밤 화면은 더 밝아지지만 않으면 됨)");
            Check(ns == 0 && arrows > 0 || ns > 0 && sOn >= sOff * 0.95f - 0.005f,
                $"{tileLabel} 대상 모양 자리는 어둠 없이 그대로 밝다: {sOn:F3} (멈춤 화면을 끄면 {sOff:F3}, {ns}점)");

            bool cardOver = hs.Any(h => ScreenOf(h.Bounds).Overlaps(card)) || labelRects.Any(r => r.Overlaps(card));
            sb.AppendLine($"    카드 {UiScreenShot.Fmt(card, ShotRes.y)}, 강조 {string.Join(" / ", hs.Select(h => UiScreenShot.Fmt(ScreenOf(h.Bounds), ShotRes.y)))}");
            Check(!cardOver, $"{tileLabel} 가운데 카드가 강조(외곽선·화살표·이름표)를 가리지 않음");

            // 외곽선 부분 x4 확대 칸(1920x1080 실제 그림에서 240x135를 잘라 네 배)
            foreach (var z in zooms)
            {
                if (z.view >= hs.Count || hs[z.view].Shapes.Count == 0) { Check(false, $"{z.name} 확대: 강조 {z.view}번이 없음"); continue; }
                Rect sr = shot.ScreenRect(hs[z.view].Shapes[0].rectTransform);
                Vector2 focus = new Vector2(sr.xMin + sr.width * z.anchor.x, sr.yMin + sr.height * z.anchor.y);
                tiles.Add(($"{z.name} 외곽선 확대 x4", Zoom4(on, focus)));
            }
            Object.DestroyImmediate(off);
            Object.DestroyImmediate(mask);
            tiles.Add(($"{tileLabel} 멈춤", Half(on)));
        }
    }

    // 외곽선 판정(mask 그림: 어둠 = 마젠타). 강조마다 그 둘레 안에서 노랑(외곽선)·마젠타(어둠)·그 밖(대상 모양)을 나눠 본다.
    //  - 노랑이 있다 / 노랑은 모두 대상 모양에서 도트 1칸(+1px) 안 - 모양에 딱 붙은 얇은 띠(떨어진 네모 테두리가 아님)
    //  - 모양 둘레가 노랑 없이 어둠에 바로 닿는 곳이 거의 없다(외곽선이 모양을 빙 둘러쌈)
    //  - 노랑 전체를 감싸는 네모의 네 모서리에 노랑이 없다(모서리가 모양을 따라 깎이거나 둥글다 - 네모 틀이면 모서리가 꽉 찬다)
    private static void AnalyzeOutlines(string tileLabel, List<TutorialGuide.HighlightView> hs, UiScreenShot shot)
    {
        float sf = guide.GetComponent<Canvas>().scaleFactor;
        int W = ShotRes.x, H = ShotRes.y;
        for (int vi = 0; vi < hs.Count; vi++)
        {
            var hv = hs[vi];
            // 이 강조만 남기고 그린다 - 옆 강조(예: 문 바로 위 "오늘 목표")의 노랑이 섞이지 않게
            Texture2D maskTex = RenderMask(shot, hs, vi);
            Color32[] mp = maskTex.GetPixels32();
            Object.DestroyImmediate(maskTex);
            string name = !string.IsNullOrEmpty(hv.Label) ? hv.Label : hv.Sources.Count > 0 ? hv.Sources[0].name : $"강조 {vi}";
            if (hv.Offscreen)
            {
                Rect ar = shot.ScreenRect(hv.Arrow);
                int yel = 0, dark = 0;
                for (int y = Mathf.Max(0, (int)ar.yMin); y < Mathf.Min(H, (int)ar.yMax); y++)
                    for (int x = Mathf.Max(0, (int)ar.xMin); x < Mathf.Min(W, (int)ar.xMax); x++)
                    {
                        Color32 c = mp[y * W + x];
                        if (IsYellow(c)) yel++;
                        else if (IsOutlineDark(c)) dark++;
                    }
                Check(yel > 200 && dark > 50, $"{tileLabel} {name}: 화면 밖 → 가장자리 노란 화살표(노랑 {yel}px, 외곽선 #2F2F2E {dark}px), 빈 네모 없음");
                continue;
            }
            Rect b = ScreenOf(hv.Bounds);
            int x0 = Mathf.Max(1, (int)b.xMin - 4), x1 = Mathf.Min(W - 1, (int)b.xMax + 4), y0 = Mathf.Max(1, (int)b.yMin - 4), y1 = Mathf.Min(H - 1, (int)b.yMax + 4);
            float dotPx = hv.DotUnits * sf;
            int R = Mathf.CeilToInt(dotPx) + 1;
            int yellow = 0, far = 0, boundary = 0, gaps = 0;
            int yxMin = int.MaxValue, yxMax = int.MinValue, yyMin = int.MaxValue, yyMax = int.MinValue;
            bool Shape(int x, int y) { Color32 c = mp[y * W + x]; return !IsMagenta(c) && !IsYellow(c); }
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Color32 c = mp[y * W + x];
                    if (IsYellow(c))
                    {
                        yellow++;
                        yxMin = Mathf.Min(yxMin, x); yxMax = Mathf.Max(yxMax, x); yyMin = Mathf.Min(yyMin, y); yyMax = Mathf.Max(yyMax, y);
                        bool near = false;
                        for (int dy = -R; dy <= R && !near; dy++)
                            for (int dx = -R; dx <= R && !near; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx >= 0 && yy >= 0 && xx < W && yy < H && Shape(xx, yy)) near = true;
                            }
                        if (!near) far++;
                    }
                    else if (!IsMagenta(c) && x > x0 && y > y0 && x < x1 - 1 && y < y1 - 1)
                    {
                        bool m = IsMagenta(mp[y * W + x - 1]) || IsMagenta(mp[y * W + x + 1]) || IsMagenta(mp[(y - 1) * W + x]) || IsMagenta(mp[(y + 1) * W + x]);
                        bool edge = m || !Shape(x - 1, y) || !Shape(x + 1, y) || !Shape(x, y - 1) || !Shape(x, y + 1);
                        if (edge) boundary++;
                        if (m) gaps++;
                    }
                }
            int cw = Mathf.Max(1, Mathf.FloorToInt(dotPx * 0.8f));
            int cornerYellow = 0;
            if (yellow > 0)
                foreach (var (cx, cy, sx, sy) in new[] { (yxMin, yyMin, 1, 1), (yxMax, yyMin, -1, 1), (yxMin, yyMax, 1, -1), (yxMax, yyMax, -1, -1) })
                    for (int j = 0; j < cw; j++) for (int i = 0; i < cw; i++) if (IsYellow(mp[(cy + sy * j) * W + cx + sx * i])) cornerYellow++;
            float gapRate = boundary > 0 ? gaps / (float)boundary : 1f;
            Check(yellow > 0 && far <= yellow * 0.01f && gapRate <= 0.05f && cornerYellow == 0,
                $"{tileLabel} {name} 외곽선이 모양을 따라감: 노랑 {yellow}px, 모양에서 {R}px 넘게 떨어진 노랑 {far}, 노랑 없이 어둠에 닿은 모양 둘레 {gaps}/{boundary}({gapRate:P1}), " +
                $"네 모서리({cw}px) 노랑 {cornerYellow} (도트 1칸 {dotPx:F1}px)");
        }
    }

    // 판정용 그림: 어둠을 마젠타로, 카드·이름표는 숨김. only >= 0이면 그 강조만 남긴다(나머지 강조의 모양·외곽선·화살표를 잠깐 끈다).
    private static Texture2D RenderMask(UiScreenShot shot, List<TutorialGuide.HighlightView> hs, int only)
    {
        Image shade = guide.transform.Find("TutorialIntro/Dim/Shade").GetComponent<Image>();
        Color keep = shade.color;
        var labelStates = hs.Select(h => h.LabelRect.gameObject.activeSelf).ToList();
        var arrowStates = hs.Select(h => h.Arrow != null && h.Arrow.gameObject.activeSelf).ToList();
        var hidden = new List<Image>();
        shade.color = Color.magenta;
        guide.CardRect.gameObject.SetActive(false);
        for (int i = 0; i < hs.Count; i++)
        {
            hs[i].LabelRect.gameObject.SetActive(false);
            if (only < 0 || i == only) continue;
            foreach (Image img in hs[i].Shapes.Concat(hs[i].Outlines)) if (img.enabled) { img.enabled = false; hidden.Add(img); }
            if (hs[i].Arrow != null) hs[i].Arrow.gameObject.SetActive(false);
        }
        Texture2D tex = shot.Render();
        foreach (Image img in hidden) img.enabled = true;
        shade.color = keep;
        guide.CardRect.gameObject.SetActive(true);
        for (int i = 0; i < hs.Count; i++)
        {
            hs[i].LabelRect.gameObject.SetActive(labelStates[i]);
            if (hs[i].Arrow != null) hs[i].Arrow.gameObject.SetActive(arrowStates[i]);
        }
        if (debugMaskDir != null) File.WriteAllBytes($"{debugMaskDir}/mask_{debugMaskCount++}_{only}.png", tex.EncodeToPNG());
        return tex;
    }
    private static string debugMaskDir = System.Environment.GetEnvironmentVariable("TGT_MASK_DIR");
    private static int debugMaskCount;

    private static List<SpriteRenderer> ShapeRenderers(GameObject go) =>
        go.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled && r.sprite != null && r.gameObject.activeInHierarchy && r.color.a > 0.01f && !r.name.StartsWith("Shadow") && r.drawMode == SpriteDrawMode.Simple).ToList();

    // 튜토리얼 Canvas 로컬(가운데 원점) → 캡처 화면 픽셀(왼쪽 아래 원점)
    private static Rect ScreenOf(Rect local)
    {
        float sf = guide.GetComponent<Canvas>().scaleFactor;
        Vector2 half = ((RectTransform)guide.transform).rect.size * 0.5f;
        return Rect.MinMaxRect((local.xMin + half.x) * sf, (local.yMin + half.y) * sf, (local.xMax + half.x) * sf, (local.yMax + half.y) * sf);
    }

    // 색 판정은 비율로 한다 - 카메라 모드 캡처에서는 화면 UI에도 마을 후처리(비네트)가 걸려 가장자리가 어두워진다(실제 게임의 오버레이 UI에는 안 걸림).
    private static bool IsMagenta(Color32 c) { int m = Mathf.Max(c.r, c.b); return m > 40 && c.g < m * 0.15f && Mathf.Abs(c.r - c.b) < m * 0.25f; }
    private static bool IsYellow(Color32 c) => c.r > 60 && c.g >= c.r * 0.78f && c.g <= c.r * 0.98f && c.b < c.r * 0.4f;
    private static bool IsOutlineDark(Color32 c) => c.r >= 12 && c.r <= 0x2F + 4 && Mathf.Abs(c.r - c.g) <= 4 && Mathf.Abs(c.g - c.b) <= 4;
    private static double Luma(Color32 c) => (0.299 * c.r + 0.587 * c.g + 0.114 * c.b) / 255.0;

    // 1920x1080 그림에서 focus를 가운데로 240x135를 잘라 네 배(도트 그대로)로 960x540 칸을 만든다.
    private static Texture2D Zoom4(Texture2D src, Vector2 focus)
    {
        int cw = TileW / 4, ch = TileH / 4;
        int sx = Mathf.Clamp(Mathf.RoundToInt(focus.x - cw * 0.5f), 0, src.width - cw), sy = Mathf.Clamp(Mathf.RoundToInt(focus.y - ch * 0.5f), 0, src.height - ch);
        Color32[] s = src.GetPixels32(), d = new Color32[TileW * TileH];
        for (int y = 0; y < TileH; y++)
            for (int x = 0; x < TileW; x++)
                d[y * TileW + x] = s[(sy + y / 4) * src.width + sx + x / 4];
        var dst = new Texture2D(TileW, TileH, TextureFormat.RGB24, false);
        dst.SetPixels32(d);
        dst.Apply();
        return dst;
    }

    // 애니메이션 프레임이 바뀌거나 뒤집혀도 외곽선이 지금 그림을 따라가는지(멈춘 동안 그림을 직접 바꿔 본다).
    private static void CheckFollowsFrame(string what, GameObject target)
    {
        SpriteRenderer body = ShapeRenderers(target).First();
        Sprite before = body.sprite;
        bool flip0 = body.flipX;
        Sprite other = AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(before.texture)).OfType<Sprite>()
            .Where(s => s != before).OrderBy(s => Mathf.Abs(s.rect.width - before.rect.width) + Mathf.Abs(s.rect.height - before.rect.height)).FirstOrDefault();
        if (other == null) { Check(false, $"{what}: 바꿔 볼 다른 프레임이 없음 ({before.name})"); return; }
        guide.RefreshLayoutNow();
        var hv0 = guide.Highlights.First(h => h.Sources.Contains(body));
        float sign0 = Mathf.Sign(hv0.Shapes[hv0.Sources.IndexOf(body)].rectTransform.localScale.y);
        body.sprite = other;
        body.flipX = !flip0;
        using (var shot = new UiScreenShot(ShotRes))
        {
            guide.RefreshLayoutNow();
            shot.Refresh();
            var hs = guide.Highlights.ToList();
            var hv = hs.First(h => h.Sources.Contains(body));
            int idx = hv.Sources.IndexOf(body);
            Image shape = hv.Shapes[idx];
            bool follow = shape.sprite == other && hv.Outlines.Skip(idx * 4).Take(4).All(o => o.sprite == other) && Mathf.Sign(shape.rectTransform.localScale.y) != sign0;
            Check(follow, $"{what}: 그림 {before.name} → {other.name}, 좌우 뒤집기 {flip0} → {!flip0} 하면 모양·외곽선 조각도 그 그림·방향으로 바뀜");
            AnalyzeOutlines($"{what} (바뀐 그림)", hs, shot);
        }
        body.sprite = before;
        body.flipX = flip0;
        guide.RefreshLayoutNow();
    }

    // 카드 그림체: 도트 패널(HUD C), 가운데, 큰 글자
    private static void CheckCardStyle()
    {
        RectTransform card = guide.CardRect;
        Image img = card.GetComponent<Image>();
        TMP_Text first = card.Find("Line").GetComponentsInChildren<TMP_Text>().FirstOrDefault();
        Check(img != null && img.sprite != null && img.sprite.texture.filterMode == FilterMode.Point && img.type == Image.Type.Sliced && card.anchorMin == new Vector2(0.5f, 0.5f) &&
              first != null && first.fontSize >= 44f && guide.GetComponent<Canvas>().sortingOrder < 1000,
            $"카드: 가운데 앵커, 도트 패널 {img?.sprite?.name}({img?.sprite?.texture.filterMode}), 글자 {first?.fontSize}, Canvas 정렬 {guide.GetComponent<Canvas>().sortingOrder} (일시정지 메뉴 1000 아래)");
    }

    // 작은 알림이 화면 안에 있고 다른 화면 UI·하단 자막 칸과 겹치지 않는다(여러 화면 비율).
    private static IEnumerator CheckNotice(string what, bool tile, string tileLabel)
    {
        yield return WaitFrames(1);
        foreach (Vector2Int res in NoticeResolutions)
        {
            using (var shot = new UiScreenShot(res))
            {
                guide.RefreshLayoutNow();
                shot.Refresh();
                if (!guide.IsNoticeShowing) { Check(false, $"{what} {res.x}x{res.y}: 작은 알림이 안 보임 ({Now()})"); continue; }
                Rect nr = shot.ScreenRect(guide.NoticeRect);
                var others = shot.Graphics(g => g.transform.IsChildOf(guide.transform));
                var hits = others.Where(o => o.rect.Overlaps(nr)).Select(o => o.group).Distinct().ToList();
                bool sub = shot.SubtitleSlots().Any(r => r.Overlaps(nr));
                bool inside = nr.xMin >= 0 && nr.yMin >= 0 && nr.xMax <= res.x && nr.yMax <= res.y;
                Check(hits.Count == 0 && !sub && inside && nr.height < res.y * 0.1f,
                    $"{what} {res.x}x{res.y}: 작은 알림 '{guide.NoticeSlot}' {UiScreenShot.Fmt(nr, res.y)} ({nr.width:F0}x{nr.height:F0}px), 다른 화면 UI {others.Select(o => o.group).Distinct().Count()}묶음과 겹침 {hits.Count}" +
                    (hits.Count > 0 ? $" [{string.Join(", ", hits)}]" : "") + $", 자막 칸 겹침 {sub}");
            }
        }
    }

    private static Rect WorldScreenRect(Camera cam, GameObject go)
    {
        Bounds b = default; bool any = false;
        foreach (SpriteRenderer r in go.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!r.enabled || r.sprite == null || !r.gameObject.activeInHierarchy || r.color.a <= 0.01f) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any)
            foreach (Collider2D c in go.GetComponentsInChildren<Collider2D>())
            {
                if (!c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
        if (!any) b = new Bounds(go.transform.position, new Vector3(1.5f, 1.5f, 0f));
        Vector3 a = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, go.transform.position.z));
        Vector3 c2 = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, go.transform.position.z));
        return Rect.MinMaxRect(Mathf.Min(a.x, c2.x), Mathf.Min(a.y, c2.y), Mathf.Max(a.x, c2.x), Mathf.Max(a.y, c2.y));
    }

    // 1920x1080 → 960x540 (2x2 평균)
    private static Texture2D Half(Texture2D src)
    {
        var dst = new Texture2D(src.width / 2, src.height / 2, TextureFormat.RGB24, false);
        Color32[] s = src.GetPixels32(), d = new Color32[dst.width * dst.height];
        for (int y = 0; y < dst.height; y++)
            for (int x = 0; x < dst.width; x++)
            {
                Color32 a = s[(2 * y) * src.width + 2 * x], b = s[(2 * y) * src.width + 2 * x + 1], c = s[(2 * y + 1) * src.width + 2 * x], e = s[(2 * y + 1) * src.width + 2 * x + 1];
                d[y * dst.width + x] = new Color32((byte)((a.r + b.r + c.r + e.r) / 4), (byte)((a.g + b.g + c.g + e.g) / 4), (byte)((a.b + b.b + c.b + e.b) / 4), 255);
            }
        dst.SetPixels32(d);
        dst.Apply();
        Object.DestroyImmediate(src);
        return dst;
    }

    // 멈춤 화면 칸들 다음 줄에 작은 알림 칸들(왼쪽 위부터).
    private static void SaveSheet()
    {
        if (tiles.Count == 0) return;
        var ordered = tiles.Where(t => t.label.EndsWith("멈춤")).Concat(tiles.Where(t => !t.label.EndsWith("멈춤"))).ToList();
        int introCount = tiles.Count(t => t.label.EndsWith("멈춤"));
        int introRows = (introCount + SheetCols - 1) / SheetCols, noticeRows = (tiles.Count - introCount + SheetCols - 1) / SheetCols;
        int rows = introRows + noticeRows;
        var sheet = new Texture2D(TileW * SheetCols, TileH * rows, TextureFormat.RGB24, false);
        Color32[] fill = Enumerable.Repeat(new Color32(0x2F, 0x2F, 0x2E, 0xFF), sheet.width * sheet.height).ToArray();
        sheet.SetPixels32(fill);
        int intro = 0, note = 0;
        sb.AppendLine("\n== 그림 " + SheetPath + $" (위 {introRows}줄 멈춤 화면, 아래 {noticeRows}줄 외곽선 부분 x4 확대, 왼쪽 위부터)");
        foreach (var t in ordered)
        {
            bool isIntro = t.label.EndsWith("멈춤");
            int idx = isIntro ? intro++ : note++;
            int row = (isIntro ? 0 : introRows) + idx / SheetCols, col = idx % SheetCols;
            sheet.SetPixels32(col * TileW, (rows - 1 - row) * TileH, TileW, TileH, t.tex.GetPixels32());
            sb.AppendLine($"  {row + 1}줄 {col + 1}칸: {t.label}");
        }
        for (int col = 1; col < SheetCols; col++) for (int y = 0; y < sheet.height; y++) for (int dx = -2; dx < 2; dx++) sheet.SetPixel(col * TileW + dx, y, Color.black);
        for (int row = 1; row < rows; row++)
            for (int x = 0; x < sheet.width; x++) for (int dy = -2; dy < 2; dy++) sheet.SetPixel(x, row * TileH + dy, row == rows - introRows ? Color.white : Color.black);
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
        File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sb.AppendLine("  저장: " + Path.GetFullPath(SheetPath));
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

    private static AnimalRescue NearestVisibleAnimal()
    {
        Vector2 p = GameObject.FindGameObjectWithTag("Player").transform.position;
        return Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)
            .Where(a => PlayerVision.IsVisibleToPlayer(a.transform.position)).OrderBy(a => Vector2.Distance(a.transform.position, p)).First();
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
