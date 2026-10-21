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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Monsters > Run Weeping Angel Test. 우는천사(WeepingAngelAI)를 실제 마을(격자 3x4, 시드 1)과 실제 플레이어 시야(PlayerVision.IsPointVisible)로 확인한다.
/// 플레이어를 앞뒤로 곧게 트인 길 위에 세우고 부채꼴 방향(LookDirection)을 정한 뒤, 우는천사를 원하는 거리에 직접 놓아 동작을 잰다.
/// 검사: 놀람 전 돌진 후 원형/부채꼴 시야에 처음 들어온 자리에서 멈춤, 벽 뒤는 시야 밖이라 계속 움직임, 놀람 후 15칸 안 시야 밖에서만 추격,
/// 15칸 밖 배회(느린 속도)·안으로 들어오면 추격·밖으로 나가면 배회, 배회 중에도 시야 안이면 멈춤, 접촉 데미지 80(시야 안에서 멈춘 동안은 없음),
/// 무기 4종(포획망·다트·폭탄·지뢰) 효과 없음과 에러 없음, 경고음 파일이 비어도 에러 없음, 경고음은 추격 중에만, 180초에 1마리(그 전에는 없음),
/// 재진입 때 다시 놀람 전, ResetDay/ResetGame 초기화. 프레임 시간 1/30초 고정. 다른 몬스터는 기절시켜 둔다. 동작 검사 동안은 마을 시간을 0에 붙잡아 둔다.
/// 결과: Logs/WeepingAngelTest.txt / batchmode: -executeMethod WeepingAngelTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class WeepingAngelTest
{
    private const string KeyActive = "WAT.Active", KeyDone = "WAT.Done", KeyBatch = "WAT.Batch", KeyPassed = "WAT.Passed";
    private const string ReportPath = "Logs/WeepingAngelTest.txt";
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

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject anglePrefab;
    private static Transform player;
    private static PlayerVision vision;
    private static Light2D ambient, focused;

    static WeepingAngelTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Monsters/Run Weeping Angel Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[WeepingAngelTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            if (routine != null) Debug.LogWarning("[WeepingAngelTest] 테스트가 끝나기 전에 Play 모드가 꺼졌다");
            EditorApplication.update -= Step;
            nested.Clear();
            TearDown();
            return;
        }
        foreach (MonsterHealth m in MonsterHealth.Active.ToArray()) if (m != null && !m.IsDead) m.Stun(1000f);
        if (holdTime && gm != null) SetTime(0f);
        if (forceLook && player != null) LookSetter?.Invoke(player.GetComponent<PlayerMovement>(), new object[] { lookDirection });
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
        Debug.Log($"[WeepingAngelTest] [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"WeepingAngelTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        gm = GameManager.Instance;
        config = gm != null ? gm.Config : null;
        anglePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeepingAngelAssetBuilder.PrefabPath);
        if (gm == null || config == null || anglePrefab == null)
        {
            Check(false, $"준비 실패: GameManager {gm != null}, config {config != null}, 프리팹 {anglePrefab != null}");
            Finish();
            yield break;
        }

        // --- 0. 프리팹과 설정
        sb.AppendLine("\n== 0. 프리팹과 설정");
        Check(anglePrefab.GetComponent<WeepingAngelAI>() != null && anglePrefab.GetComponent<NavMeshAgent>() != null, "WeepingAngelAI와 NavMeshAgent가 있다");
        Check(anglePrefab.GetComponentInChildren<MonsterHealth>(true) == null && anglePrefab.GetComponentInChildren<MonsterCorpsePickup>(true) == null,
            "MonsterHealth·MonsterCorpsePickup이 없다");
        Check(anglePrefab.GetComponentsInChildren<Collider2D>(true).Length == 0, "2D 충돌체가 없다(다트·시야 벽 판정에 걸리지 않음)");
        SerializedObject so = new SerializedObject(anglePrefab.GetComponent<WeepingAngelAI>());
        Check(so.FindProperty("bodySprite").objectReferenceValue != null && so.FindProperty("warningClip").objectReferenceValue == null,
            "스프라이트 칸은 임시 그림, 경고음 칸은 비어 있다");
        Check(anglePrefab.GetComponentInChildren<AnimalFacingFlipper>(true) != null && anglePrefab.GetComponentInChildren<SimpleFrameAnimator>(true) == null,
            "좌우 반전 컴포넌트는 있고 프레임 애니메이터는 없다(정지 이미지)");
        Check(Mathf.Approximately(config.weepingAngelSpeed, 8.4f) && Mathf.Approximately(config.weepingAngelCatchDamageRatio, 0.8f) && Mathf.Approximately(config.weepingAngelChaseRange, 15f) &&
              Mathf.Approximately(config.weepingAngelWanderSpeed, 1.5f) && Mathf.Approximately(config.weepingAngelWanderRadius, 5f) &&
              Mathf.Approximately(config.weepingAngelWanderWaitMin, 1f) && Mathf.Approximately(config.weepingAngelWanderWaitMax, 3f) &&
              Mathf.Approximately(config.weepingAngelWarningInterval, 1.5f),
            "설정 시작값: 속도 8.4, 데미지 비율 0.8, 추격 거리 15, 배회 속도 1.5·반경 5·대기 1~3초, 경고음 1.5초");
        float bestSprint = new[] { PlayerClass.Scout, PlayerClass.Trapper, PlayerClass.Gunner }
            .Max(c => config.playerMoveSpeed * config.GetClassMoveSpeedMultiplier(c) * config.GetClassSprintSpeedMultiplier(c));
        sb.AppendLine($"  참고: 플레이어 스프린트 최고 속도(러닝화 없이) {bestSprint:F4}, 우는천사/스프린트 = {config.weepingAngelSpeed / bestSprint:F3}배");

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
        Check(Vector2.Angle(focused.transform.up, dir) < 1f, $"부채꼴이 앞 방향을 본다 ({focused.transform.up})");
        int startHealth = gm.Health;

        // --- 1. 놀람 전: 뒤에서 달려와 원형 시야에 들어온 순간 멈춘다
        sb.AppendLine("\n== 1. 놀람 전 돌진 → 원형 시야에 들어온 자리에서 멈춤");
        WeepingAngelAI a = SpawnAngel(laneCenter - dir * behind);
        float startDistance = Dist(a);
        bool seenBefore = false, kept = true;
        float lastDistance = startDistance;
        int frames = 0;
        while (!a.IsSurprised && frames < 300)
        {
            yield return WaitFrames(1);
            frames++;
            if (!a.IsSurprised && vision.IsPointVisible(Pos(a))) seenBefore = true;
            if (frames > 5 && !a.IsSurprised && Dist(a) > lastDistance - 0.01f) kept = false;
            lastDistance = Dist(a);
        }
        float stop1 = Dist(a);
        Check(a.IsSurprised && a.IsFrozen, $"놀람 완료 후 멈춤 ({frames}프레임, {startDistance:F2} → {stop1:F2}칸)");
        Check(kept && !seenBefore, "놀람 전에는 시야 밖에서 쉬지 않고 다가왔다(배회 없음)");
        Check(stop1 <= ambientR + 0.05f && stop1 >= ambientR - StepPerFrame - 0.1f, $"원형 시야 경계에서 멈춤 (멈춘 거리 {stop1:F2}, 원형 반경 {ambientR:F2})");
        Vector2 held = Pos(a);
        yield return WaitFrames(45);
        Check((Pos(a) - held).magnitude < 0.02f && a.CurrentPhase == WeepingAngelAI.Phase.Chase && a.IsFrozen,
            $"시야 안에 있는 동안 멈춰 있다(1.5초 동안 {(Pos(a) - held).magnitude:F3}칸, 단계 {a.CurrentPhase})");
        Check(gm.Health == startHealth && a.WarningCount == 0, $"멈춘 동안 데미지 없음(체력 {gm.Health}), 경고음 없음({a.WarningCount})");

        // --- 2. 놀람 후: 15칸 안에서 시야 밖이면 쫓아오고, 시야 안이면 다시 멈춘다
        sb.AppendLine("\n== 2. 놀람 후 15칸 안: 시야 밖일 때만 추격");
        MovePlayer(Pos(a) + dir * (ambientR + 4f)); // 우는천사가 플레이어 뒤(부채꼴 밖), 원형 밖이 되게 앞으로 옮긴다
        yield return WaitFrames(2);
        float d2 = Dist(a);
        Check(!vision.IsPointVisible(Pos(a)) && d2 <= config.weepingAngelChaseRange, $"시야 밖, 15칸 안 ({d2:F2}칸)");
        frames = 0;
        bool moved = false;
        while (frames < 150 && !(moved && a.IsFrozen))
        {
            yield return WaitFrames(1);
            frames++;
            if (Dist(a) < d2 - 0.2f) moved = true;
        }
        float stop2 = Dist(a);
        Check(moved && a.IsFrozen && stop2 <= ambientR + 0.05f, $"쫓아와서 시야에 들어오자 다시 멈춤 ({d2:F2} → {stop2:F2}칸, {frames}프레임)");
        Check(a.WarningCount >= 1, $"추격하며 움직이는 동안 경고음 {a.WarningCount}회(소리 파일 없음 - 에러 없이 건너뜀)");
        int warnAfterChase = a.WarningCount;
        yield return WaitFrames(60);
        Check(a.WarningCount == warnAfterChase, "시야 안에서 멈춘 동안 경고음 없음");

        // --- 3. 부채꼴 시야
        sb.AppendLine("\n== 3. 놀람 전 돌진 → 부채꼴 시야에 들어온 자리에서 멈춤");
        Object.Destroy(a.gameObject);
        MovePlayer(laneCenter);
        yield return WaitFrames(3);
        a = SpawnAngel(laneCenter + dir * front);
        yield return WaitUntilSurprised(a);
        float stop3 = Dist(a);
        Check(a.IsSurprised && a.IsFrozen && stop3 <= focusedR + 0.05f && stop3 >= focusedR - StepPerFrame - 0.1f && stop3 > ambientR + 0.5f,
            $"부채꼴 반경에서 멈춤 (멈춘 거리 {stop3:F2}, 부채꼴 반경 {focusedR:F2}, 원형 반경 {ambientR:F2})");

        // --- 4. 벽에 가려지면 시야 밖
        sb.AppendLine("\n== 4. 부채꼴 안이어도 벽 뒤는 시야 밖 → 계속 움직임");
        Object.Destroy(a.gameObject);
        float wallAt = (ambientR + focusedR) * 0.5f;
        GameObject wall = Wall(laneCenter + dir * wallAt, dir, 3f);
        yield return WaitFrames(2);
        Vector2 behindWall = laneCenter + dir * (wallAt + 0.6f);
        Check(!vision.IsPointVisible(behindWall), $"벽({wallAt:F2}칸) 바로 뒤는 시야 밖으로 판정된다");
        a = SpawnAngel(laneCenter + dir * front);
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
        Check(a.CurrentPhase == WeepingAngelAI.Phase.Wander && !a.IsFrozen, $"15칸 밖: 배회 (단계 {a.CurrentPhase})");
        Check(path > 0.3f && maxSpeed <= config.weepingAngelWanderSpeed + 0.1f, $"제자리에 멈추지 않고 느리게 움직인다 (4초 {path:F2}칸, 최고 {maxSpeed:F2}/초, 배회 속도 {config.weepingAngelWanderSpeed})");
        Check(a.WarningCount == warnBefore, $"배회 중에는 경고음 없음 ({warnBefore} → {a.WarningCount})");

        // 플레이어를 길 위의 한 점으로 되돌린다: 우는천사와 5~13칸 떨어진 곳을 고르고, 우는천사를 등지게(부채꼴 반대쪽) 돌아선다.
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
        Check(!vision.IsPointVisible(Pos(a)) && d5 <= config.weepingAngelChaseRange, $"플레이어를 우는천사와 {d5:F2}칸 떨어진 길 위로(등지고) 옮겼다");
        frames = 0;
        float minD = d5;
        while (frames < 60) { yield return WaitFrames(1); frames++; minD = Mathf.Min(minD, Dist(a)); }
        Check(a.CurrentPhase == WeepingAngelAI.Phase.Chase && minD < d5 - 1f, $"15칸 안으로 들어오면 바로 추격 ({d5:F2} → {minD:F2}칸, 단계 {a.CurrentPhase})");
        MovePlayer(laneCenter + new Vector2(0f, 500f));
        yield return WaitFrames(5);
        Check(a.CurrentPhase == WeepingAngelAI.Phase.Wander, $"다시 15칸 밖으로 나가면 배회로 돌아온다 (단계 {a.CurrentPhase})");
        lookDirection = dir;

        // --- 6. 배회 중에도 시야 안이면 멈춘다 (추격 거리를 0.5로 줄인 복제 설정)
        sb.AppendLine("\n== 6. 배회 중에도 시야 안이면 멈추고, 시야 밖이면 움직인다");
        Object.Destroy(a.gameObject);
        MovePlayer(laneCenter);
        yield return WaitFrames(3);
        GameBalanceConfig shortRange = Object.Instantiate(config);
        shortRange.weepingAngelChaseRange = 0.5f;
        a = SpawnAngel(laneCenter - dir * behind, shortRange);
        yield return WaitUntilSurprised(a);
        yield return WaitFrames(5);
        held = Pos(a);
        yield return WaitFrames(60);
        Check(a.CurrentPhase == WeepingAngelAI.Phase.Wander && a.IsFrozen && (Pos(a) - held).magnitude < 0.02f,
            $"추격 거리 밖(배회)이어도 시야 안이면 멈춘다 (단계 {a.CurrentPhase}, 2초 {(Pos(a) - held).magnitude:F3}칸)");
        vision.enabled = false; // 시야를 끄면 어디도 보이지 않는다 → 시야 밖
        held = Pos(a);
        yield return WaitFrames(60);
        Check(!a.IsFrozen && (Pos(a) - held).magnitude > 0.1f, $"시야 밖이 되면 배회를 이어간다 (2초 {(Pos(a) - held).magnitude:F2}칸)");
        vision.enabled = true;
        Object.Destroy(a.gameObject);
        Object.Destroy(shortRange);

        // --- 7. 접촉 데미지
        sb.AppendLine("\n== 7. 접촉 데미지 = 최대 체력 x 0.8 (클래스 3종, 시야 안에서 멈춘 동안은 없음)");
        // 클래스를 바꿔 장착하려면 해금돼 있어야 한다(시작 클래스 선택은 끝났으므로 UnlockClassFree는 더하기만 한다).
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.UnlockClassFree(PlayerClass.Gunner);
        foreach ((PlayerClass cls, int expected) in new[] { (PlayerClass.Scout, 80), (PlayerClass.Trapper, 104), (PlayerClass.Gunner, 40) })
        {
            gm.SelectClass(cls); // 그 클래스의 최대 체력으로 가득 채운다
            yield return WaitFrames(3);
            int full = gm.Health;
            a = SpawnAngel(laneCenter - dir * 0.3f);
            yield return WaitFrames(30);
            Check(a.IsFrozen && gm.Health == full, $"{cls}: 접촉 거리 안이어도 시야 안에서 멈춘 동안 데미지 없음 (체력 {gm.Health}/{full})");
            vision.enabled = false;
            yield return WaitFrames(5);
            Check(full == GameManager.MaxHealth && full - gm.Health == expected,
                $"{cls}: 최대 체력 {GameManager.MaxHealth}, 접촉 데미지 {full - gm.Health} (기대 {expected}) → 남은 체력 {gm.Health}");
            vision.enabled = true;
            Object.Destroy(a.gameObject);
            yield return WaitFrames(3);
        }
        gm.SelectClass(PlayerClass.Scout);
        yield return WaitFrames(3);

        // --- 8. 무기 4종
        sb.AppendLine("\n== 8. 무기 4종은 효과가 없고 에러도 없다");
        int logsBefore = logs.Count;
        a = SpawnAngel(laneCenter + dir * 1.2f);
        yield return WaitFrames(5);
        Vector2 angelAt = Pos(a);
        WeepingAngelAI.Phase phaseBefore = a.CurrentPhase;

        typeof(PlayerWeaponController).GetMethod("SwingNet", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player.GetComponent<PlayerWeaponController>(), null);
        yield return WaitFrames(5);
        Check(a != null && a.GetComponent<MonsterHealth>() == null && a.CurrentPhase == phaseBefore, "포획망: 대상이 아니다(MonsterHealth.Active에 없음) - 변화 없음");

        TranquilizerDart dartPrefab = AssetDatabase.LoadAssetAtPath<TranquilizerDart>("Assets/Prefabs/TranquilizerDart.prefab");
        TranquilizerDart dart = Object.Instantiate(dartPrefab, (Vector3)laneCenter, Quaternion.identity);
        dart.Launch(dir, 12f, 6f, 1f, 3f, 1f);
        bool passed = false, aliveAfterPass = false;
        for (int i = 0; i < 30 && dart != null; i++)
        {
            yield return WaitFrames(1);
            if (dart == null) break;
            float along = Vector2.Dot((Vector2)dart.transform.position - laneCenter, dir);
            if (along > Vector2.Dot(angelAt - laneCenter, dir) + 0.5f) { passed = true; aliveAfterPass = true; break; }
        }
        Check(passed && aliveAfterPass, "마취 다트: 우는천사를 지나쳐 계속 날아간다(맞지 않음)");
        if (dart != null) Object.Destroy(dart.gameObject);

        int bombSlot = Enumerable.Range(0, gm.InventorySlotCount).FirstOrDefault(i => gm.GetSlot(i).Type == InventoryItemType.Bomb);
        gm.SelectSlot(bombSlot);
        bool hadBomb = gm.GetSlot(bombSlot).Type == InventoryItemType.Bomb;
        typeof(PlayerGadgetController).GetMethod("UseBomb", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player.GetComponent<PlayerGadgetController>(), null);
        yield return WaitFrames(5);
        Check(hadBomb && gm.GetSlot(bombSlot).Type == InventoryItemType.None && a != null && a.CurrentPhase == phaseBefore,
            "폭탄: 터졌지만(인벤토리에서 소모) 우는천사에는 데미지·기절이 없다");

        MineTrap minePrefab = AssetDatabase.LoadAssetAtPath<MineTrap>("Assets/Prefabs/MineTrap.prefab");
        MineTrap mine = Object.Instantiate(minePrefab, new Vector3(angelAt.x, angelAt.y, 0f), Quaternion.identity);
        yield return WaitFrames(30);
        bool triggered = mine != null && (bool)typeof(MineTrap).GetField("triggered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mine);
        Check(mine != null && !triggered, "지뢰: 우는천사가 위에 있어도 터지지 않는다");
        if (mine != null) Object.Destroy(mine.gameObject);
        Check(logs.Count == logsBefore, $"무기 4종을 쓰는 동안 에러 없음 ({logs.Count - logsBefore}건)");
        Object.Destroy(a.gameObject);
        yield return WaitFrames(2);

        // --- 9. 시간 소환: 180초에 1마리
        sb.AppendLine("\n== 9. 시간 소환: 180초 전에는 없고 180초에 1마리");
        forceLook = false;
        holdTime = false;
        yield return Load("TruckScene");
        gm.ResetDay();
        yield return Load("VillageScene");
        int livingBefore = VillageMonsterSpawner.CountLivingMonsters();
        SetTime(179.5f);
        yield return WaitFrames(12); // 약 179.9초
        Check(gm.VillageTime < 180f && Angels().Count == 0, $"{gm.VillageTime:F2}초: 우는천사 없음");
        yield return WaitForAngels(1, 45);
        List<WeepingAngelAI> spawned = Angels();
        Check(gm.VillageTime >= 180f && spawned.Count == 1, $"{gm.VillageTime:F2}초: 우는천사 {spawned.Count}마리");
        Check(spawned.Count == 1 && !spawned[0].IsSurprised, "소환 직후는 놀람 전");
        Check(VillageMonsterSpawner.CountLivingMonsters() == livingBefore, $"MonsterHealth가 없어 마을 몬스터 상한 계산에는 들어가지 않는다 ({livingBefore} → {VillageMonsterSpawner.CountLivingMonsters()})");
        if (spawned.Count == 1)
        {
            float spawnDist = Vector2.Distance(Pos(spawned[0]), player.position);
            Check(spawnDist >= config.villageSpawnMinPlayerDistance - 0.5f, $"소환 위치가 플레이어에게서 {spawnDist:F1}칸 (최소 {config.villageSpawnMinPlayerDistance})");
        }
        yield return WaitFrames(150);
        Check(Angels().Count == 1, "간격 소환이 없어 더 늘지 않는다");
        bool surprisedBeforeLeave = Angels().Count == 1 && Angels()[0].IsSurprised;

        // --- 10. 재진입
        sb.AppendLine("\n== 10. 같은 Day 재진입: 다시 놀람 전으로 1마리");
        yield return Load("TruckScene");
        yield return WaitFrames(10);
        yield return Load("VillageScene");
        yield return WaitForAngels(1, 45);
        spawned = Angels();
        Check(spawned.Count == 1 && !spawned[0].IsSurprised && spawned[0].CurrentPhase == WeepingAngelAI.Phase.Ambush,
            $"재진입 뒤 {spawned.Count}마리, 놀람 전 (나가기 전 놀람 {surprisedBeforeLeave})");
        Check(gm.VillageSpawns.EnteredPhase == 2 && gm.VillageSpawns.Get(2, 0).Issued == 1, "2구간 진입 소환을 다시 하지 않았다(발급 1)");

        // --- 11. Day 변경, 새 게임
        sb.AppendLine("\n== 11. Day 변경과 새 게임에서 초기화");
        yield return Load("TruckScene");
        gm.ResetDay();
        Check(gm.VillageSpawns.EnteredPhase == 0 && gm.VillageTime == 0f, "ResetDay: 소환 기록과 시간이 비었다");
        yield return Load("VillageScene");
        yield return WaitFrames(30);
        Check(Angels().Count == 0, "새 Day 마을 0초: 우는천사 없음");
        SetTime(179.9f);
        yield return WaitFrames(5);
        yield return WaitForAngels(1, 45);
        Check(Angels().Count == 1, "새 Day에도 180초에 다시 1마리");
        yield return Load("TruckScene");
        gm.ResetGame();
        Check(gm.VillageSpawns.EnteredPhase == 0 && gm.VillageTime == 0f, "ResetGame: 소환 기록과 시간이 비었다");
        RestartClass();
        yield return Load("VillageScene");
        yield return WaitFrames(30);
        Check(Angels().Count == 0, "새 게임 마을 0초: 우는천사 없음");

        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
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
        Debug.Log("[WeepingAngelTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 도움 함수 =====================

    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo VillageTimeSetter = typeof(GameManager).GetProperty(nameof(GameManager.VillageTime)).GetSetMethod(true);

    private static void SetTime(float seconds) => VillageTimeSetter.Invoke(gm, new object[] { seconds });

    private static Vector2 Pos(WeepingAngelAI a) => new Vector2(a.transform.position.x, a.transform.position.z);

    private static float Dist(WeepingAngelAI a) => Vector2.Distance(Pos(a), player.position);

    private static List<WeepingAngelAI> Angels() => Object.FindObjectsByType<WeepingAngelAI>(FindObjectsInactive.Exclude).ToList();

    private static WeepingAngelAI SpawnAngel(Vector2 at, GameBalanceConfig overrideConfig = null)
    {
        NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas);
        WeepingAngelAI a = Object.Instantiate(anglePrefab, hit.position, Quaternion.identity).GetComponent<WeepingAngelAI>();
        a.name = "TestWeepingAngel";
        if (overrideConfig != null) typeof(WeepingAngelAI).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(a, overrideConfig);
        return a;
    }

    // 소환기가 자리를 못 찾으면 0.5초 뒤 다시 시도하므로 바로 세지 않고 잠시 기다린다.
    private static IEnumerator WaitForAngels(int count, int maxFrames)
    {
        for (int i = 0; i < maxFrames && Angels().Count < count; i++) yield return WaitFrames(1);
    }

    private static IEnumerator WaitUntilSurprised(WeepingAngelAI a)
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
                    // 길 양옆 0.6칸도 막히지 않게(우는천사 몸 반경 0.4)
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
        // 씬마다 플레이어가 새로 생기므로 다시 잡는다.
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
