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
    private float lastLightIntensity;
    private float lastLightFalloff;
    private float lastFocusedEdgeFadeAngle;
    private float lastFocusedInnerRadiusRatio;

    /// <summary>지금 마을에서 켜져 있는 플레이어 시야. 몬스터 등이 매 프레임 Find 없이 시야를 물을 때 쓴다(없으면 null).</summary>
    public static PlayerVision Current { get; private set; }

    // 도메인 리로드를 끈 에디터에서 이전 Play의 파괴된 시야가 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCurrent() => Current = null;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void OnEnable() => Current = this;

    private void OnDisable()
    {
        if (Current == this) Current = null;
    }

    /// <summary>월드 위치(x, y)가 지금 플레이어 시야 안인지. 플레이어나 시야가 없으면 false.</summary>
    public static bool IsVisibleToPlayer(Vector2 point) => Current != null && Current.IsPointVisible(point);

    /// <summary>월드 위치(x, y)가 지금 이 시야 안인지 - 화면에 그려지는 두 라이트의 실제 값(반경, 부채꼴 각도와 방향, 켜짐, 그림자)을 그대로 읽는다.
    /// 그래서 클래스 배율, 플래시라이트 배율(없으면 0.7배), 마우스 방향이 화면과 똑같이 반영된다.
    /// - 원형 시야: 라이트 중심에서 반경 안
    /// - 부채꼴 시야: 반경 안이고, 라이트가 향한 방향(transform.up)에서 바깥 각도의 절반 안
    /// - 벽: 그 라이트가 그림자를 쓰면(shadowsEnabled) 라이트 중심과 위치 사이를 고정 충돌체가 막을 때 보이지 않는 것으로 본다(StaticBlockerLine,
    ///   몬스터 시야와 같은 규칙). 원형 시야는 기본 설정(ambientVisionIgnoresShadows)에서 그림자를 끄므로, 화면처럼 벽 너머도 반경 안이면 보인다.
    /// 가장자리의 부드러운 감쇠(안쪽 반경·안쪽 각도 밖의 흐려지는 띠)는 "보임"으로 친다. 맵 바깥 마스크로 가려지는 맵 밖은 따지지 않는다.
    /// 부채꼴 방향은 LateUpdate에서 갱신되므로 Update에서 부르면 직전 프레임에 그려진 방향과 같다.</summary>
    public bool IsPointVisible(Vector2 point) =>
        InsideLight(ambientVisionLight, point) || InsideLight(focusedVisionLight, point);

    /// <summary>월드 위치(x, y)가 지금 정면 부채꼴(손전등) 시야 안인지 - 원형 주변 시야는 보지 않는다. 플레이어나 시야가 없으면 false.
    /// 판정 기준(반경, 각도, 방향, 켜짐, 벽)은 IsPointVisible의 부채꼴 쪽과 같다. 박쥐 굳음이 쓴다.</summary>
    public static bool IsInFocusedVision(Vector2 point) => Current != null && Current.IsPointInFocusedVision(point);

    /// <summary>월드 위치(x, y)가 지금 이 시야의 정면 부채꼴 안인지(원형 시야 제외).</summary>
    public bool IsPointInFocusedVision(Vector2 point) => InsideLight(focusedVisionLight, point);

    private static bool InsideLight(Light2D light, Vector2 point)
    {
        if (light == null || !light.isActiveAndEnabled) return false;
        Vector2 center = light.transform.position;
        Vector2 toPoint = point - center;
        float radius = light.pointLightOuterRadius;
        if (toPoint.sqrMagnitude > radius * radius) return false;
        if (light.pointLightOuterAngle < 360f && toPoint.sqrMagnitude > 0f &&
            Vector2.Angle(light.transform.up, toPoint) > light.pointLightOuterAngle * 0.5f) return false;
        if (light.shadowsEnabled && StaticBlockerLine.IsBlocked(center, point, ~0)) return false;
        return true;
    }

    private void ApplyVisionRanges()
    {
        if (config == null) return;

        // 시야 범위는 클래스마다 다르다(거너는 더 넓게) - GameBalanceConfig의 클래스별 배율을 거친
        // 값을 기준치로 쓴다. VillageScene 진입 전 ClassSelectScene에서 클래스가 정해지므로 보통
        // 한 세션 동안 안 바뀌지만, lastClass도 함께 캐싱해서 클래스가 바뀌는 경우에도 즉시 반영한다.
        PlayerClass currentClass = GameManager.Instance != null ? GameManager.Instance.CurrentClass : PlayerClass.Scout;
        float targetAmbientRadius = config.GetClassAmbientVisionRadius(currentClass);

        // 플래시라이트 패시브가 없으면 부채꼴만 config 배율(기본 0.7)로 줄인다: 최종 = 기본 x 클래스 x 플래시라이트 배율.
        // 산 뒤에는 배율 1이라 예전 크기 그대로다. 원형 시야는 이 배율과 무관하다.
        bool hasFlashlight = PassiveEffects.HasFlashlight;
        float flashlightRadiusScale = hasFlashlight ? 1f : config.noFlashlightFocusedRadiusMultiplier;
        float flashlightAngleScale = hasFlashlight ? 1f : config.noFlashlightFocusedAngleMultiplier;
        float classFocusedAngle = config.GetClassFocusedVisionAngle(currentClass);
        float targetFocusedRadius = config.GetClassFocusedVisionRadius(currentClass) * flashlightRadiusScale;
        float targetFocusedAngle = classFocusedAngle * flashlightAngleScale;

        // 두 라이트의 밝기/감쇠 곡선은 항상 같게 맞춘다 - 다르면 겹치는 경계에서 한쪽이 먼저 어두워져 띠가 보인다.
        bool blendChanged = !visionRangesApplied || lastLightIntensity != config.visionLightIntensity ||
            lastLightFalloff != config.visionLightFalloffStrength;

        if (ambientVisionLight != null &&
            (blendChanged || lastClass != currentClass || lastAmbientRadius != targetAmbientRadius ||
             lastAmbientIgnoresShadows != config.ambientVisionIgnoresShadows))
        {
            ambientVisionLight.intensity = config.visionLightIntensity;
            ambientVisionLight.falloffIntensity = config.visionLightFalloffStrength;
            ambientVisionLight.pointLightOuterRadius = targetAmbientRadius;
            // 바로 옆인데 벽 때문에 기본 시야가 뚝 끊겨 보이는 이질감을 없애기 위해, 기본 원형 시야는
            // 기본적으로 그림자(벽/장애물 가림)를 무시하고 범위 안을 항상 전부 보여준다.
            ambientVisionLight.shadowsEnabled = !config.ambientVisionIgnoresShadows;
            lastAmbientRadius = targetAmbientRadius;
            lastAmbientIgnoresShadows = config.ambientVisionIgnoresShadows;
        }

        if (focusedVisionLight != null &&
            (blendChanged || lastClass != currentClass || lastFocusedRadius != targetFocusedRadius ||
             lastFocusedAngle != targetFocusedAngle || lastFocusedShadowSoftness != config.focusedVisionShadowSoftness ||
             lastFocusedEdgeFadeAngle != config.focusedVisionEdgeFadeAngle ||
             lastFocusedInnerRadiusRatio != config.focusedVisionInnerRadiusRatio))
        {
            focusedVisionLight.intensity = config.visionLightIntensity;
            focusedVisionLight.falloffIntensity = config.visionLightFalloffStrength;
            focusedVisionLight.pointLightOuterRadius = targetFocusedRadius;
            focusedVisionLight.pointLightInnerRadius = targetFocusedRadius * config.focusedVisionInnerRadiusRatio;
            focusedVisionLight.pointLightOuterAngle = targetFocusedAngle;
            // 부채꼴 옆 가장자리 페이드 폭. 각도 차이는 거리에 비례해 넓어지므로 플레이어 바로 옆에서는 페이드가 아주 좁다 -
            // 한쪽당 각도를 넉넉히 잡아야 원형 시야와 만나는 근거리 경계가 칼선처럼 보이지 않는다.
            // 플래시라이트 배율은 안쪽 각도에도 같은 비율로 곱해서, 줄어든 부채꼴도 가장자리 그라데이션 모양이 같다.
            focusedVisionLight.pointLightInnerAngle =
                Mathf.Max(0f, classFocusedAngle - 2f * config.focusedVisionEdgeFadeAngle) * flashlightAngleScale;
            focusedVisionLight.shadowSoftness = config.focusedVisionShadowSoftness;
            lastFocusedRadius = targetFocusedRadius;
            lastFocusedAngle = targetFocusedAngle;
            lastFocusedShadowSoftness = config.focusedVisionShadowSoftness;
            lastFocusedEdgeFadeAngle = config.focusedVisionEdgeFadeAngle;
            lastFocusedInnerRadiusRatio = config.focusedVisionInnerRadiusRatio;
        }

        // 플래시라이트 없을 때 배율을 0으로 두면 부채꼴을 아예 끈다.
        bool focusedOn = targetFocusedRadius > 0f && targetFocusedAngle > 0f;
        if (focusedVisionLight != null && focusedVisionLight.enabled != focusedOn) focusedVisionLight.enabled = focusedOn;

        lastLightIntensity = config.visionLightIntensity;
        lastLightFalloff = config.visionLightFalloffStrength;
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
