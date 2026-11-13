using System;
using System.Reflection;
using Object = UnityEngine.Object;

/// <summary>에디터 테스트가 시작할 때 앞 테스트가 남긴 상태를 비운다(테스트 쪽에서만 쓰고 게임 코드는 바꾸지 않는다).
/// 이 프로젝트는 Play 들어갈 때 도메인 리로드를 끄므로(Enter Play Mode Options) 앞 테스트 Play의 GameManager/AudioManager가
/// 파괴된 채 static Instance에 남는다. <c>== null</c>로는 없는 것처럼 보이지만 <c>?.</c> 호출은 그 파괴된 객체로 들어간다.
/// 예: VillageScene만 여는 테스트에서 몬스터가 남은 GameManager에 TakeDamage → 체력 0 → TruckScene 로드 → 진짜 GameManager가 생겨
/// 그 뒤 맵마다 이전 맵의 동물 위치가 되살아난다. 살아 있는 인스턴스는 건드리지 않는다.</summary>
public static class EditorTestState
{
    private const string BalanceConfigPath = "Assets/Settings/GameBalanceConfig.asset";
    private static float savedDuskDuration = -1f;

    /// <summary>마을 해질녘(GameBalanceConfig.duskDurationSeconds)을 테스트 동안 끈다 - 마을 시간 0초부터 밤 시야(원형 4칸)로 시험하는 테스트용.
    /// 에셋 값은 메모리에서만 바꾸고 RestoreVillageDusk에서 되돌린다(저장하지 않는다).</summary>
    public static void PinVillageNight()
    {
        var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(BalanceConfigPath);
        if (config == null) return;
        if (savedDuskDuration < 0f) savedDuskDuration = config.duskDurationSeconds;
        config.duskDurationSeconds = 0f;
    }

    /// <summary>PinVillageNight로 끈 해질녘 시간을 원래 값으로 되돌린다. 끈 적이 없으면 아무것도 하지 않는다.</summary>
    public static void RestoreVillageDusk()
    {
        if (savedDuskDuration < 0f) return;
        var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(BalanceConfigPath);
        if (config != null) config.duskDurationSeconds = savedDuskDuration;
        savedDuskDuration = -1f;
    }

    public static void ClearStaleSingletons()
    {
        foreach (Type t in new[] { typeof(GameManager), typeof(AudioManager) })
        {
            PropertyInfo prop = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            var current = prop?.GetValue(null) as Object;
            if (!ReferenceEquals(current, null) && current == null) prop.SetValue(null, null);
        }
    }
}
