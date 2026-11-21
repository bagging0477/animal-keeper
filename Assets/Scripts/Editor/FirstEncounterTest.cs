using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Monsters > Run First Encounter Test. 몬스터 첫 만남 카드(MonsterEncounterCard)를 실제 마을(격자 3x4, 시드 1)에서 확인한다.
/// 몬스터는 다섯 종류 프리팹을 플레이어 옆에 직접 놓는다(1일차 맵에는 코뿔소가 없다). 기절시켜 움직이지 않게 두고, 박쥐(기절 안 됨)는 카드를 닫자마자 치운다.
/// 0. 카드 글자(5종 이름·특징·대처법·안내)가 모두 공용 글꼴(UIFont SDF)에 있다
/// 1. 늑대가 처음 시야에 보이면 카드: 게임 멈춤(timeScale 0)·어둠·이름 "늑대"·그림·안내 줄, 게임 입력 막힘. Space로 닫으면 다시 움직이고 입력도 풀린다
/// 2. 같은 종류가 다시 보여도(다른 늑대 포함) 카드는 안 뜬다
/// 3. 호저·코뿔소가 한꺼번에 처음 보이면 차례로 한 장씩(앞 카드를 닫으면 다음 카드). 클릭으로도 닫힌다
/// 4. 소리반응형·박쥐도 처음 보일 때 뜬다(다섯 종류 카드를 1920x1080으로 찍어 한 장으로 저장)
/// 5. 새 게임(ResetGame → 클래스 선택)에서는 다시 처음부터: 튜토리얼 멈춤 화면(3단계)이 떠 있는 동안 늑대가 보여도 기다렸다가, 튜토리얼을 닫은 뒤 늑대 카드가 뜬다
/// 결과: Logs/FirstEncounterTest.txt, 그림: Logs/FirstEncounter/first_encounter_cards.png / batchmode: -executeMethod FirstEncounterTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class FirstEncounterTest
{
    private const string KeyActive = "FET.Active", KeyDone = "FET.Done", KeyBatch = "FET.Batch", KeyPassed = "FET.Passed";
    private const string ReportPath = "Logs/FirstEncounterTest.txt";
    private const string SheetPath = "Logs/FirstEncounter/first_encounter_cards.png";
    private static readonly Vector2Int ShotRes = new Vector2Int(1920, 1080);

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static GameManager gm;
    private static MonsterEncounterCard card;
    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static readonly List<(string label, Texture2D tex)> shots = new List<(string, Texture2D)>();

    static FirstEncounterTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/Monsters/Run First Encounter Test")]
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
            nested.Clear();
            Time.captureDeltaTime = 1f / 30f;
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("FirstEncounterTestKeyboard");
            vmouse = InputSystem.AddDevice<Mouse>("FirstEncounterTestMouse");
            vkb.MakeCurrent();
            vmouse.MakeCurrent();
            routine = RunTest();
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
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        VillageMapGenerator.EditorLayoutOverride = null;
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vkb = null; vmouse = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        foreach (var s in shots) if (s.tex != null) Object.DestroyImmediate(s.tex);
        shots.Clear();
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; TearDown(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e) { Debug.LogException(e); failures?.Add("예외: " + e.Message); sb?.AppendLine("예외: " + e); more = false; }
        if (more) return;
        bool passed = failures != null && failures.Count == 0;
        if (sb != null)
        {
            sb.AppendLine();
            sb.AppendLine(passed ? "결과: 통과" : $"결과: 실패 {failures?.Count ?? -1}건");
            if (!passed && failures != null) foreach (string f in failures) sb.AppendLine("  - " + f);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        }
        Debug.Log($"[FirstEncounterTest] {(passed ? "통과" : "실패")} - {ReportPath}");
        SessionState.SetBool(KeyPassed, passed);
        EditorApplication.update -= Step;
        TearDown();
        SessionState.SetBool(KeyDone, true);
        EditorApplication.ExitPlaymode();
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
    private static IEnumerator WaitRealtime(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }
    private static IEnumerator WaitScene(string name) { float until = Time.realtimeSinceStartup + 30f; while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null; yield return WaitFrames(5); }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static IEnumerator Click()
    {
        Vector2 pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 1 });
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 0 });
        yield return WaitFrames(2);
    }

    private static string Now() => $"카드 {(card.IsShowing ? card.CurrentKind.ToString() : "없음")}, 대기 {card.QueuedCount}, timeScale {Time.timeScale}, 입력 막힘 {GameplayInput.IsBlocked}";

    private static void RestartClass()
    {
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
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
        foreach (VillageMonsterSpawner sp in Object.FindObjectsByType<VillageMonsterSpawner>(FindObjectsInactive.Exclude)) sp.enabled = false;
        foreach (MonsterHealth m in MonsterHealth.Active.ToList()) if (m != null) m.gameObject.SetActive(false);
    }

    private static readonly Dictionary<MonsterKind, string> PrefabPaths = new Dictionary<MonsterKind, string>
    {
        { MonsterKind.Wolf, "Assets/Prefabs/Monsters/PatrolMonster.prefab" },
        { MonsterKind.SoundReactive, "Assets/Prefabs/SoundReactiveMonster.prefab" },
        { MonsterKind.Bat, "Assets/Prefabs/Monsters/BatMonster.prefab" },
        { MonsterKind.Porcupine, "Assets/Prefabs/Monsters/PorcupineMonster.prefab" },
        { MonsterKind.Rhino, "Assets/Prefabs/Monsters/RhinoMonster.prefab" },
    };

    // 플레이어 옆(offset 칸) NavMesh 자리에 몬스터를 놓는다. 켜자마자 기절시켜 움직이지 않게(박쥐는 기절 안 됨).
    private static GameObject Spawn(MonsterKind kind, Vector2 offset)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[kind]);
        Vector2 p = (Vector2)GameObject.FindGameObjectWithTag("Player").transform.position + offset;
        Vector3 nav = NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : new Vector3(p.x, 0f, p.y);
        Transform parent = GameObject.Find("GeneratedMap")?.transform;
        GameObject m = Object.Instantiate(prefab, nav, Quaternion.identity, parent);
        m.name = "FET_" + kind;
        MonsterHealth h = m.GetComponent<MonsterHealth>();
        if (h != null && kind != MonsterKind.Bat) h.Stun(1000f);
        return m;
    }

    private static IEnumerator WaitCard(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!card.IsShowing && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(2);
    }

    private static void CheckCardOpen(MonsterKind kind, string what)
    {
        Transform root = card.transform.Find("EncounterCard");
        bool dim = root != null && root.gameObject.activeInHierarchy && root.Find("Dim") != null && root.Find("Dim").gameObject.activeInHierarchy;
        string all = card.CardRect != null ? string.Join(" ", card.CardRect.GetComponentsInChildren<TMP_Text>().Select(t => t.text)) : "";
        Image pic = card.CardRect != null ? card.CardRect.GetComponentsInChildren<Image>().FirstOrDefault(i => i.name == "Portrait") : null;
        MonsterBestiary.Entry e = MonsterBestiary.Get(kind, gm.Config);
        float body = card.CardRect != null ? card.CardRect.GetComponentsInChildren<TMP_Text>().Where(t => t.text == e.Name).Select(t => t.fontSize).FirstOrDefault() : 0f;
        Check(card.IsShowing && card.CurrentKind == kind && Time.timeScale == 0f && dim && GameplayInput.IsBlocked && all.Contains(e.Name) && all.Contains("클릭으로 계속") &&
              pic != null && pic.enabled && pic.sprite != null && pic.sprite.texture.filterMode == FilterMode.Point && Mathf.Approximately(body, TutorialGuide.CardTextSize),
            $"{what}: {e.Name} 카드 - {Now()}, 어둠 {dim}, 그림 {pic?.sprite?.name} ({pic?.sprite?.texture.filterMode}), 이름 글씨 {body}(튜토리얼 카드 {TutorialGuide.CardTextSize}), 글자 \"{all}\"");
    }

    private static IEnumerator Dismiss(bool useSpace, string what)
    {
        yield return WaitRealtime(0.35f);
        if (useSpace) yield return Press(Key.Space); else yield return Click();
        yield return WaitFrames(2);
        Check(!card.IsShowing || card.CurrentKind != null, $"{what}: {(useSpace ? "Space" : "클릭")}으로 닫음 - {Now()}");
    }

    private static void CaptureCard(string label)
    {
        using (var shot = new UiScreenShot(ShotRes))
        {
            shot.Refresh();
            card.RefreshLayoutNow();
            shot.Refresh();
            Rect cr = shot.ScreenRect(card.CardRect);
            bool inside = cr.xMin >= 0 && cr.yMin >= 0 && cr.xMax <= ShotRes.x && cr.yMax <= ShotRes.y;
            Check(inside, $"{label} 카드 1920x1080 화면 안: {UiScreenShot.Fmt(cr, ShotRes.y)} ({cr.width:F0}x{cr.height:F0}px)");
            shots.Add((label, shot.Render()));
        }
    }

    private static IEnumerator RunTest()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"FirstEncounterTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitScene("ClassSelectScene");
        gm = GameManager.Instance;
        card = MonsterEncounterCard.EnsureInstance();
        TutorialGuide.IntrosSuppressed = true; // 1~4는 튜토리얼 멈춤 화면 없이(5에서 켠다)
        RestartClass();

        // 0. 글꼴
        sb.AppendLine("\n== 0. 카드 글자가 모두 공용 글꼴에 있다");
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        string text = MonsterEncounterCard.PromptText + MonsterEncounterCard.ChipText + string.Concat(MonsterBestiary.AllKinds.Select(k => MonsterBestiary.Get(k, gm.Config)).Select(e => e.Name + string.Concat(e.Traits) + e.Tip));
        string chars = new string(text.Where(c => c != '[' && c != ']').Distinct().ToArray());
        bool hasAll = font != null && font.HasCharacters(chars, out uint[] missing, false, false);
        Check(hasAll, $"글꼴 {font?.name}에 카드 글자 {chars.Length}종이 모두 있음");
        foreach (MonsterKind k in MonsterBestiary.AllKinds)
        {
            var e = MonsterBestiary.Get(k, gm.Config);
            sb.AppendLine($"  {e.Name}: {string.Join(" / ", e.Traits)} | 대처: {e.Tip}");
        }

        yield return LoadVillage();
        sb.AppendLine($"\n마을(격자 3x4, 시드 1), 플레이어 {GameObject.FindGameObjectWithTag("Player").transform.position}");

        // 1. 늑대 처음 → 카드, Space로 닫기
        sb.AppendLine("\n== 1. 처음 보이면 카드(멈춤·어둠·입력 막힘), Space로 닫기");
        Check(!card.IsShowing && !card.HasSeen(MonsterKind.Wolf), $"처음: {Now()}");
        GameObject wolf = Spawn(MonsterKind.Wolf, new Vector2(2.5f, 0f));
        yield return WaitCard(3f);
        CheckCardOpen(MonsterKind.Wolf, "늑대가 처음 시야에 보임");
        CaptureCard("늑대");
        yield return Dismiss(true, "늑대");
        Check(!card.IsShowing && Time.timeScale == 1f, $"닫은 뒤 다시 움직임: {Now()}");
        yield return WaitFrames(2);
        Check(!GameplayInput.IsBlocked, $"닫은 다음 프레임부터 게임 입력 받음: {Now()}");

        // 2. 두 번째는 안 뜸
        sb.AppendLine("\n== 2. 같은 종류가 다시 보여도 안 뜬다");
        wolf.SetActive(false);
        yield return WaitRealtime(0.6f);
        wolf.SetActive(true);
        wolf.GetComponent<MonsterHealth>().Stun(1000f);
        GameObject wolf2 = Spawn(MonsterKind.Wolf, new Vector2(-2.5f, 0f));
        yield return WaitRealtime(1.5f);
        Check(!card.IsShowing && card.QueuedCount == 0 && Time.timeScale == 1f, $"같은 늑대·다른 늑대가 다시 보임(1.5초): {Now()}");
        wolf.SetActive(false); wolf2.SetActive(false);

        // 3. 호저·코뿔소 한꺼번에 → 차례로
        sb.AppendLine("\n== 3. 두 종류가 한꺼번에 처음 보이면 차례로");
        GameObject porc = Spawn(MonsterKind.Porcupine, new Vector2(2.5f, 1f));
        GameObject rhino = Spawn(MonsterKind.Rhino, new Vector2(-2.5f, -1f));
        yield return WaitCard(3f);
        MonsterKind? first = card.CurrentKind;
        Check(card.IsShowing && (first == MonsterKind.Porcupine || first == MonsterKind.Rhino) && card.QueuedCount == 1, $"첫 카드 {first}, 다음 카드 대기 {card.QueuedCount}: {Now()}");
        if (first.HasValue) CheckCardOpen(first.Value, "한꺼번에 - 첫 카드");
        if (first.HasValue) CaptureCard(first == MonsterKind.Porcupine ? "호저" : "코뿔소");
        yield return Dismiss(false, "첫 카드");
        yield return WaitCard(3f);
        MonsterKind? second = card.CurrentKind;
        Check(card.IsShowing && second.HasValue && second != first && card.QueuedCount == 0, $"앞 카드를 닫자 다음 카드 {second}: {Now()}");
        if (second.HasValue) CheckCardOpen(second.Value, "한꺼번에 - 두 번째 카드");
        if (second.HasValue) CaptureCard(second == MonsterKind.Porcupine ? "호저" : "코뿔소");
        yield return Dismiss(true, "두 번째 카드");
        Check(!card.IsShowing && Time.timeScale == 1f, $"두 장 다 닫은 뒤: {Now()}");
        porc.SetActive(false); rhino.SetActive(false);

        // 4. 소리반응형·박쥐
        sb.AppendLine("\n== 4. 소리반응형·박쥐");
        foreach (MonsterKind k in new[] { MonsterKind.SoundReactive, MonsterKind.Bat })
        {
            // 박쥐는 기절하지 않고 곧장 날아오므로 바라보는 쪽(정면 손전등 시야 안 - 그 자리에서 굳는다)에 놓는다
            Vector2 look = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerMovement>().LookDirection;
            GameObject m = Spawn(k, k == MonsterKind.Bat ? look.normalized * 3f : new Vector2(2.5f, 0f));
            if (k == MonsterKind.Bat)
            {
                // 박쥐는 닿으면 물러났다가 다시 와서 또 피해를 준다 - 카드를 기다리는 동안 체력을 채워 게임오버로 카드가 막히지 않게 한다
                PropertyInfo healthProp = typeof(GameManager).GetProperty("Health");
                float batUntil = Time.realtimeSinceStartup + 3f;
                while (!card.IsShowing && Time.realtimeSinceStartup < batUntil)
                {
                    healthProp.GetSetMethod(true).Invoke(gm, new object[] { GameManager.MaxHealth });
                    yield return null;
                }
            }
            yield return WaitCard(3f);
            CheckCardOpen(k, $"{k} 처음 보임");
            CaptureCard(MonsterBestiary.Get(k, gm.Config).Name);
            yield return Dismiss(true, k.ToString());
            m.SetActive(false);
            yield return WaitFrames(2);
        }
        Check(MonsterBestiary.AllKinds.All(card.HasSeen), $"다섯 종류 모두 소개함: {string.Join(", ", MonsterBestiary.AllKinds.Where(card.HasSeen))}");

        // 5. 새 게임 + 튜토리얼 멈춤 화면 중에는 기다림
        sb.AppendLine("\n== 5. 새 게임에서 다시 처음부터, 튜토리얼 멈춤 화면이 끝난 뒤에 뜬다");
        gm.ResetGame();
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        yield return WaitFrames(3);
        Check(!card.HasSeen(MonsterKind.Wolf), $"새 게임(클래스 선택 전): 본 종류 기록이 비워짐");
        RestartClass();
        TutorialGuide.IntrosSuppressed = false;
        TutorialGuide guide = TutorialGuide.EnsureInstance();
        yield return LoadVillage();
        float until = Time.realtimeSinceStartup + 3f;
        while (!guide.IsIntroActive && Time.realtimeSinceStartup < until) yield return null;
        Check(guide.IsIntroActive && guide.CurrentStep == 3, $"튜토리얼 {guide.CurrentStep}단계 멈춤 화면: {guide.IsIntroActive}");
        GameObject wolf3 = Spawn(MonsterKind.Wolf, new Vector2(2.5f, 0f));
        yield return WaitRealtime(1.5f);
        Check(guide.IsIntroActive && !card.IsShowing && card.QueuedCount == 1, $"튜토리얼 멈춤 중에 늑대가 보임(1.5초): 카드는 기다림 - {Now()}");
        yield return WaitRealtime(0.35f);
        yield return Press(Key.Space);
        yield return WaitFrames(2);
        Check(!guide.IsIntroActive, $"튜토리얼 멈춤 화면을 Space로 닫음");
        yield return WaitCard(3f);
        CheckCardOpen(MonsterKind.Wolf, "튜토리얼이 끝난 뒤 새 게임의 늑대 카드");
        yield return Dismiss(true, "새 게임 늑대");
        Check(!card.IsShowing && Time.timeScale == 1f, $"닫은 뒤: {Now()}");
        wolf3.SetActive(false);

        // 그림: 다섯 종류 카드(1920x1080 → 반으로 줄여 3x2)
        var order = new[] { "늑대", "소리반응형", "박쥐", "호저", "코뿔소" };
        var tiles = order.Select(n => shots.FirstOrDefault(s => s.label == n)).Where(s => s.tex != null).ToList();
        const int tw = 960, th = 540;
        var sheet = new Texture2D(tw * 3, th * 2, TextureFormat.RGB24, false);
        sheet.SetPixels(Enumerable.Repeat(new Color(0.09f, 0.09f, 0.09f), tw * 3 * th * 2).ToArray());
        for (int i = 0; i < tiles.Count; i++)
        {
            Texture2D t = tiles[i].tex;
            var px = new Color[tw * th];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    Color a = t.GetPixel(2 * x, 2 * y), b = t.GetPixel(2 * x + 1, 2 * y), c = t.GetPixel(2 * x, 2 * y + 1), d = t.GetPixel(2 * x + 1, 2 * y + 1);
                    px[y * tw + x] = (a + b + c + d) * 0.25f;
                }
            int col = i % 3, row = i / 3;
            sheet.SetPixels(col * tw, (1 - row) * th, tw, th, px);
        }
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
        File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        Check(tiles.Count == 5, $"다섯 카드 그림 저장: {Path.GetFullPath(SheetPath)} ({string.Join(", ", tiles.Select(t => t.label))}) - 1920x1080을 반으로 줄여 3x2");
    }
}
