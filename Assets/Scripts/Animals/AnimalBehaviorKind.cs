/// <summary>동물이 원래 어떤 행동 프리팹(AnimalWander/AnimalFlee/AnimalSoundFlee)이었는지 기록한다.
/// 인벤토리에 담기는 순간 원본 게임오브젝트는 사라지므로, G로 다시 꺼낼 때 같은 종류로
/// 되살리려면 이 값을 슬롯 데이터에 함께 들고 있어야 한다 - 이게 없으면 어떤 동물이든
/// 항상 가장 얌전한(Wander) 프리팹으로만 되살아난다. MonsterCorpse는 동물이 아니라
/// MonsterCorpsePickup으로 인벤토리에 담긴 몬스터 시체용 - 되살릴 배회/도주 프리팹이
/// 없으므로 PlayerItemDropper는 이 종류를 G로 내려놓아도 아무것도 다시 스폰하지 않는다.</summary>
public enum AnimalBehaviorKind
{
    Wander,
    Flee,
    SoundFlee,
    MonsterCorpse
}
