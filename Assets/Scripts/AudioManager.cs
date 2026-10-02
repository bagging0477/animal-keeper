using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전역 효과음/배경음을 재생하는 싱글턴. 클립 필드를 비워두면 임시 비프음을
/// 절차적으로 생성해 재생하므로, 나중에 Inspector에 실제 오디오 파일을 끼워 넣기만
/// 하면 바로 교체된다.
///
/// 모든 소리는 카테고리별 AudioMixerGroup(SFX / UI / BGM / BGM 아래 Chase)을 거친다. 카테고리 전체의
/// 크기는 Assets/Audio/Mixer/GameAudioMixer.mixer의 그룹 볼륨으로, 원본 파일마다 녹음 레벨이 달라서
/// 생기는 차이는 아래의 클립별 볼륨(0~1)으로 보정한다. 효과음 클립별 볼륨 기본값은 각 파일의 실측 음량
/// (50ms 단위 최대 RMS)이 같은 수준(약 -19dBFS)이 되도록 계산한 값이다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("믹서 그룹")]
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioMixerGroup uiGroup;
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup chaseGroup;

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

    [Header("클립별 볼륨 보정 (원본 파일 음량 차이 보정용)")]
    [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.32f;
    [SerializeField, Range(0f, 1f)] private float hitVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float netSwingVolume = 0.48f;
    [SerializeField, Range(0f, 1f)] private float tranquilizerShotVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float monsterChaseVolume = 0.56f;
    [SerializeField, Range(0f, 1f)] private float daySuccessVolume = 0.4f;
    [SerializeField, Range(0f, 1f)] private float dayFailVolume = 0.19f;
    [Tooltip("cozy ambient는 RMS 측정치로는 마을 배경음과 비슷하지만 실제로 들으면 훨씬 크게 들린다. 귀로 맞췄던 원래 비율(마을 배경음의 0.4배)을 유지한다.")]
    [SerializeField, Range(0f, 1f)] private float truckMusicVolume = 0.33f;
    [SerializeField, Range(0f, 1f)] private float villageMusicVolume = 0.83f;

    [Header("몬스터 추격음 (몬스터마다 따로 재생되고, 여러 마리면 겹쳐서 들린다)")]
    [Tooltip("한 몬스터가 추격을 멈춘 뒤 이 시간(초) 안에 같은 몬스터가 다시 추격하면, 그 몬스터의 추격음을 처음부터 다시 틀지 않고 그대로 이어간다.")]
    [SerializeField, Min(0f)] private float chaseReleaseDelay = 1.5f;
    [Tooltip("추격이 완전히 끝났을 때 추격음이 페이드아웃되는 시간(초).")]
    [SerializeField, Min(0f)] private float chaseFadeOutDuration = 1f;
    [Tooltip("페이드아웃 도중 추격이 다시 시작됐거나, 겹친 추격음 개수가 바뀌어 볼륨이 조정될 때 목표 볼륨까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float chaseFadeInDuration = 0.3f;
    [Tooltip("추격음이 N개 겹칠 때 각각의 볼륨에 N^(-이 값)을 곱한다. 0이면 낮추지 않고, 0.5면 몇 개가 겹쳐도 전체 에너지가 하나일 때와 비슷하게 유지된다.")]
    [SerializeField, Range(0f, 1f)] private float chaseLayerVolumeFalloff = 0.35f;
    [Tooltip("체크하면 추격하는 동안 추격음을 반복 재생한다. 지금 클립(alert stinger)은 한 번 울리고 잦아드는 소리라 꺼두고, 반복 가능한 추격용 트랙으로 바꾸면 켠다.")]
    [SerializeField] private bool loopChaseClip;

    private AudioSource sfxSource;
    private AudioSource segmentSource;
    private AudioSource uiSource;
    private AudioSource musicSource;
    private string currentMusicScene;

    // 몬스터 한 마리의 추격음 상태. 같은 몬스터의 끊김/재추격 판정은 이 안에서만 하므로,
    // 서로 다른 몬스터의 추격음은 서로를 막지 않고 각자 겹쳐서 재생된다.
    private class ChaseVoice
    {
        public AudioSource Source;
        public bool Chasing;
        public float ReleaseTimer;
    }

    private readonly Dictionary<Object, ChaseVoice> chaseVoices = new Dictionary<Object, ChaseVoice>();
    private readonly List<Object> chaseVoiceKeys = new List<Object>();
    private readonly Stack<AudioSource> idleChaseSources = new Stack<AudioSource>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxSource = CreateSource(sfxGroup, false);
        segmentSource = CreateSource(sfxGroup, false);
        uiSource = CreateSource(uiGroup, false);
        musicSource = CreateSource(musicGroup, true);

        EnsurePlaceholderClips();
    }

    private AudioSource CreateSource(AudioMixerGroup group, bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.outputAudioMixerGroup = group;
        return source;
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

    private void Update()
    {
        UpdateChaseMusic();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayMusicForScene(scene.name);
    }

    private void PlayMusicForScene(string sceneName)
    {
        if (sceneName == currentMusicScene) return;
        currentMusicScene = sceneName;

        // 셸터 씬은 우선 마을 배경음을 그대로 재생한다. 트럭 씬은 예전처럼
        // 자기 전용 트랙(truckMusicClip)을 그대로 쓴다.
        (AudioClip track, float volume) = sceneName switch
        {
            "TruckScene" => (truckMusicClip, truckMusicVolume),
            "VillageScene" or "ShelterScene" => (villageMusicClip, villageMusicVolume),
            _ => (null, 0f)
        };

        if (track == null)
        {
            musicSource.Stop();
            return;
        }

        musicSource.clip = track;
        musicSource.volume = volume;
        musicSource.Play();
    }

    public void PlayPickup() => PlaySfx(pickupClip, pickupVolume);
    public void PlayHit() => PlaySfx(hitClip, hitVolume);
    public void PlayNetSwing() => PlaySfx(netSwingClip, netSwingVolume);
    public void PlayTranquilizerShot() => PlaySfx(tranquilizerShotClip, tranquilizerShotVolume);
    public void PlayDaySuccess() => PlayUi(daySuccessClip, daySuccessVolume);
    public void PlayDayFail() => PlayUi(dayFailClip, dayFailVolume);

    /// <summary>동물별 개별 픽업음처럼, 미리 정의된 슬롯이 없는 임의의 클립을 재생할 때 사용.</summary>
    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, volume);
    }

    private void PlayUi(AudioClip clip, float volume)
    {
        if (clip == null || uiSource == null) return;
        uiSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 파일 전체가 아니라 [startTime, endTime) 구간만 잘라서 재생한다. PlayOneShot은
    /// AudioSource.time으로 시작 지점을 지정할 수 없어서, 대신 segmentSource의 clip을 직접
    /// 지정해 재생한 뒤 구간 길이만큼 뒤에 Stop()을 예약하는 방식을 쓴다. sfxSource를 같이 쓰면
    /// 여기서 바꾼 volume이 이후의 PlayOneShot에도 곱해져서 전용 소스를 따로 둔다.
    /// </summary>
    public void PlaySfxSegment(AudioClip clip, float startTime, float endTime, float volume = 1f)
    {
        if (clip == null || segmentSource == null) return;

        CancelInvoke(nameof(StopSfxSegment));

        segmentSource.clip = clip;
        segmentSource.volume = volume;
        segmentSource.time = Mathf.Clamp(startTime, 0f, clip.length);
        segmentSource.Play();

        Invoke(nameof(StopSfxSegment), Mathf.Max(0f, endTime - startTime));
    }

    private void StopSfxSegment() => segmentSource.Stop();

    /// <summary>
    /// 몬스터가 매 프레임 자기가 추격 중인지 알린다(상태가 바뀔 때만이 아니라 계속 불러도 된다).
    /// 추격음은 몬스터마다 따로 재생된다. 한 몬스터의 추격이 끊긴 상태가 chaseReleaseDelay 동안
    /// 이어져야 그 몬스터의 추격음만 페이드아웃되고, 그 전에(또는 페이드아웃 도중에) 같은 몬스터가
    /// 다시 추격하면 처음부터 다시 틀지 않고 이어간다. 다른 몬스터의 추격은 이 판정에 영향을 주지 않는다.
    /// </summary>
    /// <param name="chaseClip">이 몬스터 전용 추격음. 비워두면 monsterChaseClip을 쓴다.</param>
    public void SetChasing(Object chaser, bool chasing, AudioClip chaseClip = null)
    {
        if (chaser == null) return;

        if (chaseVoices.TryGetValue(chaser, out ChaseVoice voice))
        {
            // 유예 시간/페이드아웃 도중의 재추격도 여기서 이어진다. 클립이 이미 끝까지 재생됐어도
            // (loopChaseClip 꺼짐) 이 몬스터의 추격 구간이 끝나기 전까지는 다시 틀지 않는다.
            voice.Chasing = chasing;
            if (chasing) voice.ReleaseTimer = 0f;
            return;
        }

        if (!chasing) return;

        AudioClip clip = chaseClip != null ? chaseClip : monsterChaseClip;
        AudioSource source = idleChaseSources.Count > 0 ? idleChaseSources.Pop() : CreateSource(chaseGroup, false);
        chaseVoices.Add(chaser, new ChaseVoice { Source = source, Chasing = true });

        if (clip == null) return;
        source.clip = clip;
        source.loop = loopChaseClip;
        source.volume = monsterChaseVolume * ChaseLayerScale(ActiveChaseVoiceCount());
        source.Play();
    }

    private void UpdateChaseMusic()
    {
        if (chaseVoices.Count == 0) return;

        // 일시정지(timeScale 0) 중에도 페이드가 멈추지 않게 unscaled 시간을 쓴다.
        float dt = Time.unscaledDeltaTime;

        chaseVoiceKeys.Clear();
        chaseVoiceKeys.AddRange(chaseVoices.Keys);

        foreach (Object chaser in chaseVoiceKeys)
        {
            ChaseVoice voice = chaseVoices[chaser];

            // SetChasing(false)를 부르지 못하고 파괴된 몬스터는 Unity의 == null 판정으로 걸러낸다.
            if (chaser == null) voice.Chasing = false;

            if (voice.Chasing) voice.ReleaseTimer = 0f;
            else voice.ReleaseTimer += dt;
        }

        float layeredVolume = monsterChaseVolume * ChaseLayerScale(ActiveChaseVoiceCount());

        foreach (Object chaser in chaseVoiceKeys)
        {
            ChaseVoice voice = chaseVoices[chaser];
            AudioSource source = voice.Source;

            if (voice.ReleaseTimer < chaseReleaseDelay)
            {
                if (source.isPlaying) source.volume = MoveChaseVolume(source.volume, layeredVolume, chaseFadeInDuration, dt);
                continue;
            }

            if (source.isPlaying)
            {
                source.volume = MoveChaseVolume(source.volume, 0f, chaseFadeOutDuration, dt);
                if (source.volume > 0f) continue;
                source.Stop();
            }

            chaseVoices.Remove(chaser);
            idleChaseSources.Push(source);
        }
    }

    // 페이드아웃 단계에 들어가지 않은(추격 중이거나 유예 시간 안인) 추격음 개수.
    private int ActiveChaseVoiceCount()
    {
        int count = 0;
        foreach (ChaseVoice voice in chaseVoices.Values)
        {
            if (voice.ReleaseTimer < chaseReleaseDelay) count++;
        }
        return count;
    }

    private float ChaseLayerScale(int activeCount) => Mathf.Pow(Mathf.Max(1, activeCount), -chaseLayerVolumeFalloff);

    private float MoveChaseVolume(float current, float target, float duration, float dt)
    {
        if (duration <= 0f || monsterChaseVolume <= 0f) return target;
        return Mathf.MoveTowards(current, target, monsterChaseVolume * dt / duration);
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
