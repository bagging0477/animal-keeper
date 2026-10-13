using UnityEngine;

/// <summary>
/// 거북이. 배회/놀람/도주는 AnimalFlee(고양이와 같은 코드)를 그대로 쓰고, 도주를 정해진 거리만큼 마치면 등껍질 속에 숨는다.
/// Wander → (감지 반경) Alert → Flee → Hidden → Emerge → Wander. 값은 모두 GameBalanceConfig의 "동물 - 거북이".
///
/// - 배회/도주 속도는 거북이 전용 고정값(turtleWanderSpeed, turtleFleeSpeed)이라 다른 동물보다 훨씬 느리다.
///   놀람(turtleAlertDuration) 동안은 제자리에 있다.
/// - 사방이 막혀 도망칠 방향이 없거나, 막혀서 실제로 못 움직이면 바로 숨는다.
/// - 숨은 동안(나오는 중 포함)은 들 수 없고, E를 누르면 쿨다운을 두고 자막으로 알려준다(IAnimalPickupGate).
/// - 숨을 때 hideSound, 나오기 시작할 때 emergeSound를 AudioManager(SFX 믹서 그룹)로 재생한다. 숨은 채 E를 눌러 거절될 때도
///   자막과 같은 쿨다운으로 hideSound를 다시 쓴다. 클립을 비워두면 소리 없이 넘어간다. 일시정지(timeScale 0) 중에는 재생하지 않는다.
/// - 마취(수면)에 맞으면 도주/숨기를 바로 멈추고 잠들어 들 수 있다(turtleSleepInterruptsHiding). 깨면 배회로 돌아간다.
///   맞은 뒤 잠들기 전(sleepDelay)에는 기절 프레임(stunnedSprite)에 멈춰 서 있고, 잠든 동안은 SimpleFrameAnimator의 knockedOutFrames를 반복한다.
///   끄면 도망치거나 숨어 있는 동안은 마취가 듣지 않는다.
/// - 같은 Day 재진입 때는 마지막 위치에서 배회로 시작한다(도주/숨은 채로 저장돼도 RestoreFleeing을 무시한다).
/// - 코루틴과 이벤트 구독을 쓰지 않고 Update의 타이머만 쓴다 - 씬 전환이나 일시정지(timeScale 0)에도 정리할 것이 없다.
/// </summary>
public class AnimalTurtle : AnimalFlee, IAnimalPickupGate, IAnimalWeightRange
{
    public enum TurtleState { Wander, Alert, Flee, Hidden, Emerge }

    [Header("거북이 - 숨기/나오기 프레임 (SimpleFrameAnimator로 재생, 숨은 동안은 hideFrames 마지막 프레임)")]
    [SerializeField] private Sprite[] hideFrames;
    [SerializeField] private Sprite[] emergeFrames;
    [SerializeField] private string hiddenMessage = "거북이가 등껍질 속에 숨었습니다";
    [Tooltip("마취에 맞은 뒤 잠들기 전까지 멈춰서 보여줄 기절 프레임. 비워두면 지금 프레임 그대로 멈춘다.")]
    [SerializeField] private Sprite stunnedSprite;

    [Header("거북이 - 소리 (AudioManager의 SFX 믹서 그룹으로 재생, 클립을 비워두면 건너뛴다)")]
    [Tooltip("숨을 때, 그리고 숨은 채 E를 눌러 거절될 때(자막과 같은 쿨다운) 재생한다.")]
    [SerializeField] private ShellSound hideSound = new ShellSound();
    [Tooltip("껍질에서 나오기 시작할 때 재생한다.")]
    [SerializeField] private ShellSound emergeSound = new ShellSound();

    // AnimalRescue의 픽업음 설정(pickupSound/Volume/playPickupSoundSegment/StartTime/EndTime)과 같은 모양.
    [System.Serializable]
    private class ShellSound
    {
        public AudioClip clip;
        [Tooltip("원본 파일마다 녹음 크기가 달라서 다른 효과음과 체감 크기를 맞추는 보정값이다.")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("체크하면 clip 전체가 아니라 아래 구간(초)만 잘라서 재생한다.")]
        public bool playSegment;
        public float startTime;
        public float endTime = 1f;

        public void Play()
        {
            if (clip == null || AudioManager.Instance == null) return;
            if (playSegment) AudioManager.Instance.PlaySfxSegment(clip, startTime, endTime, volume);
            else AudioManager.Instance.PlaySfx(clip, volume);
        }
    }

    private enum Shell { None, Hidden, Emerging }

    private Shell shell;
    private float hiddenTime;
    private float outsideTime;
    private float nextHiddenMessageTime;
    private Vector2 fleeStart;
    private float fleeTime;
    private float blockedTime;
    private AnimalSleep turtleSleep;
    private bool stunned;
    private Sprite[] stunnedFrames;

    // 도주가 "막혔다"고 볼 때까지 기다리는 시간. 도주 첫 물리 스텝은 아직 움직이지 않은 것으로 재므로 바로 숨지 않게 한다.
    private const float BlockedGraceTime = 0.15f;

    public TurtleState State =>
        shell == Shell.Hidden ? TurtleState.Hidden
        : shell == Shell.Emerging ? TurtleState.Emerge
        : IsFleeingState ? TurtleState.Flee
        : IsAlertState ? TurtleState.Alert
        : TurtleState.Wander;

    /// <summary>숨은 채로 E를 눌러 자막을 띄운 횟수(쿨다운으로 걸러진 것은 세지 않는다).</summary>
    public int HiddenMessageCount { get; private set; }

    /// <summary>숨기/나오기 소리를 재생하려 한 횟수(클립이 비어 있어도 센다, 일시정지 중이라 건너뛴 것은 세지 않는다). 거절될 때의 hide 소리도 HideSoundCount에 들어간다.</summary>
    public int HideSoundCount { get; private set; }
    public int EmergeSoundCount { get; private set; }

    public bool CanBePickedUp => shell == Shell.None;

    private float DetectRadius => config != null ? config.turtleDetectRadius : 5f;
    private float FleeDistance => config != null ? config.turtleFleeDistance : 2f;
    private float HideRadius => config != null ? config.turtleHideRadius : 5f;
    private float EmergeDelay => config != null ? config.turtleEmergeDelay : 3f;
    private float MaxHiddenTime => config != null ? config.turtleMaxHiddenTime : 15f;
    private bool SleepInterruptsHiding => config == null || config.turtleSleepInterruptsHiding;

    protected override float AlertRange => DetectRadius;
    protected override float AlertDuration => config != null ? config.turtleAlertDuration : 0.65f;

    protected override float WanderSpeed => config != null ? config.turtleWanderSpeed : 0.36f;
    protected override float FleeSpeed => config != null ? config.turtleFleeSpeed : 0.6f;

    protected override void Awake()
    {
        base.Awake();
        turtleSleep = GetComponent<AnimalSleep>();
        stunnedFrames = stunnedSprite != null ? new[] { stunnedSprite } : null;
    }

    public bool TryGetWeightRange(out int min, out int max)
    {
        min = config != null ? config.turtleMinWeight : 4;
        max = config != null ? Mathf.Max(config.turtleMinWeight, config.turtleMaxWeight) : 11;
        return true;
    }

    // 재진입 때는 늘 배회로 시작한다 - 도주하던 중이었어도 숨은 중이었어도 마지막 위치에서 다시 배회한다.
    public override void RestoreFleeing()
    {
    }

    protected override void OnStartFleeing()
    {
        fleeStart = Body.position;
        fleeTime = 0f;
        blockedTime = 0f;
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
        EnterHidden();
    }

    protected override void Update()
    {
        HandleSleep();

        if (stunned)
        {
            StopMoving();
            return;
        }

        if (shell == Shell.None)
        {
            base.Update();
            return;
        }

        StopMoving();
        float distance = Player != null ? Vector2.Distance(Body.position, Player.position) : float.MaxValue;

        if (shell == Shell.Hidden)
        {
            hiddenTime += Time.deltaTime;
            outsideTime = distance > HideRadius ? outsideTime + Time.deltaTime : 0f;
            if (outsideTime >= EmergeDelay || hiddenTime >= MaxHiddenTime) StartEmerging();
        }
        else if (FrameAnimator == null || !FrameAnimator.IsPlayingOnce)
        {
            shell = Shell.None;
            ReturnToWander();
        }
    }

    // 마취에 맞으면(맞은 순간부터) 도주/숨기를 바로 그만두고 배회 상태로 둔다 - 그러면 들 수 있고, 깨면 그대로 배회한다.
    // 잠들기 전까지는 기절 프레임에 멈춰 서 있고, 잠든 뒤에는 SimpleFrameAnimator가 잠자는 프레임을 이어서 재생한다.
    // 마취 연동을 끄면 도망치거나 숨어 있는 동안에는 잠드는 순간 깨워서 하던 것을 계속한다.
    private void HandleSleep()
    {
        bool hit = turtleSleep != null && (turtleSleep.IsAsleep || turtleSleep.IsSleepPending);
        if (!hit)
        {
            // 잠들기 전에 깨워졌으면 기절 프레임을 풀어 준다.
            if (stunned && FrameAnimator != null) FrameAnimator.ReleaseHold();
            stunned = false;
            return;
        }

        bool busy = shell != Shell.None || IsFleeingState || IsAlertState;
        if (busy && !SleepInterruptsHiding)
        {
            if (turtleSleep.IsAsleep) turtleSleep.WakeUp();
            return;
        }

        if (busy)
        {
            LeaveShell();
            ReturnToWander();
        }

        // 잠든 뒤에는 AnimalFlee.Update가 멈춰 두고, 기절 중 표시는 SimpleFrameAnimator가 맡는다.
        if (turtleSleep.IsAsleep)
        {
            stunned = false;
            return;
        }

        if (stunned) return;
        stunned = true;
        StopMoving();
        if (FrameAnimator == null || stunnedFrames == null) return;
        FrameAnimator.IsFleeing = false;
        FrameAnimator.IsMoving = false;
        FrameAnimator.PlayAndHold(stunnedFrames);
    }

    private void EnterHidden()
    {
        shell = Shell.Hidden;
        hiddenTime = 0f;
        outsideTime = 0f;
        StopMoving();
        PlayHideSound();
        if (FrameAnimator == null) return;
        FrameAnimator.IsFleeing = false;
        FrameAnimator.IsMoving = false;
        FrameAnimator.PlayAndHold(hideFrames);
    }

    private void StartEmerging()
    {
        shell = Shell.Emerging;
        PlayEmergeSound();
        if (FrameAnimator == null) return;
        FrameAnimator.ReleaseHold();
        FrameAnimator.PlayOnce(emergeFrames);
    }

    private void LeaveShell()
    {
        shell = Shell.None;
        if (FrameAnimator != null) FrameAnimator.ReleaseHold();
    }

    public void OnPickupBlocked()
    {
        if (Time.time < nextHiddenMessageTime) return;
        nextHiddenMessageTime = Time.time + (config != null ? config.turtleHiddenMessageCooldown : 2f);
        HiddenMessageCount++;
        SubtitleManager.Show(hiddenMessage, 2f);
        PlayHideSound();
    }

    private void PlayHideSound()
    {
        if (!CanPlaySound) return;
        HideSoundCount++;
        hideSound.Play();
    }

    private void PlayEmergeSound()
    {
        if (!CanPlaySound) return;
        EmergeSoundCount++;
        emergeSound.Play();
    }

    // 일시정지 중에는 Update의 deltaTime이 0이라 상태가 바뀌지 않지만, 혹시 바뀌어도 소리는 내지 않는다.
    private static bool CanPlaySound => !PauseMenu.IsPaused && Time.timeScale > 0f;
}
