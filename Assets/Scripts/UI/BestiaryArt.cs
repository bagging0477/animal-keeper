using UnityEngine;

/// <summary>도감 그림을 꺼낼 프리팹 목록(Resources/Bestiary/BestiaryArt). 종류는 프리팹의 행동 스크립트로 가린다(AnimalBestiary.KindOf, MonsterBestiary.KindOf).
/// 그림은 프리팹의 몸 그림(서 있는 그림)을 그대로 쓴다 - 따로 그린 그림이 없어 게임 속 모습과 늘 같다.</summary>
public class BestiaryArt : ScriptableObject
{
    public const string ResourcePath = "Bestiary/BestiaryArt";
    public GameObject[] animalPrefabs;
    public GameObject[] monsterPrefabs;
}
