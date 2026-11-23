using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 타이틀 설정 창(SettingsPanel)의 소리 크기 조절. 배경음·효과음 슬라이더(0~100%)를 GameVolume에 연결한다.
/// - 슬라이더를 움직이면 바로 GameVolume이 바뀌고 AudioManager가 믹서에 적용한다(바로 들린다).
/// - 창이 닫힐 때(닫기 버튼·ESC·씬 이동) 값을 디스크에 저장한다 - 껐다 켜도 유지된다.
/// - 효과음 슬라이더를 움직이면 짧은 효과음(다트 맞는 소리, Kenney)을 새 크기로 들려준다(PreviewInterval마다 한 번). 타이틀에는 배경음이 없어 배경음 크기는 게임 씬에서 들린다.
/// - 창이 열려 있을 때 ESC를 누르면 닫힌다(타이틀에는 ESC 일시정지 메뉴가 없다).
/// UI는 에디터 도구 Tools > UI > Build Volume Settings(VolumeSettingsBuilder)가 SettingsPanel 안에 만들어 연결한다.
/// </summary>
public class VolumeSettingsPanel : MonoBehaviour
{
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text musicValueText;
    [SerializeField] private TMP_Text sfxValueText;

    public Slider MusicSlider => musicSlider;
    public Slider SfxSlider => sfxSlider;
    public TMP_Text MusicValueText => musicValueText;
    public TMP_Text SfxValueText => sfxValueText;

    /// <summary>효과음 미리듣기 최소 간격(초, 실제 시간) - 슬라이더를 끌 때 소리가 겹쳐 쏟아지지 않게.</summary>
    public const float PreviewInterval = 0.2f;
    private float nextPreviewTime;

    /// <summary>효과음 미리듣기를 튼 횟수(테스트가 읽는다).</summary>
    public int PreviewCount { get; private set; }

    private void Awake()
    {
        if (musicSlider != null) musicSlider.onValueChanged.AddListener(v => { GameVolume.SetMusic(v / 100f); RefreshTexts(); });
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(v => { GameVolume.SetSfx(v / 100f); RefreshTexts(); PreviewSfx(); });
    }

    private void PreviewSfx()
    {
        if (Time.unscaledTime < nextPreviewTime || AudioManager.Instance == null) return;
        nextPreviewTime = Time.unscaledTime + PreviewInterval;
        AudioManager.Instance.PlayDartHit();
        PreviewCount++;
    }

    private void OnEnable()
    {
        // 열 때마다 저장된 지금 값으로 맞춘다(값을 다시 쓰지 않게 알림 없이).
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(GameVolume.ToPercent(GameVolume.Music));
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameVolume.ToPercent(GameVolume.Sfx));
        RefreshTexts();
    }

    private void OnDisable() => GameVolume.Save();

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    /// <summary>창을 닫는다(닫기 버튼의 TitleMenu.OnCloseSetting과 같다).</summary>
    public void Close() => gameObject.SetActive(false);

    private void RefreshTexts()
    {
        if (musicValueText != null) musicValueText.text = GameVolume.ToPercent(GameVolume.Music) + "%";
        if (sfxValueText != null) sfxValueText.text = GameVolume.ToPercent(GameVolume.Sfx) + "%";
    }
}
