using UnityEngine;

/// <summary>미리 슬라이스해둔 개별 Sprite들을 순환 재생하는 간단한 프레임 애니메이터. Unity의
/// Animator/AnimatorController 없이 Idle/Walk/Flee/Attack/Hurt/Death 스프라이트 배열만으로 동작해서,
/// AnimatorController의 복잡한 내부 직렬화 포맷(Write Defaults, 상태 전환 등)에 기댈 필요가 없다.
/// Walk와 Flee를 별도 배열로 나눈 이유는 평소 배회 걸음과 도주/추격처럼 급박한 움직임이 서로 다른
/// 프레임 세트를 쓰기 때문이다 - Flee가 지정돼 있으면 Walk보다 우선한다. Attack/Hurt는 그 중에서도
/// 항상 최우선인 1회성 동작으로, 재생 중에는 IsMoving/IsFleeing이 바뀌어도 끼어들지 않고 끝까지 재생된
/// 뒤에야 그 시점의 최신 이동 상태로 돌아간다. Death만 예외로, 끝까지 재생한 뒤 마지막 프레임에서
/// 영구히 멈춘다(시체 연출) - 그 뒤로는 다른 어떤 상태 변경도 받아들이지 않는다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SimpleFrameAnimator : MonoBehaviour
{
    private enum Mode { Idle, Walk, Flee, Attack, Hurt, Dead }

    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] fleeFrames;
    [SerializeField] private float frameRate = 8f;

    [Tooltip("Flee 모드 전용 재생 속도. 0이면 frameRate를 그대로 쓴다(동물처럼 Flee가 전용 스프라이트를 " +
        "쓰는 경우). 몬스터처럼 fleeFrames에 walkFrames와 같은 스프라이트를 재사용해 '평소보다 급박하게 " +
        "움직인다'는 느낌만 속도 차이로 표현하고 싶을 때 frameRate보다 높은 값을 지정한다.")]
    [SerializeField] private float fleeFrameRate = 0f;

    [Header("공격 - PlayRandomAttack() 호출 시 둘 중 하나를 50:50으로 골라 한 번만 재생한다")]
    [SerializeField] private Sprite[] attack1Frames;
    [SerializeField] private Sprite[] attack2Frames;

    [Header("피격/사망 - 둘 다 없어도 되고, 있으면 각각 PlayHurt()/PlayDeath()로 재생한다")]
    [SerializeField] private Sprite[] hurtFrames;
    [SerializeField] private Sprite[] deathFrames;

    private SpriteRenderer spriteRenderer;
    private bool isMoving;
    private bool isFleeing;
    private Mode mode;
    private int frameIndex;
    private float frameTimer;
    private Sprite[] activeOneShotFrames;
    private float oneShotTimeRemaining;

    /// <summary>Idle 스프라이트. 인벤토리 아이콘 기본값으로 쓰인다.</summary>
    public Sprite IdleSprite => idleSprite;

    /// <summary>true면 walkFrames를 순환 재생하고, false면(그리고 IsFleeing도 false면) idleSprite로
    /// 고정한다. IsFleeing이 true인 동안에는 이 값과 무관하게 Flee 프레임이 우선하고, 1회성 동작
    /// (Attack/Hurt/Death) 재생 중에는 끝날 때까지(Death는 영구히) 이 값 변경이 반영되지 않는다.</summary>
    public bool IsMoving
    {
        get => isMoving;
        set
        {
            isMoving = value;
            RefreshMode();
        }
    }

    /// <summary>true면 fleeFrames를 순환 재생한다 - Walk/Idle보다 항상 우선한다(1회성 동작 재생 중은 예외).
    /// 동물의 도주뿐 아니라, 몬스터가 Chase/Investigate처럼 급박하게 움직이는 상태를 표현할 때도
    /// 같은 슬롯을 재사용한다 - 별도의 "긴급 이동" 필드를 새로 만들 필요 없이 이미 있는 우선순위
    /// 메커니즘을 그대로 쓴다.</summary>
    public bool IsFleeing
    {
        get => isFleeing;
        set
        {
            isFleeing = value;
            RefreshMode();
        }
    }

    /// <summary>Attack1Frames/Attack2Frames 중 하나를 50:50 확률로 골라 한 번만(반복 없이) 재생한다.
    /// 두 배열 중 하나가 비어 있으면 있는 쪽을 그대로 쓰고, 둘 다 비어 있으면 아무 일도 하지 않는다.</summary>
    public void PlayRandomAttack()
    {
        if (mode == Mode.Dead) return;

        bool hasFirst = attack1Frames != null && attack1Frames.Length > 0;
        bool hasSecond = attack2Frames != null && attack2Frames.Length > 0;
        if (!hasFirst && !hasSecond) return;

        Sprite[] chosen = hasFirst && hasSecond
            ? (Random.value < 0.5f ? attack1Frames : attack2Frames)
            : hasFirst ? attack1Frames : attack2Frames;
        StartOneShot(chosen, Mode.Attack);
    }

    /// <summary>hurtFrames를 한 번만(반복 없이) 재생한 뒤 그 시점의 최신 이동 상태로 돌아간다.
    /// hurtFrames가 비어 있으면 아무 일도 하지 않는다.</summary>
    public void PlayHurt()
    {
        if (mode == Mode.Dead) return;
        StartOneShot(hurtFrames, Mode.Hurt);
    }

    /// <summary>deathFrames를 한 번만 재생한 뒤 마지막 프레임에서 영구히 멈춘다 - Attack/Hurt와 달리
    /// 끝나도 이동 상태로 복귀하지 않는다(시체 연출). 이후로는 IsMoving/IsFleeing/PlayHurt/
    /// PlayRandomAttack을 호출해도 전부 무시된다. deathFrames가 비어 있으면 아무 일도 하지 않는다.</summary>
    public void PlayDeath()
    {
        if (mode == Mode.Dead) return;
        StartOneShot(deathFrames, Mode.Dead);
    }

    private void StartOneShot(Sprite[] frames, Mode oneShotMode)
    {
        if (frames == null || frames.Length == 0) return;

        activeOneShotFrames = frames;
        mode = oneShotMode;
        frameIndex = 0;
        frameTimer = 0f;
        oneShotTimeRemaining = frames.Length / Mathf.Max(frameRate, 0.01f);
    }

    // 실제로 재생 모드가 바뀔 때만 프레임 인덱스를 리셋해서, 매 프레임 같은 값으로
    // IsMoving/IsFleeing을 설정해도 애니메이션이 끊기지 않는다. 1회성 동작 재생 중에는 끝날 때까지
    // 무시한다(Update의 타이머가 만료됐을 때만 빠져나간다 - Dead는 애초에 끝나도 빠져나가지 않는다).
    private void RefreshMode()
    {
        if (mode == Mode.Attack || mode == Mode.Hurt || mode == Mode.Dead) return;

        Mode next = isFleeing ? Mode.Flee : isMoving ? Mode.Walk : Mode.Idle;
        if (next == mode) return;

        mode = next;
        frameIndex = 0;
        frameTimer = 0f;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (mode == Mode.Attack || mode == Mode.Hurt || mode == Mode.Dead)
        {
            UpdateOneShotFrame();
            return;
        }

        Sprite[] frames = mode switch
        {
            Mode.Flee => fleeFrames,
            Mode.Walk => walkFrames,
            _ => null
        };

        if (frames == null || frames.Length == 0)
        {
            if (idleSprite != null) spriteRenderer.sprite = idleSprite;
            return;
        }

        float effectiveRate = mode == Mode.Flee && fleeFrameRate > 0f ? fleeFrameRate : frameRate;
        frameTimer += Time.deltaTime;
        float frameDuration = effectiveRate > 0f ? 1f / effectiveRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % frames.Length;
        }

        spriteRenderer.sprite = frames[frameIndex % frames.Length];
    }

    private void UpdateOneShotFrame()
    {
        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            // 반복하지 않는 1회성 동작이라 마지막 프레임에서 멈추고 되감지 않는다.
            frameIndex = Mathf.Min(frameIndex + 1, activeOneShotFrames.Length - 1);
        }

        spriteRenderer.sprite = activeOneShotFrames[Mathf.Clamp(frameIndex, 0, activeOneShotFrames.Length - 1)];

        // Dead는 끝까지 재생한 뒤에도 마지막 프레임에 영구히 머문다 - 이동 상태로 복귀하지 않는다.
        if (mode == Mode.Dead) return;

        oneShotTimeRemaining -= Time.deltaTime;
        if (oneShotTimeRemaining <= 0f)
        {
            mode = isFleeing ? Mode.Flee : isMoving ? Mode.Walk : Mode.Idle;
            frameIndex = 0;
            frameTimer = 0f;
        }
    }
}
