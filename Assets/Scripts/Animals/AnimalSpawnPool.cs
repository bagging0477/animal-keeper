using UnityEngine;

/// <summary>마을 동물 한 마리를 어떤 프리팹으로 만들지 고른다(VillageMapGenerator의 동물 스폰이 부른다).
/// 기존 동물 풀(VillageMapGenerator.animalPrefabs)은 예전처럼 균등하게 뽑고, 거북이(GameBalanceConfig.turtlePrefab)는
/// turtleSpawnChance, 거위(goosePrefab)는 gooseSpawnChance 확률로 그 자리를 대신한다 - 총 마리 수는 그대로다.
/// 한 번 굴린 값으로 [0, 거북이) → 거북이, [거북이, 거북이 + 거위) → 거위를 골라서 Random 호출 수가 예전과 같다.
/// 한 줄 배치는 turtleSpawnInLinearMode/gooseSpawnInLinearMode를 켜지 않으면 확률을 아예 굴리지 않아서, Random 호출 순서와 결과가 예전과 같다.</summary>
public static class AnimalSpawnPool
{
    public static GameObject Pick(GameObject[] basePool, GameBalanceConfig config, bool gridLayout)
    {
        GameObject turtle = config != null ? config.turtlePrefab : null;
        GameObject goose = config != null ? config.goosePrefab : null;
        bool turtleAllowed = turtle != null && config.turtleSpawnChance > 0f && (gridLayout || config.turtleSpawnInLinearMode);
        bool gooseAllowed = goose != null && config.gooseSpawnChance > 0f && (gridLayout || config.gooseSpawnInLinearMode);
        if (turtleAllowed || gooseAllowed)
        {
            float roll = Random.value;
            float turtleChance = turtleAllowed ? config.turtleSpawnChance : 0f;
            if (roll < turtleChance) return turtle;
            if (gooseAllowed && roll < turtleChance + config.gooseSpawnChance) return goose;
        }
        return basePool[Random.Range(0, basePool.Length)];
    }
}
