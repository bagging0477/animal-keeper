using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TruckStartPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
    [SerializeField] private string villageSceneName = "VillageScene";
    [SerializeField] private string shelterSceneName = "ShelterScene";
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

        bool isDown = GameManager.Instance != null && GameManager.Instance.Health <= 0;
        bool isShelterDay = GameManager.Instance != null && GameManager.Instance.Day >= GameManager.MaxDay;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange)
            {
                promptText.text = isDown
                    ? "부상으로 이동할 수 없습니다. 다음 날로 이동해주세요"
                    : (isShelterDay ? "E를 눌러 보호소로 이동" : "E를 눌러 구조 시작");
            }
        }

        if (!inRange || isDown) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        SceneManager.LoadScene(isShelterDay ? shelterSceneName : villageSceneName);
    }
}
