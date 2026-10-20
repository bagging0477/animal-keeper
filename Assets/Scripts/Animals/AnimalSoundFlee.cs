using UnityEngine;

public class AnimalSoundFlee : AnimalFlee
{
    [Header("울음소리")]
    [Tooltip("도망치기 시작하며 소리를 낼 때(=소리반응형 몬스터를 부르는 순간) 재생할 울음소리.")]
    [SerializeField] private AudioClip cryClip;
    [Tooltip("cryClip의 재생 볼륨. 원본 파일마다 녹음 크기가 달라서 다른 효과음과 체감 크기를 맞추는 보정값이다.")]
    [SerializeField, Range(0f, 1f)] private float cryVolume = 1f;

    protected override void OnStartFleeing()
    {
        Debug.Log($"동물이 소리를 냈다! 위치:({transform.position.x:F1}, {transform.position.y:F1})");
        AudioManager.Instance?.PlaySfx(cryClip, cryVolume);
        float intensity = config != null ? config.animalSoundIntensity : 1f;
        SoundEvents.Emit(transform.position, intensity);
    }
}
