using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Monsters > Run Porcupine Test. 호저(PorcupineMonsterAI, 제자리 터렛)를 실제 마을(격자 3x4, 시드 1)의 가장 큰 방(Field)에 테스트로만 놓고 확인한다.
/// 스폰 목록에는 없으므로 다른 몬스터·동물은 끄고, 시간 소환(VillageMonsterSpawner)도 끈다. 프레임 시간 1/30초 고정.
/// 1) 가져오기·프리팹: 18장 Single/PPU 32/Point/압축 없음, Visual 배율 2, 그림자·발 정렬, 체력 3, 시체 무게 12(늑대·박쥐는 15 그대로)
/// 2) 시야 밖(사거리 밖, 벽 뒤)이면 쏘지 않고 idle 4장 반복
/// 3) 시야에 들어오면 raise-01~03(마지막 장 유지) → 0.8초 뒤 3발(플레이어 방향 기준 30도 부채꼴) → shoot-01~03 → idle
/// 4) 발사 간격 2.5초 / 5) 가시 피해 20(맞으면 사라짐)·속도 9·사거리 8 / 6) 벽에 막힘
/// 7) 기절·마취 중에는 예고·발사 안 함 / 8) 포획망·다트·지뢰·폭탄 피해와 기절·마취가 늑대와 같음
/// 9) 체력 3: 포획망 5번(0.6 x 5)에 쓰러지고, 쓰러진 뒤 안 쏨, E로 회수(무게 12)
/// 실제 렌더: Logs/Porcupine/porcupine_sheet.png (대기, 예고, 발사, 날아가는 가시, 피격, 쓰러짐과 그림자, Field에서 늑대·동물 옆)
/// 결과: Logs/PorcupineTest.txt / batchmode: -executeMethod PorcupineTest.RunBatch (통과면 0 아니면 1, 그림 때문에 -nographics 없이)
/// </summary>
[InitializeOnLoad]
public static class PorcupineTest
{
    private const string KeyActive = "PCT.Active", KeyDone = "PCT.Done", KeyBatch = "PCT.Batch", KeyPassed = "PCT.Passed";
    private const string ReportPath = "Logs/PorcupineTest.txt";
    private const string SheetPath = "Logs/Porcupine/porcupine_sheet.png";
    private const float FrameTime = 1f / 30f;
    private const float TimeTol = 0.02f; // 반 프레임 남짓

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();
    private static StringBuilder sb;
    private static List<string> failures;

    private static readonly FieldInfo CooldownField = typeof(PlayerWeaponController).GetField("netCooldownTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo DartPrefabField = typeof(PlayerWeaponController).GetField("dartPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MinePrefabField = typeof(PlayerGadgetController).GetField("minePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo SwingNetMethod = typeof(PlayerWeaponController).GetMethod("SwingNet", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo UseBombMethod = typeof(PlayerGadgetController).GetMethod("UseBomb", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo HealthSetter = typeof(GameManager).GetProperty(nameof(GameManager.Health)).GetSetMethod(true);
    private static readonly FieldInfo DeadColorField = typeof(MonsterHealth).GetField("deadColor", BindingFlags.Instance | BindingFlags.NonPublic);

    static PorcupineTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Monsters/Run Porcupine Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[PorcupineTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = FrameTime;
            SetUpInput();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetBool(KeyPassed, false) ? 0 : 1);
        }
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
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

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            Finish();
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            TearDown();
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    // 실제 마우스는 끄고(커서가 플레이어 시선을 덮어쓰지 않게) 가상 마우스·키보드를 쓴다.
    private static Mouse vmouse;
    private static Keyboard vkb;
    private static readonly List<InputDevice> disabledMice = new List<InputDevice>();
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static void SetUpInput()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        disabledMice.Clear();
        foreach (InputDevice d in InputSystem.devices.ToList())
            if (d is Mouse && d.enabled) { InputSystem.DisableDevice(d); disabledMice.Add(d); }
        vmouse = InputSystem.AddDevice<Mouse>("PorcupineTestMouse");
        vmouse.MakeCurrent();
        vkb = InputSystem.AddDevice<Keyboard>("PorcupineTestKeyboard");
        vkb.MakeCurrent();
    }

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        Time.captureDeltaTime = 0f;
        VillageMapGenerator.EditorLayoutOverride = null;
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        vmouse = null;
        vkb = null;
        foreach (InputDevice d in disabledMice) if (d != null && d.added) InputSystem.EnableDevice(d);
        disabledMice.Clear();
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static void Finish()
    {
        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[PorcupineTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject player;
    private static PlayerMovement movement;
    private static PlayerWeaponController weapon;
    private static PlayerGadgetController gadget;
    private static Rigidbody2D playerBody;
    private static GameObject porcupinePrefab, wolfPrefab, batPrefab;
    private static Tilemap fieldGround;
    private static readonly List<(string label, Texture2D tex)> panels = new List<(string, Texture2D)>();

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        panels.Clear();
        sb.AppendLine($"PorcupineTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.SelectClass(PlayerClass.Trapper);
        gm.CompleteStartingClassSelection();
        config = gm.Config;
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        SceneManager.LoadScene("VillageScene");
        yield return WaitForScene("VillageScene");
        VillageMapGenerator.EditorLayoutOverride = null;

        // 호저는 아직 스폰 목록에 없다 - 시간 소환을 끄고, 이미 있는 몬스터·동물은 꺼서 호저만 본다(마지막 Field 그림에서 다시 켠다).
        VillageMonsterSpawner spawner = gm.GetComponent<VillageMonsterSpawner>();
        if (spawner != null) spawner.enabled = false;
        List<GameObject> hidden = new List<GameObject>();
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) { a.gameObject.SetActive(false); hidden.Add(a.gameObject); }
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) { m.gameObject.SetActive(false); hidden.Add(m.gameObject); }

        player = GameObject.FindGameObjectWithTag("Player");
        movement = player.GetComponent<PlayerMovement>();
        weapon = player.GetComponent<PlayerWeaponController>();
        gadget = player.GetComponent<PlayerGadgetController>();
        playerBody = player.GetComponent<Rigidbody2D>();
        movement.enabled = false;
        porcupinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PorcupineAssetBuilder.PrefabPath);
        wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/BatMonster.prefab");

        sb.AppendLine($"설정: 시야 {config.porcupineSightRange}칸, 예고 {config.porcupineWindupDuration}초, 간격 {config.porcupineVolleyInterval}초, {config.porcupineQuillCount}발 {config.porcupineSpreadAngle}도, " +
                      $"가시 피해 {config.porcupineQuillDamage} 사거리 {config.porcupineQuillRange} 속도 {config.porcupineQuillSpeed}, 시체 무게 {config.porcupineCorpseWeight} / " +
                      $"플레이어(트래퍼) 최대 체력 {GameManager.MaxHealth}");

        // 1) 가져오기·프리팹
        yield return CheckImportAndPrefab();
        if (porcupinePrefab == null) { Finish(); yield break; }

        if (!FindLane(11f, out Vector2 laneStart, out Vector2 dir))
        {
            Check(false, "Field 방에서 11칸 곧은 빈 길을 찾지 못했다");
            Finish();
            yield break;
        }
        Vector2 perp = new Vector2(-dir.y, dir.x);
        yield return CheckCorpseWeights(laneStart + dir * 5f, perp);
        sb.AppendLine($"\n시험 자리: Field 방 {laneStart} 에서 {dir} 방향 11칸이 비어 있다");

        // 2~7) 한 마리로 시야·예고·발사·간격·피해·사거리·벽·기절·마취
        yield return CheckShooting(laneStart, dir, perp);

        // 8) 무기별 피해 = 늑대
        yield return CheckWeapons(laneStart, dir, perp);

        // 9) 체력 3, 쓰러짐, 회수
        yield return CheckDeathAndCorpse(laneStart, dir, perp);

        // 실제 렌더: Field에서 늑대·동물 옆
        yield return CaptureField(hidden);
        WriteSheet();

        Finish();
    }

    // ===================== 1. 가져오기·프리팹 =====================

    private static IEnumerator CheckImportAndPrefab()
    {
        sb.AppendLine("\n== 1. 그림 가져오기와 프리팹");
        string[] names =
        {
            "idle-01", "idle-02", "idle-03", "idle-04", "walk-01", "walk-02", "walk-03", "walk-04", "raise-01", "raise-02", "raise-03",
            "shoot-01", "shoot-02", "shoot-03", "hurt-01", "fallen-01", "quill-01", "quill-02",
        };
        int ok = 0;
        foreach (string n in names)
        {
            string path = $"{PorcupineAssetBuilder.FramesFolder}/{n}.png";
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (imp != null && s != null && imp.spriteImportMode == SpriteImportMode.Single && Mathf.Approximately(s.pixelsPerUnit, 32f) &&
                imp.filterMode == FilterMode.Point && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.mipmapEnabled &&
                Vector2.Distance(s.pivot, new Vector2(16f, 16f)) < 0.01f) ok++;
        }
        Check(ok == names.Length, $"18장 모두 Single, PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데 ({ok}/{names.Length})");

        Check(porcupinePrefab != null, $"호저 프리팹 {PorcupineAssetBuilder.PrefabPath}");
        if (porcupinePrefab == null) yield break;
        Transform visual = porcupinePrefab.transform.Find("Visual");
        Transform body = visual != null ? visual.Find("Body") : null;
        Transform shadow = visual != null ? visual.Find("Shadow") : null;
        PorcupineAssetBuilder.BodyMetrics m = PorcupineAssetBuilder.MeasureIdle();
        Check(porcupinePrefab.GetComponent<PorcupineMonsterAI>() != null && porcupinePrefab.GetComponent<NavMeshAgent>() != null &&
              porcupinePrefab.GetComponent<MonsterHealth>() != null && porcupinePrefab.GetComponent<MonsterCorpsePickup>() != null,
            "루트: PorcupineMonsterAI, NavMeshAgent, MonsterHealth, MonsterCorpsePickup");
        Check(visual != null && visual.localScale == new Vector3(2f, 2f, 1f) && body != null && visual.GetChild(0) == body && shadow != null,
            "Visual 배율 2 아래 Body(첫 자식)와 Shadow (박쥐와 같은 짜임)");
        SpriteRenderer shadowRenderer = shadow != null ? shadow.GetComponent<SpriteRenderer>() : null;
        Check(shadowRenderer != null && shadowRenderer.sprite != null && shadowRenderer.sprite.name == $"Shadow_{m.ShadowW}x{m.ShadowH}",
            $"그림자 {shadowRenderer?.sprite?.name} (idle 몸 폭 {m.Width}텍셀 x 0.75 = {m.ShadowW}, 높이 {m.ShadowH}), 아래 변 {shadow?.localPosition.y * 32f:0.#}텍셀 = 발 {m.FootTexels} + 1 − {m.ShadowH}");
        FootSortOrder foot = visual != null ? visual.GetComponent<FootSortOrder>() : null;
        SerializedObject f = foot != null ? new SerializedObject(foot) : null;
        Check(f != null && f.FindProperty("body").objectReferenceValue == body.GetComponent<SpriteRenderer>() &&
              Mathf.Approximately(f.FindProperty("footTexels").floatValue, m.FootTexels) && f.FindProperty("shadow").objectReferenceValue == shadowRenderer,
            $"FootSortOrder: 몸 = Body, 발 {m.FootTexels}텍셀(idle 보이는 맨 아랫줄), 그림자 = Shadow");
        SimpleFrameAnimator anim = body != null ? body.GetComponent<SimpleFrameAnimator>() : null;
        SerializedObject a = anim != null ? new SerializedObject(anim) : null;
        string Names(string prop) => a == null ? "" : string.Join(",", Enumerable.Range(0, a.FindProperty(prop).arraySize).Select(i => a.FindProperty(prop).GetArrayElementAtIndex(i).objectReferenceValue?.name));
        Check(a != null && Names("walkFrames") == "idle-01,idle-02,idle-03,idle-04" && Names("hurtFrames") == "hurt-01" && Names("deathFrames") == "hurt-01,fallen-01",
            $"그림 연결: 대기 {Names("walkFrames")} ({a?.FindProperty("frameRate").floatValue}fps), 피격 {Names("hurtFrames")}, 쓰러짐 {Names("deathFrames")}");

        MonsterHealth h = porcupinePrefab.GetComponent<MonsterHealth>();
        SerializedObject hs = new SerializedObject(h);
        Check(h.MaxHealth == 3 && !h.IgnoresStunAndSleep, $"최대 체력 {h.MaxHealth} (늑대 {wolfPrefab.GetComponent<MonsterHealth>().MaxHealth}), 기절·마취 통함");
        sb.AppendLine($"  (참고) 다트 맞힘 원: 땅 위치 + {hs.FindProperty("dartHitOffset").vector2Value} (가로는 바라보는 쪽으로 {m.CenterXTexels}텍셀 옮김), 반경 {hs.FindProperty("dartHitRadius").floatValue:0.###}칸");
        yield return WaitFrames(1);
    }

    // 시체 무게: 호저 12, 늑대·박쥐 15 그대로 (Awake 뒤의 실제 값 - 만들자마자 읽고 Start 전에 지운다)
    private static IEnumerator CheckCorpseWeights(Vector2 at, Vector2 perp)
    {
        GameObject p = Spawn(porcupinePrefab, at);
        GameObject w = Spawn(wolfPrefab, at + perp * 1.5f);
        GameObject b = Spawn(batPrefab, at - perp * 1.5f);
        int pw = p.GetComponent<MonsterCorpsePickup>().Weight, ww = w.GetComponent<MonsterCorpsePickup>().Weight, bw = b.GetComponent<MonsterCorpsePickup>().Weight;
        p.SetActive(false); w.SetActive(false); b.SetActive(false);
        Object.Destroy(p); Object.Destroy(w); Object.Destroy(b);
        Check(pw == 12 && ww == 15 && bw == 15, $"시체 무게: 호저 {pw}, 늑대 {ww}, 박쥐 {bw} (늑대·박쥐는 wolfCorpseWeight {config.wolfCorpseWeight} 그대로)");
        yield return WaitFrames(2);
    }

    // ===================== 2~7. 시야·발사 =====================

    private static IEnumerator CheckShooting(Vector2 laneStart, Vector2 dir, Vector2 perp)
    {
        PorcupineMonsterAI p = Spawn(porcupinePrefab, laneStart).GetComponent<PorcupineMonsterAI>();
        p.name = "TestPorcupine";
        float lookDeg = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg; // 플레이어는 호저 쪽을 본다
        PlacePlayer(laneStart + dir * 30f, lookDeg); // 처음엔 아주 멀리
        yield return WaitFrames(3);
        Vector2 muzzle = p.MuzzlePosition;

        // 2a) 사거리 밖
        sb.AppendLine("\n== 2. 시야 밖이면 쏘지 않는다 (idle 4장 반복)");
        PlacePlayer(muzzle + dir * (config.porcupineSightRange + 1f), lookDeg);
        HashSet<string> idleSeen = new HashSet<string>();
        for (int i = 0; i < 90; i++)
        {
            yield return WaitFrames(1);
            idleSeen.Add(SpriteName(p));
            if (i == 45) CaptureAround(p, "1 IDLE", 1.6f);
        }
        Check(p.VolleysFired == 0 && PorcupineQuill.Active.Count == 0 && p.CurrentPhase == PorcupineMonsterAI.Phase.Idle,
            $"사거리({config.porcupineSightRange}칸) 밖 {config.porcupineSightRange + 1f}칸, 3초: 예고·발사 없음 (발사 {p.VolleysFired}회)");
        Check(idleSeen.SetEquals(new[] { "idle-01", "idle-02", "idle-03", "idle-04" }), $"대기 그림 반복: {string.Join(",", idleSeen.OrderBy(x => x))}");

        // 2b) 벽 뒤
        GameObject wall = MakeWall(muzzle + dir * 2.5f, dir, 4f);
        PlacePlayer(muzzle + dir * 5f, lookDeg);
        for (int i = 0; i < 90; i++) yield return WaitFrames(1);
        Check(p.VolleysFired == 0 && PorcupineQuill.Active.Count == 0 && p.CurrentPhase == PorcupineMonsterAI.Phase.Idle,
            $"5칸 앞이지만 사이에 벽: 3초 동안 예고·발사 없음 (발사 {p.VolleysFired}회)");

        // 3) 시야에 들어오면 예고 0.8초 뒤 3발
        sb.AppendLine("\n== 3. 시야에 들어오면 가시를 세우고(예고) 0.8초 뒤 3발, 30도 부채꼴");
        RemoveWall(wall);
        ResetPlayerHealth();
        float opened = Time.time;
        List<string> seq = new List<string>();
        bool capturedRaise = false;
        int heldFrames = 0;
        for (int i = 0; i < 60 && p.VolleysFired == 0; i++)
        {
            yield return WaitFrames(1);
            string n = SpriteName(p);
            if (seq.Count == 0 || seq[seq.Count - 1] != n) seq.Add(n);
            if (n == "raise-03") heldFrames++;
            if (!capturedRaise && n == "raise-03" && heldFrames >= 5) { capturedRaise = true; CaptureAround(p, "2 WINDUP RAISE-03", 1.6f); }
        }
        float windup = p.LastFireTime - p.WindupStartTime;
        Check(p.WindupStartTime >= opened - 0.001f && p.WindupStartTime - opened <= FrameTime + 0.001f,
            $"벽을 치운 다음 프레임에 예고 시작 ({(p.WindupStartTime - opened) / FrameTime:0}프레임 뒤)");
        Check(Mathf.Abs(windup - config.porcupineWindupDuration) <= TimeTol, $"예고 시작 → 발사 {windup:F3}초 (기대 {config.porcupineWindupDuration}초)");
        List<PorcupineQuill> volley = PorcupineQuill.Active.ToList();
        Vector2 toPlayer = (Vector2)player.transform.position - p.MuzzlePosition;
        float[] angles = volley.Select(q => Vector2.SignedAngle(toPlayer, q.Direction)).OrderBy(x => x).ToArray();
        Check(volley.Count == config.porcupineQuillCount, $"한 번에 {volley.Count}발 (기대 {config.porcupineQuillCount})");
        Check(angles.Length == 3 && Mathf.Abs(angles[0] + 15f) < 0.5f && Mathf.Abs(angles[1]) < 0.5f && Mathf.Abs(angles[2] - 15f) < 0.5f,
            $"플레이어 방향 기준 각도 {string.Join(", ", angles.Select(x => x.ToString("F1")))}도 (기대 −15, 0, +15 = 30도 부채꼴)");
        Check(volley.All(q => Vector2.Distance(q.Origin, p.MuzzlePosition) < 0.01f), $"가시가 몸 가운데(땅 위치 + {p.MuzzlePosition - (Vector2)new Vector2(p.transform.position.x, p.transform.position.z)})에서 나간다");

        // 발사 뒤 그림과 날아가는 가시
        PorcupineQuill center = volley.OrderBy(q => Mathf.Abs(Vector2.SignedAngle(toPlayer, q.Direction))).FirstOrDefault();
        float lastTraveled = 0f, speedStep = -1f;
        for (int i = 0; i < 30; i++)
        {
            yield return WaitFrames(1);
            string n = SpriteName(p);
            if (seq[seq.Count - 1] != n) seq.Add(n);
            if (i == 0 && n == "shoot-02") CaptureAround(p, "3 SHOOT-02", 1.6f);
            if (i == 1 && panels.All(x => x.label != "3 SHOOT-02") && n == "shoot-02") CaptureAround(p, "3 SHOOT-02", 1.6f);
            if (i == 5) CaptureWide(p.MuzzlePosition, (Vector2)player.transform.position, "4 SPINES IN FLIGHT");
            if (center != null)
            {
                if (lastTraveled > 0f && speedStep < 0f) speedStep = center.Traveled - lastTraveled;
                lastTraveled = center.Traveled;
            }
        }
        // 예고를 시작한 그 프레임에는 그림 갱신 순서에 따라 아직 idle이 보일 수 있어 앞쪽 idle은 뺀다.
        List<string> core = seq.SkipWhile(x => x.StartsWith("idle-")).ToList();
        string seqText = string.Join(" → ", core);
        Check(core.Take(3).SequenceEqual(new[] { "raise-01", "raise-02", "raise-03" }) &&
              core.Skip(3).Take(3).SequenceEqual(new[] { "shoot-01", "shoot-02", "shoot-03" }) && core.Skip(6).Any() && core.Skip(6).All(x => x.StartsWith("idle-")),
            $"그림 순서: {seqText}");
        Check(heldFrames >= 10, $"raise-03을 {heldFrames}프레임({heldFrames * FrameTime:F2}초) 유지 = 예고");
        Check(panels.Any(x => x.label == "3 SHOOT-02"), "발사 프레임(또는 바로 다음 프레임)에 shoot-02");

        // 5) 피해·속도: 가운데 가시가 플레이어에 맞고 사라짐
        sb.AppendLine("\n== 5. 가시 피해·속도·사거리");
        Check(Mathf.Abs(speedStep - config.porcupineQuillSpeed * FrameTime) < 0.001f, $"한 프레임에 {speedStep:F3}칸 = 속도 {speedStep / FrameTime:F2} (기대 {config.porcupineQuillSpeed})");
        Check(center == null && lastTraveled > 5f - 0.45f - 0.35f && lastTraveled < 5f, $"가운데 가시가 5칸 앞 플레이어에 맞고 사라짐 (사라지기 전 {lastTraveled:F2}칸)");
        Check(GameManager.MaxHealth - gm.Health == config.porcupineQuillDamage,
            $"플레이어 체력 {GameManager.MaxHealth} → {gm.Health} (가운데 1발 = 피해 {config.porcupineQuillDamage}, 양옆 2발은 5칸에서 1.3칸 비켜 감)");

        // 4) 발사 간격 2.5초
        sb.AppendLine("\n== 4. 발사 간격");
        List<float> fireTimes = new List<float> { p.LastFireTime };
        int seen = p.VolleysFired;
        for (int i = 0; i < 200 && p.VolleysFired < 3; i++)
        {
            yield return WaitFrames(1);
            if (p.VolleysFired != seen) { seen = p.VolleysFired; fireTimes.Add(p.LastFireTime); ResetPlayerHealth(); }
        }
        float[] gaps = fireTimes.Zip(fireTimes.Skip(1), (x, y) => y - x).ToArray();
        Check(gaps.Length == 2 && gaps.All(g => Mathf.Abs(g - config.porcupineVolleyInterval) <= TimeTol),
            $"발사 사이 {string.Join(", ", gaps.Select(g => g.ToString("F3")))}초 (기대 {config.porcupineVolleyInterval}초)");

        // 5) 사거리: 쏘자마자 플레이어가 옆으로 비키면 가운데 가시는 8칸 날아가 사라진다
        sb.AppendLine("\n== 5. (이어서) 사거리");
        PlacePlayer(muzzle + dir * 5f + perp * 3f, lookDeg);
        PorcupineQuill free = PorcupineQuill.Active.OrderBy(q => Mathf.Abs(Vector2.SignedAngle(dir, q.Direction))).FirstOrDefault();
        Vector2 freeOrigin = free != null ? free.Origin : Vector2.zero, freeDir = free != null ? free.Direction : Vector2.zero;
        float freeTraveled = 0f;
        Vector2 freeLast = Vector2.zero;
        int hpBefore = gm.Health;
        for (int i = 0; i < 45 && free != null; i++)
        {
            freeTraveled = free.Traveled;
            freeLast = free.transform.position;
            yield return WaitFrames(1);
        }
        // 사라지기 직전 프레임까지 본 거리 + 마지막 한 프레임 이동(남은 거리만큼만 움직이고 사라진다) = 사거리
        float step = config.porcupineQuillSpeed * FrameTime;
        Check(free == null && freeTraveled < config.porcupineQuillRange && freeTraveled + step >= config.porcupineQuillRange - 0.001f &&
              Vector2.Distance(freeLast, freeOrigin + freeDir * freeTraveled) < 0.01f,
            $"아무것도 안 맞은 가시는 {freeTraveled:F3}칸까지 곧게 날아간 다음 프레임(남은 {config.porcupineQuillRange - freeTraveled:F3}칸)에 사라짐 = 사거리 {config.porcupineQuillRange}");
        Check(gm.Health == hpBefore, "비킨 플레이어는 맞지 않음");

        // 6) 벽에 막힘
        sb.AppendLine("\n== 6. 벽에 막힘");
        PlacePlayer(muzzle + dir * 5f, lookDeg);
        ResetPlayerHealth();
        int before = p.VolleysFired;
        for (int i = 0; i < 120 && p.VolleysFired == before; i++) yield return WaitFrames(1);
        List<PorcupineQuill> blocked = PorcupineQuill.Active.ToList();
        wall = MakeWall(p.MuzzlePosition + dir * 2.5f, dir, 4f);
        Dictionary<PorcupineQuill, float> lastT = blocked.ToDictionary(q => q, q => q.Traveled);
        for (int i = 0; i < 30 && blocked.Any(q => q != null); i++)
        {
            foreach (PorcupineQuill q in blocked) if (q != null) lastT[q] = q.Traveled;
            yield return WaitFrames(1);
        }
        Check(blocked.Count == 3 && blocked.All(q => q == null) && lastT.Values.All(t => t < 2.5f / Mathf.Cos(15f * Mathf.Deg2Rad)),
            $"2.5칸 앞 벽에 3발 모두 막혀 사라짐 (사라지기 전 {string.Join(", ", lastT.Values.Select(t => t.ToString("F2")))}칸)");
        Check(gm.Health == GameManager.MaxHealth, $"벽 뒤 플레이어 체력 그대로 ({gm.Health})");
        RemoveWall(wall);

        // 7) 기절·마취 중에는 쏘지 않는다
        sb.AppendLine("\n== 7. 기절·마취 중에는 예고·발사 안 함");
        for (int i = 0; i < 120 && p.CurrentPhase != PorcupineMonsterAI.Phase.Windup; i++) yield return WaitFrames(1);
        yield return WaitFrames(12);
        int v0 = p.VolleysFired;
        p.Health.Stun(1f);
        yield return WaitFrames(1);
        bool cancelled = p.CurrentPhase == PorcupineMonsterAI.Phase.Idle;
        bool firedWhileStunned = false;
        for (int i = 0; i < 28; i++) { yield return WaitFrames(1); if (p.VolleysFired != v0 || PorcupineQuill.Active.Count > 0) firedWhileStunned = true; }
        Check(cancelled && !firedWhileStunned, $"예고 0.4초째 기절(1초): 예고를 접고 기절 동안 발사 없음 (기절 그림 {SpriteName(p)})");
        float stunEnd = Time.time;
        for (int i = 0; i < 60 && p.VolleysFired == v0; i++) yield return WaitFrames(1);
        Check(p.VolleysFired == v0 + 1 && Mathf.Abs(p.LastFireTime - p.WindupStartTime - config.porcupineWindupDuration) <= TimeTol && p.WindupStartTime >= stunEnd - 0.001f,
            $"기절이 풀리면 처음부터 다시 예고 → {p.LastFireTime - p.WindupStartTime:F3}초 뒤 발사");
        ResetPlayerHealth();
        int v1 = p.VolleysFired;
        p.Health.PutToSleep(0f, 3f);
        bool firedWhileAsleep = false;
        for (int i = 0; i < 88; i++) { yield return WaitFrames(1); if (p.VolleysFired != v1) firedWhileAsleep = true; }
        Check(p.Health.IsAsleep && !firedWhileAsleep, $"발사 직후 마취 3초: 원래 다음 발사(2.5초)를 지나도록 발사 없음 (잠듦 {p.Health.IsAsleep})");
        for (int i = 0; i < 60 && p.VolleysFired == v1; i++) yield return WaitFrames(1);
        Check(p.VolleysFired == v1 + 1, "깨어나면 다시 예고 뒤 발사");

        Object.Destroy(p.gameObject);
        foreach (PorcupineQuill q in PorcupineQuill.Active.ToList()) Object.Destroy(q.gameObject);
        yield return WaitFrames(3);
        ResetPlayerHealth();
    }

    // ===================== 8. 무기별 피해 =====================

    private static IEnumerator CheckWeapons(Vector2 laneStart, Vector2 dir, Vector2 perp)
    {
        sb.AppendLine("\n== 8. 무기별 피해와 기절·마취 = 늑대와 같음 (두뇌는 꺼서 제자리, MonsterHealth만 본다)");
        float lookDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 at = laneStart + dir * 2f;

        // 포획망: 부채꼴 안에 둘 다
        (MonsterHealth ph, MonsterHealth wh) = SpawnPair(at + dir * 1.3f + perp * 0.5f, at + dir * 1.3f - perp * 0.5f);
        PlacePlayer(at, lookDeg);
        yield return WaitFrames(2);
        CooldownField.SetValue(weapon, 0f);
        SwingNetMethod.Invoke(weapon, null);
        yield return WaitFrames(1);
        Check(Same(ph, wh, config.netDamage) && ph.IsStunned && wh.IsStunned,
            $"포획망: 호저 −{ph.MaxHealth - ph.Health:0.##}, 늑대 −{wh.MaxHealth - wh.Health:0.##} (netDamage {config.netDamage}), 둘 다 기절");
        Destroy(ph, wh);
        yield return WaitFrames(3);

        // 마취총 다트: 플레이어 무기의 다트 프리팹으로 각자에게 쏜다
        (ph, wh) = SpawnPair(at + dir * 3f + perp * 1f, at + dir * 3f - perp * 1f);
        yield return WaitFrames(2);
        TranquilizerDart dartPrefab = (TranquilizerDart)DartPrefabField.GetValue(weapon);
        foreach (MonsterHealth target in new[] { ph, wh })
        {
            TranquilizerDart dart = Object.Instantiate(dartPrefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
            dart.Launch(target.DartHitCenter - at, 12f, config.tranquilizerRange, config.sleepDelay, config.sleepDuration, config.tranquilizerDamage);
        }
        for (int i = 0; i < 15; i++) yield return WaitFrames(1);
        bool sameDamage = Same(ph, wh, config.tranquilizerDamage);
        for (int i = 0; i < Mathf.CeilToInt(config.sleepDelay / FrameTime) + 2; i++) yield return WaitFrames(1);
        Check(sameDamage && ph.IsAsleep && wh.IsAsleep,
            $"다트: 호저 −{ph.MaxHealth - ph.Health:0.##}, 늑대 −{wh.MaxHealth - wh.Health:0.##} (tranquilizerDamage {config.tranquilizerDamage}), {config.sleepDelay}초 뒤 둘 다 잠듦");
        Destroy(ph, wh);
        yield return WaitFrames(3);

        // 지뢰: 각자 발밑에 플레이어 가젯의 지뢰 프리팹
        (ph, wh) = SpawnPair(at + dir * 3f + perp * 1.5f, at + dir * 3f - perp * 1.5f);
        yield return WaitFrames(2);
        MineTrap minePrefab = (MineTrap)MinePrefabField.GetValue(gadget);
        foreach (MonsterHealth target in new[] { ph, wh })
            Object.Instantiate(minePrefab, new Vector3(target.GamePosition.x, target.GamePosition.y, 0f), Quaternion.identity);
        yield return WaitFrames(3);
        Check(Same(ph, wh, config.mineDamage) && ph.IsStunned && wh.IsStunned,
            $"지뢰: 호저 −{ph.MaxHealth - ph.Health:0.##}, 늑대 −{wh.MaxHealth - wh.Health:0.##} (mineDamage {config.mineDamage}), 둘 다 기절");
        Destroy(ph, wh);
        yield return WaitFrames(3);

        // 폭탄: 플레이어 둘레 반경 안에 둘 다
        (ph, wh) = SpawnPair(at + dir * 1.5f + perp * 1f, at + dir * 1.5f - perp * 1f);
        PlacePlayer(at, lookDeg);
        yield return WaitFrames(2);
        if (!gm.TryAddBomb()) { int s0 = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type != InventoryItemType.Bomb); gm.SelectSlot(s0); gm.TryDropSelectedItem(out _); gm.TryAddBomb(); }
        gm.SelectSlot(Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Bomb));
        UseBombMethod.Invoke(gadget, null);
        yield return WaitFrames(1);
        Check(Same(ph, wh, config.bombDamage) && ph.IsDead && wh.IsStunned,
            $"폭탄: 호저 −{ph.MaxHealth - ph.Health:0.##}(체력 3이라 쓰러짐), 늑대 −{wh.MaxHealth - wh.Health:0.##} (bombDamage {config.bombDamage}, 늑대 기절)");
        Destroy(ph, wh);
        // 폭발 그림은 실제 시간(unscaledDeltaTime)으로 사라져서, 고정 프레임으로 빨리 도는 테스트에서는 오래 남는다 - 다음 캡처에 남지 않게 지운다.
        yield return WaitFrames(Mathf.CeilToInt(config.bombEffectDisplayDuration / FrameTime) + 15);
        foreach (FireBurstEffect2D fx in Object.FindObjectsByType<FireBurstEffect2D>(FindObjectsInactive.Include)) Object.Destroy(fx.gameObject);
        foreach (BombExplosionEffect fx in Object.FindObjectsByType<BombExplosionEffect>(FindObjectsInactive.Include)) Object.Destroy(fx.gameObject);
        yield return WaitFrames(2);
        gm.SelectWeapon();
    }

    // 피해가 같다: 둘 다 정확히 expected만큼 줄었다(호저가 그보다 체력이 적으면 0까지).
    private static bool Same(MonsterHealth porcupine, MonsterHealth wolf, float expected)
    {
        float pd = porcupine.MaxHealth - Mathf.Max(porcupine.Health, 0f), wd = wolf.MaxHealth - Mathf.Max(wolf.Health, 0f);
        return Mathf.Abs(wd - expected) < 0.001f && Mathf.Abs(pd - Mathf.Min(expected, porcupine.MaxHealth)) < 0.001f;
    }

    private static (MonsterHealth porcupine, MonsterHealth wolf) SpawnPair(Vector2 porcupineAt, Vector2 wolfAt)
    {
        GameObject p = Spawn(porcupinePrefab, porcupineAt);
        p.GetComponent<PorcupineMonsterAI>().enabled = false;
        GameObject w = Spawn(wolfPrefab, wolfAt);
        w.GetComponent<MonsterAI>().enabled = false;
        return (p.GetComponent<MonsterHealth>(), w.GetComponent<MonsterHealth>());
    }

    private static void Destroy(MonsterHealth a, MonsterHealth b)
    {
        if (a != null) Object.Destroy(a.gameObject);
        if (b != null) Object.Destroy(b.gameObject);
    }

    // ===================== 9. 체력·쓰러짐·회수 =====================

    private static IEnumerator CheckDeathAndCorpse(Vector2 laneStart, Vector2 dir, Vector2 perp)
    {
        sb.AppendLine("\n== 9. 체력 3: 포획망 5번에 쓰러짐, 쓰러진 뒤 안 쏨, E로 회수(무게 12)");
        float lookDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        PorcupineMonsterAI p = Spawn(porcupinePrefab, laneStart + dir * 3.3f).GetComponent<PorcupineMonsterAI>();
        p.name = "TestPorcupine";
        PlacePlayer(laneStart + dir * 2f, lookDeg);
        yield return WaitFrames(3);
        MonsterHealth h = p.Health;
        List<string> seq = new List<string>();
        float healthAfter4 = 0f;
        for (int k = 1; k <= 5; k++)
        {
            CooldownField.SetValue(weapon, 0f);
            SwingNetMethod.Invoke(weapon, null);
            yield return WaitFrames(1);
            if (k == 1) CaptureAround(p, "5 HURT", 1.6f);
            if (k == 4) healthAfter4 = h.Health;
            for (int i = 0; i < 8; i++) { string n = SpriteName(p); if (seq.Count == 0 || seq[seq.Count - 1] != n) seq.Add(n); yield return WaitFrames(1); }
            ResetPlayerHealth();
        }
        Check(Mathf.Abs(healthAfter4 - 0.6f) < 0.001f && h.IsDead && h.Health <= 0f, $"4번째 뒤 체력 {healthAfter4:0.##}, 5번째에 쓰러짐 (최대 {h.MaxHealth}, 포획망 {config.netDamage})");
        for (int i = 0; i < 10; i++) yield return WaitFrames(1);
        SpriteRenderer bodyRenderer = p.transform.Find("Visual/Body").GetComponent<SpriteRenderer>();
        Color deadColor = (Color)DeadColorField.GetValue(h);
        Check(seq.Contains("hurt-01") && SpriteName(p) == "fallen-01" && bodyRenderer.color == deadColor,
            $"피격 hurt-01, 쓰러짐 {SpriteName(p)} + 시체 색 {bodyRenderer.color} (그림 순서 {string.Join(" → ", seq)})");
        CaptureAround(p, "6 FALLEN SHADOW", 1.6f);
        int volleys = p.VolleysFired;
        PlacePlayer((Vector2)new Vector2(p.transform.position.x, p.transform.position.z) - dir * 4f, lookDeg);
        for (int i = 0; i < 90; i++) yield return WaitFrames(1);
        Check(p.VolleysFired == volleys && PorcupineQuill.Active.Count == 0, "쓰러진 뒤 3초 동안 플레이어가 4칸 앞에 있어도 발사 없음");

        // 회수: 1칸 앞에서 E
        PlacePlayer((Vector2)new Vector2(p.transform.position.x, p.transform.position.z) - dir * 1f, lookDeg);
        string id = p.name; // 씬 루트에 둔 시험용 호저라 계층 경로 Id = 이름
        yield return WaitFrames(3);
        yield return Press(Key.E);
        int slot = Enumerable.Range(0, gm.InventorySlotCount).FirstOrDefault(i => gm.GetSlot(i).Type == InventoryItemType.Animal && gm.GetSlot(i).AnimalKind == AnimalBehaviorKind.MonsterCorpse);
        InventorySlotData data = gm.GetSlot(slot);
        Check(data.Type == InventoryItemType.Animal && data.AnimalKind == AnimalBehaviorKind.MonsterCorpse && data.AnimalWeight == 12 && data.AnimalId == id && !p.gameObject.activeSelf,
            $"E로 회수: 인벤토리 {slot}번 칸 시체 무게 {data.AnimalWeight} (Id {data.AnimalId}), 필드에서 치워짐");
        if (data.Type == InventoryItemType.Animal) { gm.SelectSlot(slot); gm.TryDropSelectedItem(out _); }
        foreach (MonsterCorpsePickup c in Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Exclude)) Object.Destroy(c.gameObject); // 내려놓은 시체 정리
        Object.Destroy(p.gameObject);
        yield return WaitFrames(3);
        gm.SelectWeapon();
    }

    // ===================== 실제 렌더 =====================

    // Field 방에서 원래 있던 동물·늑대를 다시 켜고(늑대가 없으면 하나 만든다), 동물 옆에 호저를, 그 옆에 늑대를 둔다.
    private static IEnumerator CaptureField(List<GameObject> hidden)
    {
        sb.AppendLine("\n== 실제 렌더: Field에서 다른 동물·몬스터 옆");
        Transform fieldRoom = fieldGround.transform.parent;
        foreach (GameObject g in hidden) if (g != null && g.transform.IsChildOf(fieldRoom)) g.SetActive(true);
        yield return WaitFrames(2);
        AnimalRescue animal = fieldRoom.GetComponentsInChildren<AnimalRescue>(false).FirstOrDefault();
        MonsterAI wolf = fieldRoom.GetComponentsInChildren<MonsterAI>(false).FirstOrDefault();
        Vector2 animalAt = animal != null ? (Vector2)animal.transform.position : FieldCenter();
        Vector2 spot = FreeSpotNear(animalAt + Vector2.right * 1.6f);
        PorcupineMonsterAI p = Spawn(porcupinePrefab, spot).GetComponent<PorcupineMonsterAI>();
        p.name = "FieldPorcupine";
        if (wolf == null) wolf = Spawn(wolfPrefab, FreeSpotNear(spot + Vector2.right * 1.6f)).GetComponent<MonsterAI>();
        else
        {
            Vector2 w = FreeSpotNear(spot + Vector2.right * 1.6f);
            NavMeshAgent agent = wolf.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.Warp(new Vector3(w.x, 0f, w.y));
        }
        wolf.enabled = false; // 찍는 동안 제자리
        Vector2 wolfAt = new Vector2(wolf.transform.position.x, wolf.transform.position.z);
        // 두뇌를 껐으므로 그림(Visual)을 NavMesh 위치(x, z)에 맞춰 한 번 옮긴다(MonsterBrain.UpdateVisualPosition과 같은 매핑).
        Transform wolfVisual = wolf.transform.Find("Visual");
        if (wolfVisual != null) wolfVisual.position = new Vector3(wolfAt.x, wolfAt.y, 0f);
        if (animal != null) foreach (MonoBehaviour mb in animal.GetComponents<MonoBehaviour>()) if (!(mb is AnimalRescue)) mb.enabled = false;
        Vector2 mid = (animalAt + spot + wolfAt) / 3f;
        PlacePlayer(mid + Vector2.down * 2.6f, 90f); // 플레이어가 아래에서 위(무리 쪽)를 본다 - 원형 시야 안
        yield return WaitFrames(4);
        Check(animal != null, $"Field 방의 동물 {(animal != null ? animal.name : "없음")} 옆 {Vector2.Distance(animalAt, spot):F1}칸에 호저, 그 옆 {Vector2.Distance(spot, wolfAt):F1}칸에 늑대 {wolf.name}");
        Vector2 min = Vector2.Min(Vector2.Min(animalAt, spot), wolfAt) - new Vector2(1.6f, 1.4f);
        Vector2 max = Vector2.Max(Vector2.Max(animalAt, spot), wolfAt) + new Vector2(1.6f, 1.4f);
        Texture2D tex = Capture(min, max, true); // 다른 칸과 같이 이 렌더만 전역 조명 1(밤 어둠에 묻히지 않게)
        if (tex != null) panels.Add(("7 FIELD WOLF ANIMAL", tex));
        Object.Destroy(p.gameObject);
        yield return WaitFrames(2);
    }

    private static void CaptureAround(PorcupineMonsterAI p, string label, float half)
    {
        SpriteRenderer body = p.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
        Vector2 c = body != null ? (Vector2)body.bounds.center : p.MuzzlePosition;
        Texture2D tex = Capture(c - Vector2.one * half, c + Vector2.one * half, true);
        if (tex != null) panels.Add((label, tex));
    }

    private static void CaptureWide(Vector2 a, Vector2 b, string label)
    {
        Vector2 min = Vector2.Min(a, b) - Vector2.one * 1.4f, max = Vector2.Max(a, b) + Vector2.one * 1.4f;
        Vector2 center = (min + max) * 0.5f;
        float half = Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f;
        Texture2D tex = Capture(center - new Vector2(half, half * 0.6f), center + new Vector2(half, half * 0.6f), true);
        if (tex != null) panels.Add((label, tex));
    }

    // 게임 카메라(Camera.main)를 RenderTexture에 한 번 더 그려 잘라 낸다(박쥐 테스트와 같은 방식). boostLight면 이 렌더 한 번만 전역 조명을 1로 올린다(게임 동작과 무관).
    private static Texture2D Capture(Vector2 worldMin, Vector2 worldMax, bool boostLight)
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        Light2D[] globals = boostLight ? Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.lightType == Light2D.LightType.Global).ToArray() : new Light2D[0];
        float[] saved = globals.Select(l => l.intensity).ToArray();
        foreach (Light2D l in globals) l.intensity = 1f;
        const int W = 1920, H = 1080;
        RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture prevTarget = cam.targetTexture, prevActive = RenderTexture.active;
        // 자를 곳이 화면 밖에 있지 않게 이 렌더 한 번만 카메라를 가운데로 옮긴다(CameraFollow가 다음 프레임에 되돌린다 - 여기서도 바로 되돌림).
        Vector3 camPos = cam.transform.position;
        Vector2 cropCenter = (worldMin + worldMax) * 0.5f;
        cam.transform.position = new Vector3(cropCenter.x, cropCenter.y, camPos.z);
        cam.targetTexture = rt;
        cam.Render();
        Vector3 lo = cam.WorldToViewportPoint(new Vector3(worldMin.x, worldMin.y, 0f));
        Vector3 hi = cam.WorldToViewportPoint(new Vector3(worldMax.x, worldMax.y, 0f));
        int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.x, hi.x) * W), 0, W - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(lo.x, hi.x) * W), x0 + 1, W);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.y, hi.y) * H), 0, H - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(lo.y, hi.y) * H), y0 + 1, H);
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(x0, y0, x1 - x0, y1 - y0), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = prevTarget;
        cam.transform.position = camPos;
        Object.DestroyImmediate(rt);
        for (int i = 0; i < globals.Length; i++) globals[i].intensity = saved[i];
        return tex;
    }

    // 패널을 가로로 늘어놓고(원래 크기) 위에 이름을 쓴다.
    private static void WriteSheet()
    {
        const int gap = 10, labelH = 40;
        List<(string label, Texture2D tex)> list = panels.Where(p => p.tex != null).ToList();
        string[] expected = { "1 IDLE", "2 WINDUP RAISE-03", "3 SHOOT-02", "4 SPINES IN FLIGHT", "5 HURT", "6 FALLEN SHADOW", "7 FIELD WOLF ANIMAL" };
        list = expected.Select(e => list.FirstOrDefault(p => p.label == e)).Where(p => p.tex != null).ToList();
        int w = gap, h = 0;
        foreach (var p in list) { w += p.tex.width + gap; h = Mathf.Max(h, p.tex.height); }
        int totalH = h + labelH + gap * 2;
        Texture2D outTex = new Texture2D(Mathf.Max(w, 1), totalH, TextureFormat.RGB24, false);
        Color bg = new Color(0.12f, 0.12f, 0.14f);
        GridMapAuditImages.FillRect(outTex, 0, 0, outTex.width, totalH, bg);
        int x = gap;
        foreach ((string label, Texture2D t) in list)
        {
            outTex.SetPixels(x, gap, t.width, t.height, t.GetPixels());
            GridMapAuditImages.DrawText(outTex, x, totalH - 6, label, 4, Color.white, bg);
            x += t.width + gap;
        }
        outTex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
        File.WriteAllBytes(SheetPath, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);
        Check(list.Count == expected.Length, $"실제 렌더 {list.Count}/{expected.Length}칸: {Path.GetFullPath(SheetPath)} ({string.Join(", ", list.Select(p => p.label))})");
        foreach (var p in panels) if (p.tex != null) Object.DestroyImmediate(p.tex);
        panels.Clear();
    }

    // ===================== 도우미 =====================

    private static string SpriteName(PorcupineMonsterAI p)
    {
        SpriteRenderer r = p.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
        return r != null && r.sprite != null ? r.sprite.name : "";
    }

    private static GameObject Spawn(GameObject prefab, Vector2 at)
    {
        Vector3 pos = NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position : new Vector3(at.x, 0f, at.y);
        return Object.Instantiate(prefab, pos, Quaternion.identity);
    }

    private static void PlacePlayer(Vector2 at, float deg)
    {
        LookSetter.Invoke(movement, new object[] { new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) });
        playerBody.position = at;
        playerBody.rotation = deg;
        player.transform.SetPositionAndRotation(new Vector3(at.x, at.y, player.transform.position.z), Quaternion.Euler(0f, 0f, deg));
        playerBody.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    private static void ResetPlayerHealth() => HealthSetter.Invoke(gm, new object[] { GameManager.MaxHealth });

    // 시험용 벽: 리지드바디 없는 고정 BoxCollider2D(StaticBlockerLine이 벽으로 본다). dir에 수직으로 length칸.
    private static GameObject MakeWall(Vector2 at, Vector2 dir, float length)
    {
        GameObject wall = new GameObject("PorcupineTestWall");
        wall.transform.SetPositionAndRotation(new Vector3(at.x, at.y, 0f), Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg));
        BoxCollider2D box = wall.AddComponent<BoxCollider2D>();
        box.size = new Vector2(0.4f, length);
        Physics2D.SyncTransforms();
        return wall;
    }

    private static void RemoveWall(GameObject wall)
    {
        if (wall == null) return;
        wall.SetActive(false);
        Object.Destroy(wall);
        Physics2D.SyncTransforms();
    }

    private static Vector2 FieldCenter()
    {
        Vector3 c = fieldGround.transform.TransformPoint(fieldGround.localBounds.center);
        return new Vector2(Mathf.Round(c.x) + 0.5f, Mathf.Round(c.y) + 0.5f);
    }

    private static Vector2 FreeSpotNear(Vector2 want)
    {
        Vector2 best = want;
        float bestD = float.MaxValue;
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                Vector2 p = new Vector2(Mathf.Floor(want.x) + 0.5f + dx, Mathf.Floor(want.y) + 0.5f + dy);
                if (!NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out _, 0.1f, NavMesh.AllAreas)) continue;
                if (Physics2D.OverlapBoxAll(p, new Vector2(1.2f, 1.2f), 0f).Any(col => !col.isTrigger)) continue;
                float d = Vector2.Distance(p, want);
                if (d < bestD) { bestD = d; best = p; }
            }
        return best;
    }

    // 가장 큰 방(Field)에서 트럭과 떨어진, length칸 곧은 빈 길(바닥·NavMesh 위, 폭 3칸 안에 막는 충돌체 없음)을 방 가운데에 가깝게 찾는다.
    private static bool FindLane(float length, out Vector2 start, out Vector2 dir)
    {
        start = Vector2.zero;
        dir = Vector2.right;
        fieldGround = null;
        foreach (Transform room in GameObject.Find("GeneratedMap").transform)
        {
            Tilemap g = room.Find("Ground")?.GetComponent<Tilemap>();
            if (g == null || !room.name.StartsWith("Room_")) continue;
            g.CompressBounds();
            if (fieldGround == null || g.cellBounds.size.x * g.cellBounds.size.y > fieldGround.cellBounds.size.x * fieldGround.cellBounds.size.y) fieldGround = g;
        }
        if (fieldGround == null) return false;
        Vector2 roomCenter = FieldCenter();
        TruckPoint truck = Object.FindAnyObjectByType<TruckPoint>();
        Vector2 truckAt = truck != null ? (Vector2)truck.transform.position : new Vector2(float.MaxValue, float.MaxValue);
        Vector2[] dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
        float bestD = float.MaxValue;
        bool found = false;
        for (int dy = -10; dy <= 10; dy++)
            for (int dx = -14; dx <= 14; dx++)
            {
                Vector2 p = roomCenter + new Vector2(dx, dy);
                if (Vector2.Distance(p, truckAt) < 10f) continue;
                foreach (Vector2 d in dirs)
                {
                    bool ok = true;
                    for (float k = -1f; k <= length && ok; k += 0.5f)
                    {
                        Vector2 q = p + d * k;
                        if (!NavMesh.SamplePosition(new Vector3(q.x, 0f, q.y), out _, 0.1f, NavMesh.AllAreas)) ok = false;
                        else if (!fieldGround.HasTile(fieldGround.WorldToCell(new Vector3(q.x, q.y, 0f)))) ok = false;
                    }
                    if (!ok) continue;
                    Vector2 mid = p + d * (length * 0.5f - 0.5f);
                    float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    if (Physics2D.OverlapBoxAll(mid, new Vector2(length + 3f, 3f), angle).Any(col => !col.isTrigger && !col.CompareTag("Player"))) continue;
                    float dist = Vector2.Distance(p + d * length * 0.5f, roomCenter);
                    if (dist < bestD) { bestD = dist; start = p; dir = d; found = true; }
                }
            }
        return found;
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 20f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
