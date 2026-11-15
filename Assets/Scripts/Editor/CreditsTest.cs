using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Credits > Run Credits Test. 타이틀의 CREDITS 버튼·크레딧 창을 실제 Play로 확인한다.
/// 검사: 버튼 네 개(PLAY·SETTING·CREDITS·EXIT)가 같은 그림·크기·글씨 크기로 같은 간격(300) 가운데 정렬 / 기존 버튼 연결(OnPlay·OnSetting·OnExit·설정 CLOSE) 그대로 /
/// CREDITS를 누르면 창이 열리고 CREDITS.md 내용(MinZinn·CC BY 4.0·색 보정함·라이선스 링크·출처 확인 필요)이 보이며 주석은 숨김, 글자가 모두 글꼴에 있음 /
/// 게임이 읽는 Resources/Credits/Credits.md가 CREDITS.md와 같음 / 1080p·16:10에서 창과 버튼이 화면 안 / 긴 글은 스크롤됨 /
/// ESC로 닫힘(일시정지 메뉴는 안 열림), 닫기 버튼으로 닫힘 / SETTING은 그대로 열고 닫힘 / 마지막에 PLAY는 그대로 클래스 선택으로.
/// 그림: Logs/Credits/credits_title.png (왼쪽 타이틀, 오른쪽 크레딧 창 / 위 1920x1080, 아래 1920x1200 - 반으로 줄임).
/// 결과: Logs/CreditsTest.txt / batchmode: -executeMethod CreditsTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class CreditsTest
{
    private const string KeyActive = "CRT.Active", KeyDone = "CRT.Done", KeyBatch = "CRT.Batch", KeyPassed = "CRT.Passed";
    private const string ReportPath = "Logs/CreditsTest.txt";
    private const string SheetPath = "Logs/Credits/credits_title.png";
    private static readonly Vector2Int[] Resolutions = { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200) };

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static List<string> failures;
    private static int checks;
    private static readonly List<string> logs = new List<string>();
    private static Keyboard vkb;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static readonly List<Texture2D> shots = new List<Texture2D>();

    static CreditsTest() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

    [MenuItem("Tools/Credits/Run Credits Test")]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[CreditsTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
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
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("CreditsTestKeyboard");
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
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        vkb = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        foreach (Texture2D t in shots) if (t != null) Object.DestroyImmediate(t);
        shots.Clear();
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
            try { SaveSheet(); } catch (System.Exception e) { Debug.LogException(e); failures?.Add("그림 저장 실패: " + e.Message); }
            Finish();
            TearDown();
            routine = null;
            SessionState.SetBool(KeyDone, true);
            EditorApplication.ExitPlaymode();
        }
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) failures.Add(what);
    }

    private static void Finish()
    {
        if (sb == null) return;
        sb.AppendLine($"\n== 콘솔 에러/예외 (Log·Warning 제외): {logs.Count}건");
        foreach (string l in logs.Take(20)) sb.AppendLine("  " + l);
        checks++;
        if (logs.Count > 0) failures.Add($"콘솔 에러/예외 {logs.Count}건");
        sb.AppendLine();
        sb.AppendLine(failures.Count == 0 ? $"결과: 통과 ({checks}/{checks})" : $"결과: 실패 {failures.Count}건 (통과 {checks - failures.Count}/{checks})");
        foreach (string f in failures) sb.AppendLine("  " + f);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
        SessionState.SetBool(KeyPassed, failures.Count == 0);
        Debug.Log("[CreditsTest] " + (failures.Count == 0 ? "통과" : $"실패 {failures.Count}건") + " - " + Path.GetFullPath(ReportPath));
    }

    // ===================== 본 테스트 =====================

    private static IEnumerator Run()
    {
        sb = new StringBuilder();
        failures = new List<string>();
        checks = 0;
        sb.AppendLine($"CreditsTest {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        yield return WaitFrames(5);

        TitleMenu menu = Object.FindAnyObjectByType<TitleMenu>();
        if (menu == null) { Check(false, "TitleMenu 없음"); yield break; }
        // 타이틀 씬의 것만(일시정지 메뉴 등 다른 곳에도 같은 이름의 버튼이 있다)
        Transform Find(string n) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(t => t.name == n && t.gameObject.scene == menu.gameObject.scene);
        Button play = Find("PlayButton")?.GetComponent<Button>(), setting = Find("SettingButton")?.GetComponent<Button>();
        Button credits = Find("CreditsButton")?.GetComponent<Button>(), exit = Find("ExitButton")?.GetComponent<Button>();
        GameObject settingsPanel = Find("SettingsPanel")?.gameObject;
        CreditsPanel panel = menu.Credits;

        // --- 1. 버튼 배치·모양
        sb.AppendLine("\n== 1. 버튼 네 개: 같은 모양·같은 간격");
        Button[] row = { play, setting, credits, exit };
        if (row.Any(b => b == null) || panel == null || settingsPanel == null) { Check(false, $"버튼·창 찾기: PLAY {play != null}, SETTING {setting != null}, CREDITS {credits != null}, EXIT {exit != null}, 크레딧 창 {panel != null}, 설정 창 {settingsPanel != null}"); yield break; }
        RectTransform[] rts = row.Select(b => (RectTransform)b.transform).ToArray();
        string[] labels = row.Select(b => b.GetComponentInChildren<Text>(true).text).ToArray();
        float[] xs = rts.Select(r => r.anchoredPosition.x).ToArray();
        float[] gaps = Enumerable.Range(1, 3).Select(i => xs[i] - xs[i - 1]).ToArray();
        Image refImg = setting.GetComponent<Image>();
        Text refText = setting.GetComponentInChildren<Text>(true);
        bool sameLook = row.All(b =>
        {
            Image img = b.GetComponent<Image>(); Text t = b.GetComponentInChildren<Text>(true); RectTransform r = (RectTransform)b.transform;
            return img.sprite == refImg.sprite && img.type == refImg.type && r.sizeDelta == ((RectTransform)setting.transform).sizeDelta && r.anchoredPosition.y == rts[0].anchoredPosition.y &&
                   t.fontSize == refText.fontSize && t.font == refText.font && t.color == refText.color && b.spriteState.highlightedSprite == setting.spriteState.highlightedSprite &&
                   b.spriteState.pressedSprite == setting.spriteState.pressedSprite && b.transition == setting.transition;
        });
        Check(labels.SequenceEqual(new[] { "PLAY", "SETTING", "CREDITS", "EXIT" }) && gaps.All(g => Mathf.Approximately(g, 300f)) && Mathf.Abs(xs.Sum()) < 0.01f && sameLook,
            $"왼쪽부터 [{string.Join(", ", labels)}], x [{string.Join(", ", xs.Select(x => x.ToString("F0")))}], 간격 [{string.Join(", ", gaps.Select(g => g.ToString("F0")))}], 같은 그림·크기·글씨 {sameLook}");

        // --- 2. 버튼 연결(기존 그대로)
        sb.AppendLine("\n== 2. 버튼 연결");
        Button settingClose = settingsPanel.transform.Find("Window/CloseButton")?.GetComponent<Button>();
        Button creditsClose = panel.transform.Find("Window/CloseButton")?.GetComponent<Button>();
        CheckCall(play, menu, "OnPlay", "PLAY");
        CheckCall(setting, menu, "OnSetting", "SETTING");
        CheckCall(exit, menu, "OnExit", "EXIT");
        CheckCall(settingClose, menu, "OnCloseSetting", "설정 창 CLOSE");
        CheckCall(credits, menu, "OnCredits", "CREDITS");
        CheckCall(creditsClose, menu, "OnCloseCredits", "크레딧 창 CLOSE");

        // --- 3. 크레딧 내용
        sb.AppendLine("\n== 3. 크레딧 창 열기·내용");
        Check(!panel.IsOpen && !settingsPanel.activeSelf, $"처음에는 크레딧 창·설정 창 닫힘 (크레딧 {panel.IsOpen}, 설정 {settingsPanel.activeSelf})");
        credits.onClick.Invoke();
        yield return WaitFrames(2);
        string body = panel.Body != null ? panel.Body.text : "";
        string plain = Regex.Replace(body, "<[^>]+>", "");
        string source = File.Exists(CreditsSync.SourcePath) ? File.ReadAllText(CreditsSync.SourcePath) : null;
        string res = File.Exists(CreditsSync.ResourceFile) ? File.ReadAllText(CreditsSync.ResourceFile) : null;
        Check(panel.IsOpen && panel.gameObject.activeInHierarchy, $"CREDITS를 누르면 크레딧 창이 열림 ({panel.IsOpen})");
        Check(source != null && source == res && source == CreditsPanel.LoadMarkdown(),
            $"게임이 읽는 {CreditsSync.ResourceFile}이 {CreditsSync.SourcePath}와 같음 (CREDITS.md {source?.Length ?? -1}자, Resources {res?.Length ?? -1}자)");
        string[] must = { "Pixel Vehicles", "MinZinn", "CC BY 4.0", "색 보정함", "https://creativecommons.org/licenses/by/4.0/", "https://minzinn.itch.io/pixelvehicles",
                          "Elthen", "Cainos", "dobo_ui", "Noto Sans KR", "Liberation Sans", "출처 확인 필요" };
        var missingWords = must.Where(w => !plain.Contains(w)).ToList();
        int unknown = Regex.Matches(plain, "출처 확인 필요").Count;
        Check(missingWords.Count == 0 && unknown >= 4, $"크레딧 글에 필요한 항목이 모두 있음 (빠진 것 [{string.Join(", ", missingWords)}]), \"출처 확인 필요\" {unknown}곳");
        Check(!plain.Contains("<!--") && !plain.Contains("Zerie") && !plain.Contains("**") && !plain.Contains("## "),
            "주석(유력 후보 메모)과 마크다운 기호는 화면에 안 보임");
        bool fontOk = UIFont.Font.HasCharacters(plain.Replace("\n", "").Replace("\r", ""), out uint[] missing, true, true);
        Check(fontOk, $"크레딧 글자 {plain.Distinct().Count()}종 중 글꼴에 없는 글자: [{(missing == null ? "" : string.Concat(missing.Select(c => char.ConvertFromUtf32((int)c))))}]");

        // --- 4. 화면 안·스크롤 + 그림
        sb.AppendLine("\n== 4. 1080p·16:10 화면 배치");
        ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);
        RectTransform window = (RectTransform)panel.transform.Find("Window");
        foreach (Vector2Int r in Resolutions)
        {
            using (var shot = new UiScreenShot(r))
            {
                panel.Close();
                yield return WaitFrames(1);
                shot.Refresh();
                Rect rowRect = Union(rts.Select(shot.ScreenRect));
                bool rowInside = Inside(rowRect, r);
                shots.Add(Half(shot.Render()));
                panel.Open();
                yield return WaitFrames(1);
                shot.Refresh();
                Canvas.ForceUpdateCanvases();
                Rect win = shot.ScreenRect(window);
                Rect view = shot.ScreenRect(scroll.viewport);
                float contentH = scroll.content.rect.height, viewH = scroll.viewport.rect.height;
                Check(rowInside && Inside(win, r) && Mathf.Approximately(scroll.verticalNormalizedPosition, 1f),
                    $"{r.x}x{r.y}: 버튼 줄 {UiScreenShot.Fmt(rowRect, r.y)} 화면 안 {rowInside}, 크레딧 창 {UiScreenShot.Fmt(win, r.y)} ({win.width:F0}x{win.height:F0}px) 화면 안 {Inside(win, r)}, 글 {contentH:F0} / 보이는 칸 {viewH:F0}, 맨 위에서 시작");
                shots.Add(Half(shot.Render()));
                if (contentH > viewH + 1f)
                {
                    scroll.verticalNormalizedPosition = 0f;
                    Canvas.ForceUpdateCanvases();
                    float moved = scroll.content.anchoredPosition.y;
                    Check(moved > contentH - viewH - 2f && scroll.verticalScrollbar != null && scroll.verticalScrollbar.gameObject.activeInHierarchy,
                        $"{r.x}x{r.y}: 글이 보이는 칸보다 길면 끝까지 스크롤됨 (내려간 거리 {moved:F0} / {contentH - viewH:F0}), 스크롤바 보임");
                    scroll.verticalNormalizedPosition = 1f;
                }
            }
        }

        // --- 5. 닫기
        sb.AppendLine("\n== 5. 닫기");
        if (!panel.IsOpen) panel.Open();
        yield return WaitFrames(2);
        yield return Press(Key.Escape);
        Check(!panel.IsOpen && !PauseMenu.IsPaused, $"ESC로 크레딧 창이 닫힘 (열림 {panel.IsOpen}), 일시정지 메뉴는 안 열림 ({PauseMenu.IsPaused})");
        credits.onClick.Invoke();
        yield return WaitFrames(2);
        bool reopened = panel.IsOpen;
        creditsClose.onClick.Invoke();
        yield return WaitFrames(2);
        Check(reopened && !panel.IsOpen, $"다시 열고(열림 {reopened}) 닫기 버튼으로 닫힘 (열림 {panel.IsOpen})");
        yield return Press(Key.Escape);
        Check(!panel.IsOpen && !PauseMenu.IsPaused && SceneManager.GetActiveScene().name == "TitleScene", "닫힌 뒤 ESC는 아무 일도 안 함");

        // --- 6. 다른 버튼은 그대로
        sb.AppendLine("\n== 6. 다른 버튼");
        setting.onClick.Invoke();
        yield return WaitFrames(2);
        bool settingOpen = settingsPanel.activeSelf && !panel.IsOpen;
        settingClose.onClick.Invoke();
        yield return WaitFrames(2);
        Check(settingOpen && !settingsPanel.activeSelf, $"SETTING은 그대로 설정 창을 열고(열림 {settingOpen}) CLOSE로 닫음 (열림 {settingsPanel.activeSelf})");
        play.onClick.Invoke();
        float until = Time.realtimeSinceStartup + 20f;
        while (SceneManager.GetActiveScene().name != "ClassSelectScene" && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(5);
        Check(SceneManager.GetActiveScene().name == "ClassSelectScene", $"PLAY는 그대로 클래스 선택 씬으로 ({SceneManager.GetActiveScene().name})");
    }

    // ===================== 도우미 =====================

    private static void CheckCall(Button b, Object target, string method, string what)
    {
        if (b == null) { Check(false, $"{what}: 버튼 없음"); return; }
        UnityEventBase e = b.onClick;
        int n = e.GetPersistentEventCount();
        var calls = Enumerable.Range(0, n).Select(i => $"{(e.GetPersistentTarget(i) != null ? e.GetPersistentTarget(i).GetType().Name : "null")}.{e.GetPersistentMethodName(i)}").ToList();
        Check(n == 1 && e.GetPersistentTarget(0) == target && e.GetPersistentMethodName(0) == method, $"{what} → [{string.Join(", ", calls)}] (기대 TitleMenu.{method})");
    }

    private static bool Inside(Rect r, Vector2Int res) => r.xMin >= 0f && r.yMin >= 0f && r.xMax <= res.x && r.yMax <= res.y;

    private static Rect Union(IEnumerable<Rect> rs)
    {
        Rect u = rs.First();
        foreach (Rect r in rs) u = Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax));
        return u;
    }

    private static Texture2D Half(Texture2D src)
    {
        var dst = new Texture2D(src.width / 2, src.height / 2, TextureFormat.RGB24, false);
        Color32[] s = src.GetPixels32(), d = new Color32[dst.width * dst.height];
        for (int y = 0; y < dst.height; y++)
            for (int x = 0; x < dst.width; x++)
            {
                Color32 a = s[2 * y * src.width + 2 * x], b = s[2 * y * src.width + 2 * x + 1], c = s[(2 * y + 1) * src.width + 2 * x], e = s[(2 * y + 1) * src.width + 2 * x + 1];
                d[y * dst.width + x] = new Color32((byte)((a.r + b.r + c.r + e.r) / 4), (byte)((a.g + b.g + c.g + e.g) / 4), (byte)((a.b + b.b + c.b + e.b) / 4), 255);
            }
        dst.SetPixels32(d);
        dst.Apply();
        Object.DestroyImmediate(src);
        return dst;
    }

    // 2열(타이틀 | 크레딧 창) x 2줄(1080p | 16:10), 칸 사이 4px 검은 줄
    private static void SaveSheet()
    {
        if (shots.Count != 4) return;
        const int gap = 4;
        int w = shots[0].width, h0 = shots[0].height, h1 = shots[2].height;
        var sheet = new Texture2D(w * 2 + gap, h0 + h1 + gap, TextureFormat.RGB24, false);
        sheet.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), sheet.width * sheet.height).ToArray());
        sheet.SetPixels32(0, h1 + gap, w, h0, shots[0].GetPixels32());
        sheet.SetPixels32(w + gap, h1 + gap, w, h0, shots[1].GetPixels32());
        sheet.SetPixels32(0, 0, w, h1, shots[2].GetPixels32());
        sheet.SetPixels32(w + gap, 0, w, h1, shots[3].GetPixels32());
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
        File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sb.AppendLine($"\n== 그림 {Path.GetFullPath(SheetPath)} (왼쪽 타이틀, 오른쪽 크레딧 창 / 위 1920x1080, 아래 1920x1200 - 반으로 줄임)");
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
}
