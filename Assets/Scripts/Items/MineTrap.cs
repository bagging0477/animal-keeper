using System.Collections;
using UnityEngine;

/// <summary>플레이어가 인벤토리에서 선택 후 클릭으로 설치하는 1회용 지뢰. 몬스터가 감지 반경 안에 들어오면 데미지를 주고
/// 짧게 스턴시킨 뒤 붉은 섬광을 띄우고 사라진다. TranquilizerDart와 마찬가지로 물리 트리거 대신
/// MonsterHealth.GamePosition과의 거리 폴링으로 감지한다 - 몬스터는 NavMesh(x,z 평면)로 움직이고
/// 2D 시각화는 매 프레임 별도로 동기화되기 때문에, 2D 콜라이더보다 이 방식이 이 프로젝트의
/// 기존 무기 코드(TranquilizerDart, PlayerWeaponController)와 일관된다.
/// 섬광 연출은 MineFlashUnity 패키지의 MineFlash2D와 같은 방식(한 장의 스프라이트를 키우며 흐리게)이다.</summary>
public class MineTrap : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;

    [Tooltip("발동 시 지뢰 자리에 띄울 섬광 스프라이트 (red-flash-32). 비워두면 섬광 없이 바로 사라진다.")]
    [SerializeField] private Sprite flashSprite;
    [Tooltip("섬광이 커지며 흐려지는 시간(초).")]
    [SerializeField, Min(0.05f)] private float flashDuration = 0.25f;
    [Tooltip("지뢰 오브젝트 크기에 대한 섬광 최종 배율. 시작 크기는 이 값의 65%.")]
    [SerializeField, Min(0.1f)] private float flashSize = 1.8f;
    [Tooltip("섬광이 지뢰보다 얼마나 위에 그려질지 (같은 Sorting Layer 기준 Order 차이).")]
    [SerializeField] private int flashSortingOrderOffset = 10;

    private bool triggered;

    private void Update()
    {
        if (triggered) return;

        float triggerRadius = config != null ? config.mineTriggerRadius : 0.4f;
        Vector2 position = transform.position;

        for (int i = MonsterHealth.Active.Count - 1; i >= 0; i--)
        {
            MonsterHealth monster = MonsterHealth.Active[i];
            if (monster.IsDead) continue;
            if (Vector2.Distance(position, monster.GamePosition) > triggerRadius) continue;

            int damage = config != null ? config.mineDamage : 2;
            float stunDuration = config != null ? config.mineStunDuration : 1.5f;
            monster.TakeDamage(damage);
            monster.Stun(stunDuration);

            Debug.Log($"{monster.name}가 지뢰를 밟음! 데미지: {damage}, 스턴: {stunDuration}초");

            Detonate();
            return;
        }
    }

    /// <summary>Play 중 컴포넌트 메뉴에서 몬스터 없이 섬광만 확인할 때 쓴다.</summary>
    [ContextMenu("Test Red Flash")]
    private void Detonate()
    {
        if (!Application.isPlaying || triggered) return;
        triggered = true;

        if (flashSprite == null)
        {
            Destroy(gameObject);
            return;
        }

        StartCoroutine(ShowFlash());
    }

    private IEnumerator ShowFlash()
    {
        SpriteRenderer mineRenderer = GetComponent<SpriteRenderer>();

        GameObject effect = new GameObject("RedFlash");
        effect.transform.SetParent(transform, false);
        SpriteRenderer flashRenderer = effect.AddComponent<SpriteRenderer>();
        flashRenderer.sprite = flashSprite;
        if (mineRenderer != null)
        {
            flashRenderer.sharedMaterial = mineRenderer.sharedMaterial;
            flashRenderer.sortingLayerID = mineRenderer.sortingLayerID;
            flashRenderer.sortingOrder = mineRenderer.sortingOrder + flashSortingOrderOffset;
            mineRenderer.enabled = false;
        }

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            float progress = elapsed / flashDuration;
            effect.transform.localScale = Vector3.one * Mathf.Lerp(flashSize * 0.65f, flashSize, progress);
            flashRenderer.color = new Color(1f, 1f, 1f, 1f - progress);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }
}
