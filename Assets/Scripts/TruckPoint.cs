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

        AnimalRescue heldAnimal = inRange ? FindHeldAnimal() : null;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange)
            {
                promptText.text = heldAnimal != null ? "E를 눌러 동물 구조하기" : "E를 눌러 트럭에 타기";
            }
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        if (heldAnimal != null)
        {
            DeliverAnimal(heldAnimal);
        }
        else
        {
            SceneManager.LoadScene(truckSceneName);
        }
    }

    private AnimalRescue FindHeldAnimal()
    {
        AnimalRescue[] animals = Object.FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude);
        foreach (AnimalRescue animal in animals)
        {
            if (animal.IsHeld) return animal;
        }
        return null;
    }

    private void DeliverAnimal(AnimalRescue animal)
    {
        int weight = animal.Weight;
        animal.CompleteRescue();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddCargo(weight);
        }
        else
        {
            Debug.LogWarning($"{name}: no GameManager instance found; cargo not recorded.");
        }
    }
}
