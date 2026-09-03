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
    }

    public void TakeDamage(int amount, string monsterTypeName)
    {
        if (Health <= 0) return;

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
