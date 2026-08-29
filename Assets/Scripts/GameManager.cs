using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private int day = 1;
    [SerializeField] private int targetCount = 3;

    public int Day => day;
    public int TargetCount => targetCount;
    public int RescuedCount { get; private set; }

    private readonly List<int> cargoWeights = new List<int>();
    public IReadOnlyList<int> CargoWeights => cargoWeights;

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
