using UnityEngine.SceneManagement;

/// <summary>씬 전환(SceneManager.LoadScene)을 부르는 모든 지점이 이 게이트를 거치게 해서, 같은
/// 프레임에 서로 다른 상호작용 지점(또는 중복 입력)이 동시에 트리거되어 LoadScene이 두 번
/// 불리는 사고를 막는다. 정상적인 단일 입력 흐름에서는 항상 첫 호출만 있으므로 기존 동작에는
/// 아무 영향이 없다 - 씬이 실제로 로드되는 순간(sceneLoaded) 자동으로 다시 열린다.</summary>
public static class SceneTransitionGuard
{
    private static bool transitionStarted;

    static SceneTransitionGuard()
    {
        SceneManager.sceneLoaded += (_, __) => transitionStarted = false;
    }

    /// <summary>이번 씬에서 아직 전환을 시작하지 않았다면 true를 반환하며 게이트를 잠근다 - 반환값이
    /// true일 때만 SceneManager.LoadScene을 호출해야 한다. 이미 누군가 전환을 시작했다면 false.</summary>
    public static bool TryBeginTransition()
    {
        if (transitionStarted) return false;
        transitionStarted = true;
        return true;
    }
}
