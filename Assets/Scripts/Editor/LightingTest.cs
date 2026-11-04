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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 빛 효과(시안 A)와 트럭 주변 시야 검사. Tools > Lighting > Run Lighting Test, 결과는 Logs/LightingTest.txt.
/// batchmode: -executeMethod LightingTest.RunBatch (끝나면 통과 0 / 실패 1로 종료).
/// 마을: 격자 3x4 시드 2(Field가 트럭 방이 아니다), 트래퍼. 시간 소환·다른 몬스터·동물은 끄고 시험할 것만 둔다.
/// 화면 검사는 실제 렌더(1920x1080)로 한다: 같은 장면을 시험 대상이 있을 때/없을 때(또는 눈 발광 켬/끔) 찍어 그 자리 픽셀을 비교한다.
/// 1. 설정: 카메라 후처리·Volume(블룸 2.6/1.8), 라이트 색, 구간별 어둠 색.
/// 2. 시야 밖 몬스터 5종: 눈 발광 꺼짐, 몸·눈 모두 화면에 안 보임(있을 때·없을 때 같은 그림).
/// 3. 시야 안 몬스터 5종: 눈 발광 켜짐, 눈 자리가 발광을 끈 그림보다 밝음.
/// 4. 나무 잎 아래 몬스터: 눈은 몸과 같은 그리기 안에서 더해져 잎(앞에 그려지는 나무)에 몸과 같이 가려진다 - 잎이 반투명하면 그만큼만 보인다.
///    (방 장애물에는 그림자(ShadowCaster2D)가 없어 나무는 빛을 막지 않는다 - 손전등을 막는 것은 맵 벽 그림자뿐이다.)
/// 5. 트럭 시야: 반경 config 값, 반경 안 보임 / 밖 안 보임(판정·화면 모두), 몬스터·동물·소품이 보임, 반경을 바꾸면 따라감, 정면 손전등 판정에는 안 들어감.
/// 6. 트럭 시야 안 박쥐는 멈추지 않는다(굳음은 정면 손전등 시야만).
/// </summary>
[InitializeOnLoad]
public static class LightingTest
{
    private const string ReportPath = "Logs/LightingTest.txt";
    private const string KeyActive = "LT.Active", KeyBatch = "LT.Batch", KeyDone = "LT.Done", KeyPassed = "LT.Passed";
    private const int Seed = 2;
    private const int W = 1920, H = 1080;
    private const int InvisibleTolerance = 3; // 8비트 색 차이(블룸·후처리 잡음 여유)
    private const int VisibleMinDiff = 15;

    private static readonly string[] MonsterPrefabs =
    {
        "Assets/Prefabs/Monsters/PatrolMonster.prefab", "Assets/Prefabs/SoundReactiveMonster.prefab", "Assets/Prefabs/Monsters/BatMonster.prefab",
        "Assets/Prefabs/Monsters/PorcupineMonster.prefab", "Assets/Prefabs/Monsters/RhinoMonster.prefab",
    };

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
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

    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);

    static LightingTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/Lighting/Run Lighting Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[LightingTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
        vmouse = InputSystem.AddDevice<Mouse>("LightingTestMouse");
        vmouse.MakeCurrent();
    }

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
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
        Debug.Log("[LightingTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    private static IEnumerator WaitScene(string name) { float until = Time.realtimeSinceStartup + 30f; while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null; yield return Frames(5); }

    // ===================== 시나리오 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"LightingTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return Frames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitScene("ClassSelectScene");
        gm = GameManager.Instance;
        config = gm.Config;
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.SelectClass(PlayerClass.Trapper);
        gm.CompleteStartingClassSelection();
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = Seed };
        SceneManager.LoadScene("VillageScene");
        yield return WaitScene("VillageScene");
        while (GameObject.Find("GeneratedMap") == null) yield return null;
        yield return Frames(10);
        VillageMapGenerator.EditorLayoutOverride = null;

        cam = Camera.main;
        cam.aspect = (float)W / H;
        player = GameObject.FindGameObjectWithTag("Player");
        foreach (MonoBehaviour b in cam.GetComponents<MonoBehaviour>()) if (b is CameraFollow) b.enabled = false;
        if (gm.GetComponent<VillageMonsterSpawner>() != null) gm.GetComponent<VillageMonsterSpawner>().enabled = false;
        SetTime(0f);
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        VillageMapGenerator gen = Object.FindAnyObjectByType<VillageMapGenerator>();
        rooms = (IList)typeof(VillageMapGenerator).GetField("gridRooms", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gen);

        yield return Settings();
        yield return OutsideAndInsideVision();
        yield return BehindTree();
        yield return TruckVision();
    }

    // ---- 1. 설정
    private static IEnumerator Settings()
    {
        sb.AppendLine("\n== 1. 설정 (카메라 후처리, Volume, 라이트 색, 구간별 어둠 색)");
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        Check(data != null && data.renderPostProcessing, $"마을 카메라 후처리 켜짐 ({data?.renderPostProcessing})");
        Volume vol = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude).FirstOrDefault(v => v.isGlobal && v.sharedProfile != null);
        Bloom bloom = null;
        bool hasBloom = vol != null && vol.sharedProfile.TryGet(out bloom);
        Check(hasBloom && bloom.active && Mathf.Approximately(bloom.threshold.value, 2.6f) && Mathf.Approximately(bloom.intensity.value, 1.8f),
            $"전역 Volume 블룸: 기준 밝기 {(hasBloom ? bloom.threshold.value : -1)} (2.6), 세기 {(hasBloom ? bloom.intensity.value : -1)} (1.8)");
        PlayerVision vision = PlayerVision.Current;
        Light2D ambient = (Light2D)typeof(PlayerVision).GetField("ambientVisionLight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vision);
        Light2D focused = (Light2D)typeof(PlayerVision).GetField("focusedVisionLight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vision);
        Check(ambient.color == config.ambientVisionLightColor && focused.color == config.focusedVisionLightColor,
            $"라이트 색: 원형 {ambient.color} = {config.ambientVisionLightColor}(차가운 흰색), 손전등 {focused.color} = {config.focusedVisionLightColor}(따뜻한 흰색)");
        VillageNightTint tint = cam.GetComponent<VillageNightTint>();
        Check(tint != null && tint.TintRenderer != null, "어둠 색: VillageNightTint가 카메라에 있다");
        if (tint != null)
        {
            for (int phase = 1; phase <= 5; phase++)
            {
                SetTime(config.villagePhaseStartSeconds[phase - 1] + 0.5f);
                yield return Frames(2);
                Color want = config.GetVillagePhaseDarknessColor(phase);
                Check(gm.VillagePhase == phase && tint.CurrentColor == want, $"{phase}구간 어둠 색 {Hex(tint.CurrentColor)} = 설정 {Hex(want)}");
            }
            SetTime(0f);
            yield return Frames(2);
        }
    }

    // ---- 2·3. 시야 밖 / 안
    private static IEnumerator OutsideAndInsideVision()
    {
        sb.AppendLine("\n== 2. 시야 밖 몬스터: 몸·눈 모두 안 보임 / 3. 시야 안 몬스터: 눈이 빛남 (Field, 플레이어는 오른쪽을 봄)");
        object field = RoomOf("Room_Field");
        Vector2 back = new Vector2(-5.5f, 0f), front = new Vector2(3.4f, 0.3f);
        Vector2 pp = PickSpot(field, new[] { back, front }, 3f + config.truckVisionRadius + 6f);
        yield return PlacePlayer(pp, Vector2.right);
        foreach (string path in MonsterPrefabs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // 시야 밖
            GameObject m = Spawn(prefab, field, pp + back);
            yield return Freeze(m);
            MonsterEyeGlow glow = m.GetComponent<MonsterEyeGlow>();
            bool glowingOut = glow != null && glow.IsGlowing;
            int diff = DiffWithout(m);
            Check(glow != null && !glowingOut && !PlayerVision.IsVisibleToPlayer(glow.EyeWorldPosition) && diff <= InvisibleTolerance,
                $"{prefab.name} 시야 밖(뒤 5.5칸, 눈 프레임 {glow?.HasEyeThisFrame}): 눈 꺼짐 {glow != null && !glowingOut}, 있을 때·없을 때 화면 차이 최대 {diff} (≤{InvisibleTolerance})");
            Object.Destroy(m);
            Time.timeScale = 1f;
            yield return Frames(2);
            // 시야 안
            m = Spawn(prefab, field, pp + front);
            yield return Freeze(m);
            glow = m.GetComponent<MonsterEyeGlow>();
            bool glowingIn = glow != null && glow.IsGlowing;
            string spriteIn = glow != null && glow.Body.sprite != null ? glow.Body.sprite.name : "-";
            int eyeGain = glow != null && glow.HasEyeThisFrame ? EyeGain(glow) : -1;
            if (prefab.name == "PorcupineMonster") openEyeGain = eyeGain;
            Check(glow != null && glow.HasEyeThisFrame && glowingIn && eyeGain >= 40,
                $"{prefab.name} 시야 안(앞 3.4칸, 그림 {spriteIn}): 눈 발광 {glowingIn}, 눈 자리가 발광을 끈 그림보다 밝음 +{eyeGain} (≥40)");
            Object.Destroy(m);
            Time.timeScale = 1f;
            yield return Frames(2);
        }
    }

    // ---- 4. 나무 잎 아래
    private static int openEyeGain = -1; // 3번에서 잰 호저의 트인 곳 눈 밝기(비교 기준)

    private static IEnumerator BehindTree()
    {
        sb.AppendLine("\n== 4. 나무 잎 아래 몬스터: 눈이 잎 위로 비치지 않음 (숲, 호저)");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabs[3]);
        // 발광은 몸 그림 재질 안에서 그린다 - 따로 그리는 렌더러가 없다(정렬·가림이 몸과 같다).
        int prefabRenderers = prefab.GetComponentsInChildren<SpriteRenderer>(true).Length;
        MonsterEyeGlow prefabGlow = prefab.GetComponent<MonsterEyeGlow>();
        Check(prefabGlow != null && prefabGlow.Body != null && prefabGlow.Body.sharedMaterial != null && prefabGlow.Body.sharedMaterial.shader.name == "Animal Keeper/Sprite-Lit-Emission",
            $"발광은 몸 그림({prefabGlow?.Body?.name}) 재질 {prefabGlow?.Body?.sharedMaterial?.shader.name} 안에서 그린다");

        object forest = RoomOf("Room_Forest");
        Transform obstacles = RootOf(forest).transform.Find("Obstacles");
        Vector2 truck = TruckCenter();
        var trees = (obstacles != null ? obstacles.GetComponentsInChildren<SpriteRenderer>() : new SpriteRenderer[0])
            .Where(r => r.enabled && r.bounds.size.y >= 2.5f && r.bounds.size.x >= 2f && Vector2.Distance(r.bounds.center, truck) > config.truckVisionRadius + 4f).ToList();
        Vector2[] playerOffsets = { new Vector2(0f, -3.2f), new Vector2(2.6f, -2.2f), new Vector2(-2.6f, -2.2f), new Vector2(3f, 0f), new Vector2(-3f, 0f) };
        bool found = false;
        GameObject m = null; MonsterEyeGlow glow = null; SpriteRenderer canopy = null;
        int tries = 0;
        foreach (SpriteRenderer tree in trees)
        {
            Bounds tb = tree.bounds;
            // 잎 가운데쯤(줄기 발보다 위 = 뒤)에 몬스터 발을 둔다 - 발 기준 정렬로 나무가 앞에 그려진다.
            for (float fy = 0.45f; fy <= 0.65f && !found && tries < 30; fy += 0.1f)
            {
                Vector2 mp = new Vector2(tb.center.x, tb.min.y + tb.size.y * fy);
                if (!Walkable(forest, mp)) continue;
                foreach (Vector2 off in playerOffsets)
                {
                    Vector2 pp = mp + off;
                    if (!Walkable(forest, pp)) continue;
                    tries++;
                    yield return PlacePlayer(pp, (mp - pp).normalized);
                    m = Spawn(prefab, forest, mp);
                    yield return Freeze(m);
                    glow = m.GetComponent<MonsterEyeGlow>();
                    bool covered = glow.HasEyeThisFrame && tb.Contains(new Vector3(glow.EyeWorldPosition.x, glow.EyeWorldPosition.y, tb.center.z))
                        && tree.sortingLayerID == glow.Body.sortingLayerID && tree.sortingOrder > glow.Body.sortingOrder;
                    if (covered && glow.IsGlowing) { found = true; canopy = tree; break; }
                    Object.Destroy(m); m = null;
                    Time.timeScale = 1f;
                    yield return Frames(2);
                }
                if (found) break;
            }
            if (found || tries >= 30) break;
        }
        Check(found, found ? $"잎 아래 자리: 나무 {canopy.name}(정렬 {canopy.sortingOrder}) 뒤 호저(정렬 {glow.Body.sortingOrder}), 눈이 시야 안이라 발광 켜짐" : $"잎이 눈을 덮고 눈이 시야 안인 자리를 찾지 못했다({tries}번 시도)");
        if (!found) yield break;
        int coveredGain = EyeGain(glow);
        float fade = canopy.color.a; // 가림 반투명(ObstacleOccluderFade)이 걸렸으면 1보다 작다
        // 잎이 눈을 덮는 비율만큼만 보여야 한다: 덮은 잎의 불투명도 a면 눈은 (1 - a)만큼. 따로 그리던 시안에서는 잎 위에 그대로(트인 곳과 같은 밝기) 비쳤다.
        int allowed = Mathf.RoundToInt(openEyeGain * (1f - fade) + 12);
        Check(openEyeGain > 0 && coveredGain <= allowed && coveredGain < openEyeGain * 0.8f,
            $"잎 아래 눈 밝기 +{coveredGain} ≤ 트인 곳 +{openEyeGain} x (1 - 잎 불투명도 {fade:F2}) + 여유 = {allowed} - 눈이 잎 위로 비치지 않고 몸과 같이 가려진다");
        Check(m.GetComponentsInChildren<SpriteRenderer>(true).Length == prefabRenderers, $"몬스터 렌더러 수가 프리팹과 같다({prefabRenderers}) - 눈만 따로 그리는 렌더러가 없다");
        Object.Destroy(m);
        Time.timeScale = 1f;
        yield return Frames(2);
    }

    // ---- 5·6. 트럭 시야
    private static IEnumerator TruckVision()
    {
        sb.AppendLine("\n== 5. 트럭 주변 시야 (플레이어는 트럭에서 멀리, 트럭 반대쪽을 봄)");
        PlayerVision vision = PlayerVision.Current;
        Light2D light = vision.TruckVisionLight;
        Check(light != null && light.isActiveAndEnabled && Mathf.Approximately(light.pointLightOuterRadius, config.truckVisionRadius) && !light.shadowsEnabled,
            $"트럭 시야 라이트: 반경 {light?.pointLightOuterRadius} = 설정 {config.truckVisionRadius}, 그림자 {light?.shadowsEnabled}, 색 {light?.color}");
        if (light == null) yield break;
        Vector2 tc = light.transform.position;
        object truckRoom = rooms.Cast<object>().First(r => InRoom(r, tc));
        // 플레이어를 트럭에서 멀리(다른 방) 두고 트럭 반대쪽을 보게 한다.
        Vector2 far = FarSpot(tc, 16f);
        yield return PlacePlayer(far, (far - tc).normalized);
        cam.transform.position = new Vector3(tc.x, tc.y, cam.transform.position.z);
        yield return Frames(2);

        Vector2[] dirs = Enumerable.Range(0, 8).Select(i => (Vector2)(Quaternion.Euler(0, 0, i * 45f) * Vector2.right)).ToArray();
        float r = config.truckVisionRadius;
        int inOk = dirs.Count(d => PlayerVision.IsVisibleToPlayer(tc + d * (r - 0.2f))), outOk = dirs.Count(d => !PlayerVision.IsVisibleToPlayer(tc + d * (r + 0.2f)));
        int focusedOff = dirs.Count(d => !PlayerVision.IsInFocusedVision(tc + d * (r - 0.2f)));
        Check(inOk == 8 && outOk == 8, $"시야 판정: 반경 {r - 0.2f:F1}칸 안 8방향 중 {inOk} 보임, {r + 0.2f:F1}칸 밖 8방향 중 {outOk} 안 보임");
        Check(focusedOff == 8, $"정면 손전등 판정(박쥐 굳음)에는 안 들어감: 8방향 중 {focusedOff}");

        GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabs[0]);
        Vector2 inPos = FloorNear(truckRoom, tc, 3.0f, 3.8f), outPos = FloorNear(truckRoom, tc, r + 1.5f, r + 3f);
        GameObject a = Spawn(wolfPrefab, truckRoom, inPos);
        yield return Freeze(a);
        MonsterEyeGlow aGlow = a.GetComponent<MonsterEyeGlow>();
        bool aGlowing = aGlow.IsGlowing, aEye = aGlow.HasEyeThisFrame;
        int dIn = DiffWithout(a);
        Check(dIn >= VisibleMinDiff && aGlowing == aEye,
            $"트럭 시야 안 몬스터({Vector2.Distance(inPos, tc):F1}칸): 화면에 보임(차이 {dIn} ≥ {VisibleMinDiff}), 눈 프레임 {aEye}·눈 발광 {aGlowing}");
        Object.Destroy(a); Time.timeScale = 1f; yield return Frames(2);
        GameObject b = Spawn(wolfPrefab, truckRoom, outPos);
        yield return Freeze(b);
        bool bGlowing = b.GetComponent<MonsterEyeGlow>().IsGlowing;
        int dOut = DiffWithout(b);
        Check(dOut <= InvisibleTolerance && !bGlowing,
            $"트럭 시야 밖 몬스터({Vector2.Distance(outPos, tc):F1}칸): 화면에 안 보임(차이 {dOut} ≤ {InvisibleTolerance}), 눈 꺼짐");
        Object.Destroy(b); Time.timeScale = 1f; yield return Frames(2);

        GameObject animalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Animals/AnimalWander.prefab");
        Vector2 aPos = FloorNear(truckRoom, tc, 2.5f, 3.5f);
        GameObject animal = Object.Instantiate(animalPrefab, new Vector3(aPos.x, aPos.y, 0f), Quaternion.identity, RootOf(truckRoom).transform);
        foreach (MonoBehaviour mb in animal.GetComponents<MonoBehaviour>()) if (mb.GetType().Name.StartsWith("Animal") && !(mb is AnimalRescue)) mb.enabled = false;
        yield return Frames(3);
        Time.timeScale = 0f;
        int dAnimal = DiffWithout(animal);
        Check(dAnimal >= VisibleMinDiff, $"트럭 시야 안 동물({Vector2.Distance(aPos, tc):F1}칸): 화면에 보임(차이 {dAnimal})");
        Object.Destroy(animal); Time.timeScale = 1f; yield return Frames(2);

        // 소품: 트럭 시야 안 장애물 그림은 트럭 시야를 켜고 끈 그림이 다르다(밝혀진다).
        SpriteRenderer prop = RootOf(truckRoom).transform.Find("Obstacles")?.GetComponentsInChildren<SpriteRenderer>()
            .Where(s => s.enabled && Vector2.Distance(s.bounds.center, tc) < r - 1f).OrderBy(s => Vector2.Distance(s.bounds.center, tc)).FirstOrDefault();
        if (prop != null)
        {
            Texture2D on = Render();
            light.enabled = false;
            Texture2D off = Render();
            light.enabled = true;
            int dProp = MaxDiff(on, off, ScreenRect(prop.bounds));
            Check(dProp >= VisibleMinDiff, $"트럭 시야 안 소품 {prop.name}({Vector2.Distance(prop.bounds.center, tc):F1}칸): 트럭 시야를 끄면 어두워짐(차이 {dProp})");
            Object.DestroyImmediate(on); Object.DestroyImmediate(off);
        }
        else sb.AppendLine("  (참고) 트럭 시야 안에 장애물 그림이 없어 소품 검사는 건너뛴다");

        // 반경을 바꾸면 따라간다.
        float saved = config.truckVisionRadius;
        config.truckVisionRadius = 3f;
        yield return Frames(2);
        bool follows = Mathf.Approximately(light.pointLightOuterRadius, 3f) && !PlayerVision.IsVisibleToPlayer(tc + Vector2.right * 3.6f) && PlayerVision.IsVisibleToPlayer(tc + Vector2.right * 2.6f);
        config.truckVisionRadius = 0f;
        yield return Frames(2);
        bool offOk = !light.enabled && !PlayerVision.IsVisibleToPlayer(tc + Vector2.right * 1f);
        config.truckVisionRadius = saved;
        yield return Frames(2);
        Check(follows && offOk && Mathf.Approximately(light.pointLightOuterRadius, saved), $"GameBalanceConfig.truckVisionRadius를 3으로 바꾸면 라이트·판정이 3칸, 0이면 꺼짐, 되돌리면 {saved}칸");

        sb.AppendLine("\n== 6. 트럭 시야 안 박쥐는 멈추지 않는다 (굳음은 정면 손전등 시야만)");
        GameObject batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabs[2]);
        Vector2 batPos = FloorNear(truckRoom, tc, 2f, 3f);
        GameObject bat = Spawn(batPrefab, truckRoom, batPos);
        BatMonsterAI ai = bat.GetComponent<BatMonsterAI>();
        Time.timeScale = 1f;
        yield return Frames(2);
        Vector2 BatPos() => new Vector2(ai.transform.position.x, ai.transform.position.z);
        bool startVisible = PlayerVision.IsVisibleToPlayer(BatPos()), startFocused = PlayerVision.IsInFocusedVision(BatPos());
        Vector2 start = BatPos();
        int frozenFrames = 0;
        for (int i = 0; i < 60; i++) { if (ai.IsFrozen) frozenFrames++; yield return null; }
        float moved = Vector2.Distance(start, BatPos());
        Check(startVisible && !startFocused && frozenFrames == 0 && moved > 0.5f,
            $"트럭 시야 안 박쥐: 시야 안 {startVisible}, 정면 손전등 시야 {startFocused}, 60프레임 중 굳은 프레임 {frozenFrames}, 움직인 거리 {moved:F2}칸 (> 0.5)");
        Object.Destroy(bat);
        yield return Frames(2);
    }

    // ===================== 도움 함수 =====================

    private static void SetTime(float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });
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

    private static bool InRoom(object room, Vector2 w)
    {
        var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
        return floor.Contains(Vector2Int.FloorToInt(w - (Vector2)RootOf(room).transform.position));
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

    // 모든 offset 자리가 걸을 수 있는 바닥이고 트럭에서 minTruckDistance 이상 떨어진, 방 가운데에 가까운 플레이어 자리.
    private static Vector2 PickSpot(object room, Vector2[] offsets, float minTruckDistance)
    {
        var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
        Vector2 o = RootOf(room).transform.position;
        Vector2 center = o + floor.Aggregate(Vector2.zero, (s, c) => s + (Vector2)c) / floor.Count;
        Vector2 truck = TruckCenter();
        return floor.Select(c => o + c + new Vector2(0.5f, 0.5f))
            .Where(p => Vector2.Distance(p, truck) >= minTruckDistance && Walkable(room, p) && offsets.All(x => Walkable(room, p + x)))
            .OrderBy(p => Vector2.Distance(p, center)).First();
    }

    private static Vector2 FarSpot(Vector2 from, float minDistance)
    {
        foreach (object room in rooms)
        {
            var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
            Vector2 o = RootOf(room).transform.position;
            foreach (Vector2Int c in floor.OrderBy(c => Mathf.Abs(Vector2.Distance(o + c, from) - minDistance - 4f)))
            {
                Vector2 p = o + c + new Vector2(0.5f, 0.5f);
                if (Vector2.Distance(p, from) >= minDistance && Walkable(room, p)) return p;
            }
        }
        return from + Vector2.right * minDistance;
    }

    private static Vector2 FloorNear(object room, Vector2 center, float minR, float maxR)
    {
        var floor = (List<Vector2Int>)room.GetType().GetField("FloorCells").GetValue(room);
        Vector2 o = RootOf(room).transform.position;
        return floor.Select(c => o + c + new Vector2(0.5f, 0.5f))
            .Where(p => { float d = Vector2.Distance(p, center); return d >= minR && d <= maxR && Walkable(room, p); })
            .OrderBy(p => Mathf.Abs(Vector2.Distance(p, center) - (minR + maxR) * 0.5f)).First();
    }

    private static IEnumerator PlacePlayer(Vector2 p, Vector2 look)
    {
        Time.timeScale = 1f;
        lookDirection = look;
        player.transform.position = new Vector3(p.x, p.y, player.transform.position.z);
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = p; rb.linearVelocity = Vector2.zero; }
        player.GetComponent<PlayerMovement>().enabled = false; // 가상 입력이 없어도 미끄러지지 않게
        cam.transform.position = new Vector3(p.x, p.y, cam.transform.position.z);
        yield return Frames(3);
    }

    private static GameObject Spawn(GameObject prefab, object room, Vector2 want)
    {
        Vector3 nav = NavMesh.SamplePosition(new Vector3(want.x, 0f, want.y), out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ? hit.position : new Vector3(want.x, 0f, want.y);
        GameObject m = Object.Instantiate(prefab, nav, Quaternion.identity, RootOf(room).transform);
        m.name = "LT_" + prefab.name;
        return m;
    }

    // AI를 멈추고(그림은 계속 돈다) 눈이 있는 프레임이 나올 때까지 기다린 뒤 시간을 멈춘다.
    private static IEnumerator Freeze(GameObject m)
    {
        Time.timeScale = 1f;
        yield return Frames(3);
        foreach (MonsterBrain b in m.GetComponents<MonsterBrain>()) b.enabled = false;
        NavMeshAgent ag = m.GetComponent<NavMeshAgent>();
        if (ag != null && ag.isOnNavMesh) ag.isStopped = true;
        MonsterEyeGlow glow = m.GetComponent<MonsterEyeGlow>();
        for (int i = 0; i < 180 && glow != null && !glow.HasEyeThisFrame; i++) yield return null;
        Time.timeScale = 0f;
        yield return Frames(2);
    }

    // 대상이 있을 때와 없을 때 그 그림 자리(렌더러 영역 + 여유)의 최대 색 차이.
    private static int DiffWithout(GameObject target)
    {
        Bounds b = default; bool first = true;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        b.Expand(new Vector3(0.6f, 0.6f, 0f));
        Texture2D with = Render();
        target.SetActive(false);
        Texture2D without = Render();
        target.SetActive(true);
        int d = MaxDiff(with, without, ScreenRect(b));
        Object.DestroyImmediate(with); Object.DestroyImmediate(without);
        return d;
    }

    // 대상이 있을 때 없을 때보다 밝아진 최대 값(8비트)과 어두워진 최대 값.
    private static int BrightenWith(GameObject target, out int darken)
    {
        Bounds b = default; bool first = true;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        b.Expand(new Vector3(0.6f, 0.6f, 0f));
        Texture2D with = Render();
        target.SetActive(false);
        Texture2D without = Render();
        target.SetActive(true);
        RectInt rect = ScreenRect(b);
        int up = 0; darken = 0;
        for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                Color32 p = with.GetPixel(x, y), q = without.GetPixel(x, y);
                up = Mathf.Max(up, p.r - q.r, p.g - q.g, p.b - q.b);
                darken = Mathf.Max(darken, q.r - p.r, q.g - p.g, q.b - p.b);
            }
        Object.DestroyImmediate(with); Object.DestroyImmediate(without);
        return up;
    }

    // 몸 그림 영역에서 발광을 켠 그림이 끈 그림보다 얼마나 밝은지(8비트, 채널 최대) - 차이는 눈 픽셀에서만 생긴다.
    private static int EyeGain(MonsterEyeGlow glow)
    {
        Texture2D on = Render();
        glow.enabled = false;
        Texture2D off = Render();
        glow.enabled = true;
        int d = MaxDiff(on, off, ScreenRect(glow.Body.bounds));
        Object.DestroyImmediate(on); Object.DestroyImmediate(off);
        return d;
    }

    // 렌더(W x H) 픽셀 좌표. 카메라 화면 크기(batchmode에서는 창 크기)가 아니라 viewport로 구한다.
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
        int best = 0;
        for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(H, r.yMax); y++)
            for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(W, r.xMax); x++)
            {
                Color32 p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                best = Mathf.Max(best, Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b));
            }
        return best;
    }

    private static Texture2D Render()
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = prev;
        rt.Release();
        Object.DestroyImmediate(rt);
        return tex;
    }
}
