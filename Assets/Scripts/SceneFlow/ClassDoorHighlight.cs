using UnityEngine;

/// <summary>보호소의 클래스 선택 문(ClassSelectDoor)에 붙인다. 상점에서 클래스가 맞지 않아 구매가 막히면(ShopPurchase.ClassRequirementBlocked)
/// 문 그림을 원래 색과 강조 색 사이에서 부드럽게 깜빡이게 하고, 정해진 시간이 지나면 원래 색으로 되돌린다. 상점 메뉴나 ESC 일시정지로
/// 시간이 멈춰 있어도 끝나도록 실제 시간(unscaled)으로 잰다. 문의 상호작용(ScenePortal)과는 상관없다.</summary>
public class ClassDoorHighlight : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [Tooltip("색을 바꿀 문 그림")]
    [SerializeField] private SpriteRenderer doorRenderer;

    private Color originalColor;
    private bool highlighting;
    private float elapsed;

    private float Duration => config != null ? config.classDoorHighlightDuration : 1.5f;
    private Color HighlightColor => config != null ? config.classDoorHighlightColor : new Color(1f, 0.85f, 0.2f, 1f);
    private float PulsesPerSecond => config != null ? config.classDoorHighlightPulsesPerSecond : 2f;

    private void Awake()
    {
        if (doorRenderer == null) Debug.LogWarning($"{name}: doorRenderer가 연결되지 않아 클래스 선택 문을 강조하지 않는다.");
        else originalColor = doorRenderer.color;
    }

    private void OnEnable() => ShopPurchase.ClassRequirementBlocked += HandleBlocked;

    private void OnDisable()
    {
        ShopPurchase.ClassRequirementBlocked -= HandleBlocked;
        StopHighlight();
    }

    // 이미 강조 중이면 겹치지 않고 시간만 처음부터 다시 잰다.
    private void HandleBlocked(PlayerClass required)
    {
        if (doorRenderer == null) return;
        highlighting = true;
        elapsed = 0f;
    }

    private void Update()
    {
        if (!highlighting) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= Duration)
        {
            StopHighlight();
            return;
        }
        // 0 → 1 → 0으로 부드럽게 오가는 펄스(시작과 끝은 원래 색).
        float pulse = 0.5f - 0.5f * Mathf.Cos(elapsed * PulsesPerSecond * 2f * Mathf.PI);
        doorRenderer.color = Color.Lerp(originalColor, HighlightColor, pulse);
    }

    private void StopHighlight()
    {
        highlighting = false;
        elapsed = 0f;
        if (doorRenderer != null) doorRenderer.color = originalColor;
    }
}
