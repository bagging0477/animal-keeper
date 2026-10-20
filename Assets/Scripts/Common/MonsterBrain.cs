using UnityEngine;

/// <summary>몬스터 두뇌(MonsterAI, SoundReactiveMonsterAI)가 똑같이 쓰던 위치 도우미, 시야 판정, 접촉(잡기) 처리를 모은 기반 클래스.
/// Unity 메시지(Awake/Start/Update 등)는 두지 않는다 - 실행 순서는 각 두뇌가 그대로 정한다. 직렬화 필드도 두지 않는다(프리팹 값은 각 두뇌에 남는다);
/// 각 두뇌의 직렬화 값·설정은 아래 연결 속성으로 받는다.</summary>
public abstract class MonsterBrain : MonoBehaviour
{
    // 두 두뇌가 같은 이름으로 들고 있던 런타임 값(직렬화되지 않는다). 각 두뇌의 Awake/Start가 채운다.
    protected SimpleFrameAnimator frameAnimator;
    protected Transform player;
    protected bool caughtLogged;

    // 각 두뇌의 직렬화 필드·설정을 기반 코드에 넘기는 연결
    protected abstract LayerMask SightBlockingMask { get; }
    protected abstract float CatchRange { get; }
    protected abstract int CatchDamage { get; }
    protected abstract string MonsterTypeName { get; }

    protected Vector2 GamePosition => new Vector2(transform.position.x, transform.position.z);
    protected Vector2 PlayerGamePosition => new Vector2(player.position.x, player.position.y);
    protected Vector3 PlayerNavPosition => new Vector3(player.position.x, 0f, player.position.y);

    // 문처럼 폭이 좁은 연결부에서는 중심선 하나만으로 시야 차단을 판정하면, 몬스터나 플레이어가
    // 문 한가운데에서 살짝만 벗어나 있어도 선이 문틀 모서리에 걸려 "완전히 막힌 것"으로 잘못
    // 판정된다. 그 상태가 ChaseGiveUpSightLostDuration만큼 이어지면 문을 넘어가는 도중에 갑자기
    // 추격을 포기하는 것처럼 보인다. 중심선 옆으로 살짝 평행하게 옮긴 보조선 두 개를 더 검사해서,
    // 셋 중 하나라도 뚫려 있으면 아직 보인다고 판정한다 - 같은 방 안에서 장애물 뒤에 완전히
    // 숨는 경우는 장애물이 이 보조선 간격보다 훨씬 크므로 여전히 셋 다 막혀서 스텔스에는 영향이 없다.
    private const float SightSampleOffset = 0.3f;

    // 3줄 중 2줄 이상 막히면 "시야 차단"으로 판정한다. 원래는 3줄 전부를 요구했는데, 추격 중인
    // 몬스터도 플레이어와 거의 같은 타이밍에 같은 모퉁이를 돌기 때문에 세 줄이 동시에 완전히
    // 막히는 순간이 실제로는 드물어서 사실상 영원히 시야를 놓치지 않는 문제가 있었다. 2줄만
    // 요구해도 문틀 모서리에 한 줄만 걸리는 흔한 오판(중심선만 잠깐 스치는 경우)은 여전히
    // 걸러진다.
    protected bool IsSightBlocked(Vector2 from, Vector2 to)
    {
        int blockedLines = 0;
        if (IsLineBlocked(from, to)) blockedLines++;

        Vector2 delta = to - from;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * SightSampleOffset;
        if (IsLineBlocked(from + offset, to + offset)) blockedLines++;
        if (IsLineBlocked(from - offset, to - offset)) blockedLines++;

        return blockedLines >= 2;
    }

    // 선이 끝나는 지점은 플레이어 한가운데라 플레이어 자신의 콜라이더(1x1 박스)에 항상 걸린다 - 예전에는
    // 이것 때문에 세 줄이 전부 "막힘"으로 판정되어 추격 내내 시야가 가려진 것으로 취급됐고, 마지막
    // 목격 위치도 추격 시작 지점에서 전혀 갱신되지 않았다. 트리거(아이템/포인트/벽 그림자용 상자 등)와
    // 움직이는 리지드바디(플레이어, 동물)는 건너뛰고, 벽처럼 고정된 콜라이더만 시야를 가린 것으로 친다.
    protected bool IsLineBlocked(Vector2 from, Vector2 to)
    {
        return StaticBlockerLine.IsBlocked(from, to, SightBlockingMask);
    }

    /// <summary>각 두뇌의 Update가 플레이어와의 거리를 잰 바로 그 자리에서 부른다(예전 코드가 있던 자리).
    /// true를 돌려주면 잡기 처리 도중 이 몬스터가 파괴된 것이므로 호출한 Update도 바로 빠져나와야 한다.</summary>
    protected bool UpdateCatch(float distanceToPlayer)
    {
        if (distanceToPlayer <= CatchRange)
        {
            if (!caughtLogged)
            {
                caughtLogged = true;
                HandleCatch();

                // HandleCatch()가 플레이어 체력을 0으로 만들면 GameManager가 동기적으로
                // SceneManager.LoadScene을 호출해 현재 씬(이 몬스터 포함)을 즉시 파괴한다.
                // 그 상태로 아래 코드(agent/visual 접근)를 계속 실행하면
                // MissingReferenceException이 난다 - 파괴됐으면 바로 빠져나온다.
                if (this == null) return true;
            }
        }
        else
        {
            caughtLogged = false;
        }
        return false;
    }

    private void HandleCatch()
    {
        frameAnimator?.PlayRandomAttack();
        int damage = CatchDamage;
        GameManager.Instance?.TakeDamage(damage, MonsterTypeName);
    }
}
