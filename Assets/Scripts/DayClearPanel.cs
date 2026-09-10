using UnityEngine;
using UnityEngine.UI;

public class DayClearPanel : MonoBehaviour
{
    [SerializeField] private Button confirmButton;
    [SerializeField] private Text titleText;

    private void Awake()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(Hide);
    }

    public void Show(string title)
    {
        if (titleText != null && !string.IsNullOrEmpty(title)) titleText.text = title;
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}
