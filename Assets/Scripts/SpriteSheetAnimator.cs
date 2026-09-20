using UnityEngine;

/// <summary>애니메이터가 재생할 수 있는 세 가지 프레임 세트. Alert는 반복되지 않는 1회성 동작(예:
/// 놀라서 일어나는 모션)이라, 마지막 프레임까지 재생한 뒤 그 자리에서 멈춰 있는다.</summary>
public enum AnimalAnimState { Idle, Alert, Moving }

/// <summary>Elthen 스타일 스프라이트 시트(각 애니메이션이 한 행에 가로로 나열된 32x32 프레임 그리드)에서
/// Idle/Alert/Movement 세 벌(전부 선택 사항이지만 Idle은 항상 있어야 한다)을 잘라내 재생하는 범용
/// 프레임 애니메이터. Alert나 Movement가 필요 없는 동물은 해당 frameCount를 0으로 두면 된다. 실제
/// 슬라이싱은 Sprite.Create로 런타임에 수행하므로, 텍스처를 Unity의 스프라이트 에디터로 미리 잘라둘
/// 필요가 없다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteSheetAnimator : MonoBehaviour
{
    [Header("시트")]
    [SerializeField] private Texture2D sheet;
    [SerializeField] private int frameSize = 32;
    [SerializeField] private float pixelsPerUnit = 16f;
    [SerializeField] private float frameRate = 8f;

    [Header("Idle - row는 시트를 위에서부터 셌을 때(0 = 맨 위 행) 몇 번째 행인지, startColumn은 그 행에서 몇 번째 열(프레임)부터 쓸지")]
    [SerializeField] private int idleRow;
    [SerializeField] private int idleStartColumn;
    [SerializeField] private int idleFrameCount = 1;

    [Header("Alert - 반복되지 않는 1회성 동작(예: 놀라서 일어나는 모션). 필요 없으면 frameCount를 0으로 둔다")]
    [SerializeField] private int alertRow;
    [SerializeField] private int alertStartColumn;
    [SerializeField] private int alertFrameCount;

    [Header("Movement (이동/도망) - 필요 없으면 frameCount를 0으로 둔다")]
    [SerializeField] private int movementRow;
    [SerializeField] private int movementStartColumn;
    [SerializeField] private int movementFrameCount;

    private SpriteRenderer spriteRenderer;
    private Sprite[] idleFrames;
    private Sprite[] alertFrames;
    private Sprite[] movementFrames;
    private AnimalAnimState state;
    private int frameIndex;
    private float frameTimer;

    /// <summary>Idle 애니메이션의 첫 프레임. 인벤토리 아이콘 기본값으로 쓰인다 - Awake에서 슬라이싱이
    /// 끝난 뒤(늦어도 다른 컴포넌트의 Start 시점)라면 항상 값이 채워져 있다.</summary>
    public Sprite IdleFirstFrame => idleFrames != null && idleFrames.Length > 0 ? idleFrames[0] : null;

    /// <summary>Idle/Alert/Moving 중 하나로 전환한다. Alert나 Movement 프레임이 설정되지 않은
    /// 상태로 전환을 요청하면 Idle로 대체된다. 상태가 실제로 바뀔 때만 프레임 인덱스를 리셋해서,
    /// 매 프레임 같은 값으로 호출해도 애니메이션이 끊기지 않는다.</summary>
    public AnimalAnimState State
    {
        get => state;
        set
        {
            if (value == AnimalAnimState.Alert && alertFrames == null) value = AnimalAnimState.Idle;
            if (value == AnimalAnimState.Moving && movementFrames == null) value = AnimalAnimState.Idle;
            if (state == value) return;

            state = value;
            frameIndex = 0;
            frameTimer = 0f;
        }
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (sheet == null)
        {
            Debug.LogWarning($"{name}: SpriteSheetAnimator에 시트 텍스처가 연결되지 않았습니다.");
            return;
        }

        idleFrames = SliceRow(idleRow, idleStartColumn, idleFrameCount);
        alertFrames = alertFrameCount > 0 ? SliceRow(alertRow, alertStartColumn, alertFrameCount) : null;
        movementFrames = movementFrameCount > 0 ? SliceRow(movementRow, movementStartColumn, movementFrameCount) : null;
    }

    // 시트 이미지를 위에서부터 보면서 row를 셌더라도(0 = 맨 위 행), 텍스처 좌표는 왼쪽 아래가
    // 원점이라 실제 Rect를 구할 때는 아래에서부터 센 행 번호로 뒤집어야 한다. startColumn은 그
    // 행에서 몇 번째 열(프레임)부터 잘라낼지 - 0이면 맨 왼쪽 프레임부터.
    private Sprite[] SliceRow(int row, int startColumn, int count)
    {
        int rowsInSheet = sheet.height / frameSize;
        int flippedRow = rowsInSheet - 1 - row;

        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            Rect rect = new Rect((startColumn + i) * frameSize, flippedRow * frameSize, frameSize, frameSize);
            frames[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }
        return frames;
    }

    private void Update()
    {
        Sprite[] frames = state switch
        {
            AnimalAnimState.Alert => alertFrames,
            AnimalAnimState.Moving => movementFrames,
            _ => idleFrames
        };
        if (frames == null || frames.Length == 0) return;

        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;
        if (frameDuration > 0f && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            bool isOneShot = state == AnimalAnimState.Alert;
            if (isOneShot)
            {
                // 1회성 동작은 마지막 프레임에서 멈추고 되감지 않는다 - 계속 반복되면 "일어나는" 동작이
                // 아니라 웅크렸다 일어나기를 반복하는 것처럼 보인다.
                if (frameIndex < frames.Length - 1) frameIndex++;
            }
            else
            {
                frameIndex = (frameIndex + 1) % frames.Length;
            }
        }

        spriteRenderer.sprite = frames[frameIndex % frames.Length];
    }
}
