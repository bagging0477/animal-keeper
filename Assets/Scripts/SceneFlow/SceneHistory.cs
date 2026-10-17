using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>직전에 있던 씬 이름을 기억한다. 같은 씬이라도 어디서 왔는지에 따라 시작 위치를 바꿀 때
/// (ArrivalSpawnPoint) 쓴다. activeSceneChanged는 단일 로드 시 이전 씬이 이미 내려가 이름이 비어 있을 수 있어서,
/// sceneLoaded마다 "지금 씬"을 직접 기록해 두고 다음 로드 때 그것을 "이전 씬"으로 넘긴다.</summary>
public static class SceneHistory
{
    private static string currentSceneName;

    /// <summary>지금 씬으로 오기 직전의 씬 이름. 게임을 막 시작한 첫 씬에서는 null.</summary>
    public static string PreviousSceneName { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        currentSceneName = null;
        PreviousSceneName = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        PreviousSceneName = currentSceneName;
        currentSceneName = scene.name;
    }
}
