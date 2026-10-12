using System.Collections.Generic;
using UnityEngine;

/// <summary>이번 판에 산 패시브와 구매 횟수. GameManager(DontDestroyOnLoad)가 하나 들고 있어서 사이클("다음 날로")과
/// 씬 전환에서는 그대로 남고, 새 게임을 시작할 때(GameManager.ResetGame) 비워진다.
/// 효과 종류별로 값을 합치는 규칙은 이 클래스에만 있다 - 다른 스크립트는 PassiveEffects로 결과만 읽는다.</summary>
public class RunPassives
{
    private readonly Dictionary<PassiveDefinition, int> purchaseCounts = new Dictionary<PassiveDefinition, int>();

    public int GetPurchaseCount(PassiveDefinition passive) =>
        passive != null && purchaseCounts.TryGetValue(passive, out int count) ? count : 0;

    public void Add(PassiveDefinition passive)
    {
        if (passive == null) return;
        purchaseCounts[passive] = GetPurchaseCount(passive) + 1;
    }

    public void Clear() => purchaseCounts.Clear();

    public int OwnedKindCount => purchaseCounts.Count;

    public float MoveSpeedMultiplier
    {
        get
        {
            float multiplier = 1f;
            foreach (KeyValuePair<PassiveDefinition, int> pair in purchaseCounts)
            {
                if (pair.Key.effectKind == PassiveEffectKind.MoveSpeedMultiplier)
                    multiplier *= Mathf.Pow(pair.Key.effectValue, pair.Value);
            }
            return multiplier;
        }
    }

    public int ExtraInventorySlots
    {
        get
        {
            float slots = 0f;
            foreach (KeyValuePair<PassiveDefinition, int> pair in purchaseCounts)
            {
                if (pair.Key.effectKind == PassiveEffectKind.ExtraInventorySlots) slots += pair.Key.effectValue * pair.Value;
            }
            return Mathf.Max(0, Mathf.RoundToInt(slots));
        }
    }

    public bool HasFlashlight
    {
        get
        {
            foreach (PassiveDefinition passive in purchaseCounts.Keys)
            {
                if (passive.effectKind == PassiveEffectKind.Flashlight) return true;
            }
            return false;
        }
    }
}

/// <summary>패시브 효과 조회 창구. 패시브를 반영해야 하는 스크립트는 패시브 종류를 직접 보지 말고 여기 값만 쓴다.
/// GameManager가 없으면(씬을 단독으로 Play 등) 패시브가 없는 기본값을 돌려준다.</summary>
public static class PassiveEffects
{
    private static RunPassives Current => GameManager.Instance != null ? GameManager.Instance.Passives : null;

    /// <summary>플레이어 기본 이동속도에 곱하는 배율 (패시브 없으면 1).</summary>
    public static float MoveSpeedMultiplier => Current != null ? Current.MoveSpeedMultiplier : 1f;

    /// <summary>인벤토리 추가 칸 수 (패시브 없으면 0). 아직 인벤토리 칸 수에는 반영하지 않는다.</summary>
    public static int ExtraInventorySlots => Current != null ? Current.ExtraInventorySlots : 0;

    /// <summary>플래시라이트 보유 여부. 아직 시야에는 반영하지 않는다.</summary>
    public static bool HasFlashlight => Current != null && Current.HasFlashlight;
}
