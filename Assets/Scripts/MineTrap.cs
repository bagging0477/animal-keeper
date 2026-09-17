using UnityEngine;

/// <summary>플레이어가 인벤토리에서 선택 후 클릭으로 설치하는 1회용 지뢰. 몬스터가 감지 반경 안에 들어오면 데미지를 주고
/// 짧게 스턴시킨 뒤 스스로 사라진다. TranquilizerDart와 마찬가지로 물리 트리거 대신
/// MonsterHealth.GamePosition과의 거리 폴링으로 감지한다 - 몬스터는 NavMesh(x,z 평면)로 움직이고
/// 2D 시각화는 매 프레임 별도로 동기화되기 때문에, 2D 콜라이더보다 이 방식이 이 프로젝트의
/// 기존 무기 코드(TranquilizerDart, PlayerWeaponController)와 일관된다.</summary>
public class MineTrap : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;

    private void Update()
    {
        float triggerRadius = config != null ? config.mineTriggerRadius : 0.4f;
        Vector2 position = transform.position;

        foreach (MonsterHealth monster in FindObjectsByType<MonsterHealth>(FindObjectsInactive.Exclude))
        {
            if (monster.IsDead) continue;
            if (Vector2.Distance(position, monster.GamePosition) > triggerRadius) continue;

            int damage = config != null ? config.mineDamage : 2;
            float stunDuration = config != null ? config.mineStunDuration : 1.5f;
            monster.TakeDamage(damage);
            monster.Stun(stunDuration);

            Debug.Log($"{monster.name}가 지뢰를 밟음! 데미지: {damage}, 스턴: {stunDuration}초");

            Destroy(gameObject);
            return;
        }
    }
}
