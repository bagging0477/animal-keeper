using UnityEngine;

/// <summary>마을 몬스터 한 마리를 어떤 프리팹으로 만들지 고른다(VillageMapGenerator의 몬스터 스폰이 부른다). AnimalSpawnPool과 같은 방식이다.
/// 지금은 한 줄 배치와, 날짜별 표(GameBalanceConfig.dayMonsterPlans)가 비어 있을 때의 격자 배치만 쓴다 - 표가 있으면 격자 배치는 표의 종류별 수 그대로 놓는다.
/// 새 몬스터 종류(호저·코뿔소·박쥐)는 각자의 확률로 자리를 대신하고, 나머지는 기존 몬스터 풀(VillageMapGenerator.monsterPrefabs - 늑대, 소리반응형)에서 뽑는다.
/// - 총 마리 수는 그대로다. 새 종류가 하나도 허용되지 않으면(새 종류가 없거나 확률이 0이거나, 한 줄 배치에서 꺼져 있으면)
/// 확률을 굴리지 않고 기존 풀에서 균등하게 뽑아서 Random 호출 순서와 결과가 예전과 완전히 같다.
/// - 새 종류가 허용되면 한 번 굴린 값으로 새 종류와 기존 풀을 함께 고른다. 새 종류 확률을 뺀 나머지는 늑대·소리반응형이
/// GameBalanceConfig.patrolMonsterSpawnShare : soundReactiveMonsterSpawnShare 비로 나눈다(지금 35% : 20%, 호저 20%, 코뿔소 10%, 박쥐 15%).
///
/// 새 몬스터를 넣을 때: GameBalanceConfig에 "프리팹, 스폰 확률, 한 줄 배치 허용(기본 꺼짐)" 세 값을 추가하고,
/// 아래 NewTypeCount와 NewType에 한 줄씩 넣는다. 확률은 [0, 첫 종류) → 첫 종류, [첫 종류, 첫 종류 + 둘째) → 둘째 … 순서로 한 번 굴린 값으로 고른다.</summary>
public static class MonsterSpawnPool
{
    // 새 몬스터 종류 수.
    private const int NewTypeCount = 3;

    // i번째 새 몬스터 종류의 (프리팹, 스폰 확률, 한 줄 배치 허용).
    private static (GameObject prefab, float chance, bool inLinearMode) NewType(GameBalanceConfig config, int i)
    {
        switch (i)
        {
            case 0: return (config.porcupinePrefab, config.porcupineSpawnChance, config.porcupineSpawnInLinearMode);
            case 1: return (config.rhinoPrefab, config.rhinoSpawnChance, config.rhinoSpawnInLinearMode);
            case 2: return (config.batPrefab, config.batSpawnChance, config.batSpawnInLinearMode);
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
            // 새 종류에 걸리지 않았으면 같은 값의 남은 구간으로 기존 풀을 고른다(Random을 더 쓰지 않는다).
            float rest = cumulative < 1f ? (roll - cumulative) / (1f - cumulative) : 0f;
            return PickBase(basePool, config, rest);
        }
        return basePool[Random.Range(0, basePool.Length)];
    }

    // 기존 풀에서 t(0~1)로 고른다. 늑대(MonsterAI)·소리반응형(SoundReactiveMonsterAI)은 설정 비율로 나누고,
    // 둘 말고 다른 프리팹이 섞여 있거나 비율이 모두 0이면 균등하게 나눈다.
    private static GameObject PickBase(GameObject[] basePool, GameBalanceConfig config, float t)
    {
        float[] weights = new float[basePool.Length];
        float total = 0f;
        bool known = true;
        for (int i = 0; i < basePool.Length; i++)
        {
            GameObject p = basePool[i];
            if (p != null && p.GetComponent<MonsterAI>() != null) weights[i] = config.patrolMonsterSpawnShare;
            else if (p != null && p.GetComponent<SoundReactiveMonsterAI>() != null) weights[i] = config.soundReactiveMonsterSpawnShare;
            else known = false;
            total += weights[i];
        }
        if (!known || total <= 0f)
            return basePool[Mathf.Min(basePool.Length - 1, (int)(t * basePool.Length))];

        float pick = t * total;
        for (int i = 0; i < basePool.Length; i++)
        {
            pick -= weights[i];
            if (pick < 0f && weights[i] > 0f) return basePool[i];
        }
        for (int i = basePool.Length - 1; i >= 0; i--)
            if (weights[i] > 0f) return basePool[i];
        return basePool[basePool.Length - 1];
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
