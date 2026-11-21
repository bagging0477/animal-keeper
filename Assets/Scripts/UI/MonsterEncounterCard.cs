using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 몬스터 첫 만남 카드. 몬스터 종류(MonsterKind)마다 그 종류가 처음 플레이어 시야에 보였을 때(PlayerVision.IsVisibleToPlayer - 화면 = 시야 판정)
/// 소개 카드(그림·이름·특징·대처법, 글은 MonsterBestiary)를 한 번 띄운다.
/// - 카드가 떠 있는 동안 게임을 멈추고(Time.timeScale 0) 화면을 어둡게 한다 - 튜토리얼 멈춤 화면과 같은 어둠·카드 모양·글씨 크기.
///   [Space] 또는 클릭으로 닫으면 다시 움직인다. 멈춘 동안(닫은 프레임 포함) 게임 입력을 막는다(GameplayInput.IsBlocked).
/// - 한 게임에서 종류마다 한 번. 새 게임(GameManager.NeedsStartingClassSelection)이 되면 처음부터 다시 뜬다.
/// - 튜토리얼 멈춤 화면이 떠 있거나 곧 뜰 차례면(TutorialGuide.HasPendingIntro), 일시정지·상점·다른 멈춤 화면이 있으면 그게 끝난 뒤에 띄운다.
///   여러 종류가 한꺼번에 처음 보이면 대기열에 넣어 차례로 띄운다. 마을을 떠나면 아직 못 띄운 카드는 지우고 다음에 다시 보일 때 띄운다.
/// - 처음 보인 뒤 ReadyDelay(실제 시간) 기다렸다 띄운다 - 같은 순간 튜토리얼이 그 몬스터로 5단계 멈춤 화면을 시작할 차례를 먼저 주려고.
/// - 씬과 무관하게 하나만 있고(DontDestroyOnLoad) 자기 전용 오버레이 Canvas에 그린다. 게임이 시작될 때 스스로 만들어진다.
/// - 다른 batch 테스트에서는 띄우지 않는다(Suppressed - TutorialIntroTestSwitch가 켠다).
/// </summary>
[DefaultExecutionOrder(-89)] // TutorialGuide(-90) 다음, 게임플레이 스크립트보다 먼저 - 닫는 Space·클릭이 같은 프레임에 공격·도구로 새지 않게
public class MonsterEncounterCard : MonoBehaviour
{
    public const string PromptText = "[Space] 또는 클릭으로 계속";
    public const string ChipText = "첫 만남";
    private const int CanvasSortingOrder = 610;  // 튜토리얼(600) 위, 일시정지 메뉴(1000) 아래
    private const float CheckInterval = 0.25f;
    private const float ReadyDelay = 0.35f;
    private const float MinShowSeconds = 0.3f;   // 멈춘 직후 눌려 있던 키로 바로 닫히지 않게
    private const float PortraitHeight = 200f;   // 몬스터 그림(보이는 부분) 높이 목표(기준 단위) - 그림 한 칸의 정수배로 키운다
    private const float PortraitMaxWidth = 420f;
    private static readonly Color TipColor = new Color32(0xA9, 0xD0, 0xC2, 0xFF); // 팔레트 "흐린 청록"(안내)

    private static MonsterEncounterCard instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    public static MonsterEncounterCard Instance => instance;

    /// <summary>참이면 카드를 띄우지도, 처음 본 것을 기록하지도 않는다. 게임에서는 늘 거짓 - 에디터 batch 테스트 중 첫 만남 카드 테스트가 아닌 것만 켠다.</summary>
    public static bool Suppressed;

    /// <summary>카드가 게임을 멈춘 동안(그리고 닫은 그 프레임) 게임 입력을 막는다 - GameplayInput.IsBlocked가 읽는다.</summary>
    public static bool BlocksInput => instance != null && (instance.showing || instance.closedFrame == Time.frameCount);

    public static MonsterEncounterCard EnsureInstance()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("MonsterEncounterCard");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MonsterEncounterCard>();
        return instance;
    }

    // ---------------- 상태 ----------------
    private readonly HashSet<MonsterKind> seen = new HashSet<MonsterKind>();
    private readonly List<(MonsterKind kind, Sprite portrait, float readyAt)> queue = new List<(MonsterKind, Sprite, float)>();
    private bool resetArmed;
    private float checkTimer;
    private bool showing;
    private float shownAt;
    private float savedTimeScale = 1f;
    private int closedFrame = -1;
    private string lastScene;

    /// <summary>지금 떠 있는 카드의 종류(없으면 null).</summary>
    public MonsterKind? CurrentKind { get; private set; }
    /// <summary>카드가 떠서 게임을 멈추고 있는지.</summary>
    public bool IsShowing => showing;
    /// <summary>이번 게임에서 이미 소개한(또는 대기열에 넣은) 종류인지.</summary>
    public bool HasSeen(MonsterKind kind) => seen.Contains(kind);
    /// <summary>기다리는 카드 수.</summary>
    public int QueuedCount => queue.Count;

    // ---------------- 그림 ----------------
    private RectTransform canvasRect;
    private GameObject root;
    private GameObject card;
    private Image portrait;
    private LayoutElement portraitHolder;
    private TMP_Text nameText;
    private static readonly Dictionary<Sprite, Rect> opaqueCache = new Dictionary<Sprite, Rect>();

    // 그림의 보이는 부분(알파 > 0.1인 픽셀)의 범위(그림 칸 안 픽셀, 왼쪽 아래 원점). 텍스처를 읽을 수 없게 들여와도 되게 GPU에서 한 번 복사해 읽고, 그림마다 한 번만 잰다.
    private static Rect OpaqueRect(Sprite s)
    {
        if (opaqueCache.TryGetValue(s, out Rect cached)) return cached;
        Rect full = new Rect(Vector2.zero, s.rect.size), result = full;
        Texture2D src = s.texture;
        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Rect tr = s.textureRect;
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(tr.x, tr.y, w, h), 0, 0);
            read.Apply();
            Color32[] px = read.GetPixels32();
            Destroy(read);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 25) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX >= minX && maxY >= minY)
            {
                Vector2 offset = s.textureRectOffset; // 잘라 들인 그림이면 그림 칸 안에서 텍스처 영역이 놓인 자리
                result = Rect.MinMaxRect(offset.x + minX, offset.y + minY, offset.x + maxX + 1, offset.y + maxY + 1);
            }
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
        opaqueCache[s] = result;
        return result;
    }
    private RectTransform traitLines, tipLines;
    private bool built;

    private void Build()
    {
        TutorialGuide g = TutorialGuide.EnsureInstance();
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = TutorialGuide.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        root = TutorialGuide.Child("EncounterCard", transform);
        TutorialGuide.Stretch((RectTransform)root.transform);
        Image dim = TutorialGuide.StretchChild("Dim", root.transform).gameObject.AddComponent<Image>();
        dim.color = TutorialGuide.DimColor;
        dim.raycastTarget = false;

        card = g.Box("Card", root.transform, g.panelSprite, TutorialGuide.CardPadding, 14f, true);
        RectTransform crt = (RectTransform)card.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        TutorialGuide.FitToContent(card);
        GameObject chip = g.Box("Chip", card.transform, g.stepSprite, new RectOffset(16, 16, 6, 6), 0f, false);
        TutorialGuide.Label(chip.transform, ChipText, TutorialGuide.CardChipSize, TutorialGuide.Cream);

        // 그림 + 이름
        GameObject head = g.Box("Head", card.transform, null, new RectOffset(0, 0, 4, 4), 36f, false);
        // 그림 칸(레이아웃 크기 = 그림의 보이는 부분) 안에 그림 전체(투명 여백 포함)를 겹쳐 놓는다 - 여백이 넓은 그림도 보이는 부분 기준으로 키운다
        portraitHolder = TutorialGuide.Child("PortraitFrame", head.transform).AddComponent<LayoutElement>();
        GameObject pic = TutorialGuide.Child("Portrait", portraitHolder.transform);
        RectTransform prt = (RectTransform)pic.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        portrait = pic.AddComponent<Image>();
        portrait.raycastTarget = false;
        nameText = TutorialGuide.Label(head.transform, "", TutorialGuide.CardTextSize, TutorialGuide.Cream);

        traitLines = (RectTransform)g.Box("Traits", card.transform, null, new RectOffset(0, 0, 0, 0), 4f, true).transform;
        tipLines = (RectTransform)g.Box("Tip", card.transform, null, new RectOffset(0, 0, 0, 0), 4f, true).transform;
        RectTransform prompt = (RectTransform)g.Box("Prompt", card.transform, null, new RectOffset(0, 0, 0, 0), 12f, false).transform;
        g.BuildLine(prompt, PromptText, TutorialGuide.CardPromptSize, TutorialGuide.CardPromptKeySize, TutorialGuide.HighlightYellow, TutorialGuide.CardKeyPadY);
        root.SetActive(false);
        built = true;
    }

    private void Fill(MonsterKind kind, Sprite sprite)
    {
        TutorialGuide g = TutorialGuide.EnsureInstance();
        MonsterBestiary.Entry e = MonsterBestiary.Get(kind, GameManager.Instance != null ? GameManager.Instance.Config : null);
        nameText.text = e.Name;
        portrait.sprite = sprite;
        portrait.enabled = sprite != null;
        LayoutElement le = portraitHolder;
        RectTransform prt = portrait.rectTransform;
        if (sprite != null)
        {
            // 도트 규칙: 그림 한 칸을 정수배로 키운다(Point 필터 그림 그대로). 배율은 보이는 부분(투명하지 않은 픽셀) 크기로 정한다.
            Rect vis = OpaqueRect(sprite);
            float k = Mathf.Max(1f, Mathf.Round(PortraitHeight / Mathf.Max(vis.height, vis.width * 0.6f)));
            k = Mathf.Max(1f, Mathf.Min(k, Mathf.Floor(PortraitMaxWidth / Mathf.Max(1f, vis.width))));
            le.preferredWidth = vis.width * k;
            le.preferredHeight = vis.height * k;
            prt.sizeDelta = sprite.rect.size * k;
            prt.anchoredPosition = -(vis.center - sprite.rect.size * 0.5f) * k;
        }
        else le.preferredWidth = le.preferredHeight = 0f;

        float maxWidth = canvasRect.rect.size.x - 2f * TutorialGuide.CardScreenMargin - TutorialGuide.CardPadding.left - TutorialGuide.CardPadding.right;
        var traitRows = e.Traits.SelectMany(t => g.WrapRows(t, TutorialGuide.CardTextSize, TutorialGuide.CardKeySize, TutorialGuide.CardLineSpacing, maxWidth)).ToList();
        g.BuildRows(traitLines, traitRows, TutorialGuide.CardTextSize, TutorialGuide.CardKeySize, TutorialGuide.Cream, TutorialGuide.CardKeyPadY, TutorialGuide.CardLineSpacing);
        var tipRows = g.WrapRows(e.Tip, TutorialGuide.CardTextSize, TutorialGuide.CardKeySize, TutorialGuide.CardLineSpacing, maxWidth);
        g.BuildRows(tipLines, tipRows, TutorialGuide.CardTextSize, TutorialGuide.CardKeySize, TipColor, TutorialGuide.CardKeyPadY, TutorialGuide.CardLineSpacing);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);
    }

    // ---------------- 진행 ----------------
    private void Update()
    {
        if (Suppressed) { if (showing) Close(); queue.Clear(); return; }
        GameManager gm = GameManager.Instance;
        if (gm == null) { if (showing) Close(); return; }
        if (!built) Build();

        // 새 게임: 시작 클래스 선택을 기다리는 상태가 되면 처음부터
        if (gm.NeedsStartingClassSelection)
        {
            if (!resetArmed) { seen.Clear(); queue.Clear(); if (showing) Close(); resetArmed = true; }
        }
        else resetArmed = false;

        string scene = SceneManager.GetActiveScene().name;
        if (scene != lastScene)
        {
            lastScene = scene;
            if (showing) Close();
            // 마을을 떠나면 아직 못 띄운 카드는 지운다 - 다음에 다시 보이면 그때 띄운다
            foreach (var q in queue) seen.Remove(q.kind);
            queue.Clear();
        }
        if (scene != "VillageScene" || gm.IsGameOver || gm.IsGameWon) { if (showing) Close(); return; }

        if (showing)
        {
            if (Time.unscaledTime - shownAt >= MinShowSeconds && DismissPressed()) Close();
            return;
        }

        // 시야에 처음 들어온 종류(실제 시간 0.25초마다 - 멈춘 동안에도 본다)
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = CheckInterval;
            foreach (MonsterHealth m in MonsterHealth.Active)
            {
                if (m == null || m.IsDead || !m.gameObject.activeInHierarchy) continue;
                MonsterKind? kind = MonsterBestiary.KindOf(m.gameObject);
                if (kind == null || seen.Contains(kind.Value)) continue;
                if (!PlayerVision.IsVisibleToPlayer(m.GamePosition)) continue;
                seen.Add(kind.Value);
                queue.Add((kind.Value, MonsterBestiary.PortraitOf(m.gameObject), Time.unscaledTime + ReadyDelay));
            }
        }

        if (queue.Count == 0 || Time.unscaledTime < queue[0].readyAt) return;
        TutorialGuide tg = TutorialGuide.Instance;
        bool tutorialFirst = tg != null && (tg.IsIntroActive || tg.HasPendingIntro);
        bool otherScreen = PauseMenu.IsPaused || ShopMenuUI.IsOpen || Time.timeScale <= 0f;
        if (tutorialFirst || otherScreen) return;
        var next = queue[0];
        queue.RemoveAt(0);
        Open(next.kind, next.portrait);
    }

    private void Open(MonsterKind kind, Sprite sprite)
    {
        Fill(kind, sprite);
        CurrentKind = kind;
        showing = true;
        shownAt = Time.unscaledTime;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        root.SetActive(true);
    }

    private void Close()
    {
        if (!showing) return;
        showing = false;
        CurrentKind = null;
        closedFrame = Time.frameCount;
        if (root != null) root.SetActive(false);
        // 게임오버·승리 화면이 시간을 멈춰 둔 중이면 되돌리지 않는다(그 화면 뒤에서 게임이 다시 움직이지 않게).
        GameManager gm = GameManager.Instance;
        bool runEnded = gm != null && (gm.IsGameOver || gm.IsGameWon);
        if (Time.timeScale <= 0f && !runEnded) Time.timeScale = savedTimeScale;
    }

    private static bool DismissPressed()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (kb != null && kb.spaceKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    /// <summary>카드 크기·자리를 바로 다시 계산한다(테스트 캡처처럼 Canvas 배치가 바뀐 뒤 부른다).</summary>
    public void RefreshLayoutNow()
    {
        Canvas.ForceUpdateCanvases();
        if (showing && CurrentKind.HasValue) Fill(CurrentKind.Value, portrait.sprite);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>카드 판(배경 상자) - 테스트에서 자리·크기를 볼 때 쓴다.</summary>
    public RectTransform CardRect => card != null ? (RectTransform)card.transform : null;
}
