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

/// <summary>
/// 마을 해질녘(시간에 따라 어두워짐) 검사. Tools > Lighting > Run Dusk Test, 결과는 Logs/DuskTest.txt, 그림은 Logs/Dusk/dusk_field_forest.png.
/// batchmode: -executeMethod DuskTest.RunBatch (끝나면 통과 0 / 실패 1로 종료). 실제 렌더가 필요하므로 -nographics 없이 돌린다.
/// 마을: 격자 3x4 시드 2(Field가 트럭 방이 아니다), 트래퍼. 시간 소환·몬스터·동물은 끄고 시험할 것만 둔다.
/// 1. 설정값(GameBalanceConfig 해질녘 항목)과 시간별 라이트 값: 원형 시야 반경이 공식대로, 손전등 반경·각도와 트럭 시야는 시간과 무관.
/// 2. 0·60·120·180초: 시야 판정 경계(반경 -0.2 보임 / +0.2 안 보임)와 화면이 같다 - 반경 안 몬스터·동물은 화면에 보이고 판정도 보임(눈 발광),
///    반경 밖은 화면에 안 보이고(있을 때·없을 때 같은 그림) 판정도 안 보임(눈 꺼짐).
/// 3. 240초·300초·400초: 해질녘을 끈(지금 밤) 그림과 픽셀까지 같고 라이트 값이 밤 값과 같다. 2와 같은 화면·판정 검사도 한다.
/// 4. 밝을 때(0초) 박쥐: 원형 시야 안(판정 보임)이어도 손전등 밖이면 멈추지 않고, 손전등 정면이면 멈춘다.
/// 5. 트럭에 다녀와도 시간이 이어지고 밝기가 그 시간을 따른다. 다음 날(ResetDay, 2일차)은 0초 해질녘부터 같은 연출.
/// 6. 시간별 색(약·중·강): 원형 시야 빛 색과 화면에 더하는 색이 0초 주황 노을 → 120초 분홍 보라 황혼 → 240초 밤 색(구간 색)으로 바뀐다.
/// 7. 노을빛 속 색 구분(프로젝트 세기, 지금 '약'): 0·60·120·180·240초에 늑대·동물·플레이어의 화면 평균 색 사이 거리가,
///    같은 시각 노을 색을 뺀 빛(세기 0 = 밤 빛 색·해질녘 끝 어둠 색, 반경·밝기는 같음)에서의 거리의 70% 이상이다(노을빛이 구분을 크게 해치지 않는다).
/// 그림: Field(위 줄)와 숲(아래 줄)에서 0·60·120·180·240초(왼쪽부터). 플레이어는 오른쪽을 보고, 왼쪽 뒤 6.5칸에 늑대, 아래 왼쪽 5칸에 동물.
/// 노을 그림(Logs/Dusk/sunset/dusk_sunset_strengths.png): 위에서부터 약 Field·약 숲·중 Field·중 숲·강 Field·강 숲, 왼쪽부터 0·60·120·180·240초.
///   가까운 늑대(왼쪽 위 3칸)·동물(왼쪽 아래 3칸)은 늘 시야 안, 먼 늑대(왼쪽 10칸)는 0·60초에만 시야 안.
/// </summary>
[InitializeOnLoad]
public static class DuskTest
{
    private const string ReportPath = "Logs/DuskTest.txt";
    private const string ImagePath = "Logs/Dusk/dusk_field_forest.png";
    private const string SunsetImagePath = "Logs/Dusk/sunset/dusk_sunset_strengths.png";
    private const int SunsetTileW = 768, SunsetTileH = 432;
    private const float DistinctMinRatio = 0.7f;
    private const string KeyActive = "DT.Active", KeyBatch = "DT.Batch", KeyDone = "DT.Done", KeyPassed = "DT.Passed";
    private const int Seed = 2;
    private const int W = 1920, H = 1080;
    private const int TileW = 960, TileH = 540;
    private const int InvisibleTolerance = 3;
    private const int VisibleMinDiff = 15;
    private const float RadiusTolerance = 0.03f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static int checks;
    private static readonly List<string> logs = new List<string>();
    private static GameManager gm;
    private static GameBalanceConfig config;
    private static Camera cam;
    private static GameObject player;
    private static Vector2 lookDirection = Vector2.right;
    private static Mouse vmouse;
    private static readonly List<InputDevice> disabledMice = new List<InputDevice>();
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static IList rooms;
    private static float savedDuskDuration = -1f;
    private static GameBalanceConfig.DuskColorStrength? savedStrength;

    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);
    private static readonly FieldInfo AmbientField = typeof(PlayerVision).GetField("ambientVisionLight", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo FocusedField = typeof(PlayerVision).GetField("focusedVisionLight", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly string WolfPath = "Assets/Prefabs/Monsters/PatrolMonster.prefab";
    private static readonly string BatPath = "Assets/Prefabs/Monsters/BatMonster.prefab";
    private static readonly string AnimalPath = "Assets/Prefabs/Animals/AnimalWander.prefab";

    static DuskTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/Lighting/Run Dusk Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[DuskTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 1f / 60f;
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

    private static void SetUpInput()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        disabledMice.Clear();
        foreach (InputDevice d in InputSystem.devices.ToList())
            if (d is Mouse && d.enabled) { InputSystem.DisableDevice(d); disabledMice.Add(d); }
        vmouse = InputSystem.AddDevice<Mouse>("DuskTestMouse");
        vmouse.MakeCurrent();
    }

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        RestoreDusk();
        RestoreStrength();
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        VillageMapGenerator.EditorLayoutOverride = null;
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vmouse = null;
        foreach (InputDevice d in disabledMice) if (d != null && d.added) InputSystem.EnableDevice(d);
        disabledMice.Clear();
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; TearDown(); return; }
        if (player != null)
        {
            LookSetter.Invoke(player.GetComponent<PlayerMovement>(), new object[] { lookDirection });
            if (vmouse != null && cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint(player.transform.position + (Vector3)(lookDirection.normalized * 3f));
                InputSystem.QueueStateEvent(vmouse, new MouseState { position = new Vector2(screen.x, screen.y) });
            }
        }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            more = false;
        }
        if (!more)
        {
            Finish();
            EditorApplication.update -= Step;
            TearDown();
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
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
        Debug.Log("[DuskTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // 에디터 업데이트 횟수가 아니라 실제 플레이어 루프 프레임(Time.frameCount)이 n개 지나갈 때까지 기다린다 - 에디터 업데이트는 한 프레임에 여러 번 돌 수 있다.
    private static IEnumerator Frames(int n) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }
    private static IEnumerator WaitScene(string name) { float until = Time.realtimeSinceStartup + 30f; while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null; yield return Frames(5); }

    // ===================== 시나리오 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        checks = 0;
        sb.AppendLine($"DuskTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return Frames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitScene("ClassSelectScene");
        gm = GameManager.Instance;
        config = gm.Config;
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.SelectClass(PlayerClass.Trapper);
        gm.CompleteStartingClassSelection();
        yield return EnterVillage(true);

        yield return Settings();
        yield return DuskColors();
        yield return Distinct();
        yield return TimeSteps();
        yield return NightEqualsOld();
        yield return BatWhenBright();
        yield return Strip();
        yield return SunsetStrip();
        yield return TruckAndNextDay();
    }

    private static IEnumerator EnterVillage(bool gridOverride)
    {
        Time.timeScale = 1f;
        if (gridOverride) VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = Seed };
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        while (GameObject.Find("GeneratedMap") == null) yield return null;
        yield return Frames(10);
        VillageMapGenerator.EditorLayoutOverride = null;
        PrepareVillage();
    }

    private static void PrepareVillage()
    {
        cam = Camera.main;
        cam.aspect = (float)W / H;
        player = GameObject.FindGameObjectWithTag("Player");
        foreach (MonoBehaviour b in cam.GetComponents<MonoBehaviour>()) if (b is CameraFollow) b.enabled = false;
        if (gm.GetComponent<VillageMonsterSpawner>() != null) gm.GetComponent<VillageMonsterSpawner>().enabled = false;
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        rooms = (IList)typeof(VillageMapGenerator).GetField("gridRooms", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gen);
    }

    // ---- 1. 설정과 시간별 라이트 값
    private static IEnumerator Settings()
    {
        sb.AppendLine("\n== 1. 설정과 시간별 라이트 값 (트래퍼)");
        Check(Mathf.Approximately(config.duskDurationSeconds, 240f) && Mathf.Approximately(config.duskStartAmbientVisionRadius, 14f) && Mathf.Approximately(config.duskStartLightIntensity, 1f),
            $"GameBalanceConfig: 어두워지는 시간 {config.duskDurationSeconds}초, 시작 시야 반경 {config.duskStartAmbientVisionRadius}칸, 시작 밝기 {config.duskStartLightIntensity}, " +
            $"시작 안쪽 비율 {config.duskStartInnerRadiusRatio}, 황혼 {config.duskTwilightSeconds}초, 색 세기 {config.duskColorStrength}({config.DuskColorStrengthValue})");
        Check(config.duskColorStrength == GameBalanceConfig.DuskColorStrength.Weak && Mathf.Approximately(config.duskTwilightSeconds, 120f) &&
              config.duskColorStrengthWeak < config.duskColorStrengthMedium && config.duskColorStrengthMedium < config.duskColorStrengthStrong,
            $"색 세기: 프로젝트 값 '약'({config.duskColorStrength}), 약 {config.duskColorStrengthWeak} < 중 {config.duskColorStrengthMedium} < 강 {config.duskColorStrengthStrong}, 황혼 {config.duskTwilightSeconds}초");
        Check(Mathf.Approximately(config.duskDurationSeconds, config.villagePhaseStartSeconds[2]),
            $"어두워지는 시간 {config.duskDurationSeconds}초 = 3구간 시작(2구간 끝) {config.villagePhaseStartSeconds[2]}초");

        Light2D ambient = Ambient(), focused = Focused(), truck = PlayerVision.Current.TruckVisionLight;
        float nightR = NightRadius();
        float focusR = config.GetClassFocusedVisionRadius(PlayerClass.Trapper) * (PassiveEffects.HasFlashlight ? 1f : config.noFlashlightFocusedRadiusMultiplier);
        float focusA = config.GetClassFocusedVisionAngle(PlayerClass.Trapper) * (PassiveEffects.HasFlashlight ? 1f : config.noFlashlightFocusedAngleMultiplier);
        foreach (float t in new[] { 0f, 60f, 120f, 180f, 240f, 300f, 400f })
        {
            yield return SetTimeFrozen(t);
            float want = ExpectedRadius(t);
            Check(Mathf.Abs(ambient.pointLightOuterRadius - want) <= RadiusTolerance,
                $"{t,3:0}초: 원형 시야 반경 {ambient.pointLightOuterRadius:F3} = 기대 {want:F3} (밤 {nightR}), 안쪽 {ambient.pointLightInnerRadius:F2}, 밝기 {ambient.intensity:F2}, 색 {Hex(ambient.color)}");
            Check(Mathf.Approximately(focused.pointLightOuterRadius, focusR) && Mathf.Approximately(focused.pointLightOuterAngle, focusA) &&
                  truck != null && Mathf.Approximately(truck.pointLightOuterRadius, config.truckVisionRadius) && truck.color == config.ambientVisionLightColor &&
                  Mathf.Approximately(truck.intensity, config.visionLightIntensity) && Mathf.Approximately(truck.pointLightInnerRadius, 0f),
                $"{t,3:0}초: 손전등 반경 {focused.pointLightOuterRadius} = {focusR}, 각도 {focused.pointLightOuterAngle} = {focusA}, 트럭 시야 반경 {truck?.pointLightOuterRadius} = {config.truckVisionRadius}, 트럭 색·밝기·안쪽 반경 그대로");
        }
    }

    // ---- 2. 시간별 화면과 시야 판정 (해질녘 동안)
    private static IEnumerator TimeSteps()
    {
        sb.AppendLine("\n== 2. 해질녘 동안 화면 = 시야 판정 (Field, 플레이어는 오른쪽을 봄, 손전등 밖 방향만)");
        object field = RoomOf("Room_Field");
        Vector2 pp = PickPlayerSpot(field);
        yield return PlacePlayer(pp, Vector2.right);
        foreach (float t in new[] { 0f, 60f, 120f, 180f })
            yield return VisionMatchesScreen(t, pp);
    }

    // 지금 시각 t에서: 경계 판정, 반경 안·밖 늑대와 동물의 화면·판정·눈 발광.
    private static IEnumerator VisionMatchesScreen(float t, Vector2 pp)
    {
        yield return SetTimeFrozen(t);
        float r = Ambient().pointLightOuterRadius;
        Vector2 truck = TruckCenter();
        // 경계: 손전등(부채꼴) 밖, 트럭 시야 밖 방향만 센다.
        float coneHalf = Focused().pointLightOuterAngle * 0.5f + 10f;
        var dirs = Enumerable.Range(0, 24).Select(i => (Vector2)(Quaternion.Euler(0, 0, i * 15f) * Vector2.right))
            .Where(d => Vector2.Angle(lookDirection, d) > coneHalf)
            .Where(d => Vector2.Distance(pp + d * r, truck) > config.truckVisionRadius + 0.5f).ToList();
        int inOk = dirs.Count(d => PlayerVision.IsVisibleToPlayer(pp + d * (r - 0.2f)));
        int outOk = dirs.Count(d => !PlayerVision.IsVisibleToPlayer(pp + d * (r + 0.2f)));
        Check(dirs.Count >= 8 && inOk == dirs.Count && outOk == dirs.Count,
            $"{t,3:0}초 경계 판정(반경 {r:F2}): {r - 0.2f:F2}칸 {dirs.Count}방향 중 {inOk} 보임, {r + 0.2f:F2}칸 {dirs.Count}방향 중 {outOk} 안 보임");

        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath);
        GameObject animalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimalPath);
        float dIn = Mathf.Max(1.8f, r * 0.6f), dOut = r + 1.2f;
        Vector2? inPos = FloorAround(pp, dIn, truck), outPos = FloorAround(pp, dOut, truck);
        Check(inPos.HasValue && outPos.HasValue, $"{t,3:0}초 시험 자리: 안 {inPos?.ToString() ?? "없음"} ({dIn:F1}칸), 밖 {outPos?.ToString() ?? "없음"} ({dOut:F1}칸)");
        if (!inPos.HasValue || !outPos.HasValue) yield break;

        // 반경 안 늑대: 판정 보임, 눈 발광 = 눈 프레임, 화면에 보임
        GameObject m = Spawn(wolf, inPos.Value);
        yield return Freeze(m, t);
        MonsterEyeGlow glow = m.GetComponent<MonsterEyeGlow>();
        bool seen = PlayerVision.IsVisibleToPlayer(m.GetComponent<MonsterHealth>().GamePosition);
        bool hasEye = glow.HasEyeThisFrame, glowing = glow.IsGlowing; // DiffWithout이 대상을 껐다 켜면 OnDisable이 발광을 끄므로 먼저 읽는다
        int diff = DiffWithout(m);
        Check(seen && glowing == hasEye && diff >= VisibleMinDiff,
            $"{t,3:0}초 반경 안 늑대({Vector2.Distance(pp, inPos.Value):F1}칸): 판정 보임 {seen}, 눈 프레임 {hasEye}·발광 {glowing}, 화면 차이 {diff} (≥{VisibleMinDiff})");
        yield return Remove(m, t);

        // 반경 밖 늑대: 판정 안 보임, 눈 꺼짐, 화면에 안 보임(몸·눈)
        m = Spawn(wolf, outPos.Value);
        yield return Freeze(m, t);
        glow = m.GetComponent<MonsterEyeGlow>();
        seen = PlayerVision.IsVisibleToPlayer(m.GetComponent<MonsterHealth>().GamePosition) || PlayerVision.IsVisibleToPlayer(glow.EyeWorldPosition);
        glowing = glow.IsGlowing;
        diff = DiffWithout(m);
        Check(!seen && !glowing && diff <= InvisibleTolerance,
            $"{t,3:0}초 반경 밖 늑대({Vector2.Distance(pp, outPos.Value):F1}칸): 판정 안 보임 {!seen}, 눈 꺼짐 {!glowing}, 화면 차이 {diff} (≤{InvisibleTolerance})");
        yield return Remove(m, t);

        // 동물도 같다.
        GameObject a = SpawnAnimal(animalPrefab, inPos.Value);
        yield return FreezeAnimal(t);
        seen = PlayerVision.IsVisibleToPlayer(a.transform.position);
        diff = DiffWithout(a);
        Check(seen && diff >= VisibleMinDiff, $"{t,3:0}초 반경 안 동물: 판정 보임 {seen}, 화면 차이 {diff} (≥{VisibleMinDiff})");
        yield return Remove(a, t);
        a = SpawnAnimal(animalPrefab, outPos.Value);
        yield return FreezeAnimal(t);
        seen = PlayerVision.IsVisibleToPlayer(a.transform.position);
        diff = DiffWithout(a);
        Check(!seen && diff <= InvisibleTolerance, $"{t,3:0}초 반경 밖 동물: 판정 안 보임 {!seen}, 화면 차이 {diff} (≤{InvisibleTolerance})");
        yield return Remove(a, t);
        cam.transform.position = new Vector3(pp.x, pp.y, cam.transform.position.z);
    }

    // ---- 3. 240초 뒤에는 지금 밤과 같다
    private static IEnumerator NightEqualsOld()
    {
        sb.AppendLine("\n== 3. 240초부터는 지금 밤과 같다 (해질녘을 끈 그림과 비교, Field)");
        object field = RoomOf("Room_Field");
        Vector2 pp = PickPlayerSpot(field);
        yield return PlacePlayer(pp, Vector2.right);
        Light2D ambient = Ambient();
        foreach (float t in new[] { 240f, 300f, 400f })
        {
            yield return SetTimeFrozen(t);
            Texture2D withDusk = Render();
            bool lightNight = Mathf.Approximately(ambient.pointLightOuterRadius, NightRadius()) && Mathf.Approximately(ambient.pointLightInnerRadius, 0f) &&
                Mathf.Approximately(ambient.intensity, config.visionLightIntensity) && ambient.color == config.ambientVisionLightColor;
            Color tint = cam.GetComponent<VillageNightTint>().CurrentColor;
            DisableDusk();
            yield return Frames(2);
            Texture2D old = Render();
            RestoreDusk();
            yield return Frames(2);
            int d = MaxDiff(withDusk, old, new RectInt(0, 0, W, H));
            Object.DestroyImmediate(withDusk); Object.DestroyImmediate(old);
            Check(lightNight && d == 0 && tint == config.GetVillagePhaseDarknessColor(gm.VillagePhase),
                $"{t,3:0}초({gm.VillagePhase}구간): 원형 시야 반경 {ambient.pointLightOuterRadius}·안쪽 {ambient.pointLightInnerRadius}·밝기 {ambient.intensity}·색 {Hex(ambient.color)} = 밤 값 {lightNight}, " +
                $"어둠 색 {Hex(tint)} = 구간 색, 해질녘을 끈 그림과 최대 차이 {d} (0)");
        }
        yield return VisionMatchesScreen(240f, pp);
        yield return VisionMatchesScreen(300f, pp);
    }

    // ---- 4. 밝을 때 박쥐
    private static IEnumerator BatWhenBright()
    {
        sb.AppendLine("\n== 4. 밝을 때(0초) 박쥐: 손전등 정면에서만 멈춘다");
        object field = RoomOf("Room_Field");
        Vector2 pp = PickPlayerSpot(field);
        yield return PlacePlayer(pp, Vector2.right);
        yield return SetTimeFrozen(0f);
        Time.timeScale = 1f;
        GameObject batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BatPath);
        Vector2 truck = TruckCenter();

        // 원형 시야 안, 손전등 밖(뒤 5칸): 멈추지 않고 다가온다.
        Vector2? back = FloorAround(pp, 5f, truck);
        Check(back.HasValue, "뒤쪽 박쥐 자리");
        if (back.HasValue)
        {
            GameObject bat = Spawn(batPrefab, back.Value);
            BatMonsterAI ai = bat.GetComponent<BatMonsterAI>();
            MonsterHealth h = bat.GetComponent<MonsterHealth>();
            yield return Frames(2);
            SetTime(0f);
            bool vis = PlayerVision.IsVisibleToPlayer(h.GamePosition), foc = PlayerVision.IsInFocusedVision(h.GamePosition);
            Vector2 start = h.GamePosition;
            int frozen = 0;
            // 박쥐가 플레이어에게 다가와 손전등 부채꼴에 들어가기 전까지(손전등 밖이고 원형 시야 안인 프레임)만 센다.
            int counted = 0;
            for (int i = 0; i < 60; i++)
            {
                SetTime(0f);
                yield return null;
                Vector2 bp = h.GamePosition;
                if (PlayerVision.IsInFocusedVision(bp) || Vector2.Distance(bp, pp) < 1.2f) break;
                if (!PlayerVision.IsVisibleToPlayer(bp)) continue;
                counted++;
                if (ai.IsFrozen) frozen++;
            }
            float moved = Vector2.Distance(start, h.GamePosition);
            Check(vis && !foc && counted >= 15 && frozen == 0 && moved > 0.5f,
                $"뒤 {Vector2.Distance(pp, start):F1}칸 박쥐: 원형 시야 판정 보임 {vis}, 손전등 {foc}, 손전등 밖·원형 시야 안 {counted}프레임 중 굳음 {frozen}, 움직인 거리 {moved:F2}칸 (> 0.5)");
            Object.Destroy(bat);
            yield return Frames(2);
        }

        // 손전등 정면(앞 4칸): 멈춘다.
        Vector2 frontWant = pp + Vector2.right * 4f;
        GameObject fbat = Spawn(batPrefab, frontWant);
        BatMonsterAI fai = fbat.GetComponent<BatMonsterAI>();
        MonsterHealth fh = fbat.GetComponent<MonsterHealth>();
        int ffrozen = 0; bool everFocused = false;
        for (int i = 0; i < 60; i++)
        {
            SetTime(0f);
            yield return null;
            if (PlayerVision.IsInFocusedVision(fh.GamePosition)) everFocused = true;
            if (fai.IsFrozen) ffrozen++;
        }
        Check(everFocused && ffrozen > 0, $"앞 {Vector2.Distance(pp, fh.GamePosition):F1}칸 박쥐(손전등 정면 {PlayerVision.IsInFocusedVision(fh.GamePosition)}): 60프레임 중 굳음 {ffrozen} (> 0)");
        Object.Destroy(fbat);
        yield return Frames(2);
    }

    // ---- 그림: Field·숲 x 0·60·120·180·240초
    private static IEnumerator Strip()
    {
        sb.AppendLine("\n== 그림 " + ImagePath + " (위 Field, 아래 숲 / 왼쪽부터 0·60·120·180·240초)");
        float[] times = { 0f, 60f, 120f, 180f, 240f };
        string[] roomNames = { "Room_Field", "Room_Forest" };
        var sheet = new Texture2D(TileW * times.Length, TileH * roomNames.Length, TextureFormat.RGB24, false);
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath), animalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimalPath);
        for (int row = 0; row < roomNames.Length; row++)
        {
            object room = RoomOf(roomNames[row]);
            Vector2 pp = PickPlayerSpot(room);
            yield return PlacePlayer(pp, Vector2.right);
            Vector2 truck = TruckCenter();
            Vector2? wp = FloorAround(pp, 6.5f, truck, Vector2.left), ap = FloorAround(pp, 5f, truck, new Vector2(-0.5f, -1f).normalized);
            GameObject m = wp.HasValue ? Spawn(wolf, wp.Value) : null;
            if (m != null) yield return Freeze(m, 0f);
            GameObject a = ap.HasValue ? SpawnAnimal(animalPrefab, ap.Value) : null;
            if (a != null) yield return FreezeAnimal(0f);
            cam.transform.position = new Vector3(pp.x, pp.y, cam.transform.position.z);
            for (int col = 0; col < times.Length; col++)
            {
                yield return SetTimeFrozen(times[col]);
                Texture2D tex = Render(TileW, TileH);
                // 아래 줄이 그림 아래쪽(텍스처 y 0)이 되도록 Field를 위 줄에 둔다.
                sheet.SetPixels(col * TileW, (roomNames.Length - 1 - row) * TileH, TileW, TileH, tex.GetPixels());
                Object.DestroyImmediate(tex);
                bool wv = m != null && PlayerVision.IsVisibleToPlayer(m.GetComponent<MonsterHealth>().GamePosition);
                bool av = a != null && PlayerVision.IsVisibleToPlayer(a.transform.position);
                sb.AppendLine($"  {roomNames[row]} {times[col],3:0}초: 원형 시야 반경 {Ambient().pointLightOuterRadius:F2}, 늑대({(wp.HasValue ? Vector2.Distance(pp, wp.Value).ToString("F1") : "-")}칸) 판정 {(wv ? "보임" : "안 보임")}, " +
                    $"동물({(ap.HasValue ? Vector2.Distance(pp, ap.Value).ToString("F1") : "-")}칸) 판정 {(av ? "보임" : "안 보임")}");
            }
            if (m != null) Object.Destroy(m);
            if (a != null) Object.Destroy(a);
            Time.timeScale = 1f;
            yield return Frames(2);
        }
        // 칸 사이 경계선(검정 2픽셀)
        for (int col = 1; col < times.Length; col++)
            for (int y = 0; y < sheet.height; y++) for (int dx = -1; dx <= 0; dx++) sheet.SetPixel(col * TileW + dx, y, Color.black);
        for (int x = 0; x < sheet.width; x++) for (int dy = -1; dy <= 0; dy++) sheet.SetPixel(x, TileH + dy, Color.black);
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(ImagePath));
        File.WriteAllBytes(ImagePath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sb.AppendLine("  저장: " + Path.GetFullPath(ImagePath));
    }

    // ---- 6. 시간별 색 (약·중·강)
    private static IEnumerator DuskColors()
    {
        sb.AppendLine("\n== 6. 시간별 색: 0초 주황 노을 → 120초 분홍 보라 황혼 → 240초 밤 색 (빛 색 x 화면에 더하는 색)");
        Light2D ambient = Ambient();
        VillageNightTint nightTint = cam.GetComponent<VillageNightTint>();
        float[] times = { 0f, 60f, 120f, 180f, 240f };
        var sat0 = new List<float>();
        foreach (GameBalanceConfig.DuskColorStrength level in new[] { GameBalanceConfig.DuskColorStrength.Weak, GameBalanceConfig.DuskColorStrength.Medium, GameBalanceConfig.DuskColorStrength.Strong })
        {
            SetStrength(level);
            var lights = new List<Color>(); var tints = new List<Color>();
            foreach (float t in times)
            {
                yield return SetTimeFrozen(t);
                float p = config.GetSteppedDuskProgress(t);
                Color wantLight = config.GetDuskAmbientLightColor(p), wantTint = config.GetDuskDarknessColor(p, gm.VillagePhase);
                lights.Add(ambient.color); tints.Add(nightTint.CurrentColor);
                Check(ambient.color == wantLight && nightTint.CurrentColor == wantTint,
                    $"{level} {t,3:0}초({gm.VillagePhase}구간): 빛 색 {Hex(ambient.color)} (색상 {Hue(ambient.color):0}°) = 설정 {Hex(wantLight)}, 더하는 색 {Hex(nightTint.CurrentColor)} (색상 {Hue(nightTint.CurrentColor):0}°) = 설정 {Hex(wantTint)}");
            }
            bool changes = true;
            for (int i = 1; i < times.Length; i++) changes &= lights[i] != lights[i - 1] && tints[i] != tints[i - 1];
            Check(changes, $"{level}: 빛 색·더하는 색이 0→60→120→180→240초마다 바뀐다");
            Check(InHue(lights[0], 10f, 45f) && InHue(tints[0], 5f, 45f) && lights[0].r > lights[0].b + 0.15f,
                $"{level} 0초 주황 노을: 빛 색상 {Hue(lights[0]):0}° (10~45°), 더하는 색 색상 {Hue(tints[0]):0}° (5~45°), 빛 R-B {lights[0].r - lights[0].b:F2} (> 0.15)");
            Check(InHue(lights[2], 270f, 330f) && InHue(tints[2], 260f, 330f) && lights[2].g < Mathf.Min(lights[2].r, lights[2].b) - 0.1f,
                $"{level} 120초 분홍 보라 황혼: 빛 색상 {Hue(lights[2]):0}° (270~330°), 더하는 색 색상 {Hue(tints[2]):0}° (260~330°), 빛 G가 R·B보다 0.1 넘게 낮다");
            Check(lights[4] == config.ambientVisionLightColor && tints[4] == config.GetVillagePhaseDarknessColor(gm.VillagePhase),
                $"{level} 240초: 빛 색 {Hex(lights[4])} = 밤 빛 색 {Hex(config.ambientVisionLightColor)}, 더하는 색 {Hex(tints[4])} = {gm.VillagePhase}구간 어둠 색");
            Color.RGBToHSV(lights[0], out _, out float sat, out _);
            sat0.Add(sat);
        }
        RestoreStrength();
        Check(sat0[0] < sat0[1] && sat0[1] < sat0[2], $"0초 빛 색 채도 약 {sat0[0]:F2} < 중 {sat0[1]:F2} < 강 {sat0[2]:F2}");
        // 해질녘 끝 바로 앞(239.9초)과 240초가 거의 같다(갑자기 튀지 않는다).
        yield return SetTimeFrozen(239.9f);
        Color l239 = ambient.color, t239 = nightTint.CurrentColor;
        yield return SetTimeFrozen(240f);
        float jump = Mathf.Max(MaxChannel(l239, ambient.color), MaxChannel(t239, nightTint.CurrentColor));
        Check(jump < 0.01f, $"239.9초 → 240초 색 차이 {jump:F4} (< 0.01, 끊김 없음)");
    }

    // ---- 7. 노을빛 속 색 구분 (프로젝트 세기)
    private static IEnumerator Distinct()
    {
        sb.AppendLine($"\n== 7. 노을빛 속 늑대·동물·플레이어 색 구분 (세기 '{config.duskColorStrength}', Field, 화면 평균 색 사이 거리(0~441) ≥ 노을 색 뺀 빛에서의 거리 x {DistinctMinRatio})");
        object field = RoomOf("Room_Field");
        Vector2 pp = PickPlayerSpot(field);
        yield return PlacePlayer(pp, Vector2.right);
        Vector2 truck = TruckCenter();
        Vector2? wp = FloorAround(pp, 2.5f, truck, new Vector2(-0.6f, 1f).normalized), ap = FloorAround(pp, 2.5f, truck, new Vector2(-0.6f, -1f).normalized);
        Check(wp.HasValue && ap.HasValue, "늑대·동물 자리 (2.5칸, 밤 시야 안)");
        if (!wp.HasValue || !ap.HasValue) yield break;
        GameObject m = Spawn(AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath), wp.Value);
        yield return Freeze(m, 0f);
        GameObject a = SpawnAnimal(AssetDatabase.LoadAssetAtPath<GameObject>(AnimalPath), ap.Value);
        yield return FreezeAnimal(0f);
        foreach (float t in new[] { 0f, 60f, 120f, 180f, 240f })
        {
            yield return SetTimeFrozen(t);
            Color cw = MeanColor(m), ca = MeanColor(a), cp = MeanColor(player);
            float dwa = Dist(cw, ca), dwp = Dist(cw, cp), dap = Dist(ca, cp);
            // 같은 시각, 노을 색만 뺀(세기 0) 빛
            float savedWeak = config.duskColorStrengthWeak;
            SetStrength(GameBalanceConfig.DuskColorStrength.Weak);
            config.duskColorStrengthWeak = 0f;
            yield return SetTimeFrozen(t);
            Color nw = MeanColor(m), na = MeanColor(a), np = MeanColor(player);
            config.duskColorStrengthWeak = savedWeak;
            RestoreStrength();
            yield return SetTimeFrozen(t);
            float nwa = Dist(nw, na), nwp = Dist(nw, np), nap = Dist(na, np);
            bool ok = dwa >= nwa * DistinctMinRatio && dwp >= nwp * DistinctMinRatio && dap >= nap * DistinctMinRatio;
            Check(ok, $"{t,3:0}초: 늑대 {Hex(cw)} · 동물 {Hex(ca)} · 플레이어 {Hex(cp)} - 거리 늑대-동물 {dwa:0} (노을 색 뺀 빛 {nwa:0}), " +
                $"늑대-플레이어 {dwp:0} ({nwp:0}), 동물-플레이어 {dap:0} ({nap:0})");
        }
        Object.Destroy(m); Object.Destroy(a);
        Time.timeScale = 1f;
        yield return Frames(2);
    }

    // ---- 노을 그림: 약·중·강 x Field·숲 x 0·60·120·180·240초
    private static IEnumerator SunsetStrip()
    {
        sb.AppendLine("\n== 노을 그림 " + SunsetImagePath + " (위에서부터 약 Field·약 숲·중 Field·중 숲·강 Field·강 숲 / 왼쪽부터 0·60·120·180·240초)");
        float[] times = { 0f, 60f, 120f, 180f, 240f };
        string[] roomNames = { "Room_Field", "Room_Forest" };
        var levels = new[] { GameBalanceConfig.DuskColorStrength.Weak, GameBalanceConfig.DuskColorStrength.Medium, GameBalanceConfig.DuskColorStrength.Strong };
        int rowsTotal = levels.Length * roomNames.Length;
        var sheet = new Texture2D(SunsetTileW * times.Length, SunsetTileH * rowsTotal, TextureFormat.RGB24, false);
        GameObject wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPath), animalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimalPath);
        for (int ri = 0; ri < roomNames.Length; ri++)
        {
            object room = RoomOf(roomNames[ri]);
            Vector2 pp = PickPlayerSpot(room);
            yield return PlacePlayer(pp, Vector2.right);
            Vector2 truck = TruckCenter();
            Vector2? wp = FloorAround(pp, 3f, truck, new Vector2(-0.7f, 1f).normalized), ap = FloorAround(pp, 3f, truck, new Vector2(-0.7f, -1f).normalized);
            Vector2? fp = FloorAround(pp, 10f, truck, Vector2.left);
            GameObject m = wp.HasValue ? Spawn(wolf, wp.Value) : null;
            if (m != null) yield return Freeze(m, 0f);
            GameObject far = fp.HasValue ? Spawn(wolf, fp.Value) : null;
            if (far != null) yield return Freeze(far, 0f);
            GameObject a = ap.HasValue ? SpawnAnimal(animalPrefab, ap.Value) : null;
            if (a != null) yield return FreezeAnimal(0f);
            cam.transform.position = new Vector3(pp.x, pp.y, cam.transform.position.z);
            for (int li = 0; li < levels.Length; li++)
            {
                SetStrength(levels[li]);
                int row = li * roomNames.Length + ri;
                for (int col = 0; col < times.Length; col++)
                {
                    yield return SetTimeFrozen(times[col]);
                    Texture2D tex = Render(SunsetTileW, SunsetTileH);
                    sheet.SetPixels(col * SunsetTileW, (rowsTotal - 1 - row) * SunsetTileH, SunsetTileW, SunsetTileH, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                    bool wv = m != null && PlayerVision.IsVisibleToPlayer(m.GetComponent<MonsterHealth>().GamePosition);
                    bool fv = far != null && (PlayerVision.IsVisibleToPlayer(far.GetComponent<MonsterHealth>().GamePosition) || far.GetComponent<MonsterEyeGlow>().IsGlowing);
                    bool av = a != null && PlayerVision.IsVisibleToPlayer(a.transform.position);
                    sb.AppendLine($"  {levels[li]} {roomNames[ri]} {times[col],3:0}초: 빛 {Hex(Ambient().color)}, 더하는 색 {Hex(cam.GetComponent<VillageNightTint>().CurrentColor)}, 반경 {Ambient().pointLightOuterRadius:F2}, " +
                        $"가까운 늑대 {(wv ? "보임" : "안 보임")}, 동물 {(av ? "보임" : "안 보임")}, 먼 늑대({(fp.HasValue ? Vector2.Distance(pp, fp.Value).ToString("F1") : "-")}칸) {(fv ? "보임" : "안 보임")}");
                }
            }
            RestoreStrength();
            if (m != null) Object.Destroy(m);
            if (far != null) Object.Destroy(far);
            if (a != null) Object.Destroy(a);
            Time.timeScale = 1f;
            yield return Frames(2);
        }
        for (int col = 1; col < times.Length; col++)
            for (int y = 0; y < sheet.height; y++) for (int dx = -1; dx <= 0; dx++) sheet.SetPixel(col * SunsetTileW + dx, y, Color.black);
        for (int row = 1; row < rowsTotal; row++)
        {
            bool levelEdge = row % roomNames.Length == 0; // 세기 단계 사이는 굵은 흰 선
            int thick = levelEdge ? 3 : 1;
            for (int x = 0; x < sheet.width; x++) for (int dy = -thick; dy < thick; dy++) sheet.SetPixel(x, row * SunsetTileH + dy, levelEdge ? Color.white : Color.black);
        }
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SunsetImagePath));
        File.WriteAllBytes(SunsetImagePath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sb.AppendLine("  저장: " + Path.GetFullPath(SunsetImagePath));
    }

    // ---- 5. 트럭 왕복, 다음 날
    private static IEnumerator TruckAndNextDay()
    {
        sb.AppendLine("\n== 5. 트럭에 다녀와도 시간에 맞는 밝기 / 2일차도 같은 연출");
        yield return SetTimeFrozen(150f);
        Time.timeScale = 1f;
        float before = gm.VillageTime;
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        yield return Frames(30);
        float inTruck = gm.VillageTime;
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        // 다시 들어온 뒤 처음 라이트가 적용된 프레임의 값
        while (PlayerVision.Current == null || PlayerVision.Current.AppliedDuskProgress < 0f) yield return null;
        float tFirst = gm.VillageTime;
        float rFirst = Ambient().pointLightOuterRadius;
        Check(Mathf.Abs(inTruck - before) < 0.001f && tFirst >= before - 0.001f && tFirst < before + 1f,
            $"트럭에 있는 동안 마을 시간 그대로({before:F2} → {inTruck:F2}), 다시 들어와 이어짐({tFirst:F2})");
        Check(Mathf.Abs(rFirst - ExpectedRadius(tFirst)) <= RadiusTolerance,
            $"다시 들어온 첫 프레임 원형 시야 반경 {rFirst:F3} = 시각 {tFirst:F2}초의 값 {ExpectedRadius(tFirst):F3} (처음 들어올 때 {config.duskStartAmbientVisionRadius}칸 아님)");
        while (GameObject.Find("GeneratedMap") == null) yield return null;
        yield return Frames(5);
        PrepareVillage();
        Vector2 pp = FarFromTruck();
        float tNow = gm.VillageTime;
        yield return PlacePlayer(pp, Vector2.right);
        yield return BoundaryOnly(tNow, pp, "트럭 다녀온 뒤");

        // 2일차
        int dayBefore = gm.CycleCount;
        gm.ResetDay();
        SceneManager.LoadScene("TruckScene");
        yield return WaitScene("TruckScene");
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        while (PlayerVision.Current == null || PlayerVision.Current.AppliedDuskProgress < 0f) yield return null;
        float t2 = gm.VillageTime, r2 = Ambient().pointLightOuterRadius;
        Check(gm.CycleCount == dayBefore + 1 && t2 < 1f && Mathf.Abs(r2 - ExpectedRadius(t2)) <= RadiusTolerance && r2 > NightRadius() + 9f,
            $"{gm.CycleCount}일차 첫 프레임: 마을 시간 {t2:F2}초, 원형 시야 반경 {r2:F3} = 기대 {ExpectedRadius(t2):F3} (해질녘 시작)");
        while (GameObject.Find("GeneratedMap") == null) yield return null;
        yield return Frames(5);
        PrepareVillage();
        pp = FarFromTruck();
        yield return PlacePlayer(pp, Vector2.right);
        foreach (float t in new[] { 0f, 120f, 240f })
        {
            yield return SetTimeFrozen(t);
            Check(Mathf.Abs(Ambient().pointLightOuterRadius - ExpectedRadius(t)) <= RadiusTolerance,
                $"{gm.CycleCount}일차 {t,3:0}초: 원형 시야 반경 {Ambient().pointLightOuterRadius:F3} = {ExpectedRadius(t):F3}");
            yield return BoundaryOnly(t, pp, $"{gm.CycleCount}일차");
        }
    }

    private static IEnumerator BoundaryOnly(float t, Vector2 pp, string label)
    {
        yield return SetTimeFrozen(t);
        float r = Ambient().pointLightOuterRadius;
        Vector2 truck = TruckCenter();
        float coneHalf = Focused().pointLightOuterAngle * 0.5f + 10f;
        var dirs = Enumerable.Range(0, 24).Select(i => (Vector2)(Quaternion.Euler(0, 0, i * 15f) * Vector2.right))
            .Where(d => Vector2.Angle(lookDirection, d) > coneHalf)
            .Where(d => Vector2.Distance(pp + d * r, truck) > config.truckVisionRadius + 0.5f).ToList();
        int inOk = dirs.Count(d => PlayerVision.IsVisibleToPlayer(pp + d * (r - 0.2f)));
        int outOk = dirs.Count(d => !PlayerVision.IsVisibleToPlayer(pp + d * (r + 0.2f)));
        Check(dirs.Count >= 6 && inOk == dirs.Count && outOk == dirs.Count,
            $"{label} {t:0.##}초 경계 판정(반경 {r:F2}): {dirs.Count}방향 안 {inOk} 보임 / 밖 {outOk} 안 보임");
    }

    // ===================== 도움 함수 =====================

    private static Light2D Ambient() => (Light2D)AmbientField.GetValue(PlayerVision.Current);
    private static Light2D Focused() => (Light2D)FocusedField.GetValue(PlayerVision.Current);
    private static float NightRadius() => config.GetClassAmbientVisionRadius(gm.CurrentClass);

    // PlayerVision과 같은 계산(진행도는 DuskProgressSteps 단위로 끊는다).
    private static float ExpectedRadius(float t) => config.GetDuskAmbientVisionRadius(NightRadius(), config.GetSteppedDuskProgress(t));

    private static void DisableDusk()
    {
        if (savedDuskDuration < 0f) savedDuskDuration = config.duskDurationSeconds;
        config.duskDurationSeconds = 0f;
    }

    private static void RestoreDusk()
    {
        if (savedDuskDuration >= 0f && config != null) config.duskDurationSeconds = savedDuskDuration;
        savedDuskDuration = -1f;
    }

    private static void SetStrength(GameBalanceConfig.DuskColorStrength level)
    {
        if (!savedStrength.HasValue) savedStrength = config.duskColorStrength;
        config.duskColorStrength = level;
    }

    private static void RestoreStrength()
    {
        if (savedStrength.HasValue && config != null) config.duskColorStrength = savedStrength.Value;
        savedStrength = null;
    }

    private static float Hue(Color c) { Color.RGBToHSV(c, out float h, out _, out _); return h * 360f; }
    private static bool InHue(Color c, float min, float max) { float h = Hue(c); return h >= min && h <= max; }
    private static float MaxChannel(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
    private static float Dist(Color a, Color b) => 255f * Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

    // 대상 그림(렌더러를 켰을 때와 껐을 때 12 넘게 다른 픽셀)의 화면 평균 색. 라이트(플레이어 시야)는 그대로 두고 렌더러만 끈다.
    private static Color MeanColor(GameObject target)
    {
        Renderer[] rs = target.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        Vector3 saved = cam.transform.position;
        cam.transform.position = new Vector3(b.center.x, b.center.y, saved.z);
        Texture2D with = Render();
        foreach (Renderer r in rs) r.enabled = false;
        Texture2D without = Render();
        foreach (Renderer r in rs) r.enabled = true;
        RectInt rect = ScreenRect(b);
        Color32[] pa = with.GetPixels32(), pb = without.GetPixels32();
        double sr = 0, sg = 0, sbl = 0; int n = 0;
        for (int y = rect.yMin; y < Mathf.Min(H, rect.yMax); y++)
            for (int x = rect.xMin; x < Mathf.Min(W, rect.xMax); x++)
            {
                Color32 p = pa[y * W + x], q = pb[y * W + x];
                if (Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)) <= 12) continue;
                sr += p.r; sg += p.g; sbl += p.b; n++;
            }
        cam.transform.position = saved;
        Object.DestroyImmediate(with); Object.DestroyImmediate(without);
        return n == 0 ? Color.black : new Color((float)(sr / n / 255.0), (float)(sg / n / 255.0), (float)(sbl / n / 255.0), 1f);
    }

    private static void SetTime(float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });

    // 시간을 멈추고(timeScale 0이면 마을 시간도 안 흐른다) 그 시각을 넣은 뒤, 라이트가 적용되게 몇 프레임 돈다.
    private static IEnumerator SetTimeFrozen(float seconds)
    {
        Time.timeScale = 0f;
        SetTime(seconds);
        yield return Frames(3);
    }

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    private static object RoomOf(string prefabName) => rooms.Cast<object>().First(r => (string)r.GetType().GetField("PrefabName").GetValue(r) == prefabName);
    private static GameObject RootOf(object room) => (GameObject)room.GetType().GetField("Root").GetValue(room);

    private static Vector2 TruckCenter()
    {
        Light2D l = PlayerVision.Current != null ? PlayerVision.Current.TruckVisionLight : null;
        if (l != null) return l.transform.position;
        GameObject t = GameObject.Find("TruckPoint");
        return t != null ? (Vector2)t.transform.position : Vector2.one * 1e6f;
    }

    private static bool Walkable(object room, Vector2 w)
    {
        var floor = new HashSet<Vector2Int>((List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room));
        var obstacles = (HashSet<Vector2Int>)room.GetType().GetField("ObstacleCells").GetValue(room);
        Vector2Int c = Vector2Int.FloorToInt(w - (Vector2)RootOf(room).transform.position);
        if (!floor.Contains(c)) return false;
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (obstacles.Contains(c + new Vector2Int(dx, dy))) return false;
        return NavMesh.SamplePosition(new Vector3(w.x, 0f, w.y), out _, 0.4f, NavMesh.AllAreas);
    }

    // 방 가운데에 가깝고 트럭 시야에서 충분히 먼(트럭 반경 + 10칸) 플레이어 자리.
    private static Vector2 PickPlayerSpot(object room)
    {
        var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
        Vector2 o = RootOf(room).transform.position;
        Vector2 center = o + floor.Aggregate(Vector2.zero, (s, c) => s + (Vector2)c) / floor.Count;
        Vector2 truck = TruckCenter();
        return floor.Select(c => o + c + new Vector2(0.5f, 0.5f))
            .Where(p => Vector2.Distance(p, truck) >= config.truckVisionRadius + 10f && Walkable(room, p))
            .OrderBy(p => Vector2.Distance(p, center)).First();
    }

    // 트럭에서 트럭 반경 + 10칸 이상 떨어진, 그 거리에 가장 가까운 걸을 수 있는 자리(어느 방이든).
    private static Vector2 FarFromTruck()
    {
        Vector2 truck = TruckCenter();
        float want = config.truckVisionRadius + 10f;
        Vector2 best = player.transform.position; float bestScore = float.MaxValue;
        foreach (object room in rooms)
        {
            var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
            Vector2 o = RootOf(room).transform.position;
            foreach (Vector2Int c in floor)
            {
                Vector2 p = o + c + new Vector2(0.5f, 0.5f);
                float d = Vector2.Distance(p, truck);
                if (d < want) continue;
                float score = d - want;
                if (score >= bestScore || !Walkable(room, p)) continue;
                best = p; bestScore = score;
            }
        }
        return best;
    }

    // 플레이어(pp)에서 거리 d(±0.6) 근처, 손전등 밖(바라보는 방향에서 각도가 큰 쪽), 트럭 시야 밖의 걸을 수 있는 바닥. preferred가 있으면 그 방향에 가까운 자리.
    private static Vector2? FloorAround(Vector2 pp, float d, Vector2 truck, Vector2? preferred = null)
    {
        float coneHalf = config.focusedVisionAngle * 0.5f + 20f;
        Vector2 want = preferred ?? -lookDirection;
        Vector2? best = null; float bestScore = float.MaxValue;
        foreach (object room in rooms)
        {
            var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
            Vector2 o = RootOf(room).transform.position;
            foreach (Vector2Int c in floor)
            {
                Vector2 p = o + c + new Vector2(0.5f, 0.5f);
                float dist = Vector2.Distance(p, pp);
                if (Mathf.Abs(dist - d) > 0.6f) continue;
                if (Vector2.Angle(lookDirection, p - pp) <= coneHalf) continue;
                if (Vector2.Distance(p, truck) <= config.truckVisionRadius + 1.5f) continue;
                float score = Vector2.Angle(want, p - pp) + Mathf.Abs(dist - d) * 10f;
                if (score >= bestScore || !Walkable(room, p)) continue;
                best = p; bestScore = score;
            }
        }
        return best;
    }

    private static object RoomContaining(Vector2 w)
    {
        foreach (object room in rooms)
        {
            var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
            if (floor.Contains(Vector2Int.FloorToInt(w - (Vector2)RootOf(room).transform.position))) return room;
        }
        return rooms[0];
    }

    private static IEnumerator PlacePlayer(Vector2 p, Vector2 look)
    {
        Time.timeScale = 1f;
        lookDirection = look;
        player.transform.position = new Vector3(p.x, p.y, player.transform.position.z);
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = p; rb.linearVelocity = Vector2.zero; }
        player.GetComponent<PlayerMovement>().enabled = false;
        cam.transform.position = new Vector3(p.x, p.y, cam.transform.position.z);
        yield return Frames(3);
    }

    private static GameObject Spawn(GameObject prefab, Vector2 want)
    {
        Vector3 nav = NavMesh.SamplePosition(new Vector3(want.x, 0f, want.y), out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ? hit.position : new Vector3(want.x, 0f, want.y);
        GameObject m = Object.Instantiate(prefab, nav, Quaternion.identity, RootOf(RoomContaining(want)).transform);
        m.name = "DT_" + prefab.name;
        return m;
    }

    private static GameObject SpawnAnimal(GameObject prefab, Vector2 p)
    {
        GameObject a = Object.Instantiate(prefab, new Vector3(p.x, p.y, 0f), Quaternion.identity, RootOf(RoomContaining(p)).transform);
        a.name = "DT_" + prefab.name;
        foreach (MonoBehaviour mb in a.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("Animal") && !(mb is AnimalRescue)) mb.enabled = false;
        return a;
    }

    // AI를 멈추고 눈이 있는 프레임이 나올 때까지 기다린 뒤 시간을 멈춘다. 그동안 마을 시각은 t로 붙잡는다.
    private static IEnumerator Freeze(GameObject m, float t)
    {
        Time.timeScale = 1f;
        for (int i = 0; i < 3; i++) { SetTime(t); yield return null; }
        foreach (MonsterBrain b in m.GetComponents<MonsterBrain>()) b.enabled = false;
        NavMeshAgent ag = m.GetComponent<NavMeshAgent>();
        if (ag != null && ag.isOnNavMesh) ag.isStopped = true;
        MonsterEyeGlow glow = m.GetComponent<MonsterEyeGlow>();
        for (int i = 0; i < 180 && glow != null && !glow.HasEyeThisFrame; i++) { SetTime(t); yield return null; }
        Time.timeScale = 0f;
        SetTime(t);
        yield return Frames(2);
    }

    private static IEnumerator FreezeAnimal(float t)
    {
        Time.timeScale = 1f;
        for (int i = 0; i < 3; i++) { SetTime(t); yield return null; }
        Time.timeScale = 0f;
        SetTime(t);
        yield return Frames(2);
    }

    private static IEnumerator Remove(GameObject g, float t)
    {
        Object.Destroy(g);
        yield return SetTimeFrozen(t);
    }

    // 대상이 있을 때와 없을 때 그 그림 자리의 최대 색 차이. 카메라를 대상에 맞춰 찍는다(화면 밖이어도 빛은 월드 기준이라 같다).
    private static int DiffWithout(GameObject target)
    {
        Bounds b = default; bool first = true;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        b.Expand(new Vector3(0.6f, 0.6f, 0f));
        Vector3 saved = cam.transform.position;
        cam.transform.position = new Vector3(b.center.x, b.center.y, saved.z);
        Texture2D with = Render();
        target.SetActive(false);
        Texture2D without = Render();
        target.SetActive(true);
        int d = MaxDiff(with, without, ScreenRect(b));
        cam.transform.position = saved;
        Object.DestroyImmediate(with); Object.DestroyImmediate(without);
        return d;
    }

    private static Vector3 ToPixel(Vector3 world) { Vector3 v = cam.WorldToViewportPoint(world); return new Vector3(v.x * W, v.y * H, v.z); }

    private static RectInt ScreenRect(Bounds b)
    {
        Vector3 a = ToPixel(new Vector3(b.min.x, b.min.y, 0f)), c = ToPixel(new Vector3(b.max.x, b.max.y, 0f));
        int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, c.x)), 0, W - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, c.y)), 0, H - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, c.x)), 0, W - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, c.y)), 0, H - 1);
        return new RectInt(x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0));
    }

    private static int MaxDiff(Texture2D a, Texture2D b, RectInt r)
    {
        Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
        int best = 0;
        for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(H, r.yMax); y++)
            for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(W, r.xMax); x++)
            {
                Color32 p = pa[y * W + x], q = pb[y * W + x];
                best = Mathf.Max(best, Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b));
            }
        return best;
    }

    private static Texture2D Render(int w = W, int h = H)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = prev;
        rt.Release();
        Object.DestroyImmediate(rt);
        return tex;
    }
}
