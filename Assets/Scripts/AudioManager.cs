using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전역 효과음/배경음을 재생하는 싱글턴. 클립 필드를 비워두면 임시 비프음을
/// 절차적으로 생성해 재생하므로, 나중에 Inspector에 실제 오디오 파일을 끼워 넣기만
/// 하면 바로 교체된다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("효과음 (비워두면 임시 비프음 자동 생성)")]
    [SerializeField] private AudioClip pickupClip;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip netSwingClip;
    [SerializeField] private AudioClip tranquilizerShotClip;
    [SerializeField] private AudioClip monsterChaseClip;
    [SerializeField] private AudioClip daySuccessClip;
    [SerializeField] private AudioClip dayFailClip;

    [Header("배경음 (비워두면 임시 루프 자동 생성)")]
    [SerializeField] private AudioClip truckMusicClip;
    [SerializeField] private AudioClip villageMusicClip;

    [Header("볼륨 (사운드 교체 전까지 0으로 음소거)")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0f;

    private AudioSource sfxSource;
    private AudioSource musicSource;
    private string currentMusicScene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;

        EnsurePlaceholderClips();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        PlayMusicForScene(SceneManager.GetActiveScene().name);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayMusicForScene(scene.name);
    }

    private void PlayMusicForScene(string sceneName)
    {
        if (sceneName == currentMusicScene) return;
        currentMusicScene = sceneName;

        AudioClip track = sceneName switch
        {
            "TruckScene" => truckMusicClip,
            "VillageScene" => villageMusicClip,
            _ => null
        };

        if (track == null)
        {
            musicSource.Stop();
            return;
        }

        musicSource.clip = track;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    public void PlayPickup() => PlaySfx(pickupClip);
    public void PlayHit() => PlaySfx(hitClip);
    public void PlayNetSwing() => PlaySfx(netSwingClip);
    public void PlayTranquilizerShot() => PlaySfx(tranquilizerShotClip);
    public void PlayMonsterChaseAlert() => PlaySfx(monsterChaseClip);
    public void PlayDaySuccess() => PlaySfx(daySuccessClip);
    public void PlayDayFail() => PlaySfx(dayFailClip);

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    private void EnsurePlaceholderClips()
    {
        if (pickupClip == null)
            pickupClip = BuildClip("Placeholder_Pickup", new[] { new ToneSegment(600f, 1000f, 0.12f) }, Waveform.Sine, 0.5f);

        if (hitClip == null)
            hitClip = BuildClip("Placeholder_Hit", new[] { new ToneSegment(220f, 110f, 0.25f) }, Waveform.Square, 0.55f);

        if (netSwingClip == null)
            netSwingClip = BuildClip("Placeholder_NetSwing", new[] { new ToneSegment(400f, 400f, 0.16f) }, Waveform.Noise, 0.35f);

        if (tranquilizerShotClip == null)
            tranquilizerShotClip = BuildClip("Placeholder_TranquilizerShot", new[] { new ToneSegment(1200f, 300f, 0.15f) }, Waveform.Triangle, 0.5f);

        if (monsterChaseClip == null)
        {
            monsterChaseClip = BuildClip("Placeholder_MonsterChase", new[]
            {
                new ToneSegment(150f, 90f, 0.3f),
                new ToneSegment(90f, 55f, 0.3f)
            }, Waveform.Triangle, 0.55f);
        }

        if (daySuccessClip == null)
        {
            daySuccessClip = BuildClip("Placeholder_DaySuccess", new[]
            {
                new ToneSegment(523.25f, 0.15f),
                new ToneSegment(659.25f, 0.15f),
                new ToneSegment(783.99f, 0.25f)
            }, Waveform.Sine, 0.5f);
        }

        if (dayFailClip == null)
        {
            dayFailClip = BuildClip("Placeholder_DayFail", new[]
            {
                new ToneSegment(440f, 0.2f),
                new ToneSegment(349.23f, 0.2f),
                new ToneSegment(261.63f, 0.35f)
            }, Waveform.Triangle, 0.5f);
        }

        if (truckMusicClip == null)
        {
            truckMusicClip = BuildClip("Placeholder_TruckMusic", new[]
            {
                new ToneSegment(261.63f, 1f),
                new ToneSegment(329.63f, 1f),
                new ToneSegment(392.00f, 1f),
                new ToneSegment(329.63f, 1f)
            }, Waveform.Sine, 0.15f);
        }

        if (villageMusicClip == null)
        {
            villageMusicClip = BuildClip("Placeholder_VillageMusic", new[]
            {
                new ToneSegment(110f, 0.5f),
                new ToneSegment(116.54f, 0.5f),
                new ToneSegment(110f, 0.5f),
                new ToneSegment(103.83f, 0.5f)
            }, Waveform.Triangle, 0.16f);
        }
    }

    private enum Waveform { Sine, Square, Triangle, Noise }

    private struct ToneSegment
    {
        public readonly float StartFreq;
        public readonly float EndFreq;
        public readonly float Duration;

        public ToneSegment(float freq, float duration)
        {
            StartFreq = freq;
            EndFreq = freq;
            Duration = duration;
        }

        public ToneSegment(float startFreq, float endFreq, float duration)
        {
            StartFreq = startFreq;
            EndFreq = endFreq;
            Duration = duration;
        }
    }

    private static AudioClip BuildClip(string clipName, ToneSegment[] segments, Waveform waveform, float volume)
    {
        const int sampleRate = 44100;
        const float edgeFadeSeconds = 0.01f;

        int totalSamples = 0;
        foreach (ToneSegment segment in segments)
        {
            totalSamples += Mathf.Max(1, Mathf.CeilToInt(segment.Duration * sampleRate));
        }

        float[] data = new float[totalSamples];
        int writeIndex = 0;
        int edgeFadeSamples = Mathf.Max(1, Mathf.RoundToInt(edgeFadeSeconds * sampleRate));

        foreach (ToneSegment segment in segments)
        {
            int segmentSamples = Mathf.Max(1, Mathf.CeilToInt(segment.Duration * sampleRate));
            double phase = 0.0;

            for (int i = 0; i < segmentSamples; i++)
            {
                float t = segmentSamples <= 1 ? 0f : i / (float)(segmentSamples - 1);
                float frequency = Mathf.Lerp(segment.StartFreq, segment.EndFreq, t);
                phase += 2.0 * System.Math.PI * frequency / sampleRate;

                float raw;
                switch (waveform)
                {
                    case Waveform.Square:
                        raw = Mathf.Sin((float)phase) >= 0f ? 1f : -1f;
                        break;
                    case Waveform.Triangle:
                        raw = Mathf.Asin(Mathf.Sin((float)phase)) * (2f / Mathf.PI);
                        break;
                    case Waveform.Noise:
                        raw = Random.Range(-1f, 1f);
                        break;
                    default:
                        raw = Mathf.Sin((float)phase);
                        break;
                }

                float fadeIn = Mathf.Clamp01(i / (float)edgeFadeSamples);
                float fadeOut = Mathf.Clamp01((segmentSamples - 1 - i) / (float)edgeFadeSamples);
                float envelope = Mathf.Min(fadeIn, fadeOut);

                data[writeIndex] = raw * volume * envelope;
                writeIndex++;
            }
        }

        AudioClip clip = AudioClip.Create(clipName, totalSamples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
