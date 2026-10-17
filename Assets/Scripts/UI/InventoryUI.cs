using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>인벤토리 슬롯 하나의 화면 표시 요소 3개(테두리/배경/라벨) 묶음.</summary>
[System.Serializable]
public class InventorySlotUI
{
    public Image frame;
    public Image fill;
    public TMP_Text label;

    /// <summary>슬롯에 담긴 아이템의 스프라이트(동물/지뢰/폭탄 아이콘)를 그리는 Image. 비워두면
    /// InventoryUI가 실행 시 frame 아래에 안쪽 여백을 둔 Image를 만들어 채운다. 빈 슬롯에서는 꺼진다.</summary>
    public Image icon;
}

/// <summary>인벤토리 칸 + 클래스 고정 무기 슬롯을 화면에 그린다. 선택된 슬롯은 선택용 배경 이미지로
/// 바뀌어 구분되고(ApplySlotBackground), 각 슬롯에는 내용물의 정지 스프라이트 아이콘과 모서리의 작은
/// 선택 키 번호(1부터)만 표시된다. 무기 슬롯과 숫자 슬롯은 서로 배타적으로만 강조되므로(GameManager.IsWeaponSelected)
/// 항상 둘 중 하나만 강조된 채로 보인다.
///
/// 칸 수는 GameManager.InventorySlotCount를 따른다. 씬에 배치한 슬롯(slots)보다 칸이 많으면 마지막 슬롯을 복제해
/// 같은 크기/배경/번호 스타일의 칸을 더하고, 칸 수가 바뀔 때마다 씬의 칸 간격 그대로 하단 중앙에 다시 늘어놓는다.</summary>
public class InventoryUI : MonoBehaviour
{
    [Tooltip("씬에 배치한 슬롯. 앞의 두 칸 간격이 칸 간격, 마지막 칸이 추가 칸의 복제 원본이 된다.")]
    [SerializeField] private InventorySlotUI[] slots = new InventorySlotUI[0];

    [Header("아이템 아이콘")]
    [Tooltip("아이콘 없이 들어온 지뢰(시작 지급/상점 구매)에 쓸 아이콘 - mine-32")]
    [SerializeField] private Sprite mineIcon;
    [Tooltip("아이콘 없이 들어온 폭탄(시작 지급/상점 구매)에 쓸 아이콘 - button-bomb-32")]
    [SerializeField] private Sprite bombIcon;
    [Tooltip("icon Image를 자동으로 만들 때 슬롯 테두리 안쪽으로 둘 여백(슬롯 한 변 대비 비율, 사방 동일)")]
    [SerializeField, Range(0f, 0.4f)] private float iconInset = 0.2f;

    [Header("클래스 고정 무기 슬롯 (스카우트는 무기가 없어 자동으로 숨겨짐)")]
    [SerializeField] private GameObject weaponSlotRoot;
    [SerializeField] private InventorySlotUI weaponSlot;
    [Tooltip("무기 슬롯을 감싸는 모서리 장식(CornerBrackets) 이미지들. 지정하면 무기 슬롯은 배경 없이 이 장식만 보이고, " +
        "선택 여부는 장식 색으로 구분한다.")]
    [SerializeField] private Image[] weaponSlotCorners;
    [SerializeField] private Color weaponCornerSelectedColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color weaponCornerUnselectedColor = Color.white;

    [Header("슬롯 배경 이미지 - 둘 다 지정되면 아이템 유무와 상관없이 선택 여부로만 배경을 고른다 (비우면 아래 색상 방식)")]
    [SerializeField] private Sprite slotDefaultSprite;
    [SerializeField] private Sprite slotSelectedSprite;
    [Tooltip("배경 이미지를 쓸 때 라벨(숫자/아이템 이름) 색. 선택 배경은 밝은 색이라 어두운 글씨가 잘 보인다.")]
    [SerializeField] private Color unselectedLabelColor = Color.white;
    [SerializeField] private Color selectedLabelColor = new Color(0.36f, 0.2f, 0.14f, 1f);

    [SerializeField] private Color selectedFrameColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color unselectedFrameColor = new Color(0.25f, 0.25f, 0.28f, 0.9f);
    [SerializeField] private Color emptyFillColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color occupiedFillColor = new Color(0.2f, 0.2f, 0.22f, 0.9f);

    [Header("아이템 종류별 슬롯 색상 (스프라이트 없이도 한눈에 구분되도록)")]
    [SerializeField] private Color animalFillColor = new Color(0.35f, 0.55f, 0.25f, 0.9f);
    [SerializeField] private Color mineFillColor = new Color(0.6f, 0.45f, 0.1f, 0.9f);
    [SerializeField] private Color bombFillColor = new Color(0.55f, 0.15f, 0.15f, 0.9f);

    private static Sprite placeholderIcon;

    // 아이콘 없는 아이템에 대한 경고는 종류별로 한 번만 남긴다 - UpdateIcon이 매 프레임 불리므로.
    private readonly System.Collections.Generic.HashSet<InventoryItemType> warnedMissingIcon =
        new System.Collections.Generic.HashSet<InventoryItemType>();

    // 실제로 그리는 칸 목록: 씬의 slots + 칸 수가 늘어 복제해 붙인 칸.
    private readonly List<InventorySlotUI> slotViews = new List<InventorySlotUI>();
    private float slotPitch;
    private int laidOutCount = -1;

    private void Awake()
    {
        foreach (InventorySlotUI slot in slots)
        {
            if (slot != null && slot.icon == null && slot.frame != null) slot.icon = CreateIcon(slot);
            slotViews.Add(slot);
        }

        // 씬에 놓인 앞의 두 칸 간격(현재 183 = 칸 165 + 틈 18)을 그대로 쓴다.
        if (slots.Length >= 2 && slots[0]?.frame != null && slots[1]?.frame != null)
        {
            slotPitch = slots[1].frame.rectTransform.anchoredPosition.x - slots[0].frame.rectTransform.anchoredPosition.x;
        }
        else if (slots.Length == 1 && slots[0]?.frame != null)
        {
            slotPitch = slots[0].frame.rectTransform.rect.width;
        }
    }

    /// <summary>칸 수에 맞게 칸을 더하거나(마지막 칸 복제) 숨기고, 하단 중앙 기준으로 같은 간격으로 늘어놓는다.
    /// 칸 수가 바뀐 프레임에만 돈다.</summary>
    private void LayoutSlots(int count)
    {
        InventorySlotUI template = slots.Length > 0 ? slots[slots.Length - 1] : null;
        while (slotViews.Count < count && template?.frame != null)
        {
            slotViews.Add(CloneSlot(template, slotViews.Count));
        }

        int shown = Mathf.Min(count, slotViews.Count);
        for (int i = 0; i < slotViews.Count; i++)
        {
            InventorySlotUI slot = slotViews[i];
            if (slot?.frame == null) continue;

            bool active = i < shown;
            slot.frame.gameObject.SetActive(active);
            if (!active) continue;

            RectTransform rt = slot.frame.rectTransform;
            rt.anchoredPosition = new Vector2((i - (shown - 1) * 0.5f) * slotPitch, rt.anchoredPosition.y);
        }

        // 바 자체의 폭도 칸 수에 맞춘다 (칸은 바 가운데 기준이라 위치에는 영향이 없다).
        if (template?.frame != null && transform is RectTransform bar)
        {
            float slotWidth = template.frame.rectTransform.sizeDelta.x;
            bar.sizeDelta = new Vector2(shown * slotWidth + Mathf.Max(0, shown - 1) * (slotPitch - slotWidth), bar.sizeDelta.y);
        }

        laidOutCount = count;
    }

    private InventorySlotUI CloneSlot(InventorySlotUI template, int index)
    {
        Transform templateFrame = template.frame.transform;
        GameObject clone = Instantiate(template.frame.gameObject, templateFrame.parent, false);
        clone.name = $"Slot{index + 1}Frame";

        InventorySlotUI slot = new InventorySlotUI
        {
            frame = clone.GetComponent<Image>(),
            fill = FindClonePart(template.fill, templateFrame, clone.transform),
            label = FindClonePart(template.label, templateFrame, clone.transform),
            icon = FindClonePart(template.icon, templateFrame, clone.transform),
        };
        if (slot.fill != null) slot.fill.name = $"Slot{index + 1}Fill";
        return slot;
    }

    // 원본 칸 안에서 part가 있던 자리(자식 순서 경로)를 복제본에서 찾는다.
    private static T FindClonePart<T>(T part, Transform templateRoot, Transform cloneRoot) where T : Component
    {
        if (part == null) return null;
        if (part.transform == templateRoot) return cloneRoot.GetComponent<T>();

        List<int> path = new List<int>();
        for (Transform t = part.transform; t != null && t != templateRoot; t = t.parent) path.Insert(0, t.GetSiblingIndex());

        Transform current = cloneRoot;
        foreach (int sibling in path)
        {
            if (sibling >= current.childCount) return null;
            current = current.GetChild(sibling);
        }
        return current.GetComponent<T>();
    }

    /// <summary>frame 아래에 테두리 안쪽 여백(iconInset)을 둔 아이콘 Image를 만든다. 라벨(키 번호)보다
    /// 먼저 그려지도록 라벨 바로 앞 순서에 끼워 넣는다.</summary>
    private Image CreateIcon(InventorySlotUI slot)
    {
        GameObject go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(slot.frame.transform, false);
        rt.anchorMin = new Vector2(iconInset, iconInset);
        rt.anchorMax = new Vector2(1f - iconInset, 1f - iconInset);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (slot.label != null && slot.label.transform.parent == slot.frame.transform)
        {
            rt.SetSiblingIndex(slot.label.transform.GetSiblingIndex());
        }

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.enabled = false;
        return image;
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;

        bool weaponSelected = GameManager.Instance.IsWeaponSelected;
        int selected = GameManager.Instance.SelectedSlotIndex;
        int slotCount = GameManager.Instance.InventorySlotCount;
        if (slotCount != laidOutCount) LayoutSlots(slotCount);

        int count = Mathf.Min(slotViews.Count, slotCount);
        for (int i = 0; i < count; i++)
        {
            InventorySlotUI slot = slotViews[i];
            if (slot == null) continue;

            InventorySlotData data = GameManager.Instance.GetSlot(i);

            ApplySlotBackground(slot, !weaponSelected && i == selected, FillColorFor(data.Type));
            if (slot.label != null) slot.label.text = $"{i + 1}";
            UpdateIcon(slot.icon, data);
        }

        UpdateWeaponSlot(weaponSelected);
    }

    private void UpdateWeaponSlot(bool weaponSelected)
    {
        WeaponType equipped = GameManager.Instance.EquippedWeapon;
        if (weaponSlotRoot != null) weaponSlotRoot.SetActive(equipped != WeaponType.None);
        if (equipped == WeaponType.None || weaponSlot == null) return;

        if (weaponSlotCorners != null && weaponSlotCorners.Length > 0)
        {
            // 모서리 장식만으로 슬롯을 표시한다 - 인벤토리 칸 배경/채움은 끄고, 선택되면 장식 색이 바뀐다.
            if (weaponSlot.frame != null) weaponSlot.frame.enabled = false;
            if (weaponSlot.fill != null) weaponSlot.fill.enabled = false;
            if (weaponSlot.label != null) weaponSlot.label.color = unselectedLabelColor;
            foreach (Image corner in weaponSlotCorners)
            {
                if (corner != null) corner.color = weaponSelected ? weaponCornerSelectedColor : weaponCornerUnselectedColor;
            }
        }
        else
        {
            ApplySlotBackground(weaponSlot, weaponSelected, occupiedFillColor);
        }
        // 화면 오른쪽 위의 별도 "장착/탄약" 표시를 없애고 이 슬롯이 그 자리를 대신하므로, 마취총 남은 탄약도 여기에 표시한다.
        if (weaponSlot.label != null)
        {
            weaponSlot.label.text = equipped == WeaponType.Net
                ? "Q\n포획망"
                : $"Q\n마취총\n{GameManager.Instance.TranquilizerAmmo}/{GameManager.Instance.MaxTranquilizerAmmo}";
        }
    }

    /// <summary>슬롯 배경을 그린다. 배경 이미지가 지정돼 있으면 선택된 슬롯은 slotSelectedSprite, 나머지는 slotDefaultSprite를
    /// 테두리(frame) Image에 그대로(흰색 틴트) 그리고, 아이템 종류별 색 채움(fill)은 끈다 - 빈 칸이든 아이템이 있든 배경은
    /// 선택 여부로만 정해진다. 이미지가 비어 있으면 예전처럼 테두리 색 + 종류별 채움 색으로 그린다.</summary>
    private void ApplySlotBackground(InventorySlotUI slot, bool isSelected, Color fillColor)
    {
        bool useSprites = slotDefaultSprite != null && slotSelectedSprite != null;

        if (slot.frame != null)
        {
            if (useSprites)
            {
                slot.frame.sprite = isSelected ? slotSelectedSprite : slotDefaultSprite;
                slot.frame.color = Color.white;
                slot.frame.preserveAspect = true;
            }
            else
            {
                slot.frame.color = isSelected ? selectedFrameColor : unselectedFrameColor;
            }
        }

        if (slot.fill != null)
        {
            slot.fill.enabled = !useSprites;
            if (!useSprites) slot.fill.color = fillColor;
        }

        if (slot.label != null && useSprites) slot.label.color = isSelected ? selectedLabelColor : unselectedLabelColor;
    }

    /// <summary>빈 슬롯이면 아이콘을 끄고, 아니면 아이템의 아이콘을 비율 유지로 그린다. 아이템에 아이콘이
    /// 없으면 종류별 기본 아이콘(mineIcon/bombIcon)을, 그것도 없으면 임시 아이콘을 쓰고 경고를 남긴다.</summary>
    private void UpdateIcon(Image icon, InventorySlotData data)
    {
        if (icon == null) return;

        if (data.Type == InventoryItemType.None)
        {
            icon.enabled = false;
            return;
        }

        icon.enabled = true;
        icon.sprite = ResolveIcon(data);
        icon.preserveAspect = true;
    }

    private Sprite ResolveIcon(InventorySlotData data)
    {
        if (data.Icon != null) return data.Icon;

        Sprite fallback = data.Type switch
        {
            InventoryItemType.Mine => mineIcon,
            InventoryItemType.Bomb => bombIcon,
            _ => null
        };
        if (fallback != null) return fallback;

        if (warnedMissingIcon.Add(data.Type))
        {
            Debug.LogWarning($"InventoryUI: {data.Type} 아이템에 아이콘이 없어 임시 아이콘으로 표시합니다. " +
                "해당 아이템의 Inventory Icon(또는 InventoryUI의 종류별 기본 아이콘)을 지정하세요.", this);
        }
        return GetPlaceholderIcon();
    }

    /// <summary>아이콘 데이터가 없는 아이템용 임시 아이콘 - 눈에 띄는 마젠타/검정 체크무늬.</summary>
    private static Sprite GetPlaceholderIcon()
    {
        if (placeholderIcon != null) return placeholderIcon;

        const int size = 8;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[y * size + x] = ((x / 2 + y / 2) % 2 == 0) ? Color.magenta : Color.black;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();

        placeholderIcon = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return placeholderIcon;
    }

    private Color FillColorFor(InventoryItemType type) => type switch
    {
        InventoryItemType.Animal => animalFillColor,
        InventoryItemType.Mine => mineFillColor,
        InventoryItemType.Bomb => bombFillColor,
        _ => emptyFillColor
    };
}
