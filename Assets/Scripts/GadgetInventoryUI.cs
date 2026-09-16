using UnityEngine;
using UnityEngine.UI;

public class GadgetInventoryUI : MonoBehaviour
{
    [SerializeField] private Text gadgetText;

    private void Update()
    {
        if (gadgetText == null) return;

        int mines = GameManager.Instance != null ? GameManager.Instance.MineCount : 0;
        int bombs = GameManager.Instance != null ? GameManager.Instance.BombCount : 0;
        gadgetText.text = $"지뢰(Q): {mines}\n폭탄(R): {bombs}";
    }
}
