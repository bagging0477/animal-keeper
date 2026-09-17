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
    private int lastDisplayedHealth = int.MinValue;

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

        // Text.text에 대입하면 값이 그대로여도 UI 캔버스 다시 그리기가 걸린다. 매 프레임 문자열을
        // 새로 만들어 대입하던 걸, 실제로 표시되는 정수가 바뀌었을 때만 하도록 바꿔서 불필요한
        // GC 할당과 캔버스 리빌드를 없앤다 - 여러 UI가 매 프레임 이렇게 쌓이면 전반적인 프레임
        // 저하(몬스터 이동까지 버벅여 보이는 원인)로 이어질 수 있다.
        if (healthText != null && health != lastDisplayedHealth)
        {
            healthText.text = $"{health}%";
            lastDisplayedHealth = health;
        }
    }
}
