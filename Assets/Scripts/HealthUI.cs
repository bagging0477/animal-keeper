using UnityEngine;
using UnityEngine.UI;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Text healthText;
    [SerializeField] [Range(0f, 1f)] private float lowHealthRatio = 0.3f;
    [SerializeField] private Color normalColor = new Color(0.3f, 0.8f, 0.35f);
    [SerializeField] private Color lowColor = new Color(0.85f, 0.2f, 0.2f);
    [SerializeField] private float fillChangeSpeed = 0.6f;

    private float displayedRatio = 1f;
    private bool initialized;

    private void Update()
    {
        int health = GameManager.Instance != null ? GameManager.Instance.Health : GameManager.MaxHealth;
        float targetRatio = Mathf.Clamp01(health / (float)GameManager.MaxHealth);

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
            float colorT = Mathf.Clamp01(Mathf.InverseLerp(1f, lowHealthRatio, displayedRatio));
            fillImage.color = Color.Lerp(normalColor, lowColor, colorT);
        }

        if (healthText != null) healthText.text = $"{health}%";
    }
}
