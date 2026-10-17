using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>ShelterScene의 상인 한 명. 가까이 가서 E를 누르면 공용 상점 메뉴(ShopMenuUI)를 자기 품목 목록
/// (ShopCatalog)으로 연다. 상인마다 다른 것은 catalog 에셋뿐이라, 새 상인은 이 컴포넌트에 다른 목록만 넣으면 된다.</summary>
public class ShopMerchant : MonoBehaviour
{
    [SerializeField] private ShopCatalog catalog;
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private float interactionRange = 1.6f;
    [SerializeField] private string promptMessage = "E를 눌러 상점 열기";

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
        // 정산(DayClearPanel) 같은 다른 화면이 시간을 멈춰둔 동안에는 열지 않는다 - 상점을 닫을 때 시간을 되돌리면
        // 그 화면 뒤에서 게임이 다시 돌아간다 (PauseMenu와 같은 규칙).
        if (player == null || GameplayInput.IsBlocked || Time.timeScale == 0f) return;

        float distance = Vector2.Distance(transform.position, player.position);
        bool inRange = distance <= interactionRange && InteractionFocus.TryFocus(this, distance);

        if (inRange) SubtitleManager.Show(promptMessage, SubtitleManager.WhileInRange);

        if (!inRange) return;

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        ShopMenuUI.Open(catalog, config);
    }
}
