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

    [Header("월드 좌표 추적 (아이템 바로 위에 띄우기, 좌우로도 따라 움직임)")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private float worldOffsetY = 1.6f;

    private RectTransform panelRectTransform;
    private Camera mainCamera;
    private int shownFrame = -1;

    private void Awake()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
            panelRectTransform = panelRoot.GetComponent<RectTransform>();
        }
    }

    public void Show(string itemName, string description, string priceLine, Transform anchor)
    {
        shownFrame = Time.frameCount;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (nameText != null) nameText.text = itemName;
        if (descriptionText != null) descriptionText.text = description;
        if (priceText != null) priceText.text = priceLine;

        PositionAbove(anchor);
    }

    // ClassInfoPopup.PositionAbove와 동일한 이유/방식 - 화면 고정 위치 대신 실제 아이템(anchor) 머리
    // 위로, 좌우 위치까지 따라가도록 패널을 옮긴다.
    private void PositionAbove(Transform anchor)
    {
        if (panelRectTransform == null || canvasRect == null || anchor == null) return;

        if (mainCamera == null) mainCamera = Camera.main;

        Vector3 worldPoint = anchor.position + Vector3.up * worldOffsetY;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(mainCamera, worldPoint);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint))
        {
            panelRectTransform.anchoredPosition = localPoint;
        }
    }

    private void LateUpdate()
    {
        if (shownFrame != Time.frameCount && panelRoot != null) panelRoot.SetActive(false);
    }
}
