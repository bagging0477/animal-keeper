/// <summary>
/// 동물을 넘길 때 수의사(DeliveryClerk)가 말풍선으로 하는 한 줄 대사 목록(밈). 대사는 모두 이 파일에만 있다.
/// - 동물 종류마다 2개씩, 그중 하나를 고른다. 여러 마리를 한 번에 넘기면 마지막 동물(인벤토리 칸 순서상 마지막)의 대사 하나만 나온다.
/// - 고르는 난수는 자기 System.Random만 쓴다 - UnityEngine.Random을 쓰면 몬스터·맵 난수 순서가 바뀐다.
/// </summary>
public static class ClerkLines
{
    public enum Species { Koala, Cat, RedPanda, Turtle, Goose, MonsterCorpse }

    /// <summary>종류별 대사 2개씩.</summary>
    public static string[] LinesOf(Species species)
    {
        switch (species)
        {
            case Species.Koala: return new[] { "하루 스무 시간 자는 친구죠?", "유칼립투스 냄새가 솔솔 나요." };
            case Species.Cat: return new[] { "상자부터 찾네요. 역시 고양이.", "벌써 제 의자를 차지했어요." };
            case Species.RedPanda: return new[] { "만세 자세! 하나도 안 무서워.", "꼬리 줄무늬 세다가 퇴근할 듯." };
            case Species.Turtle: return new[] { "천천히 오셨네요. 괜찮아요.", "등껍질 반짝반짝 닦아 줄게." };
            case Species.Goose: return new[] { "저… 물리는 거 아니죠?", "꽥! 아, 방금 그건 제 비명이에요." };
            default: return new[] { "이것도… 동물 맞죠?", "연구실로 조용히 보낼게요." };
        }
    }

    private static readonly System.Random rng = new System.Random();

    /// <summary>넘긴 동물 칸 하나의 종류. 원본 프리팹(AnimalPrefab)의 행동 컴포넌트로 가리고, 없으면 행동 종류(AnimalKind)로 정한다.</summary>
    public static Species SpeciesOf(InventorySlotData slot)
    {
        if (slot.AnimalKind == AnimalBehaviorKind.MonsterCorpse) return Species.MonsterCorpse;
        if (slot.AnimalPrefab != null)
        {
            if (slot.AnimalPrefab.GetComponent<AnimalTurtle>() != null) return Species.Turtle;
            if (slot.AnimalPrefab.GetComponent<AnimalGoose>() != null) return Species.Goose;
        }
        switch (slot.AnimalKind)
        {
            case AnimalBehaviorKind.SoundFlee: return Species.RedPanda;
            case AnimalBehaviorKind.Flee: return Species.Cat;
            default: return Species.Koala;
        }
    }

    /// <summary>그 종류의 대사 중 하나.</summary>
    public static string Pick(Species species)
    {
        string[] lines = LinesOf(species);
        return lines[rng.Next(lines.Length)];
    }
}
