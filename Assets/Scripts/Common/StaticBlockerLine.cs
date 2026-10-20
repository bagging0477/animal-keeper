using System.Collections.Generic;
using UnityEngine;

/// <summary>두 점 사이를 고정 충돌체(맵 경계, 장애물, 트럭 등)가 막는지 본다. 몬스터 시야(MonsterAI, SoundReactiveMonsterAI)와
/// 마취총 다트(TranquilizerDart)가 같은 규칙을 쓴다: 트리거(아이템/포인트/벽 그림자용 상자 등)와 움직이는 리지드바디(플레이어, 동물)는
/// 건너뛰고, 벽처럼 고정된 충돌체만 막은 것으로 친다.</summary>
public static class StaticBlockerLine
{
    private static readonly List<RaycastHit2D> sharedHits = new List<RaycastHit2D>();

    public static bool IsBlocked(Vector2 from, Vector2 to, LayerMask mask) => TryGetFirstHit(from, to, mask, out _);

    /// <summary>from에서 가장 가까운 고정 충돌체의 지점을 돌려준다. from이 이미 충돌체 안이면(Physics2D의 "충돌체 안에서 시작" 설정)
    /// 거리 0의 지점이 잡힌다.</summary>
    public static bool TryGetFirstHit(Vector2 from, Vector2 to, LayerMask mask, out RaycastHit2D firstHit)
    {
        firstHit = default;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(mask);
        int hitCount = Physics2D.Linecast(from, to, filter, sharedHits);
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = sharedHits[i];
            Rigidbody2D body = hit.rigidbody;
            if (body != null && body.bodyType != RigidbodyType2D.Static) continue;
            if (!found || hit.fraction < firstHit.fraction)
            {
                firstHit = hit;
                found = true;
            }
        }
        return found;
    }
}
