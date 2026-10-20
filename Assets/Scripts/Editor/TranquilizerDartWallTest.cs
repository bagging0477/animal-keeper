using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Items > Run Tranquilizer Dart Wall Test. 마취총 다트가 벽(고정 충돌체)에 막히는지 Play 모드에서 실제 다트를 쏴서 확인한다.
/// 1) 맵에서 멀리 떨어진 빈 곳에 만든 인공 벽: 벽 없음/벽 뒤 몬스터/벽 뒤 동물/얇은 벽 + 빠른 다트/트리거와 움직이는 리지드바디는 통과/총구가 벽 안
/// 2) 실제 마을 맵(격자 3x4 시드 1, 한 줄 시드 1): 맵 경계 뒤, 장애물 뒤, 벽 없는 곳
/// 프레임 시간을 1/30초로 고정해(Time.captureDeltaTime) 다트가 한 프레임에 크게 움직여도 검사되는지 본다. 결과: Logs/TranquilizerDartWallTest.txt
/// batchmode: -executeMethod TranquilizerDartWallTest.RunBatch  (끝나면 에디터가 종료된다, 통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class TranquilizerDartWallTest
{
    private const string KeyActive = "TDWT.Active", KeyPhase = "TDWT.Phase", KeyBatch = "TDWT.Batch", KeyReport = "TDWT.Report", KeyFails = "TDWT.Fails";
    private const string ReportPath = "Logs/TranquilizerDartWallTest.txt";
    private const string DartPrefabPath = "Assets/Prefabs/TranquilizerDart.prefab";
    private const float DartSpeed = 12f, Range = 43.2f, Damage = 0.8f;

    private static IEnumerator routine;
    private static StringBuilder sb;
    private static List<string> failures;

    static TranquilizerDartWallTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Items/Run Tranquilizer Dart Wall Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[DartWallTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin(false);
    }

    public static void RunBatch() => Begin(true);

    private static void Begin(bool batch)
    {
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetInt(KeyPhase, 0);
        SessionState.SetString(KeyReport, "");
        SessionState.SetString(KeyFails, "");
        StartPhase();
    }

    // 0: 격자 3x4 시드 1, 1: 한 줄 시드 1
    private static void StartPhase()
    {
        int phase = SessionState.GetInt(KeyPhase, 0);
        VillageMapGenerator.EditorLayoutOverride = new VillageMapGenerator.EditorOverride
        {
            Mode = phase == 0 ? MapLayoutMode.Grid : MapLayoutMode.Linear, Columns = 3, Rows = 4, Seed = 1
        };
        EditorSceneManager.OpenScene(VillageRoomBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            sb = new StringBuilder(SessionState.GetString(KeyReport, ""));
            failures = SessionState.GetString(KeyFails, "").Split('\n').Where(s => s.Length > 0).ToList();
            nested.Clear();
            routine = Run(SessionState.GetInt(KeyPhase, 0));
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            int phase = SessionState.GetInt(KeyPhase, 0) + 1;
            SessionState.SetInt(KeyPhase, phase);
            if (phase < 2) { StartPhase(); return; }

            VillageMapGenerator.EditorLayoutOverride = null;
            SessionState.SetBool(KeyActive, false);
            var report = new StringBuilder(SessionState.GetString(KeyReport, ""));
            List<string> fails = SessionState.GetString(KeyFails, "").Split('\n').Where(s => s.Length > 0).ToList();
            report.AppendLine();
            report.AppendLine(fails.Count == 0 ? "결과: 통과" : $"결과: 실패 {fails.Count}건");
            foreach (string f in fails) report.AppendLine("  " + f);
            File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
            Debug.Log("[DartWallTest] " + (fails.Count == 0 ? "통과" : $"실패 {fails.Count}건") + " - " + Path.GetFullPath(ReportPath));
            if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(fails.Count == 0 ? 0 : 1);
        }
    }

    // 안쪽 단계(yield return IEnumerator)를 차례로 실행한다 - EditorApplication.update에는 코루틴 실행기가 없다.
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();

    private static bool Advance()
    {
        while (true)
        {
            IEnumerator top = nested.Count > 0 ? nested.Peek() : routine;
            if (top == null) return false;
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
        Time.captureDeltaTime = 0f;
        SessionState.SetString(KeyReport, sb.ToString());
        SessionState.SetString(KeyFails, string.Join("\n", failures));
        EditorApplication.ExitPlaymode();
    }

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static IEnumerator WaitFrames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    private static IEnumerator Run(int phase)
    {
        Time.captureDeltaTime = 1f / 30f;
        yield return WaitFrames(5);
        var prefab = AssetDatabase.LoadAssetAtPath<TranquilizerDart>(DartPrefabPath);

        if (phase == 0)
        {
            sb.AppendLine("== 1. 인공 벽 (맵에서 멀리 떨어진 빈 곳, 프레임 1/30초)");
            Vector2 o = new Vector2(5000f, 5000f);

            yield return Shoot(prefab, o, o + new Vector2(5f, 0f), null, true, "벽 없음: 5칸 앞 몬스터가 맞는다");
            yield return Shoot(prefab, o + new Vector2(0f, 20f), o + new Vector2(5f, 20f), Box(o + new Vector2(2.5f, 20f), new Vector2(1f, 3f), Kind.Static), false, "고정 벽(두께 1) 뒤 몬스터는 맞지 않고 다트는 벽에서 사라진다");
            yield return ShootAnimal(prefab, o + new Vector2(0f, 40f), o + new Vector2(5f, 40f), null, true, "벽 없음: 5칸 앞 동물이 잠든다");
            yield return ShootAnimal(prefab, o + new Vector2(0f, 60f), o + new Vector2(5f, 60f), Box(o + new Vector2(2.5f, 60f), new Vector2(1f, 3f), Kind.Static), false, "고정 벽 뒤 동물은 잠들지 않는다");
            yield return Shoot(prefab, o + new Vector2(0f, 80f), o + new Vector2(30f, 80f), Box(o + new Vector2(15f, 80f), new Vector2(0.05f, 3f), Kind.Static), false, "빠른 다트(속도 300, 한 프레임 10칸)도 두께 0.05 벽을 건너뛰지 않는다", 300f);
            yield return Shoot(prefab, o + new Vector2(0f, 100f), o + new Vector2(5f, 100f), Box(o + new Vector2(2.5f, 100f), new Vector2(1f, 3f), Kind.Trigger), true, "트리거 상자는 벽이 아니다(통과해서 맞는다)");
            yield return Shoot(prefab, o + new Vector2(0f, 120f), o + new Vector2(5f, 120f), Box(o + new Vector2(2.5f, 120f), new Vector2(1f, 3f), Kind.Dynamic), true, "움직이는 리지드바디(플레이어·동물과 같은 Dynamic)는 벽이 아니다(통과해서 맞는다)");
            yield return Shoot(prefab, o + new Vector2(0f, 140f), o + new Vector2(0.5f, 140f), Box(o + new Vector2(0.35f, 140f), new Vector2(0.1f, 3f), Kind.Static), false, "벽 바로 뒤(맞힘 반경 0.3 안) 몬스터도 벽 너머면 맞지 않는다");
            yield return MuzzleInsideWall(prefab, o + new Vector2(0f, 160f));
        }

        string mapName = phase == 0 ? "격자 3x4 시드 1" : "한 줄 시드 1";
        sb.AppendLine($"\n== {(phase == 0 ? 2 : 3)}. 실제 마을 맵 ({mapName})");
        yield return RealMap(prefab);
    }

    private enum Kind { Static, Trigger, Dynamic }

    private static GameObject Box(Vector2 center, Vector2 size, Kind kind)
    {
        var go = new GameObject("TestWall_" + kind);
        go.transform.position = center;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;
        box.isTrigger = kind == Kind.Trigger;
        if (kind == Kind.Dynamic)
        {
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        return go;
    }

    private static MonsterHealth Monster(Vector2 at)
    {
        var go = new GameObject("TestMonster");
        go.transform.position = new Vector3(at.x, 0f, at.y); // 몬스터의 2D 위치는 (x, z)
        return go.AddComponent<MonsterHealth>();
    }

    private static TranquilizerDart Fire(TranquilizerDart prefab, Vector2 from, Vector2 to, float speed = DartSpeed)
    {
        TranquilizerDart dart = Object.Instantiate(prefab, from, Quaternion.identity);
        dart.Launch(to - from, speed, Mathf.Max(Range, (to - from).magnitude + 10f), 0.6f, 4.2f, Damage);
        return dart;
    }

    private static IEnumerator WaitGone(TranquilizerDart dart)
    {
        for (int i = 0; i < 300 && dart != null; i++) yield return null;
    }

    private static IEnumerator Shoot(TranquilizerDart prefab, Vector2 from, Vector2 target, GameObject wall, bool expectHit, string what, float speed = DartSpeed)
    {
        Physics2D.SyncTransforms();
        MonsterHealth m = Monster(target);
        yield return null;
        float before = m.Health;
        TranquilizerDart dart = Fire(prefab, from, target, speed);
        Vector2 lastPos = from;
        for (int i = 0; i < 300 && dart != null; i++) { lastPos = dart.transform.position; yield return null; }
        bool hit = m.Health < before;
        string where = wall != null && !expectHit ? $", 다트가 사라진 곳 x={lastPos.x - from.x:F2}(벽 {wall.transform.position.x - from.x:F2})" : "";
        Check(hit == expectHit && dart == null, $"{what} (체력 {before:0.#} → {m.Health:0.#}{where})");
        Object.Destroy(m.gameObject);
        if (wall != null) Object.Destroy(wall);
        yield return null;
    }

    private static IEnumerator ShootAnimal(TranquilizerDart prefab, Vector2 from, Vector2 target, GameObject wall, bool expectHit, string what)
    {
        Physics2D.SyncTransforms();
        var go = new GameObject("TestAnimal");
        go.transform.position = target;
        AnimalSleep a = go.AddComponent<AnimalSleep>();
        yield return null;
        TranquilizerDart dart = Fire(prefab, from, target);
        yield return WaitGone(dart);
        bool hit = a.IsSleepPending || a.IsAsleep;
        Check(hit == expectHit && dart == null, $"{what} (수면 {(hit ? "걸림" : "안 걸림")})");
        Object.Destroy(go);
        if (wall != null) Object.Destroy(wall);
        yield return null;
    }

    private static IEnumerator MuzzleInsideWall(TranquilizerDart prefab, Vector2 at)
    {
        GameObject wall = Box(at, new Vector2(2f, 2f), Kind.Static);
        Physics2D.SyncTransforms();
        MonsterHealth m = Monster(at + new Vector2(3f, 0f));
        yield return null;
        float before = m.Health;
        TranquilizerDart dart = Fire(prefab, at, at + new Vector2(3f, 0f));
        yield return null;
        yield return null;
        Check(dart == null && m.Health == before, "총구가 벽 안이면 다트가 바로 사라지고 아무도 맞지 않는다(예외 없음)");
        Object.Destroy(m.gameObject);
        Object.Destroy(wall);
        yield return null;
    }

    private static IEnumerator RealMap(TranquilizerDart prefab)
    {
        GameObject mapRoot = GameObject.Find("GeneratedMap");
        if (mapRoot == null) { Check(false, "GeneratedMap이 없다"); yield break; }
        // 실제 몬스터·동물이 다트 경로에 끼지 않게 끈다(씬 파일은 바꾸지 않는다).
        foreach (MonsterHealth mh in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude)) mh.gameObject.SetActive(false);
        foreach (AnimalSleep s in Object.FindObjectsByType<AnimalSleep>(FindObjectsInactive.Exclude)) s.gameObject.SetActive(false);
        Physics2D.SyncTransforms();
        yield return null;

        Transform boundary = mapRoot.transform.Find("MapBoundary");
        var boundaryBoxes = boundary != null ? boundary.GetComponents<BoxCollider2D>().ToList() : new List<BoxCollider2D>();
        var obstacleBoxes = mapRoot.GetComponentsInChildren<BoxCollider2D>()
            .Where(b => !b.isTrigger && b.attachedRigidbody == null && b.transform.parent != null && b.transform.parent.name == "Obstacles").ToList();
        sb.AppendLine($"  맵 경계 상자 {boundaryBoxes.Count}개, 장애물 상자 {obstacleBoxes.Count}개 (레이어 {(boundary != null ? LayerMask.LayerToName(boundary.gameObject.layer) : "-")}, 트리거 아님, 리지드바디 없음)");

        if (TryPickBehind(boundaryBoxes, out Vector2 bFrom, out Vector2 bTarget, out Collider2D bBox))
            yield return Shoot(prefab, bFrom, bTarget, null, false, $"맵 경계 뒤 몬스터는 맞지 않는다 (경계 상자 중심 {(Vector2)bBox.bounds.center})");
        else Check(false, "맵 경계 시험 자리를 찾지 못했다");

        if (TryPickBehind(obstacleBoxes, out Vector2 oFrom, out Vector2 oTarget, out Collider2D oBox))
            yield return Shoot(prefab, oFrom, oTarget, null, false, $"장애물({oBox.name}) 뒤 몬스터는 맞지 않는다");
        else Check(false, "장애물 시험 자리를 찾지 못했다");

        if (TryPickClear(mapRoot, out Vector2 cFrom, out Vector2 cTarget))
            yield return Shoot(prefab, cFrom, cTarget, null, true, "맵 안 벽 없는 곳에서는 4칸 앞 몬스터가 맞는다");
        else Check(false, "벽 없는 시험 자리를 찾지 못했다");
    }

    private static readonly Vector2[] Dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

    // 상자 하나를 골라, 상자 바깥 빈 곳에서 상자 중심을 향해 쏘면 첫 고정 충돌체가 그 상자이고, 상자 건너편에 몬스터를 둔다.
    private static bool TryPickBehind(List<BoxCollider2D> boxes, out Vector2 from, out Vector2 target, out Collider2D box)
    {
        foreach (BoxCollider2D b in boxes.OrderBy(x => x.bounds.center.x).ThenBy(x => x.bounds.center.y))
        {
            Vector2 c = b.bounds.center, e = b.bounds.extents;
            foreach (Vector2 d in Dirs)
            {
                float ext = Mathf.Abs(d.x) * e.x + Mathf.Abs(d.y) * e.y;
                Vector2 f = c + d * (ext + 2f);
                if (Physics2D.OverlapPointAll(f).Any(col => !col.isTrigger && col.attachedRigidbody == null)) continue;
                if (!StaticBlockerLine.TryGetFirstHit(f, c, ~0, out RaycastHit2D hit) || hit.collider != b) continue;
                from = f;
                target = c - d * (ext + 0.2f);
                box = b;
                return true;
            }
        }
        from = target = default;
        box = null;
        return false;
    }

    private static bool TryPickClear(GameObject mapRoot, out Vector2 from, out Vector2 target)
    {
        GameObject player = GameObject.Find("Player");
        Vector2 p = player != null ? (Vector2)player.transform.position : Vector2.zero;
        for (int r = 0; r < 20; r++)
            foreach (Vector2 d in Dirs)
            {
                Vector2 f = p + d * r;
                Vector2 t = f + d * 4f;
                if (Physics2D.OverlapPointAll(f).Any(col => !col.isTrigger && col.attachedRigidbody == null)) continue;
                if (StaticBlockerLine.IsBlocked(f, t + d * 0.5f, ~0)) continue;
                from = f;
                target = t;
                return true;
            }
        from = target = default;
        return false;
    }
}
