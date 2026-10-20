using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools > Player > Run Player Vision Query Test. PlayerVision.IsPointVisible / IsVisibleToPlayer가 화면의 두 시야 라이트와 같은 기준으로 판정하는지 본다.
/// 플레이어를 맵에서 멀리 떨어진 빈 곳으로 옮기고(벽은 테스트가 직접 놓는다) 원형 안, 부채꼴 안, 두 범위 밖, 벽 뒤(부채꼴/원형),
/// 플레이어 시야 없음, 플래시라이트 유무를 검사한다. 결과: Logs/PlayerVisionQueryTest.txt
/// batchmode: -executeMethod PlayerVisionQueryTest.RunBatch  (끝나면 에디터가 종료된다, 통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class PlayerVisionQueryTest
{
    private const string KeyActive = "PVQT.Active", KeyBatch = "PVQT.Batch", KeyExit = "PVQT.Exit";
    private const string ReportPath = "Logs/PlayerVisionQueryTest.txt";

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;

    static PlayerVisionQueryTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Player/Run Player Vision Query Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VisionQueryTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(false);
    }

    public static void RunBatch() => Begin(true);

    private static void Begin(bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetInt(KeyExit, 1);
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride { Mode = MapLayoutMode.Grid, Columns = 3, Rows = 4, Seed = 1 };
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            sb = new StringBuilder();
            failures = new List<string>();
            nested.Clear();
            routine = Run();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(SessionState.GetInt(KeyExit, 1));
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

    private static void Step()
    {
        bool more;
        try { more = Advance(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            failures.Add("예외: " + e.Message);
            more = false;
        }
        if (more) return;
        EditorApplication.update -= Step;
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? "결과: 통과" : $"결과: 실패 {failures.Count}건");
        foreach (string f in failures) sb.AppendLine("  " + f);
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[VisionQueryTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
        SessionState.SetInt(KeyExit, failures.Count == 0 ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator Frames(int n)
    {
        int target = Time.frameCount + n;
        float until = Time.realtimeSinceStartup + 10f;
        while (Time.frameCount < target && Time.realtimeSinceStartup < until) yield return null;
    }

    private static GameObject Wall(Vector2 center, Vector2 facing, float width)
    {
        var go = new GameObject("TestWall");
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
        go.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, width); // 바라보는 방향에 수직인 얇은 벽
        Physics2D.SyncTransforms();
        return go;
    }

    private static Vector2 Rotate(Vector2 v, float degrees) => Quaternion.Euler(0f, 0f, degrees) * v;

    private static IEnumerator Run()
    {
        yield return Frames(10);
        var config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>("Assets/Settings/GameBalanceConfig.asset");
        var flashlight = AssetDatabase.LoadAssetAtPath<PassiveDefinition>("Assets/Settings/Passives/Flashlight.asset");

        // 플래시라이트(패시브) 유무를 바꾸려고 GameManager를 만든다(VillageScene에는 없다).
        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            gm = new GameObject("TestGameManager").AddComponent<GameManager>();
            typeof(GameManager).GetField("config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(gm, config);
        }

        PlayerVision vision = PlayerVision.Current;
        if (vision == null) { Check(false, "PlayerVision.Current가 없다"); yield break; }
        Transform player = vision.transform;
        var lights = vision.GetComponentsInChildren<Light2D>(true);
        var ambient = (Light2D)typeof(PlayerVision).GetField("ambientVisionLight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(vision);
        var focused = (Light2D)typeof(PlayerVision).GetField("focusedVisionLight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(vision);

        // 맵에서 아주 먼 빈 곳으로 옮긴다(충돌체 없음).
        Vector2 p = new Vector2(5000f, 5000f);
        player.position = new Vector3(p.x, p.y, player.position.z);
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = p; rb.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
        yield return Frames(3);

        sb.AppendLine("== 플래시라이트 없음 (클래스 " + gm.CurrentClass + ")");
        Vector2 a = ambient.transform.position, f = focused.transform.position;
        float ambientR = ambient.pointLightOuterRadius, focusedR = focused.pointLightOuterRadius, angle = focused.pointLightOuterAngle;
        Vector2 up = focused.transform.up;
        sb.AppendLine($"  원형 반경 {ambientR:F2}(그림자 {ambient.shadowsEnabled}), 부채꼴 반경 {focusedR:F2} 각도 {angle:F1}(그림자 {focused.shadowsEnabled}), 방향 {up}, 라이트 중심 {a} / {f}");
        Check(!gm.Passives.HasFlashlight, "시작 때 플래시라이트가 없다");
        Check(Mathf.Approximately(focusedR, config.GetClassFocusedVisionRadius(gm.CurrentClass) * config.noFlashlightFocusedRadiusMultiplier), "부채꼴 반경 = 기본 x 클래스 x 0.7");

        Check(vision.IsPointVisible(p - up * ambientR * 0.5f), "원형 안(뒤쪽, 반경의 절반)은 보인다");
        Vector2 inCone = p + up * (ambientR + (focusedR - ambientR) * 0.5f);
        Check(vision.IsPointVisible(inCone), "부채꼴 안(원형 밖)은 보인다");
        Check(PlayerVision.IsVisibleToPlayer(inCone), "정적 함수도 같은 답(부채꼴 안)");
        Check(!vision.IsPointVisible(p - up * ambientR * 1.5f), "뒤쪽 원형 밖(부채꼴도 밖)은 안 보인다");
        Check(!vision.IsPointVisible(p + up * (focusedR + 1f)), "부채꼴 반경 밖은 안 보인다");
        Vector2 sideOut = p + Rotate(up, angle * 0.5f + 10f) * (ambientR + 1f);
        Check(!vision.IsPointVisible(sideOut), "부채꼴 각도 밖(원형 밖)은 안 보인다");

        GameObject wall = Wall(p + up * (ambientR + 0.5f), up, 3f);
        Vector2 behind = p + up * (ambientR + 1.5f);
        Check(!vision.IsPointVisible(behind), "부채꼴 안이어도 벽 뒤는 안 보인다");
        Object.Destroy(wall);
        yield return Frames(1);
        Physics2D.SyncTransforms();
        Check(vision.IsPointVisible(behind), "벽을 치우면 같은 자리가 보인다");

        GameObject nearWall = Wall(p - up * 0.4f, -up, 3f);
        Check(vision.IsPointVisible(p - up * ambientR * 0.8f) == !ambient.shadowsEnabled,
            $"원형 안의 벽 뒤: 원형 라이트가 그림자를 {(ambient.shadowsEnabled ? "쓰므로 안 보인다" : "무시하므로 화면처럼 보인다")}");
        Object.Destroy(nearWall);
        yield return Frames(1);
        Physics2D.SyncTransforms();

        // 플래시라이트: 반경 사이·각도 사이 지점은 없으면 안 보이고, 생기면 보인다.
        float fullR = focusedR / config.noFlashlightFocusedRadiusMultiplier;
        float fullAngle = angle / config.noFlashlightFocusedAngleMultiplier;
        Vector2 farPoint = p + up * ((focusedR + fullR) * 0.5f);
        Vector2 widePoint = p + Rotate(up, (angle + fullAngle) * 0.25f) * ((ambientR + focusedR) * 0.5f);
        Check(!vision.IsPointVisible(farPoint), "플래시라이트 없음: 줄어든 반경 밖(원래 반경 안)은 안 보인다");
        Check(!vision.IsPointVisible(widePoint), "플래시라이트 없음: 줄어든 각도 밖(원래 각도 안)은 안 보인다");

        gm.Passives.Add(flashlight);
        yield return Frames(3);
        sb.AppendLine("== 플래시라이트 있음");
        sb.AppendLine($"  부채꼴 반경 {focused.pointLightOuterRadius:F2} 각도 {focused.pointLightOuterAngle:F1}");
        Check(Mathf.Approximately(focused.pointLightOuterRadius, fullR), "부채꼴 반경이 원래 크기로 돌아왔다");
        Check(vision.IsPointVisible(farPoint), "플래시라이트 있음: 같은 먼 지점이 보인다");
        Check(vision.IsPointVisible(widePoint), "플래시라이트 있음: 같은 넓은 각도 지점이 보인다");

        sb.AppendLine("== 플레이어 시야 없음");
        vision.enabled = false;
        Check(PlayerVision.Current == null && !PlayerVision.IsVisibleToPlayer(p), "시야 컴포넌트가 꺼지면 Current가 비고 어디도 보이지 않는다(예외 없음)");
        vision.enabled = true;
        Check(PlayerVision.Current == vision, "다시 켜면 Current가 돌아온다");
        Object.Destroy(player.gameObject);
        yield return Frames(2);
        Check(PlayerVision.Current == null && !PlayerVision.IsVisibleToPlayer(p), "플레이어가 없어지면 어디도 보이지 않는다(예외 없음)");
    }
}
