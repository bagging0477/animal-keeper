/// <summary>게임플레이 입력(E 상호작용, 공격, 슬롯 선택, 아이템 사용/버리기 등)을 지금 받아도 되는지 한 곳에서
/// 판단한다. ESC 일시정지 중이거나 상점 메뉴가 열려 있으면(닫힌 바로 그 프레임 포함 - 닫는 E가 다른 지점의
/// 상호작용으로 이어지지 않게) 막는다.</summary>
public static class GameplayInput
{
    public static bool IsBlocked => PauseMenu.IsPaused || ShopMenuUI.IsOpen || ShopMenuUI.ClosedThisFrame;
}
