/// <summary>몬스터마다 시체 무게를 따로 정할 때 그 몬스터의 두뇌(같은 오브젝트의 컴포넌트)가 구현한다.
/// MonsterCorpsePickup은 이게 있으면 이 값을, 없으면 늑대 시체 무게(GameBalanceConfig.wolfCorpseWeight)를 쓴다 -
/// 늑대·박쥐는 구현하지 않으므로 예전과 같다. 호저: GameBalanceConfig.porcupineCorpseWeight.</summary>
public interface ICorpseWeight
{
    int CorpseWeight { get; }
}
