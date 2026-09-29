using UnityEngine;

/// <summary>상호작용 범위가 서로 겹치는 자리에서 E 한 번에 여러 지점이 동시에 반응하지 않도록(예: 캠핑카 문의
/// "구조 시작"과 동물 칸의 "납품"이 같이 실행되는 사고), 범위 안에 든 지점 중 플레이어에게 가장 가까운 하나만
/// "초점"으로 고른다. 각 지점은 범위 안에 있을 때 TryFocus로 자기 거리를 보고하고, 반환값이 true일 때만
/// 안내 문구를 띄우고 입력을 처리한다. 스크립트끼리 Update 실행 순서가 정해져 있지 않아 같은 프레임 안에서는
/// 누가 가장 가까운지 확정할 수 없으므로 바로 직전 프레임의 보고 결과로 판정한다 - 범위에 막 들어온 첫
/// 프레임만 반응이 한 프레임 늦다.</summary>
public static class InteractionFocus
{
    private static int currentFrame = -1;
    private static Object currentBest;
    private static float currentBestDistance;
    private static Object previousBest;

    public static bool TryFocus(Object owner, float distance)
    {
        int frame = Time.frameCount;
        if (frame != currentFrame)
        {
            // 직전 프레임에 아무도 보고하지 않았다면(모두 범위 밖이었다면) 이전 승자는 무효다.
            previousBest = currentFrame == frame - 1 ? currentBest : null;
            currentFrame = frame;
            currentBest = null;
            currentBestDistance = float.MaxValue;
        }

        if (distance < currentBestDistance)
        {
            currentBest = owner;
            currentBestDistance = distance;
        }

        return previousBest == owner;
    }
}
