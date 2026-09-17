/// <summary>동물이 원래 어떤 행동 프리팹(AnimalWander/AnimalFlee/AnimalSoundFlee)이었는지 기록한다.
/// 인벤토리에 담기는 순간 원본 게임오브젝트는 사라지므로, G로 다시 꺼낼 때 같은 종류로
/// 되살리려면 이 값을 슬롯 데이터에 함께 들고 있어야 한다 - 이게 없으면 어떤 동물이든
/// 항상 가장 얌전한(Wander) 프리팹으로만 되살아난다.</summary>
public enum AnimalBehaviorKind
{
    Wander,
    Flee,
    SoundFlee
}
