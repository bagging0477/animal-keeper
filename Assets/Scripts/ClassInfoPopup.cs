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

    [Header("월드 좌표 추적 (스탠드 바로 위에 띄우기, 좌우로도 따라 움직임)")]
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

    public void Show(string className, string stats, string priceLine, Transform anchor)
    {
        shownFrame = Time.frameCount;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (nameText != null) nameText.text = className;
        if (statsText != null) statsText.text = stats;
        if (priceText != null) priceText.text = priceLine;

        PositionAbove(anchor);
    }

    // 화면 고정 위치 대신 실제로 서 있는 스탠드(anchor) 머리 위로 패널을 옮긴다 - 좌우 위치도 스탠드의
    // 화면상 x좌표를 그대로 따라간다. Screen Space - Overlay 캔버스라 카메라를 null로 넘겨야
    // ScreenPointToLocalPointInRectangle이 올바르게 계산된다 - panelRectTransform은 canvasRect의
    // 직속 자식이고 앵커가 (0.5, 0.5)라서, 여기서 구한 로컬 좌표를 anchoredPosition에 그대로 대입할 수 있다.
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
