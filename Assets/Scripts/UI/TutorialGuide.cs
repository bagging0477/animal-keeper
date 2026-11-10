using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 1일차 튜토리얼 안내(Logs/Tutorial/notes.md의 9단계 표). 화면 위 가운데에 한 줄 안내를 단계별로 띄운다.
/// - 게임을 멈추지 않는다. 게임 상태(GameManager·씬·플레이어)를 읽기만 하고 아무것도 바꾸지 않는다.
/// - 단계마다 "끝난 조건"을 늘 본다 - 안내가 뜨기 전에 이미 한 행동은 끝난 것으로 쳐서 건너뛴다.
///   뜰 수 있는 단계 중 번호가 가장 작은, 아직 끝나지 않은 단계 하나만 보여 준다.
/// - 1일차(CycleCount 1)에만 나오고, 게임 오버·클리어·시간이 멈춘 동안(메뉴·일시정지)은 숨긴다.
/// - 새 게임(ResetGame 뒤 시작 클래스 선택을 기다리는 상태, GameManager.NeedsStartingClassSelection)이 되면 처음부터 다시 나온다.
/// - 씬과 무관하게 하나만 있고(DontDestroyOnLoad) 자기 전용 오버레이 Canvas에 그린다(SubtitleManager와 같은 방식). 게임이 시작될 때 스스로 만들어진다.
/// 문구 안의 [키]는 키 모양 칸으로 그린다.
/// </summary>
public class TutorialGuide : MonoBehaviour
{
    public const int StepCount = 9;
    public const string DoneChipText = "완료!";

    private static readonly Vector2 ReferenceResolution = new Vector2(2240f, 1260f); // 씬 UI Canvas와 같은 기준
    private const int CanvasSortingOrder = 450;  // HUD(0) 위, 자막(500)·일시정지 메뉴(1000) 아래
    private const float TopOffset = 140f;        // 화면 위에서 패널 위 끝까지(1080p 약 120px) - 트럭 위 정보 줄 아래
    private const float TextSize = 34f;
    private const float KeySize = 28f;
    private const float ChipSize = 24f;
    private const float PixelUnits = 4.67f;      // 패널 도트 1칸 = 1080p 4px
    private const float DoneFlashSeconds = 0.6f;
    private const float PickupToastSeconds = 2f;
    private const float Step3TimeoutSeconds = 8f;
    private const float Step5TimeoutSeconds = 10f;

    private static readonly Color Cream = new Color32(0xED, 0xE3, 0xD3, 0xFF);
    // 키 칸 글자: 공용 글꼴 머티리얼(검은 외곽선)을 그대로 쓰므로 인벤토리 칸 번호처럼 흰 글자로 둔다(진한 글자는 외곽선과 뭉개진다)
    private static readonly Color KeyText = Color.white;

    private static TutorialGuide instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    public static TutorialGuide Instance => instance;

    public static TutorialGuide EnsureInstance()
    {
        if (instance != null) return instance;
        GameObject go = new GameObject("TutorialGuide");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<TutorialGuide>();
        instance.Build();
        return instance;
    }

    // ---------------- 상태 ----------------
    private readonly bool[] done = new bool[StepCount + 1];
    private bool resetArmed;
    private string lastScene;
    private bool visitedVillage;
    private bool villageEntrySet;
    private Vector2 villageStartPos;
    private Vector2 villageStartLook;
    private float villageTimer;
    private bool animalSeen;
    private bool monsterSeen;
    private float step5Timer;
    private float visionCheckTimer;
    private InventoryItemType prevSelectedType;
    private int flashStep;
    private string flashText;
    private float flashUntil;

    /// <summary>지금 보이는 단계(1~9). 없으면 0. 끝난 단계의 "완료!" 표시 중에도 그 단계 번호다.</summary>
    public int CurrentStep { get; private set; }

    /// <summary>지금 보이는 문구([키] 표시 그대로). 없으면 null.</summary>
    public string CurrentText { get; private set; }

    /// <summary>안내 줄이 화면에 보이는지.</summary>
    public bool IsShowing => panel != null && panel.activeSelf;

    public bool IsDone(int step) => step >= 1 && step <= StepCount && done[step];

    // ---------------- 화면 ----------------
    private GameObject panel;
    private RectTransform content;
    private TMP_Text chipLabel;
    private Sprite panelSprite, stepSprite, keySprite;
    private string builtText;

    private void Build()
    {
        panelSprite = Resources.Load<Sprite>("TutorialHint/hint_panel");
        stepSprite = Resources.Load<Sprite>("TutorialHint/hint_step");
        keySprite = Resources.Load<Sprite>("TutorialHint/hint_key");

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        panel = Box("TutorialHint", transform, panelSprite, new RectOffset(22, 26, 16, 16), 12f);
        RectTransform prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 1f);
        prt.anchoredPosition = new Vector2(0f, -TopOffset);
        panel.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject chip = Box("Step", panel.transform, stepSprite, new RectOffset(14, 14, 6, 6), 0f);
        chipLabel = Label(chip.transform, "", ChipSize, Cream);
        content = (RectTransform)Box("Line", panel.transform, null, new RectOffset(0, 0, 0, 0), 8f).transform;
        panel.SetActive(false);
    }

    private GameObject Box(string name, Transform parent, Sprite sprite, RectOffset padding, float spacing)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        if (sprite != null)
        {
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f / PixelUnits;
            img.raycastTarget = false;
        }
        HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = padding;
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return go;
    }

    private static TMP_Text Label(Transform parent, string text, float size, Color color)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
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

    // "[WASD] 걷기 · [Shift] 달리기" → 키 칸과 글자 조각으로 다시 만든다(문구가 바뀔 때만).
    private void Rebuild(int step, string text, bool doneMark)
    {
        chipLabel.text = doneMark ? DoneChipText : $"안내 {step}/{StepCount}";
        string key = $"{doneMark}|{text}";
        if (builtText == key) return;
        builtText = key;
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        foreach (Match m in Regex.Matches(text, @"\[([^\]]+)\]|[^\[]+"))
        {
            if (m.Groups[1].Success)
            {
                GameObject cap = Box("Key", content, keySprite, new RectOffset(12, 12, 4, 4), 0f);
                Label(cap.transform, m.Groups[1].Value, KeySize, KeyText);
            }
            else if (m.Value.Trim().Length > 0)
            {
                Label(content, m.Value.Trim(), TextSize, Cream);
            }
        }
    }

    // ---------------- 진행 ----------------
    private void ResetProgress()
    {
        for (int i = 0; i < done.Length; i++) done[i] = false;
        visitedVillage = villageEntrySet = animalSeen = monsterSeen = false;
        villageTimer = step5Timer = 0f;
        flashStep = 0;
        flashText = null;
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        string scene = SceneManager.GetActiveScene().name;
        if (gm == null) { Show(0, null, false); return; }

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
            if (dayOne) OnSceneEntered(scene, gm);
            // 이전 씬에서 끝난 단계의 "완료!"는 새 씬으로 가져오지 않는다(새 씬의 단계를 바로 보여 준다)
            flashStep = 0;
        }
        if (!dayOne) { Show(0, null, false); return; }

        // 끝난 단계의 "완료!"(또는 "가방에 쏙!")을 잠깐 보여 준 뒤 다음 단계로
        if (flashStep > 0 && Time.unscaledTime < flashUntil)
        {
            Show(flashStep, flashText, true);
            return;
        }
        flashStep = 0;

        int next = 0;
        for (int s = 1; s <= StepCount && next == 0; s++)
            if (!done[s] && ShouldShow(s, scene, gm)) next = s;
        if (next == 5) step5Timer += Time.deltaTime;
        Show(next, next > 0 ? StepText(next, gm) : null, false);
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
            villageTimer += Time.deltaTime;
            bool moved = Vector2.Distance(player.transform.position, villageStartPos) >= 2f;
            bool turned = Vector2.Angle(move.LookDirection, villageStartLook) >= 30f;
            if ((moved && turned) || villageTimer >= Step3TimeoutSeconds) Complete(3, null);
        }

        // 무기(Q 상태에서 클릭)나 지뢰·폭탄(그 칸을 고른 채 클릭)을 쓰면 5단계 끝 - 안내가 뜨기 전에 써도 끝난 것으로 친다.
        // 지뢰·폭탄은 클릭한 그 프레임에 칸이 비워질 수 있어(PlayerGadgetController가 먼저 돌 때) 직전 프레임에 고른 칸 종류로 본다.
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
                monsterSeen = MonsterHealth.Active.Any(m => m != null && !m.IsDead && m.gameObject.activeInHierarchy && PlayerVision.IsVisibleToPlayer(m.GamePosition));
        }
    }

    private void Complete(int step, string toast)
    {
        if (done[step]) return;
        done[step] = true;
        // 지금 보이던 단계가 끝났을 때만 "완료!"를 잠깐 보여 준다(안 보이던 단계는 조용히 건너뛴다)
        if (CurrentStep == step && IsShowing)
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

    private static List<int> ToolSlots(GameManager gm) =>
        Enumerable.Range(0, gm.InventorySlotCount).Where(i => gm.GetSlot(i).Type == InventoryItemType.Mine || gm.GetSlot(i).Type == InventoryItemType.Bomb).ToList();

    private static bool IsRange(List<int> s) => s.Count > 0 && s[s.Count - 1] - s[0] == s.Count - 1;

    private void Show(int step, string text, bool doneMark)
    {
        CurrentStep = text != null ? step : 0;
        CurrentText = text;
        if (panel != null && text != null) Rebuild(step, text, doneMark);
    }

    // 보일지 말지는 모든 Update가 끝난 뒤 정한다 - 같은 프레임에 메뉴가 열려 시간이 멈춰도(소파·상점 메뉴, 일시정지) 그 화면을 가리지 않게.
    private void LateUpdate()
    {
        if (panel == null) return;
        bool visible = CurrentText != null && Time.timeScale > 0f && !PauseMenu.IsPaused;
        if (panel.activeSelf != visible) panel.SetActive(visible);
    }
}
