using UnityEngine;

/// <summary>플레이어 몸통 스프라이트를 현재 장착한 클래스(와, 거너인 경우 무기 장착 여부)에 맞춰
/// 자동으로 교체한다. ClassSelectScene에서 클래스를 바꾸는 것과 게임 시작 시 무료 클래스를 고르는
/// 것, 셸터에서 클래스를 사거나 갈아입는 것 모두 GameManager.CurrentClass를 바꾸는 것으로
/// 귀결되므로, 여기서는 그 값과 IsWeaponSelected(Q로 무기 슬롯을 선택했는지, 숫자키로 해제했는지)만
/// 매 프레임 확인하면 된다. 네 스프라이트 모두 PPU(56)는 동일하지만 원본 이미지 픽셀 크기가 서로
/// 달라서, 스프라이트를 바꿀 때 Transform.localScale(균일 배율, X=Y)도 클래스별로 함께 맞춘다.
/// 한 번은 거너 무장/비무장의 가로세로를 각각 따로 늘려서 바운딩 박스 자체를 완전히 맞춰본 적이
/// 있는데, 무장 스프라이트는 총이 옆으로 튀어나와 원본 가로세로 비율이 비무장과 달라서(비무장은
/// 거의 정사각형, 무장은 더 넓고 낮음) X/Y를 다르게 늘리면 이미지가 눌리고 늘어나 비율이 깨지는
/// 문제가 있었다 - 그래서 지금은 항상 균일 배율만 쓰고, "키(세로)"를 기준으로 스카우트/트래퍼/거너
/// 두 상태가 같은 높이가 되도록만 맞춘다. 그 결과 무기를 든 만큼 가로 폭은 자연스럽게 좀 더
/// 넓어지지만(총이 튀어나온 만큼), 이미지 자체가 눌리거나 늘어나지는 않는다.
/// 처음에는 "높이"를 PNG 캔버스 전체 높이로 계산했는데, 네 이미지가 투명 여백을 서로 다르게 갖고
/// 있어서(예: 스카우트는 106x105 캔버스에 실제 캐릭터가 64x62만 차지, 거너 무장은 83x73 캔버스에
/// 64x48만 차지) 캔버스 기준으로 맞추면 실제 그려진 캐릭터 크기는 오히려 서로 달라 보이는 문제가
/// 있었다. 그래서 지금은 캔버스가 아니라 실제 불투명 픽셀(캐릭터 몸통)의 높이를 기준으로 배율을
/// 다시 계산했다 - 스카우트/거너(비무장)/트래퍼는 몸통이 전부 64x62px로 동일하고, 거너(무장)만
/// 64x48px로 더 짧길래 그만큼 배율을 키워서 보정했다. 트래퍼는 그 기준 높이보다 약 12.5% 더
/// 크도록 잡아뒀다. 배율 값은 인스펙터에서 자유롭게 다시 조정할 수 있다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerClassAppearance : MonoBehaviour
{
    [SerializeField] private Sprite scoutSprite;
    [SerializeField] private Sprite trapperSprite;
    [SerializeField] private Sprite gunnerSprite;
    [SerializeField] private Sprite gunnerArmedSprite;

    [Header("클래스별 체감 크기 보정 배율 (원본 이미지 픽셀 크기가 서로 달라서 필요함 - 원본 비율이 깨지지 않도록 항상 균일 배율로 Transform.localScale에 적용됨. 캔버스 전체가 아니라 실제 불투명 픽셀(캐릭터 몸통) 높이 기준으로 맞춰서, 이미지마다 여백이 달라도 체감 크기가 같아지도록 계산한 값)")]
    [SerializeField] private float scoutScale = 0.64f;
    [SerializeField] private float trapperScale = 0.72f;
    [SerializeField] private float gunnerScale = 0.64f;
    [SerializeField] private float gunnerArmedScale = 0.826f;

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

        Sprite target;
        float targetScale;
        switch (currentClass)
        {
            case PlayerClass.Trapper:
                target = trapperSprite;
                targetScale = trapperScale;
                break;
            case PlayerClass.Gunner:
                target = weaponSelected ? gunnerArmedSprite : gunnerSprite;
                targetScale = weaponSelected ? gunnerArmedScale : gunnerScale;
                break;
            default:
                target = scoutSprite;
                targetScale = scoutScale;
                break;
        }

        if (applied && target == lastAppliedSprite) return;

        bodySprite.sprite = target;
        transform.localScale = Vector3.one * targetScale;
        lastAppliedSprite = target;
        applied = true;
    }
}
