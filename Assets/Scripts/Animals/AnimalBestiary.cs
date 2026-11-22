using UnityEngine;

/// <summary>마을 동물 종류(도감에서 쓰는 단위). 같은 행동 스크립트를 쓰는 동물은 같은 종류다.</summary>
public enum AnimalSpecies { Koala, Cat, RedPanda, Turtle, Goose }

/// <summary>
/// 동물 소개 글을 한곳에 모은 곳 - 도감(BestiaryUI)이 쓴다. MonsterBestiary와 같은 방식.
/// 글은 지금 코드의 실제 동작과 설정값에 맞춘다(숫자는 GameBalanceConfig에서 읽어, 값을 바꾸면 글도 같이 바뀐다). [키]는 키 모양 칸으로 그린다.
/// - 코알라(AnimalWander): 제자리 근처(1.5칸)를 느리게 맴돌고 도망치지 않는다. 다가가 E.
/// - 고양이(AnimalFlee): 가까이 오면 놀라 animalFleeSpeed로 달아난다 - 플레이어 걷기(playerMoveSpeed)보다 느리다. 도망치는 동안 소리를 낸다.
/// - 레서판다(AnimalSoundFlee): 고양이처럼 달아나며, 달아날 때 크게 울어(soundReactiveMonsterHearRange) 소리반응형 몬스터를 부른다.
/// - 거북이(AnimalTurtle): 놀라면 조금 달아나 등껍질에 숨는다(숨은 동안 못 든다). turtleHideRadius 밖에서 turtleEmergeDelay 지나면 나온다.
///   turtleSleepInterruptsHiding이면 마취총이 숨어 있어도 재운다.
/// - 거위(AnimalGoose): gooseFleeSpeed로 멀리 날아 도망친다. 깨어 있으면 못 들고, 포획망·마취총·폭탄에 맞아 잠든 동안(sleepDuration)만 든다.
/// </summary>
public static class AnimalBestiary
{
    public struct Entry
    {
        public AnimalSpecies Kind;
        public string Name;
        /// <summary>특징 한 줄.</summary>
        public string Trait;
        /// <summary>잡는 법 한 줄.</summary>
        public string Tip;
    }

    public static readonly AnimalSpecies[] AllKinds = { AnimalSpecies.Koala, AnimalSpecies.Cat, AnimalSpecies.RedPanda, AnimalSpecies.Turtle, AnimalSpecies.Goose };

    /// <summary>동물 오브젝트(또는 프리팹)의 종류. 동물이 아니면 null.</summary>
    public static AnimalSpecies? KindOf(GameObject go)
    {
        if (go == null || go.GetComponent<AnimalRescue>() == null) return null;
        if (go.GetComponent<AnimalGoose>() != null) return AnimalSpecies.Goose;
        if (go.GetComponent<AnimalTurtle>() != null) return AnimalSpecies.Turtle;
        if (go.GetComponent<AnimalSoundFlee>() != null) return AnimalSpecies.RedPanda;
        if (go.GetComponent<AnimalFlee>() != null) return AnimalSpecies.Cat;
        return AnimalSpecies.Koala;
    }

    public static Entry Get(AnimalSpecies kind, GameBalanceConfig c)
    {
        switch (kind)
        {
            case AnimalSpecies.Koala:
                return new Entry { Kind = kind, Name = "코알라", Trait = "졸린 느림보예요. 도망치지 않아요.", Tip = "다가가서 [E]로 구조!" };
            case AnimalSpecies.Cat:
                return new Entry { Kind = kind, Name = "고양이", Trait = "가까이 가면 놀라서 달아나요.", Tip = "걷기보다 느려요. 따라가서 [E]!" };
            case AnimalSpecies.RedPanda:
                return new Entry { Kind = kind, Name = "레서판다", Trait = "놀라면 크게 울어 몬스터를 불러요.", Tip = "따라가서 [E]! 울면 몬스터가 와요." };
            case AnimalSpecies.Turtle:
                return new Entry
                {
                    Kind = kind, Name = "거북이", Trait = "놀라면 조금 달아나 등껍질에 숨어요.",
                    Tip = $"숨으면 {Num(c != null ? c.turtleHideRadius : 5f)}칸 떨어져 {Num(c != null ? c.turtleEmergeDelay : 3f)}초 기다려요." +
                          (c == null || c.turtleSleepInterruptsHiding ? " 마취총은 바로!" : ""),
                };
            case AnimalSpecies.Goose:
                return new Entry { Kind = kind, Name = "거위", Trait = "무겁고, 날아서 아주 빨리 도망쳐요.", Tip = "포획망·마취총·폭탄으로 재운 뒤 [E]!" };
        }
        return new Entry { Kind = kind, Name = kind.ToString(), Trait = "", Tip = "" };
    }

    private static string Num(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#");

    /// <summary>도감에 쓸 동물 그림 - 몬스터와 같은 방법(몸 그림의 서 있는 그림). 시트를 실행 중에 잘라 쓰는 동물(레서판다, SpriteSheetAnimator)은
    /// 프리팹 그림이 자리표시라 시트의 Idle 첫 프레임을 잘라 쓴다.</summary>
    public static Sprite PortraitOf(GameObject animal)
    {
        if (animal == null) return null;
        SpriteSheetAnimator sheet = animal.GetComponentInChildren<SpriteSheetAnimator>(true);
        Sprite s = sheet != null ? sheet.CreateIdleFirstFrame() : null;
        return s != null ? s : MonsterBestiary.PortraitOf(animal);
    }
}
