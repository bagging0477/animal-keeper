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

    [Tooltip("순찰형 몬스터(늑대)가 이 거리보다 멀어지면 즉시 추격을 멈추고 그 자리에서 잠시 수색한 뒤 순찰로 돌아간다. " +
        "추격 속도(monsterChaseSpeed)가 트래퍼/거너 걷기 속도보다 빠르므로, 스프린트로 떼어낼 수 있는 거리여야 한다 " +
        "(DetectRange보다는 커야 바로 재발견되지 않는다).")]
    public float patrolMonsterLoseRange = 5.5f;

    [Tooltip("소리반응형 몬스터가 플레이어를 발견하는 거리")]
    public float soundReactiveMonsterDetectRange = 2.03f;

    [Tooltip("소리반응형 몬스터가 추격을 포기하기 시작하는(수색 상태로 전환되는) 거리. 기존(9)보다 1.5배 늘려서 " +
        "시야를 놓쳐도 더 멀리까지 쫓아오게 한다.")]
    public float soundReactiveMonsterLoseRange = 13.5f;

    [Tooltip("소리반응형 몬스터가 동물 소리를 듣고 반응하는 거리")]
    public float soundReactiveMonsterHearRange = 10.4f;

    [Header("몬스터 - 늑대(순찰형) 전용: 영역과 추격 피로")]
    [Tooltip("늑대의 영역 반경. 스폰 위치(홈)에서 플레이어까지의 거리가 이 값을 넘으면 추격을 포기하고 홈으로 걸어 돌아간다. " +
        "영역 밖에 있는 플레이어는 새로 발견하지도 않는다. 방 하나가 약 20칸이고, 너무 작으면 홈에서 바깥쪽으로 1~2초만 달려도 포기해 버린다.")]
    public float wolfLeashRadius = 14f;

    [Tooltip("늑대가 전속력(monsterChaseSpeed)으로 이 시간(초) 동안 계속 추격하면 잠깐 숨을 고른다.")]
    public float wolfChaseBurstDuration = 3.5f;

    [Tooltip("숨을 고르는 시간(초). 이 동안은 추격 속도가 wolfWindedSpeedMultiplier배로 떨어진다.")]
    public float wolfWindedDuration = 1.5f;

    [Tooltip("숨을 고르는 동안의 추격 속도 배율(monsterChaseSpeed 기준).")]
    [Range(0.1f, 1f)] public float wolfWindedSpeedMultiplier = 0.5f;

    [Header("몬스터 - 탐색(Searching) 상태")]
    [Tooltip("추격을 포기한 순찰형 몬스터가 마지막 목격 위치에 도착한 뒤 두리번거리는 시간(초)의 최솟값. " +
        "실제 시간은 최솟값~최댓값 사이에서 매번 랜덤으로 정해지고, 끝나면 순찰로 돌아간다. 소리반응형 몬스터는 탐색 없이 바로 배회로 돌아간다.")]
    public float monsterSearchLookDurationMin = 2f;

    [Tooltip("두리번거리는 시간(초)의 최댓값 - 위 최솟값과 함께 랜덤 범위를 정한다.")]
    public float monsterSearchLookDurationMax = 3f;

    [Tooltip("두리번거리는 동안 좌우로 고개(바라보는 방향)를 바꾸는 간격(초). 작을수록 빠르고 초조하게 두리번거린다.")]
    public float monsterSearchLookTurnInterval = 0.6f;

    [Tooltip("마지막 목격 위치까지 이 시간(초) 안에 도착하지 못하면(길이 막혔거나 너무 멀면) 그 자리에서 바로 두리번거리기 시작한다.")]
    public float monsterSearchTravelTimeout = 5f;

    [Tooltip("두리번거리는 도중 방향을 바꿀 차례마다 제자리에서 돌아보는 대신 주변으로 몇 걸음 옮길 확률(0~1). 0이면 제자리에서만 두리번거린다.")]
    [Range(0f, 1f)]
    public float monsterSearchStepChance = 0.4f;

    [Tooltip("두리번거리며 몇 걸음 옮길 때, 탐색 도착 지점에서 최대 이 거리 안쪽으로만 움직인다.")]
    public float monsterSearchStepRadius = 0.8f;

    [Tooltip("두리번거리며 몇 걸음 옮길 때의 이동속도 - 순찰 속도보다 느리게 둬서 조심스럽게 살피는 느낌을 낸다.")]
    public float monsterSearchStepSpeed = 1.2f;

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

    [Tooltip("소리반응형 몬스터(SoundReactiveMonsterAI) 전용 추격 포기 시간(초). 위 chaseGiveUpSightLostDuration은 " +
        "순찰형(MonsterAI)과 공유하는 값이라 그대로 늘리면 순찰형까지 같이 바뀌므로, blood 몬스터만 따로 더 오래 " +
        "쫓아오게 하려고 분리했다 - 순찰형은 이 필드와 무관하게 항상 chaseGiveUpSightLostDuration을 그대로 쓴다.")]
    public float soundReactiveMonsterChaseGiveUpDuration = 3.5f;

    [Header("몬스터 - 소리 강도 (Blood 몬스터 전용: 감지 범위와 Investigate 접근 거리 모두 이 값에 비례한다)")]
    [Tooltip("소리 내는 동물(AnimalSoundFlee)이 내는 소리의 강도. 기준값 1.0 - 감지 범위/접근 거리를 그대로(배율 1) 적용한다.")]
    public float animalSoundIntensity = 1f;

    [Tooltip("플레이어가 스프린트 중 주기적으로 내는 발소리의 강도. 동물 소리(1.0)보다 약해서, 감지 범위와 " +
        "한 번에 다가오는 접근 거리 모두 이 비율만큼 줄어든다(예: 0.3이면 감지 범위 30%, 접근 거리도 30%).")]
    public float sprintSoundIntensity = 0.3f;

    [Tooltip("플레이어가 스프린트 중일 때 이 주기(초)마다 발소리 SoundEvent를 하나씩 발생시킨다.")]
    public float sprintSoundInterval = 0.5f;

    [Tooltip("소리를 들은 소리반응형 몬스터가 Investigate 상태에서 한 번에 소리 발생 지점 쪽으로 좁히는 거리 " +
        "비율(0~1, 소리 강도 1.0 기준). 실제 적용 비율은 여기에 그 소리의 강도를 곱한 값이라, 강도가 낮을수록 " +
        "(예: 스프린트 발소리) 한 걸음에 다가가는 거리도 그만큼 작아진다. 소리가 반복되면(동물이 계속 울거나 " +
        "플레이어가 계속 스프린트하면) 매번 이만큼씩 더 다가가는 식으로 누적된다.")]
    [Range(0f, 1f)]
    public float soundInvestigateApproachFraction = 0.35f;

    [Header("바닥 타일 변형 (VillageScene 절차적 생성 전용)")]
    [Tooltip("Perlin Noise 샘플링 좌표에 곱해지는 배율. 값이 작을수록 노이즈가 넓게 퍼져서 " +
        "변형 타일 패치(이끼/얼룩)가 더 크고 완만하게 뭉친다. 값이 크면 패치가 잘게 쪼개져 " +
        "지저분해 보이므로 0.1~0.2 정도의 작은 값을 권장한다.")]
    public float floorPatchNoiseScale = 0.15f;

    [Tooltip("이 값 이상인 노이즈 영역에만 변형 타일이 나타난다(0~1). 값이 클수록 패치 빈도가 " +
        "줄어든다. 타일마다 독립적으로 확률 판정하는 대신 이 임계값으로 넓은 노이즈 지형 중 " +
        "일부 봉우리만 잘라내므로, 패치가 소금 뿌린 것처럼 흩어지지 않고 뭉쳐서 나타난다.")]
    [Range(0f, 1f)]
    public float floorPatchThreshold = 0.62f;

    [Header("몬스터 - 시체 획득 (임시값)")]
    [Tooltip("늑대 기반 순찰형 몬스터(PatrolMonster) 처치 후 시체를 주웠을 때의 무게. 일반 동물(1~10)보다 " +
        "더 무겁게 잡은 임시값이며, 최종 밸런스는 나중에 조정한다. 소리반응형(Blood) 몬스터에는 적용되지 않는다.")]
    public int wolfCorpseWeight = 15;

    [Header("몬스터 - 스폰 (VillageScene 절차적 생성 전용)")]
    [Tooltip("몬스터가 스폰될 때 플레이어 스폰 위치로부터 최소 이 거리 이상 떨어진 곳에만 생성되도록 시도한다. " +
        "스폰 시점에만 적용되며, 스폰 이후 순찰/배회로 이 범위 안에 들어오는 것은 막지 않는다.")]
    public float monsterMinSpawnDistanceFromPlayer = 6f;

    [Header("격자 맵 길 (VillageScene 격자 배치 전용)")]
    [Tooltip("방과 방을 잇는 길의 폭(칸). 방의 변마다 이 폭의 차선(입구 안쪽 2칸까지 장애물 없음)이 있어야 그 변으로 이웃과 이어진다. " +
        "기본 6, 7은 시험용(Forest 남/북, Graveyard 북 입구가 7칸까지 열려 있다).")]
    [Range(3, 8)]
    public int gridCorridorWidth = 6;

    [Tooltip("격자 칸 사이 틈(칸). 길이 이 틈 안에서 꺾이므로 길 폭 + 2 이상이어야 한다 - 더 좁으면 그 맵은 폭 + 2로 넓혀 만들고 경고를 남긴다.")]
    [Min(0)]
    public int gridCorridorGap = 8;

    [Tooltip("길 가장자리에서 안쪽으로 몇 칸에 걸쳐 어두워질지(방 가장자리의 VillageMapGenerator.outsideMaskInnerFadeWidth와 같은 방식). " +
        "1이면 폭 6 길의 가운데 4칸은 방 가운데와 같은 밝기로 남는다. 0이면 길 가장자리에서 바로 끊긴다.")]
    [Range(0f, 4f)]
    public float gridCorridorFadeDepth = 1f;

    [Header("격자 맵 스폰 (VillageScene 격자 배치 전용 - 한 줄 배치는 VillageMapGenerator의 방당 고정 수를 쓴다)")]
    [Tooltip("격자 맵 전체 동물 수. 대상 방마다 먼저 한 마리씩 돌아가고, 남으면 방당 상한까지 바닥이 넓은 방에 더 자주 간다. " +
        "모든 방이 상한에 차서 다 못 놓으면 경고를 남기고 상한까지만 놓는다.")]
    [Min(0)]
    public int gridAnimalTotal = 27;

    [Tooltip("격자 맵 전체 몬스터 수. 트럭 방(과 VillageMapGenerator.monsterFreeRoomRadius 안의 방)에는 두지 않고, 나머지 방에 동물과 같은 규칙으로 나눈다.")]
    [Min(0)]
    public int gridMonsterTotal = 12;

    [Tooltip("격자 맵에서 방 하나에 둘 수 있는 동물 최대 수")]
    [Min(1)]
    public int gridMaxAnimalsPerRoom = 3;

    [Tooltip("격자 맵에서 방 하나에 둘 수 있는 몬스터 최대 수")]
    [Min(1)]
    public int gridMaxMonstersPerRoom = 2;

    [Header("상태이상 - 포획망 스턴 / 마취총 수면")]
    [Tooltip("포획망에 맞은 몬스터가 스턴 상태가 되는 시간(초)")]
    public float stunDuration = 0.4f;

    [Tooltip("마취총에 맞은 뒤 실제로 잠들기까지 걸리는 시간(초)")]
    public float sleepDelay = 1f;

    [Tooltip("잠든 상태가 유지되는 시간(초) - 몬스터와 동물 모두에게 적용. 마취총은 거너만 쏠 수 있어 사실상 거너 전용 수치다.")]
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

    [Header("시야 라이트 블렌딩 - 원형/부채꼴 시야가 겹치는 경계가 띠처럼 보이지 않게")]
    [Tooltip("원형/부채꼴 시야 라이트 공통 밝기(Light2D Intensity). 둘을 같게 둬야 겹치는 곳에서 밝기가 어긋나지 않는다. " +
        "1을 넘기면 화면 최대 밝기에서 잘려 평평한 띠가 생기고 그 가장자리가 오히려 날카롭게 보인다.")]
    public float visionLightIntensity = 1f;

    [Tooltip("원형/부채꼴 시야 라이트 공통 감쇠 곡선 세기(Light2D Falloff Strength). 둘을 같은 곡선으로 어두워지게 한다.")]
    [Range(0f, 1f)]
    public float visionLightFalloffStrength = 0.5f;

    [Tooltip("부채꼴 시야 양옆 가장자리에서 한쪽당 몇 도에 걸쳐 서서히 어두워질지. 클수록 원형 시야와 부드럽게 섞인다 " +
        "(부채꼴 각도의 절반을 넘으면 가운데부터 바로 어두워지기 시작한다).")]
    public float focusedVisionEdgeFadeAngle = 30f;

    [Tooltip("부채꼴 시야가 최대 밝기를 유지하는 거리(반경 대비 비율). 작을수록 플레이어에서 멀어지며 일찍부터 서서히 어두워진다.")]
    [Range(0f, 1f)]
    public float focusedVisionInnerRadiusRatio = 0.55f;

    [Header("클래스 - 시야 범위 배율 (기준치 대비, %) - 100이면 기준치(위 ambientVisionRadius 등) 그대로. " +
        "원형 시야(Ambient)와 부채꼴 시야(Focused)를 따로 조절할 수 있다.")]
    [Tooltip("스카우트 기본 원형 시야(Ambient Vision) 반경 배율.")]
    public int scoutAmbientVisionRangePercent = 100;

    [Tooltip("트래퍼 기본 원형 시야(Ambient Vision) 반경 배율.")]
    public int trapperAmbientVisionRangePercent = 100;

    [Tooltip("거너 기본 원형 시야(Ambient Vision) 반경 배율 - 부채꼴 시야보다도 더 넓게 잡아서, " +
        "바로 근처에서도 다른 클래스보다 확실히 더 잘 보이게 한다.")]
    public int gunnerAmbientVisionRangePercent = 150;

    [Tooltip("스카우트 부채꼴 시야(Focused Vision)의 반경/각도 배율.")]
    public int scoutFocusedVisionRangePercent = 100;

    [Tooltip("트래퍼 부채꼴 시야(Focused Vision)의 반경/각도 배율.")]
    public int trapperFocusedVisionRangePercent = 100;

    [Tooltip("거너 부채꼴 시야(Focused Vision)의 반경/각도 배율 - " +
        "체력이 낮은 대신 더 멀리, 더 넓게 보고 안전한 거리에서 대응할 수 있는 컨셉.")]
    public int gunnerFocusedVisionRangePercent = 125;

    [Header("클래스 - 이동속도 / 체력")]
    [Tooltip("스카우트 기본(걷기) 이동속도 배율 - 몬스터 추격 속도(monsterChaseSpeed, 고정값) 대비. " +
        "1.05~1.1 정도로 잡아서 '몬스터보다 여전히 빠르지만 차이는 크지 않은' 상태를 만든다 - 스태미나 없이 그냥 도망만 쳐도 " +
        "서서히 거리가 벌어지긴 하지만, 추격 포기 거리(patrolMonsterLoseRange 등)까지 벌어지려면 시간이 걸린다. " +
        "트래퍼/거너는 배율 1(기본 이동속도 그대로)을 쓴다.")]
    [Range(1f, 1.3f)]
    public float scoutMoveSpeedMultiplier = 1.08f;

    [Tooltip("스카우트 최대 체력 비율 (playerMaxHealth 대비, %) - 기준치. 이동속도가 빠른 대신 시야는 다른 클래스와 동일한 컨셉.")]
    public int scoutMaxHealthPercent = 100;

    [Tooltip("트래퍼 최대 체력 비율 (playerMaxHealth 대비, %). 근접전을 감당할 수 있는 확실한 맷집.")]
    public int trapperMaxHealthPercent = 130;

    [Tooltip("거너 최대 체력 비율 (playerMaxHealth 대비, %). 체력이 가장 낮은 대신 넓은 시야로 안전한 거리를 두고 싸우는 컨셉.")]
    public int gunnerMaxHealthPercent = 50;

    [Tooltip("스카우트의 스프린트 스태미나 소모 배율(다른 클래스 대비). 체력이 낮은데 지구력까지 약해서, " +
        "스프린트를 오래 쓰면 더 빨리 지치고 장기적으로 위험해지는 컨셉. 트래퍼/거너는 배율 1(기본)을 그대로 쓴다.")]
    public float scoutStaminaDrainMultiplier = 1.2f;

    [Tooltip("스카우트의 스프린트 추가 가속 배율 (스카우트 자신의 기본 이동속도 대비). 평소에 이미 몬스터보다 살짝 빠른 " +
        "대신, 위기 상황에서 폭발적으로 더 빨라지지는 못하는 컨셉이라 트래퍼/거너보다 낮게 잡는다.")]
    public float scoutSprintSpeedMultiplier = 1.1f;

    [Tooltip("트래퍼/거너의 스프린트 추가 가속 배율 (각자의 기본 이동속도 대비). 평소엔 스카우트보다 느리지만, " +
        "위기 상황에서는 폭발적으로 가속해 벗어날 수 있다.")]
    public float trapperGunnerSprintSpeedMultiplier = 1.3f;

    [Header("클래스 해금 가격 (마일리지) - 게임 시작 시 무료로 선택한 클래스는 이미 해금된 상태라 여기 가격과 " +
        "무관하게 장착만 하면 되고, 나머지 두 클래스만 여기 가격으로 구매해야 한다.")]
    [Tooltip("스카우트 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int scoutUnlockPrice = 200;

    [Tooltip("트래퍼 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int trapperUnlockPrice = 200;

    [Tooltip("거너 클래스 해금 가격 (시작 시 무료로 고르지 않았을 경우)")]
    public int gunnerUnlockPrice = 200;

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

    [Tooltip("도망가는 동물(고양이)/소리 내는 동물(레서판다, 둘 다 AnimalFlee 계열)이 평소 배회(Wander)할 때의 " +
        "이동속도. 원래는 얌전한 동물(코알라, AnimalWander)과 같은 값(0.8)으로 맞췄었는데, 너무 굼떠 보여서 25% 올렸다.")]
    public float animalWanderSpeed = 1.0f;

    [Tooltip("도망가는 동물/소리 내는 동물이 Alert 이후 Fleeing(도주) 상태로 전환됐을 때의 이동속도. " +
        "평소 배회 속도와 확실히 구분되도록 기존 값(2.5)보다 35% 빠르게 잡았다.")]
    public float animalFleeSpeed = 3.375f;

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

    [Tooltip("이 사이클(순찰) 수를 목표 달성과 함께 완주하면 게임 클리어 화면이 뜬다. 예: 10이면 " +
        "10번째 사이클 정산에서 목표를 달성하는 순간 클리어된다.")]
    public int totalCyclesToWin = 10;

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

    [Header("무기 - 마취총 탄약")]
    [Tooltip("거너 클래스를 처음 장착했을 때 함께 지급되는 시작 탄약 수")]
    public int tranquilizerStartingAmmo = 3;

    [Tooltip("마취총 탄약의 최대 소지 개수")]
    public int tranquilizerMaxAmmo = 10;

    [Tooltip("상점에서 '마취총 탄약' 아이템을 구매했을 때 한 번에 충전되는 탄약 수")]
    public int tranquilizerAmmoRefillAmount = 5;

    [Tooltip("마취총(TranquilizerDart)이 날아가는 최대 사거리. 안전한 거리에서 대응할 수 있다는 거너만의 " +
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

    [Tooltip("폭탄 사용 시 피해 범위를 보여주는 화염 연출이 유지되는 시간(초)")]
    public float bombEffectDisplayDuration = 0.45f;

    [Tooltip("화염 스프라이트가 없을 때 대신 쓰는 빨간 원 범위 연출의 색상(알파값이 투명도)")]
    public Color bombEffectColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("소모품 - 디버그 시작 보유량")]
    [Tooltip("디버그용으로 게임 시작 시 기본으로 지급되는 지뢰 개수 (상점 구매 로직 완성 전 테스트용)")]
    public int debugStartingMineCount = 2;

    [Tooltip("디버그용으로 게임 시작 시 기본으로 지급되는 폭탄 개수 (상점 구매 로직 완성 전 테스트용)")]
    public int debugStartingBombCount = 1;

    [Header("상점 아이템 가격")]
    [Tooltip("상인(ShopMerchant) 메뉴에서 파는 상점 아이템 종류별 가격. 새 아이템 종류(ShopItemKind)를 추가하면 여기에 " +
        "한 줄을 더한다. 클래스 해금 가격은 여기가 아니라 위의 scoutUnlockPrice/trapperUnlockPrice/gunnerUnlockPrice로 조정한다.")]
    public List<ShopItemPrice> shopItemPrices = new List<ShopItemPrice>
    {
        new ShopItemPrice { item = ShopItemKind.TranquilizerAmmo, price = 30 },
        new ShopItemPrice { item = ShopItemKind.Mine, price = 40 },
        new ShopItemPrice { item = ShopItemKind.Bomb, price = 70 },
    };

    // 예전에는 한글 이름 문자열로 찾아서, Inspector에서 이름이 조금만 달라져도 가격이 0(공짜)이 됐다 - 종류(enum)로 찾는다.
    [Serializable]
    public class ShopItemPrice
    {
        public ShopItemKind item;
        public int price;
    }

    public int GetShopItemPrice(ShopItemKind item)
    {
        foreach (ShopItemPrice entry in shopItemPrices)
        {
            if (entry.item == item) return entry.price;
        }

        Debug.LogError($"{name}: 상점 아이템 '{item}'의 가격이 shopItemPrices에 없다 - 0으로 팔린다.");
        return 0;
    }

    public int GetClassMaxHealthPercent(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutMaxHealthPercent,
        PlayerClass.Trapper => trapperMaxHealthPercent,
        _ => gunnerMaxHealthPercent
    };

    private float GetClassAmbientVisionRangeMultiplier(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutAmbientVisionRangePercent / 100f,
        PlayerClass.Trapper => trapperAmbientVisionRangePercent / 100f,
        PlayerClass.Gunner => gunnerAmbientVisionRangePercent / 100f,
        _ => 1f
    };

    private float GetClassFocusedVisionRangeMultiplier(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutFocusedVisionRangePercent / 100f,
        PlayerClass.Trapper => trapperFocusedVisionRangePercent / 100f,
        PlayerClass.Gunner => gunnerFocusedVisionRangePercent / 100f,
        _ => 1f
    };

    public float GetClassAmbientVisionRadius(PlayerClass playerClass) => ambientVisionRadius * GetClassAmbientVisionRangeMultiplier(playerClass);

    public float GetClassFocusedVisionRadius(PlayerClass playerClass) => focusedVisionRadius * GetClassFocusedVisionRangeMultiplier(playerClass);

    public float GetClassFocusedVisionAngle(PlayerClass playerClass) => focusedVisionAngle * GetClassFocusedVisionRangeMultiplier(playerClass);

    /// <summary>playerMoveSpeed 대비 클래스별 기본(걷기) 이동속도 배율. 스카우트는 ScoutMoveSpeed(몬스터 추격 속도 기준)를
    /// playerMoveSpeed로 환산한 값을, 트래퍼/거너는 1(기본 이동속도 그대로)을 반환한다.</summary>
    public float GetClassMoveSpeedMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? ScoutMoveSpeed / playerMoveSpeed : 1f;

    /// <summary>클래스별 스프린트 추가 가속 배율(각 클래스 자신의 기본 이동속도 대비). 스카우트는 낮게, 트래퍼/거너는 높게 잡아서
    /// "스카우트는 평소에 살짝 빠르지만 위기 시 폭발적으로 빨라지지 못하고, 트래퍼/거너는 평소엔 느리지만 위기 시 폭발적으로
    /// 가속하는" 구조를 만든다.</summary>
    public float GetClassSprintSpeedMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? scoutSprintSpeedMultiplier : trapperGunnerSprintSpeedMultiplier;

    public float GetClassStaminaDrainMultiplier(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout ? scoutStaminaDrainMultiplier : 1f;

    public int GetClassUnlockPrice(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Scout => scoutUnlockPrice,
        PlayerClass.Trapper => trapperUnlockPrice,
        PlayerClass.Gunner => gunnerUnlockPrice,
        _ => 0
    };
}
