using System;
using UnityEngine;

/// <summary>
/// 플레이어가 고른 소리 크기(배경음·효과음, 0~1). 타이틀 설정 창의 슬라이더가 바꾸고, AudioManager가 믹서(GameAudioMixer)의
/// 그룹 볼륨에 곱해 적용한다 - 배경음은 BGM 그룹(그 아래 추격음 Chase 포함), 효과음은 SFX·UI 그룹. 게임의 모든 소리는 AudioManager를
/// 거쳐 이 그룹들로 나가므로(손맛·밈·Kenney 효과음 포함) 모든 씬에 같이 적용된다.
/// - 값은 PlayerPrefs에 저장해 게임을 껐다 켜도 유지된다. 저장된 값이 없으면(처음 실행) 1(= 지금 크기 100%)이다.
/// - 1이면 믹서 그룹 볼륨이 원래 값(믹서 스냅샷) 그대로다.
/// </summary>
public static class GameVolume
{
    public const string MusicKey = "AnimalKeeper.Volume.Music";
    public const string SfxKey = "AnimalKeeper.Volume.Sfx";
    public const float DefaultVolume = 1f;

    /// <summary>믹서에서 소리를 끈 것으로 보는 크기(dB). 믹서 볼륨의 최솟값과 같다.</summary>
    public const float MinDecibels = -80f;

    private static bool loaded;
    private static float music = DefaultVolume;
    private static float sfx = DefaultVolume;

    /// <summary>배경음 크기 또는 효과음 크기가 바뀌면 난다. AudioManager가 믹서에 다시 적용한다.</summary>
    public static event Action Changed;

    // 도메인 리로드를 끈 에디터에서도 Play마다 저장값을 다시 읽고, 이전 Play의 구독이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        Changed = null;
    }

    /// <summary>배경음 크기(0~1).</summary>
    public static float Music { get { EnsureLoaded(); return music; } }

    /// <summary>효과음 크기(0~1).</summary>
    public static float Sfx { get { EnsureLoaded(); return sfx; } }

    public static void SetMusic(float value)
    {
        EnsureLoaded();
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(value, music)) return;
        music = value;
        PlayerPrefs.SetFloat(MusicKey, music);
        Changed?.Invoke();
    }

    public static void SetSfx(float value)
    {
        EnsureLoaded();
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(value, sfx)) return;
        sfx = value;
        PlayerPrefs.SetFloat(SfxKey, sfx);
        Changed?.Invoke();
    }

    /// <summary>바꾼 값을 디스크에 쓴다(설정 창을 닫을 때). 게임이 정상 종료될 때도 Unity가 저장한다.</summary>
    public static void Save() => PlayerPrefs.Save();

    /// <summary>저장된 값을 다시 읽는다(테스트가 "껐다 켠 것"을 흉내 낼 때도 쓴다).</summary>
    public static void Reload()
    {
        loaded = true;
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultVolume));
        sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultVolume));
        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultVolume));
        sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultVolume));
    }

    /// <summary>크기(0~1)를 믹서 볼륨에 더할 dB로 바꾼다. 1 → 0dB, 0.5 → 약 -6dB, 0 → MinDecibels.</summary>
    public static float ToDecibels(float linear) =>
        linear <= 0.0001f ? MinDecibels : Mathf.Max(MinDecibels, 20f * Mathf.Log10(linear));

    /// <summary>화면 표시용 퍼센트(0~100, 반올림).</summary>
    public static int ToPercent(float linear) => Mathf.RoundToInt(Mathf.Clamp01(linear) * 100f);
}
