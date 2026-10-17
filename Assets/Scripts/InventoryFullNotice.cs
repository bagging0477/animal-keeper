/// <summary>인벤토리가 가득 찬 채로 들 수 있는 물건(동물, 바닥의 지뢰/폭탄, 늑대 시체)의 줍기 범위에 들어가면 자막으로 알린다.
/// 매 프레임 띄우지 않고 범위에 들어간 순간 한 번만 띄우며, 범위를 벗어났다 다시 들어오면 다시 띄운다.
/// "범위 안"은 각 물건의 줍기 안내와 같은 판정(거리 + InteractionFocus)이고, 들 수 없는 상태(숨은 거북이 등)는 부르는 쪽이 false로 넘긴다.</summary>
public static class InventoryFullNotice
{
    public const string Message = "인벤토리가 가득 차 획득할 수 없습니다";

    // 자막을 띄워 두는 시간 - 숨은 거북이 안내(AnimalTurtle.OnPickupBlocked)와 같다.
    private const float Duration = 2f;

    /// <summary>물건마다 매 프레임 부른다. shown은 그 물건이 들고 있는 "이번에 범위에 들어와 이미 띄웠는지" 값이다.</summary>
    public static void Update(bool inRangeAndPickable, ref bool shown)
    {
        if (!inRangeAndPickable)
        {
            shown = false;
            return;
        }
        if (shown || GameManager.Instance == null || GameManager.Instance.HasInventorySpace) return;

        shown = true;
        SubtitleManager.Show(Message, Duration);
    }
}
