using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public const int MaxDay = 3;
    public const int MaxHealth = 100;

    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private int day = 1;
    [SerializeField] private int targetCount = 3;

    public int Day => day;
    public int TargetCount => targetCount;
    public int RescuedCount { get; private set; }
    public int Health { get; private set; } = MaxHealth;
    public bool IsGameOver { get; private set; }

    private readonly List<int> cargoWeights = new List<int>();
    public IReadOnlyList<int> CargoWeights => cargoWeights;

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
        Health = MaxHealth;
        Mileage = 0;
        cargoWeights.Clear();
        RescuedCount = 0;
        ownedWeapons.Clear();
        EquippedWeapon = WeaponType.None;
        IsGameOver = false;
        gameSessionSeedInitialized = false;
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

    public SettlementResult SettleCargo(int pricePerWeight, int bonusMileage)
    {
        int totalWeight = TotalWeight;
        int baseMileage = totalWeight * pricePerWeight;
        bool bonusApplied = RescuedCount >= targetCount;
        int bonus = bonusApplied ? bonusMileage : 0;
        int total = baseMileage + bonus;

        Mileage += total;
        cargoWeights.Clear();
        RescuedCount = 0;

        if (day >= MaxDay)
        {
            IsGameOver = !bonusApplied;
        }

        return new SettlementResult
        {
            TotalWeight = totalWeight,
            PricePerWeight = pricePerWeight,
            BaseMileage = baseMileage,
            BonusApplied = bonusApplied,
            BonusMileage = bonus,
            TotalMileage = total
        };
    }

    public bool TrySpendMileage(int amount)
    {
        if (amount > Mileage) return false;
        Mileage -= amount;
        return true;
    }

    private readonly HashSet<WeaponType> ownedWeapons = new HashSet<WeaponType>();
    public WeaponType EquippedWeapon { get; private set; } = WeaponType.None;

    public bool OwnsWeapon(WeaponType type) => ownedWeapons.Contains(type);

    public bool PurchaseWeapon(WeaponType type, int price)
    {
        if (type == WeaponType.None || ownedWeapons.Contains(type)) return false;
        if (!TrySpendMileage(price)) return false;

        ownedWeapons.Add(type);
        return true;
    }

    public bool TryEquipWeapon(WeaponType type)
    {
        if (type != WeaponType.None && !ownedWeapons.Contains(type)) return false;

        EquippedWeapon = type;
        return true;
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
    }
}
