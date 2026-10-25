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
/// Tools > Monsters > Run Bat Monster Test. 박쥐(BatMonsterAI, 예전 우는천사 자리)를 실제 마을(격자 3x4, 시드 1)과 실제 플레이어 시야(PlayerVision.IsPointVisible)로 확인한다.
/// 플레이어를 곧게 트인 길 위에 세우고 부채꼴 방향을 정한 뒤, 박쥐를 원하는 거리에 직접 놓아 동작을 잰다.
/// 부채꼴 방향: 실제 마우스는 테스트 동안 끄고 가상 마우스를 그 방향 화면 위치에 둔다 - 마우스 커서가 어디 있든 결과가 같다.
/// 검사: 프리팹·그림·설정, 원형 주변 시야에서는 멈추지 않고 다가옴, 정면 부채꼴 시야에 들어오면 멈춤(굳음)·웅크림 3프레임 재생 후 마지막 프레임 유지,
/// 시야에서 벗어나면 웅크림을 거꾸로 재생한 뒤 다시 비행, 벽 뒤는 시야 밖, 15칸 안 추격·밖 배회, 배회 중에도 정면 시야 안이면 멈춤,
/// 접촉 데미지(최대 체력 x 0.8, 굳은 동안 없음), 무기 데미지 = 늑대와 같은 값(기절·마취 없음), 맞으면 도망(거리 = 거위 x 0.5, 속도 = 거위),
/// 도망 중 쳐다봐도 안 멈춤, 도망 중 또 맞으면 그 자리에서 다시 도망, 무적 없음, 도망 뒤 다시 다가옴, 체력 0이면 쓰러지고 E로 회수,
/// 시간 소환(180초에 1마리), 재진입, ResetDay/ResetGame. 프레임 시간 1/30초 고정. 다른 몬스터는 기절시켜 둔다(박쥐는 기절하지 않는다).
/// 실제 게임 카메라 렌더: Logs/Bat/render/crouch_*.png, view_*.png + manifest.txt(비행 → 웅크림 → 굳음 → 풀림, 주변/정면 시야 장면).
/// 결과: Logs/BatMonsterTest.txt / batchmode: -executeMethod BatMonsterTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class BatMonsterTest
{
    private const string KeyActive = "BMT.Active", KeyDone = "BMT.Done", KeyBatch = "BMT.Batch", KeyPassed = "BMT.Passed";
    private const string ReportPath = "Logs/BatMonsterTest.txt";
    private const float FrameTime = 1f / 30f;
    private const float StepPerFrame = 8.4f * FrameTime; // 한 프레임 돌진 거리(시작값 기준)

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();
    private static bool holdTime;
    private static Vector2 lookDirection = Vector2.up;
    private static bool forceLook;

    private static Mouse vmouse;
    private static Keyboard vkb;
    private static readonly List<InputDevice> disabledMice = new List<InputDevice>();
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject batPrefab;
    private static Transform player;
    private static PlayerVision vision;
    private static Light2D ambient, focused;

    static BatMonsterTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Monsters/Run Bat Monster Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BatMonsterTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            holdTime = false;
            forceLook = false;
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

    // 실제 마우스는 끄고(커서 위치가 부채꼴 방향을 덮어쓰지 않게) 가상 마우스·키보드를 쓴다.
    private static void SetUpInput()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        disabledMice.Clear();
        foreach (InputDevice d in InputSystem.devices.ToList())
            if (d is Mouse && d.enabled) { InputSystem.DisableDevice(d); disabledMice.Add(d); }
        vmouse = InputSystem.AddDevice<Mouse>("BatMonsterTestMouse");
        vmouse.MakeCurrent();
        vkb = InputSystem.AddDevice<Keyboard>("BatMonsterTestKeyboard");
        vkb.MakeCurrent();
    }

    private static readonly FieldInfo GmConfigField = typeof(GameManager).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance);
    private static GameBalanceConfig originalConfig;

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        if (gm != null && originalConfig != null) GmConfigField.SetValue(gm, originalConfig);
        originalConfig = null;
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
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
        if (routine == null || !EditorApplication.isPlaying)
        {
            if (routine != null) Debug.LogWarning("[BatMonsterTest] 테스트가 끝나기 전에 Play 모드가 꺼졌다");
            EditorApplication.update -= Step;
            nested.Clear();
            TearDown();
            return;
        }
        foreach (MonsterHealth m in MonsterHealth.Active.ToArray()) if (m != null && !m.IsDead) m.Stun(1000f); // 박쥐는 기절하지 않는다
        if (holdTime && gm != null) SetTime(0f);
        if (forceLook && player != null)
        {
            LookSetter?.Invoke(player.GetComponent<PlayerMovement>(), new object[] { lookDirection });
            AimVirtualMouse();
        }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb?.ToString() ?? e.ToString(), new UTF8Encoding(false));
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

    // 가상 마우스를 플레이어에서 lookDirection 쪽 3칸 앞 화면 위치에 둔다 - PlayerMovement가 읽어도 같은 방향이 나온다.
    private static void AimVirtualMouse()
    {
        Camera cam = Camera.main;
        if (vmouse == null || cam == null) return;
        Vector3 world = player.position + (Vector3)(lookDirection.normalized * 3f);
        Vector3 screen = cam.WorldToScreenPoint(world);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = new Vector2(screen.x, screen.y) });
    }

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        Debug.Log($"[BatMonsterTest] [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"BatMonsterTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        gm = GameManager.Instance;
        config = gm != null ? gm.Config : null;
        // 임시 박쥐 증가(1구간 항목, 2026-10-24)는 이 테스트가 보는 기본 일정(2구간 180초에 1마리)과 다르다 - 1구간을 비운 복제 설정으로 돈다.
        if (config != null && config.GetVillagePhaseSpawns(1).Length > 0)
        {
            originalConfig = config;
            config = Object.Instantiate(config);
            config.name = "BatMonsterTestConfig";
            config.villagePhaseSpawns[0] = new GameBalanceConfig.VillagePhaseSpawnList();
            GmConfigField.SetValue(gm, config);
            sb.AppendLine("(참고) 1구간 임시 박쥐 항목을 뺀 복제 설정으로 검사한다");
        }
        batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BatMonsterAssetBuilder.PrefabPath);
        GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        if (gm == null || config == null || batPrefab == null || wolfPrefab == null)
        {
            Check(false, $"준비 실패: GameManager {gm != null}, config {config != null}, 프리팹 {batPrefab != null}, 늑대 {wolfPrefab != null}");
            Finish();
            yield break;
        }

        // --- 0. 프리팹·그림·설정
        sb.AppendLine("\n== 0. 프리팹, 그림, 설정");
        CheckPrefab(wolfPrefab);

        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        RestartClass();
        yield return Load("TruckScene");

        // --- 동작 검사용 마을: 격자 3x4 시드 1, 시간은 0에 붙잡아 시간 소환이 끼어들지 않게 한다.
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        holdTime = true;
        yield return Load("VillageScene");
        VillageMapGenerator.EditorLayoutOverride = null;
        player = GameObject.FindGameObjectWithTag("Player").transform;
        vision = PlayerVision.Current;
        ambient = (Light2D)typeof(PlayerVision).GetField("ambientVisionLight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vision);
        focused = (Light2D)typeof(PlayerVision).GetField("focusedVisionLight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vision);
        float ambientR = ambient.pointLightOuterRadius, focusedR = focused.pointLightOuterRadius;
        sb.AppendLine($"\n시야: 원형 반경 {ambientR:F2}(그림자 {ambient.shadowsEnabled}), 부채꼴 반경 {focusedR:F2} 각도 {focused.pointLightOuterAngle:F1}(그림자 {focused.shadowsEnabled})");

        float behind = ambientR + 6f, front = focusedR + 3f;
        if (!FindLane(behind, front, out Vector2 laneCenter, out Vector2 dir))
        {
            Check(false, $"뒤 {behind:F1}칸 + 앞 {front:F1}칸이 곧게 트인 길을 찾지 못했다");
            Finish();
            yield break;
        }
        sb.AppendLine($"길: 중심 {laneCenter}, 앞 방향 {dir}");
        lookDirection = dir;
        forceLook = true;
        MovePlayer(laneCenter);
        yield return WaitFrames(5);
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        Check(Vector2.Angle(focused.transform.up, dir) < 1f && Vector2.Angle(movement.LookDirection, dir) < 1f,
            $"부채꼴이 앞 방향을 본다 (부채꼴 {focused.transform.up}, LookDirection {movement.LookDirection}) - 실제 마우스 위치와 무관(가상 마우스)");
        int startHealth = gm.Health;

        // --- 1. 원형 주변 시야는 통과: 뒤에서 날아와 원형 시야 안으로 들어와도 멈추지 않고 계속 다가온다
        sb.AppendLine("\n== 1. 뒤쪽(원형 주변 시야만)에서는 멈추지 않고 플레이어 가까이까지 다가옴");
        captureManifest.Clear();
        BatMonsterAI a = SpawnBat(laneCenter - dir * behind);
        float startDistance = Dist(a);
        float nearStop = Mathf.Max(config.batCatchRange + 0.5f, ambientR * 0.6f); // 접촉(데미지) 전에 멈춰 세운다
        bool enteredAmbient = false, slowedInAmbient = false, froze = false, wrapped = false, ambientShot = false;
        float lastDistance = startDistance;
        int frames = 0;
        while (Dist(a) > nearStop && frames < 300)
        {
            yield return WaitFrames(1);
            frames++;
            bool inAmbientOnly = vision.IsPointVisible(Pos(a)) && !vision.IsPointInFocusedVision(Pos(a));
            if (inAmbientOnly)
            {
                if (enteredAmbient && Dist(a) > lastDistance - 0.05f) slowedInAmbient = true;
                enteredAmbient = true;
                if (!ambientShot && Dist(a) <= ambientR * 0.75f)
                {
                    ambientShot = true;
                    CaptureWide("ambient", $"주변 시야(원형 {ambientR:F1}칸) 안 {Dist(a):F1}칸 - 멈추지 않고 다가옴", Pos(a));
                }
            }
            if (a.IsFrozen || a.IsHolding) froze = true;
            if (a.CurrentPose != BatMonsterAI.Pose.Flying) wrapped = true;
            lastDistance = Dist(a);
        }
        Check(enteredAmbient && !slowedInAmbient && !froze && !wrapped && Dist(a) <= nearStop + 0.05f,
            $"원형 주변 시야 안에서도 멈추지 않고 다가왔다 ({startDistance:F2} → {Dist(a):F2}칸, 원형 반경 {ambientR:F2}, 굳음 {froze}, 웅크림 {wrapped}, {frames}프레임)");
        Check(!a.IsSurprised && a.CurrentPhase == BatMonsterAI.Phase.Ambush, $"주변 시야만으로는 놀람도 일어나지 않는다 (단계 {a.CurrentPhase})");
        Check(gm.Health == startHealth, $"접촉 전에 세웠다(체력 {gm.Health})");
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);

        // --- 2. 정면 부채꼴 시야: 들어온 순간 멈추고, 웅크림 3프레임 재생 뒤 마지막 프레임으로 굳는다
        sb.AppendLine("\n== 2. 놀람 전 돌진 → 정면 시야에 들어온 자리에서 바로 멈춤, 웅크림 wrap-01~03 재생 후 wrap-03 유지");
        a = SpawnBat(laneCenter + dir * front);
        bool flyShot = false;
        frames = 0;
        while (!a.IsSurprised && frames < 300)
        {
            yield return WaitFrames(1);
            frames++;
            if (!flyShot && !a.IsSurprised && Dist(a) <= focusedR + 1.2f && SpriteName(a).StartsWith("fly-"))
            {
                flyShot = true;
                CaptureBat(a, "비행 (정면 시야 밖)");
            }
        }
        float stop2 = Dist(a);
        Check(a.IsSurprised && a.IsFrozen && stop2 <= focusedR + 0.05f && stop2 >= focusedR - StepPerFrame - 0.1f && stop2 > ambientR + 0.5f,
            $"부채꼴 반경에서 멈춤 (멈춘 거리 {stop2:F2}, 부채꼴 반경 {focusedR:F2}, 원형 반경 {ambientR:F2}, {frames}프레임)");
        float wrapRate = WrapFrameRate(a);
        List<string> wrapSeq = new List<string> { SpriteName(a) };
        CaptureBat(a, $"{SpriteName(a)} (정면 시야에 들어온 순간)");
        int lastWrapFrame = -1;
        Vector2 held = Pos(a);
        float maxDrift = 0f;
        for (int i = 1; i <= 45; i++)
        {
            yield return WaitFrames(1);
            maxDrift = Mathf.Max(maxDrift, (Pos(a) - held).magnitude);
            string n = SpriteName(a);
            if (n != wrapSeq[wrapSeq.Count - 1])
            {
                wrapSeq.Add(n);
                CaptureBat(a, n);
            }
            if (lastWrapFrame < 0 && n == "wrap-03") lastWrapFrame = i;
        }
        CaptureBat(a, "굳음 (1.5초 뒤에도 wrap-03)");
        CaptureWide("focused", $"정면 시야(부채꼴 {focusedR:F1}칸) 경계 {Dist(a):F1}칸 - 멈춰 웅크림", Pos(a));
        float wrapSeconds = lastWrapFrame * FrameTime, expectWrap = 2f / wrapRate;
        Check(string.Join(",", wrapSeq) == "wrap-01,wrap-02,wrap-03", $"웅크림 그림 순서 {string.Join(" → ", wrapSeq)} (기대 wrap-01 → wrap-02 → wrap-03)");
        Check(lastWrapFrame > 0 && Mathf.Abs(wrapSeconds - expectWrap) <= FrameTime * 1.5f,
            $"웅크림 재생 속도 {wrapRate} fps: wrap-01 → wrap-03까지 {wrapSeconds:F3}초 (기대 2프레임 / {wrapRate} fps = {expectWrap:F3}초)");
        Check(maxDrift < 0.02f && a.CurrentPhase == BatMonsterAI.Phase.Chase && a.IsFrozen && a.CurrentPose == BatMonsterAI.Pose.Wrapped && SpriteName(a) == "wrap-03",
            $"들어온 순간부터 멈춰 있고(1.5초 동안 {maxDrift:F3}칸) 마지막 프레임 wrap-03으로 굳어 있다 (단계 {a.CurrentPhase}, 자세 {a.CurrentPose}, 그림 {SpriteName(a)})");
        Check(gm.Health == startHealth && a.WarningCount == 0, $"멈춘 동안 데미지 없음(체력 {gm.Health}), 경고음 없음({a.WarningCount})");

        // --- 3. 시야에서 벗어나면 웅크림을 거꾸로 풀고 비행으로 돌아가 다시 다가온다(주변 시야에서는 멈추지 않음), 다시 쳐다보면 또 웅크린다
        sb.AppendLine("\n== 3. 정면 시야 밖 → 바로 다시 추격, 그림은 웅크림 거꾸로(wrap-03 → 02 → 01) 재생 뒤 비행, 다시 쳐다보면 또 웅크림");
        lookDirection = -dir; // 뒤돌아본다 - 박쥐가 플레이어 뒤(부채꼴 밖, 원형 밖)가 된다
        // 부채꼴 방향은 LateUpdate에서 돌므로 바로 다음 프레임까지는 아직 보인다 - 시야 밖이 된 프레임부터 잰다(그 프레임에 풀림이 시작된다).
        for (int i = 0; i < 5 && a.IsFrozen; i++) yield return WaitFrames(1);
        held = Pos(a);
        float unwrapStartDist = Dist(a);
        List<string> unwrapSeq = new List<string> { SpriteName(a) };
        bool freeWhileUnwrap = true;
        float unwrapDrift = 0f;
        int flyFrame = -1, movedFrame = -1;
        for (int i = 1; i <= 40; i++)
        {
            yield return WaitFrames(1);
            string n = SpriteName(a);
            if (i == 1) CaptureBat(a, $"풀림 시작 {n} (정면 시야 밖)");
            if (n.StartsWith("fly-")) { flyFrame = i; CaptureBat(a, $"다시 비행 {n}"); break; }
            unwrapDrift = Mathf.Max(unwrapDrift, (Pos(a) - held).magnitude);
            if (movedFrame < 0 && unwrapDrift > 0.05f) movedFrame = i;
            if (a.IsFrozen || a.IsHolding || a.CurrentPose != BatMonsterAI.Pose.Unwrapping) freeWhileUnwrap = false;
            if (n != unwrapSeq[unwrapSeq.Count - 1]) { unwrapSeq.Add(n); CaptureBat(a, $"풀림 {n}"); }
        }
        float unwrapSeconds = (flyFrame + 1) * FrameTime, expectUnwrap = 3f / wrapRate; // 시야 밖이 된 프레임(풀림 시작)을 포함
        Check(string.Join(",", unwrapSeq) == "wrap-03,wrap-02,wrap-01", $"풀림 그림 순서 {string.Join(" → ", unwrapSeq)} → 비행 (기대 wrap-03 → wrap-02 → wrap-01)");
        Check(flyFrame > 0 && Mathf.Abs(unwrapSeconds - expectUnwrap) <= FrameTime * 1.5f,
            $"풀림 {unwrapSeconds:F3}초 뒤 비행 그림 (기대 3프레임 / {wrapRate} fps = {expectUnwrap:F3}초)");
        Check(freeWhileUnwrap && movedFrame > 0 && movedFrame <= 3 && unwrapDrift > 0.5f && Dist(a) < unwrapStartDist - 0.5f,
            $"풀리는 동안 바로 움직인다 ({movedFrame}프레임째 출발, 풀림 그림이 끝날 때까지 {unwrapDrift:F2}칸 이동, 거리 {unwrapStartDist:F2} → {Dist(a):F2}칸)");
        float d3 = Dist(a);
        frames = 0;
        bool froze3 = false, enteredAmbient3 = false, flyFrames = true;
        while (Dist(a) > nearStop && frames < 150)
        {
            yield return WaitFrames(1);
            frames++;
            if (a.IsHolding) froze3 = true;
            if (vision.IsPointVisible(Pos(a)) && !vision.IsPointInFocusedVision(Pos(a))) enteredAmbient3 = true;
            if (!SpriteName(a).StartsWith("fly-")) flyFrames = false;
        }
        Check(a.CurrentPhase == BatMonsterAI.Phase.Chase && !froze3 && enteredAmbient3 && Dist(a) <= nearStop + 0.05f,
            $"놀람 뒤 추격도 주변 시야에서는 멈추지 않는다 ({d3:F2} → {Dist(a):F2}칸, 단계 {a.CurrentPhase}, {frames}프레임)");
        Check(flyFrames, "다가오는 동안 비행 그림(fly-01~06)");
        Check(a.WarningCount >= 1, $"추격하며 움직이는 동안 경고음 {a.WarningCount}회(소리 파일 없음 - 에러 없이 건너뜀)");
        lookDirection = dir; // 다시 돌아서서 쳐다본다
        frames = 0;
        while (!a.IsFrozen && frames < 10) { yield return WaitFrames(1); frames++; }
        Check(a.IsFrozen && SpriteName(a) == "wrap-01", $"다시 정면 시야에 들어오자 바로 멈추고 웅크림 시작 ({frames}프레임, 그림 {SpriteName(a)})");
        int warnAfterChase = a.WarningCount;
        held = Pos(a);
        yield return WaitFrames(60);
        Check(a.WarningCount == warnAfterChase && (Pos(a) - held).magnitude < 0.02f && SpriteName(a) == "wrap-03",
            $"정면 시야 안에서 멈춘 동안 경고음 없음, 움직임 없음, wrap-03 유지 (그림 {SpriteName(a)})");
        Check(gm.Health == startHealth, $"여기까지 데미지 없음(체력 {gm.Health})");
        WriteCaptureManifest();

        // --- 4. 벽에 가려지면 시야 밖
        sb.AppendLine("\n== 4. 부채꼴 안이어도 벽 뒤는 시야 밖 → 계속 움직임");
        Object.Destroy(a.gameObject);
        lookDirection = dir;
        MovePlayer(laneCenter);
        yield return WaitFrames(3);
        float wallAt = (ambientR + focusedR) * 0.5f;
        GameObject wall = Wall(laneCenter + dir * wallAt, dir, 3f);
        yield return WaitFrames(2);
        Vector2 behindWall = laneCenter + dir * (wallAt + 0.6f);
        Check(!vision.IsPointVisible(behindWall), $"벽({wallAt:F2}칸) 바로 뒤는 시야 밖으로 판정된다");
        a = SpawnBat(laneCenter + dir * front);
        yield return WaitUntilSurprised(a);
        float stop4 = Dist(a);
        Check(a.IsSurprised && stop4 < wallAt && stop4 > ambientR - 0.1f,
            $"벽 뒤(부채꼴 반경 안)를 지나 벽 앞에서야 멈춤 (멈춘 거리 {stop4:F2}, 벽 {wallAt:F2}, 부채꼴 반경 {focusedR:F2})");
        Object.Destroy(wall);
        yield return WaitFrames(2);

        // --- 5. 15칸 밖 배회 ↔ 15칸 안 추격
        sb.AppendLine("\n== 5. 15칸 밖이면 배회, 안으로 들어오면 추격, 다시 나가면 배회");
        MovePlayer(laneCenter + new Vector2(0f, 500f)); // 맵 밖 먼 곳 - 15칸 밖이고 시야도 닿지 않는다
        int warnBefore = a.WarningCount;
        float maxSpeed = 0f, path = 0f;
        Vector2 prev = Pos(a);
        for (int i = 0; i < 120; i++)
        {
            yield return WaitFrames(1);
            float step = (Pos(a) - prev).magnitude;
            maxSpeed = Mathf.Max(maxSpeed, step / FrameTime);
            path += step;
            prev = Pos(a);
        }
        Check(a.CurrentPhase == BatMonsterAI.Phase.Wander && !a.IsFrozen, $"15칸 밖: 배회 (단계 {a.CurrentPhase})");
        Check(path > 0.3f && maxSpeed <= config.batWanderSpeed + 0.1f, $"제자리에 멈추지 않고 느리게 움직인다 (4초 {path:F2}칸, 최고 {maxSpeed:F2}/초, 배회 속도 {config.batWanderSpeed})");
        Check(a.WarningCount == warnBefore, $"배회 중에는 경고음 없음 ({warnBefore} → {a.WarningCount})");

        Vector2 standAt = laneCenter;
        for (float t = -behind; t <= front; t += 0.5f)
        {
            Vector2 q = laneCenter + dir * t;
            float dq = Vector2.Distance(q, Pos(a));
            if (dq >= 5f && dq <= 13f) { standAt = q; break; }
        }
        lookDirection = (standAt - Pos(a)).normalized;
        MovePlayer(standAt);
        yield return WaitFrames(2);
        float d5 = Dist(a);
        Check(!vision.IsPointVisible(Pos(a)) && d5 <= config.batChaseRange, $"플레이어를 박쥐와 {d5:F2}칸 떨어진 길 위로(등지고) 옮겼다");
        frames = 0;
        float minD = d5;
        while (frames < 60) { yield return WaitFrames(1); frames++; minD = Mathf.Min(minD, Dist(a)); }
        Check(a.CurrentPhase == BatMonsterAI.Phase.Chase && minD < d5 - 1f, $"15칸 안으로 들어오면 바로 추격 ({d5:F2} → {minD:F2}칸, 단계 {a.CurrentPhase})");
        MovePlayer(laneCenter + new Vector2(0f, 500f));
        yield return WaitFrames(5);
        Check(a.CurrentPhase == BatMonsterAI.Phase.Wander, $"다시 15칸 밖으로 나가면 배회로 돌아온다 (단계 {a.CurrentPhase})");
        lookDirection = dir;

        // --- 6. 배회 중에도 시야 안이면 멈춘다 (추격 거리를 0.5로 줄인 복제 설정)
        sb.AppendLine("\n== 6. 배회 중에도 정면 시야 안이면 멈추고, 시야 밖이면 (풀린 뒤) 움직인다");
        Object.Destroy(a.gameObject);
        MovePlayer(laneCenter);
        yield return WaitFrames(3);
        GameBalanceConfig shortRange = Object.Instantiate(config);
        shortRange.batChaseRange = 0.5f;
        a = SpawnBat(laneCenter + dir * front, shortRange);
        yield return WaitUntilSurprised(a);
        yield return WaitFrames(5);
        held = Pos(a);
        yield return WaitFrames(60);
        Check(a.CurrentPhase == BatMonsterAI.Phase.Wander && a.IsFrozen && (Pos(a) - held).magnitude < 0.02f,
            $"추격 거리 밖(배회)이어도 시야 안이면 멈춘다 (단계 {a.CurrentPhase}, 2초 {(Pos(a) - held).magnitude:F3}칸)");
        vision.enabled = false; // 시야를 끄면 어디도 보이지 않는다 → 시야 밖
        held = Pos(a);
        yield return WaitFrames(75);
        Check(!a.IsFrozen && (Pos(a) - held).magnitude > 0.1f, $"시야 밖이 되면 웅크림이 풀린 뒤 배회를 이어간다 (2.5초 {(Pos(a) - held).magnitude:F2}칸)");
        vision.enabled = true;
        Object.Destroy(a.gameObject);
        Object.Destroy(shortRange);

        // --- 7. 접촉 데미지
        sb.AppendLine("\n== 7. 접촉 데미지 = 최대 체력 x 0.8 (클래스 3종, 굳은 동안은 없음)");
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.UnlockClassFree(PlayerClass.Gunner);
        foreach ((PlayerClass cls, int expected) in new[] { (PlayerClass.Scout, 80), (PlayerClass.Trapper, 104), (PlayerClass.Gunner, 40) })
        {
            gm.SelectClass(cls);
            yield return WaitFrames(3);
            int full = gm.Health;
            a = SpawnBat(laneCenter + dir * 0.4f); // 정면(부채꼴 안), 접촉 거리 안
            yield return WaitFrames(30);
            Check(a.IsFrozen && gm.Health == full, $"{cls}: 접촉 거리 안이어도 굳은 동안 데미지 없음 (체력 {gm.Health}/{full})");
            vision.enabled = false;
            yield return WaitFrames(5); // 시야를 끄면 바로 다가와 접촉한다(풀리는 그림과 상관없이)
            Check(full == GameManager.MaxHealth && full - gm.Health == expected,
                $"{cls}: 최대 체력 {GameManager.MaxHealth}, 접촉 데미지 {full - gm.Health} (기대 {expected}) → 남은 체력 {gm.Health}");
            vision.enabled = true;
            Object.Destroy(a.gameObject);
            yield return WaitFrames(3);
        }
        gm.SelectClass(PlayerClass.Scout);
        yield return WaitFrames(3);

        // --- 8. 무기: 늑대와 같은 데미지, 기절·마취 없음, 맞으면 도망
        sb.AppendLine("\n== 8. 무기 데미지는 늑대와 같고(같은 MonsterHealth 경로), 박쥐는 기절·마취에 걸리지 않고 도망간다");
        yield return WeaponChecks(laneCenter, dir);

        // --- 9. 도망: 거리·속도, 쳐다봐도 안 멈춤, 다시 다가옴
        sb.AppendLine("\n== 9. 맞으면 도망: 거리 = 거위 도망 거리 x 0.5, 속도 = 거위 도망 속도, 도망 중 쳐다봐도 안 멈춤, 끝나면 다시 다가옴");
        yield return FleeChecks(ambientR);

        // --- 10. 무적 없음, 도망 중 또 맞으면 그 자리에서 다시 도망
        sb.AppendLine("\n== 10. 무적 시간 없음, 도망 중 또 맞으면 그 자리에서 다시 도망 거리만큼");
        yield return RepeatHitChecks();

        // --- 11. 체력 0: 쓰러지고 회수
        sb.AppendLine("\n== 11. 체력 0이면 늑대처럼 쓰러지고 E로 회수");
        yield return DeathChecks(laneCenter, dir);

        // --- 12. 시간 소환: 180초에 1마리
        sb.AppendLine("\n== 12. 시간 소환: 180초 전에는 없고 180초에 1마리");
        forceLook = false;
        holdTime = false;
        yield return Load("TruckScene");
        gm.ResetDay();
        yield return Load("VillageScene");
        int livingBefore = VillageMonsterSpawner.CountLivingMonsters();
        SetTime(179.5f);
        yield return WaitFrames(12);
        Check(gm.VillageTime < 180f && Bats().Count == 0, $"{gm.VillageTime:F2}초: 박쥐 없음");
        yield return WaitForBats(1, 45);
        List<BatMonsterAI> spawned = Bats();
        Check(gm.VillageTime >= 180f && spawned.Count == 1, $"{gm.VillageTime:F2}초: 박쥐 {spawned.Count}마리");
        Check(spawned.Count == 1 && !spawned[0].IsSurprised, "소환 직후는 놀람 전");
        Check(VillageMonsterSpawner.CountLivingMonsters() == livingBefore + 1,
            $"MonsterHealth가 있어 마을 몬스터 수에 들어간다 ({livingBefore} → {VillageMonsterSpawner.CountLivingMonsters()}, 상한 {config.villageMonsterCap})");
        if (spawned.Count == 1)
        {
            float spawnDist = Vector2.Distance(Pos(spawned[0]), player.position);
            Check(spawnDist >= config.villageSpawnMinPlayerDistance - 0.5f, $"소환 위치가 플레이어에게서 {spawnDist:F1}칸 (최소 {config.villageSpawnMinPlayerDistance})");
        }
        yield return WaitFrames(150);
        Check(Bats().Count == 1, "간격 소환이 없어 더 늘지 않는다");
        bool surprisedBeforeLeave = Bats().Count == 1 && Bats()[0].IsSurprised;

        // --- 13. 재진입
        sb.AppendLine("\n== 13. 같은 Day 재진입: 다시 놀람 전으로 1마리");
        yield return Load("TruckScene");
        yield return WaitFrames(10);
        yield return Load("VillageScene");
        yield return WaitForBats(1, 45);
        spawned = Bats();
        Check(spawned.Count == 1 && !spawned[0].IsSurprised && spawned[0].CurrentPhase == BatMonsterAI.Phase.Ambush,
            $"재진입 뒤 {spawned.Count}마리, 놀람 전 (나가기 전 놀람 {surprisedBeforeLeave})");
        Check(gm.VillageSpawns.EnteredPhase == 2 && gm.VillageSpawns.Get(2, 0).Issued == 1, "2구간 진입 소환을 다시 하지 않았다(발급 1)");

        // --- 14. Day 변경, 새 게임
        sb.AppendLine("\n== 14. Day 변경과 새 게임에서 초기화");
        yield return Load("TruckScene");
        gm.ResetDay();
        Check(gm.VillageSpawns.EnteredPhase == 0 && gm.VillageTime == 0f, "ResetDay: 소환 기록과 시간이 비었다");
        yield return Load("VillageScene");
        yield return WaitFrames(30);
        Check(Bats().Count == 0, "새 Day 마을 0초: 박쥐 없음");
        SetTime(179.9f);
        yield return WaitFrames(5);
        yield return WaitForBats(1, 45);
        Check(Bats().Count == 1, "새 Day에도 180초에 다시 1마리");
        yield return Load("TruckScene");
        gm.ResetGame();
        Check(gm.VillageSpawns.EnteredPhase == 0 && gm.VillageTime == 0f, "ResetGame: 소환 기록과 시간이 비었다");
        RestartClass();
        yield return Load("VillageScene");
        yield return WaitFrames(30);
        Check(Bats().Count == 0, "새 게임 마을 0초: 박쥐 없음");

        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        Finish();
    }

    private static void CheckPrefab(GameObject wolfPrefab)
    {
        BatMonsterAI ai = batPrefab.GetComponent<BatMonsterAI>();
        MonsterHealth h = batPrefab.GetComponent<MonsterHealth>();
        MonsterHealth wolfHealth = wolfPrefab.GetComponent<MonsterHealth>();
        Check(ai != null && batPrefab.GetComponent<NavMeshAgent>() != null && h != null && batPrefab.GetComponent<MonsterCorpsePickup>() != null,
            "BatMonsterAI, NavMeshAgent, MonsterHealth, MonsterCorpsePickup이 있다");
        Check(h != null && wolfHealth != null && h.MaxHealth == wolfHealth.MaxHealth - 1 && h.IgnoresStunAndSleep && !wolfHealth.IgnoresStunAndSleep,
            $"최대 체력 {h?.MaxHealth} = 늑대 {wolfHealth?.MaxHealth} - 1, 박쥐만 기절·마취 무시");
        Check(batPrefab.GetComponentsInChildren<Collider2D>(true).Length == 0, "2D 충돌체가 없다(다트·시야 벽 판정에 걸리지 않음)");
        Transform visual = batPrefab.transform.Find("Visual");
        Transform body = visual != null ? visual.Find("Body") : null;
        Transform shadow = visual != null ? visual.Find("Shadow") : null;
        Check(visual != null && Mathf.Approximately(visual.localScale.x, 2f) && Mathf.Approximately(visual.localScale.y, 2f), $"Visual 배율 2 ({visual?.localScale})");
        Check(body != null && body.GetComponent<SpriteRenderer>() != null && body.GetComponent<SimpleFrameAnimator>() != null && body.GetComponent<AnimalFacingFlipper>() != null &&
              body.GetSiblingIndex() == 0, "Body: SpriteRenderer·SimpleFrameAnimator·AnimalFacingFlipper, 첫 자식(시체 색이 몸에 칠해짐)");
        SpriteRenderer shadowRenderer = shadow != null ? shadow.GetComponent<SpriteRenderer>() : null;
        Check(shadow != null && shadow.parent == visual && shadow.GetComponentInParent<SimpleFrameAnimator>() == null && shadowRenderer != null &&
              shadowRenderer.sprite != null && shadowRenderer.sprite.name == "Shadow_10x3",
            $"그림자는 몸(Body)이 아니라 Visual 아래 바닥에 따로 ({shadowRenderer?.sprite?.name}, 몸 폭 14 x 0.75)");
        FootSortOrder foot = visual != null ? visual.GetComponent<FootSortOrder>() : null;
        SerializedObject f = foot != null ? new SerializedObject(foot) : null;
        Check(f != null && f.FindProperty("body").objectReferenceValue == body.GetComponent<SpriteRenderer>() && f.FindProperty("shadow").objectReferenceValue == shadowRenderer &&
              Mathf.Approximately(f.FindProperty("footTexels").floatValue, BatMonsterAssetBuilder.ShadowTopTexels),
            $"발 기준 정렬: 몸 = Body, 그림자 = Shadow(몸보다 1 낮음), 발 = 그림자 맨 윗줄({f?.FindProperty("footTexels").floatValue} 텍셀)");
        float shadowTop = shadow != null ? shadow.localPosition.y * 32f + BatMonsterAssetBuilder.ShadowH : float.NaN;
        Check(Mathf.Abs(shadowTop - BatMonsterAssetBuilder.ShadowTopTexels) < 0.01f, $"그림자 맨 윗줄 = 몸 가운데 아래 {-shadowTop:F0}텍셀");
        string[] names = { "fly-01", "fly-06", "freeze-01", "bite-01", "hurt-01", "fallen-01", "wrap-01" };
        bool importOk = true;
        foreach (string n in names)
        {
            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath($"{BatMonsterAssetBuilder.FramesFolder}/{n}.png");
            Sprite s = BatMonsterAssetBuilder.Frame(n);
            if (imp == null || s == null || imp.spriteImportMode != SpriteImportMode.Single || !Mathf.Approximately(imp.spritePixelsPerUnit, 32f) ||
                imp.filterMode != FilterMode.Point || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.mipmapEnabled ||
                Vector2.Distance(s.pivot, new Vector2(16f, 16f)) > 0.01f) importOk = false;
        }
        Check(importOk, "그림 가져오기: Sprite Single, PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데");
        SerializedObject anim = new SerializedObject(body.GetComponent<SimpleFrameAnimator>());
        Check(anim.FindProperty("walkFrames").arraySize == 6 && anim.FindProperty("attack1Frames").arraySize == 4 && anim.FindProperty("deathFrames").arraySize == 2 &&
              anim.FindProperty("deathFrames").GetArrayElementAtIndex(1).objectReferenceValue == BatMonsterAssetBuilder.Frame("fallen-01"),
            "상태 연결: 비행 fly-01~06, 물기 bite-01~04, 피격 hurt-01, 쓰러짐 hurt-01 → fallen-01");
        SerializedObject aiSo = new SerializedObject(ai);
        SerializedProperty wrap = aiSo.FindProperty("wrapFrames");
        float wrapFps = aiSo.FindProperty("wrapFrameRate").floatValue;
        Check(wrap.arraySize == 3 && wrap.GetArrayElementAtIndex(0).objectReferenceValue == BatMonsterAssetBuilder.Frame("wrap-01") &&
              wrap.GetArrayElementAtIndex(1).objectReferenceValue == BatMonsterAssetBuilder.Frame("wrap-02") &&
              wrap.GetArrayElementAtIndex(2).objectReferenceValue == BatMonsterAssetBuilder.Frame("wrap-03") && wrapFps >= 6f && wrapFps <= 8f,
            $"웅크림 연결: wrap-01~03, {wrapFps} fps (사용방법.txt 6~8 fps)");
        Check(Mathf.Approximately(config.batSpeed, 8.4f) && Mathf.Approximately(config.batCatchDamageRatio, 0.8f) && Mathf.Approximately(config.batChaseRange, 15f) &&
              Mathf.Approximately(config.batWanderSpeed, 1.5f) && Mathf.Approximately(config.batWanderRadius, 5f) &&
              Mathf.Approximately(config.batWanderWaitMin, 1f) && Mathf.Approximately(config.batWanderWaitMax, 3f) && Mathf.Approximately(config.batWarningInterval, 1.5f),
            "설정(예전 우는천사 값 그대로 읽힘): 속도 8.4, 데미지 비율 0.8, 추격 거리 15, 배회 속도 1.5·반경 5·대기 1~3초, 경고음 1.5초");
        Check(Mathf.Approximately(ai.FleeSpeed, config.gooseFleeSpeed) && Mathf.Approximately(ai.FleeDistance, config.gooseFleeDistance * 0.5f),
            $"도망 속도 {ai.FleeSpeed} = 거위 {config.gooseFleeSpeed}, 도망 거리 {ai.FleeDistance} = 거위 {config.gooseFleeDistance} x 0.5");
    }

    private static IEnumerator WeaponChecks(Vector2 laneCenter, Vector2 dir)
    {
        // 박쥐는 쳐다보는 중이라 굳어 있다(시야를 끄면 다가와 문다). 맞으면 굳음과 상관없이 도망간다.
        MovePlayer(laneCenter);
        lookDirection = dir;
        yield return WaitFrames(3);
        // 마취 다트(늑대와 같은 tranquilizerDamage)
        BatMonsterAI a = SpawnBat(laneCenter + dir * 3f);
        yield return WaitFrames(3);
        Check(a.IsFrozen, $"무기 검사 전 굳어 있다 (거리 {Dist(a):F2}칸)");
        float before = a.Health.Health;
        TranquilizerDart dartPrefab = AssetDatabase.LoadAssetAtPath<TranquilizerDart>("Assets/Prefabs/TranquilizerDart.prefab");
        TranquilizerDart dart = Object.Instantiate(dartPrefab, (Vector3)laneCenter, Quaternion.identity);
        Vector2 toBat = (Pos(a) - laneCenter).normalized;
        dart.Launch(toBat, 30f, 12f, 1f, 3f, config.tranquilizerDamage);
        for (int i = 0; i < 30 && dart != null; i++) yield return WaitFrames(1);
        Check(Mathf.Abs(before - a.Health.Health - config.tranquilizerDamage) < 0.001f && !a.Health.IsAsleep && a.IsFleeing,
            $"마취 다트: 데미지 {before - a.Health.Health:0.##} (늑대와 같은 {config.tranquilizerDamage}), 잠들지 않음, 도망 시작");
        yield return WaitFrames(20); // 마취 지연(1초)이 지나도 잠들지 않는다
        Check(!a.Health.IsAsleep, "마취 지연 뒤에도 잠들지 않는다");
        Object.Destroy(a.gameObject);
        if (dart != null) Object.Destroy(dart.gameObject);
        yield return WaitFrames(2);

        // 폭탄(늑대와 같은 bombDamage, 스턴 없음)
        a = SpawnBat(laneCenter + dir * 1.2f);
        yield return WaitFrames(3);
        before = a.Health.Health;
        int bombSlot = Enumerable.Range(0, gm.InventorySlotCount).FirstOrDefault(i => gm.GetSlot(i).Type == InventoryItemType.Bomb);
        if (gm.GetSlot(bombSlot).Type != InventoryItemType.Bomb) { gm.TryAddBomb(); bombSlot = Enumerable.Range(0, gm.InventorySlotCount).FirstOrDefault(i => gm.GetSlot(i).Type == InventoryItemType.Bomb); }
        gm.SelectSlot(bombSlot);
        typeof(PlayerGadgetController).GetMethod("UseBomb", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player.GetComponent<PlayerGadgetController>(), null);
        yield return WaitFrames(2);
        Check(Mathf.Abs(before - a.Health.Health - config.bombDamage) < 0.001f && !a.Health.IsStunned && a.IsFleeing,
            $"폭탄: 데미지 {before - a.Health.Health:0.##} (늑대와 같은 {config.bombDamage}), 기절 없음, 도망 시작");
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);

        // 지뢰(늑대와 같은 mineDamage, 스턴 없음)
        a = SpawnBat(laneCenter + dir * 2f);
        yield return WaitFrames(3);
        before = a.Health.Health;
        Vector2 at = Pos(a);
        MineTrap minePrefab = AssetDatabase.LoadAssetAtPath<MineTrap>("Assets/Prefabs/MineTrap.prefab");
        MineTrap mine = Object.Instantiate(minePrefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
        yield return WaitFrames(3);
        Check(Mathf.Abs(before - a.Health.Health - config.mineDamage) < 0.001f && !a.Health.IsStunned && a.IsFleeing,
            $"지뢰: 데미지 {before - a.Health.Health:0.##} (늑대와 같은 {config.mineDamage}), 기절 없음, 도망 시작");
        if (mine != null) Object.Destroy(mine.gameObject);
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);
    }

    private static IEnumerator FleeChecks(float ambientR)
    {
        float fleeDistance = config.BatFleeDistance;
        if (!FindLane(3f, fleeDistance + 2f, out Vector2 lane, out Vector2 d))
        {
            Check(false, $"도망 거리({fleeDistance})만큼 곧게 트인 길을 찾지 못했다");
            yield break;
        }
        // 플레이어는 박쥐 뒤 2칸에서 박쥐를 바라본다(쳐다보는 중) - 박쥐는 맞으면 길 앞쪽(플레이어 반대)으로 도망간다.
        lookDirection = d;
        MovePlayer(lane - d * 2f);
        yield return WaitFrames(3);
        BatMonsterAI a = SpawnBat(lane);
        yield return WaitUntilSurprised(a);
        yield return WaitFrames(5);
        Check(a.IsFrozen && vision.IsPointInFocusedVision(Pos(a)), $"쳐다보는 중이라 굳어 있다 (거리 {Dist(a):F2}칸)");
        int fleeBefore = a.FleeCount;
        a.Health.TakeDamage(0.1f);
        Vector2 start = a.FleeStart;
        float maxSpeed = 0f, maxDisplacement = 0f;
        bool frozenWhileFleeing = false, visibleSometime = false, fleeFrames = false, wrapWhileFleeing = false;
        Vector2 prev = Pos(a);
        int frames = 0;
        while (a.IsFleeing && frames < 200)
        {
            yield return WaitFrames(1);
            frames++;
            maxSpeed = Mathf.Max(maxSpeed, (Pos(a) - prev).magnitude / FrameTime);
            prev = Pos(a);
            maxDisplacement = Mathf.Max(maxDisplacement, (Pos(a) - start).magnitude);
            if (a.IsFleeing && vision.IsPointInFocusedVision(Pos(a))) visibleSometime = true;
            if (a.IsFleeing && (a.IsFrozen || a.IsHolding)) frozenWhileFleeing = true;
            if (a.IsFleeing && (a.CurrentPose != BatMonsterAI.Pose.Flying || SpriteName(a).StartsWith("wrap-"))) wrapWhileFleeing = true;
            if (a.IsFleeing && BodySprite(a) != null && BodySprite(a).name.StartsWith("fly-")) fleeFrames = true;
        }
        float displacement = (Pos(a) - start).magnitude;
        Check(a.FleeCount == fleeBefore + 1, $"맞자마자 도망 시작 (도망 {a.FleeCount - fleeBefore}회)");
        Check(Mathf.Abs(displacement - fleeDistance) <= 0.35f, $"도망 거리 {displacement:F2}칸 (기대 {fleeDistance} = 거위 {config.gooseFleeDistance} x 0.5, {frames}프레임)");
        Check(maxSpeed <= config.gooseFleeSpeed + 0.15f && maxSpeed >= config.gooseFleeSpeed - 0.3f,
            $"도망 속도 최고 {maxSpeed:F2}/초 (거위 도망 속도 {config.gooseFleeSpeed})");
        Check(visibleSometime && !frozenWhileFleeing && !wrapWhileFleeing,
            $"도망 중에는 정면 시야 안이어도 멈추지 않고 웅크리지도 않는다 (정면 시야 안 {visibleSometime}, 굳음 {frozenWhileFleeing}, 웅크림 {wrapWhileFleeing})");
        Check(fleeFrames, "도망 중 비행 그림");
        // 도망이 끝나면 다시 다가온다(시야를 끄면 시야 밖).
        vision.enabled = false;
        float dEnd = Dist(a), minD = dEnd;
        for (int i = 0; i < 30; i++) { yield return WaitFrames(1); minD = Mathf.Min(minD, Dist(a)); }
        Check((a.CurrentPhase == BatMonsterAI.Phase.Return || a.CurrentPhase == BatMonsterAI.Phase.Chase) && minD < dEnd - 3f,
            $"도망이 끝나면 다시 플레이어를 찾아 다가온다 ({dEnd:F2} → {minD:F2}칸, 단계 {a.CurrentPhase})");
        vision.enabled = true;
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);
    }

    private static IEnumerator RepeatHitChecks()
    {
        float fleeDistance = config.BatFleeDistance;
        if (!FindLane(3f, fleeDistance * 2f, out Vector2 lane, out Vector2 d)) FindLane(3f, fleeDistance + 2f, out lane, out d);
        lookDirection = d;
        MovePlayer(lane - d * 2f);
        yield return WaitFrames(3);
        BatMonsterAI a = SpawnBat(lane);
        yield return WaitUntilSurprised(a);
        yield return WaitFrames(3);
        float hp0 = a.Health.Health;
        // 같은 프레임에 두 번
        a.Health.TakeDamage(0.5f);
        a.Health.TakeDamage(0.5f);
        Check(Mathf.Abs(hp0 - a.Health.Health - 1f) < 0.001f && a.FleeCount == 2, $"같은 프레임에 두 번 맞아도 두 번 다 깎인다 ({hp0:0.##} → {a.Health.Health:0.##}, 도망 {a.FleeCount}회)");
        yield return WaitFrames(1);
        float hp1 = a.Health.Health;
        a.Health.TakeDamage(0.5f);
        Check(Mathf.Abs(hp1 - a.Health.Health - 0.5f) < 0.001f, $"바로 다음 프레임에 맞아도 깎인다(무적 없음) ({hp1:0.##} → {a.Health.Health:0.##})");
        // 도망 중간에 또 맞으면 그 자리에서 다시 도망 거리만큼
        for (int i = 0; i < 25 && a.IsFleeing; i++) yield return WaitFrames(1);
        Vector2 firstStart = a.FleeStart;
        Vector2 hitAt = Pos(a);
        bool wasFleeing = a.IsFleeing;
        a.Health.TakeDamage(0.1f);
        Vector2 secondStart = a.FleeStart;
        Check(wasFleeing && (secondStart - hitAt).magnitude < 0.01f && (secondStart - firstStart).magnitude > 1f,
            $"도망 중({(hitAt - firstStart).magnitude:F2}칸 간 자리)에 또 맞으면 그 자리에서 새로 도망 시작");
        int frames = 0;
        while (a.IsFleeing && frames < 200) { yield return WaitFrames(1); frames++; }
        float second = (Pos(a) - secondStart).magnitude;
        Check(Mathf.Abs(second - fleeDistance) <= 0.35f,
            $"두 번째 도망 거리 {second:F2}칸 (기대 {fleeDistance}, 맞은 자리 기준)");
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);
    }

    private static IEnumerator DeathChecks(Vector2 laneCenter, Vector2 dir)
    {
        lookDirection = dir;
        MovePlayer(laneCenter);
        yield return WaitFrames(3);
        BatMonsterAI a = SpawnBat(laneCenter + dir * 1.0f);
        a.name = "TestBatCorpse";
        yield return WaitFrames(3);
        Transform body = a.transform.Find("Visual/Body");
        Transform shadow = a.transform.Find("Visual/Shadow");
        float bodyY = body.localPosition.y;
        int hits = 0;
        while (!a.Health.IsDead && hits < 20) { a.Health.TakeDamage(config.tranquilizerDamage); hits++; }
        Check(a.Health.IsDead && hits == Mathf.CeilToInt(a.Health.MaxHealth / config.tranquilizerDamage - 0.0001f),
            $"마취 다트 데미지 {config.tranquilizerDamage}로 {hits}번에 쓰러짐 (최대 체력 {a.Health.MaxHealth})");
        yield return WaitFrames(15);
        Check(BodySprite(a) == BatMonsterAssetBuilder.Frame("fallen-01"), $"쓰러진 그림(fallen-01) - 지금 {BodySprite(a)?.name}");
        Check(body.localPosition.y < bodyY - 0.01f && Mathf.Abs(shadow.localPosition.y - (BatMonsterAssetBuilder.ShadowTopTexels - BatMonsterAssetBuilder.ShadowH) / 32f) < 0.001f,
            $"몸이 바닥으로 내려앉고({bodyY:F3} → {body.localPosition.y:F3}) 그림자는 그대로");
        SpriteRenderer bodyRenderer = body.GetComponent<SpriteRenderer>(), shadowRenderer = shadow.GetComponent<SpriteRenderer>();
        Check(bodyRenderer.color != Color.white && shadowRenderer.color == Color.white, "시체 색은 몸에만(그림자는 그대로)");
        Check(shadowRenderer.sortingOrder == bodyRenderer.sortingOrder - 1, $"그림자 Order {shadowRenderer.sortingOrder} = 몸 {bodyRenderer.sortingOrder} - 1");
        Vector2 at = Pos(a);
        yield return WaitFrames(20);
        Check((Pos(a) - at).magnitude < 0.01f, "쓰러진 뒤 움직이지 않는다");

        // 회수: 빈칸이 없으면 마지막 칸을 비우고(버리기 - 필드에 물건은 만들지 않는다) 시체 옆에서 E
        if (!gm.HasInventorySpace)
        {
            gm.SelectSlot(gm.InventorySlotCount - 1);
            gm.TryDropSelectedItem(out _);
        }
        MovePlayer(at + Vector2.left * 0.8f);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        string id = "TestBatCorpse";
        int slot = HeldSlot(id);
        Check(gm.HasInventorySpace || slot >= 0, "인벤토리에 빈칸이 있었다");
        Check(slot >= 0 && a != null && !a.gameObject.activeInHierarchy,
            $"E로 회수: 인벤토리 {slot}번 칸에 시체(무게 {config.wolfCorpseWeight} = 늑대 시체와 같음), 필드에서 치워짐");
        if (slot >= 0) { gm.SelectSlot(slot); gm.TryDropSelectedItem(out _); } // 다음 흐름을 위해 비운다
        yield return WaitFrames(2);
    }

    private static void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[BatMonsterTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 도움 함수 =====================

    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);

    private static void SetTime(float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });

    private static Vector2 Pos(BatMonsterAI a) => new Vector2(a.transform.position.x, a.transform.position.z);

    private static float Dist(BatMonsterAI a) => Vector2.Distance(Pos(a), player.position);

    private static Sprite BodySprite(BatMonsterAI a) => a.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>()?.sprite;

    private static string SpriteName(BatMonsterAI a) => BodySprite(a) != null ? BodySprite(a).name : "";

    private static float WrapFrameRate(BatMonsterAI a) => (float)typeof(BatMonsterAI).GetField("wrapFrameRate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(a);

    // ===================== 실제 렌더 캡처 =====================
    // 게임 카메라(Camera.main)를 RenderTexture에 한 번 더 그려 잘라 낸다 - 화면과 같은 조명·시야·정렬이다.

    private const string CaptureFolder = "Logs/Bat/render";
    private static readonly List<string> captureManifest = new List<string>();

    // 몸 그림 둘레를 자른다. 시야 밖(어둠)에서도 프레임이 보이게 이 렌더 한 번만 전역 조명을 1로 올렸다가 바로 되돌린다(게임 동작과 무관).
    private static void CaptureBat(BatMonsterAI a, string label)
    {
        SpriteRenderer body = a.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
        Vector2 c = body != null ? (Vector2)body.bounds.center : Pos(a);
        Light2D[] globals = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.lightType == Light2D.LightType.Global).ToArray();
        float[] saved = globals.Select(l => l.intensity).ToArray();
        foreach (Light2D l in globals) l.intensity = 1f;
        Capture($"crouch_{captureManifest.Count:00}", label, c - Vector2.one * 1.6f, c + Vector2.one * 1.6f);
        for (int i = 0; i < globals.Length; i++) globals[i].intensity = saved[i];
    }

    // 플레이어와 박쥐가 함께 들어오게 정사각형으로 자른다.
    private static void CaptureWide(string tag, string label, Vector2 bat)
    {
        Vector2 p = player.position;
        Vector2 min = Vector2.Min(p, bat) - Vector2.one * 1.6f, max = Vector2.Max(p, bat) + Vector2.one * 1.6f;
        Vector2 center = (min + max) * 0.5f;
        float half = Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f;
        Capture($"view_{tag}", label, center - Vector2.one * half, center + Vector2.one * half);
    }

    private static void Capture(string file, string label, Vector2 worldMin, Vector2 worldMax)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        const int W = 1920, H = 1080;
        Directory.CreateDirectory(CaptureFolder);
        RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture prevTarget = cam.targetTexture, prevActive = RenderTexture.active;
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
        File.WriteAllBytes($"{CaptureFolder}/{file}.png", tex.EncodeToPNG());
        RenderTexture.active = prevActive;
        cam.targetTexture = prevTarget;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        captureManifest.Add($"{file}.png|{label}");
    }

    private static void WriteCaptureManifest()
    {
        Directory.CreateDirectory(CaptureFolder);
        File.WriteAllLines($"{CaptureFolder}/manifest.txt", captureManifest, new UTF8Encoding(false));
        sb.AppendLine($"(참고) 실제 렌더 캡처 {captureManifest.Count}장: {Path.GetFullPath(CaptureFolder)}");
    }

    private static List<BatMonsterAI> Bats() => Object.FindObjectsByType<BatMonsterAI>(FindObjectsInactive.Exclude).Where(b => !b.Health.IsDead).ToList();

    private static BatMonsterAI SpawnBat(Vector2 at, GameBalanceConfig overrideConfig = null)
    {
        NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas);
        BatMonsterAI a = Object.Instantiate(batPrefab, hit.position, Quaternion.identity).GetComponent<BatMonsterAI>();
        a.name = "TestBat";
        if (overrideConfig != null) typeof(BatMonsterAI).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(a, overrideConfig);
        return a;
    }

    private static int HeldSlot(string id)
    {
        for (int i = 0; i < gm.InventorySlotCount; i++)
        {
            InventorySlotData s = gm.GetSlot(i);
            if (s.Type == InventoryItemType.Animal && s.AnimalKind == AnimalBehaviorKind.MonsterCorpse && s.AnimalId == id) return i;
        }
        return -1;
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    // 소환기가 자리를 못 찾으면 0.5초 뒤 다시 시도하므로 바로 세지 않고 잠시 기다린다.
    private static IEnumerator WaitForBats(int count, int maxFrames)
    {
        for (int i = 0; i < maxFrames && Bats().Count < count; i++) yield return WaitFrames(1);
    }

    private static IEnumerator WaitUntilSurprised(BatMonsterAI a)
    {
        for (int i = 0; i < 300 && !a.IsSurprised; i++) yield return WaitFrames(1);
    }

    private static void MovePlayer(Vector2 at)
    {
        player.position = new Vector3(at.x, at.y, player.position.z);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = at; body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }

    private static GameObject Wall(Vector2 center, Vector2 facing, float width)
    {
        GameObject go = new GameObject("TestWall");
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
        go.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, width);
        Physics2D.SyncTransforms();
        return go;
    }

    // 바닥 칸 중에서, 한 축 방향으로 뒤 behind칸·앞 front칸이 NavMesh로 곧게 이어지고 고정 충돌체가 가리지 않는 자리를 찾는다(트럭 방 제외).
    private static bool FindLane(float behind, float front, out Vector2 center, out Vector2 direction)
    {
        center = default;
        direction = Vector2.up;
        GameObject map = GameObject.Find("GeneratedMap");
        TruckPoint truck = Object.FindAnyObjectByType<TruckPoint>();
        if (map == null) return false;
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.right, Vector2.left };
        foreach (Transform child in map.transform)
        {
            if (child.name == "OutsideFloorBand") continue;
            Tilemap ground = child.Find("Ground")?.GetComponent<Tilemap>();
            if (ground == null) continue;
            if (truck != null && ground.HasTile(ground.WorldToCell(truck.transform.position))) continue;
            foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
            {
                if (!ground.HasTile(cell) || (cell.x + cell.y) % 2 != 0) continue;
                Vector3 c = ground.GetCellCenterWorld(cell);
                Vector2 p = new Vector2(c.x, c.y);
                if (!NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out NavMeshHit hit, 0.2f, NavMesh.AllAreas)) continue;
                Vector3 p3 = hit.position;
                foreach (Vector2 d in dirs)
                {
                    Vector2 back = p - d * behind, ahead = p + d * front;
                    if (NavMesh.Raycast(p3, new Vector3(back.x, 0f, back.y), out _, NavMesh.AllAreas)) continue;
                    if (NavMesh.Raycast(p3, new Vector3(ahead.x, 0f, ahead.y), out _, NavMesh.AllAreas)) continue;
                    if (StaticBlockerLine.IsBlocked(p, back, ~0) || StaticBlockerLine.IsBlocked(p, ahead, ~0)) continue;
                    Vector2 side = new Vector2(-d.y, d.x) * 0.6f;
                    if (StaticBlockerLine.IsBlocked(p + side, back + side, ~0) || StaticBlockerLine.IsBlocked(p - side, ahead - side, ~0)) continue;
                    center = new Vector2(hit.position.x, hit.position.z);
                    direction = d;
                    return true;
                }
            }
        }
        return false;
    }

    private static void RestartClass()
    {
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 20f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
