using UnityEngine;

/// <summary>몬스터 종류(첫 만남 카드·도감에서 쓰는 단위). 같은 AI 스크립트를 쓰는 몬스터는 같은 종류다.</summary>
public enum MonsterKind { Wolf, SoundReactive, Bat, Porcupine, Rhino }

/// <summary>
/// 몬스터 소개 글을 한곳에 모은 곳 - 첫 만남 카드(MonsterEncounterCard)가 쓰고, 나중에 도감도 같은 글을 쓴다.
/// 글은 지금 코드의 실제 동작과 GameBalanceConfig 값에 맞춘다(숫자는 설정값에서 읽어, 값을 바꾸면 글도 같이 바뀐다). [키]는 키 모양 칸으로 그린다.
/// - 늑대(MonsterAI): 자기 영역(wolfLeashRadius)을 돈다. 가까이(patrolMonsterDetectRange) 오면 쫓고, wolfChaseBurstDuration 쫓은 뒤 wolfWindedDuration 동안 느려진다.
/// - 소리반응형(SoundReactiveMonsterAI): 보는 거리는 짧고(soundReactiveMonsterDetectRange), 달리는 발소리(PlayerMovement)·동물 울음(AnimalSoundFlee)을 soundReactiveMonsterHearRange에서 듣고 온다.
/// - 박쥐(BatMonsterAI): 거리 제한 없이 날아오고, 정면 손전등 시야(IsInFocusedVision) 안에서는 굳는다. 닿으면 최대 체력 x batCatchDamageRatio.
/// - 호저(PorcupineMonsterAI): 움직이지 않고, porcupineSightRange 안이면 가시를 세운 뒤 porcupineQuillCount발을 쏜다(사거리 porcupineQuillRange, 벽에 막힘).
/// - 코뿔소(RhinoMonsterAI): rhinoChargeDuration 노려본 뒤 일직선 돌진, 벽에 박으면 rhinoWallStunDuration 기절.
/// </summary>
public static class MonsterBestiary
{
    public struct Entry
    {
        public MonsterKind Kind;
        public string Name;
        /// <summary>특징 한두 줄.</summary>
        public string[] Traits;
        /// <summary>대처법 한 줄.</summary>
        public string Tip;
    }

    public static readonly MonsterKind[] AllKinds = { MonsterKind.Wolf, MonsterKind.SoundReactive, MonsterKind.Bat, MonsterKind.Porcupine, MonsterKind.Rhino };

    /// <summary>몬스터 오브젝트의 종류. 몬스터가 아니면 null.</summary>
    public static MonsterKind? KindOf(GameObject go)
    {
        if (go == null) return null;
        if (go.GetComponent<RhinoMonsterAI>() != null) return MonsterKind.Rhino;
        if (go.GetComponent<PorcupineMonsterAI>() != null) return MonsterKind.Porcupine;
        if (go.GetComponent<BatMonsterAI>() != null) return MonsterKind.Bat;
        if (go.GetComponent<SoundReactiveMonsterAI>() != null) return MonsterKind.SoundReactive;
        if (go.GetComponent<MonsterAI>() != null) return MonsterKind.Wolf;
        return null;
    }

    public static Entry Get(MonsterKind kind, GameBalanceConfig c)
    {
        switch (kind)
        {
            case MonsterKind.Wolf:
                return new Entry
                {
                    Kind = kind, Name = "늑대",
                    Traits = new[] { "자기 영역을 도는 사냥꾼이에요.", "가까이 가면 쫓아와요." },
                    Tip = "[Shift]로 달아나요! 오래 쫓으면 지쳐서 느려져요.",
                };
            case MonsterKind.SoundReactive:
                return new Entry
                {
                    Kind = kind, Name = "소리반응형",
                    Traits = new[] { "눈은 어둡고 귀가 밝아요.", "달리는 발소리와 동물 울음을 듣고 와요." },
                    Tip = "근처에서는 달리지 말고 걸어요.",
                };
            case MonsterKind.Bat:
                return new Entry
                {
                    Kind = kind, Name = "박쥐",
                    Traits = new[] { "어둠 속에서 곧장 날아와요.", "손전등 빛에 비치면 얼어붙어요." },
                    Tip = $"마우스로 비춰 세워요! 닿으면 체력 {Pct(c != null ? c.batCatchDamageRatio : 0.8f)}%가 깎여요.",
                };
            case MonsterKind.Porcupine:
                return new Entry
                {
                    Kind = kind, Name = "호저",
                    Traits = new[] { $"제자리에서 가시 {(c != null ? c.porcupineQuillCount : 3)}발을 쏴요.", $"가시는 {Num(c != null ? c.porcupineQuillRange : 8f)}칸까지 날아와요." },
                    Tip = "가시를 세우면 벽 뒤로 숨거나 옆으로 비켜요.",
                };
            case MonsterKind.Rhino:
                return new Entry
                {
                    Kind = kind, Name = "코뿔소",
                    Traits = new[] { "노려보다가 일직선으로 돌진해요." },
                    Tip = $"노려볼 때 옆으로! 벽에 박으면 {Num(c != null ? c.rhinoWallStunDuration : 2f)}초 기절해요.",
                };
        }
        return new Entry { Kind = kind, Name = kind.ToString(), Traits = new string[0], Tip = "" };
    }

    private static int Pct(float ratio) => Mathf.RoundToInt(ratio * 100f);
    private static string Num(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#");

    /// <summary>카드·도감에 쓸 몬스터 그림: 몸 그림(바닥 그림자·눈빛 덧그림 제외)의 서 있는 그림(없으면 지금 그림).</summary>
    public static Sprite PortraitOf(GameObject monster)
    {
        if (monster == null) return null;
        SpriteRenderer best = null; float bestArea = -1f;
        foreach (SpriteRenderer r in monster.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.sprite == null) continue;
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("shadow") || n.Contains("eye") || n.Contains("glow")) continue;
            float area = r.sprite.rect.width * r.sprite.rect.height;
            if (area > bestArea) { bestArea = area; best = r; }
        }
        if (best == null) return null;
        SimpleFrameAnimator anim = best.GetComponent<SimpleFrameAnimator>();
        return anim != null && anim.IdleSprite != null ? anim.IdleSprite : best.sprite;
    }
}
