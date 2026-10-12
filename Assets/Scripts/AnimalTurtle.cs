using UnityEngine;

/// <summary>
/// 거북이. 배회/놀람/도주는 AnimalFlee(고양이와 같은 코드)를 그대로 쓰고, 도주를 정해진 거리만큼 마치면 등껍질 속에 숨는다.
/// Wander → (감지 반경) Alert → Flee → Hidden → Emerge → Wander. 값은 모두 GameBalanceConfig의 "동물 - 거북이".
///
/// - 도주 속도는 지금 플레이어의 걷기 속도(PlayerMovement.WalkSpeed, 클래스·러닝화 포함) x turtleFleeSpeedMultiplier라서,
///   걷기로는 못 따라잡고 스프린트로는 따라잡는다. 놀람(turtleAlertDuration) 동안은 제자리에 있다.
/// - 사방이 막혀 도망칠 방향이 없거나, 막혀서 실제로 못 움직이면 바로 숨는다.
/// - 숨은 동안(나오는 중 포함)은 들 수 없고, E를 누르면 쿨다운을 두고 자막으로 알려준다(IAnimalPickupGate).
/// - 마취(수면)에 맞으면 도주/숨기를 바로 멈추고 잠들어 들 수 있다(turtleSleepInterruptsHiding). 깨면 배회로 돌아간다.
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

    private enum Shell { None, Hidden, Emerging }

    private Shell shell;
    private float hiddenTime;
    private float outsideTime;
    private float nextHiddenMessageTime;
    private Vector2 fleeStart;
    private float fleeTime;
    private float blockedTime;
    private PlayerMovement playerMovement;
    private AnimalSleep turtleSleep;

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

    public bool CanBePickedUp => shell == Shell.None;

    private float DetectRadius => config != null ? config.turtleDetectRadius : 5f;
    private float FleeDistance => config != null ? config.turtleFleeDistance : 4f;
    private float HideRadius => config != null ? config.turtleHideRadius : 5f;
    private float EmergeDelay => config != null ? config.turtleEmergeDelay : 3f;
    private float MaxHiddenTime => config != null ? config.turtleMaxHiddenTime : 15f;
    private bool SleepInterruptsHiding => config == null || config.turtleSleepInterruptsHiding;

    protected override float AlertRange => DetectRadius;
    protected override float AlertDuration => config != null ? config.turtleAlertDuration : 0.65f;

    protected override float FleeSpeed
    {
        get
        {
            float walk = playerMovement != null ? playerMovement.WalkSpeed : config != null ? config.playerMoveSpeed : 3.75f;
            return walk * (config != null ? config.turtleFleeSpeedMultiplier : 1.05f);
        }
    }

    protected override void Awake()
    {
        base.Awake();
        turtleSleep = GetComponent<AnimalSleep>();
    }

    protected override void Start()
    {
        base.Start();
        if (Player != null) playerMovement = Player.GetComponent<PlayerMovement>();
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

    // 마취에 맞으면(실제로 잠든 순간) 도주/숨기를 바로 그만두고 잠든 채 배회 상태로 둔다 - 그러면 들 수 있고, 깨면 그대로 배회한다.
    // 마취 연동을 끄면 도망치거나 숨어 있는 동안에는 깨워서 하던 것을 계속한다.
    private void HandleSleep()
    {
        if (turtleSleep == null || !turtleSleep.IsAsleep) return;

        bool busy = shell != Shell.None || IsFleeingState || IsAlertState;
        if (!busy) return;

        if (SleepInterruptsHiding)
        {
            LeaveShell();
            ReturnToWander();
        }
        else
        {
            turtleSleep.WakeUp();
        }
    }

    private void EnterHidden()
    {
        shell = Shell.Hidden;
        hiddenTime = 0f;
        outsideTime = 0f;
        StopMoving();
        if (FrameAnimator == null) return;
        FrameAnimator.IsFleeing = false;
        FrameAnimator.IsMoving = false;
        FrameAnimator.PlayAndHold(hideFrames);
    }

    private void StartEmerging()
    {
        shell = Shell.Emerging;
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
    }
}
