using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class TruckStartPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private string villageSceneName = "VillageScene";
    [SerializeField] private GameObject promptUI;

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

        if (promptUI != null) promptUI.SetActive(false);
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptUI != null) promptUI.SetActive(inRange);

        if (inRange)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene(villageSceneName);
            }
        }
    }
}
