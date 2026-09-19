using UnityEngine;

/// <summary>미리 슬라이스해둔 개별 Sprite들을 순환 재생하는 간단한 프레임 애니메이터. Unity의
/// Animator/AnimatorController 없이 Idle 스프라이트 하나와 Walk 프레임 배열만으로 동작해서,
/// AnimatorController의 복잡한 내부 직렬화 포맷에 기댈 필요가 없다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SimpleFrameAnimator : MonoBehaviour
{
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private float frameRate = 8f;

    private SpriteRenderer spriteRenderer;
    private bool isMoving;
    private int frameIndex;
    private float frameTimer;

    /// <summary>true면 walkFrames를 순환 재생하고, false면 idleSprite로 고정한다. 상태가 실제로
    /// 바뀔 때만 프레임 인덱스를 리셋해서, 매 프레임 같은 값으로 호출해도 애니메이션이 끊기지 않는다.</summary>
    public bool IsMoving
    {
        get => isMoving;
        set
        {
            if (isMoving == value) return;
            isMoving = value;
            frameIndex = 0;
            frameTimer = 0f;
        }
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (!isMoving || walkFrames == null || walkFrames.Length == 0)
        {
            if (idleSprite != null) spriteRenderer.sprite = idleSprite;
            return;
        }

        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % walkFrames.Length;
        }

        spriteRenderer.sprite = walkFrames[frameIndex % walkFrames.Length];
    }
}
