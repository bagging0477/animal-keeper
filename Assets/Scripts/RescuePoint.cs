using UnityEngine;

public class RescuePoint : MonoBehaviour
{
    [SerializeField] private float completionRange = 1f;

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
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > completionRange) return;

        AnimalRescue[] animals = Object.FindObjectsByType<AnimalRescue>(FindObjectsSortMode.None);
        foreach (AnimalRescue animal in animals)
        {
            if (animal.IsHeld)
            {
                animal.CompleteRescue();
            }
        }
    }
}
