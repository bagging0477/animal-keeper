using UnityEngine;

/// <summary>스폰 자리를 고를 때 원(NavMeshAgent 반경 등)보다 큰 몸 상자로 겹침을 검사해야 하는 프리팹이 구현한다(VillageMapGenerator 격자 스폰).
/// 값은 프리팹 에셋의 직렬화 값만으로 계산해야 한다(Instantiate 전에 읽는다).</summary>
public interface ISpawnFootprint
{
    /// <summary>스폰 위치(발밑 피벗, 2D 게임 좌표)에서 몸 상자 가운데까지의 거리.</summary>
    Vector2 SpawnFootprintOffset { get; }

    /// <summary>몸 상자의 반 크기(칸). 어느 쪽을 보고 생기든 덮도록 잡는다.</summary>
    Vector2 SpawnFootprintHalfExtents { get; }
}
