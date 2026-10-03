using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이 오브젝트(RectTransform)의 네 모서리에 모서리 장식 이미지(ALL_326/327/381/382)를 배치해 테두리처럼 감싼다.
/// 장식은 박스 모서리에 딱 붙지 않고 margin만큼 바깥으로 떨어져, 네 모서리 모두 같은 간격으로 놓인다.
/// 값은 이 오브젝트의 로컬 단위라서 오브젝트를 스케일로 키우면 장식도 같이 커진다.
/// </summary>
[ExecuteAlways]
public class CornerBrackets : MonoBehaviour
{
    [SerializeField] private Image topLeft;
    [SerializeField] private Image topRight;
    [SerializeField] private Image bottomLeft;
    [SerializeField] private Image bottomRight;

    [Tooltip("박스 모서리에서 장식까지 바깥쪽으로 띄우는 간격 (로컬 단위). 네 모서리 공통.")]
    [SerializeField] private float margin = 3f;
    [Tooltip("모서리 장식 하나의 크기 (로컬 단위). 원본이 10x11 픽셀이라 같은 비율로 맞추면 찌그러지지 않는다.")]
    [SerializeField] private Vector2 cornerSize = new Vector2(10f, 11f);

    private void OnEnable() => ApplyLayout();

#if UNITY_EDITOR
    // Inspector에서 값을 바꾸면 바로 반영한다. OnValidate 안에서 RectTransform을 직접 바꾸면 Unity가 경고를 내므로
    // 한 프레임 미뤄서 적용한다.
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) ApplyLayout();
        };
    }
#endif

    private void ApplyLayout()
    {
        Place(topLeft, new Vector2(0f, 1f), new Vector2(-margin, margin));
        Place(topRight, new Vector2(1f, 1f), new Vector2(margin, margin));
        Place(bottomLeft, new Vector2(0f, 0f), new Vector2(-margin, -margin));
        Place(bottomRight, new Vector2(1f, 0f), new Vector2(margin, -margin));
    }

    // 장식의 바깥 모서리(피벗)를 박스 모서리에서 margin만큼 바깥에 두면, 장식의 두 팔이 박스 두 변 바깥을 따라 안쪽으로 뻗는다.
    private void Place(Image image, Vector2 corner, Vector2 offset)
    {
        if (image == null) return;
        RectTransform rt = image.rectTransform;
        rt.anchorMin = corner;
        rt.anchorMax = corner;
        rt.pivot = corner;
        rt.anchoredPosition = offset;
        rt.sizeDelta = cornerSize;
    }
}
