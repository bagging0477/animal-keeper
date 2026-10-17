using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TruckScene(scene index 0)은 게임이 맨 처음 부팅될 때, 그리고 "다음 날로"/"재시작" 등으로
/// 다시 로드될 때마다 진입점 역할을 한다. 아직 시작 시 무료 클래스 선택(GameManager.
/// NeedsStartingClassSelection)을 거치지 않았다면 - 새 게임을 막 시작했거나 방금 재시작(ResetGame)한
/// 직후라면 - TruckScene을 보여주기 전에 ClassSelectScene으로 돌려보낸다. GameManager는 씬을 넘나들며
/// 살아남는 DontDestroyOnLoad 싱글턴이라 이 검사를 GameManager 자신에게 둘 수 없다 - GameManager.Awake()는
/// 그 오브젝트가 진짜로 처음 생성될 때 딱 한 번만 실행되므로, TruckScene을 다시 불러올 때마다(재시작 등)
/// 매번 다시 검사하려면 TruckScene 쪽에 새로 로드될 때마다 Start가 실행되는 이 오브젝트가 따로 필요하다.
/// Start(같은 프레임의 모든 Awake가 끝난 뒤 실행)에서 검사해야, TruckScene이 처음 로드되는 바로 그
/// 프레임에 GameManager.Awake()가 이 오브젝트보다 나중에 실행되더라도 Instance가 이미 준비되어 있다.</summary>
public class TruckSceneStartGate : MonoBehaviour
{
    [SerializeField] private string classSelectSceneName = "ClassSelectScene";

    private void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.NeedsStartingClassSelection)
        {
            if (!SceneTransitionGuard.TryBeginTransition()) return;
            SceneManager.LoadScene(classSelectSceneName);
        }
    }
}
