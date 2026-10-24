using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장애물(방 프리팹의 Obstacles 아래 그림 - 나무·덤불·바위·묘비 등)이 플레이어·동물·몬스터를 가리면 그 장애물을 반투명하게 한다.
/// 판정: 장애물이 캐릭터보다 앞에 그려지고(발 기준 정렬 Order가 더 큼), 두 그림 영역(bounds)이 겹친 넓이가 캐릭터 그림의
/// occluderMinCover 이상일 때 "가린다"로 본다. 가리는 동안 occluderFadeAlpha까지 흐려지고, 벗어나면 원래대로 돌아온다.
/// 캐릭터는 플레이어와, 플레이어에게서 occluderTargetRadius 안에 있는 움직이는 FootSortOrder(동물·몬스터)다.
/// VillageMapGenerator가 맵을 만든 뒤 맵 루트에 붙인다. 수치는 GameBalanceConfig(장애물 가림 반투명)에서 바꾼다.
/// </summary>
[DisallowMultipleComponent]
public class ObstacleOccluderFade : MonoBehaviour
{
    private const string ObstacleGroupName = "Obstacles";
    private const float TargetRefreshSeconds = 0.25f;

    private class Prop
    {
        public SpriteRenderer Renderer;
        public float BaseAlpha;
        public float Alpha = 1f; // BaseAlpha에 곱하는 값
    }

    private readonly List<Prop> props = new List<Prop>();
    private readonly List<SpriteRenderer> targets = new List<SpriteRenderer>();
    private GameBalanceConfig config;
    private SpriteRenderer playerBody;
    private float refreshTimer;

    private float FadeAlpha => config != null ? config.occluderFadeAlpha : 0.45f;
    private float FadeSpeed => config != null ? config.occluderFadeSpeed : 6f;
    private float MinCover => config != null ? config.occluderMinCover : 0.2f;
    private float TargetRadius => config != null ? config.occluderTargetRadius : 10f;

    /// <summary>지금 흐려져 있는(완전히 돌아오지 않은) 장애물 수. 테스트용.</summary>
    public int FadedCount { get; private set; }

    public static ObstacleOccluderFade Attach(GameObject mapRoot, SpriteRenderer playerBody, GameBalanceConfig config)
    {
        ObstacleOccluderFade f = mapRoot.GetComponent<ObstacleOccluderFade>();
        if (f == null) f = mapRoot.AddComponent<ObstacleOccluderFade>();
        f.config = config;
        f.playerBody = playerBody;
        f.Collect();
        return f;
    }

    private void Collect()
    {
        props.Clear();
        foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.sprite == null || r.transform.parent == null || r.transform.parent.name != ObstacleGroupName) continue;
            props.Add(new Prop { Renderer = r, BaseAlpha = r.color.a });
        }
    }

    private void RefreshTargets()
    {
        targets.Clear();
        if (playerBody != null) targets.Add(playerBody);
        Vector2 center = playerBody != null ? (Vector2)playerBody.bounds.center : Vector2.zero;
        float r2 = TargetRadius * TargetRadius;
        foreach (FootSortOrder f in FindObjectsByType<FootSortOrder>(FindObjectsInactive.Exclude))
        {
            if (!f.enabled || f.Body == null || f.Body == playerBody || !f.Body.enabled || !f.Body.gameObject.activeInHierarchy) continue;
            if (playerBody != null && ((Vector2)f.Body.bounds.center - center).sqrMagnitude > r2) continue;
            targets.Add(f.Body);
        }
    }

    private void LateUpdate()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = TargetRefreshSeconds;
            RefreshTargets();
        }
        Tick(Mathf.Max(0.01f, FadeSpeed) * Time.unscaledDeltaTime);
    }

    /// <summary>캐릭터 목록을 새로 읽고 흐림을 바로 목표값으로 맞춘다(캡처·테스트용).</summary>
    public void SnapNow()
    {
        RefreshTargets();
        Tick(1f);
    }

    private void Tick(float step)
    {
        float fadeAlpha = Mathf.Clamp01(FadeAlpha);
        float minCover = Mathf.Clamp01(MinCover);
        int faded = 0;
        foreach (Prop p in props)
        {
            SpriteRenderer pr = p.Renderer;
            if (pr == null) continue;
            bool covers = Covers(pr, minCover);
            float goal = covers ? fadeAlpha : 1f;
            if (Mathf.Approximately(p.Alpha, goal)) { if (p.Alpha < 1f) faded++; continue; }
            p.Alpha = Mathf.MoveTowards(p.Alpha, goal, step);
            Color c = pr.color;
            c.a = p.BaseAlpha * p.Alpha;
            pr.color = c;
            if (p.Alpha < 1f) faded++;
        }
        FadedCount = faded;
    }

    private bool Covers(SpriteRenderer prop, float minCover)
    {
        if (!prop.enabled || !prop.gameObject.activeInHierarchy) return false;
        Bounds pb = prop.bounds;
        foreach (SpriteRenderer t in targets)
        {
            if (t == null || prop.sortingLayerID != t.sortingLayerID || prop.sortingOrder <= t.sortingOrder) continue;
            Bounds tb = t.bounds;
            float w = Mathf.Min(pb.max.x, tb.max.x) - Mathf.Max(pb.min.x, tb.min.x);
            if (w <= 0f) continue;
            float h = Mathf.Min(pb.max.y, tb.max.y) - Mathf.Max(pb.min.y, tb.min.y);
            if (h <= 0f) continue;
            float area = tb.size.x * tb.size.y;
            if (area > 0f && w * h / area >= minCover) return true;
        }
        return false;
    }
}
