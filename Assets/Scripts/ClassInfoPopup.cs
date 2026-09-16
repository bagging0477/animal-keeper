using UnityEngine;
using UnityEngine.UI;

/// <summary>ClassSelectScene의 직업 스탠드(ClassInteractPoint) 3개가 공유하는 정보 팝업.
/// 매 프레임 Show가 호출되지 않으면(=아무 스탠드도 범위 안에 없으면) LateUpdate에서 자동으로 숨긴다 -
/// SharedPrompt처럼 별도 "이번 프레임에 누가 먼저 껐는지" 조율 없이도 여러 스탠드가 안전하게 공유할 수 있다.</summary>
public class ClassInfoPopup : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Text nameText;
    [SerializeField] private Text statsText;
    [SerializeField] private Text priceText;

    private int shownFrame = -1;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Show(string className, string stats, string priceLine)
    {
        shownFrame = Time.frameCount;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (nameText != null) nameText.text = className;
        if (statsText != null) statsText.text = stats;
        if (priceText != null) priceText.text = priceLine;
    }

    private void LateUpdate()
    {
        if (shownFrame != Time.frameCount && panelRoot != null) panelRoot.SetActive(false);
    }
}
