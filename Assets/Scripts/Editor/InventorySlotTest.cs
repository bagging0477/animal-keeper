using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Passives > Run Inventory Slot Test. "인벤토리 칸 +1" 패시브 전후의 인벤토리를 실제 입력 경로(가상 키보드/마우스로
/// 숫자키, E 줍기/납품, G 버리기, 클릭 사용)로 확인한다:
/// 5칸 동작 → 패시브 구매(기존 아이템/선택 유지) → 6칸 UI 배치와 숫자 라벨 → 6칸 가득 참에서 줍기/구매 차단 →
/// 6번째 칸 G 버리기/아이템 사용/동물 납품 → Day 넘김과 씬 왕복 유지 → 게임 오버 재시작 뒤 5칸 →
/// 1920x1080/1280x720에서 인벤토리·자막·무기(Q) 슬롯·모서리 장식 겹침.
/// 테스트 중에는 마을 몬스터를 끄고(플레이어가 쓰러지지 않게) Input System 포커스 설정을 잠시 바꾼 뒤 되돌린다. 씬 파일은 바꾸지 않는다.
/// 결과: Logs/InventorySlotTest.txt (+ Logs/InventorySlotTest_6slots.png)
/// </summary>
[InitializeOnLoad]
public static class InventorySlotTest
{
    private const string KeyActive = "IST.Active", KeyDone = "IST.Done";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private const string ReportPath = "Logs/InventorySlotTest.txt";
    private const string ScreenshotPath = "Logs/InventorySlotTest_6slots.png";
    private const string SlotPassivePath = "Assets/Settings/Passives/ExtraInventorySlot.asset";
    private const string PassiveCatalogPath = "Assets/Settings/Shop/PassiveMerchantCatalog.asset";
    private const string ItemCatalogPath = "Assets/Settings/Shop/ItemMerchantCatalog.asset";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private const string MinePickupPrefabPath = "Assets/Prefabs/DroppedMinePickup.prefab";

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();

    private static Keyboard vkb;
    private static Mouse vmouse;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    static InventorySlotTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Passives/Run Inventory Slot Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[InventorySlotTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            SetUpVirtualInput();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
        }
    }

    // 에디터는 Game 뷰에 포커스가 없으면 키 입력을 게임에 보내지 않는다 - 테스트 동안만 모든 입력이 게임으로 가게 한다.
    private static void SetUpVirtualInput()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        vkb = InputSystem.AddDevice<Keyboard>("InventorySlotTestKeyboard");
        vmouse = InputSystem.AddDevice<Mouse>("InventorySlotTestMouse");
        vkb.MakeCurrent();
        vmouse.MakeCurrent();
    }

    private static void TearDownVirtualInput()
    {
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vkb = null;
        vmouse = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith("[InventorySlotTest]")) return;
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
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); TearDownVirtualInput(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(ReportPath, "중단됨: " + e, new UTF8Encoding(false));
            more = false;
        }
        if (!more)
        {
            EditorApplication.update -= Step;
            Application.logMessageReceived -= OnLog;
            TearDownVirtualInput();
            routine = null;
            Time.timeScale = 1f;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static StringBuilder sb;
    private static List<string> failures;

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"InventorySlotTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        PassiveDefinition slotPassive = AssetDatabase.LoadAssetAtPath<PassiveDefinition>(SlotPassivePath);
        ShopCatalog passiveCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(PassiveCatalogPath);
        ShopCatalog itemCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ItemCatalogPath);
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        GadgetPickup minePickupPrefab = AssetDatabase.LoadAssetAtPath<GadgetPickup>(MinePickupPrefabPath);
        ShopCatalogEntry mineEntry = itemCatalog.entries.First(e => e.kind == ShopItemKind.Mine);
        config.TryGetPassivePrice(slotPassive, out GameBalanceConfig.PassivePrice slotPrice);
        sb.AppendLine($"패시브: {slotPassive.DisplayName}, 가격 {slotPrice?.price}, 최대 {slotPrice?.maxPurchases}회, {slotPassive.effectKind} +{slotPassive.effectValue}, 기본 칸 {config.inventoryBaseSlotCount}");

        yield return WaitFrames(5);

        // ---- 새 게임 ----
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();

        // ================= 5칸 =================
        sb.AppendLine("\n== 5칸 상태");
        yield return LoadVillage();
        Check(gm.InventorySlotCount == 5, $"칸 수 {gm.InventorySlotCount}, 시작 인벤토리 {Describe(gm)}");
        CheckUiLayout(5);

        sb.AppendLine("  -- 숫자키");
        yield return Press(Key.Digit3);
        Check(gm.SelectedSlotIndex == 2, $"3번 키 → 선택 {gm.SelectedSlotIndex + 1}번 칸");
        yield return Press(Key.Digit6);
        Check(gm.SelectedSlotIndex == 2, $"5칸에서 6번 키 → 선택 그대로 {gm.SelectedSlotIndex + 1}번 칸");
        yield return Press(Key.Digit5);
        Check(gm.SelectedSlotIndex == 4, $"5번 키 → 선택 {gm.SelectedSlotIndex + 1}번 칸");
        yield return Press(Key.Digit1);
        Check(gm.SelectedSlotIndex == 0, $"1번 키 → 선택 {gm.SelectedSlotIndex + 1}번 칸");

        sb.AppendLine("  -- 줍기 (E)");
        yield return PickUpMine(minePickupPrefab, true, gm, "빈 4번 칸에 지뢰 줍기");
        yield return PickUpMine(minePickupPrefab, true, gm, "빈 5번 칸에 지뢰 줍기");
        Check(!gm.HasInventorySpace, $"5칸 가득 참: {Describe(gm)}");
        yield return PickUpMine(minePickupPrefab, false, gm, "가득 찬 5칸에서 줍기 막힘");

        sb.AppendLine("  -- 상점 구매 (가득 참)");
        int mileageBefore = gm.Mileage;
        GiveMileage(gm, 100);
        bool bought = ShopPurchase.TryPurchase(config, mineEntry, out string failMessage);
        Check(!bought && failMessage == "인벤토리가 가득 찼습니다" && gm.Mileage == mileageBefore + 100,
            $"가득 찬 5칸에서 지뢰 구매: 실패 \"{failMessage}\", 마일리지 그대로 {gm.Mileage}");

        sb.AppendLine("  -- G 버리기");
        yield return Press(Key.Digit5);
        int pickupsBefore = ActivePickupCount();
        yield return Press(Key.G);
        Check(gm.GetSlot(4).Type == InventoryItemType.None && ActivePickupCount() == pickupsBefore + 1,
            $"5번 칸 G 버리기: {Describe(gm)}, 바닥 줍기 오브젝트 {pickupsBefore} → {ActivePickupCount()}");

        sb.AppendLine("  -- 구매 (빈 칸 있음)");
        bought = ShopPurchase.TryPurchase(config, mineEntry, out failMessage);
        Check(bought && gm.GetSlot(4).Type == InventoryItemType.Mine, $"빈 5번 칸이 있을 때 지뢰 구매: {Describe(gm)}");

        sb.AppendLine("  -- 클릭 사용");
        yield return Press(Key.Digit1);
        int minesBefore = Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length;
        yield return Click();
        Check(gm.GetSlot(0).Type == InventoryItemType.None && Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length == minesBefore + 1,
            $"1번 칸 지뢰 클릭 사용: {Describe(gm)}, 설치된 지뢰 {minesBefore} → {Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length}");

        sb.AppendLine("  -- 동물 줍기와 납품");
        yield return PickUpAnimal(gm);
        int animalSlot = FindSlot(gm, InventoryItemType.Animal);
        Check(animalSlot == 0, $"동물이 빈 1번 칸에 들어감: {Describe(gm)}");
        yield return DeliverAnimal(gm, Key.Digit1 + animalSlot, animalSlot);

        // ================= 패시브 구매 =================
        sb.AppendLine("\n== 인벤토리 칸 +1 구매");
        yield return Load("ShelterScene");
        gm.SelectSlot(2); // ShelterScene 플레이어에는 숫자키 선택기가 없다 - 선택 유지 확인용으로 3번 칸을 골라 둔다
        string itemsBefore = Describe(gm);
        int selectedBefore = gm.SelectedSlotIndex;
        GiveMileage(gm, slotPrice.price);
        int mileageBeforePassive = gm.Mileage;
        OpenShopAtMerchant(passiveCatalog, config);
        yield return WaitFrames(1);
        Click(Row(slotPassive.DisplayName));
        yield return WaitRealtime(0.5f);
        Check(Subtitle() == "인벤토리 칸 +1을 구매했습니다", $"구매 자막: \"{Subtitle()}\"");
        Check(gm.InventorySlotCount == 6 && gm.Mileage == mileageBeforePassive - slotPrice.price,
            $"칸 수 {gm.InventorySlotCount}, 마일리지 {mileageBeforePassive} → {gm.Mileage}");
        string itemsAfter = Describe(gm);
        Check(itemsAfter == itemsBefore.TrimEnd(']') + ",빈]", $"기존 아이템 유지: {itemsBefore} → {itemsAfter}");
        Check(gm.SelectedSlotIndex == selectedBefore, $"선택 유지: {selectedBefore + 1}번 → {gm.SelectedSlotIndex + 1}번");
        Check(RowPrice(Row(slotPassive.DisplayName)) == "구매 완료", "상점에 구매 완료로 표시");
        ShopMenuUI.Close();

        // ================= 6칸 =================
        sb.AppendLine("\n== 6칸 상태");
        yield return LoadVillage();
        CheckUiLayout(6);
        yield return Press(Key.Digit6);
        Check(gm.SelectedSlotIndex == 5, $"6번 키 → 선택 {gm.SelectedSlotIndex + 1}번 칸");

        // 앞 칸을 모두 채우고 6번째만 비운다
        while (FindSlot(gm, InventoryItemType.None) >= 0 && FindSlot(gm, InventoryItemType.None) < 5) gm.TryAddBomb();
        Check(gm.GetSlot(5).Type == InventoryItemType.None && FindSlot(gm, InventoryItemType.None) == 5, $"6번째만 빈 상태: {Describe(gm)}");
        yield return PickUpMine(minePickupPrefab, true, gm, "6번째 칸에 지뢰 줍기");
        Check(gm.GetSlot(5).Type == InventoryItemType.Mine, $"줍은 지뢰가 6번째 칸에: {Describe(gm)}");
        Check(!gm.HasInventorySpace, "6칸 가득 참");
        yield return PickUpMine(minePickupPrefab, false, gm, "가득 찬 6칸에서 줍기 막힘");
        GiveMileage(gm, 100);
        mileageBefore = gm.Mileage;
        bought = ShopPurchase.TryPurchase(config, mineEntry, out failMessage);
        Check(!bought && failMessage == "인벤토리가 가득 찼습니다" && gm.Mileage == mileageBefore,
            $"가득 찬 6칸에서 지뢰 구매: 실패 \"{failMessage}\", 마일리지 그대로 {gm.Mileage}");

        sb.AppendLine("  -- 6번째 칸 G 버리기");
        yield return Press(Key.Digit6);
        pickupsBefore = ActivePickupCount();
        yield return Press(Key.G);
        Check(gm.GetSlot(5).Type == InventoryItemType.None && ActivePickupCount() == pickupsBefore + 1,
            $"6번 칸 G 버리기: {Describe(gm)}, 바닥 줍기 오브젝트 {pickupsBefore} → {ActivePickupCount()}");

        sb.AppendLine("  -- 6번째 칸 아이템 사용");
        Check(gm.TryAddMine(), "6번째 칸에 지뢰 다시 넣기");
        yield return Press(Key.Digit6);
        minesBefore = Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length;
        yield return Click();
        Check(gm.GetSlot(5).Type == InventoryItemType.None && Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length == minesBefore + 1,
            $"6번 칸 지뢰 클릭 사용: {Describe(gm)}, 설치된 지뢰 {minesBefore} → {Object.FindObjectsByType<MineTrap>(FindObjectsInactive.Exclude).Length}");

        sb.AppendLine("  -- 6번째 칸 동물 납품");
        yield return PickUpAnimal(gm);
        Check(gm.GetSlot(5).Type == InventoryItemType.Animal, $"동물이 6번째 칸에: {Describe(gm)}");
        yield return DeliverAnimal(gm, Key.Digit6, 5);

        // ================= 화면 겹침 =================
        sb.AppendLine("\n== 화면 겹침 (6칸, 거너 무기 슬롯 표시)");
        GiveMileage(gm, config.GetClassUnlockPrice(PlayerClass.Gunner));
        gm.PurchaseClass(PlayerClass.Gunner);
        gm.SelectClass(PlayerClass.Gunner);
        yield return LoadVillage();
        SubtitleManager.Show("자막 위치 확인용 문구입니다 - 인벤토리 칸 +1을 구매했습니다", 5f);
        yield return WaitRealtime(0.6f);
        yield return Press(Key.Digit6);
        CheckOverlap(new Vector2(1920, 1080));
        CheckOverlap(new Vector2(1280, 720));
        ScreenCapture.CaptureScreenshot(ScreenshotPath);
        yield return WaitFrames(3);
        sb.AppendLine($"  Game 뷰 캡처 ({Screen.width}x{Screen.height}): {ScreenshotPath}");

        // ================= Day 넘김 + 씬 왕복 =================
        sb.AppendLine("\n== Day 넘김과 씬 왕복");
        int cycle = gm.CycleCount;
        gm.ResetDay();
        Check(gm.CycleCount == cycle + 1 && gm.InventorySlotCount == 6, $"ResetDay: Day {cycle} → {gm.CycleCount}, 칸 수 {gm.InventorySlotCount}");
        foreach (string scene in new[] { "TruckScene", "VillageScene", "ShelterScene", "TruckScene" })
        {
            if (scene == "VillageScene") yield return LoadVillage();
            else yield return Load(scene);
            int shownSlots = ActiveUiSlots().Count;
            bool hasUi = Object.FindAnyObjectByType<InventoryUI>() != null;
            Check(gm.InventorySlotCount == 6 && (!hasUi || shownSlots == 6),
                $"{scene}: 칸 수 {gm.InventorySlotCount}, UI 칸 {(hasUi ? shownSlots.ToString() : "(UI 없는 씬)")}");
        }

        // ================= 게임 오버 → 새 게임 =================
        sb.AppendLine("\n== 게임 오버 뒤 새 게임");
        if (gm.GetSlot(5).Type == InventoryItemType.None) gm.TryAddMine();
        while (gm.HasInventorySpace) gm.TryAddBomb();
        sb.AppendLine($"  게임 오버 전 인벤토리: {Describe(gm)}");
        gm.FinishCycle();
        Check(gm.IsGameOver, "목표 미달로 게임 오버");
        GameOverPanel panel = Object.FindObjectsByType<GameOverPanel>(FindObjectsInactive.Include).FirstOrDefault();
        if (panel != null)
        {
            panel.Show();
            yield return WaitFrames(1);
            Click(panel.GetComponentsInChildren<Button>(true).First().gameObject);
            yield return WaitForScene("ClassSelectScene");
        }
        else
        {
            Check(false, "TruckScene에 GameOverPanel이 없다");
        }
        int startMines = config.debugStartingMineCount, startBombs = config.debugStartingBombCount;
        string expectedStart = "[" + string.Join(",", Enumerable.Range(0, config.inventoryBaseSlotCount)
            .Select(i => i < startMines ? "지뢰" : i < startMines + startBombs ? "폭탄" : "빈")) + "]";
        Check(gm.InventorySlotCount == 5 && Describe(gm) == expectedStart && gm.SelectedSlotIndex == 0,
            $"재시작 뒤: 칸 수 {gm.InventorySlotCount}, 인벤토리 {Describe(gm)} (기대 {expectedStart}), 선택 {gm.SelectedSlotIndex + 1}번");
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        yield return LoadVillage();
        CheckUiLayout(5);
        yield return Press(Key.Digit6);
        Check(gm.SelectedSlotIndex == 0, $"새 게임 5칸에서 6번 키 → 선택 그대로 {gm.SelectedSlotIndex + 1}번 칸");

        sb.AppendLine($"\n== 콘솔 경고/에러 (Log 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 경고/에러 {logs.Count}건");

        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[InventorySlotTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ---------- 동작 ----------

    private static IEnumerator LoadVillage()
    {
        yield return Load("VillageScene");
        // 테스트 동안 플레이어가 공격받아 쓰러지지(강제 복귀) 않게 몬스터를 끈다.
        foreach (MonsterHealth monster in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) monster.gameObject.SetActive(false);
        yield return WaitFrames(2);
    }

    private static IEnumerator PickUpMine(GadgetPickup prefab, bool expectPicked, GameManager gm, string what)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        GadgetPickup pickup = Object.Instantiate(prefab, player.transform.position + new Vector3(0.15f, 0f, 0f), Quaternion.identity);
        string before = Describe(gm);
        yield return WaitFrames(3); // 범위 안 초점은 직전 프레임 기준
        yield return Press(Key.E);
        bool picked = !pickup.gameObject.activeSelf;
        Check(picked == expectPicked, $"{what}: {before} → {Describe(gm)}");
        if (!picked) Object.Destroy(pickup.gameObject);
        yield return WaitFrames(1);
    }

    private static IEnumerator PickUpAnimal(GameManager gm)
    {
        AnimalRescue animal = Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)
            .OrderBy(a => a.BehaviorKind == AnimalBehaviorKind.Wander ? 0 : 1).FirstOrDefault();
        if (animal == null) { Check(false, "마을에 주울 동물이 없다"); yield break; }

        string before = Describe(gm);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        for (int attempt = 0; attempt < 5 && animal.gameObject.activeSelf; attempt++)
        {
            Teleport(player, animal.transform.position + new Vector3(0.2f, 0f, 0f));
            yield return WaitFrames(3);
            Teleport(player, animal.transform.position + new Vector3(0.2f, 0f, 0f));
            yield return WaitFrames(1);
            yield return Press(Key.E);
        }
        Check(!animal.gameObject.activeSelf, $"동물 줍기 (E): {before} → {Describe(gm)}");
    }

    private static IEnumerator DeliverAnimal(GameManager gm, Key selectKey, int slot)
    {
        yield return Load("TruckScene");
        AnimalDeliveryPoint cage = Object.FindAnyObjectByType<AnimalDeliveryPoint>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (cage == null || player == null) { Check(false, "TruckScene에 납품 케이지나 플레이어가 없다"); yield break; }

        yield return Press(selectKey);
        int rescuedBefore = gm.RescuedCount;
        string before = Describe(gm);
        Teleport(player, cage.transform.position);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        Check(gm.GetSlot(slot).Type == InventoryItemType.None && gm.RescuedCount == rescuedBefore + 1,
            $"{slot + 1}번 칸 동물 납품 (선택 {gm.SelectedSlotIndex + 1}번, E): {before} → {Describe(gm)}, 구조 {rescuedBefore} → {gm.RescuedCount}");
    }

    private static void Teleport(GameObject player, Vector3 position)
    {
        position.z = player.transform.position.z;
        player.transform.position = position;
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

    private static IEnumerator Click()
    {
        Vector2 aim = new Vector2(Screen.width * 0.5f + 80f, Screen.height * 0.5f);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = aim }.WithButton(MouseButton.Left));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vmouse, new MouseState { position = aim });
        yield return WaitFrames(2);
    }

    private static int ActivePickupCount() => Object.FindObjectsByType<GadgetPickup>(FindObjectsInactive.Exclude).Length;

    private static int FindSlot(GameManager gm, InventoryItemType type)
    {
        for (int i = 0; i < gm.InventorySlotCount; i++) if (gm.GetSlot(i).Type == type) return i;
        return -1;
    }

    private static string Describe(GameManager gm) =>
        "[" + string.Join(",", Enumerable.Range(0, gm.InventorySlotCount).Select(i => gm.GetSlot(i).Type switch
        {
            InventoryItemType.None => "빈",
            InventoryItemType.Mine => "지뢰",
            InventoryItemType.Bomb => "폭탄",
            InventoryItemType.Animal => "동물",
            _ => "?"
        })) + "]";

    private static void GiveMileage(GameManager gm, int amount)
    {
        if (amount <= 0) return;
        gm.AddCargo(amount);
        gm.SettleCargo(1);
    }

    // ---------- UI ----------

    private static List<RectTransform> ActiveUiSlots()
    {
        InventoryUI ui = Object.FindAnyObjectByType<InventoryUI>();
        if (ui == null) return new List<RectTransform>();
        return ui.transform.Cast<Transform>()
            .Where(t => t.gameObject.activeSelf && t.name.StartsWith("Slot") && t.name.EndsWith("Frame"))
            .Select(t => (RectTransform)t)
            .OrderBy(t => t.anchoredPosition.x)
            .ToList();
    }

    private static void CheckUiLayout(int expected)
    {
        List<RectTransform> shown = ActiveUiSlots();
        string xs = string.Join(", ", shown.Select(s => s.anchoredPosition.x.ToString("0.#")));
        float pitch = shown.Count >= 2 ? shown[1].anchoredPosition.x - shown[0].anchoredPosition.x : 0f;
        bool centered = shown.Count > 0 && Mathf.Abs(shown.First().anchoredPosition.x + shown.Last().anchoredPosition.x) < 0.01f;
        bool even = shown.Zip(shown.Skip(1), (a, b) => b.anchoredPosition.x - a.anchoredPosition.x).All(d => Mathf.Abs(d - pitch) < 0.01f);
        bool sameSize = shown.All(s => s.sizeDelta == shown[0].sizeDelta);
        Check(shown.Count == expected && centered && even && sameSize,
            $"UI {shown.Count}칸 (기대 {expected}), x = [{xs}], 간격 {pitch:0.#}, 크기 {(shown.Count > 0 ? shown[0].sizeDelta.ToString() : "-")}, 가운데 정렬 {centered}");

        List<TMP_Text> labels = shown.Select(s => s.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault()).ToList();
        string labelTexts = string.Join(",", labels.Select(l => l != null ? l.text : "?"));
        TMP_Text first = labels.FirstOrDefault();
        bool sameStyle = first != null && labels.All(l => l != null && l.fontSize == first.fontSize && l.font == first.font
            && l.alignment == first.alignment && ((RectTransform)l.transform).anchoredPosition == ((RectTransform)first.transform).anchoredPosition);
        string expectedLabels = string.Join(",", Enumerable.Range(1, expected));
        Check(labelTexts == expectedLabels && sameStyle, $"숫자 라벨 [{labelTexts}] 같은 글꼴/크기/위치 {sameStyle}");
    }

    // CanvasScaler(Scale With Screen Size) 계산식으로 화면 해상도별 픽셀 사각형을 구해 서로 겹치는지 본다.
    private static void CheckOverlap(Vector2 screen)
    {
        List<(string name, Rect rect)> slots = ActiveUiSlots().Select(s => ($"칸 {s.name}", PixelRect(s, screen))).ToList();
        Rect bar = slots.Count > 0 ? Rect.MinMaxRect(slots.Min(s => s.rect.xMin), slots.Min(s => s.rect.yMin), slots.Max(s => s.rect.xMax), slots.Max(s => s.rect.yMax)) : default;

        GameObject subtitleGo = GameObject.Find("SubtitleManager");
        TextMeshProUGUI subtitleLabel = subtitleGo != null ? subtitleGo.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        Rect subtitle = subtitleLabel != null ? PixelRect(subtitleLabel.rectTransform, screen) : default;

        List<(string name, Rect rect)> others = new List<(string, Rect)>();
        GameObject weapon = GameObject.Find("WeaponSlotFrame");
        if (weapon != null)
        {
            others.Add(("무기(Q) 슬롯", PixelRect((RectTransform)weapon.transform, screen)));
            foreach (Image corner in weapon.GetComponentsInChildren<Image>(true).Where(i => i.sprite != null && i.sprite.name.StartsWith("All_")))
                others.Add(($"모서리 {corner.sprite.name}", PixelRect(corner.rectTransform, screen)));
        }

        sb.AppendLine($"  -- {screen.x}x{screen.y}");
        sb.AppendLine($"     인벤토리 {slots.Count}칸 {Fmt(bar)} | 자막 상자 {Fmt(subtitle)}");
        foreach ((string name, Rect rect) in others) sb.AppendLine($"     {name} {Fmt(rect)}");

        Check(subtitleLabel != null && !subtitle.Overlaps(bar), $"{screen.x}x{screen.y}: 자막 상자와 인벤토리가 안 겹친다 (세로 틈 {subtitle.yMin - bar.yMax:0.#}px)");
        foreach ((string name, Rect rect) in others)
        {
            Check(!rect.Overlaps(bar), $"{screen.x}x{screen.y}: {name}와 인벤토리가 안 겹친다");
            Check(!rect.Overlaps(subtitle), $"{screen.x}x{screen.y}: {name}와 자막 상자가 안 겹친다");
        }
        Check(bar.xMin >= 0 && bar.xMax <= screen.x && bar.yMin >= 0, $"{screen.x}x{screen.y}: 인벤토리가 화면 안 ({bar.xMin:0}~{bar.xMax:0}px)");
    }

    private static string Fmt(Rect r) => $"x {r.xMin:0}~{r.xMax:0}, y {r.yMin:0}~{r.yMax:0}";

    private static Rect PixelRect(RectTransform target, Vector2 screen)
    {
        List<RectTransform> chain = new List<RectTransform>();
        for (Transform t = target; t != null; t = t.parent) if (t is RectTransform r) chain.Insert(0, r);

        RectTransform root = chain[0];
        float sf = ScaleFactor(root.GetComponent<CanvasScaler>(), screen);
        Vector2 parentSize = screen / sf;
        Vector2 parentMin = -parentSize * 0.5f;
        Vector2 offset = screen * 0.5f;
        float scale = sf;
        for (int i = 1; i < chain.Count; i++)
        {
            RectTransform r = chain[i];
            Vector2 size = parentSize * (r.anchorMax - r.anchorMin) + r.sizeDelta;
            Vector2 anchorRef = parentMin + parentSize * (r.anchorMin + (r.anchorMax - r.anchorMin) * r.pivot);
            offset += scale * (anchorRef + r.anchoredPosition);
            scale *= r.localScale.x;
            parentSize = size;
            parentMin = -r.pivot * size;
        }
        Vector2 min = offset + scale * parentMin;
        return new Rect(min, scale * parentSize);
    }

    private static float ScaleFactor(CanvasScaler scaler, Vector2 screen)
    {
        if (scaler == null || scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler != null ? scaler.scaleFactor : 1f;
        Vector2 reference = scaler.referenceResolution;
        float logW = Mathf.Log(screen.x / reference.x, 2f), logH = Mathf.Log(screen.y / reference.y, 2f);
        float log = scaler.screenMatchMode switch
        {
            CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(logW, logH),
            CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(logW, logH),
            _ => Mathf.Lerp(logW, logH, scaler.matchWidthOrHeight)
        };
        return Mathf.Pow(2f, log);
    }

    private static void OpenShopAtMerchant(ShopCatalog catalog, GameBalanceConfig config)
    {
        ShopMerchant merchant = Object.FindObjectsByType<ShopMerchant>(FindObjectsInactive.Exclude)
            .FirstOrDefault(m => new SerializedObject(m).FindProperty("catalog").objectReferenceValue == catalog);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (merchant != null && player != null) Teleport(player, merchant.transform.position + Vector3.down);
        ShopMenuUI.Open(catalog, config);
    }

    private static GameObject Row(string displayName)
    {
        GameObject ui = GameObject.Find("ShopMenuUI");
        Transform row = ui != null ? ui.transform.Find("Panel/Rows/" + displayName) : null;
        return row != null ? row.gameObject : null;
    }

    private static string RowPrice(GameObject row) =>
        row != null ? row.transform.Find("Price").GetComponent<TextMeshProUGUI>().text : "(줄 없음)";

    private static void Click(GameObject go)
    {
        if (go == null) return;
        ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
    }

    private static string Subtitle()
    {
        GameObject go = GameObject.Find("SubtitleManager");
        TextMeshProUGUI label = go != null ? go.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        return label != null ? label.text : "(자막 없음)";
    }

    // ---------- 대기 ----------

    private static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + frames;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
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
