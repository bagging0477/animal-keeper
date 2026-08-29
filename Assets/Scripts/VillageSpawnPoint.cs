using UnityEngine;

public class VillageSpawnPoint : MonoBehaviour
{
    [SerializeField] private string truckPointObjectName = "TruckPoint";
    [SerializeField] private Vector2 spawnOffset = new Vector2(0f, -1f);

    private void Awake()
    {
        GameObject truckPoint = GameObject.Find(truckPointObjectName);
        if (truckPoint != null)
        {
            transform.position = (Vector2)truckPoint.transform.position + spawnOffset;
        }
        else
        {
            Debug.LogWarning($"{name}: no GameObject named '{truckPointObjectName}' found in the scene.");
        }
    }
}
