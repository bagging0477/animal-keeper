using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 화면의 트럭(UI Image)을 두 프레임(truck-01/02)으로 번갈아 그려 바퀴가 굴러가게 하고, 배경의 도로를 따라
/// 왼쪽 화면 밖에서 오른쪽 화면 밖까지 계속 지나가게 한다. 에셋 안내(사용방법.txt)의 "8 Samples로 번갈아 재생,
/// 위치 키프레임으로 이동"을 Animator 없이 스크립트로 구현한 것이다(이 프로젝트는 스프라이트 애니메이션에
/// AnimatorController 대신 스크립트를 쓴다 - SimpleFrameAnimator 참고).
/// 트럭의 RectTransform은 배경 이미지 기준 비율 앵커(anchorMin/Max)로 놓여 있어, 가로 앵커만 옮겨 이동시킨다.
/// </summary>
[RequireComponent(typeof(Image))]
public class TitleTruckAnimator : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [Tooltip("초당 프레임 수. 에셋 안내의 8 Samples.")]
    [SerializeField] private float frameRate = 8f;

    [Tooltip("끄면 원래 배치 위치(배경 기준 x=102px)에 서서 바퀴만 굴러간다.")]
    [SerializeField] private bool driveAcross = true;
    [Tooltip("화면 왼쪽 밖에서 오른쪽 밖까지 지나가는 데 걸리는 시간(초).")]
    [SerializeField] private float crossDuration = 12f;

    private Image image;
    private RectTransform rectTransform;
    private float width;
    private float timer;

    private void Awake()
    {
        image = GetComponent<Image>();
        rectTransform = (RectTransform)transform;
        width = rectTransform.anchorMax.x - rectTransform.anchorMin.x;
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;

        if (frames != null && frames.Length > 0 && frameRate > 0f)
        {
            image.sprite = frames[(int)(timer * frameRate) % frames.Length];
        }

        if (driveAcross && crossDuration > 0f)
        {
            // 트럭 왼쪽 끝이 -width(완전히 왼쪽 밖) → 1(완전히 오른쪽 밖)까지 이동하고 다시 처음부터 반복한다.
            float t = (timer / crossDuration) % 1f;
            float minX = Mathf.Lerp(-width, 1f, t);
            rectTransform.anchorMin = new Vector2(minX, rectTransform.anchorMin.y);
            rectTransform.anchorMax = new Vector2(minX + width, rectTransform.anchorMax.y);
        }
    }
}
