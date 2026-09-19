using UnityEngine;

/// <summary>이동 방향에 따라 스프라이트를 좌우로 뒤집는(flipX) 범용 컴포넌트. 세로 방향 성분만
/// 있을 때(위/아래로만 이동)는 좌우 방향을 바꾸지 않고 마지막으로 바라보던 방향을 그대로
/// 유지한다 - 동물 그림에 위/아래 전용 방향이 따로 없기 때문이다. 정지 상태(Vector2.zero)일
/// 때도 마찬가지로 마지막 방향을 유지한다. AnimalWander/AnimalFlee처럼 이동 방향을 계산하는
/// 스크립트가 매 프레임 SetMoveDirection()만 호출해주면 되고, 실제 스프라이트를 어떤 방식
/// (Animator, SpriteSheetAnimator, SimpleFrameAnimator 등)으로 그리는지는 신경 쓰지 않는다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AnimalFacingFlipper : MonoBehaviour
{
    [Tooltip("가로 방향 성분의 절댓값이 이 값보다 작으면(거의 위/아래로만 이동) 좌우 방향을 바꾸지 않는다.")]
    [SerializeField] private float horizontalDeadzone = 0.01f;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>매 프레임 이동 방향(월드 기준, 정규화 여부 상관없음)을 넘겨주면 그에 맞춰 flipX를
    /// 갱신한다. 가로 성분이 거의 없으면(정지 포함) 아무것도 하지 않아 마지막 방향이 유지된다.</summary>
    public void SetMoveDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) < horizontalDeadzone) return;

        spriteRenderer.flipX = direction.x < 0f;
    }
}
