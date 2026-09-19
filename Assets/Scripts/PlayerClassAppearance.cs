using UnityEngine;

/// <summary>플레이어 몸통 스프라이트를 현재 장착한 클래스(와, 거너인 경우 무기 장착 여부)에 맞춰
/// 자동으로 교체한다. ClassSelectScene에서 클래스를 바꾸는 것과 게임 시작 시 무료 클래스를 고르는
/// 것, 셸터에서 클래스를 사거나 갈아입는 것 모두 GameManager.CurrentClass를 바꾸는 것으로
/// 귀결되므로, 여기서는 그 값과 IsWeaponSelected(Q로 무기 슬롯을 선택했는지, 숫자키로 해제했는지)만
/// 매 프레임 확인하면 된다. 트래퍼 스프라이트(player_trapper.png)는 원본 이미지 자체가 다른
/// 클래스보다 커서(72x72 vs 64x64), Transform Scale은 그대로 두고 픽셀당 유닛 비율(Pixels Per
/// Unit)도 다른 스프라이트와 동일하게 맞춰뒀다 - 그 결과 트래퍼가 자연스럽게 더 커 보인다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerClassAppearance : MonoBehaviour
{
    [SerializeField] private Sprite scoutSprite;
    [SerializeField] private Sprite trapperSprite;
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

        Sprite target = currentClass switch
        {
            PlayerClass.Trapper => trapperSprite,
            PlayerClass.Gunner => weaponSelected ? gunnerArmedSprite : gunnerSprite,
            _ => scoutSprite
        };

        if (applied && target == lastAppliedSprite) return;

        bodySprite.sprite = target;
        lastAppliedSprite = target;
        applied = true;
    }
}
