using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// TruckScene 조명을 마을 시간에 맞춰 점점 어둡게 한다 - 마을의 해질녘(PlayerVision·VillageNightTint)과 같은 진행도
/// (GameBalanceConfig.GetSteppedDuskProgress(GameManager.VillageTime), 0초 → duskDurationSeconds)를 쓴다.
/// - 전체 조명(RVGlobalLight): 씬에 저장된 세기·색(낮)에서 truckNightGlobalIntensity·truckNightGlobalColor(밤)로.
/// - 천장 등(CeilingLight): 씬에 저장된 세기에서 truckNightCeilingIntensity로(밤에는 실내등이 더 밝아 보이게 올린다, 색은 그대로).
/// - 상점·소파 메뉴 등 UI는 화면 오버레이 캔버스라 조명과 무관하게 그대로 보인다.
/// 마을 시간은 오늘 마을에 한 번 들어간 뒤로 트럭에서도 흐르므로(GameManager.Update) 트럭에 있는 동안에도 점점 어두워진다.
/// GameManager가 Awake에서 자기 오브젝트에 붙인다(씬 파일은 바꾸지 않는다). 라이트는 이름으로 찾고, 없으면 아무것도 하지 않는다.
/// </summary>
public class TruckNightLighting : MonoBehaviour
{
    private const string TruckSceneName = "TruckScene";
    public const string GlobalLightName = "RVGlobalLight";
    public const string CeilingLightName = "CeilingLight";

    private Light2D globalLight, ceilingLight;
    private float dayGlobalIntensity, dayCeilingIntensity;
    private Color dayGlobalColor;
    private float appliedProgress = -1f;

    /// <summary>마지막으로 적용한 진행도(0 낮 ~ 1 밤). 트럭 씬이 아니거나 아직 적용 전이면 -1. 테스트가 읽는다.</summary>
    public float AppliedProgress => appliedProgress;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Bind(SceneManager.GetActiveScene());
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Bind(scene);

    private void Bind(Scene scene)
    {
        globalLight = ceilingLight = null;
        appliedProgress = -1f;
        if (scene.name != TruckSceneName) return;
        foreach (Light2D l in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (l.gameObject.scene != scene) continue;
            if (l.name == GlobalLightName) globalLight = l;
            else if (l.name == CeilingLightName) ceilingLight = l;
        }
        if (globalLight != null) { dayGlobalIntensity = globalLight.intensity; dayGlobalColor = globalLight.color; }
        if (ceilingLight != null) dayCeilingIntensity = ceilingLight.intensity;
        Apply(); // 첫 프레임부터 지금 시간의 밝기로
    }

    private void LateUpdate()
    {
        if (globalLight == null && ceilingLight == null) return;
        Apply();
    }

    /// <summary>진행도 p(0~1)에서의 (전체 조명 세기, 전체 조명 색, 천장 등 세기). 테스트·캡처가 기대값을 계산할 때도 쓴다.</summary>
    public static (float globalIntensity, Color globalColor, float ceilingIntensity) Evaluate(GameBalanceConfig c, float p,
        float dayGlobal, Color dayColor, float dayCeiling)
    {
        if (c == null || !c.truckNightLightingEnabled) return (dayGlobal, dayColor, dayCeiling);
        p = Mathf.Clamp01(p);
        Color col = Color.Lerp(dayColor, c.truckNightGlobalColor, p);
        col.a = 1f;
        return (Mathf.Lerp(dayGlobal, c.truckNightGlobalIntensity, p), col, Mathf.Lerp(dayCeiling, c.truckNightCeilingIntensity, p));
    }

    private void Apply()
    {
        GameManager gm = GameManager.Instance;
        GameBalanceConfig c = gm != null ? gm.Config : null;
        float p = c != null ? c.GetSteppedDuskProgress(gm.VillageTime) : 0f;
        if (Mathf.Approximately(p, appliedProgress)) return;
        appliedProgress = p;
        (float gi, Color gc, float ci) = Evaluate(c, p, dayGlobalIntensity, dayGlobalColor, dayCeilingIntensity);
        if (globalLight != null) { globalLight.intensity = gi; globalLight.color = gc; }
        if (ceilingLight != null) ceilingLight.intensity = ci;
    }
}
