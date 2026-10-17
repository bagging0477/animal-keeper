using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// Tools > Animals > Run Wolf Corpse Test. 늑대 시체(MonsterCorpsePickup)의 같은 Day 재진입 규칙을 실제 입력 경로(가상 키보드 E/G)와
/// 실제 씬 전환(마을 → 트럭 → 마을)으로 확인한다. 격자 3x4, 시드는 평소처럼 GameManager의 오늘 시드를 쓴다.
/// 1) 처치 → 줍기 → 납품 → 재진입 → 늑대가 안 나옴
/// 2) 처치 → 남겨둠 → 재진입(제자리 시체, 쓰러진 모습) → 줍기 → G로 버림(버린 자리에 시체) → 재진입(버린 자리에 시체, 원래 자리엔 없음)
///    → 줍기 → 납품 → 재진입(없음)
/// 3) 같은 Day에 버렸다 줍기를 두 번 반복해도 시체가 하나만 있는지
/// 4) 인벤토리가 가득 찬 상태로 버린 시체 앞에 섰을 때의 안내 자막과 E
/// 5) Day가 바뀌거나 새 게임이면 시체/납품 기록이 초기화되는지
/// 테스트 동안 대상이 아닌 몬스터와 동물은 끈다(씬 파일은 바꾸지 않는다). 결과: Logs/WolfCorpseTest.txt
/// batchmode: -executeMethod WolfCorpseTest.RunBatch  (끝나면 에디터가 종료된다, 통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class WolfCorpseTest
{
    private const string KeyActive = "WCT.Active", KeyDone = "WCT.Done", KeyBatch = "WCT.Batch", KeyPassed = "WCT.Passed";
    private const string ReportPath = "Logs/WolfCorpseTest.txt";
    private const float NearCorpse = 0.8f; // MonsterCorpsePickup.interactionRange(1.8)보다 충분히 안쪽

    private static readonly FieldInfo CorpseIdField = typeof(MonsterCorpsePickup).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo DeathFramesField = typeof(SimpleFrameAnimator).GetField("deathFrames", BindingFlags.NonPublic | BindingFlags.Instance);

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();

    private static Keyboard vkb;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static StringBuilder sb;
    private static List<string> failures;

    static WolfCorpseTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Animals/Run Wolf Corpse Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[WolfCorpseTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            vkb = InputSystem.AddDevice<Keyboard>("WolfCorpseTestKeyboard");
            vkb.MakeCurrent();
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
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        vkb = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        VillageMapGenerator.EditorLayoutOverride = null;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith("[WolfCorpseTest]")) return;
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
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb?.ToString() ?? e.ToString(), new UTF8Encoding(false));
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

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        DeliveredIdForFlow5 = null; // 도메인 리로드를 끈 에디터에서 메뉴로 다시 돌릴 때 이전 값이 남지 않게
        sb.AppendLine($"WolfCorpseTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        // 오늘 시드를 그대로 쓰고(Seed = null) 격자 3x4만 고정한다 - 같은 Day 재진입은 평소처럼 같은 맵이 된다.
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = null };

        yield return EnterVillage(null);
        List<string> wolfIds = SpawnedWolves().Select(CorpseId).ToList();
        sb.AppendLine($"마을 늑대 {wolfIds.Count}마리 (Day {gm.CycleCount}, 인벤토리 {gm.InventorySlotCount}칸)");
        if (wolfIds.Count < 4) { Check(false, "늑대가 4마리보다 적어 흐름을 나눠 확인할 수 없다"); yield return Finish(); yield break; }
        string idA = wolfIds[0], idB = wolfIds[1], idC = wolfIds[2], idD = wolfIds[3];
        sb.AppendLine($"  흐름 1 늑대: {idA}\n  흐름 2·3 늑대: {idB}\n  흐름 4 늑대: {idD}\n  흐름 5 늑대: {idC}");

        yield return Flow1(gm, idA);
        yield return Flow2And3(gm, idB);
        yield return Flow4(gm, idD);
        yield return Flow5(gm, idC);

        yield return Finish();
    }

    private static IEnumerator Finish()
    {
        sb.AppendLine($"\n== 콘솔 경고/에러 (Log 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 경고/에러 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[WolfCorpseTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
        yield break;
    }

    // ================= 1. 처치 → 줍기 → 납품 → 재진입 =================

    private static IEnumerator Flow1(GameManager gm, string id)
    {
        sb.AppendLine("\n== 1. 처치 → 줍기 → 납품 → 재진입");
        yield return EnterVillage(id);
        MonsterCorpsePickup wolf = FindSpawned(id);
        Kill(wolf);
        yield return WaitFrames(2);
        yield return PickUp(gm, wolf, id);
        Check(HeldSlot(gm, id) >= 0 && !wolf.gameObject.activeSelf, $"주우면 인벤토리에 들어가고 시체가 사라진다 (슬롯 {HeldSlot(gm, id)})");

        yield return Load("TruckScene");
        Deliver(gm, id);
        Check(gm.IsAnimalRescuedToday(id) && HeldSlot(gm, id) < 0, "트럭에서 납품하면 오늘 구조 기록에 Id가 남는다");

        yield return EnterVillage(id);
        MonsterCorpsePickup back = FindSpawned(id);
        Check(back != null && !back.gameObject.activeSelf && ActiveWithId(id) == 0,
            $"재진입: 같은 Id 늑대가 생기지만 바로 감춰진다 (활성 {ActiveWithId(id)}개)");
    }

    // ================= 2·3. 남겨둠 → 재진입 → 줍기 → 버림 → 재진입 → 줍기 → 납품 → 재진입 =================

    private static IEnumerator Flow2And3(GameManager gm, string id)
    {
        sb.AppendLine("\n== 2. 처치 → 남겨둠 → 재진입 → 줍기 → G로 버림 → 재진입 → 줍기 → 납품 → 재진입");
        yield return EnterVillage(id);
        MonsterCorpsePickup wolf = FindSpawned(id);
        Vector3 spawnNav = wolf.transform.position;
        Kill(wolf);
        yield return WaitFrames(1);
        Sprite[] deathFrames = DeathFrames(wolf);
        Sprite firstShown = VisualSprite(wolf);
        Check(deathFrames.Length > 1 && firstShown == deathFrames[0],
            $"처음 처치하면 사망 애니메이션을 처음 프레임부터 재생한다 ({SpriteName(firstShown)}, 사망 프레임 {deathFrames.Length}장)");
        // 시체가 원래 스폰 자리에서 벗어나 있어야 "원래 자리엔 없음"을 구분할 수 있다 - 처치 직후 자리를 옮기지 않으므로 처치 자리 = 스폰 자리 근처.
        Vector3 killNav = wolf.transform.position;
        yield return WaitFrames(2);

        yield return EnterVillage(id);
        MonsterCorpsePickup restored = FindSpawned(id);
        Vector3 restoredNav = restored != null ? restored.transform.position : Vector3.zero;
        Check(restored != null && restored.gameObject.activeSelf && Health(restored).IsDead && Nav2D(restoredNav, killNav) < 0.05f,
            $"재진입: 처치한 자리에 시체로 있다 (차이 {Nav2D(restoredNav, killNav):0.000})");
        CheckDeadPose(restored, "재진입 시체");
        Check(restored != null && Vector2.Distance(VisualXY(restored), NavToXY(restoredNav)) < 0.05f,
            $"재진입 시체 그림이 시체 자리에 그려진다 (차이 {(restored != null ? Vector2.Distance(VisualXY(restored), NavToXY(restoredNav)) : -1f):0.000})");

        yield return PickUp(gm, restored, id);
        Check(HeldSlot(gm, id) >= 0 && !gm.TryGetMonsterCorpse(id, out _), "주우면 인벤토리에 들어가고 시체 위치 기록이 지워진다");

        // 원래 자리와 확실히 떨어진 곳에서 버린다.
        GameObject player = Player();
        Vector2 dropFrom = FindOpenSpotAwayFrom(NavToXY(spawnNav), 6f);
        Teleport(player, dropFrom);
        yield return WaitFrames(3);
        MonsterCorpsePickup dropped = null;
        yield return Drop(gm, id, c => dropped = c);
        Vector3 droppedNav = dropped != null ? dropped.transform.position : Vector3.zero;
        Check(dropped != null && dropped.gameObject.activeSelf && Health(dropped).IsDead && CorpseId(dropped) == id && HeldSlot(gm, id) < 0,
            $"G로 버리면 같은 Id의 시체가 버린 자리에 놓인다 (플레이어에서 {Vector2.Distance(NavToXY(droppedNav), dropFrom):0.00}칸)");
        CheckDeadPose(dropped, "버린 시체");
        Check(dropped != null && Vector2.Distance(VisualXY(dropped), NavToXY(droppedNav)) < 0.05f,
            $"버린 시체 그림이 시체 자리에 그려진다 (차이 {(dropped != null ? Vector2.Distance(VisualXY(dropped), NavToXY(droppedNav)) : -1f):0.000}, 높이 {droppedNav.y:0.000})");
        bool onMesh = dropped != null && NavMesh.SamplePosition(droppedNav, out NavMeshHit meshHit, 0.5f, NavMesh.AllAreas) &&
                      Nav2D(meshHit.position, droppedNav) < 0.05f;
        Check(onMesh, "버린 시체가 NavMesh(걸을 수 있는 바닥) 위에 있다");
        Check(ActiveWithId(id) == 1, $"버린 뒤 같은 Id의 시체는 하나다 (활성 {ActiveWithId(id)}개)");

        yield return Flow3(gm, id, dropFrom, nav => droppedNav = nav);

        yield return EnterVillage(id);
        MonsterCorpsePickup again = FindSpawned(id);
        Vector3 againNav = again != null ? again.transform.position : Vector3.zero;
        Check(again != null && again.gameObject.activeSelf && Health(again).IsDead && Nav2D(againNav, droppedNav) < 0.05f,
            $"재진입: 버린 자리에 시체가 있다 (차이 {Nav2D(againNav, droppedNav):0.000})");
        Check(Nav2D(againNav, spawnNav) > 3f && SpawnedAliveWithId(id) == 0,
            $"재진입: 원래 스폰 자리에 늑대가 살아 나오지 않는다 (원래 자리에서 {Nav2D(againNav, spawnNav):0.0}칸, 살아 있는 같은 Id {SpawnedAliveWithId(id)})");
        CheckDeadPose(again, "버린 뒤 재진입 시체");
        Check(ActiveWithId(id) == 1, $"재진입 뒤 같은 Id의 시체는 하나다 (활성 {ActiveWithId(id)}개)");

        yield return PickUp(gm, again, id);
        yield return Load("TruckScene");
        Deliver(gm, id);
        yield return EnterVillage(id);
        MonsterCorpsePickup gone = FindSpawned(id);
        Check(gone != null && !gone.gameObject.activeSelf && ActiveWithId(id) == 0 && gm.IsAnimalRescuedToday(id),
            $"납품 뒤 재진입: 늑대가 나오지 않는다 (활성 {ActiveWithId(id)}개)");
    }

    // 같은 Day에 버렸다 줍기를 두 번 더 반복한다. 끝나면 버린 상태로 둔다(흐름 2가 이어서 재진입한다).
    private static IEnumerator Flow3(GameManager gm, string id, Vector2 dropFrom, System.Action<Vector3> onFinalDrop)
    {
        sb.AppendLine("\n== 3. 같은 Day에 버렸다 줍기 반복");
        for (int round = 1; round <= 2; round++)
        {
            MonsterCorpsePickup lying = ActiveCorpses(id).FirstOrDefault();
            yield return PickUp(gm, lying, id);
            int afterPick = ActiveWithId(id);
            Teleport(Player(), dropFrom);
            yield return WaitFrames(3);
            MonsterCorpsePickup dropped = null;
            yield return Drop(gm, id, c => dropped = c);
            int afterDrop = ActiveWithId(id);
            Check(afterPick == 0 && afterDrop == 1 && HeldSlot(gm, id) < 0 && !gm.TryGetMonsterCorpse(id, out _),
                $"{round}회차: 주우면 활성 시체 {afterPick}개, 다시 버리면 {afterDrop}개, 인벤토리에는 없음");
            if (dropped != null) onFinalDrop(dropped.transform.position);
        }
        int total = Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Include).Count(c => CorpseId(c) == id);
        sb.AppendLine($"  (같은 Id 오브젝트는 비활성 포함 {total}개 - 주운 시체는 꺼진 채 남고, 마을을 떠나면 함께 사라진다)");
    }

    // ================= 4. 인벤토리가 가득 찼을 때 =================

    private static IEnumerator Flow4(GameManager gm, string id)
    {
        sb.AppendLine("\n== 4. 인벤토리가 가득 찬 상태로 버린 시체 앞");
        // 흐름 2에서 납품을 마쳤으므로 다른 늑대로 버린 시체를 만든다: 처치 → 줍기 → 버리기.
        yield return EnterVillage(id);
        MonsterCorpsePickup wolf = FindSpawned(id);
        if (wolf == null || !wolf.gameObject.activeSelf || Health(wolf).IsDead) { Check(false, "살아 있는 대상 늑대가 없어 확인하지 못했다"); yield break; }
        Kill(wolf);
        yield return WaitFrames(2);
        yield return PickUp(gm, wolf, id);
        Teleport(Player(), FindOpenSpotAwayFrom(NavToXY(wolf.transform.position), 0f));
        yield return WaitFrames(3);
        MonsterCorpsePickup dropped = null;
        yield return Drop(gm, id, c => dropped = c);
        if (dropped == null) { Check(false, "버린 시체가 생기지 않아 확인하지 못했다"); yield break; }

        int filled = 0;
        while (gm.HasInventorySpace && gm.TryAddMine()) filled++;
        Teleport(Player(), NavToXY(dropped.transform.position) + Vector2.left * NearCorpse);
        yield return WaitFrames(5);
        string subtitle = Subtitle();
        sb.AppendLine($"  인벤토리 {gm.InventorySlotCount}칸을 지뢰 {filled}개로 채움, 시체 앞 자막: \"{subtitle}\"");
        Check(!subtitle.Contains("사체를 획득"), "가득 찬 동안에는 \"E를 눌러 사체를 획득하세요\" 안내가 뜨지 않는다");
        bool hasFullMessage = subtitle.Contains("가득 차");
        sb.AppendLine($"  [{(hasFullMessage ? "OK" : "없음")}] \"인벤토리가 가득 차 획득할 수 없습니다\" 안내 - " +
                      (hasFullMessage ? "뜬다" : "이 문구를 띄우는 코드가 프로젝트에 없다(인벤토리 가득 참 표시 작업이 들어 있지 않음)"));
        yield return Press(Key.E);
        Check(dropped.gameObject.activeSelf && HeldSlot(gm, id) < 0, "E를 눌러도 시체는 그대로 있고 인벤토리에 들어가지 않는다");

        // 다음 흐름을 위해 채운 지뢰를 비운다.
        for (int i = 0; i < gm.InventorySlotCount && filled > 0; i++)
        {
            gm.SelectSlot(i);
            if (gm.GetSlot(i).Type == InventoryItemType.Mine && gm.TryUseSelectedMine()) filled--;
        }
    }

    // ================= 5. Day 변경 / 새 게임 =================

    private static IEnumerator Flow5(GameManager gm, string id)
    {
        sb.AppendLine("\n== 5. Day가 바뀌면 / 새 게임이면 기록 초기화");
        yield return EnterVillage(id);
        MonsterCorpsePickup wolf = FindSpawned(id);
        Kill(wolf);
        yield return WaitFrames(2);
        yield return Load("TruckScene");
        Check(gm.TryGetMonsterCorpse(id, out _), "마을을 떠나면 남겨 둔 시체 자리가 기록된다");
        bool anyRescued = gm.IsAnimalRescuedToday(DeliveredIdForFlow5);
        int dayBefore = gm.CycleCount;
        gm.ResetDay();
        Check(!gm.TryGetMonsterCorpse(id, out _) && !gm.IsAnimalRescuedToday(DeliveredIdForFlow5) && anyRescued,
            $"Day {dayBefore} → {gm.CycleCount}: 시체 자리 기록과 납품(구조) 기록이 비워진다");

        yield return EnterVillage(null);
        int dead = SpawnedWolves().Count(w => Health(w).IsDead);
        int hidden = SpawnedWolves().Count(w => !w.gameObject.activeSelf);
        Check(dead == 0 && hidden == 0, $"새 Day 마을: 시체나 감춰진 늑대 없이 모두 살아서 나온다 (늑대 {SpawnedWolves().Count}마리, 시체 {dead}, 감춤 {hidden})");

        Kill(SpawnedWolves().First());
        yield return WaitFrames(2);
        yield return Load("TruckScene");
        bool recordedBeforeReset = SpawnedIdsRecorded(gm);
        gm.ResetGame();
        Check(recordedBeforeReset && !SpawnedIdsRecorded(gm), "새 게임(ResetGame)이면 시체 자리 기록이 비워진다");
    }

    // 흐름 1에서 납품한 늑대 Id - 흐름 5가 Day 변경 뒤 구조 기록이 비워졌는지 볼 때 쓴다.
    private static string DeliveredIdForFlow5;
    private static readonly List<string> lastVillageIds = new List<string>();

    private static bool SpawnedIdsRecorded(GameManager gm) => lastVillageIds.Any(i => gm.TryGetMonsterCorpse(i, out _));

    // ================= 도구 =================

    // 트럭을 거쳐(첫 진입은 바로) 마을에 들어가고, 대상 늑대 말고 몬스터와 동물을 끈다.
    private static IEnumerator EnterVillage(string keepId)
    {
        if (SceneManager.GetActiveScene().name == "VillageScene") yield return Load("TruckScene");
        yield return Load("VillageScene");
        DisableOthers(keepId);
        lastVillageIds.Clear();
        lastVillageIds.AddRange(SpawnedWolves().Select(CorpseId));
        yield return WaitFrames(2);
    }

    private static void DisableOthers(string keepId)
    {
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            MonsterCorpsePickup corpse = m.GetComponent<MonsterCorpsePickup>();
            if (corpse != null && keepId != null && CorpseId(corpse) == keepId) continue;
            if (corpse != null && keepId == null) continue; // 대상을 아직 안 정했으면 늑대는 남긴다(소리반응형만 끈다)
            m.gameObject.SetActive(false);
        }
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
    }

    private static List<MonsterCorpsePickup> SpawnedWolves()
    {
        GameObject root = GameObject.Find("GeneratedMap");
        return root != null ? root.GetComponentsInChildren<MonsterCorpsePickup>(true).ToList() : new List<MonsterCorpsePickup>();
    }

    private static MonsterCorpsePickup FindSpawned(string id) => SpawnedWolves().FirstOrDefault(w => CorpseId(w) == id);

    private static IEnumerable<MonsterCorpsePickup> ActiveCorpses(string id) =>
        Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Exclude).Where(c => CorpseId(c) == id);

    private static int ActiveWithId(string id) => ActiveCorpses(id).Count();

    private static int SpawnedAliveWithId(string id) => ActiveCorpses(id).Count(c => !Health(c).IsDead);

    // Start 전이면 아직 id가 비어 있으므로 계층 경로(Start가 쓰는 것과 같은 규칙)로 대신한다.
    private static string CorpseId(MonsterCorpsePickup c)
    {
        string id = CorpseIdField.GetValue(c) as string;
        if (!string.IsNullOrEmpty(id)) return id;
        var path = new StringBuilder(c.name);
        for (Transform t = c.transform.parent; t != null; t = t.parent) path.Insert(0, "/").Insert(0, t.name);
        return path.ToString();
    }

    private static MonsterHealth Health(MonsterCorpsePickup c) => c.GetComponent<MonsterHealth>();

    private static void Kill(MonsterCorpsePickup wolf)
    {
        MonsterHealth h = Health(wolf);
        h.TakeDamage(h.Health + 1f);
    }

    private static IEnumerator PickUp(GameManager gm, MonsterCorpsePickup corpse, string id)
    {
        if (corpse == null) { Check(false, "주울 시체가 없다"); yield break; }
        Teleport(Player(), NavToXY(corpse.transform.position) + Vector2.left * NearCorpse);
        yield return WaitFrames(4); // InteractionFocus는 직전 프레임의 보고로 정한다
        yield return Press(Key.E);
        if (HeldSlot(gm, id) < 0) Check(false, $"E로 시체를 줍지 못했다 ({id})");
    }

    private static IEnumerator Drop(GameManager gm, string id, System.Action<MonsterCorpsePickup> onDropped)
    {
        int slot = HeldSlot(gm, id);
        if (slot < 0) { Check(false, "버릴 시체가 인벤토리에 없다"); yield break; }
        HashSet<MonsterCorpsePickup> before = new HashSet<MonsterCorpsePickup>(Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Include));
        gm.SelectSlot(slot);
        yield return Press(Key.G);
        yield return WaitFrames(2);
        onDropped(Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Include).FirstOrDefault(c => !before.Contains(c)));
    }

    private static void Deliver(GameManager gm, string id)
    {
        int slot = HeldSlot(gm, id);
        if (slot < 0) { Check(false, "납품할 시체가 인벤토리에 없다"); return; }
        gm.SelectSlot(slot);
        gm.TryDeliverSelectedAnimal();
        DeliveredIdForFlow5 ??= id;
    }

    private static int HeldSlot(GameManager gm, string id)
    {
        for (int i = 0; i < gm.InventorySlotCount; i++)
        {
            InventorySlotData s = gm.GetSlot(i);
            if (s.Type == InventoryItemType.Animal && s.AnimalKind == AnimalBehaviorKind.MonsterCorpse && s.AnimalId == id) return i;
        }
        return -1;
    }

    private static Sprite[] DeathFrames(MonsterCorpsePickup c)
    {
        SimpleFrameAnimator anim = c.GetComponentInChildren<SimpleFrameAnimator>(true);
        return anim != null ? (DeathFramesField.GetValue(anim) as Sprite[]) ?? new Sprite[0] : new Sprite[0];
    }

    private static Sprite VisualSprite(MonsterCorpsePickup c)
    {
        SpriteRenderer r = c.GetComponentInChildren<SimpleFrameAnimator>(true)?.GetComponent<SpriteRenderer>();
        return r != null ? r.sprite : null;
    }

    private static Vector2 VisualXY(MonsterCorpsePickup c)
    {
        Transform v = c.GetComponentInChildren<SimpleFrameAnimator>(true)?.transform;
        return v != null ? (Vector2)v.position : new Vector2(float.NaN, float.NaN);
    }

    // 사망 애니메이션을 재생하지 않고 마지막 프레임(쓰러진 모습)으로 바로 보이는지 - 시체가 생긴 직후 몇 프레임 동안 계속 마지막 프레임이어야 한다.
    private static void CheckDeadPose(MonsterCorpsePickup c, string what)
    {
        if (c == null) { Check(false, $"{what}: 시체가 없다"); return; }
        Sprite[] frames = DeathFrames(c);
        Sprite shown = VisualSprite(c);
        Check(frames.Length > 0 && shown == frames[frames.Length - 1],
            $"{what}: 사망 애니메이션 없이 쓰러진 마지막 모습으로 보인다 ({SpriteName(shown)}, 마지막 {SpriteName(frames.LastOrDefault())})");
    }

    private static string SpriteName(Sprite s) => s != null ? s.name : "없음";

    // 주위 1칸 안에 벽이 없고 원래 자리에서 minDistance 이상 떨어진, 늑대가 서 있던 방 바닥(NavMesh) 위의 자리.
    private static Vector2 FindOpenSpotAwayFrom(Vector2 origin, float minDistance)
    {
        for (int ring = 0; ring < 40; ring++)
        {
            for (int k = 0; k < 16; k++)
            {
                float angle = k * Mathf.PI * 2f / 16f;
                Vector2 p = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (minDistance + ring * 0.5f);
                if (!NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out NavMeshHit hit, 0.5f, NavMesh.AllAreas) ||
                    Vector2.Distance(NavToXY(hit.position), p) > 0.05f) continue;
                if (Physics2D.OverlapCircleAll(p, 1.6f).Any(GridMapAuditInspector.IsBlocker)) continue;
                return p;
            }
        }
        throw new System.Exception($"({origin.x:0.0},{origin.y:0.0}) 근처에서 막힌 데 없는 자리를 찾지 못했다");
    }

    private static Vector2 NavToXY(Vector3 nav) => new Vector2(nav.x, nav.z);

    private static float Nav2D(Vector3 a, Vector3 b) => Vector2.Distance(NavToXY(a), NavToXY(b));

    private static GameObject Player() => GameObject.FindGameObjectWithTag("Player");

    private static void Teleport(GameObject player, Vector2 position)
    {
        player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = position;
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static string Subtitle()
    {
        GameObject go = GameObject.Find("SubtitleManager");
        TextMeshProUGUI label = go != null ? go.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        return label != null ? label.text : "(자막 없음)";
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
