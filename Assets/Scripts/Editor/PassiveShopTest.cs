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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Passives > Run Passive Shop Test. TitleScene에서 Play를 시작해 실제 상점 메뉴(ShopMenuUI) 줄을 클릭하며 패시브 흐름을 확인한다:
/// 마일리지 부족 → 구매(같은 프레임 연타 포함) → 구매 완료 표시/버튼 꺼짐/다시 클릭 → 클래스 3종 걷기·스프린트 속도 구매 전후 →
/// Day 넘김과 씬 왕복 뒤 유지 → 게임 오버 재시작 뒤 초기화 → 타이틀 PLAY 뒤 초기화. 일반 품목(지뢰) 구매가 그대로인지도 본다.
/// 마일리지는 화물 정산(AddCargo + SettleCargo)으로 넣는다. 씬 파일은 바꾸지 않는다.
/// 결과: Logs/PassiveShopTest.txt
/// </summary>
[InitializeOnLoad]
public static class PassiveShopTest
{
    private const string KeyActive = "PST.Active", KeyDone = "PST.Done";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private const string ReportPath = "Logs/PassiveShopTest.txt";
    private const string RunningShoesPath = "Assets/Settings/Passives/RunningShoes.asset";
    private const string PassiveCatalogPath = "Assets/Settings/Shop/PassiveMerchantCatalog.asset";
    private const string ItemCatalogPath = "Assets/Settings/Shop/ItemMerchantCatalog.asset";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();

    static PassiveShopTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Passives/Run Passive Shop Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[PassiveShopTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(KeyDone, false))
        {
            SessionState.SetBool(KeyActive, false);
        }
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log || condition.StartsWith("[PassiveShopTest]")) return;
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
        if (routine == null || !EditorApplication.isPlaying) { EditorApplication.update -= Step; nested.Clear(); return; }
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
            routine = null;
            Time.timeScale = 1f;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static readonly PlayerClass[] Classes = { PlayerClass.Scout, PlayerClass.Trapper, PlayerClass.Gunner };

    private static IEnumerator Run()
    {
        StringBuilder sb = new StringBuilder();
        List<string> failures = new List<string>();
        void Check(bool ok, string what)
        {
            sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
            if (!ok) failures.Add(what);
        }
        sb.AppendLine($"PassiveShopTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        PassiveDefinition shoes = AssetDatabase.LoadAssetAtPath<PassiveDefinition>(RunningShoesPath);
        ShopCatalog passiveCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(PassiveCatalogPath);
        ShopCatalog itemCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ItemCatalogPath);
        GameBalanceConfig config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        config.TryGetPassivePrice(shoes, out GameBalanceConfig.PassivePrice shoesPrice);
        sb.AppendLine($"러닝화: 가격 {shoesPrice?.price}, 최대 {shoesPrice?.maxPurchases}회, 효과 {shoes.effectKind} x{shoes.effectValue}");

        for (int i = 0; i < 5; i++) yield return null;

        // ---- 새 게임 시작 (타이틀 PLAY) ----
        sb.AppendLine("\n== 새 게임 시작");
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        Check(gm.Mileage == 0 && gm.Passives.GetPurchaseCount(shoes) == 0, $"시작 상태: 마일리지 {gm.Mileage}, 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회");

        yield return Load("ShelterScene");
        PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
        Check(pm != null, "ShelterScene에 PlayerMovement가 있다");

        // ---- 마일리지 0에서 구매 시도 ----
        sb.AppendLine("\n== 마일리지 부족");
        OpenShopAtMerchant(passiveCatalog, config);
        yield return null;
        GameObject shoesRow = Row(shoes.DisplayName);
        Check(shoesRow != null, "패시브 상인 메뉴에 러닝화 줄이 있다 (\"준비 중입니다\" 대신)");
        Check(EmptyTextVisible() == false, "\"준비 중입니다\" 문구가 숨겨졌다");
        sb.AppendLine($"  줄 가격 표시: {RowPrice(shoesRow)}");
        Click(shoesRow);
        yield return WaitRealtime(0.5f);
        Check(Subtitle() == "마일리지가 부족합니다", $"자막: \"{Subtitle()}\"");
        Check(gm.Mileage == 0 && gm.Passives.GetPurchaseCount(shoes) == 0, $"마일리지 {gm.Mileage} (0 아래로 안 내려감), 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회");
        ShopMenuUI.Close();

        // 클래스 둘을 해금하고 150 남긴다 (러닝화 200보다 적게)
        GiveMileage(gm, config.GetClassUnlockPrice(PlayerClass.Trapper) + config.GetClassUnlockPrice(PlayerClass.Gunner) + 150);
        Check(gm.PurchaseClass(PlayerClass.Trapper) && gm.PurchaseClass(PlayerClass.Gunner), "트래퍼/거너 해금");
        Check(gm.Mileage == 150, $"남은 마일리지 {gm.Mileage}");

        // ---- 구매 전 속도 ----
        Dictionary<PlayerClass, (float walk, float sprint)> before = MeasureSpeeds(gm, pm);

        OpenShopAtMerchant(passiveCatalog, config);
        yield return null;
        Click(Row(shoes.DisplayName));
        yield return WaitRealtime(0.5f);
        Check(Subtitle() == "마일리지가 부족합니다" && gm.Mileage == 150, $"150 마일리지로 시도: 자막 \"{Subtitle()}\", 마일리지 {gm.Mileage}");
        ShopMenuUI.Close();

        // ---- 구매 (같은 프레임 두 번 + 다음 프레임 한 번) ----
        sb.AppendLine("\n== 구매");
        GiveMileage(gm, 100); // 250
        OpenShopAtMerchant(passiveCatalog, config);
        yield return null;
        shoesRow = Row(shoes.DisplayName);
        Click(shoesRow);
        Click(shoesRow);
        yield return null;
        Click(shoesRow);
        yield return WaitRealtime(0.5f);
        Check(gm.Passives.GetPurchaseCount(shoes) == 1, $"연타 3번 뒤 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회");
        Check(gm.Mileage == 50, $"마일리지 250 → {gm.Mileage} (200 한 번만 차감)");
        Check(Subtitle() == "이미 구매했습니다", $"마지막(구매 완료 뒤) 클릭 자막: \"{Subtitle()}\"");
        Check(RowPrice(shoesRow) == "구매 완료", $"줄 표시: \"{RowPrice(shoesRow)}\"");
        Check(!shoesRow.GetComponent<Button>().interactable, "구매 버튼이 꺼졌다");
        ShopMenuUI.Close();

        // 구매 직후 자막은 별도 확인 (새 판에서 한 번만 클릭)
        Check(Mathf.Approximately(PassiveEffects.MoveSpeedMultiplier, shoes.effectValue), $"PassiveEffects.MoveSpeedMultiplier = {PassiveEffects.MoveSpeedMultiplier:0.####}");

        // ---- 구매 후 속도 ----
        Dictionary<PlayerClass, (float walk, float sprint)> after = MeasureSpeeds(gm, pm);
        sb.AppendLine("\n== 클래스별 속도 (구매 전 → 구매 후, 배율)");
        foreach (PlayerClass c in Classes)
        {
            float walkRatio = after[c].walk / before[c].walk;
            float sprintRatio = after[c].sprint / before[c].sprint;
            sb.AppendLine($"  {c,-7} 걷기 {before[c].walk:0.0000} → {after[c].walk:0.0000} (x{walkRatio:0.0000}) | " +
                          $"스프린트 {before[c].sprint:0.0000} → {after[c].sprint:0.0000} (x{sprintRatio:0.0000})");
            float expectedWalk = config.playerMoveSpeed * config.GetClassMoveSpeedMultiplier(c) * shoes.effectValue;
            Check(Mathf.Abs(after[c].walk - expectedWalk) < 1e-4f && Mathf.Abs(walkRatio - shoes.effectValue) < 1e-4f && Mathf.Abs(sprintRatio - shoes.effectValue) < 1e-4f,
                $"{c}: 걷기 = playerMoveSpeed x 클래스 배율 x {shoes.effectValue}, 스프린트도 같은 배율");
        }

        // ---- 일반 품목은 그대로 ----
        sb.AppendLine("\n== 일반 품목 (지뢰)");
        GiveMileage(gm, 100);
        int mileageBeforeMine = gm.Mileage;
        OpenShopAtMerchant(itemCatalog, config);
        yield return null;
        GameObject mineRow = Row("지뢰");
        Click(mineRow);
        yield return WaitRealtime(0.5f);
        Check(Subtitle() == "지뢰 구매 완료" && gm.Mileage == mileageBeforeMine - config.GetShopItemPrice(ShopItemKind.Mine) && mineRow.GetComponent<Button>().interactable,
            $"지뢰 구매: 자막 \"{Subtitle()}\", 마일리지 {mileageBeforeMine} → {gm.Mileage}, 버튼 켜짐 {mineRow.GetComponent<Button>().interactable}");
        ShopMenuUI.Close();

        // ---- Day 넘김 + 씬 왕복 ----
        sb.AppendLine("\n== Day 넘김과 씬 왕복");
        int cycle = gm.CycleCount;
        gm.ResetDay();
        Check(gm.CycleCount == cycle + 1 && gm.Passives.GetPurchaseCount(shoes) == 1, $"ResetDay: Day {cycle} → {gm.CycleCount}, 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회");
        foreach (string scene in new[] { "TruckScene", "VillageScene", "TruckScene", "ShelterScene" })
        {
            yield return Load(scene);
            PlayerMovement scenePm = Object.FindAnyObjectByType<PlayerMovement>();
            string speed = scenePm != null ? $"걷기 {scenePm.WalkSpeed:0.0000}, 스프린트 {scenePm.SprintSpeed:0.0000}" : "플레이어 없음";
            Check(SceneManager.GetActiveScene().name == scene && gm.Passives.GetPurchaseCount(shoes) == 1 && Mathf.Approximately(PassiveEffects.MoveSpeedMultiplier, shoes.effectValue),
                $"{scene}: 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회, 배율 {PassiveEffects.MoveSpeedMultiplier:0.####}, {speed}");
        }
        OpenShopAtMerchant(passiveCatalog, config);
        yield return null;
        Check(RowPrice(Row(shoes.DisplayName)) == "구매 완료", "Day를 넘긴 뒤에도 상점에 구매 완료로 보인다");
        ShopMenuUI.Close();

        // ---- 게임 오버 → 재시작 ----
        sb.AppendLine("\n== 게임 오버 뒤 새 게임");
        CycleOutcomeLine(sb, gm.FinishCycle());
        Check(gm.IsGameOver, "목표 미달로 게임 오버");
        GameOverPanel panel = Object.FindObjectsByType<GameOverPanel>(FindObjectsInactive.Include).FirstOrDefault();
        Check(panel != null, "ShelterScene에 GameOverPanel이 있다");
        if (panel != null)
        {
            panel.Show();
            yield return null;
            Click(panel.GetComponentsInChildren<Button>(true).First().gameObject);
            yield return WaitForScene("ClassSelectScene");
            Check(!gm.IsGameOver && gm.Mileage == 0 && gm.Passives.GetPurchaseCount(shoes) == 0 && gm.Passives.OwnedKindCount == 0 && Mathf.Approximately(PassiveEffects.MoveSpeedMultiplier, 1f),
                $"재시작 버튼 뒤: 게임 오버 {gm.IsGameOver}, 마일리지 {gm.Mileage}, 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회, 배율 {PassiveEffects.MoveSpeedMultiplier:0.####}");
        }

        // ---- 다시 사서 성공 자막 확인 → 타이틀로 나가 PLAY ----
        sb.AppendLine("\n== 타이틀로 나간 뒤 새 게임");
        gm.UnlockClassFree(PlayerClass.Scout);
        gm.SelectClass(PlayerClass.Scout);
        gm.CompleteStartingClassSelection();
        yield return Load("ShelterScene");
        GiveMileage(gm, 200);
        OpenShopAtMerchant(passiveCatalog, config);
        yield return null;
        Click(Row(shoes.DisplayName));
        yield return WaitRealtime(0.5f);
        Check(Subtitle() == "러닝화를 구매했습니다" && gm.Mileage == 0 && gm.Passives.GetPurchaseCount(shoes) == 1,
            $"구매 성공 자막: \"{Subtitle()}\", 마일리지 {gm.Mileage}, 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회");
        ShopMenuUI.Close();
        yield return Load("TitleScene");
        Check(GameManager.Instance == gm && gm.Passives.GetPurchaseCount(shoes) == 1, "TitleScene으로 나가도 PLAY 전까지는 그대로 (같은 GameManager)");
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        Check(gm.Passives.GetPurchaseCount(shoes) == 0 && Mathf.Approximately(PassiveEffects.MoveSpeedMultiplier, 1f),
            $"PLAY 뒤: 러닝화 {gm.Passives.GetPurchaseCount(shoes)}회, 배율 {PassiveEffects.MoveSpeedMultiplier:0.####}");

        sb.AppendLine($"\n== 콘솔 경고/에러 (Log 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        if (logs.Count > 0) failures.Add($"콘솔 경고/에러 {logs.Count}건");

        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[PassiveShopTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    private static void CycleOutcomeLine(StringBuilder sb, GameManager.CycleOutcome outcome) =>
        sb.AppendLine($"  FinishCycle: 목표 달성 {outcome.TargetMet}, 클리어 {outcome.GameWon}");

    private static Dictionary<PlayerClass, (float, float)> MeasureSpeeds(GameManager gm, PlayerMovement pm)
    {
        PlayerClass original = gm.CurrentClass;
        Dictionary<PlayerClass, (float, float)> speeds = new Dictionary<PlayerClass, (float, float)>();
        foreach (PlayerClass c in Classes)
        {
            gm.SelectClass(c);
            speeds[c] = (pm.WalkSpeed, pm.SprintSpeed);
        }
        gm.SelectClass(original);
        return speeds;
    }

    // 실제로 상인 앞에서 여는 것처럼 플레이어를 그 상인 옆으로 옮긴다 - ClassSelectScene에서 오면 클래스 선택 문 앞에서
    // 시작해서, 그대로 두면 문 안내 자막이 상점 자막을 덮는다.
    private static void OpenShopAtMerchant(ShopCatalog catalog, GameBalanceConfig config)
    {
        ShopMerchant merchant = Object.FindObjectsByType<ShopMerchant>(FindObjectsInactive.Exclude)
            .FirstOrDefault(m => new SerializedObject(m).FindProperty("catalog").objectReferenceValue == catalog);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (merchant != null && player != null)
        {
            Vector3 pos = merchant.transform.position + Vector3.down;
            player.transform.position = pos;
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = pos;
        }
        else
        {
            Debug.LogWarning($"[PassiveShopTest] {catalog.name}를 쓰는 상인 또는 플레이어를 찾지 못했다.");
        }
        ShopMenuUI.Open(catalog, config);
    }

    // 정산 단가 1로 화물을 팔아 마일리지를 정확히 amount만큼 넣는다.
    private static void GiveMileage(GameManager gm, int amount)
    {
        gm.AddCargo(amount);
        gm.SettleCargo(1);
    }

    private static GameObject Row(string displayName)
    {
        GameObject ui = GameObject.Find("ShopMenuUI");
        Transform row = ui != null ? ui.transform.Find("Panel/Rows/" + displayName) : null;
        return row != null ? row.gameObject : null;
    }

    private static string RowPrice(GameObject row) =>
        row != null ? row.transform.Find("Price").GetComponent<TextMeshProUGUI>().text : "(줄 없음)";

    private static bool EmptyTextVisible()
    {
        GameObject ui = GameObject.Find("ShopMenuUI");
        Transform empty = ui != null ? ui.transform.Find("Panel/Empty") : null;
        return empty != null && empty.gameObject.activeInHierarchy;
    }

    // 실제 마우스 클릭처럼 줄의 모든 클릭 처리기(Button, 구매 완료용 EventTrigger)에 보낸다.
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
        for (int i = 0; i < 5; i++) yield return null;
    }
}
