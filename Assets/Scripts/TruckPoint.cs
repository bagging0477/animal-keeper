using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TruckPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.8f;
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private Text promptText;

    private Transform player;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
        }

        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange) promptText.text = "E를 눌러 트럭에 타기";
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        foreach (AnimalRescue heldAnimal in FindHeldAnimals())
        {
            DeliverAnimal(heldAnimal);
        }

        SceneManager.LoadScene(truckSceneName);
    }

    private AnimalRescue[] FindHeldAnimals()
    {
        AnimalRescue[] animals = Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude);
        List<AnimalRescue> held = new List<AnimalRescue>();
        foreach (AnimalRescue animal in animals)
        {
            if (animal.IsHeld) held.Add(animal);
        }
        return held.ToArray();
    }

    private void DeliverAnimal(AnimalRescue animal)
    {
        int weight = animal.Weight;
        string animalId = animal.name;
        animal.CompleteRescue();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddCargo(weight);
            GameManager.Instance.MarkAnimalRescuedToday(animalId);
        }
        else
        {
            Debug.LogWarning($"{name}: no GameManager instance found; cargo not recorded.");
        }
    }
}
