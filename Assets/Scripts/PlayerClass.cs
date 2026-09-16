/// <summary>플레이어가 선택할 수 있는 클래스. 클래스마다 이동속도/체력 배율과 사용 가능한
/// 무기가 고정되어 있다 (배율/해금 가격은 GameBalanceConfig, 무기 매핑은 GameManager 참고).</summary>
public enum PlayerClass
{
    /// <summary>기본 제공 클래스. 무기 없음, 이동속도 빠름, 체력 낮음.</summary>
    Scout,

    /// <summary>마일리지로 해금. 근접(포획망) 전용, 이동속도 평균, 체력 높음.</summary>
    Trapper,

    /// <summary>마일리지로 해금(트래퍼보다 비쌈). 원거리(마취화살) 전용, 이동속도 평균, 체력 평균.</summary>
    Archer
}
