using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>상점에서 구매하는(현재는 디버그로 기본 지급되는) 1회용 소모품 - 지뢰/폭탄 - 의
/// 설치/사용을 담당한다. 무기(PlayerWeaponController)와 별개로, 클래스와 상관없이 누구나 쓸 수 있다.</summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerGadgetController : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private MineTrap minePrefab;

    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.qKey.wasPressedThisFrame) PlaceMine();
        if (kb.rKey.wasPressedThisFrame) UseBomb();
    }

    private void PlaceMine()
    {
        if (minePrefab == null) return;

        if (GameManager.Instance == null || !GameManager.Instance.TryConsumeMine())
        {
            Debug.Log("지뢰가 없습니다");
            return;
        }

        float distance = config != null ? config.minePlacementDistance : 1.2f;
        Vector3 position = transform.position + (Vector3)(playerMovement.LookDirection * distance);
        Instantiate(minePrefab, position, Quaternion.identity);
    }

    private void UseBomb()
    {
        if (GameManager.Instance == null || !GameManager.Instance.TryConsumeBomb())
        {
            Debug.Log("폭탄이 없습니다");
            return;
        }

        int damage = config != null ? config.bombDamage : 3;
        float stunDuration = config != null ? config.bombStunDuration : 2f;
        float radius = config != null ? config.bombRadius : 3f;
        Vector2 origin = transform.position;

        BombExplosionEffect.Spawn(transform.position, radius);

        foreach (MonsterHealth monster in FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            if (monster.IsDead) continue;
            if (Vector2.Distance(origin, monster.GamePosition) > radius) continue;

            monster.TakeDamage(damage);
            monster.Stun(stunDuration);

            Debug.Log($"{monster.name}가 폭탄 범위에 맞음! 데미지: {damage}, 스턴: {stunDuration}초");
        }
    }
}
