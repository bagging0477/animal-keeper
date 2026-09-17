using UnityEngine;
using UnityEngine.UI;

public class GadgetInventoryUI : MonoBehaviour
{
    [SerializeField] private Text gadgetText;

    private int lastMines = int.MinValue;
    private int lastBombs = int.MinValue;

    private void Update()
    {
        if (gadgetText == null) return;

        int mines = GameManager.Instance != null ? GameManager.Instance.MineCount : 0;
        int bombs = GameManager.Instance != null ? GameManager.Instance.BombCount : 0;

        // 값이 바뀌지 않았는데도 매 프레임 문자열을 새로 만들어 대입하면 불필요한 GC 할당과
        // 캔버스 리빌드가 계속 쌓인다 - 실제로 바뀌었을 때만 갱신한다.
        if (mines == lastMines && bombs == lastBombs) return;

        gadgetText.text = $"지뢰(Q): {mines}\n폭탄(R): {bombs}";
        lastMines = mines;
        lastBombs = bombs;
    }
}
