using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public const int InventorySlotCount = 5;

    /// <summary>config가 아직 연결되지 않았을 때만 쓰이는 안전장치용 기본값.</summary>
    private const int FallbackMaxHealth = 100;

    public static int MaxHealth => Instance != null ? Instance.ComputeMaxHealth() : FallbackMaxHealth;

    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private string truckSceneName = "TruckScene";

    /// <summary>게임을 시작한 뒤로 "다음 날로"를 몇 번 완료했는지(=몇 번째 순찰인지)를 센다. 예전
    /// 3일 주기의 Day처럼 특정 값에서 흐름이 바뀌는 게이트로 쓰이지 않는다 - 순수하게 누적되는
    /// 기록용 값으로, UI 표시와 이후 통계/난이도 조정에 쓰기 위해 존재한다. 1부터 시작해서
    /// ResetDay()가 호출될 때마다(정산 후든, 필드에서 바로든) 1씩 늘어난다.</summary>
    public int CycleCount { get; private set; } = 1;

    /// <summary>TruckScene의 구조시작 지점에서 다음 상호작용 때 마을 탐색 대신 보호소로 보낼지.
    /// 상호작용할 때마다 ToggleTruckDestination()으로 뒤집혀서, 같은 지점을 반복 방문할 때마다
    /// 두 목적지를 번갈아 제시한다.</summary>
    public bool NextTruckDestinationIsShelter { get; private set; }

    public void ToggleTruckDestination() => NextTruckDestinationIsShelter = !NextTruckDestinationIsShelter;

    /// <summary>TruckScene의 "다음날로" 지점(NextDayPoint)을 한 번 쓰면 false가 되어, 같은 트럭 방문
    /// 동안 계속 눌러서 CycleCount를 무한히 불릴 수 없게 막는다. 보호소(ShelterScene)를 한 번
    /// 다녀오면 다시 true로 풀린다 - 마을만 왕복해서는(트럭을 안 벗어나는 한) 다시 열리지 않는다.</summary>
    public bool NextDayPointAvailable { get; private set; } = true;

    public void MarkNextDayPointUsed() => NextDayPointAvailable = false;

    /// <summary>ShelterScene에 들어올 때 호출해서 다음날로 지점을 다시 쓸 수 있게 푼다.</summary>
    public void ReopenNextDayPoint() => NextDayPointAvailable = true;

    public int TargetCount => config != null ? config.targetRescueCount : 3;
    public int RescuedCount { get; private set; }
    public int Health { get; private set; }
    public bool IsGameOver { get; private set; }

    private int ComputeMaxHealth()
    {
        int baseHealth = config != null ? config.playerMaxHealth : FallbackMaxHealth;
        int percent = config != null ? config.GetClassMaxHealthPercent(CurrentClass) : 100;
        return Mathf.Max(1, Mathf.RoundToInt(baseHealth * (percent / 100f)));
    }

    private readonly List<int> cargoWeights = new List<int>();

    private readonly HashSet<string> rescuedAnimalIdsToday = new HashSet<string>();

    public bool IsAnimalRescuedToday(string animalId) => rescuedAnimalIdsToday.Contains(animalId);

    public void MarkAnimalRescuedToday(string animalId) => rescuedAnimalIdsToday.Add(animalId);

    /// <summary>납품하지 않고 아직 인벤토리에 들고만 있는 동물인지. 트럭에 들렀다가(납품하지 않고)
    /// 마을로 되돌아오면 VillageMapGenerator가 맵을 통째로 새로 생성하면서 같은 동물을 또 Instantiate
    /// 하는데, rescuedAnimalIdsToday는 실제 납품(TryDeliverSelectedAnimal) 시점에만 기록되므로 이 값만
    /// 보면 "이미 주웠지만 아직 안 판" 동물이 중복 스폰되는 것처럼 보였다 - AnimalRescue.Awake는
    /// 이 둘을 함께 확인해야 한다.</summary>
    public bool IsAnimalCurrentlyHeld(string animalId)
    {
        foreach (InventorySlotData slot in inventorySlots)
        {
            if (slot.Type == InventoryItemType.Animal && slot.AnimalId == animalId) return true;
        }
        return false;
    }

    private struct AnimalFieldState
    {
        public Vector3 Position;
        public bool WasFleeing;
    }

    // 오늘 아직 못 잡은 동물이 트럭에 갔다 왔을 때도 "떠날 당시 마지막 위치"에서 이어지도록 기억해둔다.
    // VillageMapGenerator가 같은 시드로 맵을 통째로 다시 만들 때 이 값을 찾아 스폰 위치를 덮어쓴다.
    private readonly Dictionary<string, AnimalFieldState> animalFieldStates = new Dictionary<string, AnimalFieldState>();

    /// <summary>AnimalRescue.OnDestroy에서(VillageScene이 언로드되기 직전) 호출된다 - 이 시점에는
    /// 아직 모든 동물의 Transform이 살아있어 각자 자기 위치를 안전하게 남길 수 있다.</summary>
    public void SaveAnimalFieldState(string animalId, Vector3 position, bool wasFleeing) =>
        animalFieldStates[animalId] = new AnimalFieldState { Position = position, WasFleeing = wasFleeing };

    public bool TryGetAnimalFieldState(string animalId, out Vector3 position, out bool wasFleeing)
    {
        if (animalFieldStates.TryGetValue(animalId, out AnimalFieldState saved))
        {
            position = saved.Position;
            wasFleeing = saved.WasFleeing;
            return true;
        }
        position = default;
        wasFleeing = false;
        return false;
    }

    public int TotalWeight
    {
        get
        {
            int sum = 0;
            foreach (int weight in cargoWeights) sum += weight;
            return sum;
        }
    }

    public void AddCargo(int weight)
    {
        cargoWeights.Add(weight);
        RescuedCount++;
    }

    /// <summary>"오늘"을 마무리하고 다음 순찰(사이클)로 넘어간다 - 정산을 마치고 ShelterScene에서
    /// 호출되든, 필드에서 보호소를 거치지 않고 TruckScene의 다음날로 지점에서 바로 호출되든 동일하게
    /// 동작한다. 예전처럼 Day가 1→2→3으로 누적되며 3일째에만 보호소로 갈 수 있던 게이트는 없고,
    /// 매번 "오늘"만 있는 구조라 항상 이 한 메서드로 오늘 상태를 리셋하면서 CycleCount만 늘린다.</summary>
    public void ResetDay()
    {
        CycleCount++;
        rescuedAnimalIdsToday.Clear();
        animalFieldStates.Clear();
        Health = MaxHealth;
        gameSessionSeedInitialized = false;

        // HasSettledCargo는 "이번 사이클에 이미 정산했는지"를 막는 가드다(ShelterScene ↔
        // ClassSelectScene을 오가도 SettleCargo가 중복 호출되지 않게). 예전엔 게임당 보호소를
        // 딱 한 번만 방문했으니 문제가 없었지만, 이제 사이클마다 반복 방문하므로 여기서 매번
        // 새로 열어주지 않으면 두 번째 사이클부터는 SettleCargo 자체가 다시는 실행되지 않는다.
        HasSettledCargo = false;
        LastSettlementResult = default;
    }

    public void ResetGame()
    {
        CycleCount = 1;
        NextTruckDestinationIsShelter = false;
        NextDayPointAvailable = true;
        rescuedAnimalIdsToday.Clear();
        animalFieldStates.Clear();
        Mileage = 0;
        cargoWeights.Clear();
        RescuedCount = 0;
        unlockedClasses.Clear();
        CurrentClass = PlayerClass.Scout;
        Health = MaxHealth;
        TranquilizerAmmo = 0;
        IsGameOver = false;
        gameSessionSeedInitialized = false;
        ResetInventory();
        HasSettledCargo = false;
        LastSettlementResult = default;
        NeedsStartingClassSelection = true;
    }

    /// <summary>true면 TruckScene(첫 순찰)에 들어가기 전에 반드시 ClassSelectScene에서 시작 클래스를
    /// 골라야 한다 - 새 게임을 막 시작했거나 ResetGame()으로 재시작한 직후에만 켜진다. ResetDay()("다음
    /// 날로")는 마일리지/해금 클래스를 그대로 유지하는 소프트 리셋이라 여기 포함되지 않는다 - 이미
    /// 고른 클래스를 잃지 않으므로 다시 고를 필요가 없다.</summary>
    public bool NeedsStartingClassSelection { get; private set; } = true;

    /// <summary>시작 시 무료 선택을 마친다. 이 시점의 CurrentClass(스탠드를 하나도 안 골랐다면 기본값인
    /// 스카우트)를 그대로 무료 해금 처리해서, 어떤 클래스를 골랐든(혹은 아무것도 안 골랐든) 그 클래스는
    /// 이후 다시 값을 지불하지 않고 계속 쓸 수 있게 한다.</summary>
    public void CompleteStartingClassSelection()
    {
        unlockedClasses.Add(CurrentClass);
        NeedsStartingClassSelection = false;
    }

    public void TakeDamage(int amount, string monsterTypeName)
    {
        if (Health <= 0) return;

        AudioManager.Instance?.PlayHit();
        Health = Mathf.Max(0, Health - amount);
        Debug.Log($"{monsterTypeName} 몬스터에게 당함! 데미지: {amount}%, 남은 체력: {Health}%");

        if (Health <= 0)
        {
            HandlePlayerDown();
        }
    }

    private void HandlePlayerDown()
    {
        // 부상으로 쓰러지면 그동안 주운 동물은 놓치고 돌아간다(지뢰/폭탄은 그대로 유지) -
        // 예전에 IsHeld 동물을 전부 Drop하던 것과 같은 의도를, 이제는 인벤토리의 동물 칸만 비우는
        // 것으로 구현한다.
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i].Type == InventoryItemType.Animal) inventorySlots[i] = InventorySlotData.Empty;
        }

        Debug.Log("부상으로 강제 복귀했습니다");
        SceneManager.LoadScene(truckSceneName);
    }

    public int Mileage { get; private set; }

    public struct SettlementResult
    {
        public int TotalWeight;
        public int PricePerWeight;
        public int BaseMileage;
    }

    /// <summary>ShelterScene에 처음 들어왔을 때 이미 정산했는지 여부. ClassSelectScene을 거쳐
    /// ShelterScene으로 다시 돌아왔을 때 SettleCargo를 또 호출하지 않게 막는 가드로 쓰인다.
    /// (예전에는 이 값을 ShelterSettlement의 static 필드로 뒀는데, 에디터에서 Play를 여러 번
    /// 껐다 켜는 동안 static이 초기화되지 않고 남아있는 경우가 있어 실제 정산이 조용히 건너뛰어지는
    /// 버그가 있었다 - DontDestroyOnLoad로 살아있는 GameManager 인스턴스 필드로 옮겨서, 매 Play마다
    /// 새 GameManager가 만들어질 때 확실히 초기화되게 했다.)</summary>
    public bool HasSettledCargo { get; private set; }
    public SettlementResult LastSettlementResult { get; private set; }

    /// <summary>보호소에 들어갈 때마다(하루에 여러 번 왕복 가능) 그 시점까지 실은 화물만 마일리지로
    /// 바꾼다. 목표 달성/게임 오버 판정은 여기서 하지 않는다 - 그 판정은 "오늘"이 실제로 끝나는
    /// 시점(FinishCycle, "다음 날로" 클릭)에만 한다. 예전엔 여기서 곧바로 판정까지 했는데, 하루에
    /// 여러 번 보호소를 들르는 게 가능해지면서 화물을 일부만 팔러 잠깐 들른 것도 "오늘 최종 판정"으로
    /// 취급되는 문제가 있었다 - 예를 들어 목표 3마리 중 2마리만 팔고 다시 나가 마저 채우려 했는데,
    /// 그 잠깐의 방문만으로 미달성 게임 오버가 나버렸다.</summary>
    public SettlementResult SettleCargo(int pricePerWeight)
    {
        int totalWeight = TotalWeight;
        int baseMileage = totalWeight * pricePerWeight;

        Mileage += baseMileage;
        cargoWeights.Clear();
        // RescuedCount는 여기서 초기화하지 않는다 - 오늘 목표 달성 여부는 하루 동안(여러 번의 정산에
        // 걸쳐) 누적 구조한 마릿수로 판정해야 하므로, FinishCycle이 하루를 마감할 때만 초기화한다.

        SettlementResult result = new SettlementResult
        {
            TotalWeight = totalWeight,
            PricePerWeight = pricePerWeight,
            BaseMileage = baseMileage
        };

        HasSettledCargo = true;
        LastSettlementResult = result;
        return result;
    }

    public struct CycleOutcome
    {
        public bool TargetMet;
        public int BonusMileage;
    }

    /// <summary>"오늘"을 실제로 마감할 때(TruckScene/ShelterScene 어느 쪽의 "다음 날로"든) 목표 달성
    /// 여부를 판정한다 - 보호소에 몇 번을 들렀든 상관없이, 오늘 누적 구조한 마릿수(RescuedCount)
    /// 기준으로 딱 한 번만 판정하고 곧바로 ResetDay()로 다음 사이클을 시작한다.</summary>
    public CycleOutcome FinishCycle()
    {
        bool targetMet = RescuedCount >= TargetCount;
        int bonus = 0;
        if (targetMet)
        {
            bonus = config != null ? config.targetBonusMileage : 50;
            Mileage += bonus;
        }

        IsGameOver = !targetMet;
        ResetDay();

        return new CycleOutcome { TargetMet = targetMet, BonusMileage = bonus };
    }

    public bool TrySpendMileage(int amount)
    {
        if (amount > Mileage) return false;
        Mileage -= amount;
        return true;
    }

    private readonly HashSet<PlayerClass> unlockedClasses = new HashSet<PlayerClass>();
    public PlayerClass CurrentClass { get; private set; } = PlayerClass.Scout;

    /// <summary>현재 장착 중인 클래스의 고정 무기.</summary>
    public WeaponType EquippedWeapon => GetWeaponForClass(CurrentClass);

    /// <summary>클래스별 고정 무기. 스카우트는 무기 없음, 트래퍼는 근접(포획망), 거너는 원거리(마취총)만 쓴다.
    /// static이라 ClassInteractPoint 같은 곳에서 아직 장착하지 않은(현재 클래스가 아닌) 다른 클래스의
    /// 무기를 미리 보여줄 때도 이 한 곳의 매핑을 그대로 재사용할 수 있다.</summary>
    public static WeaponType GetWeaponForClass(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Trapper => WeaponType.Net,
        PlayerClass.Gunner => WeaponType.TranquilizerGun,
        _ => WeaponType.None
    };

    public bool IsClassUnlocked(PlayerClass playerClass) => unlockedClasses.Contains(playerClass);

    public int TranquilizerAmmo { get; private set; }
    public int MaxTranquilizerAmmo => config != null ? config.tranquilizerMaxAmmo : 10;

    /// <summary>마일리지를 소모해 클래스를 해금한다(장착은 하지 않음). 이미 해금된 클래스(시작 시 무료로
    /// 고른 클래스 포함)는 대상이 될 수 없다.</summary>
    public bool PurchaseClass(PlayerClass playerClass)
    {
        if (unlockedClasses.Contains(playerClass)) return false;

        int price = config != null ? config.GetClassUnlockPrice(playerClass) : 0;
        if (!TrySpendMileage(price)) return false;

        unlockedClasses.Add(playerClass);
        return true;
    }

    /// <summary>시작 시 무료 클래스 선택(NeedsStartingClassSelection)에서만 쓰인다 - 마일리지를 전혀
    /// 건드리지 않고 바로 해금한다. PurchaseClass와 달리 가격 확인/차감이 없다.</summary>
    public bool UnlockClassFree(PlayerClass playerClass)
    {
        if (unlockedClasses.Contains(playerClass)) return false;

        unlockedClasses.Add(playerClass);
        return true;
    }

    /// <summary>이미 해금된 클래스를 현재 클래스로 장착한다. 체력은 새 클래스의 최대 체력으로 채워지고,
    /// 거너를 처음 장착할 때는 마취총 탄약을 시작 수량만큼 지급한다.</summary>
    public bool SelectClass(PlayerClass playerClass)
    {
        if (!IsClassUnlocked(playerClass)) return false;

        CurrentClass = playerClass;
        Health = MaxHealth;

        if (playerClass == PlayerClass.Gunner && TranquilizerAmmo <= 0)
        {
            int startingAmmo = config != null ? config.tranquilizerStartingAmmo : 3;
            TranquilizerAmmo = Mathf.Min(startingAmmo, MaxTranquilizerAmmo);
        }

        return true;
    }

    public bool TryConsumeTranquilizerAmmo()
    {
        if (TranquilizerAmmo <= 0) return false;
        TranquilizerAmmo--;
        return true;
    }

    public bool TryPurchaseTranquilizerAmmo(int price, int refillAmount)
    {
        if (!TrySpendMileage(price)) return false;

        TranquilizerAmmo = Mathf.Min(TranquilizerAmmo + refillAmount, MaxTranquilizerAmmo);
        return true;
    }

    // 5칸 인벤토리: 동물/지뢰/폭탄이 전부 이 슬롯을 공유해서 나눠 쓴다. 같은 종류라도 절대
    // 합쳐지지 않고 슬롯 하나에 1개씩만 들어가므로, 개수가 아니라 점유된 슬롯 수가 곧 소지량이다.
    private readonly InventorySlotData[] inventorySlots = new InventorySlotData[InventorySlotCount];

    public int SelectedSlotIndex { get; private set; }

    /// <summary>true면 클래스 고정 무기(트래퍼 포획망/거너 마취총)가 "장착되어 사용 가능한" 상태다.
    /// 숫자 슬롯 선택과 서로 배타적이다 - 무기를 선택하면 숫자 슬롯 아이템은 클릭해도 반응하지
    /// 않고, 숫자키를 누르면 다시 무기가 비활성화된다. SelectedSlotIndex 자체는 그대로 남아있어서,
    /// 무기를 쓰다가 숫자키 없이 돌아와도 G로 버릴 대상(직전에 고르던 일반 슬롯)을 기억한다.</summary>
    public bool IsWeaponSelected { get; private set; }

    public InventorySlotData GetSlot(int index) => inventorySlots[index];

    public bool HasInventorySpace => FindEmptySlotIndex() >= 0;

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= InventorySlotCount) return;
        SelectedSlotIndex = index;
        IsWeaponSelected = false;
    }

    public void SelectWeapon()
    {
        IsWeaponSelected = true;
    }

    private int FindEmptySlotIndex()
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i].Type == InventoryItemType.None) return i;
        }
        return -1;
    }

    private bool TryAddItem(InventorySlotData item)
    {
        int index = FindEmptySlotIndex();
        if (index < 0)
        {
            Debug.Log("인벤토리가 가득 찼습니다");
            return false;
        }

        inventorySlots[index] = item;
        return true;
    }

    public bool TryAddAnimal(string animalId, int weight, AnimalBehaviorKind kind, Sprite icon) =>
        TryAddItem(new InventorySlotData { Type = InventoryItemType.Animal, AnimalId = animalId, AnimalWeight = weight, AnimalKind = kind, Icon = icon });

    public bool TryAddMine(Sprite icon = null) => TryAddItem(new InventorySlotData { Type = InventoryItemType.Mine, Icon = icon });

    public bool TryAddBomb(Sprite icon = null) => TryAddItem(new InventorySlotData { Type = InventoryItemType.Bomb, Icon = icon });

    /// <summary>선택된 슬롯이 지뢰일 때만 소모한다 - 인벤토리 어딘가에 지뢰가 있어도 선택되어 있지
    /// 않으면 아무 일도 일어나지 않는다.</summary>
    public bool TryUseSelectedMine()
    {
        if (inventorySlots[SelectedSlotIndex].Type != InventoryItemType.Mine) return false;
        inventorySlots[SelectedSlotIndex] = InventorySlotData.Empty;
        return true;
    }

    /// <summary>선택된 슬롯이 폭탄일 때만 소모한다. TryUseSelectedMine과 동일한 이유.</summary>
    public bool TryUseSelectedBomb()
    {
        if (inventorySlots[SelectedSlotIndex].Type != InventoryItemType.Bomb) return false;
        inventorySlots[SelectedSlotIndex] = InventorySlotData.Empty;
        return true;
    }

    /// <summary>선택된 슬롯이 동물일 때만 납품한다(화물 적재 + 오늘 구조 기록). 선택되지 않은
    /// 슬롯에 동물이 있어도 납품되지 않는다 - 지뢰/폭탄과 동일하게 "선택해야 사용 가능" 규칙을 따른다.</summary>
    public bool TryDeliverSelectedAnimal()
    {
        InventorySlotData slot = inventorySlots[SelectedSlotIndex];
        if (slot.Type != InventoryItemType.Animal) return false;

        AddCargo(slot.AnimalWeight);
        MarkAnimalRescuedToday(slot.AnimalId);
        inventorySlots[SelectedSlotIndex] = InventorySlotData.Empty;
        return true;
    }

    /// <summary>현재 선택된 "일반" 슬롯(1~5)의 아이템을 꺼내 슬롯을 비운다. 무기 슬롯은 이 배열에
    /// 아예 들어있지 않으므로(클래스에 고정된 별도 장비) G로는 절대 버릴 수 없다.</summary>
    public bool TryDropSelectedItem(out InventorySlotData dropped)
    {
        dropped = inventorySlots[SelectedSlotIndex];
        if (dropped.Type == InventoryItemType.None) return false;

        inventorySlots[SelectedSlotIndex] = InventorySlotData.Empty;
        return true;
    }

    private void ResetInventory()
    {
        for (int i = 0; i < inventorySlots.Length; i++) inventorySlots[i] = InventorySlotData.Empty;
        SelectedSlotIndex = 0;
        IsWeaponSelected = false;

        int startingMines = config != null ? config.debugStartingMineCount : 2;
        int startingBombs = config != null ? config.debugStartingBombCount : 1;
        for (int i = 0; i < startingMines; i++) TryAddMine();
        for (int i = 0; i < startingBombs; i++) TryAddBomb();
    }

    private int gameSessionSeed;
    private bool gameSessionSeedInitialized;

    /// <summary>같은 사이클(오늘) 안에서는 VillageScene을 몇 번을 다시 들어가도 같은 시드를 돌려줘서
    /// 똑같은 맵이 나오게 한다. ResetDay()로 사이클이 넘어가면 자동으로 다른 값이 나온다.
    /// (Random.Range(int.MinValue, int.MaxValue)는 내부 범위 계산에서 오버플로가 나는지
    /// 항상 같은 값을 반환해서 시드가 안 바뀌는 버그가 있었다 - Guid 기반으로 교체.)</summary>
    public int GetVillageMapSeedForToday()
    {
        if (!gameSessionSeedInitialized)
        {
            gameSessionSeedInitialized = true;
            gameSessionSeed = System.Guid.NewGuid().GetHashCode();
        }
        return gameSessionSeed + CycleCount * 7919;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Health = MaxHealth;
        ResetInventory();
    }
}
