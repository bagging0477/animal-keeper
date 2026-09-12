using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;

    [Header("Aim")]
    [SerializeField] private Camera aimCamera;

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private float currentSpeed;
    private float exhaustionTimer;

    /// <summary>마우스가 가리키는 방향(월드 기준, 정규화된 벡터). 무기 조준, 시야 범위 등에서 사용하세요.</summary>
    public Vector2 LookDirection { get; private set; } = Vector2.up;

    /// <summary>LookDirection을 각도(degrees, atan2 기준)로 표현한 값.</summary>
    public float LookAngle => Mathf.Atan2(LookDirection.y, LookDirection.x) * Mathf.Rad2Deg;

    public float MaxStamina => config != null ? config.maxStamina : 100f;
    public float Stamina { get; private set; }
    public bool IsSprinting { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // 벽 콜라이더 모서리를 스칠 때 마찰로 인해 미세하게 걸리는(corner-catching) 떨림을 없애기 위해 무마찰 재질을 사용한다.
        rb.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };

        if (aimCamera == null) aimCamera = Camera.main;

        Stamina = MaxStamina;
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

        UpdateStamina(kb);
        UpdateLookDirection();
    }

    private void UpdateStamina(Keyboard kb)
    {
        bool sprintKeyHeld = kb != null && kb.leftShiftKey.isPressed;

        if (exhaustionTimer > 0f) exhaustionTimer -= Time.deltaTime;

        IsSprinting = sprintKeyHeld && exhaustionTimer <= 0f && Stamina > 0f;

        if (IsSprinting)
        {
            float drainPerSecond = config != null ? config.sprintStaminaDrainPerSecond : 25f;
            Stamina -= drainPerSecond * Time.deltaTime;

            if (Stamina <= 0f)
            {
                Stamina = 0f;
                IsSprinting = false;
                exhaustionTimer = config != null ? config.staminaExhaustionCooldown : 1.5f;
            }
        }
        else
        {
            float regenPerSecond = config != null ? config.staminaRegenPerSecond : 15f;
            Stamina = Mathf.Min(MaxStamina, Stamina + regenPerSecond * Time.deltaTime);
        }

        float baseSpeed = config != null ? config.playerMoveSpeed : 3.75f;
        float sprintSpeed = config != null ? config.PlayerSprintSpeed : baseSpeed * 1.25f;
        currentSpeed = IsSprinting ? sprintSpeed : baseSpeed;
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
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * currentSpeed * Time.fixedDeltaTime);
    }
}
