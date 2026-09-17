using UnityEngine;
using UnityEngine.UI;

/// <summary>플레이어의 스태미나를 표시하는 UI. HealthUI와 달리 PlayerMovement는 씬마다 존재하는
/// "Player" 오브젝트에 붙어있으므로 GameManager처럼 정적 싱글턴이 아니라 태그로 찾는다.</summary>
public class StaminaUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Text staminaText;
    [SerializeField] private Color normalColor = new Color(0.95f, 0.8f, 0.25f);
    [SerializeField] private Color sprintingColor = new Color(0.95f, 0.5f, 0.15f);
    [SerializeField] [Range(0f, 1f)] private float lowStaminaRatio = 0.25f;
    [SerializeField] private Color lowColor = new Color(0.85f, 0.2f, 0.2f);
    [SerializeField] private float fillChangeSpeed = 2f;

    private PlayerMovement player;
    private float displayedRatio = 1f;
    private bool initialized;
    private int lastDisplayedStamina = int.MinValue;

    private void Update()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.GetComponent<PlayerMovement>();
        }

        float stamina = player != null ? player.Stamina : 0f;
        float maxStamina = player != null ? player.MaxStamina : 100f;
        float targetRatio = maxStamina > 0f ? Mathf.Clamp01(stamina / maxStamina) : 0f;

        if (!initialized)
        {
            displayedRatio = targetRatio;
            initialized = true;
        }
        else
        {
            displayedRatio = Mathf.MoveTowards(displayedRatio, targetRatio, fillChangeSpeed * Time.deltaTime);
        }

        if (fillImage != null)
        {
            fillImage.fillAmount = displayedRatio;

            // 기존에는 스프린트 중인지 여부로만 색이 바뀌어서, 스프린트를 멈추면 스태미나가
            // 거의 바닥난 상태여도 평소와 같은 노란색으로 보였다 - 잔량이 실제로 얼마나
            // 남았는지와 무관하게 경고가 전혀 안 들어가는 문제였다. HealthUI와 동일하게
            // 잔량이 낮을수록 경고색(빨강)으로 섞이게 해서, 스프린트 여부와 별개로 실제
            // 스태미나 부족을 항상 눈에 띄게 알린다.
            Color baseColor = player != null && player.IsSprinting ? sprintingColor : normalColor;
            float lowColorT = Mathf.Clamp01(Mathf.InverseLerp(lowStaminaRatio, 0f, displayedRatio));
            fillImage.color = Color.Lerp(baseColor, lowColor, lowColorT);
        }

        // Text.text 대입은 값이 그대로여도 캔버스 리빌드를 유발하므로, 표시 정수가 실제로
        // 바뀌었을 때만 다시 대입한다(HealthUI와 동일한 이유).
        int displayedStamina = Mathf.CeilToInt(stamina);
        if (staminaText != null && displayedStamina != lastDisplayedStamina)
        {
            staminaText.text = $"{displayedStamina}";
            lastDisplayedStamina = displayedStamina;
        }
    }
}
