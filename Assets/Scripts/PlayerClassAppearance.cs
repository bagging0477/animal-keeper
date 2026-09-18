using UnityEngine;

/// <summary>플레이어 몸통 스프라이트를 현재 장착한 클래스(와, 거너인 경우 무기 장착 여부)에 맞춰
/// 자동으로 교체한다. 트래퍼는 아직 전용 스프라이트가 없어서 스카우트와 같은 스프라이트를 그대로
/// 쓴다. ClassSelectScene에서 클래스를 바꾸는 것과 게임 시작 시 무료 클래스를 고르는 것 모두
/// GameManager.CurrentClass를 바꾸는 것으로 귀결되므로, 여기서는 그 값과 IsWeaponSelected(Q로 무기
/// 슬롯을 선택했는지, 숫자키로 해제했는지)만 매 프레임 확인하면 된다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerClassAppearance : MonoBehaviour
{
    [SerializeField] private Sprite scoutSprite;
    [SerializeField] private Sprite gunnerSprite;
    [SerializeField] private Sprite gunnerArmedSprite;

    private SpriteRenderer bodySprite;
    private Sprite lastAppliedSprite;
    private bool applied;

    private void Awake()
    {
        bodySprite = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        PlayerClass currentClass = GameManager.Instance != null ? GameManager.Instance.CurrentClass : PlayerClass.Scout;
        bool weaponSelected = GameManager.Instance != null && GameManager.Instance.IsWeaponSelected;

        Sprite target = currentClass == PlayerClass.Gunner
            ? (weaponSelected ? gunnerArmedSprite : gunnerSprite)
            : scoutSprite;

        if (applied && target == lastAppliedSprite) return;

        bodySprite.sprite = target;
        lastAppliedSprite = target;
        applied = true;
    }
}
