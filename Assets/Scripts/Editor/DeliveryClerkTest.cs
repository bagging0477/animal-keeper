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
using TMPro;

/// <summary>
/// Tools > Truck > Run Delivery Clerk Test. 트럭 씬 납품 직원(DeliveryClerk, 막내 수의사)을 실제 씬 흐름(타이틀 → 클래스 선택 → 트럭)으로 확인한다.
/// 검사: 구조(위치 (2, -1)·범위 2.3·초록 네모 안 씀·그림 자식·정렬·충돌체), 맡길 것이 없으면 안내 없음, 있으면 "E를 눌러 모두 맡기기 (N마리)",
/// E 한 번에 동물·시체를 모두 맡김(같은 묶음을 한 마리씩 맡긴 것과 구조 수·무게·마일리지가 같음, 지뢰는 남음, 칸 선택 무관)과 받기 동작 한 번 재생 후 대기로 돌아옴,
/// 플레이어가 왼쪽·아래·위에서 걸어 들어가도 수의사를 통과하지 못함(막힌 자리에서도 E가 됨), 발 기준 앞뒤 정렬,
/// 마을 목표 표시("오늘 목표  N/M")가 맡길 때마다 갱신됨, 소파: 목표 미달이면 확인 창 → [취소]·ESC는 그대로, [확인]은 게임 오버, 달성이면 창 없이 보호소.
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

        Vector3 standLeft = new Vector3(root.x - 1.2f, root.y - 0.2f, 0f);

        // --- 1. 맡길 것이 없으면 안내 없음
        sb.AppendLine("\n== 1. 안내 문구");
        yield return Teleport(standLeft);
        gm.SelectSlot(0);
        yield return WaitFrames(5);
        string text = Subtitle();
        Check(!HasAnimal() && !IsDeliverPrompt(text),
            $"맡길 것 없음, 범위 안({Vector2.Distance(player.transform.position, root):F2}칸): 자막 \"{text ?? "(없음)"}\"");

        // --- 2. 계산 비교: 같은 동물 묶음을 예전 방식(칸 골라 한 마리씩)과 새 방식(E 한 번에 모두)으로 맡겨 증가량을 비교한다
        sb.AppendLine("\n== 2. 한 번에 모두 맡기기 - 계산이 한 마리씩 맡긴 것과 같음, 도구는 남음, 받기 동작 1회");
        gm.SelectSlot(2);
        bool bombUsed = gm.TryUseSelectedBomb(); // 시작 지급 폭탄 칸을 비워 동물 2 + 시체 1이 들어갈 자리를 만든다(지뢰 2개는 남는다)
        (string id, int weight, bool corpse)[] batch = { ("cat", 6, false), ("koala", 9, false), ("wolf", 15, true) };
        void AddBatch(string prefix)
        {
            foreach (var a in batch)
            {
                if (a.corpse) gm.TryAddMonsterCorpse($"{prefix}/{a.id}", a.weight, null, null);
                else gm.TryAddAnimal($"{prefix}/{a.id}", a.weight, AnimalBehaviorKind.Flee, null);
            }
        }
        string Tools() => string.Join(",", Enumerable.Range(0, gm.InventorySlotCount).Select(i => gm.GetSlot(i).Type).Where(t => t == InventoryItemType.Mine || t == InventoryItemType.Bomb));

        // 예전 방식: 칸을 골라 TryDeliverSelectedAnimal을 한 마리씩
        AddBatch("ClerkTest/one");
        int r0 = gm.RescuedCount, w0 = gm.TotalWeight;
        for (int i = 0; i < gm.InventorySlotCount; i++)
            if (gm.GetSlot(i).Type == InventoryItemType.Animal) { gm.SelectSlot(i); gm.TryDeliverSelectedAnimal(); }
        int oneByOneCount = gm.RescuedCount - r0, oneByOneWeight = gm.TotalWeight - w0;
        Check(bombUsed && oneByOneCount == 3 && oneByOneWeight == 30, $"예전 방식(한 마리씩 3번): 구조 +{oneByOneCount}, 무게 +{oneByOneWeight}kg");

        // 새 방식: 같은 묶음, 지뢰 칸을 고른 채로 E 한 번
        AddBatch("ClerkTest/all");
        gm.SelectSlot(0);
        string toolsBefore = Tools();
        yield return WaitFrames(5);
        text = Subtitle();
        Check(text == AnimalDeliveryPoint.DeliverPrompt(3) && gm.CarriedAnimalCount == 3,
            $"동물 2 + 시체 1, 고른 칸 1번({gm.GetSlot(0).Type}): 자막 \"{text}\" (기대 \"{AnimalDeliveryPoint.DeliverPrompt(3)}\")");
        int r1 = gm.RescuedCount, w1 = gm.TotalWeight, takes = clerk.TakePlayCount;
        yield return Press(Key.E);
        int allCount = gm.RescuedCount - r1, allWeight = gm.TotalWeight - w1;
        Check(allCount == oneByOneCount && allWeight == oneByOneWeight && gm.CarriedAnimalCount == 0,
            $"E 한 번: 구조 +{allCount}, 무게 +{allWeight}kg (한 마리씩 +{oneByOneCount}, +{oneByOneWeight}kg), 남은 동물 {gm.CarriedAnimalCount}");
        int mileagePerKg = gm.Config != null ? gm.Config.mileagePerWeight : 10;
        Check(allWeight * mileagePerKg == oneByOneWeight * mileagePerKg,
            $"정산 마일리지(무게 x {mileagePerKg}) {allWeight * mileagePerKg} = 한 마리씩 {oneByOneWeight * mileagePerKg}");
        Check(batch.All(a => gm.IsAnimalRescuedToday($"ClerkTest/all/{a.id}") && gm.IsAnimalRescuedToday($"ClerkTest/one/{a.id}")),
            "두 방식 모두 오늘 구조 기록(Id)에 남음 - 마을 재진입 때 다시 나오지 않는다");
        Check(Tools() == toolsBefore && toolsBefore == "Mine,Mine" && gm.SelectedSlotIndex == 0,
            $"도구는 그대로: [{Tools()}], 고른 칸 {gm.SelectedSlotIndex + 1}번 그대로");
        Check(clerk.TakePlayCount == takes + 1 && clerk.IsPlayingTake, $"받기 동작 시작 (재생 {takes} → {clerk.TakePlayCount}회, 재생 중 {clerk.IsPlayingTake})");
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
        Check(clerk.TakePlayCount == takes + 1 && idleSeen.SetEquals(new[] { "vet_idle_1", "vet_idle_2", "vet_idle_3" }),
            $"그 뒤 3.3초: 3마리를 맡겨도 받기는 한 번(재생 {clerk.TakePlayCount - takes}회), 대기 그림 [{string.Join(", ", idleSeen.OrderBy(x => x))}] 반복");
        // 범위 안 안내는 마지막 호출 뒤 실제 시간 WhileInRange(0.15초)가 지나야 사라진다 - batch는 프레임이 빨라 실제 시간으로 기다린다.
        yield return WaitRealtime(SubtitleManager.WhileInRange + 0.25f);
        text = Subtitle();
        Check(!IsDeliverPrompt(text), $"맡긴 뒤 맡길 것 없음: 자막 \"{text ?? "(없음)"}\"");
        int takesBeforeEmptyE = clerk.TakePlayCount;
        yield return Press(Key.E);
        Check(clerk.TakePlayCount == takesBeforeEmptyE, "맡길 것이 없을 때 E: 받기 동작 없음");

        // --- 3. 통과하지 못함
        sb.AppendLine("\n== 3. 플레이어가 수의사를 통과하지 못함 (1.5초씩 걸어 들어감)");
        int takesBeforeWalls = clerk.TakePlayCount;
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
            int rw = gm.RescuedCount;
            yield return Press(Key.E);
            Check(gm.RescuedCount == rw + 1, $"{from}: 막힌 자리에서 E로 맡기기 ({Vector2.Distance(p, root):F2}칸, 구조 {rw} → {gm.RescuedCount})");
            // 발 기준 앞뒤: 플레이어 발이 수의사 발보다 아래면 플레이어가 앞(정렬점 Y가 작음)
            float playerFoot = p.y - PlayerFootOffset;
            bool playerInFront = p.y < sortY;
            Check(playerInFront == (playerFoot < clerkFoot), $"{from}: 발 플레이어 {playerFoot:F2} / 수의사 {clerkFoot:F2} → {(playerInFront ? "플레이어가 앞" : "수의사가 앞")}");
        }
        Check(clerk.TakePlayCount == takesBeforeWalls + 3, $"맡길 때마다 받기 동작 한 번 (세 번 맡김: {takesBeforeWalls} → {clerk.TakePlayCount}회)");

        // --- 4. 마을 목표 표시
        sb.AppendLine("\n== 4. 마을 목표 표시 (HUD 체력·스태미나 막대 아래 \"오늘 목표  N/M\")");
        yield return NewGameInTruck();
        yield return LoadScene("VillageScene");
        VillageObjectiveChip chip = Object.FindAnyObjectByType<VillageObjectiveChip>();
        yield return WaitFrames(3);
        Check(chip != null && chip.isActiveAndEnabled && chip.CurrentValue == VillageObjectiveChip.ValueText(0, gm.TargetCount),
            $"새 게임 마을: \"{chip?.CurrentValue}\" (기대 \"{VillageObjectiveChip.ValueText(0, gm.TargetCount)}\")");
        if (chip != null)
        {
            RectTransform chipRect = (RectTransform)chip.transform;
            RectTransform hudRect = Object.FindAnyObjectByType<AnimalKeeperHUD>()?.GetComponent<RectTransform>();
            Check(chipRect.anchorMin == new Vector2(0f, 1f) && chipRect.anchoredPosition.y < 0f && chipRect.parent == hudRect?.parent,
                $"왼쪽 위 기준, HUD와 같은 Canvas({chipRect.parent?.name}), 위치 {chipRect.anchoredPosition}, 크기 {chipRect.sizeDelta}");
            TMP_Text[] texts = chip.GetComponentsInChildren<TMP_Text>();
            float used = texts.Sum(t => t.preferredWidth) + 42f; // 양쪽 여백 21씩
            sb.AppendLine($"  (참고) 글자 폭 {string.Join(" + ", texts.Select(t => $"{t.text} {t.preferredWidth:F0}"))} + 여백 42 = {used:F0} / 칩 폭 {chipRect.sizeDelta.x:F0} (빈 폭 {chipRect.sizeDelta.x - used:F0})");
        }
        gm.AddCargo(4); // 마을 안에서 값이 바뀌어도 다음 프레임에 갱신된다
        yield return WaitFrames(2);
        Check(chip != null && chip.CurrentValue == VillageObjectiveChip.ValueText(1, gm.TargetCount), $"맡긴 수가 바뀐 다음 프레임: \"{chip?.CurrentValue}\"");
        yield return LoadScene("TruckScene");
        gm.TryAddAnimal("ClerkTest/village/cat", 5, AnimalBehaviorKind.Flee, null);
        gm.TryAddAnimal("ClerkTest/village/koala", 7, AnimalBehaviorKind.Wander, null);
        AnimalDeliveryPoint point2 = Object.FindAnyObjectByType<AnimalDeliveryPoint>();
        yield return Teleport(point2.transform.position + new Vector3(-1.2f, -0.2f, 0f));
        yield return Press(Key.E);
        yield return LoadScene("VillageScene");
        chip = Object.FindAnyObjectByType<VillageObjectiveChip>();
        yield return WaitFrames(3);
        Check(chip != null && chip.CurrentValue == VillageObjectiveChip.ValueText(3, gm.TargetCount) && gm.RescuedCount == 3,
            $"트럭에서 두 마리를 한 번에 맡기고 돌아온 마을: \"{chip?.CurrentValue}\" (구조 {gm.RescuedCount})");

        // --- 5. 소파 확인 창
        sb.AppendLine("\n== 5. 소파: 목표 미달이면 확인 창, 취소하면 그대로, 확인하면 게임 오버");
        yield return NewGameInTruck();
        TruckHubPoint hub = Object.FindAnyObjectByType<TruckHubPoint>();
        Button nextDay = HubField<Button>(hub, "nextDayButton");
        Button ok = HubField<Button>(hub, "confirmButton");
        Button cancel = HubField<Button>(hub, "cancelButton");
        GameObject confirmPanel = HubField<GameObject>(hub, "confirmPanel");
        TMP_Text confirmMessage = HubField<TMP_Text>(hub, "confirmMessage");
        Check(nextDay != null && ok != null && cancel != null && confirmPanel != null && confirmMessage != null && !confirmPanel.activeSelf,
            "확인 창·[확인]·[취소]가 연결되어 있고 처음엔 꺼져 있다");
        int cycle = gm.CycleCount;

        // 5-1. 미달 → 확인 창 → [취소]
        yield return OpenSofaMenu(hub);
        Check(hub.IsMenuOpen && Time.timeScale == 0f, $"소파에서 E: 메뉴 열림 (timeScale {Time.timeScale})");
        nextDay.onClick.Invoke();
        yield return WaitFrames(2);
        int remaining = gm.TargetCount - gm.RescuedCount;
        Check(hub.IsConfirmOpen && confirmPanel.activeInHierarchy && confirmMessage.text == TruckHubPoint.ConfirmText(remaining) && !gm.IsGameOver && Time.timeScale == 0f,
            $"구조 {gm.RescuedCount}/{gm.TargetCount}에서 [다음 날로]: 확인 창 \"{confirmMessage.text}\", 게임 오버 {gm.IsGameOver}");
        cancel.onClick.Invoke();
        yield return WaitFrames(3);
        Check(!hub.IsConfirmOpen && !hub.IsMenuOpen && !confirmPanel.activeSelf && Time.timeScale == 1f && !gm.IsGameOver && gm.CycleCount == cycle &&
              SceneManager.GetActiveScene().name == "TruckScene",
            $"[취소]: 창·메뉴 닫힘, 트럭 그대로 (timeScale {Time.timeScale}, 게임 오버 {gm.IsGameOver}, Day {gm.CycleCount})");

        // 5-2. 미달 → 확인 창 → ESC도 취소
        yield return OpenSofaMenu(hub);
        nextDay.onClick.Invoke();
        yield return WaitFrames(2);
        yield return Press(Key.Escape);
        Check(!hub.IsConfirmOpen && !hub.IsMenuOpen && Time.timeScale == 1f && !gm.IsGameOver, "확인 창에서 ESC: 취소와 같음");

        // 5-3. 미달 → 확인 창 → [확인] → 게임 오버
        yield return OpenSofaMenu(hub);
        nextDay.onClick.Invoke();
        yield return WaitFrames(2);
        ok.onClick.Invoke();
        yield return WaitFrames(3);
        GameOverPanel over = Object.FindAnyObjectByType<GameOverPanel>(FindObjectsInactive.Include);
        Check(!hub.IsConfirmOpen && gm.IsGameOver && over != null && over.gameObject.activeInHierarchy,
            $"[확인]: 게임 오버 {gm.IsGameOver}, 게임 오버 화면 {over?.gameObject.activeInHierarchy}");
        Time.timeScale = 1f;

        // 5-4. 목표 달성이면 확인 창 없이 보호소로
        yield return NewGameInTruck();
        hub = Object.FindAnyObjectByType<TruckHubPoint>();
        for (int i = 0; i < gm.TargetCount; i++) gm.AddCargo(5);
        yield return OpenSofaMenu(hub);
        HubField<Button>(hub, "nextDayButton").onClick.Invoke();
        yield return WaitForScene("ShelterScene");
        Check(SceneManager.GetActiveScene().name == "ShelterScene" && !gm.IsGameOver, $"구조 {gm.RescuedCount}/{gm.TargetCount}에서 [다음 날로]: 확인 창 없이 {SceneManager.GetActiveScene().name}");
        Time.timeScale = 1f;
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

    private static bool IsDeliverPrompt(string text) => text != null && text.StartsWith("E를 눌러 모두 맡기기");

    private static T HubField<T>(TruckHubPoint hub, string name) where T : Object =>
        hub == null ? null : typeof(TruckHubPoint).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hub) as T;

    private static IEnumerator LoadScene(string name)
    {
        SceneManager.LoadScene(name);
        yield return WaitForScene(name);
        player = GameObject.FindGameObjectWithTag("Player");
    }

    // 트럭에서 새 게임을 시작한다(시작 클래스 선택은 스카우트로 끝낸 것으로 하고 트럭을 다시 불러온다).
    private static IEnumerator NewGameInTruck()
    {
        Time.timeScale = 1f;
        if (SceneManager.GetActiveScene().name != "TruckScene") yield return LoadScene("TruckScene");
        gm.ResetGame();
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        yield return LoadScene("TruckScene");
    }

    private static IEnumerator OpenSofaMenu(TruckHubPoint hub)
    {
        yield return Teleport(hub.transform.position + new Vector3(0f, -0.5f, 0f));
        yield return Press(Key.E);
        yield return WaitFrames(2);
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
