using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 1일차 튜토리얼 안내(Logs/Tutorial/notes.md의 9단계 표 - 단계와 넘어가는 조건은 그대로).
/// - 새 단계가 시작되면 게임을 멈춘다(Time.timeScale 0 - 마을 시간·해질녘도 멈춘다). 화면 전체를 어둡게 덮고 가운데에 큰 안내 카드와
///   "[Space] 또는 클릭으로 시작"을 띄운다. 그 단계에서 볼 대상(플레이어·동물·몬스터·트럭·수의사·소파 등 월드 대상, 인벤토리 칸 등 UI 대상)만
///   어둠 위로 밝게 남기고, 대상 모양을 그대로 따라가는 도트 1칸 노란 외곽선을 두른다(월드: 지금 그림 프레임의 투명하지 않은 부분,
///   UI: 그 Image 그림 모양). 화면 밖 대상은 화면 가장자리에 그쪽을 가리키는 노란 화살표. 멈춘 동안은 게임 입력을 막는다(GameplayInput.IsBlocked).
///   모양은 스텐실로 낸다(Resources/TutorialHint/TutorialSilhouette 셰이더) - 대상의 원래 그림·조명·재질은 건드리지 않는다.
/// - Space나 클릭을 누르면 게임이 다시 움직이고, 카드는 사라지고 작은 한 줄 알림만 남는다. 알림 자리는 다른 화면 UI(HUD·목표 표시·인벤토리·
///   트럭 상단 정보·마일리지·석상 정보 팝업 등)와 하단 자막 칸(상호작용 문구·구간 자막)에 겹치지 않는 후보 중 처음 것을 고른다(기본: 자막 줄 바로 위 가운데).
///   단계가 끝나면 잠깐 "완료!"(줍기는 "가방에 쏙!") 뒤 알림도 사라진다.
/// - 단계마다 "끝난 조건"을 늘 본다 - 안내가 뜨기 전에 이미 한 행동은 끝난 것으로 쳐서 건너뛴다(멈추지도 않는다).
/// - 1일차(CycleCount 1)에만 나오고, 게임 오버·클리어에서는 안 나온다. 일시정지 메뉴·소파 메뉴/확인 창·상점처럼 다른 화면이 시간을 멈추면 숨는다.
/// - 새 게임(ResetGame 뒤 시작 클래스 선택을 기다리는 상태, GameManager.NeedsStartingClassSelection)이 되면 처음부터 다시 나온다.
/// - 씬과 무관하게 하나만 있고(DontDestroyOnLoad) 자기 전용 오버레이 Canvas에 그린다. 게임이 시작될 때 스스로 만들어진다.
/// 문구 안의 [키]는 키 모양 칸으로 그린다. 그림은 HUD C 도트 조각(Resources/TutorialHint)과 외곽선 #2F2F2E를 쓴다.
/// </summary>
[DefaultExecutionOrder(-90)] // PauseMenu(-100) 다음, 게임플레이 스크립트보다 먼저 - 멈춤을 푸는 Space·클릭이 같은 프레임에 공격·도구로 새지 않게
public class TutorialGuide : MonoBehaviour
{
    public const int StepCount = 9;
    public const string DoneChipText = "완료!";
    public const string StartPromptText = "[Space] 또는 클릭으로 시작";

    private static readonly Vector2 ReferenceResolution = new Vector2(2240f, 1260f); // 씬 UI Canvas와 같은 기준
    private const int CanvasSortingOrder = 600;  // HUD(0)·자막(500) 위(멈춤 화면 어둠이 자막도 덮는다), 일시정지 메뉴(1000) 아래
    private const float PixelUnits = 4.67f;      // 도트 1칸 = 1080p 4px
    private const float CardTextSize = 50f, CardKeySize = 42f, CardChipSize = 30f, CardPromptSize = 32f, CardPromptKeySize = 28f;
    private const float NoticeTextSize = 28f, NoticeKeySize = 24f, NoticeChipSize = 22f;
    private const float LabelSize = 34f;
    private const int MaxOutlineSteps = 6;       // UI 외곽선: 도트 1칸(PixelUnits)을 그림 한 칸 이하 간격으로 나눠 민다
    private const int ArrowTexels = 15;          // 화면 밖 대상 화살표 그림 크기(도트)
    private const float ArrowDot = PixelUnits * 1.25f;
    private const float ArrowEdgeMargin = 40f;
    private const int StencilBit = 128;          // 대상 모양 표시용 스텐실 비트(UI Mask가 쓰는 아래 비트와 겹치지 않게 맨 위 비트)
    private const float MinIntroSeconds = 0.3f;  // 멈춘 직후 눌려 있던 키로 바로 넘어가지 않게
    private const float NoticeRecheckSeconds = 0.25f;
    private const float SubtitleTop = 300f;      // 하단 자막 칸 위 끝(기준 단위, SubtitleManager 212 + 칸 높이 88)
    private const float DoneFlashSeconds = 0.6f;
    private const float PickupToastSeconds = 2f;
    private const float Step3TimeoutSeconds = 8f;
    private const float Step5TimeoutSeconds = 10f;

    private static readonly Color Cream = new Color32(0xED, 0xE3, 0xD3, 0xFF);
    private static readonly Color Outline = new Color32(0x2F, 0x2F, 0x2E, 0xFF);
    /// <summary>강조 외곽선 색 - 눈에 확 띄는 밝은 노랑.</summary>
    public static readonly Color HighlightYellow = new Color32(0xFF, 0xE0, 0x2E, 0xFF);
    private static readonly Color DimColor = new Color(0.035f, 0.035f, 0.05f, 0.92f); // 선형 색공간이라 0.92여야 눈에 밝기 약 1/3로 보인다
    // 키 칸 글자: 공용 글꼴 머티리얼(검은 외곽선)을 그대로 쓰므로 인벤토리 칸 번호처럼 흰 글자로 둔다
    private static readonly Color KeyText = Color.white;

    private static TutorialGuide instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    public static TutorialGuide Instance => instance;

    /// <summary>참이면 단계 시작 멈춤 화면을 띄우지 않는다(작은 알림은 그대로). 게임에서는 늘 거짓 -
    /// 에디터 batch 테스트 중 튜토리얼 테스트가 아닌 것만 켠다(TutorialIntroTestSwitch). Play 진입 때 지우지 않는다.</summary>
    public static bool IntrosSuppressed;

    public static TutorialGuide EnsureInstance()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("TutorialGuide");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<TutorialGuide>();
        instance.Build();
        return instance;
    }

    /// <summary>튜토리얼이 게임을 멈춘 동안(그리고 멈춤을 푼 그 프레임) 게임 입력을 막는다 - GameplayInput.IsBlocked가 읽는다.</summary>
    public static bool BlocksInput => instance != null && (instance.introActive || instance.introEndFrame == Time.frameCount);

    // ---------------- 상태 ----------------
    private readonly bool[] done = new bool[StepCount + 1];
    private readonly bool[] introShown = new bool[StepCount + 1];
    private bool resetArmed;
    private string lastScene;
    private int sceneEnteredFrame;
    private bool visitedVillage;
    private bool villageEntrySet;
    private Vector2 villageStartPos;
    private Vector2 villageStartLook;
    private float villageTimer;
    private bool animalSeen;
    private bool monsterSeen;
    private MonsterHealth firstMonster;
    private float step5Timer;
    private float visionCheckTimer;
    private InventoryItemType prevSelectedType;
    private int flashStep;
    private string flashText;
    private float flashUntil;

    private bool introActive;
    private int introStep;
    private float introStartTime;
    private float savedTimeScale = 1f;
    private int introEndFrame = -1;
    private AnimalRescue introAnimal;

    /// <summary>지금 단계(1~9). 없으면 0. 끝난 단계의 "완료!" 표시 중에도 그 단계 번호다.</summary>
    public int CurrentStep { get; private set; }

    /// <summary>지금 문구([키] 표시 그대로). 없으면 null.</summary>
    public string CurrentText { get; private set; }

    /// <summary>단계 시작 멈춤(어둠 + 가운데 카드 + 노란 외곽선) 중인지.</summary>
    public bool IsIntroActive => introActive;

    /// <summary>멈춤 화면이 보이는지.</summary>
    public bool IsIntroShowing => introRoot != null && introRoot.activeSelf;

    /// <summary>다시 움직일 때의 작은 한 줄 알림이 보이는지.</summary>
    public bool IsNoticeShowing => notice != null && notice.activeSelf;

    /// <summary>안내(멈춤 화면이나 작은 알림)가 화면에 보이는지.</summary>
    public bool IsShowing => IsIntroShowing || IsNoticeShowing;

    public bool IsDone(int step) => step >= 1 && step <= StepCount && done[step];

    /// <summary>작은 알림이 놓인 자리 이름("자막 위" 등).</summary>
    public string NoticeSlot { get; private set; }

    /// <summary>지금 그리는 강조(대상 하나마다 모양·외곽선 조각·화면 밖 화살표·이름표).</summary>
    public IReadOnlyList<HighlightView> Highlights => views.Take(activeViews).ToList();
    public IReadOnlyList<string> HighlightLabels => views.Take(activeViews).Select(f => f.Label).ToList();
    public RectTransform CardRect => card != null ? (RectTransform)card.transform : null;
    public RectTransform NoticeRect => notice != null ? (RectTransform)notice.transform : null;

    // ---------------- 화면 ----------------
    private RectTransform canvasRect;
    private Sprite panelSprite, stepSprite, keySprite, arrowSprite;
    // 멈춤 화면: 대상 모양 표시(스텐실) → 어둠(표시 밖만) → 노란 외곽선(표시 밖만) → 표시 지우기 → 화살표·이름표 → 카드
    private GameObject introRoot;
    private RectTransform maskRoot, dimRoot, outlineRoot, clearRoot, labelRoot;
    private Image dim;
    private Material maskMat, dimMat, outlineMat, clearMat;
    private readonly List<Image> maskImages = new List<Image>(), outlineImages = new List<Image>(), clearImages = new List<Image>();
    private int usedMask, usedOutline, usedClear;
    private readonly List<HighlightView> views = new List<HighlightView>();
    private int activeViews;
    private GameObject card;
    private TMP_Text cardChip;
    private RectTransform cardLine, cardPrompt;
    private string cardBuilt;
    // 작은 알림
    private GameObject notice;
    private TMP_Text noticeChip;
    private RectTransform noticeLine;
    private string noticeBuilt;
    private float noticeRecheckAt;
    private int noticeSlotIndex = -1;

    /// <summary>강조 하나(대상 하나). Bounds는 이 Canvas 로컬 좌표(가운데 원점) - 모양과 외곽선(화면 밖이면 화살표)을 감싸는 자리.</summary>
    public class HighlightView
    {
        public string Label;
        public bool Offscreen;
        public Rect Bounds;
        /// <summary>외곽선 두께(이 Canvas 단위) - 월드 대상은 그림 한 칸, UI 대상은 도트 한 칸.</summary>
        public float DotUnits;
        /// <summary>대상 모양 조각(그림 알파 그대로, 스텐실에만 그린다 - 어둠이 빠지는 자리).</summary>
        public readonly List<Image> Shapes = new List<Image>();
        /// <summary>노란 외곽선 조각(모양을 위아래좌우로 민 실루엣 - 모양 밖에만 칠해진다).</summary>
        public readonly List<Image> Outlines = new List<Image>();
        /// <summary>모양을 딴 원본(월드: SpriteRenderer, UI: Graphic/RectTransform).</summary>
        public readonly List<Object> Sources = new List<Object>();
        public RectTransform Arrow;
        public RectTransform LabelRect;
        internal GameObject labelGo;
        internal TMP_Text labelText;
    }

    private struct Target
    {
        public GameObject world;
        public RectTransform[] ui;
        public string label;
    }

    private struct NoticeSlotDef
    {
        public string name;
        public Vector2 anchor;
        public Vector2 position;
    }

    // 작은 알림 후보 자리(앞에서부터, 다른 UI와 겹치지 않는 첫 자리). 자막 줄(하단 가운데, 인벤토리 바로 위) 바로 위가 기본이다.
    private static readonly NoticeSlotDef[] NoticeSlots =
    {
        new NoticeSlotDef { name = "자막 위", anchor = new Vector2(0.5f, 0f), position = new Vector2(0f, SubtitleTop + 22f) },
        new NoticeSlotDef { name = "위 가운데", anchor = new Vector2(0.5f, 1f), position = new Vector2(0f, -150f) },
        new NoticeSlotDef { name = "오른쪽 가운데", anchor = new Vector2(1f, 0.5f), position = new Vector2(-28f, 0f) },
        new NoticeSlotDef { name = "왼쪽 가운데", anchor = new Vector2(0f, 0.5f), position = new Vector2(28f, 0f) },
    };

    private void Build()
    {
        panelSprite = Resources.Load<Sprite>("TutorialHint/hint_panel");
        stepSprite = Resources.Load<Sprite>("TutorialHint/hint_step");
        keySprite = Resources.Load<Sprite>("TutorialHint/hint_key");
        arrowSprite = CreateArrowSprite();
        Shader silhouette = Resources.Load<Shader>("TutorialHint/TutorialSilhouette");
        if (silhouette == null) Debug.LogError("TutorialGuide: Resources/TutorialHint/TutorialSilhouette 셰이더가 없다 - 강조 외곽선을 그릴 수 없다.");
        else
        {
            const float always = (float)UnityEngine.Rendering.CompareFunction.Always, notEqual = (float)UnityEngine.Rendering.CompareFunction.NotEqual;
            const float replace = (float)UnityEngine.Rendering.StencilOp.Replace, keep = (float)UnityEngine.Rendering.StencilOp.Keep;
            maskMat = StencilMaterial(silhouette, "TutorialMask", always, replace, StencilBit, 0f);
            dimMat = StencilMaterial(silhouette, "TutorialDim", notEqual, keep, StencilBit, 15f);
            outlineMat = StencilMaterial(silhouette, "TutorialOutline", notEqual, keep, StencilBit, 15f);
            clearMat = StencilMaterial(silhouette, "TutorialMaskClear", always, replace, 0f, 0f);
        }

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        // 멈춤 화면: 대상 모양 표시 → 어둠(표시 밖) → 노란 외곽선(표시 밖) → 표시 지우기 → 화살표·이름표 → 가운데 카드
        introRoot = Child("TutorialIntro", transform);
        Stretch((RectTransform)introRoot.transform);
        maskRoot = StretchChild("Mask", introRoot.transform);
        dimRoot = StretchChild("Dim", introRoot.transform);
        dim = StretchChild("Shade", dimRoot).gameObject.AddComponent<Image>();
        dim.color = DimColor;
        dim.material = dimMat;
        dim.raycastTarget = false;
        outlineRoot = StretchChild("Outline", introRoot.transform);
        clearRoot = StretchChild("MaskClear", introRoot.transform);
        labelRoot = StretchChild("Highlights", introRoot.transform);

        card = Box("TutorialCard", introRoot.transform, panelSprite, new RectOffset(48, 48, 30, 34), 20f, true);
        RectTransform crt = (RectTransform)card.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        FitToContent(card);
        GameObject cchip = Box("Step", card.transform, stepSprite, new RectOffset(16, 16, 6, 6), 0f, false);
        cardChip = Label(cchip.transform, "", CardChipSize, Cream);
        cardLine = (RectTransform)Box("Line", card.transform, null, new RectOffset(0, 0, 0, 0), 10f, false).transform;
        cardPrompt = (RectTransform)Box("Prompt", card.transform, null, new RectOffset(0, 0, 0, 0), 8f, false).transform;
        BuildLine(cardPrompt, StartPromptText, CardPromptSize, CardPromptKeySize, HighlightYellow);
        introRoot.SetActive(false);

        // 작은 알림
        notice = Box("TutorialNotice", transform, panelSprite, new RectOffset(18, 22, 10, 10), 10f, false);
        FitToContent(notice);
        GameObject nchip = Box("Step", notice.transform, stepSprite, new RectOffset(12, 12, 4, 4), 0f, false);
        noticeChip = Label(nchip.transform, "", NoticeChipSize, Cream);
        noticeLine = (RectTransform)Box("Line", notice.transform, null, new RectOffset(0, 0, 0, 0), 6f, false).transform;
        notice.SetActive(false);
    }

    // 화면 밖 대상 화살표(오른쪽을 가리킴): 노란 세모 + 외곽선 #2F2F2E 1칸. 도트 그림이라 Point 필터.
    private static Sprite CreateArrowSprite()
    {
        const int n = ArrowTexels, mid = n / 2;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "TutorialArrow" };
        bool Inside(int x, int y) => x >= 2 && x <= n - 3 && Mathf.Abs(y - mid) <= (n - 3 - x) * 0.6f + 0.01f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++) for (int dx = -1; dx <= 1 && !edge; dx++) edge = Inside(x + dx, y + dy);
                tex.SetPixel(x, y, Inside(x, y) ? HighlightYellow : edge ? Outline : Color.clear);
            }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.name = "TutorialArrow";
        return s;
    }

    private static Material StencilMaterial(Shader shader, string name, float comp, float op, float refValue, float colorMask)
    {
        var m = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        m.SetFloat("_StencilComp", comp);
        m.SetFloat("_StencilOp", op);
        m.SetFloat("_Stencil", refValue);
        m.SetFloat("_StencilReadMask", StencilBit);
        m.SetFloat("_StencilWriteMask", StencilBit);
        m.SetFloat("_ColorMask", colorMask);
        m.SetFloat("_AlphaCutoff", 0.5f);
        return m;
    }

    private static RectTransform StretchChild(string name, Transform parent)
    {
        RectTransform rt = (RectTransform)Child(name, parent).transform;
        Stretch(rt);
        return rt;
    }

    private static GameObject Child(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void FitToContent(GameObject go)
    {
        ContentSizeFitter f = go.AddComponent<ContentSizeFitter>();
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private Image SlicedImage(GameObject go, Sprite sprite)
    {
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f / PixelUnits;
        img.raycastTarget = false;
        return img;
    }

    private GameObject Box(string name, Transform parent, Sprite sprite, RectOffset padding, float spacing, bool vertical)
    {
        GameObject go = Child(name, parent);
        if (sprite != null) SlicedImage(go, sprite);
        HorizontalOrVerticalLayoutGroup h = vertical ? go.AddComponent<VerticalLayoutGroup>() : (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>();
        h.padding = padding;
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return go;
    }

    private static TMP_Text Label(Transform parent, string text, float size, Color color)
    {
        GameObject go = Child("Text", parent);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        UIFont.Apply(t);
        t.fontSize = size;
        t.color = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Midline;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    // "[WASD] 걷기 · [Shift] 달리기" → 키 칸과 글자 조각으로 다시 만든다.
    private void BuildLine(RectTransform line, string text, float textSize, float keySize, Color color)
    {
        for (int i = line.childCount - 1; i >= 0; i--)
        {
            GameObject c = line.GetChild(i).gameObject;
            c.transform.SetParent(null);
            Destroy(c);
        }
        foreach (Match m in Regex.Matches(text, @"\[([^\]]+)\]|[^\[]+"))
        {
            if (m.Groups[1].Success)
            {
                GameObject cap = Box("Key", line, keySprite, new RectOffset(Mathf.RoundToInt(keySize * 0.42f), Mathf.RoundToInt(keySize * 0.42f), 4, 4), 0f, false);
                Label(cap.transform, m.Groups[1].Value, keySize, KeyText);
            }
            else if (m.Value.Trim().Length > 0)
            {
                Label(line, m.Value.Trim(), textSize, color);
            }
        }
    }

    // ---------------- 진행 ----------------
    private void ResetProgress()
    {
        for (int i = 0; i < done.Length; i++) { done[i] = false; introShown[i] = false; }
        visitedVillage = villageEntrySet = animalSeen = monsterSeen = false;
        firstMonster = null;
        villageTimer = step5Timer = 0f;
        flashStep = 0;
        flashText = null;
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        string scene = SceneManager.GetActiveScene().name;
        if (gm == null) { EndIntro(); Show(0, null, false); return; }

        // 새 게임: 시작 클래스 선택을 기다리는 상태가 되면 처음부터
        if (gm.NeedsStartingClassSelection)
        {
            if (!resetArmed) { ResetProgress(); resetArmed = true; }
        }
        else resetArmed = false;

        bool dayOne = gm.CycleCount == 1 && !gm.IsGameOver && !gm.IsGameWon;
        bool sceneChanged = scene != lastScene;
        // 끝난 조건을 씬 전환 처리보다 먼저 본다 - 씬이 바뀌는 프레임에 끝난 단계(예: 클래스를 고르자마자 트럭으로)의 "완료!"도 아래에서 같이 지운다
        if (dayOne) UpdateCompletion(scene, gm, sceneChanged);
        if (sceneChanged)
        {
            lastScene = scene;
            sceneEnteredFrame = Time.frameCount;
            // 씬이 바뀌면 멈춤 화면은 닫는다(시간은 PauseMenu가 씬 로드 때 되돌린다).
            introActive = false;
            if (dayOne) OnSceneEntered(scene, gm);
            // 이전 씬에서 끝난 단계의 "완료!"는 새 씬으로 가져오지 않는다
            flashStep = 0;
        }
        if (!dayOne) { EndIntro(); Show(0, null, false); return; }

        // 끝난 단계의 "완료!"(또는 "가방에 쏙!")을 잠깐 보여 준 뒤 다음 단계로
        if (flashStep > 0 && Time.unscaledTime < flashUntil)
        {
            Show(flashStep, flashText, true);
            if (introActive) EndIntro();
            return;
        }
        flashStep = 0;

        int next = 0;
        for (int s = 1; s <= StepCount && next == 0; s++)
            if (!done[s] && ShouldShow(s, scene, gm)) next = s;
        if (next == 5) step5Timer += Time.deltaTime;
        Show(next, next > 0 ? StepText(next, gm) : null, false);
        UpdateIntro();
    }

    private void UpdateIntro()
    {
        if (introActive)
        {
            if (CurrentStep != introStep) { EndIntro(); return; }
            if (Time.unscaledTime - introStartTime >= MinIntroSeconds && DismissPressed()) EndIntro();
            return;
        }
        int step = CurrentStep;
        if (step <= 0 || introShown[step]) return;
        if (IntrosSuppressed) { introShown[step] = true; return; } // 다른 기능 테스트: 멈춤 화면 없이 작은 알림만
        if (Time.frameCount - sceneEnteredFrame < 3) return; // 씬이 막 열린 프레임은 대상이 아직 자리를 못 잡았을 수 있다
        if (Time.timeScale <= 0f || PauseMenu.IsPaused || ShopMenuUI.IsOpen) return; // 다른 화면이 멈춰 둔 동안은 기다린다
        StartIntro(step);
    }

    private static bool DismissPressed()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (kb != null && kb.spaceKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    private void StartIntro(int step)
    {
        introShown[step] = true;
        introActive = true;
        introStep = step;
        introStartTime = Time.unscaledTime;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        introAnimal = step == 4 ? NearestVisibleAnimal() : null;
        cardBuilt = null;
    }

    private void EndIntro()
    {
        if (!introActive) return;
        introActive = false;
        introEndFrame = Time.frameCount;
        if (Time.timeScale <= 0f) Time.timeScale = savedTimeScale;
    }

    private void OnSceneEntered(string scene, GameManager gm)
    {
        if (scene == "VillageScene")
        {
            visitedVillage = true;
            villageEntrySet = false;
            Complete(2, null);
        }
        else if (scene == "TruckScene" && visitedVillage) Complete(6, null);
        else if (scene == "ShelterScene") Complete(8, null);
    }

    private void UpdateCompletion(string scene, GameManager gm, bool sceneChanged)
    {
        if (!gm.NeedsStartingClassSelection) Complete(1, null);
        if (gm.CarriedAnimalCount > 0 || gm.RescuedCount > 0) Complete(4, "가방에 쏙!");
        if (gm.RescuedCount > 0) Complete(7, null);
        // 마을에 막 들어온 프레임은 진입 처리(OnSceneEntered)가 먼저다 - 시작 위치·시선은 다음 프레임부터 잰다
        if (scene != "VillageScene" || sceneChanged) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        PlayerMovement move = player != null ? player.GetComponent<PlayerMovement>() : null;
        if (move != null && !done[3])
        {
            if (!villageEntrySet)
            {
                villageEntrySet = true;
                villageStartPos = player.transform.position;
                villageStartLook = move.LookDirection;
                villageTimer = 0f;
            }
            villageTimer += Time.deltaTime; // 멈춘 동안은 흐르지 않는다
            bool moved = Vector2.Distance(player.transform.position, villageStartPos) >= 2f;
            bool turned = Vector2.Angle(move.LookDirection, villageStartLook) >= 30f;
            if ((moved && turned) || villageTimer >= Step3TimeoutSeconds) Complete(3, null);
        }

        // 무기(Q 상태에서 클릭)나 지뢰·폭탄(그 칸을 고른 채 클릭)을 쓰면 5단계 끝 - 안내가 뜨기 전에 써도 끝난 것으로 친다.
        // 지뢰·폭탄은 클릭한 그 프레임에 칸이 비워질 수 있어(PlayerGadgetController가 먼저 돌 때) 직전 프레임에 고른 칸 종류로 본다.
        // 멈춤을 푸는 클릭은 GameplayInput.IsBlocked로 막혀 여기서 세지 않는다.
        Mouse mouse = GameplayInput.IsBlocked ? null : Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            bool weapon = gm.IsWeaponSelected && gm.EquippedWeapon != WeaponType.None;
            bool tool = prevSelectedType == InventoryItemType.Mine || prevSelectedType == InventoryItemType.Bomb;
            if (weapon || (!gm.IsWeaponSelected && tool)) Complete(5, null);
        }
        prevSelectedType = gm.IsWeaponSelected ? InventoryItemType.None : gm.GetSlot(gm.SelectedSlotIndex).Type;
        if (step5Timer >= Step5TimeoutSeconds) Complete(5, null);
        if (monsterSeen && !done[5] && gm.EquippedWeapon == WeaponType.None && ToolSlots(gm).Count == 0) Complete(5, null);

        // 시야에 처음 들어온 동물·몬스터(0.25초마다 확인)
        visionCheckTimer -= Time.unscaledDeltaTime;
        if (visionCheckTimer <= 0f)
        {
            visionCheckTimer = 0.25f;
            if (!animalSeen)
                animalSeen = FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude).Any(a => PlayerVision.IsVisibleToPlayer(a.transform.position));
            if (!monsterSeen)
            {
                firstMonster = MonsterHealth.Active.FirstOrDefault(m => m != null && !m.IsDead && m.gameObject.activeInHierarchy && PlayerVision.IsVisibleToPlayer(m.GamePosition));
                monsterSeen = firstMonster != null;
            }
        }
    }

    private void Complete(int step, string toast)
    {
        if (done[step]) return;
        done[step] = true;
        // 지금 보이던 단계가 끝났을 때만 "완료!"를 잠깐 보여 준다(안 보이던 단계는 조용히 건너뛴다)
        if (CurrentStep == step && (IsShowing || introActive))
        {
            flashStep = step;
            flashText = toast ?? CurrentText;
            flashUntil = Time.unscaledTime + (toast != null ? PickupToastSeconds : DoneFlashSeconds);
        }
    }

    private bool ShouldShow(int step, string scene, GameManager gm)
    {
        int carried = gm.CarriedAnimalCount;
        switch (step)
        {
            case 1: return scene == "ClassSelectScene" && gm.NeedsStartingClassSelection;
            case 2: return scene == "TruckScene";
            case 3: return scene == "VillageScene";
            case 4: return scene == "VillageScene" && animalSeen;
            case 5: return scene == "VillageScene" && monsterSeen;
            case 6: return scene == "VillageScene" && carried > 0 && (!gm.HasInventorySpace || carried >= gm.TargetCount - gm.RescuedCount);
            case 7: return scene == "TruckScene" && carried > 0;
            case 8: return scene == "TruckScene" && gm.RescuedCount >= gm.TargetCount;
            case 9: return scene == "ShelterScene";
        }
        return false;
    }

    /// <summary>단계 문구. [키]는 키 모양 칸으로 그린다.</summary>
    public static string StepText(int step, GameManager gm)
    {
        switch (step)
        {
            case 1: return "석상 앞에서 [E]로 직업을 골라요. 그냥 출발하면 스카우트!";
            case 2: return $"오늘 할 일: 동물 {gm.TargetCount}마리를 트럭에 맡기기. 문 앞에서 [E]로 출발!";
            case 3: return "[WASD] 걷기 · 마우스로 앞을 비추기 · [Shift] 달리기";
            case 4: return "반짝이는 눈망울 발견! 다가가서 [E]로 구조해요";
            case 5:
                if (gm.EquippedWeapon != WeaponType.None) return "[Q]로 무기를 들고 클릭!";
                List<int> slots = ToolSlots(gm);
                string keys = slots.Count == 0 ? "[1]" : IsRange(slots) && slots.Count > 2 ? $"[{slots[0] + 1}]~[{slots[slots.Count - 1] + 1}]" : string.Join("·", slots.Select(i => $"[{i + 1}]"));
                return $"{keys}로 지뢰·폭탄을 고르고 클릭!";
            case 6:
                return gm.HasInventorySpace ? "다 모았어요! 트럭으로 돌아가 [E]로 타요" : "가방이 찼어요! 트럭으로 돌아가 [E] (필요 없으면 [G]로 내려놓기)";
            case 7: return "수의사에게 [E]로 모두 맡기기";
            case 8: return "목표 달성! 소파에서 [E] → [다음 날로]";
            case 9: return "정산 끝! 상점은 자유. 출구에서 [E]로 다음 날";
        }
        return null;
    }

    /// <summary>강조 대상 이름표에 쓰는 글자들(글꼴 검사용).</summary>
    public static readonly string[] HighlightLabelTexts = { "나", "동물", "몬스터", "무기 칸", "지뢰·폭탄 칸", "트럭", "수의사", "소파", "출구", "문", "오늘 목표" };

    private static List<int> ToolSlots(GameManager gm) =>
        Enumerable.Range(0, gm.InventorySlotCount).Where(i => gm.GetSlot(i).Type == InventoryItemType.Mine || gm.GetSlot(i).Type == InventoryItemType.Bomb).ToList();

    private static bool IsRange(List<int> s) => s.Count > 0 && s[s.Count - 1] - s[0] == s.Count - 1;

    private void Show(int step, string text, bool doneMark)
    {
        CurrentStep = text != null ? step : 0;
        CurrentText = text;
        showingDone = doneMark;
    }

    private bool showingDone;

    // ---------------- 강조 대상 ----------------
    private static AnimalRescue NearestVisibleAnimal()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return null;
        Vector2 p = player.transform.position;
        return FindObjectsByType<AnimalRescue>(FindObjectsInactive.Exclude)
            .Where(a => PlayerVision.IsVisibleToPlayer(a.transform.position))
            .OrderBy(a => Vector2.Distance(a.transform.position, p)).FirstOrDefault();
    }

    private void CollectTargets(int step, List<Target> list)
    {
        GameManager gm = GameManager.Instance;
        switch (step)
        {
            case 1:
                foreach (ClassInteractPoint cp in FindObjectsByType<ClassInteractPoint>(FindObjectsInactive.Exclude).OrderBy(c => c.transform.position.x))
                    list.Add(new Target { world = cp.gameObject });
                break;
            case 2:
                AddWorld(list, FindAnyObjectByType<TruckStartPoint>(), "문");
                GameObject top = GameObject.Find("TopInfoPanel");
                if (top != null && top.transform is RectTransform trt) list.Add(new Target { ui = new[] { trt }, label = "오늘 목표" });
                break;
            case 3:
                list.Add(new Target { world = GameObject.FindGameObjectWithTag("Player"), label = "나" });
                break;
            case 4:
                if (introAnimal == null) introAnimal = NearestVisibleAnimal();
                if (introAnimal != null) list.Add(new Target { world = introAnimal.gameObject, label = "동물" });
                break;
            case 5:
                if (firstMonster != null && firstMonster.gameObject.activeInHierarchy) list.Add(new Target { world = firstMonster.gameObject, label = "몬스터" });
                InventoryUI inv = FindAnyObjectByType<InventoryUI>();
                if (inv != null && gm != null)
                {
                    if (gm.EquippedWeapon != WeaponType.None)
                    {
                        if (inv.WeaponSlotRect != null) list.Add(new Target { ui = new[] { inv.WeaponSlotRect }, label = "무기 칸" });
                    }
                    else
                    {
                        RectTransform[] tools = ToolSlots(gm).Select(inv.SlotRect).Where(r => r != null).ToArray();
                        if (tools.Length > 0) list.Add(new Target { ui = tools, label = "지뢰·폭탄 칸" });
                    }
                }
                break;
            case 6:
                GameObject truck = GameObject.Find("TruckPoint");
                if (truck != null) list.Add(new Target { world = truck, label = "트럭" });
                break;
            case 7: AddWorld(list, FindAnyObjectByType<AnimalDeliveryPoint>(), "수의사"); break;
            case 8:
                // 소파 그림(TruckScene "Sofa")을 감싼다. 없으면 소파 상호작용 지점.
                GameObject sofa = GameObject.Find("Sofa");
                if (sofa != null && sofa.GetComponentInChildren<SpriteRenderer>() != null) list.Add(new Target { world = sofa, label = "소파" });
                else AddWorld(list, FindAnyObjectByType<TruckHubPoint>(), "소파");
                break;
            case 9: AddWorld(list, FindAnyObjectByType<ShelterExitPoint>(), "출구"); break;
        }
    }

    private static void AddWorld(List<Target> list, Component c, string label)
    {
        if (c != null) list.Add(new Target { world = c.gameObject, label = label });
    }

    /// <summary>화면 UI(RectTransform)의 화면 영역(뷰포트 0~1). 오버레이·카메라 모드 Canvas 모두.</summary>
    public static bool UiViewportRect(RectTransform rt, out Rect vr)
    {
        vr = default;
        if (rt == null) return false;
        Canvas c = rt.GetComponentInParent<Canvas>();
        if (c == null) return false;
        c = c.rootCanvas;
        Camera cam = c.renderMode == RenderMode.ScreenSpaceOverlay ? null : (c.worldCamera != null ? c.worldCamera : Camera.main);
        if (c.renderMode != RenderMode.ScreenSpaceOverlay && cam == null) return false;
        float w = cam == null ? Screen.width : cam.pixelWidth, h = cam == null ? Screen.height : cam.pixelHeight;
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 p in corners)
        {
            Vector2 s = cam == null ? (Vector2)p : (Vector2)cam.WorldToScreenPoint(p);
            min = Vector2.Min(min, s); max = Vector2.Max(max, s);
        }
        vr = Rect.MinMaxRect(min.x / w, min.y / h, max.x / w, max.y / h);
        return true;
    }

    // ---------------- 그리기 ----------------
    // 보일지 말지는 모든 Update가 끝난 뒤 정한다 - 같은 프레임에 메뉴가 열려 시간이 멈춰도(소파·상점 메뉴, 일시정지) 그 화면을 가리지 않게.
    private void LateUpdate()
    {
        if (introRoot == null) return;
        bool otherScreen = PauseMenu.IsPaused || ShopMenuUI.IsOpen || (!introActive && Time.timeScale <= 0f);
        bool showIntro = introActive && !otherScreen;
        bool showNotice = !introActive && CurrentText != null && !otherScreen;
        if (introRoot.activeSelf != showIntro) introRoot.SetActive(showIntro);
        if (notice.activeSelf != showNotice) { notice.SetActive(showNotice); noticeRecheckAt = 0f; }
        if (showIntro) LayoutIntro();
        if (showNotice) LayoutNotice(false);
    }

    /// <summary>지금 화면 배치(강조 모양·외곽선·카드, 작은 알림 자리)를 바로 다시 계산한다. 테스트 캡처처럼 Canvas 배치가 바뀐 뒤 부른다.</summary>
    public void RefreshLayoutNow()
    {
        Canvas.ForceUpdateCanvases();
        if (introRoot != null && introRoot.activeSelf) LayoutIntro();
        if (notice != null && notice.activeSelf) LayoutNotice(true);
        Canvas.ForceUpdateCanvases();
    }

    private Vector2 CanvasSize => canvasRect.rect.size;

    // 뷰포트(0~1) → 이 Canvas의 로컬 좌표(가운데 원점)
    private Rect ToLocal(Rect vr)
    {
        Vector2 size = CanvasSize;
        return Rect.MinMaxRect((vr.xMin - 0.5f) * size.x, (vr.yMin - 0.5f) * size.y, (vr.xMax - 0.5f) * size.x, (vr.yMax - 0.5f) * size.y);
    }

    private static Rect Expand(Rect r, float d) => Rect.MinMaxRect(r.xMin - d, r.yMin - d, r.xMax + d, r.yMax + d);

    private readonly List<Target> targetBuffer = new List<Target>();
    private readonly List<Rect> holeBuffer = new List<Rect>();

    private void LayoutIntro()
    {
        GameManager gm = GameManager.Instance;
        // 카드 글자(단계 문구가 바뀔 때만 다시 만든다)
        string key = $"{introStep}|{CurrentText}";
        if (cardBuilt != key && CurrentText != null)
        {
            cardBuilt = key;
            cardChip.text = $"안내 {introStep}/{StepCount}";
            BuildLine(cardLine, CurrentText, CardTextSize, CardKeySize, Cream);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);
        }

        // 대상마다: 모양(스텐실 표시) + 노란 외곽선, 화면 밖이면 가장자리 화살표. 이름표는 그 아래(모자라면 위).
        targetBuffer.Clear();
        CollectTargets(introStep, targetBuffer);
        Camera cam = Camera.main;
        Vector2 size = CanvasSize;
        Rect screen = Rect.MinMaxRect(-size.x * 0.5f, -size.y * 0.5f, size.x * 0.5f, size.y * 0.5f);
        holeBuffer.Clear();
        activeViews = usedMask = usedOutline = usedClear = 0;
        foreach (Target t in targetBuffer)
        {
            HighlightView hv = ViewAt(activeViews);
            hv.Shapes.Clear(); hv.Outlines.Clear(); hv.Sources.Clear();
            hv.Label = t.label;
            bool ok = t.ui != null ? BuildUiShapes(hv, t.ui) : BuildWorldShapes(hv, t.world, cam);
            if (!ok) continue;
            activeViews++;
            hv.Offscreen = !hv.Bounds.Overlaps(screen);
            if (hv.Offscreen)
            {
                // 화면 밖: 모양 조각은 치우고, 화면 가운데에서 대상 쪽으로 그은 선이 화면 가장자리와 만나는 자리에 그쪽을 가리키는 화살표
                ReleaseShapes(hv);
                Vector2 d = hv.Bounds.center;
                float arrowSize = ArrowTexels * ArrowDot;
                Vector2 half = screen.size * 0.5f - Vector2.one * (arrowSize * 0.5f + ArrowEdgeMargin);
                float k = Mathf.Min(Mathf.Abs(d.x) > 0.01f ? half.x / Mathf.Abs(d.x) : float.MaxValue, Mathf.Abs(d.y) > 0.01f ? half.y / Mathf.Abs(d.y) : float.MaxValue);
                Vector2 c = d * Mathf.Min(1f, k);
                RectTransform a = ArrowOf(hv);
                a.anchorMin = a.anchorMax = a.pivot = new Vector2(0.5f, 0.5f);
                a.anchoredPosition = c;
                a.sizeDelta = new Vector2(arrowSize, arrowSize);
                a.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                a.gameObject.SetActive(true);
                hv.Bounds = new Rect(c - Vector2.one * arrowSize * 0.5f, Vector2.one * arrowSize);
            }
            else if (hv.Arrow != null) hv.Arrow.gameObject.SetActive(false);

            Rect outer = hv.Bounds;
            if (!string.IsNullOrEmpty(t.label))
            {
                if (hv.labelText.text != t.label) hv.labelText.text = t.label;
                hv.labelGo.SetActive(true);
                RectTransform lrt = hv.LabelRect;
                LayoutRebuilder.ForceRebuildLayoutImmediate(lrt);
                Vector2 ls = lrt.rect.size;
                bool below = outer.yMin - ls.y - 8f > screen.yMin + 8f;
                float y = below ? outer.yMin - 8f - ls.y : outer.yMax + 8f;
                float x = Mathf.Clamp(outer.center.x - ls.x * 0.5f, screen.xMin + 8f, screen.xMax - 8f - ls.x);
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = Vector2.zero;
                lrt.anchoredPosition = new Vector2(x, y);
                holeBuffer.Add(Rect.MinMaxRect(Mathf.Min(outer.xMin, x), Mathf.Min(outer.yMin, y), Mathf.Max(outer.xMax, x + ls.x), Mathf.Max(outer.yMax, y + ls.y)));
            }
            else
            {
                hv.labelGo.SetActive(false);
                holeBuffer.Add(outer);
            }
        }
        for (int i = activeViews; i < views.Count; i++)
        {
            views[i].labelGo.SetActive(false);
            if (views[i].Arrow != null) views[i].Arrow.gameObject.SetActive(false);
            views[i].Shapes.Clear(); views[i].Outlines.Clear(); views[i].Sources.Clear();
        }
        HideUnused(maskImages, usedMask);
        HideUnused(outlineImages, usedOutline);
        HideUnused(clearImages, usedClear);

        // 카드: 가운데. 강조(모양·외곽선·화살표·이름표)와 겹치면 위쪽 → 아래쪽으로 비킨다. 강조를 절대 가리지 않는 것이 먼저이고,
        // 그다음으로 다른 화면 UI(HUD·인벤토리·자막 칸 등)도 되도록 피한다(가장 덜 겹치는 자리).
        var otherUi = OtherUiViewportRects().Select(ToLocal).ToList();
        RectTransform crt = (RectTransform)card.transform;
        Vector2 cs = crt.rect.size;
        float[] offsets = { 0f, size.y * 0.27f, -size.y * 0.27f, size.y * 0.36f, -size.y * 0.36f };
        float bestArea = float.MaxValue; float bestY = 0f;
        foreach (float oy in offsets)
        {
            Rect cr = new Rect(new Vector2(-cs.x * 0.5f, oy - cs.y * 0.5f), cs);
            if (cr.yMin < screen.yMin + 10f || cr.yMax > screen.yMax - 10f) continue;
            Rect padded = Expand(cr, 12f);
            float area = holeBuffer.Sum(h => OverlapArea(h, padded)) * 1000f + otherUi.Sum(h => OverlapArea(h, padded));
            if (area < bestArea - 0.5f) { bestArea = area; bestY = oy; }
            if (area <= 0f) break;
        }
        crt.anchoredPosition = new Vector2(0f, bestY);
    }

    private static float OverlapArea(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0f && h > 0f ? w * h : 0f;
    }

    // 월드 대상: 켜진 스프라이트(바닥 그림자 제외)마다 지금 그림(애니메이션 프레임)·뒤집기·회전·크기 그대로 모양을 딴다.
    // 외곽선 두께 = 그 그림의 한 칸(도트 1칸). 스프라이트가 없으면 충돌체(없으면 위치 둘레 1.5칸) 네모.
    private bool BuildWorldShapes(HighlightView hv, GameObject go, Camera cam)
    {
        if (go == null || cam == null) return false;
        bool any = false;
        Rect bounds = default;
        hv.DotUnits = 0f;
        foreach (SpriteRenderer r in go.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!r.enabled || r.sprite == null || !r.gameObject.activeInHierarchy || r.color.a <= 0.01f) continue;
            if (r.name.StartsWith("Shadow")) continue; // 바닥 그림자는 대상 모양이 아니다
            if (r.drawMode != SpriteDrawMode.Simple) continue;
            Sprite s = r.sprite;
            Rect sr = s.rect;
            float ppu = s.pixelsPerUnit;
            Vector2 piv = s.pivot;
            Vector2 L(float tx, float ty)
            {
                float lx = (tx - piv.x) / ppu, ly = (ty - piv.y) / ppu;
                if (r.flipX) lx = -lx;
                if (r.flipY) ly = -ly;
                return WorldToLocal(cam, r.transform.TransformPoint(new Vector3(lx, ly, 0f)));
            }
            Vector2 p00 = L(0f, 0f), u = L(sr.width, 0f) - p00, v = L(0f, sr.height) - p00;
            Vector2 du = u / sr.width, dv = v / sr.height;
            AddShape(hv, s, null, 1f, p00, u, v, du, dv, 1);
            hv.Sources.Add(r);
            float dot = Mathf.Max(du.magnitude, dv.magnitude);
            hv.DotUnits = Mathf.Max(hv.DotUnits, dot);
            Rect qb = Expand(QuadBounds(p00, u, v), dot);
            bounds = any ? Union(bounds, qb) : qb;
            any = true;
        }
        if (!any)
        {
            Bounds b = default; bool col = false;
            foreach (Collider2D c in go.GetComponentsInChildren<Collider2D>())
            {
                if (!c.enabled) continue;
                if (!col) { b = c.bounds; col = true; } else b.Encapsulate(c.bounds);
            }
            if (!col) b = new Bounds(go.transform.position, new Vector3(1.5f, 1.5f, 0f));
            Vector2 p00 = WorldToLocal(cam, new Vector3(b.min.x, b.min.y, go.transform.position.z));
            Vector2 p11 = WorldToLocal(cam, new Vector3(b.max.x, b.max.y, go.transform.position.z));
            Vector2 u = new Vector2(p11.x - p00.x, 0f), v = new Vector2(0f, p11.y - p00.y);
            AddShape(hv, null, null, 1f, p00, u, v, new Vector2(PixelUnits, 0f), new Vector2(0f, PixelUnits), 1);
            hv.Sources.Add(go.transform);
            hv.DotUnits = PixelUnits;
            bounds = Expand(QuadBounds(p00, u, v), PixelUnits);
        }
        hv.Bounds = bounds;
        return true;
    }

    // UI 대상: 그 RectTransform의 Image 모양(스프라이트 알파 - 둥근 모서리 그대로, 9조각이면 9조각 그대로). 외곽선 두께 = 도트 1칸.
    private bool BuildUiShapes(HighlightView hv, RectTransform[] rts)
    {
        bool any = false;
        Rect bounds = default;
        hv.DotUnits = PixelUnits;
        Canvas mine = GetComponent<Canvas>();
        foreach (RectTransform rt in rts)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) continue;
            Canvas root = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            if (root == null) continue;
            if (!UiCorners(rt, root, out Vector2 p00, out Vector2 p10, out Vector2 p01)) continue;
            Vector2 u = p10 - p00, v = p01 - p00;
            Image src = rt.GetComponent<Image>();
            if (src != null && !src.enabled) src = null;
            Sprite s = src != null ? src.sprite : null;
            // 그림 한 칸의 크기(이 Canvas 단위): 외곽선은 도트 1칸(PixelUnits)을 그림 한 칸 이하 간격으로 나눠 밀어 빈틈이 없게 한다
            float texel = PixelUnits, ppuMult = 1f;
            if (s != null && src.type == Image.Type.Simple) texel = Mathf.Min(u.magnitude / s.rect.width, v.magnitude / s.rect.height);
            else if (s != null && src.hasBorder && src.type != Image.Type.Simple)
            {
                float srcScale = root.scaleFactor, myScale = mine.scaleFactor;
                float copyPpu = src.pixelsPerUnit * myScale / srcScale;
                ppuMult = copyPpu * mine.referencePixelsPerUnit / s.pixelsPerUnit;
                texel = 1f / copyPpu;
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(PixelUnits / Mathf.Max(0.01f, texel) - 0.01f), 1, MaxOutlineSteps);
            float step = PixelUnits / steps;
            AddShape(hv, s, src, ppuMult, p00, u, v, u.normalized * step, v.normalized * step, steps);
            hv.Sources.Add(src != null ? (Object)src : rt);
            Rect qb = Expand(QuadBounds(p00, u, v), PixelUnits);
            bounds = any ? Union(bounds, qb) : qb;
            any = true;
        }
        hv.Bounds = bounds;
        return any;
    }

    // 모양 한 조각: 스텐실 표시용 + 표시 지우기용 + 노란 외곽선(모양을 du·dv 방향으로 1~steps번 민 실루엣, 모양 밖에만 칠해진다).
    private void AddShape(HighlightView hv, Sprite sprite, Image src, float ppuMult, Vector2 p00, Vector2 u, Vector2 v, Vector2 du, Vector2 dv, int steps)
    {
        Image m = PoolImage(maskImages, maskRoot, maskMat, ref usedMask);
        CopyLook(m, sprite, src, ppuMult);
        PlaceQuad(m.rectTransform, p00, u, v);
        hv.Shapes.Add(m);
        Image c = PoolImage(clearImages, clearRoot, clearMat, ref usedClear);
        CopyLook(c, sprite, src, ppuMult);
        PlaceQuad(c.rectTransform, p00, u, v);
        for (int k = 1; k <= steps; k++)
        {
            Vector2 a = du * k, b = dv * k;
            Vector2[] offs = { a, -a, b, -b };
            foreach (Vector2 off in offs)
            {
                Image o = PoolImage(outlineImages, outlineRoot, outlineMat, ref usedOutline);
                CopyLook(o, sprite, src, ppuMult);
                o.color = HighlightYellow;
                PlaceQuad(o.rectTransform, p00 + off, u, v);
                hv.Outlines.Add(o);
            }
        }
    }

    // 화면 밖 화살표로 바꿀 때 이 강조의 모양 조각은 그리지 않는다(풀에서 뒤로 돌려놓는 대신 끈다).
    private static void ReleaseShapes(HighlightView hv)
    {
        foreach (Image i in hv.Shapes) i.enabled = false;
        foreach (Image i in hv.Outlines) i.enabled = false;
        hv.Shapes.Clear();
        hv.Outlines.Clear();
    }

    private static void CopyLook(Image dst, Sprite sprite, Image src, float ppuMult)
    {
        dst.enabled = true;
        dst.sprite = sprite;
        dst.color = Color.white;
        if (src != null && sprite != null)
        {
            dst.type = src.type;
            dst.preserveAspect = src.preserveAspect;
            dst.fillCenter = src.fillCenter;
            dst.fillMethod = src.fillMethod;
            dst.fillAmount = src.fillAmount;
            dst.fillOrigin = src.fillOrigin;
            dst.fillClockwise = src.fillClockwise;
            dst.pixelsPerUnitMultiplier = ppuMult;
        }
        else
        {
            dst.type = Image.Type.Simple;
            dst.preserveAspect = false;
            dst.pixelsPerUnitMultiplier = 1f;
        }
    }

    // p00(그림 왼쪽 아래) · u(그림 가로 방향 끝까지) · v(세로) 사각형에 맞춘다 - 회전·뒤집기 포함.
    private static void PlaceQuad(RectTransform rt, Vector2 p00, Vector2 u, Vector2 v)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = p00 + (u + v) * 0.5f;
        rt.sizeDelta = new Vector2(u.magnitude, v.magnitude);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg);
        rt.localScale = new Vector3(1f, u.x * v.y - u.y * v.x < 0f ? -1f : 1f, 1f);
    }

    private static Rect QuadBounds(Vector2 p00, Vector2 u, Vector2 v)
    {
        Vector2 p10 = p00 + u, p01 = p00 + v, p11 = p00 + u + v;
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(p00.x, p10.x), Mathf.Min(p01.x, p11.x)), Mathf.Min(Mathf.Min(p00.y, p10.y), Mathf.Min(p01.y, p11.y)),
                               Mathf.Max(Mathf.Max(p00.x, p10.x), Mathf.Max(p01.x, p11.x)), Mathf.Max(Mathf.Max(p00.y, p10.y), Mathf.Max(p01.y, p11.y)));
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // 월드 좌표 → 이 Canvas 로컬(가운데 원점)
    private Vector2 WorldToLocal(Camera cam, Vector3 world)
    {
        Vector3 vp = cam.WorldToViewportPoint(world);
        Vector2 size = CanvasSize;
        return new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
    }

    // 다른 Canvas의 UI 네 모서리 → 이 Canvas 로컬(왼쪽 아래 · 오른쪽 아래 · 왼쪽 위)
    private bool UiCorners(RectTransform rt, Canvas root, out Vector2 p00, out Vector2 p10, out Vector2 p01)
    {
        p00 = p10 = p01 = default;
        Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (root.worldCamera != null ? root.worldCamera : Camera.main);
        if (root.renderMode != RenderMode.ScreenSpaceOverlay && cam == null) return false;
        float w = cam == null ? Screen.width : cam.pixelWidth, h = cam == null ? Screen.height : cam.pixelHeight;
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 size = CanvasSize;
        Vector2 Conv(Vector3 p)
        {
            Vector2 s = cam == null ? (Vector2)p : (Vector2)cam.WorldToScreenPoint(p);
            return new Vector2((s.x / w - 0.5f) * size.x, (s.y / h - 0.5f) * size.y);
        }
        p00 = Conv(c[0]); p01 = Conv(c[1]); p10 = Conv(c[3]);
        return true;
    }

    private static Image PoolImage(List<Image> pool, RectTransform parent, Material mat, ref int used)
    {
        if (pool.Count <= used)
        {
            GameObject go = Child("Piece", parent);
            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.material = mat;
            pool.Add(img);
        }
        Image i = pool[used++];
        if (!i.gameObject.activeSelf) i.gameObject.SetActive(true);
        return i;
    }

    private static void HideUnused(List<Image> pool, int used)
    {
        for (int i = used; i < pool.Count; i++) if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
    }

    private HighlightView ViewAt(int i)
    {
        while (views.Count <= i)
        {
            GameObject label = Box("Label", labelRoot, stepSprite, new RectOffset(12, 12, 4, 4), 0f, false);
            FitToContent(label);
            TMP_Text t = Label(label.transform, "", LabelSize, Cream);
            views.Add(new HighlightView { labelGo = label, labelText = t, LabelRect = (RectTransform)label.transform });
        }
        return views[i];
    }

    private RectTransform ArrowOf(HighlightView hv)
    {
        if (hv.Arrow == null)
        {
            GameObject go = Child("Arrow", labelRoot);
            Image img = go.AddComponent<Image>();
            img.sprite = arrowSprite;
            img.raycastTarget = false;
            hv.Arrow = (RectTransform)go.transform;
            hv.Arrow.SetAsFirstSibling();
        }
        return hv.Arrow;
    }

    private void LayoutNotice(bool force)
    {
        string key = $"{showingDone}|{CurrentStep}|{CurrentText}";
        if (noticeBuilt != key)
        {
            noticeBuilt = key;
            noticeChip.text = showingDone ? DoneChipText : $"안내 {CurrentStep}/{StepCount}";
            BuildLine(noticeLine, CurrentText, NoticeTextSize, NoticeKeySize, Cream);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)notice.transform);
            force = true;
        }
        if (!force && Time.unscaledTime < noticeRecheckAt) return;
        noticeRecheckAt = Time.unscaledTime + NoticeRecheckSeconds;

        List<Rect> blocked = OtherUiViewportRects();

        RectTransform nrt = (RectTransform)notice.transform;
        int pick = -1;
        // 지금 자리가 여전히 비어 있으면 그대로(자리가 자꾸 바뀌지 않게), 아니면 앞에서부터 빈 자리
        if (noticeSlotIndex >= 0 && SlotFree(noticeSlotIndex, nrt, blocked)) pick = noticeSlotIndex;
        for (int i = 0; i < NoticeSlots.Length && pick < 0; i++) if (SlotFree(i, nrt, blocked)) pick = i;
        if (pick < 0) pick = 0;
        noticeSlotIndex = pick;
        ApplySlot(nrt, NoticeSlots[pick]);
        NoticeSlot = NoticeSlots[pick].name;
    }

    // 다른 화면 UI(이 Canvas 제외, 그려지는 그래픽)와 하단 자막 칸(글자가 없어도 그 줄)의 자리(뷰포트 0~1).
    private List<Rect> OtherUiViewportRects()
    {
        var blocked = new List<Rect>();
        foreach (Graphic g in FindObjectsByType<Graphic>(FindObjectsInactive.Exclude))
        {
            if (g == null || !g.enabled || g.canvas == null || g.color.a <= 0.01f) continue;
            Canvas root = g.canvas.rootCanvas;
            if (root == null || root.transform == transform || root.renderMode == RenderMode.WorldSpace) continue;
            if (g is TMP_Text tt && string.IsNullOrWhiteSpace(tt.text)) continue;
            if (!UiViewportRect(g.rectTransform, out Rect vr)) continue;
            if (vr.width * vr.height > 0.5f) continue; // 화면을 덮는 배경·페이드는 자리 판단에서 뺀다
            blocked.Add(vr);
        }
        SubtitleManager subtitle = FindAnyObjectByType<SubtitleManager>();
        if (subtitle != null)
            foreach (TMP_Text t in subtitle.GetComponentsInChildren<TMP_Text>(true))
                if (UiViewportRect(t.rectTransform, out Rect sr)) blocked.Add(sr);
        return blocked;
    }

    private static void ApplySlot(RectTransform rt, NoticeSlotDef slot)
    {
        rt.anchorMin = rt.anchorMax = slot.anchor;
        rt.pivot = slot.anchor;
        rt.anchoredPosition = slot.position;
    }

    private bool SlotFree(int i, RectTransform nrt, List<Rect> blocked)
    {
        ApplySlot(nrt, NoticeSlots[i]);
        if (!UiViewportRect(nrt, out Rect vr)) return true;
        Vector2 margin = new Vector2(12f / CanvasSize.x, 12f / CanvasSize.y);
        Rect padded = Rect.MinMaxRect(vr.xMin - margin.x, vr.yMin - margin.y, vr.xMax + margin.x, vr.yMax + margin.y);
        if (padded.xMin < 0f || padded.xMax > 1f || padded.yMin < 0f || padded.yMax > 1f) return false;
        return !blocked.Any(b => b.Overlaps(padded));
    }
}
