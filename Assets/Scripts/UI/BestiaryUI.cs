using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 구조 도감(시안: Logs/Tutorial/notes.md 7장, Logs/Tutorial/bestiary_truck.png).
/// - 여는 곳: 트럭·보호소에서 [Tab]. 마을에서는 열지 않는다(멈추면 위험). 트럭·보호소 오른쪽 위에 "[Tab] 도감" 작은 안내를 둔다.
/// - 여는 동안 게임을 멈추고(Time.timeScale 0) 게임 입력을 막는다(GameplayInput.IsBlocked, 닫은 프레임 포함 - 닫는 E가 상호작용으로 새지 않게).
///   닫기 [Tab]·[E]·[Esc], [←]/[→]로 넘기기, 마우스로 칸 클릭.
/// - 화면: 왼쪽 목록(동물 5칸, 몬스터 5칸), 오른쪽 상세, 위에 "만난 대상 n/10".
/// - 만나지 않은 대상: 그림은 실루엣, 이름 "???", 상세는 "아직 만나지 않았어요."만. 만남 = 마을에서 처음 플레이어 시야에 보임(첫 만남 카드와 같은 판정).
///   새 게임(GameManager.NeedsStartingClassSelection)이 되면 기록을 비운다(첫 만남 카드와 같은 때).
/// - 글: 몬스터는 MonsterBestiary(첫 만남 카드와 같은 글), 동물은 AnimalBestiary. 그림은 Resources/Bestiary/BestiaryArt의 프리팹에서 꺼낸다.
/// - 모양은 튜토리얼·첫 만남 카드와 같다(같은 판 그림·어둠·글꼴·키 칸, 도트 1칸 = 4.67 기준 단위). 새로 만드는 판 그림도 Point 필터·압축 없음.
/// - 씬과 무관하게 하나만 있고(DontDestroyOnLoad) 자기 전용 오버레이 Canvas에 그린다. 게임이 시작될 때 스스로 만들어진다.
/// </summary>
[DefaultExecutionOrder(-88)] // 첫 만남 카드(-89) 다음, 게임플레이 스크립트보다 먼저 - 닫는 E가 같은 프레임에 상호작용으로 새지 않게
public class BestiaryUI : MonoBehaviour
{
    public const string TitleText = "구조 도감";
    public const string CloseHintText = "[Tab] [E] [Esc] 닫기   [←] [→] 넘기기";
    public const string OpenHintText = "[Tab] 도감";
    public const string UnknownName = "???";
    public const string UnknownText = "아직 만나지 않았어요.";
    public const string AnimalHeader = "동물", MonsterHeader = "몬스터", TraitHeader = "특징", AnimalTipHeader = "잡는 법", MonsterTipHeader = "대처법";
    public static readonly string[] OpenScenes = { "TruckScene", "ShelterScene" };

    private const int CanvasSortingOrder = 620;   // 첫 만남 카드(610) 위, 일시정지 메뉴(1000) 아래
    private const float PixelUnits = 4.67f;       // 도트 1칸(튜토리얼과 같다)
    private const float CheckInterval = 0.25f;
    private static readonly Vector2 PanelSize = new Vector2(1880f, 1120f);
    private const float Margin = 60f;
    private const float ScreenEdge = 16f;          // 판이 화면보다 클 때(21:9 등) 화면 가장자리에서 남길 여백(기준 단위)
    private static readonly Vector2 CellSize = new Vector2(200f, 180f);
    private const float CellGap = 16f;
    private const float DetailX = 740f, DetailTop = 140f;
    private const float TitleSize = 64f, CountSize = 36f, HintSize = 32f, HintKeySize = 28f, SectionSize = 38f;
    private const float CellNameSize = 32f, NameSize = 72f, TagSize = 32f, BodySize = 46f, BodyKeySize = 40f, BodyHeaderSize = 34f;

    private static readonly Color Cream = new Color32(0xED, 0xE3, 0xD3, 0xFF);
    private static readonly Color Muted = new Color32(0xBB, 0xAE, 0x9A, 0xFF);     // 크림 그림자
    private static readonly Color Teal = new Color32(0xA9, 0xD0, 0xC2, 0xFF);      // 흐린 청록(동물·안내, 첫 만남 카드 대처법 색)
    private static readonly Color Pink = new Color32(0xE3, 0x94, 0x98, 0xFF);      // 흐린 빨강(몬스터)
    private static readonly Color OutlineColor = new Color32(0x2F, 0x2F, 0x2E, 0xFF);
    private static readonly Color SilhouetteColor = new Color32(0x5C, 0x4B, 0x48, 0xFF); // 밤 칸 위에서 보이는 나무색 실루엣

    private static BestiaryUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    public static BestiaryUI Instance => instance;

    /// <summary>도감이 열려 게임을 멈춘 동안(그리고 닫은 그 프레임) 게임 입력을 막는다 - GameplayInput.IsBlocked가 읽는다.</summary>
    public static bool BlocksInput => instance != null && (instance.open || instance.closedFrame == Time.frameCount);

    public static BestiaryUI EnsureInstance()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("BestiaryUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BestiaryUI>();
        return instance;
    }

    // ---------------- 항목 ----------------
    public struct Item
    {
        public bool IsMonster;
        public AnimalSpecies Animal;
        public MonsterKind Monster;
        public GameObject Prefab;
    }

    private readonly List<Item> items = new List<Item>();
    private readonly HashSet<AnimalSpecies> seenAnimals = new HashSet<AnimalSpecies>();
    private readonly HashSet<MonsterKind> seenMonsters = new HashSet<MonsterKind>();

    /// <summary>항목(동물 다음 몬스터, 목록 순서).</summary>
    public IReadOnlyList<Item> Items => items;
    public int AnimalCount => items.Count(i => !i.IsMonster);
    public int MonsterCount => items.Count(i => i.IsMonster);
    public int DiscoveredCount => items.Count(IsDiscovered);
    public bool IsDiscovered(Item i) => i.IsMonster ? seenMonsters.Contains(i.Monster) : seenAnimals.Contains(i.Animal);
    public bool IsDiscovered(AnimalSpecies a) => seenAnimals.Contains(a);
    public bool IsDiscovered(MonsterKind m) => seenMonsters.Contains(m);
    /// <summary>만난 것으로 기록한다(마을에서 처음 보일 때 부른다. 테스트도 쓴다).</summary>
    public void Discover(AnimalSpecies a) { if (seenAnimals.Add(a) && open) Refresh(); }
    public void Discover(MonsterKind m) { if (seenMonsters.Add(m) && open) Refresh(); }
    public void ClearDiscoveries() { seenAnimals.Clear(); seenMonsters.Clear(); if (open) Refresh(); }

    public bool IsOpen => open;
    public int SelectedIndex => selected;
    public Item SelectedItem => items[selected];

    // ---------------- 상태 ----------------
    private bool built;
    private bool open;
    private int selected;
    private float savedTimeScale = 1f;
    private int closedFrame = -1;
    private bool resetArmed;
    private float checkTimer;

    // ---------------- 그림 ----------------
    private RectTransform canvasRect;
    private GameObject root, hint;
    private RectTransform panel, detail, body;
    private TMP_Text countText, detailName, tagText;
    private Image tagBox, detailPortrait;
    private RectTransform detailPortraitHolder;
    private readonly List<(RectTransform rt, Image frame, Image pic, TMP_Text name)> cells = new List<(RectTransform, Image, Image, TMP_Text)>();
    private Sprite nightSprite, frameSprite, redChipSprite;
    private readonly Dictionary<Sprite, (Rect vis, Sprite silhouette)> spriteInfo = new Dictionary<Sprite, (Rect, Sprite)>();
    private readonly Dictionary<int, Sprite> portraits = new Dictionary<int, Sprite>();

    public RectTransform PanelRect => panel;
    public RectTransform HintRect => hint != null ? (RectTransform)hint.transform : null;
    public bool IsHintShowing => hint != null && hint.activeInHierarchy;
    public RectTransform CellRect(int index) => cells[index].rt;
    public string CellName(int index) => cells[index].name.text;
    public bool CellShowsSilhouette(int index) => cells[index].pic.sprite != null && cells[index].pic.sprite.name.EndsWith("_silhouette");
    public string CountText => countText != null ? countText.text : "";
    public string DetailName => detailName != null ? detailName.text : "";
    public Image DetailPortrait => detailPortrait;
    /// <summary>상세 칸의 모든 글자(이름·태그·특징·대처법).</summary>
    public string DetailText => detail != null ? string.Join(" ", detail.GetComponentsInChildren<TMP_Text>().Where(t => t.gameObject.activeInHierarchy).Select(t => t.text)) : "";

    private void BuildItems()
    {
        items.Clear();
        BestiaryArt art = Resources.Load<BestiaryArt>(BestiaryArt.ResourcePath);
        if (art == null) Debug.LogError($"BestiaryUI: Resources/{BestiaryArt.ResourcePath}가 없다 - 도감 그림 없이 연다.");
        foreach (AnimalSpecies a in AnimalBestiary.AllKinds)
            items.Add(new Item { IsMonster = false, Animal = a, Prefab = art?.animalPrefabs?.FirstOrDefault(p => AnimalBestiary.KindOf(p) == a) });
        foreach (MonsterKind m in MonsterBestiary.AllKinds)
            items.Add(new Item { IsMonster = true, Monster = m, Prefab = art?.monsterPrefabs?.FirstOrDefault(p => MonsterBestiary.KindOf(p) == m) });
    }

    // ---------------- 만들기 ----------------
    private void Build()
    {
        BuildItems();
        TutorialGuide g = TutorialGuide.EnsureInstance();
        nightSprite = PanelSprite("BestiaryNight", OutlineColor, new Color32(0x3A, 0x33, 0x40, 0xFF), new Color32(0x1E, 0x1A, 0x21, 0xFF), new Color32(0x15, 0x12, 0x1A, 0xFF));
        redChipSprite = PanelSprite("BestiaryRedChip", OutlineColor, new Color32(0xE3, 0x94, 0x98, 0xFF), new Color32(0xC8, 0x65, 0x6E, 0xFF), new Color32(0xA4, 0x4E, 0x57, 0xFF));
        frameSprite = FrameSprite();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = TutorialGuide.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        // 여는 곳 안내(트럭·보호소 오른쪽 위, 마일리지 배지 아래)
        hint = g.Box("BestiaryHint", transform, g.panelSprite, new RectOffset(18, 22, 10, 10), 10f, false);
        TutorialGuide.FitToContent(hint);
        RectTransform hrt = (RectTransform)hint.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 1f);
        hrt.anchoredPosition = new Vector2(-36f, -170f);
        RectTransform hline = (RectTransform)g.Box("Line", hint.transform, null, new RectOffset(0, 0, 0, 0), 6f, false).transform;
        g.BuildLine(hline, OpenHintText, 28f, 24f, Cream);
        hint.SetActive(false);

        root = TutorialGuide.Child("Bestiary", transform);
        TutorialGuide.Stretch((RectTransform)root.transform);
        Image dim = TutorialGuide.StretchChild("Dim", root.transform).gameObject.AddComponent<Image>();
        dim.color = TutorialGuide.DimColor;
        dim.raycastTarget = false;

        panel = (RectTransform)TutorialGuide.Child("Panel", root.transform).transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = PanelSize;
        Sliced(panel.gameObject, g.panelSprite, Color.white);

        // 머리 줄
        TMP_Text title = TutorialGuide.Label(panel, TitleText, TitleSize, Cream);
        Place(title.rectTransform, new Vector2(Margin, 34f), new Vector2(360f, 80f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        countText = TutorialGuide.Label(panel, "", CountSize, Muted);
        Place(countText.rectTransform, new Vector2(Margin + 340f, 34f), new Vector2(400f, 80f));
        countText.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform closeLine = (RectTransform)g.Box("CloseHint", panel, null, new RectOffset(0, 0, 0, 0), 8f, false).transform;
        TutorialGuide.FitToContent(closeLine.gameObject);
        closeLine.anchorMin = closeLine.anchorMax = closeLine.pivot = new Vector2(1f, 1f);
        closeLine.anchoredPosition = new Vector2(-Margin, -52f);
        g.BuildLine(closeLine, CloseHintText, HintSize, HintKeySize, TutorialGuide.HighlightYellow);

        // 왼쪽 목록
        float y = DetailTop;
        for (int section = 0; section < 2; section++)
        {
            bool monsters = section == 1;
            TMP_Text head = TutorialGuide.Label(panel, monsters ? MonsterHeader : AnimalHeader, SectionSize, monsters ? Pink : Teal);
            Place(head.rectTransform, new Vector2(Margin, y), new Vector2(300f, 48f));
            head.alignment = TextAlignmentOptions.MidlineLeft;
            y += 56f;
            var list = Enumerable.Range(0, items.Count).Where(i => items[i].IsMonster == monsters).ToList();
            for (int n = 0; n < list.Count; n++)
            {
                int col = n % 3, row = n / 3;
                cells.Add(BuildCell(list[n], new Vector2(Margin + col * (CellSize.x + CellGap), y + row * (CellSize.y + CellGap))));
            }
            y += ((list.Count + 2) / 3) * (CellSize.y + CellGap) - CellGap + 34f;
        }

        // 오른쪽 상세
        detail = (RectTransform)TutorialGuide.Child("Detail", panel).transform;
        Place(detail, new Vector2(DetailX, DetailTop), new Vector2(PanelSize.x - DetailX - Margin, PanelSize.y - DetailTop - Margin));
        Sliced(detail.gameObject, g.panelSprite, new Color(0.78f, 0.78f, 0.78f, 1f));
        RectTransform picBox = (RectTransform)TutorialGuide.Child("PortraitBox", detail).transform;
        Place(picBox, new Vector2(44f, 44f), new Vector2(300f, 300f));
        Sliced(picBox.gameObject, nightSprite, Color.white);
        detailPortraitHolder = picBox;
        detailPortrait = TutorialGuide.Child("Portrait", picBox).AddComponent<Image>();
        detailPortrait.raycastTarget = false;
        detailName = TutorialGuide.Label(detail, "", NameSize, Cream);
        Place(detailName.rectTransform, new Vector2(390f, 60f), new Vector2(600f, 100f));
        detailName.alignment = TextAlignmentOptions.MidlineLeft;
        GameObject tag = g.Box("Tag", detail, g.stepSprite, new RectOffset(20, 20, 8, 8), 0f, false);
        TutorialGuide.FitToContent(tag);
        RectTransform trt = (RectTransform)tag.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(390f, -180f);
        tagBox = tag.GetComponent<Image>();
        tagText = TutorialGuide.Label(tag.transform, "", TagSize, Cream);

        body = (RectTransform)TutorialGuide.Child("Body", detail).transform;
        body.anchorMin = body.anchorMax = body.pivot = new Vector2(0f, 1f);
        body.anchoredPosition = new Vector2(44f, -390f);
        body.sizeDelta = new Vector2(detail.sizeDelta.x - 88f, 0f);
        VerticalLayoutGroup v = body.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.UpperLeft;
        v.spacing = 10f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        ContentSizeFitter f = body.gameObject.AddComponent<ContentSizeFitter>();
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        root.SetActive(false);
        built = true;
    }

    private (RectTransform, Image, Image, TMP_Text) BuildCell(int index, Vector2 pos)
    {
        RectTransform rt = (RectTransform)TutorialGuide.Child("Cell" + index, panel).transform;
        Place(rt, pos, CellSize);
        Sliced(rt.gameObject, nightSprite, Color.white);
        Image pic = TutorialGuide.Child("Portrait", rt).AddComponent<Image>();
        pic.raycastTarget = false;
        TMP_Text name = TutorialGuide.Label(rt, "", CellNameSize, Cream);
        Place(name.rectTransform, new Vector2(0f, CellSize.y - 54f), new Vector2(CellSize.x, 44f));
        Image frame = TutorialGuide.StretchChild("Selected", rt).gameObject.AddComponent<Image>();
        frame.sprite = frameSprite;
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 1f / PixelUnits;
        frame.raycastTarget = false;
        frame.rectTransform.offsetMin = new Vector2(-2f * PixelUnits, -2f * PixelUnits);
        frame.rectTransform.offsetMax = new Vector2(2f * PixelUnits, 2f * PixelUnits);
        frame.enabled = false;
        return (rt, frame, pic, name);
    }

    private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rt.sizeDelta = size;
    }

    private static Image Sliced(GameObject go, Sprite sprite, Color color)
    {
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f / PixelUnits;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // 튜토리얼 판 그림(hint_panel·hint_step)과 같은 16x16 모양을 다른 색으로: 외곽선 1칸(모서리 비움), 위·왼쪽 밝은 줄, 아래·오른쪽 어두운 줄. 도트 그림이라 Point 필터·압축 없음.
    private static Sprite PanelSprite(string name, Color32 outline, Color32 hi, Color32 mid, Color32 lo)
    {
        const int n = 16;
        var tex = NewTexture(n, name);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int ty = n - 1 - y; // 위에서부터
                bool corner = (x == 0 || x == n - 1) && (ty == 0 || ty == n - 1);
                Color32 c;
                if (corner) c = new Color32(0, 0, 0, 0);
                else if (x == 0 || x == n - 1 || ty == 0 || ty == n - 1) c = outline;
                else if ((x == 1 && ty == 1) || (x == n - 2 && ty == 1) || (x == 1 && ty == n - 2)) c = mid;
                else if (ty == 1 || x == 1) c = hi;
                else if (ty == n - 2 || x == n - 2) c = lo;
                else c = mid;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return SpriteFrom(tex, name, new Vector4(3, 3, 3, 3));
    }

    // 고른 칸 테두리: 외곽선 1칸 + 노랑 2칸, 가운데는 비움.
    private static Sprite FrameSprite()
    {
        const int n = 12;
        var tex = NewTexture(n, "BestiarySelected");
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1);
                tex.SetPixel(x, y, corner ? new Color(0, 0, 0, 0) : d == 0 ? (Color)OutlineColor : d <= 2 ? TutorialGuide.HighlightYellow : new Color(0, 0, 0, 0));
            }
        tex.Apply();
        return SpriteFrom(tex, "BestiarySelected", new Vector4(4, 4, 4, 4));
    }

    private static Texture2D NewTexture(int n, string name) =>
        new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };

    private static Sprite SpriteFrom(Texture2D tex, string name, Vector4 border)
    {
        Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.name = name;
        return s;
    }

    // ---------------- 그림(초상) ----------------
    private Sprite PortraitFor(int index)
    {
        if (portraits.TryGetValue(index, out Sprite s)) return s;
        Item it = items[index];
        s = it.Prefab == null ? null : it.IsMonster ? MonsterBestiary.PortraitOf(it.Prefab) : AnimalBestiary.PortraitOf(it.Prefab);
        portraits[index] = s;
        return s;
    }

    // 그림의 보이는 부분(알파 > 0.1)과 같은 모양의 실루엣. 텍스처를 읽을 수 없게 들여와도 되게 GPU에서 한 번 복사해 읽는다(첫 만남 카드와 같은 방법). 그림마다 한 번만.
    private (Rect vis, Sprite silhouette) InfoOf(Sprite s)
    {
        if (spriteInfo.TryGetValue(s, out var cached)) return cached;
        Rect full = new Rect(Vector2.zero, s.rect.size), vis = full;
        Sprite sil = null;
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
            Vector2 offset = s.textureRectOffset;
            int fw = Mathf.RoundToInt(s.rect.width), fh = Mathf.RoundToInt(s.rect.height);
            int ox = Mathf.RoundToInt(offset.x), oy = Mathf.RoundToInt(offset.y);
            Texture2D silTex = new Texture2D(fw, fh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = s.name + "_silhouette" };
            var silPx = new Color32[fw * fh];
            Color32 on = SilhouetteColor, off = new Color32(0, 0, 0, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool solid = px[y * w + x].a > 25;
                    int fx = ox + x, fy = oy + y;
                    if (fx >= 0 && fx < fw && fy >= 0 && fy < fh) silPx[fy * fw + fx] = solid ? on : off;
                    if (solid) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                }
            silTex.SetPixels32(silPx);
            silTex.Apply();
            sil = Sprite.Create(silTex, new Rect(0, 0, fw, fh), new Vector2(0.5f, 0.5f), s.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sil.name = s.name + "_silhouette";
            if (maxX >= minX && maxY >= minY) vis = Rect.MinMaxRect(ox + minX, oy + minY, ox + maxX + 1, oy + maxY + 1);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
        var info = (vis, sil);
        spriteInfo[s] = info;
        return info;
    }

    // 도트 규칙: 그림 한 칸을 정수배로 키운다(Point 필터 그림 그대로). 배율은 보이는 부분이 box 안에 들어가는 가장 큰 정수.
    private void SetPicture(Image img, Sprite sprite, bool known, Vector2 box, Vector2 center)
    {
        if (sprite == null) { img.enabled = false; return; }
        var info = InfoOf(sprite);
        img.enabled = true;
        img.sprite = known || info.silhouette == null ? sprite : info.silhouette;
        img.color = Color.white;
        Rect vis = info.vis;
        float k = Mathf.Max(1f, Mathf.Floor(Mathf.Min(box.x / Mathf.Max(1f, vis.width), box.y / Mathf.Max(1f, vis.height))));
        RectTransform prt = img.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = sprite.rect.size * k;
        prt.anchoredPosition = new Vector2(center.x, -center.y) - (vis.center - sprite.rect.size * 0.5f) * k;
    }

    // ---------------- 채우기 ----------------
    private static string NameOf(Item it, GameBalanceConfig c) => it.IsMonster ? MonsterBestiary.Get(it.Monster, c).Name : AnimalBestiary.Get(it.Animal, c).Name;

    /// <summary>항목의 태그 글(동물·무게 범위 / 몬스터·체력) - 프리팹의 실제 값.</summary>
    public static string TagOf(Item it)
    {
        if (it.IsMonster)
        {
            MonsterHealth h = it.Prefab != null ? it.Prefab.GetComponent<MonsterHealth>() : null;
            return h != null ? $"{MonsterHeader} · 체력 {h.MaxHealth}" : MonsterHeader;
        }
        AnimalRescue r = it.Prefab != null ? it.Prefab.GetComponent<AnimalRescue>() : null;
        if (r == null) return AnimalHeader;
        r.GetWeightRange(out int min, out int max);
        return $"{AnimalHeader} · {min}~{max}kg";
    }

    // 판이 화면(Canvas)보다 크면(21:9처럼 세로가 짧은 화면) 판 전체를 같은 비율로 줄여 위아래·좌우가 잘리지 않게 한다.
    // 16:9·16:10에서는 화면이 판보다 커서 배율 1 그대로다.
    private void FitPanel()
    {
        if (panel == null || canvasRect == null) return;
        Vector2 screen = canvasRect.rect.size;
        if (screen.x <= 0f || screen.y <= 0f) return;
        float s = Mathf.Min(1f, (screen.y - 2f * ScreenEdge) / PanelSize.y, (screen.x - 2f * ScreenEdge) / PanelSize.x);
        if (!Mathf.Approximately(panel.localScale.x, s)) panel.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>판의 지금 배율(1이면 원래 크기). 테스트에서 본다.</summary>
    public float PanelScale => panel != null ? panel.localScale.x : 1f;

    private void Refresh()
    {
        FitPanel();
        TutorialGuide g = TutorialGuide.EnsureInstance();
        GameBalanceConfig c = GameManager.Instance != null ? GameManager.Instance.Config : null;
        countText.text = $"만난 대상 {DiscoveredCount}/{items.Count}";
        for (int i = 0; i < cells.Count; i++)
        {
            bool known = IsDiscovered(items[i]);
            var cell = cells[i];
            cell.name.text = known ? NameOf(items[i], c) : UnknownName;
            cell.name.color = known ? Cream : Muted;
            cell.frame.enabled = i == selected;
            SetPicture(cell.pic, PortraitFor(i), known, new Vector2(CellSize.x - 40f, CellSize.y - 76f), new Vector2(CellSize.x * 0.5f, 18f + (CellSize.y - 76f) * 0.5f));
        }

        Item it = items[selected];
        bool k = IsDiscovered(it);
        SetPicture(detailPortrait, PortraitFor(selected), k, new Vector2(248f, 248f), new Vector2(150f, 150f));
        detailName.text = k ? NameOf(it, c) : UnknownName;
        tagBox.sprite = it.IsMonster ? redChipSprite : g.stepSprite;
        tagText.text = k ? TagOf(it) : (it.IsMonster ? MonsterHeader : AnimalHeader);

        // 본문: 특징 / 대처법(잡는 법). 만나지 않았으면 한 줄만.
        for (int i = body.childCount - 1; i >= 0; i--) { GameObject ch = body.GetChild(i).gameObject; ch.transform.SetParent(null); Destroy(ch); }
        float maxWidth = body.sizeDelta.x;
        if (!k)
        {
            AddRows(g, new[] { UnknownText }, Muted, maxWidth);
        }
        else
        {
            string[] traits; string tip;
            if (it.IsMonster) { var e = MonsterBestiary.Get(it.Monster, c); traits = e.Traits; tip = e.Tip; }
            else { var e = AnimalBestiary.Get(it.Animal, c); traits = new[] { e.Trait }; tip = e.Tip; }
            AddHeader(TraitHeader, Muted);
            AddRows(g, traits, Cream, maxWidth);
            AddSpacer(26f);
            AddHeader(it.IsMonster ? MonsterTipHeader : AnimalTipHeader, it.IsMonster ? Pink : Teal);
            AddRows(g, new[] { tip }, Teal, maxWidth);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    private void AddHeader(string text, Color color)
    {
        TMP_Text t = TutorialGuide.Label(body, text, BodyHeaderSize, color);
        t.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void AddSpacer(float h)
    {
        LayoutElement le = TutorialGuide.Child("Space", body).AddComponent<LayoutElement>();
        le.preferredHeight = h;
    }

    private void AddRows(TutorialGuide g, IEnumerable<string> lines, Color color, float maxWidth)
    {
        foreach (string line in lines)
        {
            RectTransform box = (RectTransform)g.Box("Line", body, null, new RectOffset(0, 0, 0, 0), 4f, true).transform;
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            g.BuildRows(box, g.WrapRows(line, BodySize, BodyKeySize, TutorialGuide.CardLineSpacing, maxWidth), BodySize, BodyKeySize, color, TutorialGuide.CardKeyPadY, TutorialGuide.CardLineSpacing);
        }
    }

    // ---------------- 진행 ----------------
    private void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) { if (open) Close(); if (hint != null) hint.SetActive(false); return; }
        if (!built) Build();

        // 새 게임: 시작 클래스 선택을 기다리는 상태가 되면 만난 기록을 비운다(첫 만남 카드와 같은 때)
        if (gm.NeedsStartingClassSelection) { if (!resetArmed) { ClearDiscoveries(); resetArmed = true; } }
        else resetArmed = false;

        string scene = SceneManager.GetActiveScene().name;
        if (scene == "VillageScene") WatchVillage();
        bool openScene = System.Array.IndexOf(OpenScenes, scene) >= 0 && !gm.IsGameOver && !gm.IsGameWon;
        Keyboard kb = Keyboard.current;

        if (open)
        {
            FitPanel();
            if (!openScene) { Close(); }
            else if (kb != null && (kb.tabKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) Close();
            else
            {
                if (kb != null && kb.rightArrowKey.wasPressedThisFrame) Select((selected + 1) % items.Count);
                else if (kb != null && kb.leftArrowKey.wasPressedThisFrame) Select((selected + items.Count - 1) % items.Count);
                else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ClickAt(Mouse.current.position.ReadValue());
            }
        }
        else if (openScene && kb != null && kb.tabKey.wasPressedThisFrame && CanOpen()) Open();

        if (hint != null) hint.SetActive(openScene && !open && CanOpen());
    }

    private static bool CanOpen()
    {
        TutorialGuide tg = TutorialGuide.Instance;
        return !PauseMenu.IsPaused && !ShopMenuUI.IsOpen && Time.timeScale > 0f && !(tg != null && tg.IsIntroActive) &&
               !(MonsterEncounterCard.Instance != null && MonsterEncounterCard.Instance.IsShowing);
    }

    // 마을에서 처음 플레이어 시야에 보인 종류를 기록한다(실제 시간 0.25초마다 - 첫 만남 카드와 같은 판정).
    private void WatchVillage()
    {
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer > 0f) return;
        checkTimer = CheckInterval;
        if (PlayerVision.Current == null) return;
        foreach (MonsterHealth m in MonsterHealth.Active)
        {
            if (m == null || m.IsDead || !m.gameObject.activeInHierarchy) continue;
            MonsterKind? kind = MonsterBestiary.KindOf(m.gameObject);
            if (kind == null || seenMonsters.Contains(kind.Value) || !PlayerVision.IsVisibleToPlayer(m.GamePosition)) continue;
            seenMonsters.Add(kind.Value);
        }
        foreach (AnimalSleep a in AnimalSleep.Active)
        {
            if (a == null || !a.gameObject.activeInHierarchy) continue;
            AnimalSpecies? kind = AnimalBestiary.KindOf(a.gameObject);
            if (kind == null || seenAnimals.Contains(kind.Value) || !PlayerVision.IsVisibleToPlayer(a.GamePosition)) continue;
            seenAnimals.Add(kind.Value);
        }
    }

    /// <summary>도감을 연다(트럭·보호소에서 Tab이 부른다. 테스트도 쓴다).</summary>
    public void Open()
    {
        if (open) return;
        if (!built) Build();
        open = true;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        closedFrame = Time.frameCount;
        if (root != null) root.SetActive(false);
        // 게임오버·승리 화면이 시간을 멈춰 둔 중이면 되돌리지 않는다(그 화면 뒤에서 게임이 다시 움직이지 않게).
        GameManager gm = GameManager.Instance;
        bool runEnded = gm != null && (gm.IsGameOver || gm.IsGameWon);
        if (Time.timeScale <= 0f && !runEnded) Time.timeScale = savedTimeScale;
    }

    public void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, items.Count - 1);
        if (open) Refresh();
    }

    private void ClickAt(Vector2 screen)
    {
        for (int i = 0; i < cells.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(cells[i].rt, screen, null)) { Select(i); return; }
    }

    /// <summary>배치를 바로 다시 계산한다(테스트 캡처처럼 Canvas 배치가 바뀐 뒤 부른다).</summary>
    public void RefreshLayoutNow()
    {
        Canvas.ForceUpdateCanvases();
        if (open) Refresh();
        Canvas.ForceUpdateCanvases();
    }
}
