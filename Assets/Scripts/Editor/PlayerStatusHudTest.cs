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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > UI > Run Player Status HUD Test. 체력·스태미나 바(AnimalKeeperHUD + PlayerStatusHUD)를 실제 씬 흐름으로 확인한다.
/// 검사: TruckScene·VillageScene에만 있고 예전 HealthUI/StaminaUI가 없음, 씬 Canvas(2240x1260, Match 0.5) 안의 중첩 Canvas,
/// 씬이 시작되는 첫 프레임부터 맞는 값(새 게임·체력 0 강제 복귀·씬 전환), 데미지 47·우는천사(최대 체력 80%)·0·최대, 클래스별 최대 체력,
/// 스태미나 달리기(감소)·탈진(0)·쉬기(회복), 다른 HUD와 겹치지 않음(1920x1080, 1280x720), 화면 캡처.
/// 프레임 시간은 1/30초로 고정한다(Time.captureDeltaTime). 마을 몬스터는 끈다(씬 파일은 바꾸지 않는다). 결과: Logs/PlayerStatusHudTest.txt
/// batchmode: -executeMethod PlayerStatusHudTest.RunBatch  (끝나면 에디터가 종료된다, 통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class PlayerStatusHudTest
{
    private const string KeyActive = "PSHT.Active", KeyDone = "PSHT.Done", KeyBatch = "PSHT.Batch", KeyPassed = "PSHT.Passed";
    private const string ReportPath = "Logs/PlayerStatusHudTest.txt";
    private const string CaptureFolder = "Logs/PlayerStatusHUD";
    private const float FrameTime = 1f / 30f;
    private const float PercentTolerance = 0.01f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();
    private static Keyboard vkb;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static readonly FieldInfo HpFill = typeof(AnimalKeeperHUD).GetField("hpFill", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo StaminaFill = typeof(AnimalKeeperHUD).GetField("staminaFill", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo HpDigits = typeof(AnimalKeeperHUD).GetField("hpDigits", BindingFlags.NonPublic | BindingFlags.Instance);

    static PlayerStatusHudTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/UI/Run Player Status HUD Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[PlayerStatusHudTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("PlayerStatusHudTestKeyboard");
            vkb.MakeCurrent();
            Time.captureDeltaTime = FrameTime;
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
        if (vkb != null) { InputSystem.RemoveDevice(vkb); vkb = null; }
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
    }

    // 에러·예외는 모두, 경고는 HUD 관련(에셋·연결 코드)만 남긴다.
    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log) return;
        if (type == LogType.Warning && !(condition.Contains("HUD") || stackTrace.Contains("AnimalKeeperHUD") || stackTrace.Contains("PlayerStatusHUD"))) return;
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

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"PlayerStatusHudTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        GameManager gm = GameManager.Instance;
        GameBalanceConfig config = gm != null ? gm.Config : null;
        if (gm == null || config == null)
        {
            Check(false, "TitleScene에서 GameManager 또는 GameBalanceConfig를 찾지 못했다");
            Finish();
            yield break;
        }

        // --- 0. 씬 구성
        sb.AppendLine("\n== 0. 체력·스태미나 바가 있는 씬 (예전 HealthUI/StaminaUI 자리)");
        Check(Object.FindAnyObjectByType<PlayerStatusHUD>(FindObjectsInactive.Include) == null && Object.FindAnyObjectByType<AnimalKeeperHUD>(FindObjectsInactive.Include) == null,
            "TitleScene에는 체력·스태미나 바가 없다");
        RestartClass(gm, PlayerClass.Scout);

        // --- 1. 트럭: 구성과 첫 프레임 값
        sb.AppendLine("\n== 1. TruckScene: 구성, 첫 프레임부터 맞는 값");
        gm.TakeDamage(53, "테스트"); // 100 → 47
        yield return LoadAndCheckFirstFrame(gm, "TruckScene", "체력 47로 트럭에 들어옴");
        yield return CheckStructure("TruckScene", "HubMenuPanel");

        // --- 2. 마을: 데미지, 0, 최대, 클래스별 최대 체력
        sb.AppendLine("\n== 2. VillageScene: 체력 바 갱신");
        RestartClass(gm, PlayerClass.Scout);
        yield return LoadAndCheckFirstFrame(gm, "VillageScene", "최대 체력으로 마을에 들어옴");
        DisableMonsters();
        yield return CheckStructure("VillageScene", null);
        PlayerStatusHUD bridge = Object.FindAnyObjectByType<PlayerStatusHUD>();
        AnimalKeeperHUD hud = bridge.Hud;

        yield return Settle(hud);
        CheckHealth(hud, gm, "최대 체력", 100f);

        gm.TakeDamage(config.patrolMonsterDamage, "테스트");
        yield return Settle(hud);
        CheckHealth(hud, gm, $"늑대 접촉 {config.patrolMonsterDamage}", 100f - config.patrolMonsterDamage);

        gm.SelectClass(PlayerClass.Scout); // 체력이 다시 최대로
        yield return Settle(hud);
        CheckHealth(hud, gm, "다시 최대", 100f);
        int angel = Mathf.RoundToInt(GameManager.MaxHealth * config.weepingAngelCatchDamageRatio);
        gm.TakeDamage(angel, "테스트");
        yield return Settle(hud);
        CheckHealth(hud, gm, $"우는천사 {angel}(최대 체력 {GameManager.MaxHealth}의 {config.weepingAngelCatchDamageRatio:P0})", 20f);

        foreach (PlayerClass cls in new[] { PlayerClass.Trapper, PlayerClass.Gunner })
        {
            gm.UnlockClassFree(cls);
            gm.SelectClass(cls);
            yield return Settle(hud);
            CheckHealth(hud, gm, $"{cls} 최대 체력 {GameManager.MaxHealth}", 100f);
            int before = gm.Health;
            gm.TakeDamage(config.patrolMonsterDamage, "테스트");
            yield return Settle(hud);
            CheckHealth(hud, gm, $"{cls} {before} - {config.patrolMonsterDamage}", (before - config.patrolMonsterDamage) * 100f / GameManager.MaxHealth);
        }
        gm.SelectClass(PlayerClass.Scout);
        yield return Settle(hud);

        // --- 3. 스태미나
        sb.AppendLine("\n== 3. 스태미나: 달리면 줄고, 탈진하면 0, 쉬면 회복");
        PlayerMovement player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerMovement>();
        CheckStamina(hud, player, "시작", 100f);
        bool tracked = true;
        float minSeen = player.Stamina;
        bool exhausted = false;
        float previous = player.Stamina;
        for (int i = 0; i < 300 && !exhausted; i++) // 달리기(왼쪽 Shift)를 탈진할 때까지
        {
            InputSystem.QueueStateEvent(vkb, new KeyboardState(Key.LeftShift));
            yield return WaitFrames(1);
            if (!Tracks(hud.staminaPercent, player, previous)) tracked = false;
            previous = player.Stamina;
            minSeen = Mathf.Min(minSeen, player.Stamina);
            if (player.Stamina <= 0f) exhausted = true;
        }
        Check(tracked, "달리는 동안 매 프레임 바 목표값 = 스태미나 / 최대 (연결 코드는 PlayerMovement보다 먼저 돌아 그 프레임 시작 값을 넘긴다)");
        Check(exhausted, $"달리면 스태미나가 줄어 탈진한다 (최저 {minSeen:F1})");
        // 스태미나는 쉬는 즉시 회복되기 시작하므로, 탈진한 순간 게임 시간을 멈추고(timeScale 0) 표시값이 0까지 따라오는지 본다.
        // HUD는 실제 시간(unscaled)으로 움직인다.
        Time.timeScale = 0f;
        yield return Settle(hud);
        CheckStamina(hud, player, "탈진", 0f);
        Time.timeScale = 1f;
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
        tracked = true;
        float startRegen = player.Stamina;
        previous = player.Stamina;
        for (int i = 0; i < 90; i++) // 3초 쉬기
        {
            yield return WaitFrames(1);
            if (!Tracks(hud.staminaPercent, player, previous)) tracked = false;
            previous = player.Stamina;
        }
        Check(tracked && player.Stamina > startRegen, $"쉬는 동안 회복하고 바 목표값이 따라간다 ({startRegen:F1} → {player.Stamina:F1})");
        yield return SettleFrozen(hud);
        CheckStamina(hud, player, "회복 중", player.Stamina / player.MaxStamina * 100f);
        for (int i = 0; i < 400 && player.Stamina < player.MaxStamina; i++) yield return WaitFrames(1);
        yield return Settle(hud);
        CheckStamina(hud, player, "가득 회복", 100f);

        // --- 4. 겹침과 캡처 (마을)
        sb.AppendLine("\n== 4. VillageScene: 다른 HUD와 겹치지 않음, 캡처");
        Directory.CreateDirectory(CaptureFolder);
        gm.TakeDamage(config.patrolMonsterDamage, "테스트"); // 바가 줄어든 모습으로 찍는다
        InputSystem.QueueStateEvent(vkb, new KeyboardState(Key.LeftShift));
        yield return WaitFrames(20);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return Settle(hud);
        yield return CaptureScene("VillageScene", hud);

        // --- 5. 체력 0 → 트럭 강제 복귀
        sb.AppendLine("\n== 5. 체력 0: 트럭으로 강제 복귀, 첫 프레임부터 0%");
        gm.TakeDamage(1000, "테스트");
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != "TruckScene" && Time.realtimeSinceStartup < until) yield return null;
        yield return CheckFirstFrame(gm, "체력 0 강제 복귀");
        sb.AppendLine($"  (참고) 강제 복귀 직후 체력 {gm.Health}/{GameManager.MaxHealth}");

        // --- 6. 트럭 캡처
        sb.AppendLine("\n== 6. TruckScene: 다른 HUD와 겹치지 않음, 캡처");
        hud = Object.FindAnyObjectByType<PlayerStatusHUD>().Hud;
        gm.SelectClass(PlayerClass.Scout);
        gm.TakeDamage(config.patrolMonsterDamage, "테스트");
        yield return Settle(hud);
        yield return CaptureScene("TruckScene", hud);

        // --- 7. 새 게임
        sb.AppendLine("\n== 7. 새 게임(ResetGame, 게임 오버 뒤 다시 시작과 같은 경로) 뒤 첫 프레임부터 최대");
        gm.TakeDamage(30, "테스트");
        gm.ResetGame();
        RestartClass(gm, PlayerClass.Scout);
        yield return LoadAndCheckFirstFrame(gm, "TruckScene", "새 게임 트럭");
        yield return LoadAndCheckFirstFrame(gm, "VillageScene", "새 게임 마을");
        DisableMonsters();
        yield return LoadAndCheckFirstFrame(gm, "ShelterScene", "보호소");

        sb.AppendLine($"\n== 콘솔 에러/예외와 HUD 관련 경고: {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/경고 {logs.Count}건");

        Finish();
    }

    private static void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[PlayerStatusHudTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 검사 도우미 =====================

    private static float Fill(AnimalKeeperHUD hud, FieldInfo field) => field.GetValue(hud) is Image img && img != null ? img.fillAmount : -1f;
    // 숫자는 그림 글자(digits)로 그린다 - HUD가 지금 그린 글자를 읽는다.
    private static string HealthLabel(AnimalKeeperHUD hud) => string.IsNullOrEmpty(hud.HealthLabel) ? "(없음)" : hud.HealthLabel;
    private static string StaminaLabel(AnimalKeeperHUD hud) => string.IsNullOrEmpty(hud.StaminaLabel) ? "(없음)" : hud.StaminaLabel;

    private static void CheckHealth(AnimalKeeperHUD hud, GameManager gm, string what, float expectedPercent)
    {
        float expected = gm.Health * 100f / GameManager.MaxHealth;
        float fill = Fill(hud, HpFill);
        string label = HealthLabel(hud);
        Check(Mathf.Abs(expected - expectedPercent) < 0.01f && Mathf.Abs(hud.healthPercent - expected) < PercentTolerance &&
              Mathf.Abs(fill - expected / 100f) < 0.001f && label == Mathf.RoundToInt(expected) + "%",
            $"{what}: 체력 {gm.Health}/{GameManager.MaxHealth} → 바 {fill * 100f:F1}% 글자 \"{label}\" (기대 {expectedPercent:F1}%)");
    }

    private static void CheckStamina(AnimalKeeperHUD hud, PlayerMovement player, string what, float expectedPercent)
    {
        float expected = player.Stamina / player.MaxStamina * 100f;
        float fill = Fill(hud, StaminaFill);
        string label = StaminaLabel(hud);
        Check(Mathf.Abs(expected - expectedPercent) < 0.05f && Mathf.Abs(fill - expected / 100f) < 0.001f && label == Mathf.RoundToInt(expected) + "%",
            $"{what}: 스태미나 {player.Stamina:F1}/{player.MaxStamina} → 바 {fill * 100f:F1}% 글자 \"{label}\" (기대 {expectedPercent:F1}%)");
    }

    // 연결 코드(실행 순서 -10)는 PlayerMovement.Update보다 먼저 돌아 그 프레임이 시작될 때의 스태미나를 넘긴다 - 직전 프레임 값이나 현재 값과 같으면 된다.
    private static bool Tracks(float targetPercent, PlayerMovement player, float previousStamina) =>
        Mathf.Abs(targetPercent - previousStamina / player.MaxStamina * 100f) <= PercentTolerance ||
        Mathf.Abs(targetPercent - player.Stamina / player.MaxStamina * 100f) <= PercentTolerance;

    // HUD가 부드럽게 따라가는 표시값이 목표값에 닿을 때까지 기다린다(에셋 기본 180%/초, 실제 시간).
    private static IEnumerator Settle(AnimalKeeperHUD hud)
    {
        yield return WaitFrames(2);
        float until = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < until &&
               (Mathf.Abs(Fill(hud, HpFill) - hud.healthPercent / 100f) > 0.0005f || Mathf.Abs(Fill(hud, StaminaFill) - hud.staminaPercent / 100f) > 0.0005f))
            yield return null;
        yield return WaitFrames(1);
    }

    // 게임 시간을 멈춘 채(스태미나가 더 바뀌지 않게) 표시값이 따라오기를 기다린다.
    private static IEnumerator SettleFrozen(AnimalKeeperHUD hud)
    {
        yield return WaitFrames(1); // 쉬기 입력이 반영된 프레임
        Time.timeScale = 0f;
        yield return Settle(hud);
        Time.timeScale = 1f;
    }

    private static IEnumerator LoadAndCheckFirstFrame(GameManager gm, string scene, string what)
    {
        SceneManager.LoadScene(scene);
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < until) yield return null;
        yield return CheckFirstFrame(gm, what);
    }

    // 씬이 활성화된 다음 프레임(처음 그려지는 프레임) - HUD가 100%에서 내려오지 않고 바로 맞는 값이어야 한다.
    private static IEnumerator CheckFirstFrame(GameManager gm, string what)
    {
        string scene = SceneManager.GetActiveScene().name;
        yield return WaitFrames(1);
        PlayerStatusHUD bridge = Object.FindAnyObjectByType<PlayerStatusHUD>();
        bool expectHud = scene == "TruckScene" || scene == "VillageScene";
        if (!expectHud)
        {
            Check(bridge == null && Object.FindAnyObjectByType<AnimalKeeperHUD>(FindObjectsInactive.Include) == null, $"{what}: {scene}에는 체력·스태미나 바가 없다");
            yield break;
        }
        if (bridge == null || bridge.Hud == null) { Check(false, $"{what}: {scene}에 PlayerStatusHUD/AnimalKeeperHUD가 없다"); yield break; }
        AnimalKeeperHUD hud = bridge.Hud;
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        PlayerMovement player = playerObject != null ? playerObject.GetComponent<PlayerMovement>() : null;
        float hp = gm.Health / (float)GameManager.MaxHealth;
        float st = player != null ? player.Stamina / player.MaxStamina : 0f;
        float hpFill = Fill(hud, HpFill), stFill = Fill(hud, StaminaFill);
        Check(Mathf.Abs(hpFill - hp) < 0.001f && Mathf.Abs(stFill - st) < 0.001f,
            $"{what}: {scene} 첫 프레임 체력 바 {hpFill * 100f:F1}% (체력 {gm.Health}/{GameManager.MaxHealth}), 스태미나 바 {stFill * 100f:F1}%");
    }

    private static IEnumerator CheckStructure(string scene, string mustBeBelow)
    {
        yield return WaitFrames(1);
        Check(Object.FindAnyObjectByType<HealthUI>(FindObjectsInactive.Include) == null && Object.FindAnyObjectByType<StaminaUI>(FindObjectsInactive.Include) == null,
            $"{scene}: 예전 HealthUI/StaminaUI 바가 없다");
        PlayerStatusHUD[] bridges = Object.FindObjectsByType<PlayerStatusHUD>(FindObjectsInactive.Include);
        AnimalKeeperHUD[] huds = Object.FindObjectsByType<AnimalKeeperHUD>(FindObjectsInactive.Include);
        Check(bridges.Length == 1 && huds.Length == 1 && bridges[0].Hud == huds[0], $"{scene}: PlayerStatusHUD {bridges.Length}개, AnimalKeeperHUD {huds.Length}개, 연결됨");
        if (huds.Length == 0) yield break;
        AnimalKeeperHUD hud = huds[0];
        Canvas hudCanvas = hud.GetComponentInChildren<Canvas>(true);
        Canvas root = hud.GetComponentInParent<Canvas>(true);
        CanvasScaler scaler = hudCanvas != null ? hudCanvas.rootCanvas.GetComponent<CanvasScaler>() : null;
        Check(hudCanvas != null && !hudCanvas.isRootCanvas && !hudCanvas.overrideSorting && scaler != null &&
              scaler.referenceResolution == new Vector2(2240f, 1260f) && Mathf.Approximately(scaler.matchWidthOrHeight, 0.5f),
            $"{scene}: HUD Canvas는 씬 Canvas \"{hudCanvas?.rootCanvas.name}\"(기준 {scaler?.referenceResolution}, Match {scaler?.matchWidthOrHeight}) 안의 중첩 Canvas");
        RectTransform canvasRect = hudCanvas != null ? (RectTransform)hudCanvas.transform : null;
        RectTransform rootRect = (RectTransform)hudCanvas.rootCanvas.transform;
        Check(canvasRect != null && Vector2.Distance(canvasRect.rect.size, rootRect.rect.size) < 1f, $"{scene}: HUD Canvas 크기 {canvasRect?.rect.size} = 씬 Canvas {rootRect.rect.size}");
        Check(HpDigits.GetValue(hud) is Image[] digits && digits.Length > 0 && digits[0] != null && digits[0].sprite != null, $"{scene}: 숫자 그림 글자 {(HpDigits.GetValue(hud) as Image[])?[0]?.sprite?.texture?.name}");
        if (mustBeBelow != null)
        {
            Transform panel = rootRect.Find(mustBeBelow);
            Check(panel != null && hud.transform.GetSiblingIndex() < panel.GetSiblingIndex(),
                $"{scene}: HUD는 {mustBeBelow}보다 먼저 그려진다(형제 순서 {hud.transform.GetSiblingIndex()} < {panel?.GetSiblingIndex()}) - 메뉴가 열리면 메뉴 아래에 있다");
        }
        Check(Mathf.Approximately(hud.hudScale, 0.875f) && hud.topLeftOffset == new Vector2(21f, 21f), $"{scene}: Hud Scale {hud.hudScale}, Top Left Offset {hud.topLeftOffset}");
    }

    private static IEnumerator CaptureScene(string scene, AnimalKeeperHUD hud)
    {
        foreach (Vector2Int res in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
        {
            string file = $"{CaptureFolder}/{scene}_{res.x}x{res.y}.png";
            CaptureWithOverlapCheck(file, res, scene, hud);
            sb.AppendLine($"  캡처: {Path.GetFullPath(file)}");
            yield return WaitFrames(1);
        }
    }

    // 메인 카메라와 화면 오버레이 UI를 함께 res 크기로 렌더해 저장한다(오버레이 Canvas를 잠깐 카메라 모드로 돌리고 되돌린다).
    // 같은 배치에서 HUD 그래픽의 화면 영역이 다른 UI(모서리 장식·인벤토리·무기 슬롯·상단 정보 등 씬의 다른 그래픽, 하단 자막·구간 자막 칸)와 겹치는지 검사한다.
    private static void CaptureWithOverlapCheck(string file, Vector2Int res, string scene, AnimalKeeperHUD hud)
    {
        Camera cam = Camera.main;
        if (cam == null) { Check(false, "메인 카메라가 없어 캡처하지 못했다"); return; }
        RenderTexture rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture previousTarget = cam.targetTexture;
        cam.targetTexture = rt;

        List<Canvas> canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToList();
        var saved = new List<(Canvas canvas, CanvasScaler scaler, bool scalerOn, float scale, float plane)>();
        for (int i = 0; i < canvases.Count; i++)
        {
            Canvas c = canvases[i];
            CanvasScaler scaler = c.GetComponent<CanvasScaler>();
            saved.Add((c, scaler, scaler != null && scaler.enabled, c.scaleFactor, c.planeDistance));
            float factor = scaler != null && scaler.enabled ? ScaleFactorFor(scaler, res) : 1f;
            if (scaler != null) scaler.enabled = false;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = cam.nearClipPlane + 1f + (canvases.Count - i) * 0.01f;
            c.scaleFactor = factor;
        }
        Canvas.ForceUpdateCanvases();
        foreach (TMPro.TMP_Text text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Exclude)) text.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        cam.Render();

        HashSet<Graphic> hudGraphics = new HashSet<Graphic>(hud.GetComponentsInChildren<Graphic>(false));
        Rect hudRect = Rect.zero;
        bool first = true;
        foreach (Graphic g in hudGraphics)
        {
            Rect r = ScreenRect(cam, g.rectTransform);
            hudRect = first ? r : Rect.MinMaxRect(Mathf.Min(hudRect.xMin, r.xMin), Mathf.Min(hudRect.yMin, r.yMin), Mathf.Max(hudRect.xMax, r.xMax), Mathf.Max(hudRect.yMax, r.yMax));
            first = false;
        }
        List<(string name, Rect rect)> others = new List<(string, Rect)>();
        foreach (Graphic g in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude))
        {
            if (hudGraphics.Contains(g) || g.canvas == null || !canvases.Contains(g.canvas.rootCanvas)) continue; // 화면 HUD만(월드 공간 Canvas 제외)
            others.Add(($"{g.transform.parent?.name}/{g.name}", ScreenRect(cam, g.rectTransform)));
        }
        SubtitleManager subtitle = Object.FindAnyObjectByType<SubtitleManager>();
        if (subtitle != null)
            foreach (TMPro.TMP_Text t in subtitle.GetComponentsInChildren<TMPro.TMP_Text>(true)) others.Add(("자막 칸(하단 자막·구간 자막)", ScreenRect(cam, t.rectTransform)));
        List<string> hits = others.Where(o => o.rect.Overlaps(hudRect)).Select(o => o.name).Distinct().ToList();
        Check(hits.Count == 0, $"{scene} {res.x}x{res.y}: HUD 영역 {Fmt(hudRect)}(왼쪽 위 기준 x {hudRect.xMin:F0}, y {res.y - hudRect.yMax:F0}, {hudRect.width:F0}x{hudRect.height:F0}px)가 다른 UI {others.Count}개와 겹치지 않는다" +
                               (hits.Count > 0 ? $" - 겹침: {string.Join(", ", hits.Take(6))}" : ""));
        float scale = ScaleFactorFor(hud.GetComponentInParent<Canvas>().rootCanvas.GetComponent<CanvasScaler>(), res);
        Check(Mathf.Abs(hudRect.xMin - 21f * scale) < 2f && Mathf.Abs(res.y - hudRect.yMax - 21f * scale) < 2f,
            $"{scene} {res.x}x{res.y}: 왼쪽 위 여백 (21, 21) x 배율 {scale:F3} = {21f * scale:F1}px");

        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res.x, res.y, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, res.x, res.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());

        foreach (var s in saved)
        {
            s.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s.canvas.worldCamera = null;
            s.canvas.planeDistance = s.plane;
            if (s.scaler != null) s.scaler.enabled = s.scalerOn;
            if (s.scaler == null || !s.scalerOn) s.canvas.scaleFactor = s.scale;
        }
        cam.targetTexture = previousTarget;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static string Fmt(Rect r) => $"({r.xMin:F0}~{r.xMax:F0}, {r.yMin:F0}~{r.yMax:F0})";

    private static Rect ScreenRect(Camera cam, RectTransform rt)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 c in corners)
        {
            Vector3 s = cam.WorldToScreenPoint(c);
            min = Vector2.Min(min, s);
            max = Vector2.Max(max, s);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static float ScaleFactorFor(CanvasScaler scaler, Vector2Int res)
    {
        if (scaler == null) return 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;
        Vector2 reference = scaler.referenceResolution;
        switch (scaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand: return Mathf.Min(res.x / reference.x, res.y / reference.y);
            case CanvasScaler.ScreenMatchMode.Shrink: return Mathf.Max(res.x / reference.x, res.y / reference.y);
            default:
                return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(res.x / reference.x, 2f), Mathf.Log(res.y / reference.y, 2f), scaler.matchWidthOrHeight));
        }
    }

    private static void RestartClass(GameManager gm, PlayerClass cls)
    {
        gm.UnlockClassFree(cls);
        gm.SelectClass(cls);
        gm.CompleteStartingClassSelection();
    }

    private static void DisableMonsters()
    {
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }
}
