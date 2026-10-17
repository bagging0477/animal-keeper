using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 거위. 배회/놀람은 AnimalFlee(고양이와 같은 이동)를 그대로 쓰고, 정해진 거리를 날아 도망친 뒤 잠깐 멈췄다가 다시 느리게 배회한다.
/// Wander → (감지 반경) Alert → Flee → Rest → Wander. Rest가 끝날 때 플레이어가 감지 반경 안에 있으면 Wander 대신 바로 Flee로 간다.
/// 값은 모두 GameBalanceConfig의 "동물 - 거위".
///
/// - 도주는 NavMesh 경로를 따라간다: 플레이어 반대쪽을 중심으로 12방향 후보 중 실제로 걸어서 갈 수 있고 플레이어에게서 가장 멀어지는
///   지점을 고른다(목표 근처까지 우회하는 경로, 또는 그 방향으로 막히기 전까지 곧게 갈 수 있는 가장 먼 점). 이동 중에 막히면 그 자리에서
///   다시 찾는다(MaxFleeRepaths번까지). 도주를 시작할 때 경로를 못 찾으면(NavMesh가 없는 곳 등) 예전처럼 AnimalFlee의 방향 회피를 쓴다.
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

    // NavMesh 도주 경로. NavMesh는 2D (x, y)를 (x, 0, y)로 놓고 굽는다(몬스터와 같다).
    private const int FleeDirectionCount = 12;          // 플레이어 반대 방향부터 30도씩 좌우로 번갈아 본다
    private const float NavSampleRadius = 2f;           // 후보 지점 근처에서 걸을 수 있는 곳을 찾는 반경
    private const float MinFleeGain = 1f;               // 지금보다 플레이어에게서 이만큼은 더 멀어지는 곳만 고른다
    private const float MaxPathLengthRatio = 2f;        // 도주 거리의 이 배수보다 긴 우회 경로는 버린다
    private const float CornerReachDistance = 0.3f;
    private const int MaxFleeRepaths = 3;               // 이동 중에 막혔을 때 다시 찾는 최대 횟수(한 번 도주당)

    private readonly List<Vector2> fleePath = new List<Vector2>();
    private NavMeshPath navPath;
    private bool navFlee;
    private int fleeCorner;
    private int fleeRepaths;
    private float stuckTime;

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

    private float FleeDistance => config != null ? config.gooseFleeDistance : 24f;
    private float RestDuration => config != null ? config.gooseRestDuration : 0.5f;
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
        stuckTime = 0f;
        fleeRepaths = 0;
        fleePath.Clear();
        // 처음부터 경로를 못 찾으면(NavMesh가 없거나 더 멀어질 곳이 없으면) 이번 도주는 예전 방향 회피로 한다.
        navFlee = TryPlanFleePath();

        CryCount++;
        AudioManager.Instance?.PlaySfx(cryClip, cryVolume);
        float intensity = config != null ? config.animalSoundIntensity : 1f;
        SoundEvents.Emit(transform.position, intensity);
    }

    protected override bool ShouldStopFleeing(float distanceToPlayer)
    {
        fleeTime += Time.deltaTime;
        bool reachedDistance = (Body.position - fleeStart).magnitude >= FleeDistance;
        // 혹시 계속 미끄러지기만 하며 거리를 못 채워도 무한히 도망치지 않게 한다.
        bool tooLong = fleeTime >= FleeDistance / Mathf.Max(0.1f, FleeSpeed) * 2f + 1f;
        if (reachedDistance || tooLong) return true;
        if (navFlee) return ShouldStopNavFlee();

        blockedTime = FleeBlocked || (fleeTime > BlockedGraceTime && !IsActuallyMoving) ? blockedTime + Time.deltaTime : 0f;
        return FleeBlocked ? blockedTime > 0f : blockedTime >= BlockedGraceTime;
    }

    // 경로 끝에 닿았거나 갈 곳이 없으면 멈춘다. 움직이지 못한 채 BlockedGraceTime이 지나면 지금 자리에서 경로를 다시 찾고,
    // MaxFleeRepaths번을 넘기거나 더 갈 곳이 없으면 멈춘다.
    private bool ShouldStopNavFlee()
    {
        while (fleeCorner < fleePath.Count && (fleePath[fleeCorner] - Body.position).sqrMagnitude <= CornerReachDistance * CornerReachDistance)
            fleeCorner++;
        if (fleeCorner >= fleePath.Count) return true;

        stuckTime = fleeTime > BlockedGraceTime && !IsActuallyMoving ? stuckTime + Time.deltaTime : 0f;
        if (stuckTime < BlockedGraceTime) return false;

        stuckTime = 0f;
        if (fleeRepaths >= MaxFleeRepaths) return true;
        fleeRepaths++;
        return !TryPlanFleePath();
    }

    protected override Vector2 FleeDirection()
    {
        if (!navFlee) return base.FleeDirection();
        if (fleeCorner >= fleePath.Count) return Vector2.zero;
        Vector2 toCorner = fleePath[fleeCorner] - Body.position;
        return toCorner.sqrMagnitude > 0.0001f ? toCorner.normalized : Vector2.zero;
    }

    // 지금 자리에서 도주 경로를 새로 찾는다. 찾지 못하면(사방이 막혔거나 더 멀어질 곳이 없으면) false.
    private bool TryPlanFleePath()
    {
        fleePath.Clear();
        fleeCorner = 0;
        if (Player == null) return false;

        Vector2 from = Body.position;
        if (!NavMesh.SamplePosition(ToNav(from), out NavMeshHit start, NavSampleRadius, NavMesh.AllAreas)) return false;
        if (navPath == null) navPath = new NavMeshPath();

        Vector2 playerPosition = Player.position;
        Vector2 away = from - playerPosition;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        float startDistance = Vector2.Distance(from, playerPosition);
        float bestDistance = startDistance + MinFleeGain;

        for (int i = 0; i < FleeDirectionCount; i++)
        {
            // 0, +30, -30, +60, -60, ... 180도 순서. 같은 거리면 플레이어 반대쪽에 가까운 후보가 남는다.
            float angle = (i + 1) / 2 * (360f / FleeDirectionCount) * (i % 2 == 0 ? 1f : -1f);
            Vector2 target = from + (Vector2)(Quaternion.Euler(0f, 0f, angle) * away) * FleeDistance;

            // 목표 근처의 걸을 수 있는 곳까지 벽을 돌아가는 경로.
            if (NavMesh.SamplePosition(ToNav(target), out NavMeshHit end, NavSampleRadius, NavMesh.AllAreas))
                ConsiderFleePath(start.position, end.position, playerPosition, startDistance, ref bestDistance);

            // 목표까지 갈 수 없을 때(맵 경계, 막다른 길): 그 방향으로 막히기 전까지 곧게 갈 수 있는 가장 먼 점.
            NavMesh.Raycast(start.position, ToNav(target), out NavMeshHit edge, NavMesh.AllAreas);
            ConsiderFleePath(start.position, edge.position, playerPosition, startDistance, ref bestDistance);
        }
        return fleePath.Count > 0;
    }

    // 끝점이 지금까지의 후보보다 플레이어에게서 멀고, 실제로 이어진 경로이며, 너무 돌아가지 않고, 가는 길에 플레이어 쪽으로 다가가지 않으면 고른다.
    private void ConsiderFleePath(Vector3 start, Vector3 end, Vector2 playerPosition, float startDistance, ref float bestDistance)
    {
        float endDistance = Vector2.Distance(new Vector2(end.x, end.z), playerPosition);
        if (endDistance <= bestDistance) return;
        if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, navPath) || navPath.status != NavMeshPathStatus.PathComplete) return;

        Vector3[] corners = navPath.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
            if (Vector2.Distance(new Vector2(corners[i].x, corners[i].z), playerPosition) < startDistance) return;
        }
        if (length > FleeDistance * MaxPathLengthRatio) return;

        bestDistance = endDistance;
        fleePath.Clear();
        for (int i = 1; i < corners.Length; i++) fleePath.Add(new Vector2(corners[i].x, corners[i].z));
    }

    private static Vector3 ToNav(Vector2 position) => new Vector3(position.x, 0f, position.y);

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
        // 플레이어가 아직 감지 반경 안에 있으면 놀람 없이 바로 다시 도망친다.
        if (Player != null && Vector2.Distance(Body.position, Player.position) <= AlertRange)
        {
            StartFleeing();
            return;
        }
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
