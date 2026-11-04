using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 그림 판(AnimalKeeperNight가 Play 때 만드는 "Title Surface")을 화면에 꽉 채운다 - 원본 비율(480x300, 16:10)은 지키고
/// 화면 밖으로 넘치는 부분은 잘린다(AspectRatioFitter Envelope). 그래서 16:9 화면 좌우의 여백이 없어진다.
/// 꽉 채우면 반드시 보여야 할 영역(mustShowArea - 로고·트럭과 헤드라이트·길)이 잘리는 화면 비율(21:9 등)에서는 꽉 채우지 않고
/// 예전처럼 원본 비율 그대로 화면 안에 맞춘다(FitInParent, 좌우 여백).
/// AnimalKeeperNight 스크립트는 바꾸지 않는다 - 같은 오브젝트(AnimalKeeperNightRoot)에 붙여 그 스크립트가 만든 판의 맞춤 방식만 바꾼다.
/// 그림 배치는 AnimalKeeperNight가 판 크기(surface.rect.width / 480)로 매 프레임 다시 하므로 따로 손대지 않는다.
/// </summary>
public class TitleArtFill : MonoBehaviour
{
    [Tooltip("원본 그림 크기(칸). AnimalKeeperNight의 레이아웃 크기와 같다.")]
    [SerializeField] private Vector2 artSize = new Vector2(480f, 300f);

    [Tooltip("꽉 채워도 화면 안에 다 보여야 하는 영역(원본 그림 칸, 왼쪽 위가 0). " +
        "x 72~403: 트럭이 출발하는 자리(72)부터 따라잡힌 뒤 헤드라이트 끝(약 403)까지. " +
        "y 37~264: 로고 그림 윗줄(35 + 2)부터 길 아래 가장자리 선(264)까지 - 트럭(192~241, 흔들림 포함)과 헤드라이트(204~239)가 그 안에 있다.")]
    [SerializeField] private Rect mustShowArea = Rect.MinMaxRect(72f, 37f, 403f, 264f);

    private AspectRatioFitter fitter;
    private RectTransform parentRect;

    /// <summary>지금 꽉 채우고 있는지(false면 원본 비율로 화면 안에 맞춤).</summary>
    public bool IsFilling { get; private set; }

    private void LateUpdate() => Apply();

    /// <summary>판의 맞춤 방식을 지금 화면(부모 Canvas) 비율에 맞게 정한다. 판이 아직 없으면 아무것도 하지 않는다.</summary>
    public void Apply()
    {
        if (fitter == null)
        {
            fitter = GetComponentInChildren<AspectRatioFitter>(true);
            if (fitter == null) return;
            parentRect = fitter.transform.parent as RectTransform;
        }
        if (parentRect == null) return;
        Rect area = parentRect.rect;
        if (area.width <= 0f || area.height <= 0f) return;

        IsFilling = FillKeepsAreaVisible(area.width / area.height, artSize, mustShowArea);
        AspectRatioFitter.AspectMode mode = IsFilling ? AspectRatioFitter.AspectMode.EnvelopeParent : AspectRatioFitter.AspectMode.FitInParent;
        if (fitter.aspectMode != mode) fitter.aspectMode = mode;
    }

    /// <summary>화면 비율 screenAspect에서 꽉 채웠을 때(가운데 기준으로 넘치는 부분을 자름) mustShow가 모두 화면 안에 남는지.</summary>
    public static bool FillKeepsAreaVisible(float screenAspect, Vector2 art, Rect mustShow)
    {
        float artAspect = art.x / art.y;
        if (screenAspect >= artAspect)
        {
            // 화면이 더 넓다: 가로를 맞추고 위아래가 잘린다.
            float visibleHeight = art.x / screenAspect;
            float crop = (art.y - visibleHeight) * 0.5f;
            return mustShow.yMin >= crop && mustShow.yMax <= art.y - crop;
        }
        else
        {
            // 화면이 더 좁다: 세로를 맞추고 좌우가 잘린다.
            float visibleWidth = art.y * screenAspect;
            float crop = (art.x - visibleWidth) * 0.5f;
            return mustShow.xMin >= crop && mustShow.xMax <= art.x - crop;
        }
    }
}
