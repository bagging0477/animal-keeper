using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMovement))]
public class PlayerWeaponController : MonoBehaviour
{
    [Header("밸런스 설정")]
    [SerializeField] private GameBalanceConfig config;

    [Header("마취총 (원거리)")]
    [SerializeField] private TranquilizerDart dartPrefab;
    [SerializeField] private float dartSpeed = 12f;
    [SerializeField] private Transform muzzlePoint;

    private PlayerMovement playerMovement;
    private float netCooldownTimer;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (netCooldownTimer > 0f) netCooldownTimer -= Time.deltaTime;

        // Q로 무기 슬롯을 선택했을 때만 클릭이 공격으로 이어진다 - 숫자 슬롯(아이템)이 선택된
        // 동안에는 무기가 "장착 해제"된 것으로 취급해 클릭이 PlayerGadgetController 쪽으로만 간다.
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
            GameManager.Instance != null && GameManager.Instance.IsWeaponSelected)
        {
            Attack();
        }
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
        netCooldownTimer = config != null ? config.netCooldown : 0.75f;
        AudioManager.Instance?.PlayNetSwing();

        float netRange = config != null ? config.netRange : 2f;
        float netAngle = config != null ? config.netAngle : 90f;
        float netDamage = config != null ? config.netDamage : 1f;
        float netStunDuration = config != null ? config.stunDuration : 0.4f;

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

        if (GameManager.Instance == null || !GameManager.Instance.TryConsumeTranquilizerAmmo())
        {
            Debug.Log("탄약이 없습니다");
            return;
        }

        float sleepDelay = config != null ? config.sleepDelay : 1f;
        float sleepDuration = config != null ? config.sleepDuration : 3.5f;
        float range = config != null ? config.tranquilizerRange : 36f;

        // 총구(MuzzlePoint)는 플레이어 회전을 따라가는 자식 트랜스폼이라, 그 월드 위치가 곧
        // "현재 총구가 향한 방향의 총구 끝"이다 - 미할당 시에만 플레이어 중심으로 대체한다.
        Vector3 spawnPosition = muzzlePoint != null ? muzzlePoint.position : transform.position;
        TranquilizerDart dart = Instantiate(dartPrefab, spawnPosition, Quaternion.identity);
        dart.Launch(playerMovement.LookDirection, dartSpeed, range, sleepDelay, sleepDuration);
        AudioManager.Instance?.PlayTranquilizerShot();
    }
}
