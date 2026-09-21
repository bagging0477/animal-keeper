using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>TruckScene 필드에서 보호소로 가는 지점 - 부상으로 마을 탐색이 막혔을 때도 이 지점을 통해
/// 보호소로 가서 회복/정산할 수 있다. 이 지점 자체는 순수한 이동 포탈일 뿐이다 - 실제로 "오늘"을
/// 마감하는 판정(목표 달성 확인, 사이클 증가, 체력 회복)은 ShelterScene의 출구(ShelterExitPoint)에서만
/// 일어난다. "구조시작" 지점(TruckStartPoint)과 목적지를 완전히 분리해서, 목표를 막 달성한 순간
/// 우연히 여기로 넘어와 버리는(예전 토글 방식의 문제) 일이 없게 한다.</summary>
public class NextDayPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 1.2f;
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

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(inRange);
            if (inRange) promptText.text = "E를 눌러 보호소로 이동";
        }

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        SceneManager.LoadScene(shelterSceneName);
    }
}
