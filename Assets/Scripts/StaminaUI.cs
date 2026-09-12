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
    [SerializeField] private float fillChangeSpeed = 2f;

    private PlayerMovement player;
    private float displayedRatio = 1f;
    private bool initialized;

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
            fillImage.color = player != null && player.IsSprinting ? sprintingColor : normalColor;
        }

        if (staminaText != null) staminaText.text = $"{Mathf.CeilToInt(stamina)}";
    }
}
