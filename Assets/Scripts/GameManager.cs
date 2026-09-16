using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public const int MaxDay = 3;

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
        MineCount = config != null ? config.debugStartingMineCount : 2;
        BombCount = config != null ? config.debugStartingBombCount : 1;
        HasSettledCargo = false;
        LastSettlementResult = default;
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
        AnimalRescue[] animals = FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude);
        foreach (AnimalRescue animal in animals)
        {
            if (animal.IsHeld) animal.Drop();
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

    /// <summary>지뢰/폭탄 소지 개수. 상점 구매 로직이 아직 없어서, Awake/ResetGame에서
    /// GameBalanceConfig의 디버그 시작 보유량으로 채워 테스트할 수 있게 한다.</summary>
    public int MineCount { get; private set; }
    public int BombCount { get; private set; }

    public bool TryConsumeMine()
    {
        if (MineCount <= 0) return false;
        MineCount--;
        return true;
    }

    public bool TryConsumeBomb()
    {
        if (BombCount <= 0) return false;
        BombCount--;
        return true;
    }

    public void AddMine(int amount) => MineCount += amount;
    public void AddBomb(int amount) => BombCount += amount;

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
        MineCount = config != null ? config.debugStartingMineCount : 2;
        BombCount = config != null ? config.debugStartingBombCount : 1;
    }
}
