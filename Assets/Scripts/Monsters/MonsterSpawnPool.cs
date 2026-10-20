using UnityEngine;

/// <summary>마을 몬스터 한 마리를 어떤 프리팹으로 만들지 고른다(VillageMapGenerator의 몬스터 스폰이 부른다). AnimalSpawnPool과 같은 방식이다.
/// 기존 몬스터 풀(VillageMapGenerator.monsterPrefabs - 늑대, 소리반응형)은 예전처럼 균등하게 뽑고, 새 몬스터 종류는 각자의 확률로 그 자리를 대신한다
/// - 총 마리 수는 그대로다. 새 종류가 하나도 허용되지 않으면(지금처럼 새 종류가 없거나 확률이 0이거나, 한 줄 배치에서 꺼져 있으면)
/// 확률을 굴리지 않아서 Random 호출 순서와 결과가 예전과 완전히 같다.
///
/// 새 몬스터를 넣을 때: GameBalanceConfig에 거북이·거위처럼 "프리팹, 스폰 확률, 한 줄 배치 허용(기본 꺼짐)" 세 값을 추가하고,
/// 아래 NewTypeCount와 NewType에 한 줄씩 넣는다. 확률은 [0, 첫 종류) → 첫 종류, [첫 종류, 첫 종류 + 둘째) → 둘째 … 순서로 한 번 굴린 값으로 고른다.</summary>
public static class MonsterSpawnPool
{
    // 새 몬스터 종류 수. 지금은 없다.
    private const int NewTypeCount = 0;

    // i번째 새 몬스터 종류의 (프리팹, 스폰 확률, 한 줄 배치 허용). 새 종류를 넣을 때 여기에 GameBalanceConfig 값을 연결한다. 예:
    // case 0: return (config.weepingAngelPrefab, config.weepingAngelSpawnChance, config.weepingAngelSpawnInLinearMode);
    private static (GameObject prefab, float chance, bool inLinearMode) NewType(GameBalanceConfig config, int i)
    {
        switch (i)
        {
            default: return (null, 0f, false);
        }
    }

    public static GameObject Pick(GameObject[] basePool, GameBalanceConfig config, bool gridLayout)
    {
        if (config != null && AnyNewTypeAllowed(config, gridLayout))
        {
            float roll = Random.value;
            float cumulative = 0f;
            for (int i = 0; i < NewTypeCount; i++)
            {
                var (prefab, chance, inLinearMode) = NewType(config, i);
                if (!Allowed(prefab, chance, inLinearMode, gridLayout)) continue;
                cumulative += chance;
                if (roll < cumulative) return prefab;
            }
        }
        return basePool[Random.Range(0, basePool.Length)];
    }

    private static bool AnyNewTypeAllowed(GameBalanceConfig config, bool gridLayout)
    {
        for (int i = 0; i < NewTypeCount; i++)
        {
            var (prefab, chance, inLinearMode) = NewType(config, i);
            if (Allowed(prefab, chance, inLinearMode, gridLayout)) return true;
        }
        return false;
    }

    private static bool Allowed(GameObject prefab, float chance, bool inLinearMode, bool gridLayout) =>
        prefab != null && chance > 0f && (gridLayout || inLinearMode);
}
