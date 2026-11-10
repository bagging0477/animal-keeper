using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>TruckScene 안의 동물 납품 자리(직원 DeliveryClerk). 범위 안에서 E를 한 번 누르면 숫자키 선택과 상관없이
/// 인벤토리의 동물(몬스터 시체 포함)을 모두 맡긴다(GameManager.DeliverAllAnimals). 지뢰·폭탄은 남는다.
/// 범위 안 안내: 맡길 것이 있으면 "E를 눌러 모두 맡기기 (N마리)", 없으면 아무것도 띄우지 않는다.
/// 맡기면 직원 그림(clerk)의 받기 동작을 한 번 재생한다(몇 마리를 맡겨도 한 번).</summary>
public class AnimalDeliveryPoint : MonoBehaviour
{
    [SerializeField] private float interactionRange = 2.3f;
    [Tooltip("맡기면 받기 동작을 재생할 직원 그림. 비워 두면 자식에서 찾는다.")]
    [SerializeField] private DeliveryClerkAnimator clerk;

    private Transform player;

    /// <summary>범위 안 안내 문구. count는 맡길 동물(시체 포함) 수.</summary>
    public static string DeliverPrompt(int count) => $"E를 눌러 모두 맡기기 ({count}마리)";

    private void Awake()
    {
        if (clerk == null) clerk = GetComponentInChildren<DeliveryClerkAnimator>();
    }

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
        if (player == null || GameManager.Instance == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance > interactionRange || !InteractionFocus.TryFocus(this, distance)) return;

        int carried = GameManager.Instance.CarriedAnimalCount;
        if (carried == 0) return;
        SubtitleManager.Show(DeliverPrompt(carried), SubtitleManager.WhileInRange);

        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        int delivered = GameManager.Instance.DeliverAllAnimals();
        if (delivered > 0)
        {
            Debug.Log($"동물 {delivered}마리를 납품했습니다");
            if (clerk != null) clerk.PlayTake();
        }
    }
}
