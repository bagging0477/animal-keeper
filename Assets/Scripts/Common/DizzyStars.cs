using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 코뿔소가 벽에 부딪혀 기절해 있는 동안 머리 바로 위를 빙빙 도는 별(밈). RhinoMonsterAI가 벽 충돌 때 Play를 부른다.
/// - 자리: 몸 그림 가운데 + 머리 윗끝(RhinoMonsterAI.dizzyHeadTopTexels, 기절 그림 recover-01에서 잰 귀 끝). 보는 방향(flipX)에 따라 매 프레임 좌우를 바꾼다.
/// - 그림: GameBalanceConfig.dizzyStarSprite(MemeAssetBuilder가 만드는 7x7 도트 별, 1도트 = 1/16칸, 외곽선 #2F2F2E, Point, 압축 끔).
/// - 별은 몸 그림(배율 3)의 자식이 아니라 따로 둔 오브젝트라 배율 1 그대로 1도트 = 1/16칸이다. 위치도 1/16칸 격자에 맞춘다.
/// - 시야: 별 묶음 가운데가 플레이어 시야 밖이면(PlayerVision.IsVisibleToPlayer - 몬스터 눈 발광과 같은 판정) 그리지 않고,
///   재질도 파티클과 같은 빛 받는 재질(hitParticleMaterial)이라 어둠 속에서는 보이지 않는다.
/// - 기절이 풀리거나(MonsterHealth.IsStunned 끝), 쓰러지거나 잠들면 바로 사라진다.
/// - 그림만 바꾼다. 충돌체·NavMesh·AI·난수(UnityEngine.Random)는 건드리지 않는다.
/// </summary>
public class DizzyStars : MonoBehaviour
{
    private const float Grid = 1f / 16f;

    /// <summary>지금 떠 있는 별 묶음(테스트·캡처용).</summary>
    public static readonly List<DizzyStars> Active = new List<DizzyStars>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active.Clear();

    private SpriteRenderer body;
    private MonsterHealth health;
    private readonly List<SpriteRenderer> stars = new List<SpriteRenderer>();
    private float age;
    private float radiusX, radiusY, turnsPerSecond;
    private Vector2 headTop;

    /// <summary>별이 지금 화면에 그려지는지(시야 안이고 기절 중).</summary>
    public bool IsShown { get; private set; }

    /// <summary>별 묶음 가운데(머리 위) 월드 위치.</summary>
    public Vector2 Center { get; private set; }

    public MonsterHealth Owner => health;

    /// <summary>body(코뿔소 몸 그림) 머리 위에 별을 띄운다. 이미 떠 있으면 처음부터 다시 돈다.</summary>
    /// headTop: 오른쪽을 볼 때 그림 가운데에서 머리 윗끝까지의 거리(월드 칸). 왼쪽을 보면(flipX) 가로를 뒤집어 머리를 따라간다.
    public static DizzyStars Play(SpriteRenderer body, MonsterHealth health, Vector2 headTop)
    {
        GameBalanceConfig c = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (body == null || health == null || c == null || c.dizzyStarSprite == null) return null;
        foreach (DizzyStars existing in Active)
        {
            if (existing != null && existing.health == health) { existing.age = 0f; existing.headTop = headTop; return existing; }
        }
        var go = new GameObject("DizzyStars");
        var d = go.AddComponent<DizzyStars>();
        d.headTop = headTop;
        d.Init(body, health, c);
        return d;
    }

    private void Init(SpriteRenderer body, MonsterHealth health, GameBalanceConfig c)
    {
        this.body = body;
        this.health = health;
        radiusX = c.dizzyStarOrbitRadius.x;
        radiusY = c.dizzyStarOrbitRadius.y;
        turnsPerSecond = c.dizzyStarTurnsPerSecond;
        int count = Mathf.Max(1, c.dizzyStarCount);
        for (int i = 0; i < count; i++)
        {
            var child = new GameObject("Star" + i);
            child.transform.SetParent(transform, false);
            var r = child.AddComponent<SpriteRenderer>();
            r.sprite = c.dizzyStarSprite;
            if (c.hitParticleMaterial != null) r.sharedMaterial = c.hitParticleMaterial;
            r.sortingLayerID = body.sortingLayerID;
            stars.Add(r);
        }
        Active.Add(this);
        LateUpdate();
    }

    private void OnDestroy() => Active.Remove(this);

    private void LateUpdate()
    {
        if (body == null || health == null || health.IsDead || health.IsAsleep || !health.IsStunned)
        {
            Destroy(gameObject);
            return;
        }
        age += Time.deltaTime;

        // 머리 바로 위: 그림 가운데(피벗) + 머리 윗끝(headTop, 보는 방향으로 가로 뒤집음). 궤도 아래쪽 별의 밑변이 머리 윗끝에 닿도록
        // 궤도 가운데를 (세로 반지름 + 별 반 높이)만큼 올린다. 그림은 오른쪽을 본다(flipX면 왼쪽).
        Bounds b = body.bounds;
        float facing = body.flipX ? -1f : 1f;
        float starHalf = stars.Count > 0 && stars[0].sprite != null ? stars[0].sprite.bounds.extents.y : 3.5f * Grid;
        Vector2 center = new Vector2(b.center.x + facing * headTop.x, b.center.y + headTop.y + radiusY + starHalf);
        center = Snap(center);
        Center = center;
        transform.position = new Vector3(center.x, center.y, 0f);

        IsShown = PlayerVision.IsVisibleToPlayer(center);
        float baseAngle = age * turnsPerSecond * 360f;
        for (int i = 0; i < stars.Count; i++)
        {
            SpriteRenderer r = stars[i];
            r.enabled = IsShown;
            if (!IsShown) continue;
            float a = (baseAngle + 360f * i / stars.Count) * Mathf.Deg2Rad;
            float sin = Mathf.Sin(a);
            // 타원 궤도. 위쪽 반(sin > 0)은 머리 뒤로 돌아가므로 몸보다 뒤에, 아래쪽 반은 앞에 그린다.
            r.transform.localPosition = (Vector3)Snap(new Vector2(Mathf.Cos(a) * radiusX, sin * radiusY));
            r.sortingLayerID = body.sortingLayerID;
            r.sortingOrder = sin > 0f ? body.sortingOrder - 1 : body.sortingOrder + 1;
        }
    }

    private static Vector2 Snap(Vector2 p) => new Vector2(Mathf.Round(p.x / Grid) * Grid, Mathf.Round(p.y / Grid) * Grid);
}
