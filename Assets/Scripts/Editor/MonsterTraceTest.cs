using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Tools > Monsters > Run Monster Trace Test. 몬스터 공통 구조를 정리하기 전후의 동작을 비교하려고, 늑대(MonsterAI)와 소리반응형(SoundReactiveMonsterAI)이
/// 정해진 상황에서 프레임마다 어떤 상태를 지나고 어디로 움직이는지 텍스트로 기록한다. 런타임 코드는 읽기만 한다(상태 등 private 값은 리플렉션).
/// - 맵: 격자 3x4, 시드 1과 2(VillageMapGenerator.EditorLayoutOverride). 프레임 시간 1/30초 고정(Time.captureDeltaTime). 상황마다 Random.InitState 고정
/// - 기록은 플레이어 루프의 PostLateUpdate에 붙인 단계에서 매 프레임 한 번씩 남긴다(에디터 업데이트 횟수와 무관)
/// - 상황: 플레이어 없음(배회·순찰) / 발견과 추격 시작 / 추격 포기·탐색·최대 추격 시간 / 소리 강도 0.3·0.6·1.0과 거리별 조사 / 포획망·마취·폭탄·지뢰 피해와 사망 / 같은 Day 재진입 복원
/// 결과: 메뉴는 Logs/monster_trace_latest.txt, batch는 -traceOut 경로(기본 Logs/monster_trace_before.txt)
/// batchmode: -executeMethod MonsterTraceTest.RunBatch -traceOut Logs/monster_trace_before.txt
/// 비교: Tools > Monsters > Compare Monster Trace (before와 latest), batch: -executeMethod MonsterTraceTest.CompareBatch -traceA a.txt -traceB b.txt
/// </summary>
[InitializeOnLoad]
public static class MonsterTraceTest
{
    private const string KeyActive = "MTT.Active", KeyBatch = "MTT.Batch", KeyOut = "MTT.Out", KeyExit = "MTT.Exit";
    public const string BeforePath = "Logs/monster_trace_before.txt";
    public const string LatestPath = "Logs/monster_trace_latest.txt";
    private const string ComparePath = "Logs/monster_trace_compare.txt";
    private const float FrameTime = 1f / 30f;
    private static readonly int[] Seeds = { 1, 2 };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> problems;
    private static PlayerLoopSystem savedLoop;
    private static bool loopInstalled;
    private static bool finished;

    // 상황마다 기록하는 몬스터와 프레임 번호
    private static string scenario;
    private static int frame;
    private static readonly List<MonsterHealth> tracked = new List<MonsterHealth>();

    static MonsterTraceTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    // ===================== 진입 =====================

    [MenuItem("Tools/Monsters/Run Monster Trace Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[MonsterTrace] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(false, LatestPath);
    }

    public static void RunBatch()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-traceOut");
        Begin(true, i >= 0 && i + 1 < args.Length ? args[i + 1] : BeforePath);
    }

    private static void Begin(bool batch, string outPath)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetString(KeyOut, outPath);
        SessionState.SetInt(KeyExit, 1);
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            sb = new StringBuilder();
            problems = new List<string>();
            nested.Clear();
            tracked.Clear();
            finished = false;
            routine = Run();
            InstallLoop();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            UninstallLoop();
            Time.captureDeltaTime = 0f;
            UnpinEngine();
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetInt(KeyExit, 1));
        }
    }

    // ===================== 플레이어 루프 =====================

    private static void InstallLoop()
    {
        if (loopInstalled) return;
        savedLoop = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem root = savedLoop;
        PlayerLoopSystem[] subs = root.subSystemList.ToArray();
        for (int i = 0; i < subs.Length; i++)
        {
            if (subs[i].type != typeof(PostLateUpdate)) continue;
            var list = subs[i].subSystemList.ToList();
            list.Add(new PlayerLoopSystem { type = typeof(MonsterTraceTest), updateDelegate = Tick });
            subs[i].subSystemList = list.ToArray();
        }
        root.subSystemList = subs;
        PlayerLoop.SetPlayerLoop(root);
        loopInstalled = true;
    }

    private static void UninstallLoop()
    {
        if (!loopInstalled) return;
        PlayerLoop.SetPlayerLoop(savedLoop);
        loopInstalled = false;
    }

    // 매 프레임 한 번: 기록하고 시나리오를 한 걸음 진행한다.
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || finished || routine == null) return;
        try
        {
            RecordFrame();
            if (!Advance()) Finish();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            problems.Add("예외: " + e.Message);
            Finish();
        }
    }

    private static bool Advance()
    {
        while (true)
        {
            IEnumerator top = nested.Count > 0 ? nested.Peek() : routine;
            if (!top.MoveNext())
            {
                if (nested.Count == 0) return false;
                nested.Pop();
                continue;
            }
            if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
            return true;
        }
    }

    private static void Finish()
    {
        finished = true;
        Time.captureDeltaTime = 0f;
        UnpinEngine();
        string outPath = SessionState.GetString(KeyOut, LatestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        if (problems.Count > 0)
        {
            sb.AppendLine("# 문제");
            foreach (string p in problems) sb.AppendLine("# " + p);
        }
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        int lines = sb.ToString().Count(c => c == '\n');
        Debug.Log($"[MonsterTrace] {lines}줄 기록, 문제 {problems.Count}건 - {Path.GetFullPath(outPath)}");
        SessionState.SetInt(KeyExit, problems.Count == 0 ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    // ===================== 기록 =====================

    private static void Note(string text) => sb.AppendLine($"# {scenario} f{frame:D4} {text}");

    private static void RecordFrame()
    {
        if (string.IsNullOrEmpty(scenario)) return;
        foreach (MonsterHealth m in tracked)
        {
            if (m == null) { sb.AppendLine($"{scenario}\t{frame:D4}\t(파괴됨)"); continue; }
            sb.AppendLine(Describe(m));
        }
        frame++;
    }

    private static string Describe(MonsterHealth m)
    {
        GameObject go = m.gameObject;
        string id = HierarchyId(go.transform);
        if (!go.activeInHierarchy) return $"{scenario}\t{frame:D4}\t{id}\t(비활성)";
        Component brain = Brain(go);
        string state = brain != null ? ReadField(brain, "state")?.ToString() ?? "?" : "-";
        string caught = brain != null && ReadField(brain, "caughtLogged") is bool c ? (c ? "1" : "0") : "-";
        Vector2 p = m.GamePosition;
        string flags = $"{(m.IsDead ? "D" : "")}{(m.IsStunned ? "S" : "")}{(m.IsAsleep ? "Z" : "")}";
        SimpleFrameAnimator anim = go.GetComponentInChildren<SimpleFrameAnimator>();
        SpriteRenderer sr = anim != null ? anim.GetComponent<SpriteRenderer>() : null;
        string animText = anim != null ? $"{(anim.IsFleeing ? "F" : "")}{(anim.IsMoving ? "M" : "")}:{(sr != null && sr.sprite != null ? sr.sprite.name : "null")}" : "-";
        return $"{scenario}\t{frame:D4}\t{id}\t{state}\t{p.x:F2},{p.y:F2}\thp={m.Health:0.##}{(flags.Length > 0 ? " " + flags : "")}\tanim={animText}\tchase={ChaseReported(brain)}\tcaught={caught}";
    }

    private static Component Brain(GameObject go) =>
        (Component)go.GetComponent<MonsterAI>() ?? go.GetComponent<SoundReactiveMonsterAI>();

    // 정리 후 상태 등이 기반 클래스로 옮겨가도 찾도록 상속 계층을 따라 올라가며 찾는다.
    private static object ReadField(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(target);
        }
        return null;
    }

    // 몬스터가 AudioManager.SetChasing으로 보고한 마지막 값(추격 중인지). AudioManager의 chaseVoices에서 읽는다.
    private static string ChaseReported(Component brain)
    {
        if (brain == null || AudioManager.Instance == null) return "-";
        var voices = ReadField(AudioManager.Instance, "chaseVoices") as System.Collections.IDictionary;
        if (voices == null || !voices.Contains(brain)) return "0";
        object voice = voices[brain];
        return voice != null && ReadField(voice, "Chasing") is bool on && on ? "1" : "0";
    }

    private static string HierarchyId(Transform t)
    {
        var path = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) path.Insert(0, "/").Insert(0, p.name);
        return path.ToString();
    }

    // ===================== 시나리오 =====================

    private static GameBalanceConfig config;
    private static Transform player;
    private static Rigidbody2D playerBody;

    // 기록이 흔들리지 않게 테스트 동안만 바꾸는 엔진 전역 설정(런타임 코드는 그대로). 끝나면 되돌린다.
    // - NavMesh 경로 계산을 한 프레임 안에 끝내게 해 경로가 몇 프레임 뒤에 나오는지가 실행마다 달라지지 않게 한다
    // - 작업 스레드를 쓰지 않게 해 NavMesh 카빙·군중 이동 같은 비동기 작업이 끝나는 프레임을 고정한다
    private static int savedPathIterations = -1, savedWorkers = -1;

    private static void PinEngine()
    {
        savedPathIterations = UnityEngine.AI.NavMesh.pathfindingIterationsPerFrame;
        UnityEngine.AI.NavMesh.pathfindingIterationsPerFrame = 100000;
        savedWorkers = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
        Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = 0;
    }

    private static void UnpinEngine()
    {
        if (savedPathIterations >= 0) UnityEngine.AI.NavMesh.pathfindingIterationsPerFrame = savedPathIterations;
        if (savedWorkers >= 0) Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = savedWorkers;
        savedPathIterations = savedWorkers = -1;
    }

    private static IEnumerator Run()
    {
        Time.captureDeltaTime = FrameTime;
        PinEngine();
        config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>("Assets/Settings/GameBalanceConfig.asset");
        ClearStaleSingletons();
        sb.AppendLine("# Monster Trace (격자 3x4, 프레임 1/30초). 열: 상황 / 프레임 / 몬스터 Id / 상태 / 위치(x,y) / 체력·플래그(D 사망 S 스턴 Z 수면) / 애니메이션(F 도주 M 이동:스프라이트) / 추격음 보고 / 잡기 판정");

        foreach (int seed in Seeds)
        {
            yield return Scenario($"s{seed}.idle", seed, IdlePatrol());
            yield return Scenario($"s{seed}.wolfChase", seed, WolfChase());
            yield return Scenario($"s{seed}.srmChase", seed, SoundReactiveChase());
            yield return Scenario($"s{seed}.sound", seed, SoundEvents());
            yield return Scenario($"s{seed}.weapons", seed, Weapons());
        }
        yield return Reentry("s1.reentry", 1);
    }

    // 도메인 리로드가 꺼진 에디터에서 이전 Play의 파괴된 GameManager/AudioManager가 static에 남아 있으면 비운다(테스트 쪽에서만).
    private static void ClearStaleSingletons()
    {
        foreach (Type t in new[] { typeof(GameManager), typeof(AudioManager) })
        {
            PropertyInfo prop = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            var current = prop?.GetValue(null) as Object;
            if (!ReferenceEquals(current, null) && current == null) prop.SetValue(null, null);
        }
    }

    // 맵을 새로 불러오고 대상 몬스터를 고른 뒤 난수를 고정하고 기록을 시작한다.
    private static IEnumerator Scenario(string name, int seed, IEnumerator body)
    {
        yield return LoadMap(seed);
        scenario = name;
        frame = 0;
        Random.InitState(1000 + seed);
        yield return body;
        scenario = null;
        tracked.Clear();
    }

    private static IEnumerator LoadMap(int seed)
    {
        scenario = null;
        tracked.Clear();
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = seed };
        SceneManager.LoadScene(Path.GetFileNameWithoutExtension(VillageRoomBuilder.ScenePath));
        for (int i = 0; i < SettleFrames; i++) yield return null;

        if (AudioManager.Instance == null) new GameObject("TraceAudioManager").AddComponent<AudioManager>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        player = p != null ? p.transform : null;
        playerBody = p != null ? p.GetComponent<Rigidbody2D>() : null;
        if (player == null) problems.Add($"시드 {seed}: 플레이어가 없다");

        // 대상이 아닌 동물은 모두 끈다(도주 소리 이벤트가 소리반응형을 건드리지 않게).
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        MovePlayer(Far);
        yield return null;
    }

    private static readonly Vector2 Far = new Vector2(100000f, 100000f);

    // 맵을 불러온 뒤 기록 전에 기다리는 프레임 수(NavMesh 장애물 카빙 등 생성 직후 처리가 끝나도록).
    private const int SettleFrames = 60;

    private static void MovePlayer(Vector2 at)
    {
        if (player == null) return;
        player.position = new Vector3(at.x, at.y, player.position.z);
        if (playerBody != null)
        {
            playerBody.position = at;
            playerBody.linearVelocity = Vector2.zero;
        }
        Physics2D.SyncTransforms();
    }

    private static List<T> Sorted<T>() where T : Component =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Exclude).OrderBy(c => HierarchyId(c.transform), StringComparer.Ordinal).ToList();

    // 대상 몬스터만 남기고 나머지 몬스터는 끈다.
    private static void Track(params MonsterHealth[] keep)
    {
        tracked.Clear();
        tracked.AddRange(keep.Where(k => k != null));
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
            if (!tracked.Contains(m)) m.gameObject.SetActive(false);
        foreach (MonsterHealth m in tracked) Note($"대상 {HierarchyId(m.transform)} @ {m.GamePosition.x:F2},{m.GamePosition.y:F2}");
    }

    private static IEnumerator Wait(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private static readonly Vector2[] Dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

    private static bool Clear(Vector2 at) =>
        !Physics2D.OverlapPointAll(at).Any(c => !c.isTrigger && c.attachedRigidbody == null);

    // from에서 dist만큼 떨어지고 사이에 벽이 없는 빈 자리.
    private static bool TryOpenSpot(Vector2 from, float dist, out Vector2 spot)
    {
        foreach (Vector2 d in Dirs)
        {
            Vector2 s = from + d * dist;
            if (Clear(s) && !StaticBlockerLine.IsBlocked(from, s, ~0)) { spot = s; return true; }
        }
        spot = default;
        return false;
    }

    // ---- 1. 플레이어 없음: 배회와 순찰 ----
    private static IEnumerator IdlePatrol()
    {
        MonsterHealth wolf = Sorted<MonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        MonsterHealth srm = Sorted<SoundReactiveMonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        if (wolf == null || srm == null) { problems.Add($"{scenario}: 늑대나 소리반응형이 없다"); yield break; }
        Track(wolf, srm);
        Note("플레이어 없음(아주 먼 곳)");
        yield return Wait(300);
    }

    // ---- 2·3. 늑대: 시야 발견 → 추격 → 거리 벌어짐(탐색) → 영역 밖(귀환) ----
    private static IEnumerator WolfChase()
    {
        float detect = config.patrolMonsterDetectRange, lose = config.patrolMonsterLoseRange;
        foreach (MonsterAI brain in Sorted<MonsterAI>())
        {
            MonsterHealth wolf = brain.GetComponent<MonsterHealth>();
            if (!TryOpenSpot(wolf.GamePosition, detect * 0.7f, out Vector2 near)) continue;
            Track(wolf);
            Note($"플레이어를 발견 거리 안({detect * 0.7f:F2})에 둔다");
            MovePlayer(near);
            yield return Wait(120);

            Vector2 w = wolf.GamePosition;
            if (TryOpenSpot(w, lose + 1f, out Vector2 mid))
            {
                Note($"놓침 거리({lose}) 밖, 영역 안으로 옮긴다 → 탐색");
                MovePlayer(mid);
            }
            else
            {
                Note("놓침 거리 밖 빈 자리가 없어 아주 먼 곳으로 옮긴다");
                MovePlayer(Far);
            }
            yield return Wait(240);

            Note("아주 먼 곳(영역 밖) → 귀환·순찰");
            MovePlayer(Far);
            yield return Wait(150);
            yield break;
        }
        problems.Add($"{scenario}: 플레이어를 둘 빈 자리가 있는 늑대가 없다");
    }

    // ---- 2·3. 소리반응형: 거리 발견 → 계속 3칸 앞에서 도망(최대 추격 시간) → 탐색 → 배회 ----
    private static IEnumerator SoundReactiveChase()
    {
        float detect = config.soundReactiveMonsterDetectRange;
        foreach (SoundReactiveMonsterAI brain in Sorted<SoundReactiveMonsterAI>())
        {
            MonsterHealth srm = brain.GetComponent<MonsterHealth>();
            if (!TryOpenSpot(srm.GamePosition, detect * 0.7f, out Vector2 near)) continue;
            Vector2 away = (near - srm.GamePosition).normalized;
            Track(srm);
            Note($"플레이어를 발견 거리 안({detect * 0.7f:F2})에 둔다");
            MovePlayer(near);
            yield return Wait(45);

            Note("매 프레임 몬스터에서 3칸 떨어진 곳으로 옮긴다(최대 추격 시간 확인)");
            for (int i = 0; i < 390; i++)
            {
                MovePlayer(srm.GamePosition + away * 3f);
                yield return null;
            }
            Note("아주 먼 곳 → 탐색·배회");
            MovePlayer(Far);
            yield return Wait(240);
            yield break;
        }
        problems.Add($"{scenario}: 플레이어를 둘 빈 자리가 있는 소리반응형이 없다");
    }

    // ---- 4. 소리 이벤트: 강도 0.3/0.6/1.0, 거리별 ----
    private static IEnumerator SoundEvents()
    {
        MonsterHealth srm = Sorted<SoundReactiveMonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        if (srm == null) { problems.Add($"{scenario}: 소리반응형이 없다"); yield break; }
        Track(srm);
        Note("플레이어 없음(아주 먼 곳)");
        foreach (float intensity in new[] { 0.3f, 0.6f, 1f })
        {
            foreach (float d in new[] { 2f, 4f, 8f, 12f, 16f })
            {
                Vector2 at = srm.GamePosition + Vector2.right * d;
                Note($"소리 강도 {intensity:0.0} 거리 {d:0} @ {at.x:F2},{at.y:F2} (들리는 반경 {config.soundReactiveMonsterHearRange * intensity:F2})");
                global::SoundEvents.Emit(new Vector3(at.x, at.y, 0f), intensity);
                yield return Wait(45);
            }
        }
    }

    // ---- 5. 무기: 포획망 스턴, 마취(실제 다트), 폭탄, 지뢰(실제 지뢰), 사망과 늑대 시체 ----
    private static IEnumerator Weapons()
    {
        MonsterHealth wolf = Sorted<MonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        MonsterHealth srm = Sorted<SoundReactiveMonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        if (wolf == null || srm == null) { problems.Add($"{scenario}: 늑대나 소리반응형이 없다"); yield break; }
        Track(wolf, srm);
        Note("플레이어 없음(아주 먼 곳)");

        Object weaponOwner = Object.FindAnyObjectByType<PlayerWeaponController>(FindObjectsInactive.Include);
        Object gadgetOwner = Object.FindAnyObjectByType<PlayerGadgetController>(FindObjectsInactive.Include);
        var dartPrefab = weaponOwner != null ? ReadField(weaponOwner, "dartPrefab") as TranquilizerDart : null;
        float dartSpeed = weaponOwner != null && ReadField(weaponOwner, "dartSpeed") is float s ? s : 12f;
        var minePrefab = gadgetOwner != null ? ReadField(gadgetOwner, "minePrefab") as MineTrap : null;
        if (dartPrefab == null || minePrefab == null) problems.Add($"{scenario}: 다트나 지뢰 프리팹을 찾지 못했다");

        // 늑대: 포획망 → 마취 → 지뢰 → 포획망(사망)
        yield return Net(wolf);
        yield return Dart(wolf, dartPrefab, dartSpeed);
        yield return Mine(wolf, minePrefab);
        yield return Net(wolf);
        Note($"늑대 사망 {wolf.IsDead}, 시체 줍기 컴포넌트 {(wolf.GetComponent<MonsterCorpsePickup>() != null)}");
        yield return Wait(30);

        // 소리반응형: 마취 → 폭탄 → 포획망(사망)
        yield return Dart(srm, dartPrefab, dartSpeed);
        yield return Bomb(srm);
        yield return Net(srm);
        Note($"소리반응형 사망 {srm.IsDead}");
        yield return Wait(30);
    }

    // PlayerWeaponController.SwingNet의 몬스터 쪽 처리와 같은 값
    private static IEnumerator Net(MonsterHealth m)
    {
        Note($"포획망 {HierarchyId(m.transform)}: 데미지 {config.netDamage}, 스턴 {config.stunDuration}");
        if (!m.IsDead) { m.TakeDamage(config.netDamage); m.Stun(config.stunDuration); }
        yield return Wait(30);
    }

    // PlayerGadgetController.UseBomb의 몬스터 쪽 처리와 같은 값
    private static IEnumerator Bomb(MonsterHealth m)
    {
        Note($"폭탄 {HierarchyId(m.transform)}: 데미지 {config.bombDamage}, 스턴 {config.bombStunDuration}");
        if (!m.IsDead) { m.TakeDamage(config.bombDamage); m.Stun(config.bombStunDuration); }
        yield return Wait(30);
    }

    private static IEnumerator Dart(MonsterHealth m, TranquilizerDart prefab, float speed)
    {
        if (prefab == null) yield break;
        Vector2 target = m.GamePosition;
        Vector2 from = target + Vector2.right * 0.6f;
        foreach (Vector2 d in Dirs)
        {
            Vector2 f = target + d * 0.6f;
            if (Clear(f) && !StaticBlockerLine.IsBlocked(f, target, ~0)) { from = f; break; }
        }
        Note($"마취 다트 {HierarchyId(m.transform)}: {from.x:F2},{from.y:F2}에서 발사, 데미지 {config.tranquilizerDamage}, 수면 {config.sleepDelay}초 뒤 {config.sleepDuration}초");
        TranquilizerDart dart = Object.Instantiate(prefab, from, Quaternion.identity);
        dart.Launch(target - from, speed, config.tranquilizerRange, config.sleepDelay, config.sleepDuration, config.tranquilizerDamage);
        yield return Wait(170);
    }

    private static IEnumerator Mine(MonsterHealth m, MineTrap prefab)
    {
        if (prefab == null) yield break;
        Vector2 at = m.GamePosition;
        Note($"지뢰 {HierarchyId(m.transform)} 위치에 설치: 데미지 {config.mineDamage}, 스턴 {config.mineStunDuration}");
        Object.Instantiate(prefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
        yield return Wait(60);
    }

    // ---- 6. 같은 Day 재진입: 늑대 시체와 소리반응형 사망 기록 ----
    private static IEnumerator Reentry(string name, int seed)
    {
        // 처치 기록은 GameManager에 남으므로 이 상황에서만 GameManager를 만든다(플레이어는 아주 먼 곳에 두어 잡히지 않게 한다).
        var gmGo = new GameObject("TraceGameManager");
        GameManager gm = gmGo.AddComponent<GameManager>();
        typeof(GameManager).GetField("config", Any)?.SetValue(gm, config);

        yield return LoadMap(seed);
        scenario = name;
        frame = 0;
        Random.InitState(1000 + seed);
        MonsterHealth wolf = Sorted<MonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        MonsterHealth srm = Sorted<SoundReactiveMonsterAI>().Select(b => b.GetComponent<MonsterHealth>()).FirstOrDefault();
        if (wolf == null || srm == null) { problems.Add($"{scenario}: 늑대나 소리반응형이 없다"); yield break; }
        string wolfId = HierarchyId(wolf.transform), srmId = HierarchyId(srm.transform);
        Track(wolf, srm);
        yield return Wait(30);
        Note("둘 다 처치(데미지 99)");
        wolf.TakeDamage(99f);
        srm.TakeDamage(99f);
        yield return Wait(30);
        Note($"마을을 떠났다가 같은 Day로 다시 들어온다 (기록: 늑대 {gm.TryGetMonsterCorpse(wolfId, out _)}, 소리반응형 {gm.TryGetMonsterCorpse(srmId, out _)} - 떠날 때 기록됨)");
        scenario = null;

        yield return LoadMap(seed);
        scenario = name + ".after";
        frame = 0;
        Random.InitState(2000 + seed);
        Note($"재진입 직후 기록: 늑대 {gm.TryGetMonsterCorpse(wolfId, out Vector3 wp)} @ {wp.x:F2},{wp.z:F2}, 소리반응형 {gm.TryGetMonsterCorpse(srmId, out Vector3 sp)} @ {sp.x:F2},{sp.z:F2}");
        var all = Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Include);
        MonsterHealth wolfAgain = all.FirstOrDefault(m => HierarchyId(m.transform) == wolfId);
        MonsterHealth srmAgain = all.FirstOrDefault(m => HierarchyId(m.transform) == srmId);
        tracked.Clear();
        if (wolfAgain != null) tracked.Add(wolfAgain); else Note("늑대가 다시 생기지 않았다");
        if (srmAgain != null) tracked.Add(srmAgain); else Note("소리반응형이 다시 생기지 않았다");
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
            if (!tracked.Contains(m)) m.gameObject.SetActive(false);
        yield return Wait(30);
        scenario = null;
        tracked.Clear();
        Object.Destroy(gmGo);
        yield return null;
    }

    // ===================== 비교 =====================

    [MenuItem("Tools/Monsters/Compare Monster Trace")]
    private static void CompareFromMenu()
    {
        string result = Compare(BeforePath, LatestPath);
        Debug.Log("[MonsterTrace] " + result.Split('\n')[0] + " - " + Path.GetFullPath(ComparePath));
        EditorUtility.DisplayDialog("Compare Monster Trace", result.Length > 1500 ? result.Substring(0, 1500) + "\n…" : result, "OK");
    }

    public static void CompareBatch()
    {
        string[] args = Environment.GetCommandLineArgs();
        int a = Array.IndexOf(args, "-traceA"), b = Array.IndexOf(args, "-traceB");
        string pathA = a >= 0 && a + 1 < args.Length ? args[a + 1] : BeforePath;
        string pathB = b >= 0 && b + 1 < args.Length ? args[b + 1] : LatestPath;
        string result = Compare(pathA, pathB);
        Debug.Log("[MonsterTrace] " + result.Split('\n')[0]);
        EditorApplication.Exit(result.StartsWith("동일") ? 0 : 1);
    }

    // 정리 전(a)과 정리 후(b) 기록을 줄 단위로 비교해 Logs/monster_trace_compare.txt에 쓴다. 같으면 "동일".
    public static string Compare(string pathA, string pathB)
    {
        var report = new StringBuilder();
        if (!File.Exists(pathA) || !File.Exists(pathB))
        {
            report.AppendLine($"비교할 수 없음: {(File.Exists(pathA) ? "" : pathA + " 없음 ")}{(File.Exists(pathB) ? "" : pathB + " 없음")}");
        }
        else
        {
            byte[] bytesA = File.ReadAllBytes(pathA), bytesB = File.ReadAllBytes(pathB);
            string[] la = File.ReadAllLines(pathA), lb = File.ReadAllLines(pathB);
            if (bytesA.SequenceEqual(bytesB))
            {
                report.AppendLine($"동일 ({la.Length}줄, {bytesA.Length}바이트)");
            }
            else
            {
                int diffs = 0;
                var body = new StringBuilder();
                for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
                {
                    string x = i < la.Length ? la[i] : "(없음)", y = i < lb.Length ? lb[i] : "(없음)";
                    if (x == y) continue;
                    diffs++;
                    if (diffs <= 200) body.AppendLine($"{i + 1}행\n  전: {x}\n  후: {y}");
                }
                report.AppendLine($"다름: {diffs}줄 (전 {la.Length}줄, 후 {lb.Length}줄)");
                report.Append(body);
                if (diffs > 200) report.AppendLine($"… 나머지 {diffs - 200}줄 생략");
            }
        }
        report.Insert(0, "");
        File.WriteAllText(ComparePath, $"{pathA} vs {pathB}\n" + report, new UTF8Encoding(false));
        return report.ToString();
    }
}
