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
/// Tools > Monsters > Run Corpse Drop Test. 몬스터 시체를 E로 주웠다가 G로 내려놓으면 원래 몬스터로 다시 생기는지 실제 마을(격자 3x4, 시드 1)에서 확인한다.
/// - 늑대: 맵 생성기가 만든 늑대(VillageMapGenerator → MonsterCorpsePickup.RememberSource)
/// - 박쥐: 시간 소환기가 만든 박쥐(VillageMonsterSpawner), 없으면 같은 방식(RememberSource)으로 만든 박쥐
/// - 호저: 스폰 목록에 없어 같은 방식(RememberSource)으로 만든 호저
/// 각각 처치 → (전) 그림·시체 색·그림자 기록 → E로 줍기(칸의 프리팹·무게) → G로 내려놓기 → (후) 같은 몬스터·같은 쓰러진 그림·시체 색·그림자, 다시 주우면 같은 무게. 3번 반복.
/// - 소리반응형: 시체를 주울 수 없는 그대로인지(MonsterCorpsePickup 없음, E를 눌러도 인벤토리 그대로).
/// 실제 렌더: Logs/Corpse/corpse_drop_sheet.png (네 몬스터 시체, 내려놓기 전·후)
/// 결과: Logs/CorpseDropTest.txt / batchmode: -executeMethod CorpseDropTest.RunBatch (통과면 0 아니면 1, 그림 때문에 -nographics 없이)
/// </summary>
[InitializeOnLoad]
public static class CorpseDropTest
{
    private const string KeyActive = "CDT.Active", KeyDone = "CDT.Done", KeyBatch = "CDT.Batch", KeyPassed = "CDT.Passed";
    private const string ReportPath = "Logs/CorpseDropTest.txt";
    private const string SheetPath = "Logs/Corpse/corpse_drop_sheet.png";
    private const float FrameTime = 1f / 30f;
    private const int Repeats = 3;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();
    private static StringBuilder sb;
    private static List<string> failures;

    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);
    private static readonly MethodInfo HealthSetter = typeof(GameManager).GetProperty(nameof(GameManager.Health)).GetSetMethod(true);
    private static readonly FieldInfo DeadColorField = typeof(MonsterHealth).GetField("deadColor", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo IdField = typeof(MonsterCorpsePickup).GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);

    static CorpseDropTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Monsters/Run Corpse Drop Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[CorpseDropTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
        vmouse = InputSystem.AddDevice<Mouse>("CorpseDropTestMouse");
        vmouse.MakeCurrent();
        vkb = InputSystem.AddDevice<Keyboard>("CorpseDropTestKeyboard");
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
        Debug.Log("[CorpseDropTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static GameManager gm;
    private static GameBalanceConfig config;
    private static GameObject player;
    private static PlayerMovement movement;
    private static Rigidbody2D playerBody;
    private static Tilemap fieldGround;
    private static readonly List<(string label, Texture2D tex)> panels = new List<(string, Texture2D)>();

    /// <summary>시체 하나의 겉모습: 몬스터 종류, 쓰러진 그림, 시체 색, 그림자(그림·몸 기준 자리), 그림 묶음이 땅 위치에 있는지.</summary>
    private struct Look
    {
        public string Brain, Sprite, ShadowSprite;
        public Color BodyColor;
        public Vector2 ShadowFromBody;
        public bool ShadowOn, VisualOnGround, Dead;
        public override string ToString() =>
            $"{Brain}, 그림 {Sprite}, 색 {BodyColor}, 그림자 {(ShadowOn ? ShadowSprite : "없음")} @몸{ShadowFromBody}, 그림 묶음이 땅 위치에 {(VisualOnGround ? "있음" : "없음")}";
    }

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        panels.Clear();
        sb.AppendLine($"CorpseDropTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

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

        player = GameObject.FindGameObjectWithTag("Player");
        movement = player.GetComponent<PlayerMovement>();
        playerBody = player.GetComponent<Rigidbody2D>();
        movement.enabled = false;
        GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        GameObject batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/BatMonster.prefab");
        GameObject porcupinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PorcupineMonster.prefab");
        GameObject soundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SoundReactiveMonster.prefab");

        // 시험할 몬스터: 맵 늑대 1, 맵 소리반응형 1, 시간 소환 박쥐(있으면) 1. 나머지 몬스터·동물과 시간 소환은 끈다.
        VillageMonsterSpawner spawner = gm.GetComponent<VillageMonsterSpawner>();
        if (spawner != null) spawner.enabled = false;
        MonsterAI mapWolf = Object.FindObjectsByType<MonsterAI>(FindObjectsInactive.Exclude).OrderBy(m => m.name).FirstOrDefault(m => m.transform.parent != null && m.transform.parent.name.StartsWith("Room_"));
        SoundReactiveMonsterAI mapSound = Object.FindObjectsByType<SoundReactiveMonsterAI>(FindObjectsInactive.Exclude).OrderBy(m => m.name).FirstOrDefault();
        BatMonsterAI timedBat = Object.FindObjectsByType<BatMonsterAI>(FindObjectsInactive.Exclude).OrderBy(m => m.name).FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name == VillageMonsterSpawner.ParentName);
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
            if (mapWolf == null || m.gameObject != mapWolf.gameObject)
                if (mapSound == null || m.gameObject != mapSound.gameObject)
                    if (timedBat == null || m.gameObject != timedBat.gameObject) m.gameObject.SetActive(false);

        sb.AppendLine("\n== 1. 원래 프리팹 기억 (스폰하는 쪽이 MonsterCorpsePickup.SourcePrefab을 넣는다)");
        Check(mapWolf != null && mapWolf.GetComponent<MonsterCorpsePickup>().SourcePrefab == wolfPrefab,
            $"맵 생성 늑대 {mapWolf?.transform.parent?.name}/{mapWolf?.name}: 원래 프리팹 {mapWolf?.GetComponent<MonsterCorpsePickup>()?.SourcePrefab?.name}");
        if (timedBat != null)
            Check(timedBat.GetComponent<MonsterCorpsePickup>().SourcePrefab == batPrefab, $"시간 소환 박쥐 {timedBat.name}: 원래 프리팹 {timedBat.GetComponent<MonsterCorpsePickup>().SourcePrefab?.name}");
        else
            sb.AppendLine("  (참고) 시작할 때 시간 소환 박쥐가 없어 스폰하는 쪽과 같은 방식(RememberSource)으로 만든다");
        Check(soundPrefab.GetComponent<MonsterCorpsePickup>() == null && mapSound != null && mapSound.GetComponent<MonsterCorpsePickup>() == null,
            "소리반응형: 시체 회수 컴포넌트 없음(주울 수 없는 그대로)");

        if (!FindRow(14f, out Vector2 rowStart))
        {
            Check(false, "Field 방에서 가로 14칸 빈 줄을 찾지 못했다");
            Finish();
            yield break;
        }
        sb.AppendLine($"\n시험 자리: Field 방 {rowStart}부터 오른쪽 14칸 (몬스터를 3.5칸 간격으로 둔다)");

        // 시험 자리로 옮긴다. 플레이어는 아주 멀리(추격·발사 범위 밖)에 두고 처치한다.
        PlacePlayer(rowStart + new Vector2(0f, -40f), 0f);
        GameObject wolf = mapWolf.gameObject;
        GameObject bat = timedBat != null ? timedBat.gameObject : SpawnLikeSpawner(batPrefab, rowStart + new Vector2(3.5f, 0f), "BatMonster_T0_test");
        GameObject porcupine = SpawnLikeSpawner(porcupinePrefab, rowStart + new Vector2(7f, 0f), "PorcupineMonster_test");
        GameObject sound = mapSound.gameObject;
        Warp(wolf, rowStart);
        Warp(bat, rowStart + new Vector2(3.5f, 0f));
        Warp(sound, rowStart + new Vector2(10.5f, 0f));
        yield return WaitFrames(5);
        foreach (GameObject m in new[] { wolf, bat, porcupine, sound }) m.GetComponent<MonsterHealth>().TakeDamage(100f);
        yield return WaitFrames(20); // 쓰러지는 그림이 끝나고 박쥐가 바닥으로 내려앉도록

        // 2~4. 늑대·박쥐·호저
        yield return DropCycle("WOLF", wolf, wolfPrefab, config.wolfCorpseWeight);
        yield return DropCycle("BAT", bat, batPrefab, config.wolfCorpseWeight);
        yield return DropCycle("PORCUPINE", porcupine, porcupinePrefab, config.porcupineCorpseWeight);

        // 5. 소리반응형: 주울 수 없음
        sb.AppendLine("\n== 5. 소리반응형: 시체를 주울 수 없는 그대로");
        Vector2 soundAt = GroundOf(sound);
        CaptureAround(sound, "SOUND NO PICKUP");
        List<InventorySlotData> before = Slots();
        PlacePlayer(soundAt + Vector2.left * 1f, 0f);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        Check(sound.activeSelf && sound.GetComponent<MonsterHealth>().IsDead && Slots().SequenceEqual(before),
            "쓰러진 소리반응형 1칸 앞에서 E: 인벤토리 그대로, 시체도 필드에 그대로");

        WriteSheet();
        Finish();
    }

    // 시체 하나: (전) 기록 → 3번 [E로 줍기 → G로 내려놓기 → (후) 비교]. 마지막에 한 번 더 주워 무게를 보고 칸을 비운다.
    private static IEnumerator DropCycle(string label, GameObject monster, GameObject prefab, int expectedWeight)
    {
        sb.AppendLine($"\n== {label}: 줍기 → 내려놓기 {Repeats}번 (기대 무게 {expectedWeight})");
        MonsterCorpsePickup corpse = monster.GetComponent<MonsterCorpsePickup>();
        string id = (string)IdField.GetValue(corpse);
        Look original = LookOf(monster);
        Check(original.Dead && original.ShadowOn && original.VisualOnGround, $"처치한 {label} 시체(전): {original}");
        CaptureAround(monster, $"{label} BEFORE");

        GameObject current = monster;
        Vector2 home = GroundOf(monster); // 매번 처음 쓰러진 자리에 다시 내려놓는다(옆 몬스터 시체와 섞이지 않게)
        bool capturedAfter = false;
        for (int r = 1; r <= Repeats; r++)
        {
            Vector2 at = GroundOf(current);
            PlacePlayer(at + Vector2.left * 1f, 0f); // 오른쪽(시체 쪽)을 본다
            ResetPlayerHealth();
            yield return WaitFrames(3);
            yield return Press(Key.E);
            int slot = SlotOf(id);
            InventorySlotData data = slot >= 0 ? gm.GetSlot(slot) : default;
            Check(slot >= 0 && !current.activeSelf && data.AnimalWeight == expectedWeight && data.MonsterPrefab == prefab && data.AnimalKind == AnimalBehaviorKind.MonsterCorpse,
                $"{r}회차 E로 줍기: {slot}번 칸, 무게 {data.AnimalWeight}, 다시 만들 프리팹 {(data.MonsterPrefab != null ? data.MonsterPrefab.name : "없음")}, 필드에서 치워짐 {!current.activeSelf}");
            if (slot < 0) yield break;

            // 처음 자리 1칸 왼쪽에서 오른쪽을 보고 G (dropDistance 1칸 앞 = 처음 자리에 놓인다)
            PlacePlayer(home + Vector2.left * 1f, 0f);
            gm.SelectSlot(slot);
            yield return WaitFrames(2);
            yield return Press(Key.G);
            yield return WaitFrames(5);
            GameObject dropped = DroppedOf(id, current);
            Check(dropped != null && SlotOf(id) < 0, $"{r}회차 G로 내려놓기: 칸이 비고 시체가 생김 ({(dropped != null ? dropped.name : "없음")})");
            if (dropped == null) yield break;
            Look look = LookOf(dropped);
            bool sameLook = look.Brain == original.Brain && look.Sprite == original.Sprite && look.BodyColor == original.BodyColor &&
                            look.ShadowOn && look.ShadowSprite == original.ShadowSprite && Vector2.Distance(look.ShadowFromBody, original.ShadowFromBody) < 0.01f && look.VisualOnGround && look.Dead;
            Check(sameLook, $"{r}회차 내려놓은 시체(후): {look}");
            Check(dropped.GetComponent<MonsterCorpsePickup>().Weight == expectedWeight && dropped.GetComponent<MonsterCorpsePickup>().SourcePrefab == prefab,
                $"{r}회차 내려놓은 시체의 무게 {dropped.GetComponent<MonsterCorpsePickup>().Weight}, 원래 프리팹 {dropped.GetComponent<MonsterCorpsePickup>().SourcePrefab?.name}");
            if (!capturedAfter) { CaptureAround(dropped, $"{label} AFTER"); capturedAfter = true; }
            if (current != monster) Object.Destroy(current); // 앞 회차에 내려놓았다가 다시 주워 꺼진 시체
            current = dropped;
        }

        // 마지막으로 다시 주워 무게 확인 후 칸을 비우고(G 없이) 정리
        PlacePlayer(GroundOf(current) + Vector2.left * 1f, 0f);
        yield return WaitFrames(3);
        yield return Press(Key.E);
        int last = SlotOf(id);
        Check(last >= 0 && gm.GetSlot(last).AnimalWeight == expectedWeight, $"{Repeats}번 내려놓은 뒤 다시 주운 무게 {(last >= 0 ? gm.GetSlot(last).AnimalWeight : -1)} (원래 {expectedWeight})");
        if (last >= 0) { gm.SelectSlot(last); gm.TryDropSelectedItem(out _); }
        if (current != monster) Object.Destroy(current);
        gm.SelectWeapon();
        yield return WaitFrames(2);
    }

    // ===================== 겉모습 =====================

    private static Look LookOf(GameObject m)
    {
        SimpleFrameAnimator anim = m.GetComponentInChildren<SimpleFrameAnimator>(true);
        SpriteRenderer body = anim != null ? anim.GetComponent<SpriteRenderer>() : null;
        Transform visual = anim != null ? anim.transform : null;
        while (visual != null && visual.parent != null && visual.parent != m.transform) visual = visual.parent;
        Transform shadowT = visual != null ? visual.Find("Shadow") ?? body?.transform.Find("Shadow") : null;
        SpriteRenderer shadow = shadowT != null ? shadowT.GetComponent<SpriteRenderer>() : null;
        MonsterBrain brain = m.GetComponent<MonsterBrain>();
        Vector2 ground = GroundOf(m);
        return new Look
        {
            Brain = brain != null ? brain.GetType().Name : "(없음)",
            Sprite = body != null && body.sprite != null ? body.sprite.name : "",
            BodyColor = body != null ? body.color : Color.clear,
            ShadowOn = shadow != null && shadow.enabled && shadow.gameObject.activeInHierarchy && shadow.sprite != null,
            ShadowSprite = shadow != null && shadow.sprite != null ? shadow.sprite.name : "",
            ShadowFromBody = shadow != null && body != null ? (Vector2)(shadow.transform.position - body.transform.position) : Vector2.zero,
            VisualOnGround = visual != null && Vector2.Distance(visual.position, ground) < 0.01f,
            Dead = m.GetComponent<MonsterHealth>().IsDead,
        };
    }

    private static Vector2 GroundOf(GameObject m) => new Vector2(m.transform.position.x, m.transform.position.z);

    private static GameObject DroppedOf(string id, GameObject except)
    {
        foreach (MonsterCorpsePickup c in Object.FindObjectsByType<MonsterCorpsePickup>(FindObjectsInactive.Exclude))
            if (c.gameObject != except && (string)IdField.GetValue(c) == id) return c.gameObject;
        return null;
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

    private static List<InventorySlotData> Slots() => Enumerable.Range(0, gm.InventorySlotCount).Select(i => gm.GetSlot(i)).ToList();

    // 스폰하는 쪽(VillageMapGenerator.SpawnMonsterAt, VillageMonsterSpawner.Create)과 같은 순서: NavMesh 자리에 만들고 이름을 붙이고 원래 프리팹을 기억시킨다.
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
        if (NavMesh.SamplePosition(new Vector3(at.x, 0f, at.y), out NavMeshHit hit, 1f, NavMesh.AllAreas) && agent != null && agent.isOnNavMesh) agent.Warp(hit.position);
    }

    // ===================== 실제 렌더 =====================

    private static void CaptureAround(GameObject m, string label)
    {
        Vector2 c = GroundOf(m);
        Texture2D tex = Capture(c - new Vector2(1.6f, 1.6f), c + new Vector2(1.6f, 1.6f));
        if (tex != null) panels.Add((label, tex));
    }

    // 게임 카메라를 자를 곳 가운데로 잠깐 옮겨 RenderTexture에 한 번 더 그린다. 이 렌더 한 번만 전역 조명을 1로(밤 어둠에 묻히지 않게).
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
        string[] expected = { "WOLF BEFORE", "WOLF AFTER", "BAT BEFORE", "BAT AFTER", "PORCUPINE BEFORE", "PORCUPINE AFTER", "SOUND NO PICKUP" };
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
        Check(list.Count == expected.Length, $"실제 렌더 {list.Count}/{expected.Length}칸: {Path.GetFullPath(SheetPath)}");
        foreach (var p in panels) if (p.tex != null) Object.DestroyImmediate(p.tex);
        panels.Clear();
    }

    // ===================== 도우미 =====================

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

    // 가장 큰 방(Field)에서 트럭과 떨어진, 오른쪽으로 length칸 곧은 빈 줄(바닥·NavMesh 위, 위아래 1.5칸 안에 막는 충돌체 없음)을 방 가운데에 가깝게 찾는다.
    private static bool FindRow(float length, out Vector2 start)
    {
        start = Vector2.zero;
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
        float bestD = float.MaxValue;
        bool found = false;
        for (int dy = -10; dy <= 10; dy++)
            for (int dx = -16; dx <= 4; dx++)
            {
                Vector2 p = roomCenter + new Vector2(dx, dy);
                if (Vector2.Distance(p, truckAt) < 10f || Vector2.Distance(p + Vector2.right * length, truckAt) < 10f) continue;
                bool ok = true;
                for (float k = -1.5f; k <= length && ok; k += 0.5f)
                {
                    Vector2 q = p + Vector2.right * k;
                    if (!NavMesh.SamplePosition(new Vector3(q.x, 0f, q.y), out _, 0.1f, NavMesh.AllAreas)) ok = false;
                    else if (!fieldGround.HasTile(fieldGround.WorldToCell(new Vector3(q.x, q.y, 0f)))) ok = false;
                }
                if (!ok) continue;
                if (Physics2D.OverlapBoxAll(p + Vector2.right * (length * 0.5f - 0.75f), new Vector2(length + 3f, 3f), 0f).Any(col => !col.isTrigger && !col.CompareTag("Player"))) continue;
                float d = Vector2.Distance(p + Vector2.right * length * 0.5f, roomCenter);
                if (d < bestD) { bestD = d; start = p; found = true; }
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
