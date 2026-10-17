using UnityEngine;

/// <summary>특정 씬에서 넘어왔을 때만 플레이어를 이 위치에서 시작시킨다. 예: ClassSelectScene의 "보호소로
/// 돌아가기" 문으로 ShelterScene에 돌아오면 트럭에서 왔을 때의 기본 위치 대신 클래스 선택 문 앞에서 시작한다.
/// 다른 씬에서 왔으면 씬에 배치된 플레이어 위치를 그대로 둔다.</summary>
[DefaultExecutionOrder(-50)] // 다른 스크립트의 Start(거리 기반 상호작용 판정 등)보다 먼저 옮긴다.
public class ArrivalSpawnPoint : MonoBehaviour
{
    [SerializeField] private string fromSceneName = "ClassSelectScene";

    // SceneHistory는 sceneLoaded(모든 Awake 이후)에 갱신되므로 Awake가 아닌 Start에서 확인한다.
    private void Start()
    {
        if (SceneHistory.PreviousSceneName != fromSceneName) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning($"{name}: no GameObject tagged 'Player' found in the scene.");
            return;
        }

        Vector3 position = new Vector3(transform.position.x, transform.position.y, player.transform.position.z);
        player.transform.position = position;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = position;
    }
}
