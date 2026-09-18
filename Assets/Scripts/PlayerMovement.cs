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

    public float MaxStamina => config != null ? config.maxStamina : 70f;
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
            float baseDrainPerSecond = config != null ? config.sprintStaminaDrainPerSecond : 25f;
            float drainMultiplier = config != null && GameManager.Instance != null
                ? config.GetClassStaminaDrainMultiplier(GameManager.Instance.CurrentClass)
                : 1f;
            Stamina -= baseDrainPerSecond * drainMultiplier * Time.deltaTime;

            if (Stamina <= 0f)
            {
                Stamina = 0f;
                IsSprinting = false;
                exhaustionTimer = config != null ? config.staminaExhaustionCooldown : 1.5f;
            }
        }
        else
        {
            float regenPerSecond = config != null ? config.staminaRegenPerSecond : 12f;
            Stamina = Mathf.Min(MaxStamina, Stamina + regenPerSecond * Time.deltaTime);
        }

        float classMoveMultiplier = config != null && GameManager.Instance != null
            ? config.GetClassMoveSpeedMultiplier(GameManager.Instance.CurrentClass)
            : 1f;
        float classSprintMultiplier = config != null && GameManager.Instance != null
            ? config.GetClassSprintSpeedMultiplier(GameManager.Instance.CurrentClass)
            : 1.25f;

        float baseSpeed = (config != null ? config.playerMoveSpeed : 3.75f) * classMoveMultiplier;
        float sprintSpeed = baseSpeed * classSprintMultiplier;
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

        // 몸통 스프라이트(player_base.png)가 기본적으로 +X(오른쪽) 방향을 바라보도록 그려져 있어서,
        // atan2 기준인 LookAngle을 별도 각도 보정 없이 그대로 회전에 사용할 수 있다. WASD 이동
        // (moveInput 기반 MovePosition)과는 완전히 분리된 값이라, 다른 방향을 바라보며 옆으로
        // 이동하는 것도 그대로 가능하다.
        rb.MoveRotation(LookAngle);
    }
}
