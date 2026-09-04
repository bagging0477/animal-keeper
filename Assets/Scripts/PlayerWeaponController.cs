using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMovement))]
public class PlayerWeaponController : MonoBehaviour
{
    [Header("포획망 (근접)")]
    [SerializeField] private float netRange = 2f;
    [SerializeField] private float netAngle = 90f;
    [SerializeField] private int netDamage = 1;
    [SerializeField] private float netStunDuration = 0.4f;
    [SerializeField] private float netCooldown = 0.5f;

    [Header("마취총 (원거리)")]
    [SerializeField] private TranquilizerDart dartPrefab;
    [SerializeField] private float dartSpeed = 12f;
    [SerializeField] private float sleepDelay = 1f;
    [SerializeField] private float sleepDuration = 5f;

    [Header("장착 무기 표시")]
    [SerializeField] private SpriteRenderer weaponVisual;
    [SerializeField] private float weaponVisualScale = 0.25f;
    [SerializeField] private float weaponVisualDistance = 0.4f;
    [SerializeField] private Color netVisualColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color gunVisualColor = new Color(0.2f, 0.55f, 0.3f, 1f);

    private PlayerMovement playerMovement;
    private float netCooldownTimer;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();

        if (weaponVisual == null)
        {
            GameObject visualObj = new GameObject("WeaponVisual");
            visualObj.transform.SetParent(transform, false);
            visualObj.transform.localScale = Vector3.one * weaponVisualScale;

            weaponVisual = visualObj.AddComponent<SpriteRenderer>();
            SpriteRenderer playerSprite = GetComponent<SpriteRenderer>();
            weaponVisual.sprite = playerSprite != null ? playerSprite.sprite : null;
            weaponVisual.sortingOrder = (playerSprite != null ? playerSprite.sortingOrder : 0) + 1;
            weaponVisual.enabled = false;
        }
    }

    private void Update()
    {
        HandleWeaponSwitch();
        UpdateWeaponVisual();

        if (netCooldownTimer > 0f) netCooldownTimer -= Time.deltaTime;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Attack();
        }
    }

    private void UpdateWeaponVisual()
    {
        if (weaponVisual == null || GameManager.Instance == null) return;

        WeaponType equipped = GameManager.Instance.EquippedWeapon;
        if (equipped == WeaponType.None)
        {
            weaponVisual.enabled = false;
            return;
        }

        weaponVisual.enabled = true;
        weaponVisual.color = equipped == WeaponType.Net ? netVisualColor : gunVisualColor;
        weaponVisual.transform.position = transform.position + (Vector3)(playerMovement.LookDirection * weaponVisualDistance);
    }

    private void HandleWeaponSwitch()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || GameManager.Instance == null) return;

        if (kb.digit1Key.wasPressedThisFrame) GameManager.Instance.TryEquipWeapon(WeaponType.Net);
        else if (kb.digit2Key.wasPressedThisFrame) GameManager.Instance.TryEquipWeapon(WeaponType.TranquilizerGun);
    }

    private void Attack()
    {
        if (GameManager.Instance == null) return;

        switch (GameManager.Instance.EquippedWeapon)
        {
            case WeaponType.Net:
                SwingNet();
                break;
            case WeaponType.TranquilizerGun:
                FireTranquilizerGun();
                break;
        }
    }

    private void SwingNet()
    {
        if (netCooldownTimer > 0f) return;
        netCooldownTimer = netCooldown;

        Vector2 origin = transform.position;
        Vector2 aimDirection = playerMovement.LookDirection;

        foreach (MonsterHealth monster in FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            if (monster.IsDead) continue;

            Vector2 toMonster = monster.GamePosition - origin;
            if (toMonster.magnitude > netRange) continue;
            if (Vector2.Angle(aimDirection, toMonster) > netAngle * 0.5f) continue;

            monster.TakeDamage(netDamage);
            monster.Stun(netStunDuration);
        }
    }

    private void FireTranquilizerGun()
    {
        if (dartPrefab == null) return;

        TranquilizerDart dart = Instantiate(dartPrefab, transform.position, Quaternion.identity);
        dart.Launch(playerMovement.LookDirection, dartSpeed, sleepDelay, sleepDuration);
    }
}
