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
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Passives > Run Vision Size Test. 같은 시드(격자 3x4, 시드 1)의 VillageScene, 같은 위치(플레이어 스폰), 같은 방향(가상 마우스)에서
/// 클래스 3종의 원형/부채꼴 시야 Light2D 값을 숫자로 남기고 Game 뷰를 캡처한다.
/// - 플래시라이트 패시브가 없는 빌드(main)에서도 돌아가도록 패시브 API는 리플렉션으로 찾는다 - 없으면 한 번만 잰다(라벨 main).
/// - 패시브가 있으면: 사기 전 → (마을 안에서) 구매 → 산 뒤를 재고, Day 넘김/씬 왕복 유지, 게임 오버 재시작 뒤 초기화,
///   사기 전 배율 0이면 부채꼴이 꺼지는지(잠시 바꿨다 되돌림)를 확인한다.
/// 결과: Logs/VisionSizeTest_{라벨}.txt, Logs/VisionSize_{라벨}_{클래스}.png
/// </summary>
[InitializeOnLoad]
public static class VisionSizeTest
{
    private const string KeyActive = "VST.Active", KeyDone = "VST.Done";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private const string FlashlightPath = "Assets/Settings/Passives/Flashlight.asset";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();

    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static readonly PlayerClass[] Classes = { PlayerClass.Scout, PlayerClass.Trapper, PlayerClass.Gunner };

    static VisionSizeTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Passives/Run Vision Size Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VisionSizeTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyDone, false);
        EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
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
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("VisionSizeTestKeyboard");
            vmouse = InputSystem.AddDevice<Mouse>("VisionSizeTestMouse");
            vkb.MakeCurrent();
            vmouse.MakeCurrent();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
        }
    }

    private static void TearDown()
    {
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vkb = null;
        vmouse = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
#if UNITY_EDITOR
        VillageMapGenerator.EditorLayoutOverride = null;
#endif
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith("[VisionSizeTest]")) return;
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
            File.WriteAllText("Logs/VisionSizeTest_error.txt", "중단됨: " + e, new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            Application.logMessageReceived -= OnLog;
            TearDown();
            routine = null;
            Time.timeScale = 1f;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static StringBuilder sb;
    private static List<string> failures;
    private static Vector3? spawnPosition;

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    // ---- 패시브 API (main에는 없다) ----
    private static readonly System.Type PassiveEffectsType = System.Type.GetType("PassiveEffects, Assembly-CSharp");
    private static readonly System.Type ShopPurchaseType = System.Type.GetType("ShopPurchase, Assembly-CSharp");
    private static bool PassivesAvailable => PassiveEffectsType != null && ShopPurchaseType != null;

    private static bool HasFlashlight() => (bool)PassiveEffectsType.GetProperty("HasFlashlight").GetValue(null);

    private static bool BuyFlashlight(GameBalanceConfig config, out string message)
    {
        ScriptableObject flashlight = AssetDatabase.LoadAssetAtPath<ScriptableObject>(FlashlightPath);
        MethodInfo buy = ShopPurchaseType.GetMethod("TryPurchasePassive");
        object[] args = { config, flashlight, null };
        bool ok = (bool)buy.Invoke(null, args);
        message = args[2] as string;
        return ok;
    }

    // 이 작업에서 추가한 설정값도 main에는 없어서 이름으로 읽고 쓴다.
    private static float ConfigFloat(GameBalanceConfig config, string field) =>
        (float)typeof(GameBalanceConfig).GetField(field).GetValue(config);

    private static void SetConfigFloat(GameBalanceConfig config, string field, float value) =>
        typeof(GameBalanceConfig).GetField(field).SetValue(config, value);

    private static int FlashlightPrice(GameBalanceConfig config)
    {
        foreach (object entry in (System.Collections.IList)typeof(GameBalanceConfig).GetField("passivePrices").GetValue(config))
        {
            Object passive = (Object)entry.GetType().GetField("passive").GetValue(entry);
            if (passive != null && AssetDatabase.GetAssetPath(passive) == FlashlightPath) return (int)entry.GetType().GetField("price").GetValue(entry);
        }
        return -1;
    }

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        string label = PassivesAvailable ? "branch" : "main";
        sb.AppendLine($"VisionSizeTest [{label}] {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} - 격자 3x4 시드 1, 플레이어 스폰 위치, 마우스 화면 중앙 +(300, 120)px");
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);

        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        GiveMileage(gm, config.GetClassUnlockPrice(PlayerClass.Trapper) + config.GetClassUnlockPrice(PlayerClass.Gunner));
        gm.PurchaseClass(PlayerClass.Trapper);
        gm.PurchaseClass(PlayerClass.Gunner);

        if (!PassivesAvailable)
        {
            yield return Measure("main");
            Finish(label);
            yield break;
        }

        // ---- 사기 전 ----
        Check(!HasFlashlight(), "새 게임: 플래시라이트 없음");
        yield return Measure("before");

        // ---- 마을 안에서 구매 → 바로 적용 ----
        sb.AppendLine("\n== 구매 즉시 적용 (마을 안, 스카우트)");
        gm.SelectClass(PlayerClass.Scout);
        yield return WaitFrames(3);
        VisionValues beforeBuy = Read();
        int price = FlashlightPrice(config);
        GiveMileage(gm, price);
        int mileageBefore = gm.Mileage;
        bool bought = BuyFlashlight(config, out string message);
        yield return WaitFrames(2);
        VisionValues afterBuy = Read();
        Check(bought && HasFlashlight() && gm.Mileage == mileageBefore - price, $"구매: 성공 {bought} ({message ?? "-"}), 마일리지 {mileageBefore} → {gm.Mileage}");
        Check(Mathf.Abs(afterBuy.FocusedOuterRadius - beforeBuy.FocusedOuterRadius / ConfigFloat(config, "noFlashlightFocusedRadiusMultiplier")) < 1e-4f,
            $"2프레임 뒤 부채꼴 반경 {beforeBuy.FocusedOuterRadius:0.####} → {afterBuy.FocusedOuterRadius:0.####}");
        Check(!BuyFlashlight(config, out message), $"두 번째 구매는 막힘 (\"{message}\")");

        // ---- 산 뒤 ----
        yield return Measure("after");

        // ---- Day 넘김 + 씬 왕복 ----
        sb.AppendLine("\n== Day 넘김과 씬 왕복");
        gm.SelectClass(PlayerClass.Scout);
        float fullRadius = config.GetClassFocusedVisionRadius(PlayerClass.Scout);
        gm.ResetDay();
        foreach (string scene in new[] { "TruckScene", "VillageScene", "ShelterScene", "VillageScene" })
        {
            if (scene == "VillageScene") yield return LoadVillage(); else yield return Load(scene);
            string light = "";
            if (scene == "VillageScene")
            {
                yield return WaitFrames(3);
                VisionValues v = Read();
                light = $", 부채꼴 반경 {v.FocusedOuterRadius:0.####}";
                Check(Mathf.Abs(v.FocusedOuterRadius - fullRadius) < 1e-4f && v.FocusedEnabled, $"{scene}: 플래시라이트 {HasFlashlight()}{light}");
            }
            else
            {
                Check(HasFlashlight(), $"{scene}: 플래시라이트 {HasFlashlight()}");
            }
        }

        // ---- 사기 전 배율 0이면 꺼짐 (잠시 바꿨다 되돌림) ----
        sb.AppendLine("\n== 게임 오버 뒤 새 게임");
        yield return Load("TruckScene");
        gm.FinishCycle();
        Check(gm.IsGameOver, "목표 미달로 게임 오버");
        GameOverPanel panel = Object.FindObjectsByType<GameOverPanel>(FindObjectsInactive.Include).FirstOrDefault();
        if (panel != null)
        {
            panel.Show();
            yield return WaitFrames(1);
            UnityEngine.EventSystems.ExecuteEvents.Execute(panel.GetComponentsInChildren<Button>(true).First().gameObject,
                new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            yield return WaitForScene("ClassSelectScene");
        }
        Check(!HasFlashlight(), $"재시작 뒤 플래시라이트 {HasFlashlight()}");
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        yield return LoadVillage();
        yield return WaitFrames(3);
        VisionValues reset = Read();
        Check(Mathf.Abs(reset.FocusedOuterRadius - fullRadius * ConfigFloat(config, "noFlashlightFocusedRadiusMultiplier")) < 1e-4f,
            $"새 게임 마을: 부채꼴 반경 {reset.FocusedOuterRadius:0.####} (= {fullRadius} x {ConfigFloat(config, "noFlashlightFocusedRadiusMultiplier")})");

        sb.AppendLine("\n== 사기 전 배율 0");
        float savedRadius = ConfigFloat(config, "noFlashlightFocusedRadiusMultiplier"), savedAngle = ConfigFloat(config, "noFlashlightFocusedAngleMultiplier");
        SetConfigFloat(config, "noFlashlightFocusedRadiusMultiplier", 0f);
        yield return WaitFrames(3);
        bool offByRadius = !Read().FocusedEnabled;
        SetConfigFloat(config, "noFlashlightFocusedRadiusMultiplier", savedRadius);
        SetConfigFloat(config, "noFlashlightFocusedAngleMultiplier", 0f);
        yield return WaitFrames(3);
        bool offByAngle = !Read().FocusedEnabled;
        SetConfigFloat(config, "noFlashlightFocusedAngleMultiplier", savedAngle);
        yield return WaitFrames(3);
        VisionValues restored = Read();
        Check(offByRadius && offByAngle, $"반경 배율 0 → 꺼짐 {offByRadius}, 각도 배율 0 → 꺼짐 {offByAngle}");
        Check(restored.FocusedEnabled && Mathf.Abs(restored.FocusedOuterRadius - reset.FocusedOuterRadius) < 1e-4f,
            $"0.7로 되돌린 뒤 다시 켜짐 {restored.FocusedEnabled}, 반경 {restored.FocusedOuterRadius:0.####} (설정 값 {ConfigFloat(config, "noFlashlightFocusedRadiusMultiplier")}/{ConfigFloat(config, "noFlashlightFocusedAngleMultiplier")}로 복원)");

        Finish(label);
    }

    private struct VisionValues
    {
        public bool FocusedEnabled;
        public float FocusedOuterRadius, FocusedInnerRadius, FocusedOuterAngle, FocusedInnerAngle, FocusedRotation, FocusedIntensity, FocusedFalloff, FocusedSoftness;
        public bool AmbientEnabled, AmbientShadows;
        public float AmbientOuterRadius, AmbientInnerRadius, AmbientIntensity, AmbientFalloff;
        public Vector3 Position;
    }

    private static VisionValues Read()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        PlayerVision vision = player.GetComponent<PlayerVision>();
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Light2D focused = (Light2D)typeof(PlayerVision).GetField("focusedVisionLight", flags).GetValue(vision);
        Light2D ambient = (Light2D)typeof(PlayerVision).GetField("ambientVisionLight", flags).GetValue(vision);
        return new VisionValues
        {
            FocusedEnabled = focused.enabled && focused.gameObject.activeInHierarchy,
            FocusedOuterRadius = focused.pointLightOuterRadius,
            FocusedInnerRadius = focused.pointLightInnerRadius,
            FocusedOuterAngle = focused.pointLightOuterAngle,
            FocusedInnerAngle = focused.pointLightInnerAngle,
            FocusedRotation = focused.transform.eulerAngles.z,
            FocusedIntensity = focused.intensity,
            FocusedFalloff = focused.falloffIntensity,
            FocusedSoftness = focused.shadowSoftness,
            AmbientEnabled = ambient.enabled && ambient.gameObject.activeInHierarchy,
            AmbientShadows = ambient.shadowsEnabled,
            AmbientOuterRadius = ambient.pointLightOuterRadius,
            AmbientInnerRadius = ambient.pointLightInnerRadius,
            AmbientIntensity = ambient.intensity,
            AmbientFalloff = ambient.falloffIntensity,
            Position = player.transform.position,
        };
    }

    // 같은 시드/위치/방향에서 클래스 3종을 재고 캡처한다. 결과 줄 형식은 main/branch가 같아서 그대로 diff할 수 있다.
    private static IEnumerator Measure(string state)
    {
        GameManager gm = GameManager.Instance;
        sb.AppendLine($"\n== 측정 [{state}]");
        yield return LoadVillage();
        foreach (PlayerClass c in Classes)
        {
            gm.SelectClass(c);
            yield return WaitFrames(5);
            VisionValues v = Read();
            sb.AppendLine($"  {state} {c,-7} 부채꼴 켜짐 {v.FocusedEnabled} 반경 {v.FocusedInnerRadius:0.0000}/{v.FocusedOuterRadius:0.0000} 각도 {v.FocusedInnerAngle:0.0000}/{v.FocusedOuterAngle:0.0000} " +
                          $"회전 {v.FocusedRotation:0.00} 밝기 {v.FocusedIntensity:0.00} 감쇠 {v.FocusedFalloff:0.00} 그림자부드러움 {v.FocusedSoftness:0.00}");
            sb.AppendLine($"  {state} {c,-7} 원형 켜짐 {v.AmbientEnabled} 반경 {v.AmbientInnerRadius:0.0000}/{v.AmbientOuterRadius:0.0000} 밝기 {v.AmbientIntensity:0.00} 감쇠 {v.AmbientFalloff:0.00} 그림자 {v.AmbientShadows} | 위치 {v.Position.x:0.000},{v.Position.y:0.000}");
            string shot = $"Logs/VisionSize_{state}_{c}.png";
            ScreenCapture.CaptureScreenshot(shot);
            yield return WaitFrames(3);
            sb.AppendLine($"  캡처 {shot} ({Screen.width}x{Screen.height})");
        }
        gm.SelectClass(PlayerClass.Scout);
    }

    private static IEnumerator LoadVillage()
    {
#if UNITY_EDITOR
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
#endif
        yield return Load("VillageScene");
        foreach (MonsterHealth monster in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) monster.gameObject.SetActive(false);

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (spawnPosition == null) spawnPosition = player.transform.position;
        player.transform.position = spawnPosition.Value;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = spawnPosition.Value;

        Vector2 aim = new Vector2(Screen.width * 0.5f + 300f, Screen.height * 0.5f + 120f);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = aim });
        yield return WaitFrames(5);
    }

    private static void GiveMileage(GameManager gm, int amount)
    {
        if (amount <= 0) return;
        gm.AddCargo(amount);
        gm.SettleCargo(1);
    }

    private static void Finish(string label)
    {
        sb.AppendLine($"\n== 콘솔 경고/에러 (Log 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 경고/에러 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        string path = $"Logs/VisionSizeTest_{label}.txt";
        Directory.CreateDirectory("Logs");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[VisionSizeTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(path));
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        yield return WaitForScene(sceneName);
    }

    private static IEnumerator WaitForScene(string sceneName)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
    }
}
