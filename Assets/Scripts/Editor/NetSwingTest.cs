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
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Player > Run Net Swing Test. 트래퍼 포획망 그림(NetSwingEffect, 시안 B)을 실제 마을(격자 3x4, 시드 1)에서 실제 PlayerWeaponController.SwingNet으로 확인한다.
/// 휘두르기는 실제 플레이처럼 무기 선택(Q) 상태에서 가상 마우스 왼쪽 클릭으로 한다(PlayerWeaponController.Update → SwingNet, 게임 루프 안).
/// 바라보는 방향은 마우스 대신 PlayerMovement.LookDirection과 몸 회전을 직접 정한다(PlayerMovement는 끈다).
/// 1) 씬 연결: Player의 PlayerWeaponController에 연결, 플레이어 자식이 아닌 배율 1 오브젝트, 그림 PPU 16·피벗 = 판정 원점
/// 2) 휘두르면 그물 그림이 0.3초(20fps 6칸: 뭉침 → 펼침 → 다 펼침 x2 → 거둠 → 뭉침) 재생되고 사라짐, 정렬 = 플레이어 몸 + 1
/// 3) 그림 방향 = 판정 방향, 다 펼친 그물 범위 = 판정 범위(2칸, 90°) - 트래퍼 배율 0.72가 곱해지지 않음. 경계 안·밖 늑대로 판정도 함께 확인
/// 4) 휘두르는 도중에 플레이어가 돌아도 그림 방향은 고정, 위치는 플레이어를 따라감
/// 5) 재사용 대기 중에 다시 휘두르면 그림도 나오지 않음, 대기가 끝나면 새 방향으로 다시 나옴
/// 프레임 시간 1/30초 고정. 결과: Logs/NetSwingTest.txt / batchmode: -executeMethod NetSwingTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class NetSwingTest
{
    private const string KeyActive = "NST.Active", KeyDone = "NST.Done", KeyBatch = "NST.Batch", KeyPassed = "NST.Passed";
    private const string ReportPath = "Logs/NetSwingTest.txt";
    private const float FrameTime = 1f / 30f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static readonly List<string> logs = new List<string>();
    private static StringBuilder sb;
    private static List<string> failures;

    private static readonly FieldInfo EffectField = typeof(PlayerWeaponController).GetField("netSwingEffect", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo FramesField = typeof(NetSwingEffect).GetField("frames", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CooldownField = typeof(PlayerWeaponController).GetField("netCooldownTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo LookSetter = typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.LookDirection)).GetSetMethod(true);

    static NetSwingTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Player/Run Net Swing Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NetSwingTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            SetUpMouse();
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

    private static void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        Time.captureDeltaTime = 0f;
        VillageMapGenerator.EditorLayoutOverride = null;
        if (vmouse != null) InputSystem.RemoveDevice(vmouse);
        vmouse = null;
        foreach (InputDevice d in disabledMice) if (d != null && d.added) InputSystem.EnableDevice(d);
        disabledMice.Clear();
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
    }

    // 실제 마우스는 끄고(손이 닿아도 결과가 같게) 가상 마우스만 쓴다.
    private static Mouse vmouse;
    private static readonly List<InputDevice> disabledMice = new List<InputDevice>();
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;

    private static void SetUpMouse()
    {
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        disabledMice.Clear();
        foreach (InputDevice d in InputSystem.devices.ToList())
            if (d is Mouse && d.enabled) { InputSystem.DisableDevice(d); disabledMice.Add(d); }
        vmouse = InputSystem.AddDevice<Mouse>("NetSwingTestMouse");
        vmouse.MakeCurrent();
    }

    // 왼쪽 버튼을 누른다. 누른 입력은 다음 프레임 처음에 처리되어 그 프레임의 PlayerWeaponController.Update가 휘두른다.
    // 휘두른 그 프레임이 끝난 뒤(그림이 처음 그려진 뒤)에 돌아온다. 버튼은 그다음 프레임에 뗀다.
    private static IEnumerator Click()
    {
        InputSystem.QueueStateEvent(vmouse, new MouseState().WithButton(MouseButton.Left, true));
        yield return WaitFrames(1);
        InputSystem.QueueStateEvent(vmouse, new MouseState());
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
        Debug.Log("[NetSwingTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static GameObject player;
    private static PlayerMovement movement;
    private static PlayerWeaponController weapon;
    private static Rigidbody2D body;
    private static NetSwingEffect effect;
    private static SpriteRenderer effectRenderer, playerRenderer;
    private static GameBalanceConfig config;

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        sb.AppendLine($"NetSwingTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        yield return WaitFrames(5);
        Object.FindAnyObjectByType<TitleMenu>().OnPlay();
        yield return WaitForScene("ClassSelectScene");
        GameManager gm = GameManager.Instance;
        gm.UnlockClassFree(PlayerClass.Trapper);
        gm.SelectClass(PlayerClass.Trapper);
        gm.CompleteStartingClassSelection();
        config = gm.Config;
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        SceneManager.LoadScene("VillageScene");
        yield return WaitForScene("VillageScene");
        VillageMapGenerator.EditorLayoutOverride = null;
        gm.SelectWeapon();

        player = GameObject.FindGameObjectWithTag("Player");
        movement = player.GetComponent<PlayerMovement>();
        weapon = player.GetComponent<PlayerWeaponController>();
        body = player.GetComponent<Rigidbody2D>();
        playerRenderer = player.GetComponent<SpriteRenderer>();
        effect = EffectField.GetValue(weapon) as NetSwingEffect;
        sb.AppendLine($"포획망 설정: 거리 {config.netRange}칸, 각도 {config.netAngle}도, 피해 {config.netDamage}, 기절 {config.stunDuration}초, 재사용 대기 {config.netCooldown}초 / 트래퍼 배율 {player.transform.localScale.x}");

        // 1) 씬 연결
        sb.AppendLine("\n== 1. 씬 연결과 그림 설정");
        Check(effect != null, "VillageScene의 Player PlayerWeaponController에 NetSwingEffect가 연결돼 있다");
        if (effect == null) { Finish(); yield break; }
        effectRenderer = effect.GetComponent<SpriteRenderer>();
        Sprite[] frames = (Sprite[])FramesField.GetValue(effect);
        Check(effect.transform.parent == null && effect.transform.lossyScale == Vector3.one,
            $"플레이어 자식이 아닌 따로 둔 오브젝트, 배율 {effect.transform.lossyScale} (플레이어 배율 {player.transform.localScale.x}이 곱해지지 않음)");
        Check(frames.Length == 6 && string.Join(",", frames.Select(f => f.name)) == "net_B1_bundle,net_B2_open,net_B3_full,net_B3_full,net_B4_retract,net_B5_bundle",
            $"칸 {frames.Length}개: {string.Join(" → ", frames.Select(f => f.name))}");
        bool importOk = frames.Distinct().All(s =>
        {
            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s));
            return imp != null && Mathf.Approximately(s.pixelsPerUnit, 16f) && Vector2.Distance(s.pivot, new Vector2(2f, 24f)) < 0.01f &&
                   imp.filterMode == FilterMode.Point && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.mipmapEnabled;
        });
        Check(importOk, "그림 가져오기: PPU 16(1텍셀 = 1/16칸), 피벗 (2, 24)텍셀 = 판정 원점, Point, 압축 없음, 밉맵 끔");
        Check(effect.Duration > 0.299f && effect.Duration < 0.301f, $"재생 시간 {effect.Duration:F3}초 (6칸 / 20fps)");
        Check(!effectRenderer.enabled && !effect.IsPlaying, "휘두르기 전에는 그림이 보이지 않는다");

        // 시험 자리: 플레이어 직접 조종(마우스 시선 끔), 다른 몬스터·동물 끔
        movement.enabled = false;
        foreach (AnimalRescue a in Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)) a.gameObject.SetActive(false);
        foreach (MonsterHealth m in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) m.gameObject.SetActive(false);
        Vector2 spot = OpenSpot();
        Place(spot, 30f);
        yield return WaitFrames(3);

        // 2·3) 30도로 휘두르기: 재생·사라짐·방향·범위·판정
        sb.AppendLine("\n== 2. 30도로 휘두르기: 0.3초 재생 후 사라짐, 정렬 = 플레이어 몸 + 1");
        GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/PatrolMonster.prefab");
        var probes = new (string what, float dist, float rel, bool expect)[]
        {
            ("정면 1.9칸", 1.9f, 0f, true), ("정면 2.1칸", 2.1f, 0f, false), ("1.5칸 +43도", 1.5f, 43f, true),
            ("1.5칸 +48도", 1.5f, 48f, false), ("1.5칸 -43도", 1.5f, -43f, true), ("1.5칸 -48도", 1.5f, -48f, false),
        };
        List<(string what, bool expect, MonsterHealth wolf)> wolves = new List<(string, bool, MonsterHealth)>();
        foreach (var p in probes)
        {
            Vector2 at = spot + p.dist * Dir(30f + p.rel);
            MonsterHealth w = Object.Instantiate(wolfPrefab, new Vector3(at.x, 0f, at.y), Quaternion.identity).GetComponent<MonsterHealth>();
            w.name = "NetSwingTestWolf";
            wolves.Add((p.what, p.expect, w));
        }
        yield return WaitFrames(2);
        foreach (var w in wolves) w.wolf.Stun(1000f); // 판정 전까지 제자리(기절은 늑대 위치만 붙잡는다)

        CooldownField.SetValue(weapon, 0f);
        yield return Click(); // 돌아오면 휘두른 그 프레임이 이미 한 번 그려진 뒤다
        float swingAngle = effect.transform.eulerAngles.z;
        Vector2 origin = player.transform.position;
        Check(effect.IsPlaying && effectRenderer.enabled && effectRenderer.sprite.name == "net_B1_bundle" && Mathf.Abs(Mathf.DeltaAngle(swingAngle, 30f)) < 0.01f,
            $"클릭해 휘두른 그 프레임부터 첫 칸({effectRenderer.sprite?.name}) 재생, 그림 방향 {swingAngle:F2}도 = 판정 방향(LookDirection) 30도");

        List<string> seq = new List<string>();
        int visibleFrames = 0, orderOk = 0, orderChecked = 0;
        bool turnedFixed = true, followed = true;
        float b3Max = 0f, b3AngleMin = float.MaxValue, b3AngleMax = float.MinValue;
        Vector2 moved = spot;
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) yield return WaitFrames(1);
            if (!effectRenderer.enabled) break;
            visibleFrames++;
            string n = effectRenderer.sprite.name;
            if (seq.Count == 0 || seq[seq.Count - 1] != n) seq.Add(n);
            orderChecked++;
            if (effectRenderer.sortingOrder == playerRenderer.sortingOrder + 1 && effectRenderer.sortingLayerID == playerRenderer.sortingLayerID) orderOk++;
            // 그림은 플레이어를 따라가므로(아래에서 도중에 옮긴다) 원점은 지금 그림 자리 = 플레이어 위치로 잰다.
            if (n == "net_B3_full" && b3Max == 0f) MeasureRange(effect.transform.position, 30f, ref b3Max, ref b3AngleMin, ref b3AngleMax);
            // 4) 휘두르는 도중(3번째 프레임)에 플레이어가 120도로 돌고 0.5칸 옮겨 간다
            if (i == 2)
            {
                moved = spot + new Vector2(0.3f, -0.4f);
                Place(moved, 120f);
            }
            if (i >= 3)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(effect.transform.eulerAngles.z, 30f)) > 0.01f) turnedFixed = false;
                if (Vector2.Distance(effect.transform.position, player.transform.position) > 0.001f) followed = false;
            }
        }
        float shown = visibleFrames * FrameTime;
        Check(visibleFrames == 9 && !effect.IsPlaying && !effectRenderer.enabled,
            $"그림이 {visibleFrames}프레임 = {shown:F3}초 보이고 사라진다 (기대 1/30초 프레임 9개 = 0.30초: 경과 0~0.267초에 그려지고 0.30초에 사라짐)");
        Check(string.Join(",", seq) == "net_B1_bundle,net_B2_open,net_B3_full,net_B4_retract,net_B5_bundle",
            $"재생 순서 {string.Join(" → ", seq)}");
        Check(orderOk == orderChecked && orderChecked > 0, $"정렬: 보이는 {orderChecked}프레임 모두 플레이어 몸과 같은 레이어, Order + 1 ({orderOk}/{orderChecked})");

        sb.AppendLine("\n== 3. 다 펼친 그물 범위 = 판정 범위(2칸, 90도) - 트래퍼 배율이 곱해지지 않음");
        Check(b3Max >= config.netRange - 0.06f && b3Max <= config.netRange + 0.001f,
            $"다 펼친 그물(B3)의 가장 먼 칸 가운데 {b3Max:F3}칸 (판정 거리 {config.netRange}칸, 배율이 곱해졌다면 {b3Max * player.transform.localScale.x:F3}칸)");
        Check(b3AngleMin >= -config.netAngle * 0.5f - 0.6f && b3AngleMax <= config.netAngle * 0.5f + 0.6f && b3AngleMin <= -config.netAngle * 0.5f + 2f && b3AngleMax >= config.netAngle * 0.5f - 2f,
            $"다 펼친 그물의 각도 범위 {b3AngleMin:F1}~{b3AngleMax:F1}도 (판정 ±{config.netAngle * 0.5f}도, 판정 방향 기준)");
        foreach (var w in wolves)
        {
            bool hit = w.wolf.Health < w.wolf.MaxHealth - 0.0001f;
            Check(hit == w.expect, $"판정: {w.what} 늑대 → {(hit ? $"맞음(체력 {w.wolf.Health:0.##})" : "안 맞음")} (기대 {(w.expect ? "맞음" : "안 맞음")})");
            Object.Destroy(w.wolf.gameObject);
        }

        sb.AppendLine("\n== 4. 휘두르는 도중 플레이어가 돌아도 그림 방향은 고정, 위치는 따라감");
        Check(turnedFixed, $"3번째 프레임에 플레이어가 120도로 돌았지만 그림은 끝까지 30도 (플레이어 지금 {player.transform.eulerAngles.z:F0}도)");
        Check(followed, $"플레이어를 0.5칸 옮기자 그림 위치도 따라감 (그림 {(Vector2)effect.transform.position}, 플레이어 {(Vector2)player.transform.position})");

        // 5) 재사용 대기
        sb.AppendLine("\n== 5. 재사용 대기 중에는 그림도 나오지 않음");
        Place(moved, 90f);
        yield return WaitFrames(1);
        float cd = (float)CooldownField.GetValue(weapon);
        yield return Click();
        Check(cd > 0f && !effect.IsPlaying && !effectRenderer.enabled, $"대기 {cd:F2}초 남았을 때 클릭해도 그림이 나오지 않는다");
        int waitFrames = Mathf.CeilToInt(cd / FrameTime) + 1;
        yield return WaitFrames(waitFrames);
        yield return Click();
        Check(effect.IsPlaying && Mathf.Abs(Mathf.DeltaAngle(effect.transform.eulerAngles.z, 90f)) < 0.01f,
            $"대기가 끝나면({waitFrames}프레임 뒤) 클릭에 다시 나오고 새 방향 90도로 재생 (그림 {effect.transform.eulerAngles.z:F1}도)");
        yield return WaitFrames(12);
        Check(!effectRenderer.enabled, "다시 0.3초 뒤 사라진다");

        Finish();
    }

    // 지금 보이는 그림(B3)의 투명 아닌 칸 가운데를 월드로 옮겨, 휘두른 원점에서의 거리·판정 방향 기준 각도 범위를 잰다.
    private static void MeasureRange(Vector2 origin, float lookDeg, ref float maxDist, ref float minAngle, ref float maxAngle)
    {
        Sprite s = effectRenderer.sprite;
        Texture2D t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(s)));
        for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
            {
                if (t.GetPixel(x, y).a < 0.01f) continue;
                Vector2 local = (new Vector2(x + 0.5f, y + 0.5f) - s.pivot) / s.pixelsPerUnit;
                Vector2 world = effect.transform.TransformPoint(local);
                Vector2 d = world - origin;
                maxDist = Mathf.Max(maxDist, d.magnitude);
                float a = Mathf.DeltaAngle(lookDeg, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                minAngle = Mathf.Min(minAngle, a);
                maxAngle = Mathf.Max(maxAngle, a);
            }
        Object.DestroyImmediate(t);
    }

    private static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

    // 플레이어를 at에 세우고 deg 방향을 보게 한다(판정이 읽는 LookDirection과 몸 회전을 함께).
    private static void Place(Vector2 at, float deg)
    {
        LookSetter.Invoke(movement, new object[] { Dir(deg) });
        body.position = at;
        body.rotation = deg;
        player.transform.SetPositionAndRotation(new Vector3(at.x, at.y, player.transform.position.z), Quaternion.Euler(0f, 0f, deg));
        body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    // 가장 큰 방(Field)에서 트럭·장애물 없이 5x5칸이 바닥이고 NavMesh 위인, 방 가운데에 가까운 자리.
    private static Vector2 OpenSpot()
    {
        Tilemap best = null;
        foreach (Transform room in GameObject.Find("GeneratedMap").transform)
        {
            Tilemap g = room.Find("Ground")?.GetComponent<Tilemap>();
            if (g == null || !room.name.StartsWith("Room_")) continue;
            g.CompressBounds();
            if (best == null || g.cellBounds.size.x * g.cellBounds.size.y > best.cellBounds.size.x * best.cellBounds.size.y) best = g;
        }
        Vector3 c = best.transform.TransformPoint(best.localBounds.center);
        Vector2 roomCenter = new Vector2(Mathf.Round(c.x) + 0.5f, Mathf.Round(c.y) + 0.5f);
        TruckPoint truck = Object.FindAnyObjectByType<TruckPoint>();
        Vector2 truckAt = truck != null ? (Vector2)truck.transform.position : new Vector2(float.MaxValue, float.MaxValue);
        Vector2 spot = roomCenter;
        float bestDistance = float.MaxValue;
        for (int dy = -8; dy <= 8; dy++)
            for (int dx = -12; dx <= 12; dx++)
            {
                Vector2 p = roomCenter + new Vector2(dx, dy);
                if (Vector2.Distance(p, truckAt) < 9f) continue;
                if (!NavMesh.SamplePosition(new Vector3(p.x, 0f, p.y), out NavMeshHit hit, 0.1f, NavMesh.AllAreas)) continue;
                Vector3Int cell = best.WorldToCell(new Vector3(p.x, p.y, 0f));
                bool floor = true;
                for (int yy = -3; yy <= 3 && floor; yy++)
                    for (int xx = -3; xx <= 3 && floor; xx++)
                        if (!best.HasTile(cell + new Vector3Int(xx, yy, 0))) floor = false;
                if (!floor) continue;
                if (Physics2D.OverlapBoxAll(p, new Vector2(5f, 5f), 0f).Any(col => !col.isTrigger && !col.CompareTag("Player"))) continue;
                float d = Vector2.Distance(p, roomCenter);
                if (d < bestDistance) { bestDistance = d; spot = p; }
            }
        return spot;
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
