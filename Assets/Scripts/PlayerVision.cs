using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// VillageScene 전용 2단 시야 시스템. 플레이어를 중심으로 한 짧은 원형 기본 시야(Ambient)와
/// 바라보는 방향(마우스 방향)의 넓은 부채꼴 시야(Focused)를 Light2D 두 개로 구현한다.
/// 두 라이트가 겹쳐서 밝히는 범위 밖은 VillageScene의 Global Light 2D를 어둡게 낮춰둔 덕분에
/// 자연스럽게 어두워진다(별도의 검은 오버레이 스프라이트 불필요).
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerVision : MonoBehaviour
{
    [SerializeField] private GameBalanceConfig config;
    [SerializeField] private Light2D ambientVisionLight;
    [SerializeField] private Light2D focusedVisionLight;

    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void ApplyVisionRanges()
    {
        if (config == null) return;

        if (ambientVisionLight != null)
        {
            ambientVisionLight.pointLightOuterRadius = config.ambientVisionRadius;
            // 바로 옆인데 벽 때문에 기본 시야가 뚝 끊겨 보이는 이질감을 없애기 위해, 기본 원형 시야는
            // 기본적으로 그림자(벽/장애물 가림)를 무시하고 범위 안을 항상 전부 보여준다.
            ambientVisionLight.shadowsEnabled = !config.ambientVisionIgnoresShadows;
        }

        if (focusedVisionLight != null)
        {
            focusedVisionLight.pointLightOuterRadius = config.focusedVisionRadius;
            focusedVisionLight.pointLightOuterAngle = config.focusedVisionAngle;
            focusedVisionLight.shadowSoftness = config.focusedVisionShadowSoftness;
        }
    }

    private void LateUpdate()
    {
        // 매 프레임 다시 적용해서, 플레이 모드 중 GameBalanceConfig 값을 조정해도 즉시 반영되게 한다.
        ApplyVisionRanges();

        if (focusedVisionLight == null) return;

        // Point Light 2D의 부채꼴은 회전 0도일 때 로컬 +Y(위쪽)를 향하므로, atan2 기준
        // LookAngle(0도 = +X, 오른쪽)에 맞춰 돌리려면 90도를 빼줘야 한다.
        focusedVisionLight.transform.rotation = Quaternion.Euler(0f, 0f, playerMovement.LookAngle - 90f);
    }
}
