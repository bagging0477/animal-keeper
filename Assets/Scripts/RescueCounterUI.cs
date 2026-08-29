using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RescueCounterUI : MonoBehaviour
{
    [SerializeField] private Text label;

    private bool roundCleared;

    private void Update()
    {
        if (label == null || roundCleared) return;

        AnimalRescue[] animals = Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        int total = animals.Length;
        int rescued = 0;
        foreach (AnimalRescue animal in animals)
        {
            if (animal.IsCompleted) rescued++;
        }

        label.text = $"구조 현황: {rescued} / {total}";

        if (total > 0 && rescued >= total)
        {
            roundCleared = true;
            Debug.Log("라운드 클리어!");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
