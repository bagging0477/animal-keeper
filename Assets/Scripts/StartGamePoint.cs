using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>ClassSelectScene이 "시작 시 무료 선택" 모드(GameManager.NeedsStartingClassSelection)일 때만
/// 나타나는 진행 지점. E로 상호작용하면 그 플래그를 끄고 TruckScene(Day 1)으로 이동한다 - 이 시점까지
/// 고른 클래스(아무 스탠드도 안 골랐다면 기본값인 스카우트)가 그대로 해금+장착된 채로 게임이 시작된다.
/// 평소(마일리지로 다른 클래스를 사러 오는) 방문에서는 이 지점 대신 ShelterReturnDoor(shelterReturnDoor)가
/// 나타나야 하므로, 이 스크립트가 그 door의 활성 상태도 같이 관리한다 - 시작 선택이 끝나기 전에
/// ShelterScene으로 돌아가 버리면(그날 화물을 하나도 못 모은 채로) 정산이 잘못 확정되는 버그가 생긴다.</summary>
public class StartGamePoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 2.1f;
    [SerializeField] private string truckSceneName = "TruckScene";
    [SerializeField] private GameObject shelterReturnDoor;

    private Transform player;

    private void Start()
    {
        bool needsSelection = GameManager.Instance != null && GameManager.Instance.NeedsStartingClassSelection;

        gameObject.SetActive(needsSelection);
        if (shelterReturnDoor != null) shelterReturnDoor.SetActive(!needsSelection);
        if (!needsSelection) return;

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
        bool inRange = distance <= interactionRange && InteractionFocus.TryFocus(this, distance);

        if (inRange) SubtitleManager.Show("E를 눌러 시작하기", SubtitleManager.WhileInRange);

        if (!inRange) return;

        Keyboard kb = PauseMenu.IsPaused ? null : Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame || GameManager.Instance == null) return;

        if (!SceneTransitionGuard.TryBeginTransition()) return;
        GameManager.Instance.CompleteStartingClassSelection();
        SceneManager.LoadScene(truckSceneName);
    }
}
