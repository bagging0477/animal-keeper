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
    public int patrolMonsterDamage = 47;

    [Tooltip("소리반응형 몬스터(SoundReactiveMonsterAI)가 플레이어를 붙잡았을 때 주는 데미지(%)")]
    public int soundReactiveMonsterDamage = 47;

    [Header("몬스터 - 체력")]
    [Tooltip("모든 몬스터(MonsterHealth)의 최대 체력. 정수가 아니어도 되며 실제 체력은 반올림해서 적용된다.")]
    public float monsterMaxHealth = 4.5f;

    [Header("몬스터 - 감지")]
    [Tooltip("순찰형 몬스터가 플레이어를 발견하는 거리")]
    public float patrolMonsterDetectRange = 4.05f;

    [Tooltip("순찰형 몬스터가 추격을 포기하기 시작하는(수색 상태로 전환되는) 거리")]
    public float patrolMonsterLoseRange = 10f;

    [Tooltip("소리반응형 몬스터가 플레이어를 발견하는 거리")]
    public float soundReactiveMonsterDetectRange = 2.03f;

    [Tooltip("소리반응형 몬스터가 추격을 포기하기 시작하는(수색 상태로 전환되는) 거리")]
    public float soundReactiveMonsterLoseRange = 9f;

    [Tooltip("소리반응형 몬스터가 동물 소리를 듣고 반응하는 거리")]
    public float soundReactiveMonsterHearRange = 10.4f;

    [Tooltip("몬스터가 시야에서 플레이어를 놓친 뒤, 완전히 포기하고 순찰/배회로 돌아가기 전까지 마지막으로 본 위치 근처에서 수색하는 시간(초)")]
    public float monsterSearchDuration = 3f;

    [Header("몬스터 - 포획 판정")]
    [Tooltip("순찰형 몬스터가 이 거리 안으로 들어오면 플레이어를 붙잡은 것으로 판정한다")]
    public float patrolMonsterCatchRange = 0.7f;

    [Tooltip("소리반응형 몬스터가 이 거리 안으로 들어오면 플레이어를 붙잡은 것으로 판정한다")]
    public float soundReactiveMonsterCatchRange = 0.7f;

    [Tooltip("몬스터가 목적지(순찰 웨이포인트/배회 지점 등)에 도착했다고 판정하는 거리 - 두 몬스터 타입이 공통으로 쓴다")]
    public float monsterWaypointStopDistance = 0.2f;

    [Header("몬스터 - 추격 포기 / 반응성")]
    [Tooltip("Chase 중 플레이어와의 거리가 위의 LoseRange를 넘거나(벽/장애물에 가려 Linecast가 막혀도 같은 취급) " +
        "완전히 시야를 놓친 상태가 이 시간(초) 이상 '연속으로' 유지되어야 추격을 포기하고 Search로 전환한다. " +
        "한순간 스치듯 놓친 것만으로는 포기하지 않아서, 코너 뒤로 잠깐 숨었다가 다시 보이면 계속 쫓아온다.")]
    public float chaseGiveUpSightLostDuration = 2f;

    [Tooltip("Chase 중 몬스터가 플레이어의 최신 위치로 목적지(SetDestination)를 다시 잡는 주기(초). " +
        "0보다 크게 두면 플레이어가 급하게 코너를 꺾었을 때 몬스터가 살짝 늦게 반응하는 느낌을 준다 - 매 프레임 완벽하게 추적하지 않는다.")]
    public float chaseDirectionUpdateInterval = 0.25f;

    [Header("몬스터 - 스폰 (VillageScene 절차적 생성 전용)")]
    [Tooltip("몬스터가 스폰될 때 플레이어 스폰 위치로부터 최소 이 거리 이상 떨어진 곳에만 생성되도록 시도한다. " +
        "스폰 시점에만 적용되며, 스폰 이후 순찰/배회로 이 범위 안에 들어오는 것은 막지 않는다.")]
    public float monsterMinSpawnDistanceFromPlayer = 6f;

    [Header("상태이상 - 포획망 스턴 / 마취화살 수면")]
    [Tooltip("포획망에 맞은 몬스터가 스턴 상태가 되는 시간(초)")]
    public float stunDuration = 0.4f;

    [Tooltip("마취화살에 맞은 뒤 실제로 잠들기까지 걸리는 시간(초)")]
    public float sleepDelay = 1f;

    [Tooltip("잠든 상태가 유지되는 시간(초) - 몬스터와 동물 모두에게 적용. 마취화살은 아처만 쏠 수 있어 사실상 아처 전용 수치다.")]
    public float sleepDuration = 4.2f;

    [Header("플레이어")]
    [Tooltip("플레이어 최대 체력(%)")]
    public int playerMaxHealth = 100;

    [Header("플레이어 시야 (VillageScene 전용)")]
    [Tooltip("항상 켜져 있는 기본 원형 시야(Ambient Vision)의 반경")]
    public float ambientVisionRadius = 3f;

    [Tooltip("체크하면 기본 원형 시야(Ambient Vision) 범위 안은 벽/장애물 그림자를 무시하고 항상 전부 보인다. " +
        "바로 옆에 있는데 벽 때문에 시야가 뚝 끊겨 보이는 이질감을 없애기 위한 옵션 - 부채꼴 시야(Focused Vision)는 " +
        "이 옵션과 무관하게 항상 벽에 가려진다(스텔스의 핵심이라 그대로 둔다).")]
    public bool ambientVisionIgnoresShadows = true;

    [Tooltip("바라보는 방향으로 밝혀지는 부채꼴 시야(Focused Vision)의 반경")]
    public float focusedVisionRadius = 13f;

    [Tooltip("부채꼴 시야(Focused Vision)의 각도(도)")]
    public float focusedVisionAngle = 80f;

    [Tooltip("부채꼴 시야가 벽/장애물에 가려질 때 그림자 경계를 얼마나 부드럽게 처리할지 (0 = 칼같이 딱 끊김, 1 = 최대한 부드럽게). " +
        "너무 딱딱하게 끊기면 이질감이 들어서 기본값을 약간 부드럽게 뒀다.")]
    [Range(0f, 1f)]
    public float focusedVisionShadowSoftness = 0.5f;

    [Header("클래스 - 시야 범위 배율 (기준치 대비, %) - 100이면 기준치(위 ambientVisionRadius 등) 그대로. " +
        "원형 시야(Ambient)와 부채꼴 시야(Focused)를 따로 조절할 수 있다.")]
    [Tooltip("스카우트 기본 원형 시야(Ambient Vision) 반경 배율.")]
    public int scoutAmbientVisionRangePercent = 100;

    [Tooltip("트래퍼 기본 원형 시야(Ambient Vision) 반경 배율.")]
    public int trapperAmbientVisionRangePercent = 100;

    [Tooltip("아처 기본 원형 시야(Ambient Vision) 반경 배율 - 부채꼴 시야보다도 더 넓게 잡아서, " +
        "바로 근처에서도 다른 클래스보다 확실히 더 잘 보이게 한다.")]
    public int archerAmbientVisionRangePercent = 150;

    [Tooltip("스카우트 부채꼴 시야(Focused Vision)의 반경/각도 배율.")]
    public int scoutFocusedVisionRangePercent = 100;

    [Tooltip("트래퍼 부채꼴 시야(Focused Vision)의 반경/각도 배율.")]
    public int trapperFocusedVisionRangePercent = 100;

    [Tooltip("아처 부채꼴 시야(Focused Vision)의 반경/각도 배율 - " +
        "체력이 낮은 대신 더 멀리, 더 넓게 보고 안전한 거리에서 대응할 수 있는 컨셉.")]
    public int archerFocusedVisionRangePercent = 125;

    [Header("클래스 - 이동속도 / 체력")]
    [Tooltip("스카우트 기본(걷기) 이동속도 배율 - 몬스터 추격 속도(monsterChaseSpeed, 고정값) 대비. " +
        "1.05~1.1 정도로 잡아서 '몬스터보다 여전히 빠르지만 차이는 크지 않은' 상태를 만든다 - 스태미나 없이 그냥 도망만 쳐도 " +
        "서서히 거리가 벌어지긴 하지만, 추격 포기 거리(patrolMonsterLoseRange 등)까지 벌어지려면 시간이 걸린다. " +
        "트래퍼/아처는 배율 1(기본 이동속도 그대로)을 쓴다.")]
    [Range(1f, 1.3f)]
    public float scoutMoveSpeedMultiplier = 1.08f;

    [Tooltip("스카우트 최대 체력 비율 (playerMaxHealth 대비, %) - 기준치. 이동속도가 빠른 대신 시야는 다른 클래스와 동일한 컨셉.")]
    public int scoutMaxHealthPercent = 100;

    [Tooltip("트래퍼 최대 체력 비율 (playerMaxHealth 대비, %). 근접전을 감당할 수 있는 확실한 맷집.")]
    public int trapperMaxHealthPercent = 130;

    [Tooltip("아처 최대 체력 비율 (playerMaxHealth 대비, %). 체력이 가장 낮은 대신 넓은 시야로 안전한 거리를 두고 싸우는 컨셉.")]
    public int archerMaxHealthPercent = 50;

    [Tooltip("스카우트의 스프린트 스태미나 소모 배율(다른 클래스 대비). 체력이 낮은데 지구력까지 약해서, " +
        "스프린트를 오래 쓰면 더 빨리 지치고 장기적으로 위험해지는 컨셉. 트래퍼/아처는 배율 1(기본)을 그대로 쓴다.")]
    public float scoutStaminaDrainMultiplier = 1.2f;

    [Tooltip("스카우트의 스프린트 추가 가속 배율 (스카우트 자신의 기본 이동속도 대비). 평소에 이미 몬스터보다 살짝 빠른 " +
        "대신, 위기 상황에서 폭발적으로 더 빨라지지는 못하는 컨셉이라 트래퍼/아처보다 낮게 잡는다.")]
    public float scoutSprintSpeedMultiplier = 1.1f;

    [Tooltip("트래퍼/아처의 스프린트 추가 가속 배율 (각자의 기본 이동속도 대비). 평소엔 스카우트보다 느리지만, " +
        "위기 상황에서는 폭발적으로 가속해 벗어날 수 있다.")]
    public float trapperArcherSprintSpeedMultiplier = 1.3f;

    [Header("클래스 해금 가격 (마일리지) - 게임 시작 시 무료로 선택한 클래스는 이미 해금된 상태라 여기 가격과 " +
        "무관하게 장착만 하면 되고, 나머지 두 클래스만 여기 가격으로 구매해야 한다.")]
    [Tooltip("스카우트 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int scoutUnlockPrice = 200;

    [Tooltip("트래퍼 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int trapperUnlockPrice = 200;

    [Tooltip("아처 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int archerUnlockPrice = 200;

    [Header("이동 속도")]
    [Tooltip("플레이어 기본(걷기) 이동속도")]
    public float playerMoveSpeed = 3.75f;

    [Tooltip("순찰형 몬스터(MonsterAI)의 순찰 이동속도")]
    public float patrolMonsterPatrolSpeed = 1.6f;

    [Tooltip("소리반응형 몬스터(SoundReactiveMonsterAI)의 배회 이동속도")]
    public float soundReactiveMonsterWanderSpeed = 2.4f;

    [Tooltip("몬스터 추격 속도(고정값). playerMoveSpeed 등 다른 수치가 바뀌어도 함께 변하지 않는 독립된 절대 수치다. " +
        "두 몬스터 타입(MonsterAI/SoundReactiveMonsterAI) 공통으로 적용된다.")]
    public float monsterChaseSpeed = 4.3125f;

    /// <summary>스카우트의 기본(걷기) 이동속도. 몬스터 추격 속도(고정값)에 scoutMoveSpeedMultiplier를 곱해 구한다.</summary>
    public float ScoutMoveSpeed => monsterChaseSpeed * scoutMoveSpeedMultiplier;

    [Header("스태미나")]
    [Tooltip("최대 스태미나")]
    public float maxStamina = 85f;

    [Tooltip("스프린트 중 초당 소모되는 스태미나")]
    public float sprintStaminaDrainPerSecond = 18f;

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
    [Tooltip("포획망 한 번 휘두를 때 몬스터에게 주는 데미지. 근접이라 리스크가 큰 대신, 확실하게 몬스터를 " +
        "처리할 수 있는 게 트래퍼만의 강점이 되도록 다른 클래스보다 높게 잡는다. 정수가 아니어도 되며 " +
        "실제로 깎이는 체력에는 반올림 없이 그대로 누적된다(MonsterHealth.Health가 float).")]
    public float netDamage = 1.2f;

    [Tooltip("포획망이 닿는 거리")]
    public float netRange = 2f;

    [Tooltip("포획망이 닿는 부채꼴 각도(도)")]
    public float netAngle = 90f;

    [Tooltip("포획망을 다시 휘두르기까지 걸리는 쿨다운(초)")]
    public float netCooldown = 0.75f;

    [Header("무기 - 마취화살 탄약")]
    [Tooltip("아처 클래스를 처음 장착했을 때 함께 지급되는 시작 탄약 수")]
    public int tranquilizerStartingAmmo = 3;

    [Tooltip("마취화살 탄약의 최대 소지 개수")]
    public int tranquilizerMaxAmmo = 10;

    [Tooltip("상점에서 '마취화살 탄약' 아이템을 구매했을 때 한 번에 충전되는 탄약 수")]
    public int tranquilizerAmmoRefillAmount = 5;

    [Tooltip("마취화살(TranquilizerDart)이 날아가는 최대 사거리. 안전한 거리에서 대응할 수 있다는 아처만의 " +
        "확실한 강점이 되도록 다른 무기보다 멀리 잡는다. 다트의 실제 비행 시간은 이 값과 발사 속도(PlayerWeaponController.dartSpeed)로부터 계산된다.")]
    public float tranquilizerRange = 43.2f;

    [Header("소모품 - 지뢰 (임시값)")]
    [Tooltip("지뢰가 몬스터에게 주는 데미지")]
    public int mineDamage = 2;

    [Tooltip("지뢰에 맞은 몬스터가 스턴되는 시간(초)")]
    public float mineStunDuration = 1.5f;

    [Tooltip("인벤토리에서 지뢰 슬롯을 선택하고 클릭해 설치할 때, 플레이어가 바라보는 방향으로 얼마나 떨어진 위치에 놓을지")]
    public float minePlacementDistance = 1.2f;

    [Tooltip("지뢰가 몬스터를 감지해 발동하는 반경")]
    public float mineTriggerRadius = 0.4f;

    [Header("소모품 - 폭탄 (임시값)")]
    [Tooltip("폭탄이 범위 안의 각 몬스터에게 주는 데미지")]
    public int bombDamage = 3;

    [Tooltip("폭탄에 맞은 몬스터가 스턴되는 시간(초)")]
    public float bombStunDuration = 2f;

    [Tooltip("인벤토리에서 폭탄 슬롯을 선택하고 클릭해 사용했을 때 플레이어 위치를 중심으로 피해를 주는 반경")]
    public float bombRadius = 3f;

    [Tooltip("폭탄 사용 시 피해 범위를 보여주는 빨간 원 연출이 유지되는 시간(초)")]
    public float bombEffectDisplayDuration = 0.35f;

    [Tooltip("폭탄 범위 연출의 색상(알파값이 투명도)")]
    public Color bombEffectColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("소모품 - 디버그 시작 보유량")]
    [Tooltip("디버그용으로 게임 시작 시 기본으로 지급되는 지뢰 개수 (상점 구매 로직 완성 전 테스트용)")]
    public int debugStartingMineCount = 2;

    [Tooltip("디버그용으로 게임 시작 시 기본으로 지급되는 폭탄 개수 (상점 구매 로직 완성 전 테스트용)")]
    public int debugStartingBombCount = 1;

    [Header("상점 아이템 가격")]
    [Tooltip("ShelterScene의 각 상점 아이템(ShopItemInteractPoint) 이름과 가격. itemName은 ShopItemInteractPoint.GetPrice()가 " +
        "찾는 이름과 정확히 일치해야 한다. 클래스 해금 가격은 여기가 아니라 위의 scoutUnlockPrice/trapperUnlockPrice/archerUnlockPrice로 조정한다.")]
    public List<ShopItemPrice> shopItemPrices = new List<ShopItemPrice>
    {
        new ShopItemPrice { itemName = "마취화살 탄약", price = 30 },
        new ShopItemPrice { itemName = "지뢰", price = 40 },
        new ShopItemPrice { itemName = "폭탄", price = 70 },
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

    public int GetClassMaxHealthPercent(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutMaxHealthPercent,
        PlayerClass.Trapper => trapperMaxHealthPercent,
        _ => archerMaxHealthPercent
    };

    private float GetClassAmbientVisionRangeMultiplier(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutAmbientVisionRangePercent / 100f,
        PlayerClass.Trapper => trapperAmbientVisionRangePercent / 100f,
        PlayerClass.Archer => archerAmbientVisionRangePercent / 100f,
        _ => 1f
    };

    private float GetClassFocusedVisionRangeMultiplier(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutFocusedVisionRangePercent / 100f,
        PlayerClass.Trapper => trapperFocusedVisionRangePercent / 100f,
        PlayerClass.Archer => archerFocusedVisionRangePercent / 100f,
        _ => 1f
    };

    public float GetClassAmbientVisionRadius(PlayerClass playerClass) => ambientVisionRadius * GetClassAmbientVisionRangeMultiplier(playerClass);

    public float GetClassFocusedVisionRadius(PlayerClass playerClass) => focusedVisionRadius * GetClassFocusedVisionRangeMultiplier(playerClass);

    public float GetClassFocusedVisionAngle(PlayerClass playerClass) => focusedVisionAngle * GetClassFocusedVisionRangeMultiplier(playerClass);

    /// <summary>playerMoveSpeed 대비 클래스별 기본(걷기) 이동속도 배율. 스카우트는 ScoutMoveSpeed(몬스터 추격 속도 기준)를
    /// playerMoveSpeed로 환산한 값을, 트래퍼/아처는 1(기본 이동속도 그대로)을 반환한다.</summary>
    public float GetClassMoveSpeedMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? ScoutMoveSpeed / playerMoveSpeed : 1f;

    /// <summary>클래스별 스프린트 추가 가속 배율(각 클래스 자신의 기본 이동속도 대비). 스카우트는 낮게, 트래퍼/아처는 높게 잡아서
    /// "스카우트는 평소에 살짝 빠르지만 위기 시 폭발적으로 빨라지지 못하고, 트래퍼/아처는 평소엔 느리지만 위기 시 폭발적으로
    /// 가속하는" 구조를 만든다.</summary>
    public float GetClassSprintSpeedMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? scoutSprintSpeedMultiplier : trapperArcherSprintSpeedMultiplier;

    public float GetClassStaminaDrainMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? scoutStaminaDrainMultiplier : 1f;

    public int GetClassUnlockPrice(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutUnlockPrice,
        PlayerClass.Trapper => trapperUnlockPrice,
        PlayerClass.Archer => archerUnlockPrice,
        _ => 0
    };
}
