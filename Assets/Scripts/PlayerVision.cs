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

    // Light2D의 반경/각도/그림자 소프트니스 같은 "모양"에 영향을 주는 속성은 대입할 때마다 값이
    // 바뀌었는지와 무관하게 URP 2D 렌더러가 그 라이트의 메시/그림자를 다시 계산한다. 이전에는
    // LateUpdate마다 매 프레임 무조건 재대입해서(GameBalanceConfig를 플레이 중에도 바로 반영하려는
    // 의도였다) 값이 그대로인 대부분의 프레임에도 계속 다시 계산이 일어났고, 방마다 여러 개씩 있는
    // ShadowCaster2D와 맞물려 전체적인 프레임 저하로 이어져 몬스터 이동까지 버벅이는 것처럼 보이게
    // 했다. 마지막으로 적용한 값을 기억해두고 실제로 바뀐 경우에만 다시 대입한다 - 플레이 중
    // Inspector에서 값을 조정하면 여전히 즉시 반영되지만, 값이 그대로인 프레임에는 아무 비용도 들지 않는다.
    private bool visionRangesApplied;
    private PlayerClass lastClass;
    private float lastAmbientRadius;
    private bool lastAmbientIgnoresShadows;
    private float lastFocusedRadius;
    private float lastFocusedAngle;
    private float lastFocusedShadowSoftness;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void ApplyVisionRanges()
    {
        if (config == null) return;

        // 시야 범위는 클래스마다 다르다(아처는 더 넓게) - GameBalanceConfig의 클래스별 배율을 거친
        // 값을 기준치로 쓴다. VillageScene 진입 전 ClassSelectScene에서 클래스가 정해지므로 보통
        // 한 세션 동안 안 바뀌지만, lastClass도 함께 캐싱해서 클래스가 바뀌는 경우에도 즉시 반영한다.
        PlayerClass currentClass = GameManager.Instance != null ? GameManager.Instance.CurrentClass : PlayerClass.Scout;
        float targetAmbientRadius = config.GetClassAmbientVisionRadius(currentClass);
        float targetFocusedRadius = config.GetClassFocusedVisionRadius(currentClass);
        float targetFocusedAngle = config.GetClassFocusedVisionAngle(currentClass);

        if (ambientVisionLight != null &&
            (!visionRangesApplied || lastClass != currentClass || lastAmbientRadius != targetAmbientRadius ||
             lastAmbientIgnoresShadows != config.ambientVisionIgnoresShadows))
        {
            ambientVisionLight.pointLightOuterRadius = targetAmbientRadius;
            // 바로 옆인데 벽 때문에 기본 시야가 뚝 끊겨 보이는 이질감을 없애기 위해, 기본 원형 시야는
            // 기본적으로 그림자(벽/장애물 가림)를 무시하고 범위 안을 항상 전부 보여준다.
            ambientVisionLight.shadowsEnabled = !config.ambientVisionIgnoresShadows;
            lastAmbientRadius = targetAmbientRadius;
            lastAmbientIgnoresShadows = config.ambientVisionIgnoresShadows;
        }

        if (focusedVisionLight != null &&
            (!visionRangesApplied || lastClass != currentClass || lastFocusedRadius != targetFocusedRadius ||
             lastFocusedAngle != targetFocusedAngle || lastFocusedShadowSoftness != config.focusedVisionShadowSoftness))
        {
            focusedVisionLight.pointLightOuterRadius = targetFocusedRadius;
            focusedVisionLight.pointLightOuterAngle = targetFocusedAngle;
            focusedVisionLight.shadowSoftness = config.focusedVisionShadowSoftness;
            lastFocusedRadius = targetFocusedRadius;
            lastFocusedAngle = targetFocusedAngle;
            lastFocusedShadowSoftness = config.focusedVisionShadowSoftness;
        }

        lastClass = currentClass;
        visionRangesApplied = true;
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
