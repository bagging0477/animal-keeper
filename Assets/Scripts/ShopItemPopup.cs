using UnityEngine;
using UnityEngine.UI;

/// <summary>ShelterScene의 상점 아이템 오브젝트(ShopItemInteractPoint)들이 공유하는 정보 팝업.
/// ClassSelectScene의 ClassInfoPopup과 동일한 패턴 - 매 프레임 아무도 Show를 호출하지 않으면
/// LateUpdate에서 자동으로 숨겨진다.</summary>
public class ShopItemPopup : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Text nameText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Text priceText;

    private int shownFrame = -1;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Show(string itemName, string description, string priceLine)
    {
        shownFrame = Time.frameCount;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (nameText != null) nameText.text = itemName;
        if (descriptionText != null) descriptionText.text = description;
        if (priceText != null) priceText.text = priceLine;
    }

    private void LateUpdate()
    {
        if (shownFrame != Time.frameCount && panelRoot != null) panelRoot.SetActive(false);
    }
}
