using UnityEngine;

/// <summary>미리 슬라이스해둔 개별 Sprite들을 순환 재생하는 간단한 프레임 애니메이터. Unity의
/// Animator/AnimatorController 없이 Idle/Walk/Flee 스프라이트 배열만으로 동작해서,
/// AnimatorController의 복잡한 내부 직렬화 포맷(Write Defaults, 상태 전환 등)에 기댈 필요가 없다.
/// Walk와 Flee를 별도 배열로 나눈 이유는 평소 배회 걸음과 도주 질주가 서로 다른 프레임 세트를
/// 쓰기 때문이다 - Flee가 지정돼 있으면 Walk보다 우선한다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SimpleFrameAnimator : MonoBehaviour
{
    private enum Mode { Idle, Walk, Flee }

    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] fleeFrames;
    [SerializeField] private float frameRate = 8f;

    private SpriteRenderer spriteRenderer;
    private bool isMoving;
    private bool isFleeing;
    private Mode mode;
    private int frameIndex;
    private float frameTimer;

    /// <summary>Idle 스프라이트. 인벤토리 아이콘 기본값으로 쓰인다.</summary>
    public Sprite IdleSprite => idleSprite;

    /// <summary>true면 walkFrames를 순환 재생하고, false면(그리고 IsFleeing도 false면) idleSprite로
    /// 고정한다. IsFleeing이 true인 동안에는 이 값과 무관하게 Flee 프레임이 우선한다.</summary>
    public bool IsMoving
    {
        get => isMoving;
        set
        {
            isMoving = value;
            RefreshMode();
        }
    }

    /// <summary>true면 fleeFrames를 순환 재생한다 - Walk/Idle보다 항상 우선한다.</summary>
    public bool IsFleeing
    {
        get => isFleeing;
        set
        {
            isFleeing = value;
            RefreshMode();
        }
    }

    // 실제로 재생 모드가 바뀔 때만 프레임 인덱스를 리셋해서, 매 프레임 같은 값으로
    // IsMoving/IsFleeing을 설정해도 애니메이션이 끊기지 않는다.
    private void RefreshMode()
    {
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

        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % frames.Length;
        }

        spriteRenderer.sprite = frames[frameIndex % frames.Length];
    }
}
