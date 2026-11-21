using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 승리 화면(GameClearPanel)·게임오버 화면(GameOverPanel)의 [타이틀로] 버튼.
/// [다시 시작] 버튼을 그대로 복제해서(같은 그림·글꼴·크기·색) 글자만 바꾸고, 두 버튼을 같은 높이에 나란히 놓는다 -
/// 패널이 씬 세 곳(트럭·보호소·마을용 프리팹)에 따로 있어도 모두 같은 모양이 된다.
/// 누르면 새 게임 상태로 초기화하고(GameManager.ResetGame - 저장된 동물·시체 자리도 비우고, 마을에서 누르면 씬이 내려간 뒤 한 번 더 비운다)
/// 타이틀 씬으로 간다.
/// </summary>
public static class EndScreenButtons
{
    public const string TitleButtonName = "TitleButton";
    public const string TitleButtonText = "타이틀로";
    public const string TitleSceneName = "TitleScene";
    private const float Gap = 40f; // 두 버튼 사이 간격(기준 단위)

    /// <summary>[다시 시작] 옆에 [타이틀로]를 만든다(이미 있으면 그것을 돌려준다).</summary>
    public static Button AddTitleButton(Button restartButton)
    {
        if (restartButton == null) return null;
        Transform parent = restartButton.transform.parent;
        Transform existing = parent.Find(TitleButtonName);
        if (existing != null) return existing.GetComponent<Button>();

        GameObject copy = Object.Instantiate(restartButton.gameObject, parent, false);
        copy.name = TitleButtonName;
        Button title = copy.GetComponent<Button>();
        title.onClick = new Button.ButtonClickedEvent(); // 복제된 클릭 연결은 버린다(다시 시작이 같이 불리지 않게)
        foreach (TMP_Text label in copy.GetComponentsInChildren<TMP_Text>(true)) label.text = TitleButtonText;
        foreach (Text label in copy.GetComponentsInChildren<Text>(true)) label.text = TitleButtonText;
        copy.transform.SetSiblingIndex(restartButton.transform.GetSiblingIndex() + 1);

        // 같은 높이에 나란히: 원래 가운데 자리를 기준으로 [다시 시작]은 왼쪽, [타이틀로]는 오른쪽
        RectTransform r = (RectTransform)restartButton.transform, t = (RectTransform)copy.transform;
        float half = r.rect.width * 0.5f + Gap * 0.5f;
        Vector2 center = r.anchoredPosition;
        r.anchoredPosition = center + new Vector2(-half, 0f);
        t.anchoredPosition = center + new Vector2(half, 0f);
        title.onClick.AddListener(GoToTitle);
        return title;
    }

    /// <summary>새 게임 상태로 초기화하고 타이틀 씬으로 간다.</summary>
    public static void GoToTitle()
    {
        // 패널이 떠 있는 동안 여러 번 눌러도 한 번만(다시 시작 버튼과 같다)
        if (!SceneTransitionGuard.TryBeginTransition()) return;
        Time.timeScale = 1f;
        GameManager.Instance?.ResetGame();
        SceneManager.LoadScene(TitleSceneName);
    }
}
