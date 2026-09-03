using UnityEngine;
using UnityEngine.UI;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private Text healthText;

    private void Update()
    {
        int health = GameManager.Instance != null ? GameManager.Instance.Health : GameManager.MaxHealth;
        if (healthText != null) healthText.text = $"체력: {health}%";
    }
}
