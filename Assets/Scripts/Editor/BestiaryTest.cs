using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > UI > Run Bestiary Test. 구조 도감(BestiaryUI)을 실제 게임 흐름(타이틀 → 클래스 선택 → 트럭 → 마을)에서 확인한다. 키는 가상 키보드로 누른다.
/// 0. 도감 글자가 모두 공용 글꼴에 있다. 항목 수 = 실제 종류 수(프리팹 폴더의 동물·몬스터 프리팹을 종류로 묶은 수, 마을 생성기·설정의 스폰 프리팹 종류 수)
/// 1. 트럭: "[Tab] 도감" 안내가 보이고, Tab으로 열면 멈춤(timeScale 0)·입력 막힘, 숫자키가 칸 선택으로 새지 않는다. 새 게임이라 만난 대상 0, 모든 칸 "???"·실루엣
/// 2. →/←·클릭으로 넘기기, Tab·E·Esc로 닫기(닫으면 다시 움직이고 다음 프레임부터 입력을 받는다, Esc가 일시정지 메뉴를 열지 않는다)
/// 3. 마을: Tab으로 열리지 않는다. 플레이어 옆에 코알라·늑대를 놓으면 만난 것으로 기록된다
/// 4. 트럭에서 다시 열면 만난 대상 2/10, 코알라·늑대는 이름·그림·글(MonsterBestiary/AnimalBestiary 그대로)이 보인다
/// 5. 화면 캡처(1920x1080): 동물 쪽(거북이 선택), 몬스터 쪽(늑대 선택), 여는 곳(트럭 "[Tab] 도감") - 거위·코뿔소는 아직 못 만난 상태 → Logs/Bestiary/bestiary_applied.png
/// 결과: Logs/BestiaryTest.txt / batchmode: -executeMethod BestiaryTest.RunBatch (통과면 0 아니면 1, 그림 때문에 -nographics 없이)
/// </summary>
[InitializeOnLoad]
public static class BestiaryTest
{
    private const string KeyActive = "BET.Active", KeyDone = "BET.Done", KeyBatch = "BET.Batch", KeyPassed = "BET.Passed";
    private const string ReportPath = "Logs/BestiaryTest.txt";
    private const string SheetPath = "Logs/Bestiary/bestiary_applied.png";
    private static readonly Vector2Int ShotRes = new Vector2Int(1920, 1080);

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static GameManager gm;
    private static BestiaryUI ui;
    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static readonly List<(string label, Texture2D tex)> shots = new List<(string, Texture2D)>();

    static BestiaryTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/UI/Run Bestiary Test")]
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
            vkb = InputSystem.AddDevice<Keyboard>("BestiaryTestKeyboard");
            vmouse = InputSystem.AddDevice<Mouse>("BestiaryTestMouse");
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
        Debug.Log($"[BestiaryTest] {(passed ? "통과" : "실패")} - {ReportPath}");
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

    private static IEnumerator ClickAt(Vector2 pos)
    {
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 1 });
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = pos, buttons = 0 });
        yield return WaitFrames(2);
    }

    private static string Now() => $"도감 {(ui.IsOpen ? "열림" : "닫힘")}, 고른 칸 {ui.SelectedIndex}, timeScale {Time.timeScale}, 입력 막힘 {GameplayInput.IsBlocked}, 일시정지 {PauseMenu.IsPaused}";

    private static IEnumerator LoadTruck()
    {
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        yield return WaitFrames(5);
    }

    private static IEnumerator LoadVillage()
    {
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        float until = Time.realtimeSinceStartup + 20f;
        while (GameObject.Find("GeneratedMap") == null && Time.realtimeSinceStartup < until) yield return null;
        // 처음 보이는 동물·몬스터를 기록하기 전에 모두 치운다(직접 놓은 것만 보이게)
        VillageMapGenerator.EditorLayoutOverride = null;
        foreach (VillageMonsterSpawner sp in Object.FindObjectsByType<VillageMonsterSpawner>(FindObjectsInactive.Exclude)) sp.enabled = false;
        foreach (MonsterHealth m in MonsterHealth.Active.ToList()) if (m != null) m.gameObject.SetActive(false);
        foreach (AnimalSleep a in AnimalSleep.Active.ToList()) if (a != null) a.gameObject.SetActive(false);
        ui.ClearDiscoveries(); // 치우기 전 몇 프레임 사이에 보인 것은 지운다
        yield return WaitFrames(2);
    }

    private static GameObject SpawnNearPlayer(string path, Vector2 offset)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Vector2 p = (Vector2)GameObject.FindGameObjectWithTag("Player").transform.position + offset;
        // 몬스터는 NavMesh(x, 0, z) 위치, 동물은 2D(x, y) 위치에 둔다(AnimalSleep.GamePosition = transform.position)
        Vector3 pos = new Vector3(p.x, p.y, 0f);
        if (prefab.GetComponent<MonsterHealth>() != null)
            pos = NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : new Vector3(p.x, 0f, p.y);
        GameObject go = Object.Instantiate(prefab, pos, Quaternion.identity, GameObject.Find("GeneratedMap")?.transform);
        go.name = "BET_" + prefab.name;
        MonsterHealth h = go.GetComponent<MonsterHealth>();
        if (h != null) h.Stun(1000f);
        return go;
    }

    // 프리팹 폴더에서 실제 종류(행동 스크립트로 묶음)를 센다
    private static (HashSet<AnimalSpecies> animals, HashSet<MonsterKind> monsters) PrefabKinds()
    {
        var animals = new HashSet<AnimalSpecies>();
        var monsters = new HashSet<MonsterKind>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (AnimalBestiary.KindOf(go) is AnimalSpecies a) animals.Add(a);
            if (go.GetComponent<MonsterHealth>() != null && MonsterBestiary.KindOf(go) is MonsterKind m) monsters.Add(m);
        }
        return (animals, monsters);
    }

    // 마을에서 실제로 나오는 동물 종류: 마을 생성기의 동물 풀 + 설정의 거북이·거위 프리팹
    private static HashSet<AnimalSpecies> VillageAnimalKinds()
    {
        var set = new HashSet<AnimalSpecies>();
        string text = File.ReadAllText("Assets/Scenes/VillageScene.unity");
        int start = text.IndexOf("  animalPrefabs:", System.StringComparison.Ordinal);
        if (start >= 0)
        {
            foreach (string line in text.Substring(start).Split('\n').Skip(1).TakeWhile(l => l.StartsWith("  - ")))
            {
                int g = line.IndexOf("guid: ", System.StringComparison.Ordinal);
                if (g < 0) continue;
                string guid = line.Substring(g + 6, 32);
                if (AnimalBestiary.KindOf(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid))) is AnimalSpecies a) set.Add(a);
            }
        }
        if (AnimalBestiary.KindOf(gm.Config.turtlePrefab) is AnimalSpecies t) set.Add(t);
        if (AnimalBestiary.KindOf(gm.Config.goosePrefab) is AnimalSpecies go) set.Add(go);
        return set;
    }

    private static Texture2D Capture(string label, bool checkPanel)
    {
        using (var shot = new UiScreenShot(ShotRes))
        {
            shot.Refresh();
            ui.RefreshLayoutNow();
            shot.Refresh();
            RectTransform target = checkPanel ? ui.PanelRect : ui.HintRect;
            Rect r = shot.ScreenRect(target);
            bool inside = r.xMin >= 0 && r.yMin >= 0 && r.xMax <= ShotRes.x && r.yMax <= ShotRes.y;
            Check(inside, $"{label}: {(checkPanel ? "도감 판" : "Tab 안내")} 1920x1080 화면 안 - {UiScreenShot.Fmt(r, ShotRes.y)} ({r.width:F0}x{r.height:F0}px)");
            Texture2D tex = shot.Render();
            shots.Add((label, tex));
            return tex;
        }
    }

    private static IEnumerator RunTest()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"BestiaryTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitScene("ClassSelectScene");
        gm = GameManager.Instance;
        ui = BestiaryUI.EnsureInstance();
        TutorialGuide.IntrosSuppressed = true;
        MonsterEncounterCard.Suppressed = true;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();

        // 0. 글꼴·항목 수
        sb.AppendLine("\n== 0. 글꼴, 항목 수 = 실제 종류 수");
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        var allText = new StringBuilder(BestiaryUI.TitleText + BestiaryUI.CloseHintText + BestiaryUI.OpenHintText + BestiaryUI.UnknownName + BestiaryUI.UnknownText +
            BestiaryUI.AnimalHeader + BestiaryUI.MonsterHeader + BestiaryUI.TraitHeader + BestiaryUI.AnimalTipHeader + BestiaryUI.MonsterTipHeader + "만난 대상 0123456789/·~kg체력");
        foreach (AnimalSpecies a in AnimalBestiary.AllKinds) { var e = AnimalBestiary.Get(a, gm.Config); allText.Append(e.Name + e.Trait + e.Tip); sb.AppendLine($"  {e.Name}: {e.Trait} | 잡는 법: {e.Tip}"); }
        foreach (MonsterKind m in MonsterBestiary.AllKinds) { var e = MonsterBestiary.Get(m, gm.Config); allText.Append(e.Name + string.Concat(e.Traits) + e.Tip); sb.AppendLine($"  {e.Name}: {string.Join(" / ", e.Traits)} | 대처법: {e.Tip}"); }
        string chars = new string(allText.ToString().Where(c => c != '[' && c != ']' && c != ' ').Distinct().ToArray());
        uint[] missing = null;
        bool hasAll = font != null && font.HasCharacters(chars, out missing, false, false);
        Check(hasAll, $"글꼴 {font?.name}에 도감 글자 {chars.Length}종이 모두 있음" + (hasAll ? "" : $" (없는 글자: {new string(missing?.Select(u => (char)u).ToArray())})"));

        yield return LoadTruck();
        var (prefabAnimals, prefabMonsters) = PrefabKinds();
        var villageAnimals = VillageAnimalKinds();
        Check(ui.AnimalCount == prefabAnimals.Count && ui.AnimalCount == villageAnimals.Count && ui.Items.Where(i => !i.IsMonster).Select(i => i.Animal).ToHashSet().SetEquals(villageAnimals),
            $"동물 항목 {ui.AnimalCount} = 프리팹 동물 종류 {prefabAnimals.Count} ({string.Join(",", prefabAnimals)}) = 마을에 나오는 종류 {villageAnimals.Count} ({string.Join(",", villageAnimals)})");
        Check(ui.MonsterCount == prefabMonsters.Count && ui.Items.Where(i => i.IsMonster).Select(i => i.Monster).ToHashSet().SetEquals(prefabMonsters) && prefabMonsters.Count == MonsterBestiary.AllKinds.Length,
            $"몬스터 항목 {ui.MonsterCount} = 프리팹 몬스터 종류 {prefabMonsters.Count} ({string.Join(",", prefabMonsters)})");
        Check(ui.Items.All(i => i.Prefab != null), $"항목마다 그림 프리팹 있음: {string.Join(", ", ui.Items.Select(i => (i.IsMonster ? i.Monster.ToString() : i.Animal.ToString()) + "=" + (i.Prefab != null ? i.Prefab.name : "없음")))}");
        foreach (var it in ui.Items) sb.AppendLine($"  태그 {(it.IsMonster ? it.Monster.ToString() : it.Animal.ToString())}: {BestiaryUI.TagOf(it)}");

        // 1. 트럭에서 Tab으로 열기
        sb.AppendLine("\n== 1. 트럭에서 Tab으로 열기(멈춤·입력 막힘), 새 게임이라 모두 ???");
        Check(!ui.IsOpen && ui.IsHintShowing && Time.timeScale == 1f, $"트럭: Tab 안내 보임 - {Now()}");
        yield return Press(Key.Tab);
        Check(ui.IsOpen && Time.timeScale == 0f && GameplayInput.IsBlocked && !ui.IsHintShowing, $"Tab으로 열림 - {Now()}");
        Check(ui.DiscoveredCount == 0 && ui.CountText == $"만난 대상 0/{ui.Items.Count}", $"머리 줄 \"{ui.CountText}\"");
        Check(Enumerable.Range(0, ui.Items.Count).All(i => ui.CellName(i) == BestiaryUI.UnknownName && ui.CellShowsSilhouette(i)), "모든 칸 ??? + 실루엣");
        Check(ui.DetailText.Contains(BestiaryUI.UnknownText), $"상세: \"{ui.DetailText}\"");
        int slotBefore = gm.SelectedSlotIndex;
        int pressSlot = slotBefore == 1 ? 2 : 1;
        yield return Press(Key.Digit1 + pressSlot);
        Check(gm.SelectedSlotIndex == slotBefore && ui.IsOpen, $"열린 동안 숫자키 {pressSlot + 1}: 칸 선택 그대로 {gm.SelectedSlotIndex} (게임 입력 막힘)");
        yield return Press(Key.Escape);
        Check(!ui.IsOpen && !PauseMenu.IsPaused && Time.timeScale == 1f, $"Esc로 닫힘, 일시정지 메뉴 안 열림 - {Now()}");
        yield return WaitFrames(1);
        Check(!GameplayInput.IsBlocked, $"닫은 다음 프레임부터 입력 받음 - {Now()}");

        // 2. 넘기기·클릭·닫기
        sb.AppendLine("\n== 2. →/←·클릭으로 넘기기, Tab·E로 닫기");
        yield return Press(Key.Tab);
        int s0 = ui.SelectedIndex;
        yield return Press(Key.RightArrow);
        Check(ui.SelectedIndex == (s0 + 1) % ui.Items.Count, $"→: {s0} → {ui.SelectedIndex}");
        yield return Press(Key.LeftArrow);
        yield return Press(Key.LeftArrow);
        Check(ui.SelectedIndex == (s0 + ui.Items.Count - 1) % ui.Items.Count, $"← 두 번: {ui.SelectedIndex} (처음과 끝이 이어짐)");
        Canvas.ForceUpdateCanvases();
        Vector3[] wc = new Vector3[4];
        ui.CellRect(7).GetWorldCorners(wc);
        yield return ClickAt((wc[0] + wc[2]) * 0.5f);
        Check(ui.SelectedIndex == 7 && ui.IsOpen, $"8번째 칸 클릭: 고른 칸 {ui.SelectedIndex}");
        yield return Press(Key.Tab);
        Check(!ui.IsOpen && Time.timeScale == 1f, $"Tab으로 닫힘 - {Now()}");
        yield return Press(Key.Tab);
        Check(ui.IsOpen, $"다시 Tab으로 열림 - {Now()}");
        yield return Press(Key.E);
        Check(!ui.IsOpen && Time.timeScale == 1f && SceneManager.GetActiveScene().name == "TruckScene", $"E로 닫힘(트럭 상호작용으로 새지 않음) - {Now()}, 씬 {SceneManager.GetActiveScene().name}");

        // 3. 마을: 열리지 않고, 처음 보이면 기록
        sb.AppendLine("\n== 3. 마을에서는 열리지 않고, 처음 시야에 보인 종류를 기록");
        yield return LoadVillage();
        Check(!ui.IsHintShowing, "마을: Tab 안내 없음");
        yield return Press(Key.Tab);
        Check(!ui.IsOpen && Time.timeScale == 1f, $"마을에서 Tab: 열리지 않음 - {Now()}");
        GameObject koala = SpawnNearPlayer("Assets/Prefabs/Animals/AnimalWander.prefab", new Vector2(1.5f, 0f));
        GameObject wolf = SpawnNearPlayer("Assets/Prefabs/Monsters/PatrolMonster.prefab", new Vector2(-2.5f, 0f));
        yield return WaitRealtime(1.2f);
        Check(ui.IsDiscovered(AnimalSpecies.Koala) && ui.IsDiscovered(MonsterKind.Wolf) && ui.DiscoveredCount == 2,
            $"코알라·늑대가 보인 뒤 만난 대상 {ui.DiscoveredCount} (코알라 {ui.IsDiscovered(AnimalSpecies.Koala)}, 늑대 {ui.IsDiscovered(MonsterKind.Wolf)})");
        koala.SetActive(false); wolf.SetActive(false);

        // 4. 트럭에서 다시 열기
        sb.AppendLine("\n== 4. 트럭에서 만난 대상 확인");
        yield return LoadTruck();
        yield return Press(Key.Tab);
        Check(ui.IsOpen && ui.CountText == $"만난 대상 2/{ui.Items.Count}", $"머리 줄 \"{ui.CountText}\" - {Now()}");
        int koalaIdx = ui.Items.ToList().FindIndex(i => !i.IsMonster && i.Animal == AnimalSpecies.Koala);
        int wolfIdx = ui.Items.ToList().FindIndex(i => i.IsMonster && i.Monster == MonsterKind.Wolf);
        ui.Select(koalaIdx);
        var ka = AnimalBestiary.Get(AnimalSpecies.Koala, gm.Config);
        Check(ui.DetailName == ka.Name && ui.DetailText.Contains(ka.Trait) && ui.DetailText.Contains("구조!") && !ui.CellShowsSilhouette(koalaIdx) &&
              ui.DetailPortrait.enabled && ui.DetailPortrait.sprite.texture.filterMode == FilterMode.Point,
            $"코알라 상세: \"{ui.DetailText}\", 그림 {ui.DetailPortrait.sprite?.name} ({ui.DetailPortrait.sprite?.texture.filterMode})");
        ui.Select(wolfIdx);
        var wm = MonsterBestiary.Get(MonsterKind.Wolf, gm.Config);
        Check(ui.DetailName == wm.Name && wm.Traits.All(t => ui.DetailText.Contains(t)) && ui.DetailText.Contains("로 달아나요!") && ui.DetailPortrait.sprite.texture.filterMode == FilterMode.Point,
            $"늑대 상세(첫 만남 카드 글 그대로): \"{ui.DetailText}\"");
        Check(ui.CellName(koalaIdx + 1) == BestiaryUI.UnknownName, $"못 만난 고양이 칸: {ui.CellName(koalaIdx + 1)}");

        // 5. 캡처: 거위·코뿔소만 못 만난 상태
        sb.AppendLine("\n== 5. 화면 캡처 1920x1080");
        foreach (AnimalSpecies a in AnimalBestiary.AllKinds) if (a != AnimalSpecies.Goose) ui.Discover(a);
        foreach (MonsterKind m in MonsterBestiary.AllKinds) if (m != MonsterKind.Rhino) ui.Discover(m);
        ui.Select(ui.Items.ToList().FindIndex(i => !i.IsMonster && i.Animal == AnimalSpecies.Turtle));
        yield return WaitFrames(2);
        Capture("동물 쪽(거북이)", true);
        ui.Select(wolfIdx);
        yield return WaitFrames(2);
        Capture("몬스터 쪽(늑대)", true);
        yield return Press(Key.Tab);
        yield return WaitFrames(3);
        Check(!ui.IsOpen && ui.IsHintShowing, $"닫은 뒤 Tab 안내 다시 보임 - {Now()}");
        Capture("여는 곳(트럭 [Tab] 도감)", false);

        // 세 장을 세로로 이어 한 장(1920x3240)
        int n = shots.Count;
        var sheet = new Texture2D(ShotRes.x, ShotRes.y * n, TextureFormat.RGB24, false);
        for (int i = 0; i < n; i++) sheet.SetPixels(0, (n - 1 - i) * ShotRes.y, ShotRes.x, ShotRes.y, shots[i].tex.GetPixels());
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
        File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        Check(n == 3, $"캡처 저장: {Path.GetFullPath(SheetPath)} ({string.Join(", ", shots.Select(s => s.label))}) - 1920x1080 세 장을 세로로");
    }
}
