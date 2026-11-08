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
