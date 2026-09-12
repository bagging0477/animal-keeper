using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 전체의 밸런스 수치를 한곳에 모아둔 ScriptableObject. 코드를 건드리지 않고
/// Inspector에서 이 에셋(Assets/Settings/GameBalanceConfig.asset)의 값만 바꾸면
/// 몬스터/플레이어/정산/무기/상점 밸런스가 즉시 반영된다.
/// </summary>
[CreateAssetMenu(fileName = "GameBalanceConfig", menuName = "Animal Keeper/Game Balance Config")]
public class GameBalanceConfig : ScriptableObject
{
    [Header("몬스터 - 데미지")]
    [Tooltip("순찰형 몬스터(MonsterAI)가 플레이어를 붙잡았을 때 주는 데미지(%)")]
    public int patrolMonsterDamage = 34;

    [Tooltip("소리반응형 몬스터(SoundReactiveMonsterAI)가 플레이어를 붙잡았을 때 주는 데미지(%)")]
    public int soundReactiveMonsterDamage = 34;

    [Header("몬스터 - 체력")]
    [Tooltip("모든 몬스터(MonsterHealth)의 최대 체력")]
    public int monsterMaxHealth = 3;

    [Header("상태이상 - 포획망 스턴 / 마취총 수면")]
    [Tooltip("포획망에 맞은 몬스터가 스턴 상태가 되는 시간(초)")]
    public float stunDuration = 0.4f;

    [Tooltip("마취총에 맞은 뒤 실제로 잠들기까지 걸리는 시간(초)")]
    public float sleepDelay = 1f;

    [Tooltip("잠든 상태가 유지되는 시간(초) - 몬스터와 동물 모두에게 적용")]
    public float sleepDuration = 5f;

    [Header("플레이어")]
    [Tooltip("플레이어 최대 체력(%)")]
    public int playerMaxHealth = 100;

    [Header("이동 속도")]
    [Tooltip("플레이어 기본(걷기) 이동속도")]
    public float playerMoveSpeed = 3.75f;

    [Tooltip("순찰형 몬스터(MonsterAI)의 순찰 이동속도")]
    public float patrolMonsterPatrolSpeed = 1.6f;

    [Tooltip("소리반응형 몬스터(SoundReactiveMonsterAI)의 배회 이동속도")]
    public float soundReactiveMonsterWanderSpeed = 2.4f;

    [Tooltip("몬스터 추격 속도 = 플레이어 기본 이동속도 × 이 배수. 두 몬스터 타입 공통으로 적용된다.")]
    public float monsterChaseSpeedMultiplier = 1.15f;

    [Tooltip("플레이어 스프린트 이동속도 = 몬스터 추격 속도 × 이 배수")]
    public float playerSprintSpeedMultiplier = 1.25f;

    /// <summary>몬스터 추격 속도. playerMoveSpeed가 바뀌면 자동으로 함께 조정된다.</summary>
    public float MonsterChaseSpeed => playerMoveSpeed * monsterChaseSpeedMultiplier;

    /// <summary>플레이어 스프린트 속도. MonsterChaseSpeed가 바뀌면 자동으로 함께 조정된다.</summary>
    public float PlayerSprintSpeed => MonsterChaseSpeed * playerSprintSpeedMultiplier;

    [Header("스태미나")]
    [Tooltip("최대 스태미나")]
    public float maxStamina = 100f;

    [Tooltip("스프린트 중 초당 소모되는 스태미나")]
    public float sprintStaminaDrainPerSecond = 25f;

    [Tooltip("스프린트를 하지 않을 때 초당 회복되는 스태미나")]
    public float staminaRegenPerSecond = 15f;

    [Tooltip("스태미나가 0이 된 직후 스프린트를 다시 쓸 수 없는 탈진 시간(초)")]
    public float staminaExhaustionCooldown = 1.5f;

    [Header("Day 목표")]
    [Tooltip("보너스 마일리지를 받기 위해 하루 동안 구조해야 하는 목표 마릿수")]
    public int targetRescueCount = 3;

    [Header("정산 (마일리지)")]
    [Tooltip("구조한 동물 무게 1kg당 지급되는 마일리지")]
    public int mileagePerWeight = 10;

    [Tooltip("목표 마릿수를 달성했을 때 추가로 지급되는 보너스 마일리지")]
    public int targetBonusMileage = 50;

    [Header("무기 - 포획망")]
    [Tooltip("포획망 한 번 휘두를 때 몬스터에게 주는 데미지")]
    public int netDamage = 1;

    [Tooltip("포획망이 닿는 거리")]
    public float netRange = 2f;

    [Tooltip("포획망이 닿는 부채꼴 각도(도)")]
    public float netAngle = 90f;

    [Tooltip("포획망을 다시 휘두르기까지 걸리는 쿨다운(초)")]
    public float netCooldown = 0.5f;

    [Header("상점 아이템 가격")]
    [Tooltip("ShelterShop의 각 아이템 이름과 가격. itemName은 ShelterShop 인스펙터의 항목 이름과 정확히 일치해야 한다.")]
    public List<ShopItemPrice> shopItemPrices = new List<ShopItemPrice>
    {
        new ShopItemPrice { itemName = "사료", price = 30 },
        new ShopItemPrice { itemName = "약품", price = 50 },
        new ShopItemPrice { itemName = "장비", price = 80 },
        new ShopItemPrice { itemName = "포획망", price = 60 },
        new ShopItemPrice { itemName = "마취총", price = 120 },
    };

    [Serializable]
    public class ShopItemPrice
    {
        public string itemName;
        public int price;
    }

    public int GetShopItemPrice(string itemName)
    {
        foreach (ShopItemPrice entry in shopItemPrices)
        {
            if (entry.itemName == itemName) return entry.price;
        }

        Debug.LogWarning($"{name}: no shop price configured for '{itemName}'.");
        return 0;
    }
}
