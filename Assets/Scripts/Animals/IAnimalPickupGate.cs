/// <summary>동물 프리팹에 붙은 행동 스크립트가 AnimalRescue의 줍기를 잠시 막고 싶을 때 구현한다(예: 등껍질 속에 숨은 거북이).
/// 막혀 있는 동안에는 줍기 안내가 뜨지 않고, E를 누르면 줍는 대신 OnPickupBlocked가 불린다.</summary>
public interface IAnimalPickupGate
{
    bool CanBePickedUp { get; }
    void OnPickupBlocked();
}

/// <summary>프리팹에 적힌 minWeight/maxWeight 대신 다른 곳(GameBalanceConfig 등)의 무게 범위를 쓰고 싶은 동물이 구현한다.
/// AnimalRescue.Awake가 무게를 굴릴 때 한 번 묻는다.</summary>
public interface IAnimalWeightRange
{
    bool TryGetWeightRange(out int min, out int max);
}
