using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// ESC 일시정지 메뉴. Resources/PauseMenu 프리팹을 게임 시작 시 한 번만 만들어 씬을 넘어가도 유지하므로,
/// 씬마다 따로 배치하지 않아도 모든 게임플레이 씬(TruckScene, VillageScene, ShelterScene, ClassSelectScene 등)에서
/// 같은 메뉴가 뜬다. disabledScenes(기본: TitleScene)에서는 ESC가 동작하지 않는다.
///
/// 다른 화면(트럭 허브 메뉴, 게임오버/클리어 패널)이 이미 Time.timeScale을 0으로 멈춰둔 동안에는 ESC로 열리지 않는다 -
/// 그 화면들이 직접 시간을 되돌리므로, 여기서 1로 되돌려버리면 그 화면 뒤에서 게임이 다시 돌아간다.
/// 트럭 허브 메뉴는 ESC로 닫히기도 하므로, 같은 프레임에 메뉴가 닫히자마자 일시정지가 열리지 않도록 다른 스크립트보다
/// 먼저 실행한다(DefaultExecutionOrder).
///
/// 게임플레이 입력(E 상호작용, 공격, 슬롯 선택, 이동 등)을 읽는 스크립트는 IsPaused일 때 입력을 무시한다.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PauseMenu : MonoBehaviour
{
    private const string ResourcePath = "PauseMenu";

    /// <summary>일시정지 메뉴가 열려 있는 동안 true. 게임플레이 입력을 읽는 곳에서 확인한다.</summary>
    public static bool IsPaused { get; private set; }

    [SerializeField] private GameObject menuRoot;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private string titleSceneName = "TitleScene";
    [Tooltip("ESC 일시정지를 쓰지 않는 씬 이름들.")]
    [SerializeField] private string[] disabledScenes = { "TitleScene" };

    private static PauseMenu instance;
    private GameObject fallbackEventSystem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStartup()
    {
        IsPaused = false;
        if (instance != null) return;
        PauseMenu prefab = Resources.Load<PauseMenu>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"PauseMenu: Resources/{ResourcePath} 프리팹을 찾지 못해 ESC 일시정지 메뉴 없이 진행한다.");
            return;
        }

        instance = Instantiate(prefab);
        instance.name = prefab.name;
        DontDestroyOnLoad(instance.gameObject);
    }

    private void Awake()
    {
        Hide();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 씬이 바뀌면 메뉴는 항상 닫힌 상태로 시작한다. 시간도 여기서 무조건 되돌린다 - 씬을 넘기는 쪽이
        // 깜빡하거나(게임 클리어 패널이 뜬 채 출구에서 E로 재시작 등), ESC를 누른 바로 그 프레임에 몬스터에게
        // 잡혀 강제 복귀하면 timeScale 0이 다음 씬으로 넘어가 화면이 얼어붙고, ESC도 "다른 화면이 멈춰둔
        // 상태"로 오인해 열리지 않아 빠져나갈 방법이 없었다.
        IsPaused = false;
        Time.timeScale = 1f;
        Hide();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        if (IsPaused)
        {
            // 설정 창이 열려 있으면 ESC는 설정 창만 닫는다.
            if (settingsPanel != null && settingsPanel.activeSelf) OnCloseSetting();
            else OnResume();
            return;
        }

        if (System.Array.IndexOf(disabledScenes, SceneManager.GetActiveScene().name) >= 0) return;
        if (Time.timeScale == 0f) return; // 다른 화면이 이미 게임을 멈춰둔 상태
        Pause();
    }

    private void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        EnsureEventSystem();
        if (menuRoot != null) menuRoot.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void OnResume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        Hide();
    }

    public void OnSetting()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void OnCloseSetting()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void OnExit()
    {
        if (!SceneTransitionGuard.TryBeginTransition()) return;
        Time.timeScale = 1f;
        IsPaused = false;
        Hide();
        SceneManager.LoadScene(titleSceneName);
    }

    private void Hide()
    {
        if (menuRoot != null) menuRoot.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (fallbackEventSystem != null)
        {
            Destroy(fallbackEventSystem);
            fallbackEventSystem = null;
        }
    }

    // 버튼 클릭에는 EventSystem이 필요한데, 씬에 없을 수도 있다(VillageScene 등). 그럴 때만 메뉴가 열려 있는 동안 임시로 만든다 -
    // 항상 하나를 들고 다니면 자체 EventSystem이 있는 씬에서 두 개가 겹친다.
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        fallbackEventSystem = new GameObject("PauseMenuEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        fallbackEventSystem.transform.SetParent(transform, false);
    }
}
