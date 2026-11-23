using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > UI > Run Volume Settings Test. 타이틀 설정 창의 소리 크기 조절(VolumeSettingsPanel·GameVolume·AudioManager)을 실제 Play로 확인한다.
/// 1회차 Play(처음 실행 - 저장값 없음):
///  1. 처음 값 100%/100%, 믹서 볼륨이 믹서에 저장된 원래 값(BGM -8, SFX·UI +3dB) 그대로
///  2. SETTING으로 창을 열면 슬라이더 100·글자 "100%"
///  3. 슬라이더를 40/70으로 움직이면 GameVolume 0.4/0.7, 글자 "40%"/"70%", 믹서 BGM = 원래+20log10(0.4), SFX·UI = 원래+20log10(0.7)
///  4. 실제 소리: 오디오 출력(AudioListener)의 크기(RMS)를 배경음(트럭 배경음)·효과음(폭탄 폭발음)마다 100%·변경값·0%에서 잰다 - 비율이 슬라이더 값과 맞는지
///  5. ESC로 창이 닫히고(일시정지 메뉴는 안 열림) 닫을 때 PlayerPrefs에 저장, 다시 열면 값 유지, CLOSE 버튼으로도 닫힘
///  6. 그림: 1920x1080 기본값(왼쪽)·슬라이더를 움직인 상태(오른쪽) → Logs/FinalCheck/settings_volume.png
/// 2회차 Play(껐다 켠 것과 같이 새로 시작): 저장된 40/70을 다시 읽어 시작부터 믹서에 적용, 창을 열면 슬라이더 40/70.
/// 끝나면 테스트 전 PlayerPrefs 값(없었으면 없음)으로 되돌린다.
/// 결과: Logs/VolumeSettingsTest.txt / batchmode: -executeMethod VolumeSettingsTest.RunBatch (통과면 0 아니면 1)
/// </summary>
[InitializeOnLoad]
public static class VolumeSettingsTest
{
    // "VST."는 VisionSizeTest가 쓰므로 겹치지 않게 "VolST."를 쓴다.
    private const string KeyActive = "VolST.Active", KeyPhase = "VolST.Phase", KeyBatch = "VolST.Batch", KeyFails = "VolST.Fails";
    private const string KeyHadMusic = "VolST.HadMusic", KeyHadSfx = "VolST.HadSfx", KeyOldMusic = "VolST.OldMusic", KeyOldSfx = "VolST.OldSfx";
    private const string ReportPath = "Logs/VolumeSettingsTest.txt";
    private const string ShotPath = "Logs/FinalCheck/settings_volume.png";
    private const float MusicSet = 40f, SfxSet = 70f;
    private static readonly Vector2Int ShotRes = new Vector2Int(1920, 1080);
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> nested = new Stack<IEnumerator>();
    private static StringBuilder sb;
    private static int fails;
    private static Keyboard vkb;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
    private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
    private static readonly List<Texture2D> shots = new List<Texture2D>();

    static VolumeSettingsTest() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

    [MenuItem("Tools/UI/Run Volume Settings Test")]
    public static void Run() => Begin(false);

    public static void RunBatch() => Begin(true);

    /// <summary>창 모드 에디터(오디오 장치 있음)에서 -executeMethod로 실행할 때 - 끝나면 에디터를 닫는다.</summary>
    public static void RunAndQuit() => Begin(true);

    private static void Begin(bool batch)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VolumeSettingsTest] Play 모드를 끈 뒤 실행해야 한다."); return; }
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyBatch, batch);
        SessionState.SetInt(KeyPhase, 1);
        SessionState.SetInt(KeyFails, 0);
        // 테스트 전 저장값을 기억했다가 끝나면 되돌린다. 1회차는 저장값이 없는 처음 실행으로 시작한다.
        SessionState.SetBool(KeyHadMusic, PlayerPrefs.HasKey(GameVolume.MusicKey));
        SessionState.SetBool(KeyHadSfx, PlayerPrefs.HasKey(GameVolume.SfxKey));
        SessionState.SetFloat(KeyOldMusic, PlayerPrefs.GetFloat(GameVolume.MusicKey, 1f));
        SessionState.SetFloat(KeyOldSfx, PlayerPrefs.GetFloat(GameVolume.SfxKey, 1f));
        PlayerPrefs.DeleteKey(GameVolume.MusicKey);
        PlayerPrefs.DeleteKey(GameVolume.SfxKey);
        PlayerPrefs.Save();
        File.WriteAllText(ReportPath, "소리 크기 조절 테스트 (" + System.DateTime.Now + ")\n", new UTF8Encoding(false));
        EnterTitle();
    }

    private static void EnterTitle()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScene.unity", OpenSceneMode.Single);
        EditorTestState.ClearStaleSingletons();
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        int phase = SessionState.GetInt(KeyPhase, 1);
        if (change == PlayModeStateChange.EnteredPlayMode && (phase == 1 || phase == 2))
        {
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 30f;
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vkb = InputSystem.AddDevice<Keyboard>("VolumeSettingsTestKeyboard");
            vkb.MakeCurrent();
            sb = new StringBuilder();
            fails = SessionState.GetInt(KeyFails, 0);
            nested.Clear();
            routine = phase == 1 ? Phase1() : Phase2();
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            if (phase == 2)
            {
                // 1회차 Play가 끝난 뒤 에디터에서도 저장값이 남아 있는지 보고, 2회차(껐다 켠 것)를 시작한다.
                float m = PlayerPrefs.GetFloat(GameVolume.MusicKey, -1f), s = PlayerPrefs.GetFloat(GameVolume.SfxKey, -1f);
                bool ok = Mathf.Approximately(m, MusicSet / 100f) && Mathf.Approximately(s, SfxSet / 100f);
                AppendReport($"\n== Play를 끈 뒤 PlayerPrefs: 배경음 {m:0.##}, 효과음 {s:0.##}\n  [{(ok ? "OK" : "실패")}] 저장값이 0.4 / 0.7로 남아 있다\n");
                if (!ok) SessionState.SetInt(KeyFails, SessionState.GetInt(KeyFails, 0) + 1);
                EnterTitle();
            }
            else if (phase == 3)
            {
                // 테스트 전 저장값으로 되돌린다.
                if (SessionState.GetBool(KeyHadMusic, false)) PlayerPrefs.SetFloat(GameVolume.MusicKey, SessionState.GetFloat(KeyOldMusic, 1f)); else PlayerPrefs.DeleteKey(GameVolume.MusicKey);
                if (SessionState.GetBool(KeyHadSfx, false)) PlayerPrefs.SetFloat(GameVolume.SfxKey, SessionState.GetFloat(KeyOldSfx, 1f)); else PlayerPrefs.DeleteKey(GameVolume.SfxKey);
                PlayerPrefs.Save();
                int f = SessionState.GetInt(KeyFails, 0);
                AppendReport($"\nPlayerPrefs를 테스트 전 값으로 되돌림\n\n{(f == 0 ? "결과: 통과" : $"결과: 실패 {f}건")}\n");
                Debug.Log($"[VolumeSettingsTest] {(f == 0 ? "통과" : "실패")} - {ReportPath}");
                SessionState.SetBool(KeyActive, false);
                if (SessionState.GetBool(KeyBatch, false)) EditorApplication.Exit(f == 0 ? 0 : 1);
            }
        }
    }

    private static void AppendReport(string s) => File.AppendAllText(ReportPath, s, new UTF8Encoding(false));

    private static void TearDown()
    {
        Time.captureDeltaTime = 0f;
        Time.timeScale = 1f;
        if (vkb != null) InputSystem.RemoveDevice(vkb);
        vkb = null;
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
        InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
        foreach (Texture2D t in shots) if (t != null) Object.DestroyImmediate(t);
        shots.Clear();
    }

    private static void Step()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Step; TearDown(); return; }
        bool more;
        try { more = MoveNextNested(); }
        catch (System.Exception e) { Debug.LogException(e); Check(false, "예외: " + e.Message); more = false; }
        if (more) return;
        EditorApplication.update -= Step;
        AppendReport(sb.ToString());
        SessionState.SetInt(KeyFails, fails);
        SessionState.SetInt(KeyPhase, SessionState.GetInt(KeyPhase, 1) + 1);
        TearDown();
        EditorApplication.ExitPlaymode();
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

    private static void Check(bool ok, string what)
    {
        sb.AppendLine($"  [{(ok ? "OK" : "실패")}] {what}");
        if (!ok) fails++;
    }

    private static IEnumerator WaitFrames(int n) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }
    private static IEnumerator WaitRealtime(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(vkb, new KeyboardState(key));
        yield return WaitFrames(2);
        InputSystem.QueueStateEvent(vkb, new KeyboardState());
        yield return WaitFrames(2);
    }

    private static float Db(AudioMixer mixer, string param) => mixer.GetFloat(param, out float v) ? v : float.NaN;
    private static bool Near(float a, float b, float tol = 0.05f) => Mathf.Abs(a - b) <= tol;
    private static float Expected(string param, float linear) => (AudioManager.TryGetMixerBaseDecibels(param, out float b) ? b : float.NaN) + GameVolume.ToDecibels(linear);

    private static GameObject SettingsPanel()
    {
        TitleMenu tm = Object.FindAnyObjectByType<TitleMenu>(FindObjectsInactive.Include);
        return tm != null ? typeof(TitleMenu).GetField("settingsPanel", Any)?.GetValue(tm) as GameObject : null;
    }

    private static Button TitleButton(string name) =>
        Object.FindObjectsByType<Button>(FindObjectsInactive.Include).FirstOrDefault(b => b.name == name && b.gameObject.scene.name == "TitleScene");

    private static string MixerLine(AudioMixer mixer) =>
        $"믹서 BGM {Db(mixer, AudioManager.MusicVolumeParam):0.00}dB, SFX {Db(mixer, AudioManager.SfxVolumeParam):0.00}dB, UI {Db(mixer, AudioManager.UiVolumeParam):0.00}dB";

    // ---------------- 1회차 ----------------
    private static IEnumerator Phase1()
    {
        yield return WaitFrames(10);
        sb.AppendLine("\n== 1회차 Play (저장값 없음 = 처음 실행)");
        AudioManager am = AudioManager.Instance;
        AudioMixer mixer = am != null ? am.Mixer : null;
        Check(am != null && mixer != null, "AudioManager와 믹서가 있다");
        if (mixer == null) yield break;

        // 1. 처음 값
        bool hasBase = AudioManager.TryGetMixerBaseDecibels(AudioManager.MusicVolumeParam, out float bgmBase)
                       & AudioManager.TryGetMixerBaseDecibels(AudioManager.SfxVolumeParam, out float sfxBase)
                       & AudioManager.TryGetMixerBaseDecibels(AudioManager.UiVolumeParam, out float uiBase);
        Check(hasBase && Near(bgmBase, -8f) && Near(sfxBase, 3f) && Near(uiBase, 3f), $"믹서에 저장된 원래 값: BGM {bgmBase:0.##}, SFX {sfxBase:0.##}, UI {uiBase:0.##}dB (믹서 에셋 -8 / 3 / 3)");
        Check(Mathf.Approximately(GameVolume.Music, 1f) && Mathf.Approximately(GameVolume.Sfx, 1f), $"처음 실행 기본값 배경음 {GameVolume.Music:0.##}, 효과음 {GameVolume.Sfx:0.##} (= 100%)");
        Check(Near(Db(mixer, AudioManager.MusicVolumeParam), bgmBase) && Near(Db(mixer, AudioManager.SfxVolumeParam), sfxBase) && Near(Db(mixer, AudioManager.UiVolumeParam), uiBase),
            "100%면 믹서 볼륨이 원래 값 그대로 - " + MixerLine(mixer));

        // 2. 창 열기
        GameObject panel = SettingsPanel();
        VolumeSettingsPanel vsp = panel != null ? panel.GetComponent<VolumeSettingsPanel>() : null;
        Check(vsp != null && vsp.MusicSlider != null && vsp.SfxSlider != null, "설정 창에 VolumeSettingsPanel과 슬라이더 두 개가 연결돼 있다" +
            (vsp == null ? $" (창 {(panel != null ? panel.name + "/" + panel.scene.name : "null")}, 컴포넌트 {(panel != null ? string.Join(",", panel.GetComponents<Component>().Select(c => c == null ? "Missing" : c.GetType().Name)) : "-")})" : ""));
        if (vsp == null) yield break;
        Button settingButton = TitleButton("SettingButton");
        settingButton.onClick.Invoke();
        yield return WaitFrames(3);
        Check(panel.activeInHierarchy, "SETTING 버튼으로 창이 열린다");
        Check(vsp.MusicSlider.value == 100f && vsp.SfxSlider.value == 100f && vsp.MusicValueText.text == "100%" && vsp.SfxValueText.text == "100%",
            $"슬라이더 {vsp.MusicSlider.value}/{vsp.SfxSlider.value}, 글자 {vsp.MusicValueText.text}/{vsp.SfxValueText.text}, 범위 {vsp.MusicSlider.minValue}~{vsp.MusicSlider.maxValue} 정수 {vsp.MusicSlider.wholeNumbers}");
        foreach (Texture2D tex in panel.GetComponentsInChildren<Image>(true).Where(i => i.sprite != null && i.sprite.texture.name.StartsWith("volume_")).Select(i => i.sprite.texture).Distinct())
        {
            string path = AssetDatabase.GetAssetPath(tex);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(ti.filterMode == FilterMode.Point && ti.textureCompression == TextureImporterCompression.Uncompressed && !ti.mipmapEnabled,
                $"도트 규칙 {Path.GetFileName(path)}: 필터 {ti.filterMode}, 압축 {ti.textureCompression}, 밉맵 {ti.mipmapEnabled}");
        }
        yield return Shot();

        // 4-1. 실제 소리 크기(100%)
        sb.AppendLine("\n== 실제 출력 크기 (AudioListener 출력 RMS, 같은 클립의 같은 구간)");
        {
            yield return WaitRealtime(0.5f);
            float floor = 0f; int k = 0; float until = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < until) { floor += OutputRms(); k++; yield return WaitRealtime(0.05f); }
            var playing = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include).Where(a => a.isPlaying).Select(a => a.name + ":" + (a.clip != null ? a.clip.name : "-") + ":" + (a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "-"));
            sb.AppendLine($"  아무것도 틀지 않았을 때 출력 RMS {floor / Mathf.Max(1, k):0.00000}, 재생 중: {string.Join(", ", playing)}");
        }
        float music100 = 0f, sfx100 = 0f, real100 = 0f;
        yield return MeasureMusic(am, v => music100 = v, true);
        yield return MeasureMusic(am, v => real100 = v, false);
        yield return MeasureSfx(am, v => sfx100 = v);

        // 3. 슬라이더 움직이기(UI가 값을 바꾸는 것과 같이 value를 바꿔 onValueChanged가 난다)
        int previewBefore = vsp.PreviewCount;
        vsp.MusicSlider.value = MusicSet;
        vsp.SfxSlider.value = SfxSet;
        yield return WaitFrames(3);
        Check(vsp.PreviewCount == previewBefore + 1, $"효과음 슬라이더를 움직이면 미리듣기 효과음이 한 번 난다({vsp.PreviewCount - previewBefore}번)");
        Check(Camera.main != null && Camera.main.GetComponent<AudioListener>() != null, "타이틀 메인 카메라에 AudioListener가 있다(소리가 들린다)");
        yield return WaitRealtime(3.0f); // 미리듣기 효과음이 끝난 뒤 잰다
        Check(Mathf.Approximately(GameVolume.Music, 0.4f) && Mathf.Approximately(GameVolume.Sfx, 0.7f) && vsp.MusicValueText.text == "40%" && vsp.SfxValueText.text == "70%",
            $"슬라이더 40/70 → GameVolume {GameVolume.Music:0.##}/{GameVolume.Sfx:0.##}, 글자 {vsp.MusicValueText.text}/{vsp.SfxValueText.text}");
        float eB = Expected(AudioManager.MusicVolumeParam, 0.4f), eS = Expected(AudioManager.SfxVolumeParam, 0.7f), eU = Expected(AudioManager.UiVolumeParam, 0.7f);
        Check(Near(Db(mixer, AudioManager.MusicVolumeParam), eB) && Near(Db(mixer, AudioManager.SfxVolumeParam), eS) && Near(Db(mixer, AudioManager.UiVolumeParam), eU),
            $"믹서에 바로 적용: {MixerLine(mixer)} (기대 BGM {eB:0.00}, SFX {eS:0.00}, UI {eU:0.00})");

        // 4-2. 실제 소리 크기(40/70%)
        float music40 = 0f, sfx70 = 0f, real40 = 0f;
        yield return MeasureMusic(am, v => music40 = v, true);
        yield return MeasureMusic(am, v => real40 = v, false);
        yield return MeasureSfx(am, v => sfx70 = v);
        bool audioOn = music100 > 1e-5f && sfx100 > 1e-5f;
        sb.AppendLine($"  배경음 경로(음악 소스 → BGM 그룹, 일정한 440Hz 시험음) RMS 100% {music100:0.00000} → 40% {music40:0.00000} (비율 {(music100 > 0 ? music40 / music100 : 0):0.000}, 기대 0.400)");
        sb.AppendLine($"  참고: 실제 트럭 배경음(cozy ambient 10~12초 구간 평균) RMS 100% {real100:0.00000} → 40% {real40:0.00000} (비율 {(real100 > 0 ? real40 / real100 : 0):0.000}) - 곡 자체의 크기가 계속 바뀌어 대략값");
        sb.AppendLine($"  효과음 RMS 100% {sfx100:0.00000} → 70% {sfx70:0.00000} (비율 {(sfx100 > 0 ? sfx70 / sfx100 : 0):0.000}, 기대 0.700)");
        if (audioOn)
        {
            Check(Mathf.Abs(music40 / music100 - 0.4f) < 0.03f, "배경음 실제 출력이 40%로 줄었다");
            Check(Mathf.Abs(sfx70 / sfx100 - 0.7f) < 0.03f, "효과음 실제 출력이 70%로 줄었다");
        }
        else sb.AppendLine($"  (오디오 출력이 0 - 이 실행 환경에 오디오 출력이 없다(batchmode {Application.isBatchMode}). 믹서 값으로만 확인)");

        yield return Shot();
        SaveSheet();

        // 0%: 소리가 꺼진다
        vsp.MusicSlider.value = 0f; vsp.SfxSlider.value = 0f;
        yield return WaitFrames(3);
        float music0 = 0f, sfx0 = 0f;
        yield return MeasureMusic(am, v => music0 = v);
        yield return MeasureSfx(am, v => sfx0 = v);
        Check(Near(Db(mixer, AudioManager.MusicVolumeParam), -80f) && Near(Db(mixer, AudioManager.SfxVolumeParam), -80f),
            $"0%면 믹서 -80dB(끔) - {MixerLine(mixer)}, 출력 RMS 배경음 {music0:0.00000}, 효과음 {sfx0:0.00000}");
        if (audioOn) Check(music0 < music100 * 0.001f && sfx0 < sfx100 * 0.001f, "0%면 실제 출력이 사라진다");
        vsp.MusicSlider.value = MusicSet; vsp.SfxSlider.value = SfxSet;
        yield return WaitFrames(3);

        // 5. ESC로 닫기 → 저장
        sb.AppendLine("\n== 닫기·저장");
        yield return Press(Key.Escape);
        Check(!panel.activeInHierarchy && !PauseMenu.IsPaused, $"ESC로 창이 닫힌다(열림 {panel.activeInHierarchy}), 일시정지 메뉴는 안 열린다({PauseMenu.IsPaused})");
        Check(Mathf.Approximately(PlayerPrefs.GetFloat(GameVolume.MusicKey, -1f), 0.4f) && Mathf.Approximately(PlayerPrefs.GetFloat(GameVolume.SfxKey, -1f), 0.7f),
            $"닫을 때 저장: PlayerPrefs 배경음 {PlayerPrefs.GetFloat(GameVolume.MusicKey, -1f):0.##}, 효과음 {PlayerPrefs.GetFloat(GameVolume.SfxKey, -1f):0.##}");
        settingButton.onClick.Invoke();
        yield return WaitFrames(3);
        Check(panel.activeInHierarchy && vsp.MusicSlider.value == MusicSet && vsp.SfxSlider.value == SfxSet, $"다시 열면 값 유지 {vsp.MusicSlider.value}/{vsp.SfxSlider.value}");
        Button close = panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "CloseButton");
        close.onClick.Invoke();
        yield return WaitFrames(3);
        Check(!panel.activeInHierarchy, "CLOSE 버튼으로도 닫힌다");
        // 크레딧 ESC는 그대로
        TitleMenu tm = Object.FindAnyObjectByType<TitleMenu>();
        tm.OnCredits();
        yield return WaitFrames(3);
        yield return Press(Key.Escape);
        Check(tm.Credits != null && !tm.Credits.gameObject.activeSelf && !panel.activeInHierarchy, "크레딧 창도 ESC로 그대로 닫히고 설정 창은 안 열린다");
    }

    // ---------------- 2회차 ----------------
    private static IEnumerator Phase2()
    {
        yield return WaitFrames(10);
        sb.AppendLine("\n== 2회차 Play (껐다 켠 것과 같이 새로 시작)");
        AudioManager am = AudioManager.Instance;
        AudioMixer mixer = am.Mixer;
        Check(Mathf.Approximately(GameVolume.Music, 0.4f) && Mathf.Approximately(GameVolume.Sfx, 0.7f), $"저장값을 다시 읽음: 배경음 {GameVolume.Music:0.##}, 효과음 {GameVolume.Sfx:0.##}");
        float eB = Expected(AudioManager.MusicVolumeParam, 0.4f), eS = Expected(AudioManager.SfxVolumeParam, 0.7f);
        Check(Near(Db(mixer, AudioManager.MusicVolumeParam), eB) && Near(Db(mixer, AudioManager.SfxVolumeParam), eS),
            $"창을 열기 전, 시작부터 믹서에 적용: {MixerLine(mixer)} (기대 BGM {eB:0.00}, SFX {eS:0.00})");
        GameObject panel = SettingsPanel();
        VolumeSettingsPanel vsp = panel.GetComponent<VolumeSettingsPanel>();
        TitleButton("SettingButton").onClick.Invoke();
        yield return WaitFrames(3);
        Check(vsp.MusicSlider.value == MusicSet && vsp.SfxSlider.value == SfxSet && vsp.MusicValueText.text == "40%" && vsp.SfxValueText.text == "70%",
            $"창을 열면 슬라이더 {vsp.MusicSlider.value}/{vsp.SfxSlider.value}, 글자 {vsp.MusicValueText.text}/{vsp.SfxValueText.text}");
        // 다른 씬(트럭)에서도 같은 믹서 값
        GameManager.Instance.ResetGame();
        GameManager.Instance.UnlockClassFree(PlayerClass.Scout);
        GameManager.Instance.SelectClass(PlayerClass.Scout);
        GameManager.Instance.CompleteStartingClassSelection();
        SceneManager.LoadScene("TruckScene");
        float until = Time.realtimeSinceStartup + 20f;
        while (SceneManager.GetActiveScene().name != "TruckScene" && Time.realtimeSinceStartup < until) yield return null;
        yield return WaitFrames(10);
        AudioSource music = (AudioSource)typeof(AudioManager).GetField("musicSource", Any).GetValue(AudioManager.Instance);
        Check(Near(Db(mixer, AudioManager.MusicVolumeParam), eB) && music != null && music.isPlaying && music.outputAudioMixerGroup != null && music.outputAudioMixerGroup.name == "BGM",
            $"트럭 씬: 배경음 {(music != null && music.clip != null ? music.clip.name : "-")} 재생 {music != null && music.isPlaying}, 그룹 {(music != null && music.outputAudioMixerGroup != null ? music.outputAudioMixerGroup.name : "-")}, {MixerLine(mixer)}");
        var sources = AudioManager.Instance.GetComponents<AudioSource>();
        Check(sources.All(s => s.outputAudioMixerGroup != null), "AudioManager의 모든 소리 출력이 믹서 그룹을 거친다: " + string.Join(", ", sources.Select(s => s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "없음")));
        var stray = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include).Where(s => s.GetComponentInParent<AudioManager>() == null).ToList();
        Check(stray.All(s => s.outputAudioMixerGroup != null), $"AudioManager 밖의 AudioSource {stray.Count}개(모두 믹서 그룹을 거친다)");
    }

    // ---------------- 실제 출력 크기 ----------------
    private static float OutputRms()
    {
        var data = new float[4096];
        float sum = 0f; int n = 0;
        for (int ch = 0; ch < 2; ch++)
        {
            AudioListener.GetOutputData(data, ch);
            foreach (float d in data) { sum += d * d; n++; }
        }
        return Mathf.Sqrt(sum / Mathf.Max(1, n));
    }

    private static AudioClip toneClip;

    // 음악 소스(BGM 그룹)로 소리를 틀고 출력 크기를 잰다. tone이면 크기가 일정한 440Hz 시험음(정확한 비율용),
    // 아니면 실제 트럭 배경음을 같은 위치(10초)부터 2초 동안 재 평균한다. 잰 뒤 멈춘다.
    private static IEnumerator MeasureMusic(AudioManager am, System.Action<float> result, bool tone = true)
    {
        AudioSource music = (AudioSource)typeof(AudioManager).GetField("musicSource", Any).GetValue(am);
        AudioClip clip = tone ? Tone() : (AudioClip)typeof(AudioManager).GetField("truckMusicClip", Any).GetValue(am);
        float vol = tone ? 1f : (float)typeof(AudioManager).GetField("truckMusicVolume", Any).GetValue(am);
        if (music == null || clip == null) { result(0f); yield break; }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        music.Stop(); music.clip = clip; music.volume = vol; music.loop = true;
        music.time = tone ? 0f : Mathf.Min(10f, clip.length * 0.5f);
        music.Play();
        yield return WaitRealtime(0.5f);
        float sum = 0f; int k = 0;
        float until = Time.realtimeSinceStartup + (tone ? 0.5f : 2f);
        while (Time.realtimeSinceStartup < until) { sum += OutputRms(); k++; yield return WaitRealtime(0.05f); }
        music.Stop();
        yield return WaitRealtime(0.3f);
        result(sum / Mathf.Max(1, k));
    }

    private static AudioClip Tone()
    {
        if (toneClip != null) return toneClip;
        int rate = 48000, n = rate * 2;
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = 0.25f * Mathf.Sin(2f * Mathf.PI * 440f * i / rate);
        toneClip = AudioClip.Create("VolumeTestTone440", n, 1, rate, false);
        toneClip.SetData(data, 0);
        return toneClip;
    }

    // 폭탄 폭발음을 효과음(SFX 그룹)으로 한 번 틀고 그 동안의 출력 크기 최댓값을 잰다.
    private static IEnumerator MeasureSfx(AudioManager am, System.Action<float> result)
    {
        am.PlayBombExplosion();
        float peak = 0f, until = Time.realtimeSinceStartup + 0.8f;
        while (Time.realtimeSinceStartup < until) { peak = Mathf.Max(peak, OutputRms()); yield return null; }
        yield return WaitRealtime(1.5f);
        result(peak);
    }

    // ---------------- 그림 ----------------
    private static IEnumerator Shot()
    {
        yield return WaitFrames(2);
        using (var shot = new UiScreenShot(ShotRes))
        {
            yield return WaitFrames(1);
            shot.Refresh();
            Canvas.ForceUpdateCanvases();
            shots.Add(shot.Render());
        }
        yield return WaitFrames(1);
    }

    private static void SaveSheet()
    {
        if (shots.Count < 2) return;
        Texture2D a = shots[shots.Count - 2], b = shots[shots.Count - 1];
        int gap = 8;
        var sheet = new Texture2D(a.width + b.width + gap, Mathf.Max(a.height, b.height), TextureFormat.RGB24, false);
        Color32[] fill = Enumerable.Repeat(new Color32(0, 0, 0, 255), sheet.width * sheet.height).ToArray();
        sheet.SetPixels32(fill);
        sheet.SetPixels(0, 0, a.width, a.height, a.GetPixels());
        sheet.SetPixels(a.width + gap, 0, b.width, b.height, b.GetPixels());
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
        File.WriteAllBytes(ShotPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sb.AppendLine($"  그림 저장: {ShotPath} (왼쪽 기본값, 오른쪽 배경음 40%·효과음 70%, 각 {ShotRes.x}x{ShotRes.y})");
    }
}
