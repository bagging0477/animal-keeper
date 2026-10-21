using UnityEngine;

/// <summary>
/// 게임의 체력·스태미나 값을 AnimalKeeperHUD(Assets/Sprites/Player/AnimalKeeperHUD, 에셋 그대로)에 넘기는 연결 코드.
/// - 체력: GameManager.Instance.Health / GameManager.MaxHealth (예전 HealthUI와 같은 값)
/// - 스태미나: "Player" 태그 오브젝트의 PlayerMovement.Stamina / MaxStamina (예전 StaminaUI와 같은 값)
/// 값이 바뀐 프레임에만 SetHealth/SetStamina를 부른다. 계산(데미지·탈진·회복)은 하지 않고 읽기만 한다. 구독하는 이벤트는 없다.
///
/// 이 오브젝트는 씬 HUD Canvas(기준 2240x1260, Match 0.5)의 자식이고, AnimalKeeperHUD가 Start에서 만드는 Canvas는 그 안에 들어가는
/// 중첩 Canvas가 된다 - 크기 조정과 그리는 순서는 씬 Canvas를 따르고(에셋의 1280x720 스케일러·정렬 20은 중첩 Canvas라 쓰이지 않는다),
/// 예전 체력 바 자리의 형제 순서 그대로 그려진다. 중첩 Canvas는 화면 크기로 늘어나지 않으므로 처음 한 번 부모를 꽉 채우게 맞춘다.
/// 실행 순서를 앞당겨(-10) AnimalKeeperHUD.Start보다 먼저 현재 값을 넣는다 - 씬이 시작될 때 100%에서 내려오지 않고 바로 맞는 값으로 보인다.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerStatusHUD : MonoBehaviour
{
    [SerializeField] private AnimalKeeperHUD hud;

    private PlayerMovement player;
    private bool warnedHud, warnedPlayer, canvasFitted;
    private int lastHealth = int.MinValue, lastMaxHealth = int.MinValue;
    private float lastStamina = float.NaN, lastMaxStamina = float.NaN;

    /// <summary>값을 넘기는 대상 HUD(테스트용).</summary>
    public AnimalKeeperHUD Hud => hud;

    private void Awake()
    {
        if (hud == null) hud = GetComponent<AnimalKeeperHUD>();
    }

    private void Start()
    {
        player = FindPlayer();
        if (player == null && !warnedPlayer)
        {
            warnedPlayer = true;
            Debug.LogWarning($"{name}: Player 태그의 PlayerMovement를 찾지 못했다 - 찾을 때까지 스태미나 바는 0으로 보인다.", this);
        }
        Push();
    }

    private void Update() => Push();

    private void LateUpdate()
    {
        if (!canvasFitted) FitNestedCanvas();
    }

    private void Push()
    {
        if (hud == null)
        {
            if (!warnedHud)
            {
                warnedHud = true;
                Debug.LogWarning($"{name}: AnimalKeeperHUD 참조가 없어 체력·스태미나 바를 갱신하지 않는다.", this);
            }
            return;
        }

        GameManager gm = GameManager.Instance;
        int maxHealth = GameManager.MaxHealth;
        int health = gm != null ? gm.Health : maxHealth;
        if (health != lastHealth || maxHealth != lastMaxHealth)
        {
            lastHealth = health;
            lastMaxHealth = maxHealth;
            hud.SetHealth(health, maxHealth);
        }

        if (player == null) player = FindPlayer();
        float stamina = player != null ? player.Stamina : 0f;
        float maxStamina = player != null ? player.MaxStamina : 100f;
        if (stamina != lastStamina || maxStamina != lastMaxStamina)
        {
            lastStamina = stamina;
            lastMaxStamina = maxStamina;
            hud.SetStamina(stamina, maxStamina);
        }
    }

    private static PlayerMovement FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.GetComponent<PlayerMovement>() : null;
    }

    // AnimalKeeperHUD가 만든 Canvas가 중첩 Canvas면 부모(이 오브젝트, 씬 Canvas를 꽉 채움)를 꽉 채우게 한다.
    // 최상위 Canvas로 쓰이는 경우(씬 루트에 둔 경우)는 에셋 기본 동작 그대로 둔다.
    private void FitNestedCanvas()
    {
        if (hud == null) return;
        Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
        if (canvas == null || canvas.gameObject == hud.gameObject) return; // 아직 Start 전
        canvasFitted = true;
        if (canvas.isRootCanvas) return;
        RectTransform rt = (RectTransform)canvas.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
