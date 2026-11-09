using UnityEngine;

/// <summary>트럭 납품 직원(막내 수의사)의 그림. 평소에는 대기 순서(idleSequence - 숨쉬기·눈 깜빡임, 같은 그림을 여러 번 넣어 길이를 맞춘다)를
/// 반복하고, PlayTake()를 부르면 받기 그림(takeFrames - 팔 벌리기, 하트)을 한 번 재생한 뒤 대기로 돌아간다.
/// 받기 재생 중에 다시 부르면 처음부터 다시 재생한다. 일시정지(timeScale 0) 중에는 멈춘다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DeliveryClerkAnimator : MonoBehaviour
{
    [Tooltip("대기 때 반복할 그림 순서. 같은 그림을 여러 번 넣으면 그만큼 오래 보인다.")]
    [SerializeField] private Sprite[] idleSequence;
    [Tooltip("대기 순서를 넘기는 속도(장/초)")]
    [SerializeField] private float idleFrameRate = 4f;
    [Tooltip("동물을 받았을 때 한 번 재생할 그림 순서")]
    [SerializeField] private Sprite[] takeFrames;
    [Tooltip("받기 그림을 넘기는 속도(장/초)")]
    [SerializeField] private float takeFrameRate = 6f;

    private SpriteRenderer spriteRenderer;
    private int frameIndex;
    private float frameTimer;
    private bool playingTake;

    /// <summary>받기 그림을 재생 중인지.</summary>
    public bool IsPlayingTake => playingTake;

    /// <summary>받기 재생을 시작한 횟수(이 씬에서).</summary>
    public int TakePlayCount { get; private set; }

    /// <summary>지금 보이는 그림.</summary>
    public Sprite CurrentSprite => spriteRenderer != null ? spriteRenderer.sprite : null;

    public Sprite[] TakeFrames => takeFrames;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        ShowIdle(0);
    }

    /// <summary>받기 그림을 처음부터 한 번 재생한다. 끝나면 대기 순서 처음으로 돌아간다.</summary>
    public void PlayTake()
    {
        if (takeFrames == null || takeFrames.Length == 0) return;
        playingTake = true;
        TakePlayCount++;
        frameIndex = 0;
        frameTimer = 0f;
        spriteRenderer.sprite = takeFrames[0];
    }

    private void Update()
    {
        bool take = playingTake;
        Sprite[] frames = take ? takeFrames : idleSequence;
        if (frames == null || frames.Length == 0) return;
        float rate = Mathf.Max(0.01f, take ? takeFrameRate : idleFrameRate);
        frameTimer += Time.deltaTime;
        while (frameTimer >= 1f / rate)
        {
            frameTimer -= 1f / rate;
            frameIndex++;
            if (frameIndex < frames.Length) continue;
            if (take)
            {
                playingTake = false;
                frameTimer = 0f;
                ShowIdle(0);
                return;
            }
            frameIndex = 0;
        }
        spriteRenderer.sprite = frames[frameIndex];
    }

    private void ShowIdle(int index)
    {
        frameIndex = index;
        if (spriteRenderer != null && idleSequence != null && idleSequence.Length > 0) spriteRenderer.sprite = idleSequence[index];
    }
}
