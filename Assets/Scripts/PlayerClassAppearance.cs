using UnityEngine;

/// <summary>플레이어 몸통 스프라이트를 현재 장착한 클래스에 맞춰 자동으로 교체한다. 트래퍼는 아직
/// 전용 스프라이트가 없어서 스카우트와 같은 스프라이트를 그대로 쓴다. ClassSelectScene에서 클래스를
/// 바꾸는 것과 게임 시작 시 무료 클래스를 고르는 것 모두 GameManager.CurrentClass를 바꾸는 것으로
/// 귀결되므로, 여기서는 그 값만 매 프레임 확인하면 된다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerClassAppearance : MonoBehaviour
{
    [SerializeField] private Sprite scoutSprite;
    [SerializeField] private Sprite gunnerSprite;

    private SpriteRenderer bodySprite;
    private PlayerClass lastAppliedClass;
    private bool applied;

    private void Awake()
    {
        bodySprite = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        PlayerClass currentClass = GameManager.Instance != null ? GameManager.Instance.CurrentClass : PlayerClass.Scout;
        if (applied && currentClass == lastAppliedClass) return;

        bodySprite.sprite = currentClass == PlayerClass.Gunner ? gunnerSprite : scoutSprite;
        lastAppliedClass = currentClass;
        applied = true;
    }
}
