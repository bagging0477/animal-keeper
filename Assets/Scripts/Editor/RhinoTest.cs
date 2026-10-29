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
/// Tools > Monsters > Run Rhino Test. 코뿔소(RhinoMonsterAI, 돌진형)를 실제 마을(격자 3x4, 시드 1)의 가장 큰 방(Field)에 테스트로만 놓고 확인한다.
/// 스폰 목록에는 없으므로 다른 몬스터·동물과 시간 소환은 끄고, 스폰하는 쪽과 같은 방식(MonsterCorpsePickup.RememberSource)으로 만든다. 프레임 시간 1/30초 고정.
/// 1) 가져오기·프리팹 2) 6칸 밖·벽 뒤면 차징 안 함(배회: idle·walk) 3) 차징 1초(제자리, 플레이어를 봄) → 끝나는 순간의 방향으로 고정 돌진(속도 9, 8칸), 피해 60, 멈칫 1.5초
/// 4) 돌진 중 플레이어가 비켜도 방향 안 바뀜 5) 벽에 부딪히면 멈추고 2초 기절 6) 기절·마취가 차징·돌진을 멈춤 7) 무기별 피해 = 늑대
/// 8) 체력 7: 포획망 12번에 쓰러짐, E로 회수(무게 25), G로 내려놓으면 코뿔소 시체로 다시 생김
/// 실제 렌더: Logs/Rhino/rhino_sheet.png / 결과: Logs/RhinoTest.txt / batchmode: -executeMethod RhinoTest.RunBatch (그림 때문에 -nographics 없이)
/// </summary>
[InitializeOnLoad]
public static class RhinoTest
{
    private const string KeyActive = "RHT.Active", KeyDone = "RHT.Done", KeyBatch = "RHT.Batch", KeyPassed = "RHT.Passed";
    private const string ReportPath = "Logs/RhinoTest.txt";
    private const string SheetPath = "Logs/Rhino/rhino_sheet.png";
    private const float FrameTime = 1f / 30f;
    private const float TimeTol = 0.02f;

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
    private static readonly FieldInfo CorpseIdField = typeof(MonsterCorpsePickup).GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);

    static RhinoTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Monsters/Run Rhino Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[RhinoTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
        vmouse = InputSystem.AddDevice<Mouse>("RhinoTestMouse");
        vmouse.MakeCurrent();
        vkb = InputSystem.AddDevice<Keyboard>("RhinoTestKeyboard");
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
        Debug.Log("[RhinoTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject player;
    private static PlayerMovement movement;
    private static PlayerWeaponController weapon;
    private static PlayerGadgetController gadget;
    private static Rigidbody2D playerBody;
    private static GameObject rhinoPrefab, wolfPrefab, porcupinePrefab;
    private static Tilemap fieldGround;
    private static Vector2 laneStart, dir, perp;
    private static readonly List<(string label, Texture2D tex)> panels = new List<(string, Texture2D)>();

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        panels.Clear();
        sb.AppendLine($"RhinoTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

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
        rhinoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RhinoAssetBuilder.PrefabPath);
        wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        porcupinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PorcupineMonster.prefab");

        sb.AppendLine($"설정: 걷기 {config.rhinoWalkSpeed}, 배회 반경 {config.rhinoWanderRadius} 대기 {config.rhinoWanderWaitMin}~{config.rhinoWanderWaitMax}초, 발견 {config.rhinoDetectRange}칸, " +
                      $"차징 {config.rhinoChargeDuration}초, 돌진 속도 {config.rhinoDashSpeed} 거리 {config.rhinoDashDistance} 피해 {config.rhinoDashDamage}(닿는 거리 {config.rhinoDashHitRadius}), " +
                      $"멈칫 {config.rhinoRecoverDuration}초, 벽 기절 {config.rhinoWallStunDuration}초, 시체 무게 {config.rhinoCorpseWeight} / 플레이어(트래퍼) 최대 체력 {GameManager.MaxHealth}");

        yield return CheckImportAndPrefab();
        if (rhinoPrefab == null) { Finish(); yield break; }
        if (!FindLane(12f, out laneStart, out dir))
        {
            Check(false, "Field 방에서 12칸 곧은 빈 길을 찾지 못했다");
            Finish();
            yield break;
        }
        perp = new Vector2(-dir.y, dir.x);
        sb.AppendLine($"\n시험 자리: Field 방 {laneStart} 에서 {dir} 방향 12칸이 비어 있다");
        yield return CheckCorpseWeight();

        RhinoMonsterAI r = SpawnLikeSpawner(rhinoPrefab, laneStart, "RhinoMonster_test").GetComponent<RhinoMonsterAI>();
        PlayerFar();
        yield return WaitFrames(3);

        yield return CheckNoCharge(r);
        yield return CheckChargeAndDash(r);
        yield return CheckDirectionLocked(r);
        yield return CheckWallStun(r);
        yield return CheckInterrupts(r);
        Object.Destroy(r.gameObject);
        yield return WaitFrames(3);
        yield return CheckWeapons();
        yield return CheckDeathAndCorpse();
        yield return CaptureField(hidden);
        WriteSheet();
        yield return CheckDashWallRate();
        Finish();
    }

    // 실제 돌진 벽 판정(RhinoMonsterAI.DashFreeDistance)으로, 걸을 수 있는 아무 자리에서 발견 조건(1.5~6칸, 사이가 막히지 않음)을 만족하는 플레이어 쪽으로
    // 8칸 돌진할 때 다 못 달리고 벽에 닿는 비율. 비교로 예전 몸 가운데 줄 판정(StaticBlockerLine + NavMesh.Raycast)도 센다.
    private static IEnumerator CheckDashWallRate()
    {
        sb.AppendLine("\n== 11. 새 크기로 돌진할 때 8칸을 다 못 달리고 벽에 부딪히는 비율 (격자 3x4 GridMapAudit 시드 1~5, 시드마다 300번)");
        int total = 0, hitNew = 0, hitOld = 0;
        float freeSum = 0f;
        for (int seed = 1; seed <= 5; seed++)
        {
            VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = seed };
            SceneManager.LoadScene("VillageScene");
            yield return WaitFrames(2);
            yield return WaitForScene("VillageScene");
            VillageMapGenerator.EditorLayoutOverride = null;
            VillageMonsterSpawner sp = gm.GetComponent<VillageMonsterSpawner>();
            if (sp != null) sp.enabled = false;
            Vector2 playerSpawn = GameObject.FindGameObjectWithTag("Player").transform.position;
            RhinoMonsterAI probe = SpawnLikeSpawner(rhinoPrefab, playerSpawn, "RhinoProbe").GetComponent<RhinoMonsterAI>();
            probe.enabled = false;
            yield return WaitFrames(2);
            Bounds b = new Bounds();
            bool first = true;
            foreach (Tilemap t in GameObject.Find("GeneratedMap").GetComponentsInChildren<Tilemap>())
            {
                if (t.name != "Ground" && t.name != "Corridors") continue;
                t.CompressBounds();
                Bounds tb = new Bounds(t.transform.TransformPoint(t.localBounds.center), t.localBounds.size);
                if (first) { b = tb; first = false; } else b.Encapsulate(tb);
            }
            System.Random rng = new System.Random(seed * 31);
            int n = 0, sNew = 0, sOld = 0;
            for (int tries = 0; n < 300 && tries < 30000; tries++)
            {
                Vector3 q = new Vector3(b.min.x + (float)rng.NextDouble() * b.size.x, 0f, b.min.y + (float)rng.NextDouble() * b.size.y);
                if (!NavMesh.SamplePosition(q, out NavMeshHit gh, 0.5f, NavMesh.AllAreas)) continue;
                Vector2 g = new Vector2(gh.position.x, gh.position.z);
                float a = (float)(rng.NextDouble() * Mathf.PI * 2.0), dist = 1.5f + (float)rng.NextDouble() * 4.5f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p = g + d * dist;
                if (!NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out _, 0.3f, NavMesh.AllAreas)) continue;
                if (StaticBlockerLine.IsBlocked(g, p, ~0) || NavMesh.Raycast(gh.position, new Vector3(p.x, gh.position.y, p.y), out _, NavMesh.AllAreas)) continue;
                n++;
                float free = probe.DashFreeDistance(g, d, config.rhinoDashDistance, out bool blocked);
                bool old = StaticBlockerLine.IsBlocked(g, g + d * config.rhinoDashDistance, ~0) ||
                           NavMesh.Raycast(gh.position, gh.position + new Vector3(d.x, 0f, d.y) * config.rhinoDashDistance, out _, NavMesh.AllAreas);
                if (blocked) sNew++;
                if (old) sOld++;
                freeSum += free;
            }
            total += n; hitNew += sNew; hitOld += sOld;
            sb.AppendLine($"  시드 {seed}: {n}번 중 몸 가장자리 판정(지금) {100f * sNew / Mathf.Max(1, n):F1}%, 예전 몸 가운데 줄 판정 {100f * sOld / Mathf.Max(1, n):F1}%");
            Object.Destroy(probe.gameObject);
            yield return WaitFrames(2);
        }
        sb.AppendLine($"  합계 {total}번: 8칸을 다 못 달리고 벽에 닿음 {100f * hitNew / Mathf.Max(1, total):F1}% (예전 몸 가운데 줄 판정이었다면 {100f * hitOld / Mathf.Max(1, total):F1}%), 평균 {freeSum / Mathf.Max(1, total):F1}칸 달림");
        Check(total == 1500, $"돌진 벽 충돌 비율 표본 {total}/1500");
    }

    // ===================== 1. 가져오기·프리팹 =====================

    private static IEnumerator CheckImportAndPrefab()
    {
        sb.AppendLine("\n== 1. 그림 가져오기와 프리팹");
        int ok = 0;
        foreach (string n in RhinoAssetBuilder.FrameNames)
        {
            string path = $"{RhinoAssetBuilder.FramesFolder}/{n}.png";
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (imp != null && s != null && imp.spriteImportMode == SpriteImportMode.Single && Mathf.Approximately(s.pixelsPerUnit, 32f) &&
                imp.filterMode == FilterMode.Point && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.mipmapEnabled &&
                Vector2.Distance(s.pivot, new Vector2(24f, 16f)) < 0.01f) ok++;
        }
        Check(ok == RhinoAssetBuilder.FrameNames.Length, $"{RhinoAssetBuilder.FrameNames.Length}장 모두 Single, PPU 32, Point, 압축 없음, 밉맵 끔, 피벗 가운데 ({ok}/{RhinoAssetBuilder.FrameNames.Length})");
        Check(rhinoPrefab != null, $"코뿔소 프리팹 {RhinoAssetBuilder.PrefabPath}");
        if (rhinoPrefab == null) yield break;

        RhinoAssetBuilder.BodyMetrics m = RhinoAssetBuilder.MeasureIdle();
        Transform visual = rhinoPrefab.transform.Find("Visual");
        Transform body = visual != null ? visual.Find("Body") : null;
        Transform shadow = visual != null ? visual.Find("Shadow") : null;
        Check(rhinoPrefab.GetComponent<RhinoMonsterAI>() != null && rhinoPrefab.GetComponent<MonsterHealth>() != null && rhinoPrefab.GetComponent<MonsterCorpsePickup>() != null &&
              visual != null && visual.localScale == new Vector3(3f, 3f, 1f) && body != null && visual.GetChild(0) == body && shadow != null,
            $"루트: RhinoMonsterAI, NavMeshAgent, MonsterHealth, MonsterCorpsePickup / Visual 배율 {visual?.localScale.x}(1.5배) 아래 Body(첫 자식)와 Shadow(배율을 따라 커짐)");
        SpriteRenderer shadowRenderer = shadow != null ? shadow.GetComponent<SpriteRenderer>() : null;
        Check(shadowRenderer != null && shadowRenderer.sprite != null && shadowRenderer.sprite.name == $"Shadow_{m.ShadowW}x{m.ShadowH}",
            $"그림자 {shadowRenderer?.sprite?.name} (idle 몸 {m.Width}x{m.Height}텍셀, 폭 x 0.75), 아래 변 {shadow?.localPosition.y * 32f:0.#}텍셀 = 발 {m.FootTexels} + 1 − {m.ShadowH}");
        FootSortOrder foot = visual != null ? visual.GetComponent<FootSortOrder>() : null;
        SerializedObject f = foot != null ? new SerializedObject(foot) : null;
        Check(f != null && f.FindProperty("body").objectReferenceValue == body.GetComponent<SpriteRenderer>() && Mathf.Approximately(f.FindProperty("footTexels").floatValue, m.FootTexels) &&
              f.FindProperty("shadow").objectReferenceValue == shadowRenderer, $"FootSortOrder: 몸 = Body, 발 {m.FootTexels}텍셀, 그림자 = Shadow (다른 몬스터와 같은 방식)");
        float wolfRadius = wolfPrefab.GetComponent<NavMeshAgent>().radius, rhinoRadius = rhinoPrefab.GetComponent<NavMeshAgent>().radius;
        SerializedObject hs = new SerializedObject(rhinoPrefab.GetComponent<MonsterHealth>());
        float dartR = hs.FindProperty("dartHitRadius").floatValue;
        Check(Mathf.Abs(rhinoRadius - 0.825f) < 0.001f && Mathf.Abs(dartR - 1.10f) < 0.01f && Mathf.Abs(config.rhinoDashHitRadius - 1.5f) < 0.001f,
            $"길찾기 반경 {rhinoRadius:0.###}(전 0.55, 늑대 {wolfRadius:0.##}), 다트 맞힘 반경 {dartR:0.###}(전 0.734), 돌진 맞힘 {config.rhinoDashHitRadius}(전 1.0), 다트 원 가운데 땅 위치 + {hs.FindProperty("dartHitOffset").vector2Value}");
        RhinoMonsterAI prefabAi = rhinoPrefab.GetComponent<RhinoMonsterAI>();
        Check(Vector2.Distance(prefabAi.BodyHalfExtents, new Vector2(m.Width, m.Height) * 0.5f * 3f / 32f) < 0.001f,
            $"돌진 벽 판정 몸 상자 반폭·반높이 {prefabAi.BodyHalfExtents} = idle 몸 {m.Width}x{m.Height}텍셀의 절반 x 3/32칸");
        SimpleFrameAnimator anim = body.GetComponent<SimpleFrameAnimator>();
        SerializedObject a = new SerializedObject(anim);
        string Names(string prop) => string.Join(",", Enumerable.Range(0, a.FindProperty(prop).arraySize).Select(i => a.FindProperty(prop).GetArrayElementAtIndex(i).objectReferenceValue?.name));
        Check(Names("walkFrames") == "walk-01,walk-02,walk-03,walk-04,walk-05,walk-06" && Names("fleeFrames") == "idle-01,idle-02,idle-03,idle-04" &&
              Names("hurtFrames") == "hurt-01" && Names("deathFrames") == "hurt-01,fallen-01",
            $"그림 연결: 걷기 {Names("walkFrames")}, 서 있음 {Names("fleeFrames")}, 피격 {Names("hurtFrames")}, 쓰러짐 {Names("deathFrames")}, 기절 {a.FindProperty("knockedOutSprite").objectReferenceValue?.name}");
        MonsterHealth h = rhinoPrefab.GetComponent<MonsterHealth>();
        Check(h.MaxHealth == 7 && !h.IgnoresStunAndSleep, $"최대 체력 {h.MaxHealth} (늑대 {wolfPrefab.GetComponent<MonsterHealth>().MaxHealth}), 기절·마취 통함");
        yield return WaitFrames(1);
    }

    private static IEnumerator CheckCorpseWeight()
    {
        GameObject g = SpawnLikeSpawner(rhinoPrefab, laneStart + dir * 6f, "RhinoWeightProbe");
        int w = g.GetComponent<MonsterCorpsePickup>().Weight;
        g.SetActive(false);
        Object.Destroy(g);
        Check(w == 25, $"시체 무게 {w} (몬스터별 무게 ICorpseWeight)");
        yield return WaitFrames(2);
    }

    // ===================== 2. 차징 안 함 =====================

    private static IEnumerator CheckNoCharge(RhinoMonsterAI r)
    {
        sb.AppendLine("\n== 2. 6칸 밖이나 벽 뒤면 차징하지 않고 배회 (idle·walk)");
        HashSet<string> seen = new HashSet<string>();
        bool charged = false;
        float moved = 0f;
        Vector2 last = Ground(r);
        for (int i = 0; i < 180; i++)
        {
            PlacePlayer(r.BodyCenter + dir * (config.rhinoDetectRange + 0.6f), Deg(-dir));
            yield return WaitFrames(1);
            string n = SpriteName(r);
            seen.Add(n);
            moved += Vector2.Distance(Ground(r), last);
            last = Ground(r);
            if (r.CurrentState != RhinoMonsterAI.State.Wander) charged = true;
            if (n.StartsWith("idle-") && panels.All(p => p.label != "1 IDLE")) CaptureAround(r, "1 IDLE");
            if (n.StartsWith("walk-") && panels.All(p => p.label != "2 WALK") && i % 5 == 0) CaptureAround(r, "2 WALK");
        }
        Check(!charged && r.DashesStarted == 0, $"플레이어를 몸 가운데에서 {config.rhinoDetectRange + 0.6f}칸에 계속 둔 6초: 차징 없음");
        Check(seen.Any(s => s.StartsWith("idle-")) && seen.Any(s => s.StartsWith("walk-")) && moved > 1f,
            $"배회: 서 있을 때 idle, 걸을 때 walk ({string.Join(",", seen.OrderBy(x => x))}), {moved:F1}칸 걸음");

        GameObject wall = null;
        for (int i = 0; i < 90; i++)
        {
            Vector2 c = r.BodyCenter;
            if (wall == null) wall = MakeWall(c + dir * 2f, dir, 5f);
            else { wall.transform.position = new Vector3((c + dir * 2f).x, (c + dir * 2f).y, 0f); Physics2D.SyncTransforms(); }
            PlacePlayer(c + dir * 4f, Deg(-dir));
            yield return WaitFrames(1);
            if (r.CurrentState != RhinoMonsterAI.State.Wander) charged = true;
        }
        PlayerFar();
        RemoveWall(wall);
        Check(!charged && r.DashesStarted == 0, "4칸 앞이지만 사이에 벽: 3초 동안 차징 없음");
        yield return WaitFrames(2);
    }

    // ===================== 3. 차징 → 돌진 → 멈칫 =====================

    private static IEnumerator CheckChargeAndDash(RhinoMonsterAI r)
    {
        sb.AppendLine("\n== 3. 차징 1초(제자리, 플레이어를 봄) → 고정 방향 돌진(속도·거리·피해 60) → 멈칫 1.5초 → 걷기");
        yield return ResetAt(r, laneStart);
        ResetPlayerHealth();
        PlacePlayer(Ground(r) + dir * 5f, Deg(-dir));
        Vector2 chargePos = Ground(r);
        List<string> seq = new List<string>();
        float drift = 0f;
        bool facingOk = true;
        int dashes0 = r.DashesStarted;
        for (int i = 0; i < 60 && r.DashesStarted == dashes0; i++)
        {
            // 차징 중에 플레이어가 좌우로 움직이면 코뿔소는 그쪽을 본다. 끝나기 전에 길 위로 돌아온다.
            if (i == 9) PlacePlayer(chargePos + dir * 5f + perp * 2.5f, Deg(-dir));
            if (i == 18) PlacePlayer(chargePos + dir * 5f - perp * 2.5f, Deg(-dir));
            if (i == 25) PlacePlayer(chargePos + dir * 5f, Deg(-dir));
            yield return WaitFrames(1);
            if (r.CurrentState == RhinoMonsterAI.State.Charge)
            {
                drift = Mathf.Max(drift, Vector2.Distance(Ground(r), chargePos));
                string n = SpriteName(r);
                if (seq.Count == 0 || seq[seq.Count - 1] != n) seq.Add(n);
                float dx = player.transform.position.x - Ground(r).x;
                if ((i == 12 || i == 21) && Mathf.Abs(dx) > 0.5f && r.FacingLeft != dx < 0f) facingOk = false;
                if (i == 22 && panels.All(p => p.label != "3 CHARGE")) CaptureAround(r, "3 CHARGE");
            }
        }
        Vector2 toPlayerAtDash = (Vector2)player.transform.position - r.DashStartPosition;
        Check(Mathf.Abs(r.DashStartTime - r.ChargeStartTime - config.rhinoChargeDuration) <= TimeTol, $"차징 시작 → 돌진 시작 {r.DashStartTime - r.ChargeStartTime:F3}초 (기대 {config.rhinoChargeDuration})");
        Check(drift < 0.01f, $"차징 중 제자리 (최대 {drift:F3}칸 움직임)");
        Check(facingOk, "차징 중 플레이어가 좌우로 움직이면 그쪽을 본다");
        List<string> core = seq.SkipWhile(x => !x.StartsWith("charge-")).ToList();
        Check(core.Take(4).SequenceEqual(new[] { "charge-01", "charge-02", "charge-03", "charge-04" }) && core.Skip(4).All(x => x == "charge-04"),
            $"차징 그림: {string.Join(" → ", core)} (마지막 장 유지)");
        Check(Vector2.Angle(r.DashDirection, toPlayerAtDash) < 0.5f, $"돌진 방향 = 차징이 끝나는 순간의 플레이어 방향 (차이 {Vector2.Angle(r.DashDirection, toPlayerAtDash):F2}도)");

        // 돌진 따라가기
        Vector2 start = r.DashStartPosition;
        float maxOff = 0f, firstStep = -1f;
        Vector2 prev = Ground(r);
        HashSet<string> dashSprites = new HashSet<string>();
        int frames = 0;
        while (r.CurrentState == RhinoMonsterAI.State.Dash && frames < 60)
        {
            yield return WaitFrames(1);
            frames++;
            Vector2 p = Ground(r);
            if (firstStep < 0f && frames == 2) firstStep = Vector2.Distance(p, prev);
            prev = p;
            Vector2 d = p - start;
            maxOff = Mathf.Max(maxOff, Mathf.Abs(Vector2.Dot(d, new Vector2(-r.DashDirection.y, r.DashDirection.x))));
            if (r.CurrentState == RhinoMonsterAI.State.Dash) dashSprites.Add(SpriteName(r));
            if (frames == 8) CaptureAround(r, "4 DASH");
        }
        Check(Mathf.Abs(firstStep - config.rhinoDashSpeed * FrameTime) < 0.005f, $"돌진 한 프레임 {firstStep:F3}칸 = 속도 {firstStep / FrameTime:F2} (기대 {config.rhinoDashSpeed})");
        Check(r.LastDashEnd == RhinoMonsterAI.DashEnd.Distance && Mathf.Abs(r.DashTraveled - config.rhinoDashDistance) < 0.01f && Mathf.Abs(Vector2.Distance(Ground(r), start) - config.rhinoDashDistance) < 0.05f,
            $"{r.DashTraveled:F3}칸 달리고 멈춤 (기대 {config.rhinoDashDistance}, 시작점에서 {Vector2.Distance(Ground(r), start):F3}칸)");
        Check(maxOff < 0.01f, $"일직선: 돌진 방향에서 옆으로 최대 {maxOff:F4}칸");
        Check(dashSprites.SetEquals(new[] { "dash-01", "dash-02", "dash-03", "dash-04" }), $"돌진 그림 반복: {string.Join(",", dashSprites.OrderBy(x => x))}");
        Check(r.DashHits == 1 && GameManager.MaxHealth - gm.Health == config.rhinoDashDamage,
            $"길 위 플레이어에게 닿음: 체력 {GameManager.MaxHealth} → {gm.Health} (피해 {config.rhinoDashDamage}, 돌진 한 번에 한 번)");

        // 멈칫
        bool dashFacing = r.FacingLeft;
        PlayerFar();
        Vector2 recoverPos = Ground(r);
        List<string> rseq = new List<string>();
        bool recoverStill = true;
        string flipNote = "";
        for (int i = 0; i < 80 && r.CurrentState == RhinoMonsterAI.State.Recover; i++)
        {
            string n = SpriteName(r);
            if (rseq.Count == 0 || rseq[rseq.Count - 1] != n) rseq.Add(n);
            if (Vector2.Distance(Ground(r), recoverPos) > 0.01f) recoverStill = false;
            if (flipNote == "" && r.FacingLeft != dashFacing) flipNote = $"{i}프레임째({n}) 방향이 바뀜";
            if (i == 20) CaptureAround(r, "5 RECOVER");
            yield return WaitFrames(1);
        }
        float recoverTime = Time.time - r.RecoverStartTime;
        Check(Mathf.Abs(recoverTime - config.rhinoRecoverDuration) <= FrameTime + TimeTol && r.CurrentState == RhinoMonsterAI.State.Wander && recoverStill,
            $"멈칫 {recoverTime:F3}초 제자리 (기대 {config.rhinoRecoverDuration}), 그 뒤 배회");
        Check(flipNote == "", $"멈칫 동안 돌진하던 방향을 그대로 봄 (돌진 {(dashFacing ? "왼쪽" : "오른쪽")}{(flipNote != "" ? ", " + flipNote : "")})");
        rseq = rseq.SkipWhile(x => !x.StartsWith("recover-")).ToList(); // 돌진이 끝난 그 프레임에는 그림 갱신 순서에 따라 아직 dash가 보일 수 있다
        Check(rseq.SequenceEqual(new[] { "recover-01", "recover-02" }), $"멈칫 그림: {string.Join(" → ", rseq)} (마지막 장 유지)");
        bool walked = false;
        for (int i = 0; i < 120 && !walked; i++) { yield return WaitFrames(1); walked = SpriteName(r).StartsWith("walk-"); }
        Check(walked, "멈칫 뒤 다시 걷기(walk)");
    }

    // ===================== 4. 돌진 중 방향 고정 =====================

    private static IEnumerator CheckDirectionLocked(RhinoMonsterAI r)
    {
        sb.AppendLine("\n== 4. 돌진 중 플레이어가 비켜도 방향을 바꾸지 않음");
        yield return ResetAt(r, laneStart);
        ResetPlayerHealth();
        PlacePlayer(Ground(r) + dir * 5f, Deg(-dir));
        int d0 = r.DashesStarted;
        for (int i = 0; i < 60 && r.DashesStarted == d0; i++) yield return WaitFrames(1);
        Vector2 lockedDir = r.DashDirection, start = r.DashStartPosition;
        PlacePlayer(start + dir * 4f + perp * 3f, Deg(-dir)); // 돌진이 시작되자마자 옆으로 3칸 비킨다
        float maxAngle = 0f;
        Vector2 prev = Ground(r);
        for (int i = 0; i < 60 && r.CurrentState == RhinoMonsterAI.State.Dash; i++)
        {
            yield return WaitFrames(1);
            Vector2 p = Ground(r);
            if ((p - prev).sqrMagnitude > 1e-6f) maxAngle = Mathf.Max(maxAngle, Vector2.Angle(p - prev, lockedDir));
            prev = p;
        }
        Check(r.DashDirection == lockedDir && maxAngle < 0.5f && Mathf.Abs(r.DashTraveled - config.rhinoDashDistance) < 0.01f,
            $"비킨 플레이어 쪽으로 꺾지 않음 (매 프레임 이동 방향과 고정 방향 차이 최대 {maxAngle:F2}도, {r.DashTraveled:F2}칸)");
        Check(gm.Health == GameManager.MaxHealth && r.DashHits == 1, $"비킨 플레이어는 맞지 않음 (체력 {gm.Health})");
        PlayerFar();
        for (int i = 0; i < 60 && r.CurrentState != RhinoMonsterAI.State.Wander; i++) yield return WaitFrames(1);
    }

    // ===================== 5. 벽 =====================

    private static IEnumerator CheckWallStun(RhinoMonsterAI r)
    {
        sb.AppendLine($"\n== 5. 돌진 중 몸 가장자리가 벽에 닿으면 그 자리에서 멈추고(파묻히지 않음) 2초 기절 - 길 방향 {dir}");
        yield return WallDash(r, laneStart, dir, true);
        if (FindLane(9f, out Vector2 hStart, out Vector2 hDir, new[] { Vector2.right, Vector2.left }))
        {
            sb.AppendLine($"\n== 5-2. 가로 돌진(머리 쪽)도 같음 - Field 방 {hStart}에서 {hDir}");
            yield return WallDash(r, hStart, hDir, false);
        }
        else Check(false, "Field 방에서 가로 9칸 빈 길을 찾지 못해 가로 돌진 벽 시험을 못 함");
    }

    private static IEnumerator WallDash(RhinoMonsterAI r, Vector2 start0, Vector2 d, bool full)
    {
        Vector2 p = new Vector2(-d.y, d.x);
        yield return ResetAt(r, start0);
        ResetPlayerHealth();
        PlacePlayer(Ground(r) + d * 5f, Deg(-d));
        int d0 = r.DashesStarted;
        for (int i = 0; i < 60 && r.DashesStarted == d0; i++) yield return WaitFrames(1);
        Vector2 start = r.DashStartPosition;
        // 몸 가운데에서 벽 앞면까지 3칸이 되게 벽을 둔다(몸이 클수록 가장자리는 그보다 먼저 닿는다).
        Vector2 bodyAtStart = r.BodyCenterAt(start, d.x < -0.001f || (Mathf.Abs(d.x) <= 0.001f && r.FacingLeft));
        Vector2 half = r.BodyHalfExtents;
        float front = Mathf.Abs(d.x) * half.x + Mathf.Abs(d.y) * half.y; // 몸 상자의 돌진 방향 앞쪽 끝(몸 가운데 기준)
        float faceAlong = Vector2.Dot(bodyAtStart - start, d) + 3f;
        GameObject wall = MakeWall(start + d * (faceAlong + 0.2f) + p * Vector2.Dot(bodyAtStart - start, p), d, 6f);
        float recover0 = r.RecoverStartTime;
        for (int i = 0; i < 30 && r.CurrentState == RhinoMonsterAI.State.Dash; i++) yield return WaitFrames(1);
        Vector2 stopPos = Ground(r);
        float hitTime = r.DashEndTime;
        Vector2 bodyNow = r.BodyCenter;
        float frontNow = Vector2.Dot(bodyNow - start, d) + front;
        float gap = faceAlong - frontNow;
        float centerGap = faceAlong - Vector2.Dot(stopPos - start, d);
        Check(r.LastDashEnd == RhinoMonsterAI.DashEnd.Wall && r.Health.IsStunned && gap >= -0.001f && gap <= 0.06f,
            $"몸 가장자리가 벽 앞면 {gap:F3}칸 앞에서 멈춤(파묻히지 않음, 몸 가운데는 벽에서 {centerGap:F2}칸), {r.DashTraveled:F2}칸 달림, 기절 {r.Health.IsStunned} (몸 앞쪽 끝 {front:F2}칸)");
        SpriteRenderer bodyR = r.transform.Find("Visual/Body").GetComponent<SpriteRenderer>();
        int maxPen = VisiblePixelsPastFace(bodyR, start, d, faceAlong);
        Check(maxPen == 0, $"실제 그림: 몸의 보이는 픽셀 중 벽 앞면을 넘은 것 {maxPen}개 ({bodyR.sprite.name})");
        if (!full)
        {
            CaptureAround(r, "6 WALL STUN");
            Vector2 contact = start + d * faceAlong + p * Vector2.Dot(bodyNow - start, p);
            Texture2D z = Capture(contact - new Vector2(1.3f, 1.3f), contact + new Vector2(1.3f, 1.3f));
            if (z != null) panels.Add(("7 WALL EDGE x3", Enlarge(z, 3)));
        }
        string stunSprite = SpriteName(r);
        bool still = true;
        for (int i = 0; i < 90 && r.Health.IsStunned; i++)
        {
            yield return WaitFrames(1);
            if (Vector2.Distance(Ground(r), stopPos) > 0.01f) still = false;
        }
        float stunTime = Time.time - hitTime;
        Check(Mathf.Abs(stunTime - config.rhinoWallStunDuration) <= FrameTime + TimeTol && still,
            $"기절 {stunTime:F3}초 동안 제자리 (기대 {config.rhinoWallStunDuration}, 그림 {stunSprite})");
        Check(r.RecoverStartTime == recover0 && r.CurrentState == RhinoMonsterAI.State.Wander, "벽에 부딪힌 뒤에는 멈칫 없이 기절이 풀리면 배회");
        Check(gm.Health == GameManager.MaxHealth, $"벽 뒤 플레이어 체력 그대로 ({gm.Health})");
        PlayerFar();
        RemoveWall(wall);
        yield return WaitFrames(2);
    }

    // 경계 확대용: 픽셀을 k배로 키운다(보간 없이).
    private static Texture2D Enlarge(Texture2D src, int k)
    {
        Texture2D dst = new Texture2D(src.width * k, src.height * k, TextureFormat.RGB24, false);
        Color[] s = src.GetPixels(), d = new Color[dst.width * dst.height];
        for (int y = 0; y < dst.height; y++)
            for (int x = 0; x < dst.width; x++) d[y * dst.width + x] = s[(y / k) * src.width + x / k];
        dst.SetPixels(d);
        dst.Apply();
        Object.DestroyImmediate(src);
        return dst;
    }

    // 몸 그림의 보이는(알파 > 0.5) 픽셀 가운데를 월드로 옮겨, 돌진 방향으로 벽 앞면(start에서 faceAlong)을 넘은 개수를 센다.
    private static int VisiblePixelsPastFace(SpriteRenderer body, Vector2 start, Vector2 d, float faceAlong)
    {
        Texture2D t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(body.sprite)));
        int count = 0;
        for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
            {
                if (t.GetPixel(x, y).a < 0.5f) continue;
                float lx = (x + 0.5f - body.sprite.pivot.x) / body.sprite.pixelsPerUnit, ly = (y + 0.5f - body.sprite.pivot.y) / body.sprite.pixelsPerUnit;
                if (body.flipX) lx = -lx;
                Vector2 w = body.transform.TransformPoint(new Vector3(lx, ly, 0f));
                if (Vector2.Dot(w - start, d) > faceAlong + 0.001f) count++;
            }
        Object.DestroyImmediate(t);
        return count;
    }

    // ===================== 6. 기절·마취가 멈춤 =====================

    private static IEnumerator CheckInterrupts(RhinoMonsterAI r)
    {
        sb.AppendLine("\n== 6. 기절·마취가 차징·돌진을 멈춤 (무기가 부르는 MonsterHealth.Stun / PutToSleep)");
        // 돌진 중 기절
        yield return StartDash(r);
        yield return WaitFrames(3);
        float traveled = r.DashTraveled;
        r.Health.Stun(1f);
        yield return WaitFrames(1);
        Vector2 stopped = Ground(r);
        float t1 = r.DashTraveled;
        bool still = true;
        for (int i = 0; i < 25; i++) { yield return WaitFrames(1); if (Vector2.Distance(Ground(r), stopped) > 0.01f) still = false; }
        Check(r.LastDashEnd == RhinoMonsterAI.DashEnd.Incapacitated && r.CurrentState == RhinoMonsterAI.State.Wander && t1 - traveled < 0.31f && still,
            $"돌진 {traveled:F2}칸째 기절(1초): 바로 멈춤({t1:F2}칸), 기절 동안 제자리");
        PlayerFar();
        for (int i = 0; i < 10; i++) yield return WaitFrames(1);

        // 돌진 중 마취
        yield return StartDash(r);
        yield return WaitFrames(3);
        traveled = r.DashTraveled;
        r.Health.PutToSleep(0f, 2f);
        yield return WaitFrames(2);
        stopped = Ground(r);
        t1 = r.DashTraveled;
        still = true;
        for (int i = 0; i < 40; i++) { yield return WaitFrames(1); if (Vector2.Distance(Ground(r), stopped) > 0.01f) still = false; }
        Check(r.LastDashEnd == RhinoMonsterAI.DashEnd.Incapacitated && r.Health.IsAsleep && t1 - traveled < 0.61f && still,
            $"돌진 {traveled:F2}칸째 마취(2초): 멈춤({t1:F2}칸), 잠든 동안 제자리");
        PlayerFar();
        for (int i = 0; i < 40; i++) yield return WaitFrames(1);

        // 차징 중 기절
        yield return ResetAt(r, laneStart);
        PlacePlayer(Ground(r) + dir * 5f, Deg(-dir));
        for (int i = 0; i < 30 && r.CurrentState != RhinoMonsterAI.State.Charge; i++) yield return WaitFrames(1);
        yield return WaitFrames(12);
        int d0 = r.DashesStarted;
        r.Health.Stun(1f);
        bool dashed = false;
        for (int i = 0; i < 29; i++) { yield return WaitFrames(1); if (r.DashesStarted != d0) dashed = true; }
        Check(!dashed && r.CurrentState == RhinoMonsterAI.State.Wander, "차징 0.4초째 기절(1초): 차징을 접고 기절 동안 돌진 없음");
        PlayerFar();
        for (int i = 0; i < 10; i++) yield return WaitFrames(1);
    }

    private static IEnumerator StartDash(RhinoMonsterAI r)
    {
        yield return ResetAt(r, laneStart);
        ResetPlayerHealth();
        PlacePlayer(Ground(r) + dir * 5f + perp * 1.5f, Deg(-dir)); // 발견 범위 안, 길 옆(멈추기 전에는 닿지 않게)
        int d0 = r.DashesStarted;
        for (int i = 0; i < 60 && r.DashesStarted == d0; i++) yield return WaitFrames(1);
    }

    // ===================== 7. 무기별 피해 =====================

    private static IEnumerator CheckWeapons()
    {
        sb.AppendLine("\n== 7. 무기별 피해와 기절·마취 = 늑대와 같음 (두뇌는 꺼서 제자리, MonsterHealth만 본다)");
        float look = Deg(dir);
        Vector2 at = laneStart + dir * 2f;

        (MonsterHealth rh, MonsterHealth wh) = SpawnPair(at + dir * 1.3f + perp * 0.9f, at + dir * 1.3f - perp * 0.9f);
        PlacePlayer(at, look);
        yield return WaitFrames(2);
        CooldownField.SetValue(weapon, 0f);
        SwingNetMethod.Invoke(weapon, null);
        yield return WaitFrames(1);
        Check(Same(rh, wh, config.netDamage) && rh.IsStunned && wh.IsStunned, $"포획망: 코뿔소 −{Lost(rh):0.##}, 늑대 −{Lost(wh):0.##} (netDamage {config.netDamage}), 둘 다 기절");
        Destroy(rh, wh);
        yield return WaitFrames(3);

        (rh, wh) = SpawnPair(at + dir * 3.5f + perp * 1.5f, at + dir * 3.5f - perp * 1.5f);
        yield return WaitFrames(2);
        TranquilizerDart dartPrefab = (TranquilizerDart)DartPrefabField.GetValue(weapon);
        foreach (MonsterHealth target in new[] { rh, wh })
        {
            TranquilizerDart dart = Object.Instantiate(dartPrefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
            dart.Launch(target.DartHitCenter - at, 12f, config.tranquilizerRange, config.sleepDelay, config.sleepDuration, config.tranquilizerDamage);
        }
        for (int i = 0; i < 15; i++) yield return WaitFrames(1);
        bool sameDamage = Same(rh, wh, config.tranquilizerDamage);
        for (int i = 0; i < Mathf.CeilToInt(config.sleepDelay / FrameTime) + 2; i++) yield return WaitFrames(1);
        Check(sameDamage && rh.IsAsleep && wh.IsAsleep, $"다트: 코뿔소 −{Lost(rh):0.##}, 늑대 −{Lost(wh):0.##} (tranquilizerDamage {config.tranquilizerDamage}), 둘 다 잠듦");
        Destroy(rh, wh);
        yield return WaitFrames(3);

        (rh, wh) = SpawnPair(at + dir * 3f + perp * 1.6f, at + dir * 3f - perp * 1.6f);
        yield return WaitFrames(2);
        MineTrap minePrefab = (MineTrap)MinePrefabField.GetValue(gadget);
        foreach (MonsterHealth target in new[] { rh, wh })
            Object.Instantiate(minePrefab, new Vector3(target.GamePosition.x, target.GamePosition.y, 0f), Quaternion.identity);
        yield return WaitFrames(3);
        Check(Same(rh, wh, config.mineDamage) && rh.IsStunned && wh.IsStunned, $"지뢰: 코뿔소 −{Lost(rh):0.##}, 늑대 −{Lost(wh):0.##} (mineDamage {config.mineDamage}), 둘 다 기절");
        Destroy(rh, wh);
        yield return WaitFrames(3);

        (rh, wh) = SpawnPair(at + dir * 1.5f + perp * 1.1f, at + dir * 1.5f - perp * 1.1f);
        PlacePlayer(at, look);
        yield return WaitFrames(2);
        if (!gm.TryAddBomb()) { int s0 = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type != InventoryItemType.Bomb); gm.SelectSlot(s0); gm.TryDropSelectedItem(out _); gm.TryAddBomb(); }
        gm.SelectSlot(Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Bomb));
        UseBombMethod.Invoke(gadget, null);
        yield return WaitFrames(1);
        Check(Same(rh, wh, config.bombDamage) && rh.IsStunned && wh.IsStunned, $"폭탄: 코뿔소 −{Lost(rh):0.##}, 늑대 −{Lost(wh):0.##} (bombDamage {config.bombDamage}), 둘 다 기절");
        Destroy(rh, wh);
        // 폭발 그림은 실제 시간으로 사라져 고정 프레임 테스트에서는 오래 남는다 - 다음 캡처에 남지 않게 지운다.
        yield return WaitFrames(15);
        foreach (FireBurstEffect2D fx in Object.FindObjectsByType<FireBurstEffect2D>(FindObjectsInactive.Include)) Object.Destroy(fx.gameObject);
        foreach (BombExplosionEffect fx in Object.FindObjectsByType<BombExplosionEffect>(FindObjectsInactive.Include)) Object.Destroy(fx.gameObject);
        yield return WaitFrames(2);
        gm.SelectWeapon();
    }

    private static float Lost(MonsterHealth h) => h.MaxHealth - Mathf.Max(h.Health, 0f);
    private static bool Same(MonsterHealth a, MonsterHealth wolf, float expected) => Mathf.Abs(Lost(wolf) - expected) < 0.001f && Mathf.Abs(Lost(a) - expected) < 0.001f;

    private static (MonsterHealth rhino, MonsterHealth wolf) SpawnPair(Vector2 rhinoAt, Vector2 wolfAt)
    {
        GameObject r = SpawnLikeSpawner(rhinoPrefab, rhinoAt, "RhinoMonster_pair");
        r.GetComponent<RhinoMonsterAI>().enabled = false;
        GameObject w = SpawnLikeSpawner(wolfPrefab, wolfAt, "PatrolMonster_pair");
        w.GetComponent<MonsterAI>().enabled = false;
        return (r.GetComponent<MonsterHealth>(), w.GetComponent<MonsterHealth>());
    }

    private static void Destroy(MonsterHealth a, MonsterHealth b)
    {
        if (a != null) Object.Destroy(a.gameObject);
        if (b != null) Object.Destroy(b.gameObject);
    }

    // ===================== 8. 체력·쓰러짐·회수·내려놓기 =====================

    private static IEnumerator CheckDeathAndCorpse()
    {
        sb.AppendLine("\n== 8. 체력 7: 포획망 12번에 쓰러짐, E로 회수(무게 25), G로 내려놓으면 코뿔소 시체로 다시 생김");
        float look = Deg(dir);
        RhinoMonsterAI r = SpawnLikeSpawner(rhinoPrefab, laneStart + dir * 3.3f, "RhinoMonster_corpse").GetComponent<RhinoMonsterAI>();
        PlacePlayer(laneStart + dir * 2f, look);
        yield return WaitFrames(2);
        MonsterHealth h = r.Health;
        float after11 = 0f;
        List<string> seq = new List<string>();
        for (int k = 1; k <= 12; k++)
        {
            CooldownField.SetValue(weapon, 0f);
            SwingNetMethod.Invoke(weapon, null);
            yield return WaitFrames(1);
            if (k == 1) CaptureAround(r, "8 HURT");
            if (k == 11) after11 = h.Health;
            for (int i = 0; i < 7; i++) { string n = SpriteName(r); if (seq.Count == 0 || seq[seq.Count - 1] != n) seq.Add(n); yield return WaitFrames(1); }
            ResetPlayerHealth();
        }
        Check(Mathf.Abs(after11 - 0.4f) < 0.001f && h.IsDead, $"11번째 뒤 체력 {after11:0.##}, 12번째에 쓰러짐 (최대 {h.MaxHealth}, 포획망 {config.netDamage})");
        for (int i = 0; i < 10; i++) yield return WaitFrames(1);
        SpriteRenderer bodyR = r.transform.Find("Visual/Body").GetComponent<SpriteRenderer>();
        SpriteRenderer shadowR = r.transform.Find("Visual/Shadow").GetComponent<SpriteRenderer>();
        Color dead = (Color)DeadColorField.GetValue(h);
        Check(seq.Contains("hurt-01") && SpriteName(r) == "fallen-01" && bodyR.color == dead && shadowR.enabled,
            $"피격 hurt-01, 쓰러짐 {SpriteName(r)} + 시체 색 + 그림자 {shadowR.sprite.name}");
        CaptureAround(r, "9 FALLEN SHADOW");
        int dashes = r.DashesStarted;
        PlacePlayer(Ground(r) - dir * 4f, look);
        for (int i = 0; i < 60; i++) yield return WaitFrames(1);
        Check(r.DashesStarted == dashes && r.CurrentState == RhinoMonsterAI.State.Wander, "쓰러진 뒤 플레이어가 4칸 앞에 있어도 차징·돌진 없음");

        // 회수 → 내려놓기 → 다시 회수
        string id = (string)CorpseIdField.GetValue(r.GetComponent<MonsterCorpsePickup>());
        Vector2 home = Ground(r);
        PlacePlayer(home - dir * 1f, look);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        int slot = SlotOf(id);
        InventorySlotData data = slot >= 0 ? gm.GetSlot(slot) : default;
        Check(slot >= 0 && data.AnimalWeight == 25 && data.MonsterPrefab == rhinoPrefab && !r.gameObject.activeSelf,
            $"E로 회수: {slot}번 칸 무게 {data.AnimalWeight}, 다시 만들 프리팹 {(data.MonsterPrefab != null ? data.MonsterPrefab.name : "없음")}, 필드에서 치워짐");
        if (slot < 0) yield break;
        gm.SelectSlot(slot);
        PlacePlayer(home - dir * 1f, look);
        yield return WaitFrames(2);
        yield return Press(Key.G);
        yield return WaitFrames(5);
        MonsterCorpsePickup dropped = Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Exclude).FirstOrDefault(c => (string)CorpseIdField.GetValue(c) == id);
        RhinoMonsterAI dr = dropped != null ? dropped.GetComponent<RhinoMonsterAI>() : null;
        SpriteRenderer db = dr != null ? dr.transform.Find("Visual/Body").GetComponent<SpriteRenderer>() : null;
        SpriteRenderer ds = dr != null ? dr.transform.Find("Visual/Shadow").GetComponent<SpriteRenderer>() : null;
        Check(dr != null && dr.Health.IsDead && db.sprite.name == "fallen-01" && db.color == dead && ds.enabled && ds.sprite == shadowR.sprite &&
              Vector2.Distance(dr.transform.Find("Visual").position, Ground(dr)) < 0.01f && dropped.Weight == 25,
            $"G로 내려놓기: {(dr != null ? "코뿔소(RhinoMonsterAI)" : "코뿔소 아님")} 시체, 그림 {db?.sprite?.name}, 시체 색, 그림자 {ds?.sprite?.name}, 무게 {dropped?.Weight}");
        if (dr != null)
        {
            PlacePlayer(Ground(dr) - dir * 1f, look);
            yield return WaitFrames(3);
            yield return Press(Key.E);
            int again = SlotOf(id);
            Check(again >= 0 && gm.GetSlot(again).AnimalWeight == 25, $"다시 주운 무게 {(again >= 0 ? gm.GetSlot(again).AnimalWeight : -1)}");
            if (again >= 0) { gm.SelectSlot(again); gm.TryDropSelectedItem(out _); }
            Object.Destroy(dr.gameObject);
        }
        Object.Destroy(r.gameObject);
        gm.SelectWeapon();
        yield return WaitFrames(3);
    }

    // ===================== 실제 렌더 =====================

    // Field 방의 늑대(없으면 만든다) 옆에 코뿔소와 호저를 둔다. 셋 다 두뇌를 끄고 그림을 땅 위치에 맞춘다.
    private static IEnumerator CaptureField(List<GameObject> hidden)
    {
        sb.AppendLine("\n== 실제 렌더: Field에서 늑대·호저 옆");
        Transform fieldRoom = fieldGround.transform.parent;
        GameObject wolf = hidden.FirstOrDefault(g => g != null && g.transform.IsChildOf(fieldRoom) && g.GetComponent<MonsterAI>() != null);
        Vector2 at = laneStart + dir * 6f;
        if (wolf != null) { wolf.SetActive(true); yield return WaitFrames(1); }
        else wolf = SpawnLikeSpawner(wolfPrefab, at, "PatrolMonster_field");
        GameObject rhino = SpawnLikeSpawner(rhinoPrefab, at, "RhinoMonster_field");
        GameObject porcupine = SpawnLikeSpawner(porcupinePrefab, at + perp * 3.4f, "PorcupineMonster_field");
        Warp(wolf, at - perp * 3.4f);
        foreach (GameObject g in new[] { wolf, rhino, porcupine }) foreach (MonsterBrain b in g.GetComponents<MonsterBrain>()) b.enabled = false;
        yield return WaitFrames(1);
        foreach (GameObject g in new[] { wolf, rhino, porcupine })
        {
            Transform v = g.transform.Find("Visual");
            if (v != null) v.position = new Vector3(g.transform.position.x, g.transform.position.z, 0f);
        }
        Vector2 playerAt = at - dir * 3f + perp * 1.7f;
        PlacePlayer(playerAt, Deg(dir));
        yield return WaitFrames(3);
        Vector2[] pts = { Ground(wolf), Ground(rhino), Ground(porcupine), playerAt };
        Vector2 min = new Vector2(pts.Min(p => p.x), pts.Min(p => p.y)) - new Vector2(2.2f, 1.6f);
        Vector2 max = new Vector2(pts.Max(p => p.x), pts.Max(p => p.y)) + new Vector2(2.2f, 1.6f);
        Texture2D tex = Capture(min, max);
        if (tex != null) panels.Add(("10 FIELD WOLF PORCUPINE PLAYER", tex));
        Check(tex != null, $"Field 방에서 늑대 {Vector2.Distance(pts[0], pts[1]):F1}칸, 호저 {Vector2.Distance(pts[2], pts[1]):F1}칸, 플레이어 {Vector2.Distance(pts[3], pts[1]):F1}칸 옆에 코뿔소");
        Object.Destroy(rhino);
        Object.Destroy(porcupine);
        yield return WaitFrames(2);
    }

    private static void CaptureAround(RhinoMonsterAI r, string label)
    {
        Vector2 c = r.BodyCenter;
        Texture2D tex = Capture(c - new Vector2(3f, 2.4f), c + new Vector2(3f, 2.4f));
        if (tex != null) panels.Add((label, tex));
    }

    private static Texture2D Capture(Vector2 worldMin, Vector2 worldMax)
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        Light2D[] globals = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.lightType == Light2D.LightType.Global).ToArray();
        float[] saved = globals.Select(l => l.intensity).ToArray();
        foreach (Light2D l in globals) l.intensity = 1f;
        const int W = 1920, H = 1080;
        RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        RenderTexture prevTarget = cam.targetTexture, prevActive = RenderTexture.active;
        Vector3 camPos = cam.transform.position;
        Vector2 center = (worldMin + worldMax) * 0.5f;
        cam.transform.position = new Vector3(center.x, center.y, camPos.z);
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

    private static void WriteSheet()
    {
        const int gap = 10, labelH = 40;
        string[] expected = { "1 IDLE", "2 WALK", "3 CHARGE", "4 DASH", "5 RECOVER", "6 WALL STUN", "7 WALL EDGE x3", "8 HURT", "9 FALLEN SHADOW", "10 FIELD WOLF PORCUPINE PLAYER" };
        List<(string label, Texture2D tex)> list = expected.Select(e => panels.FirstOrDefault(p => p.label == e)).Where(p => p.tex != null).ToList();
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
            GridMapAuditImages.DrawText(outTex, x, totalH - 6, label, 3, Color.white, bg);
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

    private static Vector2 Ground(RhinoMonsterAI r) => new Vector2(r.transform.position.x, r.transform.position.z);
    private static Vector2 Ground(GameObject g) => new Vector2(g.transform.position.x, g.transform.position.z);
    private static float Deg(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

    private static string SpriteName(RhinoMonsterAI r)
    {
        SpriteRenderer s = r.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
        return s != null && s.sprite != null ? s.sprite.name : "";
    }

    private static int SlotOf(string id)
    {
        for (int i = 0; i < gm.InventorySlotCount; i++)
        {
            InventorySlotData s = gm.GetSlot(i);
            if (s.Type == InventoryItemType.Animal && s.AnimalKind == AnimalBehaviorKind.MonsterCorpse && s.AnimalId == id) return i;
        }
        return -1;
    }

    // 스폰하는 쪽(VillageMapGenerator, VillageMonsterSpawner)과 같은 순서: NavMesh 자리에 만들고 이름을 붙이고 원래 프리팹을 기억시킨다.
    private static GameObject SpawnLikeSpawner(GameObject prefab, Vector2 at, string name)
    {
        Vector3 pos = NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position : new Vector3(at.x, 0f, at.y);
        GameObject m = Object.Instantiate(prefab, pos, Quaternion.identity);
        m.name = name;
        MonsterCorpsePickup.RememberSource(m, prefab);
        return m;
    }

    private static void Warp(GameObject m, Vector2 at)
    {
        NavMeshAgent agent = m.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh && NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas)) agent.Warp(hit.position);
    }

    // 플레이어를 멀리 두고(발견 범위 밖) 코뿔소를 길 시작점으로 옮긴 뒤, 배회로 돌아올 때까지 기다린다.
    private static IEnumerator ResetAt(RhinoMonsterAI r, Vector2 at)
    {
        PlayerFar();
        for (int i = 0; i < 120 && (r.CurrentState != RhinoMonsterAI.State.Wander || r.Health.IsIncapacitated); i++) yield return WaitFrames(1);
        Warp(r.gameObject, at);
        NavMeshAgent agent = r.GetComponent<NavMeshAgent>();
        if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
        yield return WaitFrames(2);
        Warp(r.gameObject, at);
        if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
    }

    private static void PlayerFar() => PlacePlayer(laneStart - perp * 40f, 0f);

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

    private static Sprite wallSprite;

    // 시험용 벽: 리지드바디 없는 고정 BoxCollider2D(돌진 벽 판정이 벽으로 본다). dir에 수직으로 length칸, 두께 0.4칸.
    // 그림에서 몸과 벽 경계가 보이도록 같은 크기의 회색 판을 모든 세계 그림 위에 그린다(몸이 파묻히면 판에 가려진다).
    private static GameObject MakeWall(Vector2 at, Vector2 d, float length)
    {
        GameObject wall = new GameObject("RhinoTestWall");
        wall.transform.SetPositionAndRotation(new Vector3(at.x, at.y, 0f), Quaternion.Euler(0f, 0f, Deg(d)));
        wall.transform.localScale = new Vector3(0.4f, length, 1f);
        BoxCollider2D box = wall.AddComponent<BoxCollider2D>();
        box.size = Vector2.one;
        if (wallSprite == null)
        {
            Texture2D t = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            Color c = new Color(0.45f, 0.42f, 0.40f, 1f);
            t.SetPixels(Enumerable.Repeat(c, 16).ToArray());
            t.Apply();
            wallSprite = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
        SpriteRenderer sr = wall.AddComponent<SpriteRenderer>();
        sr.sprite = wallSprite;
        sr.sortingOrder = FootSortOrder.EffectOrder - 1;
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

    // 가장 큰 방(Field)에서 트럭과 떨어진, length칸 곧은 빈 길(바닥·NavMesh 위, 폭 4칸 안에 막는 충돌체 없음)을 방 가운데에 가깝게 찾는다.
    private static bool FindLane(float length, out Vector2 start, out Vector2 d, Vector2[] allowed = null)
    {
        start = Vector2.zero;
        d = Vector2.right;
        fieldGround = null;
        foreach (Transform room in GameObject.Find("GeneratedMap").transform)
        {
            Tilemap g = room.Find("Ground")?.GetComponent<Tilemap>();
            if (g == null || !room.name.StartsWith("Room_")) continue;
            g.CompressBounds();
            if (fieldGround == null || g.cellBounds.size.x * g.cellBounds.size.y > fieldGround.cellBounds.size.x * fieldGround.cellBounds.size.y) fieldGround = g;
        }
        if (fieldGround == null) return false;
        Vector3 c3 = fieldGround.transform.TransformPoint(fieldGround.localBounds.center);
        Vector2 roomCenter = new Vector2(Mathf.Round(c3.x) + 0.5f, Mathf.Round(c3.y) + 0.5f);
        TruckPoint truck = Object.FindAnyObjectByType<TruckPoint>();
        Vector2 truckAt = truck != null ? (Vector2)truck.transform.position : new Vector2(float.MaxValue, float.MaxValue);
        Vector2[] dirs = allowed ?? new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
        float bestD = float.MaxValue;
        bool found = false;
        for (int dy = -10; dy <= 10; dy++)
            for (int dx = -14; dx <= 14; dx++)
            {
                Vector2 p = roomCenter + new Vector2(dx, dy);
                if (Vector2.Distance(p, truckAt) < 10f) continue;
                foreach (Vector2 dd in dirs)
                {
                    bool ok = true;
                    Vector2 pp = new Vector2(-dd.y, dd.x);
                    for (float k = -1.5f; k <= length && ok; k += 0.5f)
                        for (float s = -1.5f; s <= 1.5f && ok; s += 1.5f)
                        {
                            Vector2 q = p + dd * k + pp * s;
                            if (!NavMesh.SamplePosition(new Vector3(q.x, 0f, q.y), out _, 0.1f, NavMesh.AllAreas)) ok = false;
                            else if (!fieldGround.HasTile(fieldGround.WorldToCell(new Vector3(q.x, q.y, 0f)))) ok = false;
                        }
                    if (!ok) continue;
                    Vector2 mid = p + dd * (length * 0.5f - 0.75f);
                    if (Physics2D.OverlapBoxAll(mid, new Vector2(length + 3f, 4f), Deg(dd)).Any(col => !col.isTrigger && !col.CompareTag("Player"))) continue;
                    float dist = Vector2.Distance(p + dd * length * 0.5f, roomCenter);
                    if (dist < bestD) { bestD = dist; start = p; d = dd; found = true; }
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
