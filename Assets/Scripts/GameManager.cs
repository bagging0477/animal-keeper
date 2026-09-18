using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public const int MaxDay = 3;
    public const int InventorySlotCount = 5;

    /// <summary>config가 아직 연결되지 않았을 때만 쓰이는 안전장치용 기본값.</summary>
    private const int FallbackMaxHealth = 100;

    public static int MaxHealth => Instance != null ? Instance.ComputeMaxHealth() : FallbackMaxHealth;

    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private int day = 1;

    public int Day => day;
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

    public void AdvanceDay()
    {
        if (day >= MaxDay) return;
        day++;
        rescuedAnimalIdsToday.Clear();
        Health = MaxHealth;
    }

    public void ResetDay()
    {
        day = 1;
        rescuedAnimalIdsToday.Clear();
        Health = MaxHealth;
        gameSessionSeedInitialized = false;
    }

    public void ResetGame()
    {
        day = 1;
        rescuedAnimalIdsToday.Clear();
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

    /// <summary>true면 TruckScene(Day 1)에 들어가기 전에 반드시 ClassSelectScene에서 시작 클래스를
    /// 골라야 한다 - 새 게임을 막 시작했거나 ResetGame()으로 재시작한 직후에만 켜진다. ResetDay()(Day
    /// 클리어 후 "다음 날로")는 마일리지/해금 클래스를 그대로 유지하는 소프트 리셋이라 여기 포함되지
    /// 않는다 - 이미 고른 클래스를 잃지 않으므로 다시 고를 필요가 없다.</summary>
    public bool NeedsStartingClassSelection { get; private set; } = true;

    public void CompleteStartingClassSelection() => NeedsStartingClassSelection = false;

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
        public bool BonusApplied;
        public int BonusMileage;
        public int TotalMileage;
    }

    /// <summary>ShelterScene에 처음 들어왔을 때 이미 정산했는지 여부. ClassSelectScene을 거쳐
    /// ShelterScene으로 다시 돌아왔을 때 SettleCargo를 또 호출하지 않게 막는 가드로 쓰인다.
    /// (예전에는 이 값을 ShelterSettlement의 static 필드로 뒀는데, 에디터에서 Play를 여러 번
    /// 껐다 켜는 동안 static이 초기화되지 않고 남아있는 경우가 있어 실제 정산이 조용히 건너뛰어지는
    /// 버그가 있었다 - DontDestroyOnLoad로 살아있는 GameManager 인스턴스 필드로 옮겨서, 매 Play마다
    /// 새 GameManager가 만들어질 때 확실히 초기화되게 했다.)</summary>
    public bool HasSettledCargo { get; private set; }
    public SettlementResult LastSettlementResult { get; private set; }

    public SettlementResult SettleCargo(int pricePerWeight, int bonusMileage)
    {
        int totalWeight = TotalWeight;
        int baseMileage = totalWeight * pricePerWeight;
        bool bonusApplied = RescuedCount >= TargetCount;
        int bonus = bonusApplied ? bonusMileage : 0;
        int total = baseMileage + bonus;

        Mileage += total;
        cargoWeights.Clear();
        RescuedCount = 0;

        if (day >= MaxDay)
        {
            IsGameOver = !bonusApplied;
        }

        SettlementResult result = new SettlementResult
        {
            TotalWeight = totalWeight,
            PricePerWeight = pricePerWeight,
            BaseMileage = baseMileage,
            BonusApplied = bonusApplied,
            BonusMileage = bonus,
            TotalMileage = total
        };

        HasSettledCargo = true;
        LastSettlementResult = result;
        return result;
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

    /// <summary>클래스별 고정 무기. 스카우트는 무기 없음, 트래퍼는 근접(포획망), 아처는 원거리(마취화살)만 쓴다.
    /// static이라 ClassInteractPoint 같은 곳에서 아직 장착하지 않은(현재 클래스가 아닌) 다른 클래스의
    /// 무기를 미리 보여줄 때도 이 한 곳의 매핑을 그대로 재사용할 수 있다.</summary>
    public static WeaponType GetWeaponForClass(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Trapper => WeaponType.Net,
        PlayerClass.Archer => WeaponType.TranquilizerGun,
        _ => WeaponType.None
    };

    public bool IsClassUnlocked(PlayerClass playerClass) =>
        playerClass == PlayerClass.Scout || unlockedClasses.Contains(playerClass);

    public int TranquilizerAmmo { get; private set; }
    public int MaxTranquilizerAmmo => config != null ? config.tranquilizerMaxAmmo : 10;

    /// <summary>마일리지를 소모해 클래스를 해금한다(장착은 하지 않음). 스카우트는 항상 해금 상태라 대상이 될 수 없다.</summary>
    public bool PurchaseClass(PlayerClass playerClass)
    {
        if (playerClass == PlayerClass.Scout || unlockedClasses.Contains(playerClass)) return false;

        int price = config != null ? config.GetClassUnlockPrice(playerClass) : 0;
        if (!TrySpendMileage(price)) return false;

        unlockedClasses.Add(playerClass);
        return true;
    }

    /// <summary>시작 시 무료 클래스 선택(NeedsStartingClassSelection)에서만 쓰인다 - 마일리지를 전혀
    /// 건드리지 않고 바로 해금한다. PurchaseClass와 달리 가격 확인/차감이 없다.</summary>
    public bool UnlockClassFree(PlayerClass playerClass)
    {
        if (playerClass == PlayerClass.Scout || unlockedClasses.Contains(playerClass)) return false;

        unlockedClasses.Add(playerClass);
        return true;
    }

    /// <summary>이미 해금된 클래스를 현재 클래스로 장착한다. 체력은 새 클래스의 최대 체력으로 채워지고,
    /// 아처를 처음 장착할 때는 마취화살 탄약을 시작 수량만큼 지급한다.</summary>
    public bool SelectClass(PlayerClass playerClass)
    {
        if (!IsClassUnlocked(playerClass)) return false;

        CurrentClass = playerClass;
        Health = MaxHealth;

        if (playerClass == PlayerClass.Archer && TranquilizerAmmo <= 0)
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

    /// <summary>true면 클래스 고정 무기(트래퍼 포획망/아처 마취화살)가 "장착되어 사용 가능한" 상태다.
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

    public bool TryAddAnimal(string animalId, int weight, AnimalBehaviorKind kind) =>
        TryAddItem(new InventorySlotData { Type = InventoryItemType.Animal, AnimalId = animalId, AnimalWeight = weight, AnimalKind = kind });

    public bool TryAddMine() => TryAddItem(new InventorySlotData { Type = InventoryItemType.Mine });

    public bool TryAddBomb() => TryAddItem(new InventorySlotData { Type = InventoryItemType.Bomb });

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

    /// <summary>같은 Day 안에서는 VillageScene을 몇 번을 다시 들어가도 같은 시드를 돌려줘서
    /// 똑같은 맵이 나오게 한다. Day가 바뀌면 자동으로 다른 값이 나온다.
    /// (Random.Range(int.MinValue, int.MaxValue)는 내부 범위 계산에서 오버플로가 나는지
    /// 항상 같은 값을 반환해서 Day가 바뀌어도 맵이 안 바뀌는 버그가 있었다 - Guid 기반으로 교체.)</summary>
    public int GetVillageMapSeedForToday()
    {
        if (!gameSessionSeedInitialized)
        {
            gameSessionSeedInitialized = true;
            gameSessionSeed = System.Guid.NewGuid().GetHashCode();
        }
        return gameSessionSeed + day * 7919;
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
