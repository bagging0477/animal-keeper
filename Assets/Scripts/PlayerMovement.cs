using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    [Header("Aim")]
    [Tooltip("회전시킬 대상. 비워두면 이 오브젝트 자신을 회전시킵니다.")]
    [SerializeField] private Transform aimTransform;
    [Tooltip("스프라이트가 기본적으로 위쪽(Y+)을 바라보고 있다면 -90, 오른쪽(X+)을 바라보고 있다면 0으로 설정하세요.")]
    [SerializeField] private float spriteAngleOffset = -90f;
    [SerializeField] private Camera aimCamera;

    private Rigidbody2D rb;
    private Vector2 moveInput;

    /// <summary>마우스가 가리키는 방향(월드 기준, 정규화된 벡터). 무기 조준, 시야 범위 등에서 사용하세요.</summary>
    public Vector2 LookDirection { get; private set; } = Vector2.up;

    /// <summary>LookDirection을 각도(degrees, atan2 기준)로 표현한 값.</summary>
    public float LookAngle => Mathf.Atan2(LookDirection.y, LookDirection.x) * Mathf.Rad2Deg;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // 벽 콜라이더 모서리를 스칠 때 마찰로 인해 미세하게 걸리는(corner-catching) 떨림을 없애기 위해 무마찰 재질을 사용한다.
        rb.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };

        if (aimTransform == null) aimTransform = transform;
        if (aimCamera == null) aimCamera = Camera.main;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            moveInput = Vector2.zero;
        }
        else
        {
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            moveInput = new Vector2(x, y).normalized;
        }

        UpdateLookDirection();
    }

    private void UpdateLookDirection()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || aimCamera == null) return;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = aimCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, aimCamera.nearClipPlane));
        Vector2 direction = (Vector2)worldPos - (Vector2)transform.position;

        if (direction.sqrMagnitude < 0.0001f) return;

        LookDirection = direction.normalized;

        float angle = Mathf.Atan2(LookDirection.y, LookDirection.x) * Mathf.Rad2Deg;
        aimTransform.rotation = Quaternion.Euler(0f, 0f, angle + spriteAngleOffset);
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * moveSpeed * Time.fixedDeltaTime);
    }
}
