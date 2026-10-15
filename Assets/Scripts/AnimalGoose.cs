using UnityEngine;

/// <summary>
/// 거위. 배회/놀람/도주는 AnimalFlee(고양이와 같은 이동)를 그대로 쓰고, 정해진 거리를 날아 도망친 뒤 잠깐 멈췄다가 다시 느리게 배회한다.
/// Wander → (감지 반경) Alert → Flee → Rest → Wander. 값은 모두 GameBalanceConfig의 "동물 - 거위".
///
/// - 도망치기 시작할 때 울고(cryClip), 레서판다(AnimalSoundFlee)처럼 소리 이벤트를 내 소리반응형 몬스터를 부른다.
///   레서판다와 같이 도주를 시작할 때 한 번만 울고, 강도는 animalSoundIntensity를 쓴다.
/// - 잠든 동안에만 들 수 있다(IAnimalPickupGate). 마취총/폭탄에 맞으면 그 자리에 멈춰 downFrames를 한 번 재생하고(gooseDownDuration 동안,
///   AnimalSleep.DownDuration), 그 뒤 잠든다. 쓰러지는 동안은 들 수 없고, 다시 맞아도 시간이 늘거나 처음부터 다시 재생되지 않는다.
/// - 맞으면 놀람/도주/멈춤을 바로 그만두고, 깨면 배회로 돌아간다.
/// - 같은 Day 재진입 때는 마지막 위치에서 배회로 시작한다(도주 중에 저장돼도 RestoreFleeing을 무시한다). 수면은 저장하지 않아 깬 채로 나온다.
/// - 코루틴과 이벤트 구독을 쓰지 않고 Update의 타이머만 쓴다.
/// </summary>
public class AnimalGoose : AnimalFlee, IAnimalPickupGate, IAnimalWeightRange
{
    public enum GooseState { Wander, Alert, Flee, Rest, Down, Asleep }

    [Header("거위 - 울음소리 (비워 두면 소리 없이 소리 이벤트만 낸다)")]
    [SerializeField] private AudioClip cryClip;
    [Tooltip("cryClip의 재생 볼륨. 원본 파일마다 녹음 크기가 달라서 다른 효과음과 체감 크기를 맞추는 보정값이다.")]
    [SerializeField, Range(0f, 1f)] private float cryVolume = 1f;

    [Header("거위 - 쓰러짐 (마취/폭탄에 맞은 뒤 잠들기 전까지 한 번 재생하고 마지막 프레임에 멈춘다)")]
    [SerializeField] private Sprite[] downFrames;

    private AnimalSleep gooseSleep;
    private bool downed;
    private Vector2 fleeStart;
    private float fleeTime;
    private float blockedTime;
    private bool resting;
    private float restTimer;

    // 도주가 "막혔다"고 볼 때까지 기다리는 시간(거북이와 같다). 도주 첫 물리 스텝은 아직 움직이지 않은 것으로 재므로 바로 멈추지 않게 한다.
    private const float BlockedGraceTime = 0.15f;

    public GooseState State =>
        IsAsleep ? GooseState.Asleep
        : downed ? GooseState.Down
        : resting ? GooseState.Rest
        : IsFleeingState ? GooseState.Flee
        : IsAlertState ? GooseState.Alert
        : GooseState.Wander;

    /// <summary>울음(소리 이벤트)을 낸 횟수.</summary>
    public int CryCount { get; private set; }

    public bool CanBePickedUp => IsAsleep;

    private bool IsAsleep => gooseSleep != null && gooseSleep.IsAsleep;

    private float FleeDistance => config != null ? config.gooseFleeDistance : 12f;
    private float RestDuration => config != null ? config.gooseRestDuration : 2f;
    private float DownDuration => config != null ? config.gooseDownDuration : 0.8f;

    protected override float AlertRange => config != null ? config.gooseDetectRadius : 3f;
    protected override float WanderSpeed => config != null ? config.gooseWanderSpeed : 0.32f;
    protected override float FleeSpeed => config != null ? config.gooseFleeSpeed : 7.17255f;

    protected override void Awake()
    {
        base.Awake();
        gooseSleep = GetComponent<AnimalSleep>();
        if (gooseSleep != null) gooseSleep.DownDuration = DownDuration;
    }

    public bool TryGetWeightRange(out int min, out int max)
    {
        min = config != null ? config.gooseMinWeight : 14;
        max = config != null ? Mathf.Max(config.gooseMinWeight, config.gooseMaxWeight) : 21;
        return true;
    }

    // 재진입 때는 늘 배회로 시작한다 - 도주하던 중이었어도 마지막 위치에서 다시 배회한다.
    public override void RestoreFleeing()
    {
    }

    protected override void OnStartFleeing()
    {
        fleeStart = Body.position;
        fleeTime = 0f;
        blockedTime = 0f;

        CryCount++;
        AudioManager.Instance?.PlaySfx(cryClip, cryVolume);
        float intensity = config != null ? config.animalSoundIntensity : 1f;
        SoundEvents.Emit(transform.position, intensity);
    }

    protected override bool ShouldStopFleeing(float distanceToPlayer)
    {
        fleeTime += Time.deltaTime;
        blockedTime = FleeBlocked || (fleeTime > BlockedGraceTime && !IsActuallyMoving) ? blockedTime + Time.deltaTime : 0f;

        bool reachedDistance = (Body.position - fleeStart).magnitude >= FleeDistance;
        bool blocked = FleeBlocked ? blockedTime > 0f : blockedTime >= BlockedGraceTime;
        // 혹시 계속 미끄러지기만 하며 거리를 못 채워도 무한히 도망치지 않게 한다.
        bool tooLong = fleeTime >= FleeDistance / Mathf.Max(0.1f, FleeSpeed) * 2f + 1f;
        return reachedDistance || blocked || tooLong;
    }

    protected override void OnStopFleeing()
    {
        ReturnToWander();
        resting = true;
        restTimer = RestDuration;
        StopMoving();
    }

    protected override void Update()
    {
        HandleSleep();

        if (downed)
        {
            StopMoving();
            return;
        }

        if (!resting)
        {
            base.Update();
            return;
        }

        StopMoving();
        if (FrameAnimator != null)
        {
            FrameAnimator.IsFleeing = false;
            FrameAnimator.IsMoving = false;
        }

        restTimer -= Time.deltaTime;
        if (restTimer > 0f) return;

        resting = false;
        ReturnToWander();
    }

    // 마취/폭탄에 맞으면(맞은 순간부터) 놀람/도주/멈춤을 바로 그만두고 배회 상태로 둔다 - 깨면 그대로 느리게 배회한다.
    // 잠들기 전(쓰러지는 동안)에는 제자리에서 downFrames를 한 번만 재생하고, 잠든 뒤에는 SimpleFrameAnimator가 기절 스프라이트로 바꾼다.
    private void HandleSleep()
    {
        bool pending = gooseSleep != null && gooseSleep.IsSleepPending;
        if (!pending)
        {
            // 잠들지 않고 풀렸으면(WakeUp) 멈춰 둔 쓰러짐 프레임을 풀어 준다.
            if (downed && !IsAsleep && FrameAnimator != null) FrameAnimator.ReleaseHold();
            downed = false;
            if (!IsAsleep) return;
        }

        if (resting || IsFleeingState || IsAlertState)
        {
            resting = false;
            StopMoving();
            ReturnToWander();
        }

        if (!pending || downed) return;
        downed = true;
        StopMoving();
        if (FrameAnimator == null) return;
        FrameAnimator.IsFleeing = false;
        FrameAnimator.IsMoving = false;
        FrameAnimator.PlayAndHold(downFrames);
    }

    // 깨어 있는 거위에게 E를 눌러도 따로 알리지 않는다(줍기 안내도 뜨지 않는다).
    public void OnPickupBlocked()
    {
    }
}
