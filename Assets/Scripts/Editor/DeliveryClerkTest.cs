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

/// <summary>
/// Tools > Truck > Run Delivery Clerk Test. 트럭 씬 납품 직원(DeliveryClerk, 막내 수의사)을 실제 씬 흐름(타이틀 → 클래스 선택 → 트럭)으로 확인한다.
/// 검사: 구조(위치 (2, -1)·범위 2.3·초록 네모 안 씀·그림 자식·정렬·충돌체), 동물이 없으면 안내 없음, 동물은 있는데 다른 칸이면 "숫자키로 동물 칸을 골라 주세요",
/// 동물 칸을 고르면 "E를 눌러 맡기기", 다른 칸에서 E를 눌러도 납품 안 됨, 동물 칸에서 E → 납품(구조 +1, 칸 비움)과 받기 동작 한 번 재생 후 대기로 돌아옴,
/// 플레이어가 왼쪽·아래·위에서 걸어 들어가도 수의사를 통과하지 못함(막힌 자리에서도 E가 됨), 발 기준 앞뒤 정렬.
/// 키는 가상 키보드로 누른다. 프레임 시간 1/30초 고정. 결과: Logs/DeliveryClerkTest.txt / batchmode: -executeMethod DeliveryClerkTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class DeliveryClerkTest
{
    private const string KeyActive = "DCT.Active", KeyDone = "DCT.Done", KeyBatch = "DCT.Batch", KeyPassed = "DCT.Passed";
    private const string ReportPath = "Logs/DeliveryClerkTest.txt";
    private const float FrameTime = 1f / 30f;
    private const float PlayerFootOffset = 0.41f; // 플레이어 발 = 몸 가운데 - 0.41칸 (VillageMapGenerator.PlayerFootWorldOffset)

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static readonly List<string> logs = new List<string>();
    private static Keyboard vkb;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static GameManager gm;
    private static GameObject player;

    static DeliveryClerkTest() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

    [MenuItem("Tools/Truck/Run Delivery Clerk Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[DeliveryClerkTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            Time.captureDeltaTime = FrameTime;
            // 에디터는 Game 뷰에 포커스가 없으면 키 입력을 게임에 보내지 않는다 - 테스트 동안만 모든 입력이 게임으로 가게 한다.
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("DeliveryClerkTestKeyboard");
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
        Application.logMessageReceived -= OnLog;
        Time.captureDeltaTime = 0f;
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        vkb = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || type == LogType.Warning) return;
        logs.Add($"{type}: {condition.Split('\n')[0]}");
    }

    private static void Step()
    {
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDown(); return; }
        bool more = false;
        try
        {
            if (nested.Count == 0) nested.Push(routine);
            while (nested.Count > 0)
            {
                IEnumerator top = nested.Peek();
                if (!top.MoveNext()) { nested.Pop(); continue; }
                if (top.Current is IEnumerator inner) { nested.Push(inner); continue; }
                more = true;
                break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            sb?.AppendLine("\n중단됨: " + e);
            failures?.Add("예외로 중단됨: " + e.Message);
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            Finish();
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
        Debug.Log("[DeliveryClerkTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"DeliveryClerkTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);
        gm = GameManager.Instance;
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        SceneManager.LoadScene("TruckScene");
        yield return WaitForScene("TruckScene");

        AnimalDeliveryPoint point = Object.FindAnyObjectByType<AnimalDeliveryPoint>();
        player = GameObject.FindGameObjectWithTag("Player");
        DeliveryClerkAnimator clerk = point != null ? point.GetComponentInChildren<DeliveryClerkAnimator>() : null;
        SpriteRenderer clerkRenderer = clerk != null ? clerk.GetComponent<SpriteRenderer>() : null;
        BoxCollider2D blocker = point != null ? point.GetComponentInChildren<BoxCollider2D>() : null;
        SpriteRenderer playerRenderer = player != null ? player.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(r => r.enabled && r.sprite != null) : null;
        if (point == null || player == null || clerk == null || blocker == null || playerRenderer == null)
        {
            Check(false, $"준비 실패: 납품 지점 {point != null}, 플레이어 {player != null}, 직원 그림 {clerk != null}, 충돌체 {blocker != null}");
            yield break;
        }
        Vector2 root = point.transform.position;
        float range = (float)typeof(AnimalDeliveryPoint).GetField("interactionRange", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(point);

        // --- 0. 구조
        sb.AppendLine("\n== 0. 구조");
        Check(point.name == "DeliveryClerk" && root == new Vector2(2f, -1f) && Mathf.Approximately(range, 2.3f),
            $"이름 {point.name}, 납품 위치 {root}, 상호작용 범위 {range} (기대 DeliveryClerk, (2, -1), 2.3)");
        Check(point.GetComponent<SpriteRenderer>() == null && Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include).All(r => r.sprite == null || r.sprite.name != "RescuePointSquare"),
            "초록 네모(RescuePointSquare)를 그리는 렌더러가 없다");
        Check(clerk.transform.parent == point.transform && clerkRenderer.sprite != null && clerkRenderer.sprite.name.StartsWith("vet_") &&
              Mathf.Approximately(clerkRenderer.sprite.pixelsPerUnit, 16f) && clerkRenderer.sprite.texture.filterMode == FilterMode.Point,
            $"그림은 자식 {clerk.name}, {clerkRenderer.sprite?.name}, PPU {clerkRenderer.sprite?.pixelsPerUnit}, {clerkRenderer.sprite?.texture.filterMode}");
        Check(clerkRenderer.sortingLayerID == playerRenderer.sortingLayerID && clerkRenderer.sortingOrder == playerRenderer.sortingOrder && clerkRenderer.spriteSortPoint == SpriteSortPoint.Pivot,
            $"정렬: 플레이어와 같은 레이어·Order({clerkRenderer.sortingOrder} = {playerRenderer.sortingOrder}), 정렬점 {clerkRenderer.spriteSortPoint}");
        float clerkFoot = clerkRenderer.bounds.min.y + 1f / 16f; // 그림 아래 1텍셀은 비어 있다
        float sortY = clerk.transform.position.y;
        Check(Mathf.Abs(sortY - clerkFoot - PlayerFootOffset) < 0.01f,
            $"정렬점(피벗) {sortY:F3} = 발 {clerkFoot:F3} + {PlayerFootOffset} - 플레이어(몸 가운데로 정렬)와 발 높이로 비교된다");
        Check(!blocker.isTrigger && blocker.enabled, $"충돌체 {blocker.size.x:F2}x{blocker.size.y:F2}칸, 아래 끝 {blocker.bounds.min.y:F2} (발 {clerkFoot:F2}), 트리거 아님");

        // 동물 칸을 모두 비운다(시작 지급 지뢰·폭탄은 그대로)
        Vector3 standLeft = new Vector3(root.x - 1.2f, root.y - 0.2f, 0f);

        // --- 1. 동물이 없으면 안내 없음
        sb.AppendLine("\n== 1. 안내 문구");
        yield return Teleport(standLeft);
        gm.SelectSlot(0);
        yield return WaitFrames(5);
        string text = Subtitle();
        Check(!HasAnimal() && text != AnimalDeliveryPoint.DeliverPrompt && text != AnimalDeliveryPoint.ChooseSlotPrompt,
            $"동물 없음, 범위 안({Vector2.Distance(player.transform.position, root):F2}칸): 자막 \"{text ?? "(없음)"}\"");

        // --- 2. 동물이 있는데 다른 칸
        bool added = gm.TryAddAnimal("DeliveryClerkTest/cat", 6, AnimalBehaviorKind.Flee, null);
        int animalSlot = Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Animal);
        yield return WaitFrames(5);
        text = Subtitle();
        Check(added && gm.SelectedSlotIndex != animalSlot && text == AnimalDeliveryPoint.ChooseSlotPrompt,
            $"동물은 {animalSlot + 1}번 칸, 고른 칸 {gm.SelectedSlotIndex + 1}번({gm.GetSlot(gm.SelectedSlotIndex).Type}): 자막 \"{text}\"");
        int rescued = gm.RescuedCount;
        yield return Press(Key.E);
        Check(gm.RescuedCount == rescued && gm.GetSlot(animalSlot).Type == InventoryItemType.Animal && clerk.TakePlayCount == 0,
            $"다른 칸에서 E: 납품 안 됨 (구조 {rescued} → {gm.RescuedCount}, 받기 {clerk.TakePlayCount}회)");

        // --- 3. 숫자키로 동물 칸을 고르면
        yield return Press(Key.Digit1 + animalSlot);
        yield return WaitFrames(3);
        text = Subtitle();
        Check(gm.SelectedSlotIndex == animalSlot && text == AnimalDeliveryPoint.DeliverPrompt, $"{animalSlot + 1}번 키로 동물 칸 선택: 자막 \"{text}\"");

        // --- 4. E로 맡기기 + 받기 동작 한 번
        sb.AppendLine("\n== 2. 맡기기와 받기 동작");
        int weightBefore = gm.TotalWeight;
        yield return Press(Key.E);
        Check(gm.RescuedCount == rescued + 1 && gm.GetSlot(animalSlot).Type == InventoryItemType.None,
            $"E: 납품됨 (구조 {rescued} → {gm.RescuedCount}, {animalSlot + 1}번 칸 {gm.GetSlot(animalSlot).Type}, 적재 무게 {weightBefore} → {gm.TotalWeight})");
        Check(clerk.TakePlayCount == 1 && clerk.IsPlayingTake, $"받기 동작 시작 (재생 {clerk.TakePlayCount}회, 재생 중 {clerk.IsPlayingTake})");
        List<string> seen = new List<string>();
        for (int i = 0; i < 60 && clerk.IsPlayingTake; i++)
        {
            string n = clerk.CurrentSprite?.name;
            if (seen.Count == 0 || seen[seen.Count - 1] != n) seen.Add(n);
            yield return WaitFrames(1);
        }
        string[] want = clerk.TakeFrames.Select(s => s.name).Distinct().ToArray();
        Check(seen.SequenceEqual(want), $"받기 그림 순서 [{string.Join(", ", seen)}] (기대 [{string.Join(", ", want)}])");
        yield return WaitFrames(3);
        Check(!clerk.IsPlayingTake && clerk.CurrentSprite != null && clerk.CurrentSprite.name.StartsWith("vet_idle"),
            $"끝나면 대기로 돌아감 ({clerk.CurrentSprite?.name})");
        HashSet<string> idleSeen = new HashSet<string>();
        for (int i = 0; i < 100; i++) { idleSeen.Add(clerk.CurrentSprite?.name); yield return WaitFrames(1); }
        Check(clerk.TakePlayCount == 1 && idleSeen.SetEquals(new[] { "vet_idle_1", "vet_idle_2", "vet_idle_3" }),
            $"그 뒤 3.3초: 받기 다시 안 함(재생 {clerk.TakePlayCount}회), 대기 그림 [{string.Join(", ", idleSeen.OrderBy(x => x))}] 반복");
        // 범위 안 안내는 마지막 호출 뒤 실제 시간 WhileInRange(0.15초)가 지나야 사라진다 - batch는 프레임이 빨라 실제 시간으로 기다린다.
        yield return WaitRealtime(SubtitleManager.WhileInRange + 0.25f);
        text = Subtitle();
        Check(text != AnimalDeliveryPoint.DeliverPrompt && text != AnimalDeliveryPoint.ChooseSlotPrompt, $"맡긴 뒤 동물 없음: 자막 \"{text ?? "(없음)"}\"");

        // --- 5. 통과하지 못함
        sb.AppendLine("\n== 3. 플레이어가 수의사를 통과하지 못함 (1.5초씩 걸어 들어감)");
        Bounds b = blocker.bounds;
        CircleCollider2D pc = player.GetComponent<CircleCollider2D>();
        float pr = pc.radius * Mathf.Abs(player.transform.lossyScale.x);
        foreach ((string from, Vector3 start, Key key, System.Func<Vector2, bool> blocked) in new (string, Vector3, Key, System.Func<Vector2, bool>)[]
        {
            ("왼쪽에서 D", new Vector3(b.min.x - pr - 1.5f, b.center.y, 0f), Key.D, p => p.x <= b.min.x - pr + 0.03f),
            ("아래에서 W", new Vector3(b.center.x, b.min.y - pr - 1.5f, 0f), Key.W, p => p.y <= b.min.y - pr + 0.03f),
            ("위에서 S", new Vector3(b.center.x, b.max.y + pr + 1.0f, 0f), Key.S, p => p.y >= b.max.y + pr - 0.03f),
        })
        {
            yield return Teleport(start);
            yield return Hold(key, 45);
            Vector2 p = player.transform.position;
            Check(blocked(p), $"{from}: 멈춘 자리 ({p.x:F2}, {p.y:F2}), 충돌체 {b.min.x:F2}~{b.max.x:F2} x {b.min.y:F2}~{b.max.y:F2}, 플레이어 반경 {pr:F2}");
            gm.TryAddAnimal($"DeliveryClerkTest/{key}", 3, AnimalBehaviorKind.Wander, null);
            gm.SelectSlot(Enumerable.Range(0, gm.InventorySlotCount).First(i => gm.GetSlot(i).Type == InventoryItemType.Animal));
            int r0 = gm.RescuedCount;
            yield return Press(Key.E);
            Check(gm.RescuedCount == r0 + 1, $"{from}: 막힌 자리에서 E로 맡기기 ({Vector2.Distance(p, root):F2}칸, 구조 {r0} → {gm.RescuedCount})");
            // 발 기준 앞뒤: 플레이어 발이 수의사 발보다 아래면 플레이어가 앞(정렬점 Y가 작음)
            float playerFoot = p.y - PlayerFootOffset;
            bool playerInFront = p.y < sortY;
            Check(playerInFront == (playerFoot < clerkFoot), $"{from}: 발 플레이어 {playerFoot:F2} / 수의사 {clerkFoot:F2} → {(playerInFront ? "플레이어가 앞" : "수의사가 앞")}");
        }
        Check(clerk.TakePlayCount == 4, $"맡길 때마다 받기 동작 (총 {clerk.TakePlayCount}회, 기대 4)");
    }

    // ===================== 도움 함수 =====================

    private static readonly FieldInfo SubtitleInstance = typeof(SubtitleManager).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo SubtitlePending = typeof(SubtitleManager).GetField("pendingText", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SubtitleCurrent = typeof(SubtitleManager).GetField("currentText", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SubtitleHideAt = typeof(SubtitleManager).GetField("hideAt", BindingFlags.NonPublic | BindingFlags.Instance);

    // 지금 보이거나 곧 보일 자막(사라지는 중이면 null).
    private static string Subtitle()
    {
        if (!(SubtitleInstance?.GetValue(null) is SubtitleManager m) || m == null) return null;
        string pending = SubtitlePending.GetValue(m) as string;
        if (pending != null) return pending;
        float hideAt = (float)SubtitleHideAt.GetValue(m);
        return Time.unscaledTime < hideAt ? SubtitleCurrent.GetValue(m) as string : null;
    }

    private static bool HasAnimal() => Enumerable.Range(0, gm.InventorySlotCount).Any(i => gm.GetSlot(i).Type == InventoryItemType.Animal);

    private static IEnumerator Teleport(Vector3 at)
    {
        at.z = player.transform.position.z;
        player.transform.position = at;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
        yield return WaitFrames(3);
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static IEnumerator Hold(Key key, int frames)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(frames);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(3);
    }

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 20f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitForScene(string name)
    {
        float until = Time.realtimeSinceStartup + 30f;
        while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(10);
    }
}
