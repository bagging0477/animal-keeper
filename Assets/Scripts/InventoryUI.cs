using UnityEngine;
using UnityEngine.UI;

/// <summary>인벤토리 슬롯 하나의 화면 표시 요소 3개(테두리/배경/라벨) 묶음.</summary>
[System.Serializable]
public class InventorySlotUI
{
    public Image frame;
    public Image fill;
    public Text label;

    /// <summary>슬롯에 담긴 아이템의 실제 스프라이트(동물/지뢰/폭탄 아이콘)를 그리는 Image. 아이콘이
    /// 없는 아이템(아직 아이콘이 지정되지 않은 지뢰/폭탄 등)이나 빈 슬롯에서는 비활성화되어, 기존의
    /// 색상 채움(fill)만으로 표시되는 상태를 유지한다.</summary>
    public Image icon;
}

/// <summary>5칸 인벤토리 + 클래스 고정 무기 슬롯을 화면에 그린다. 선택된 슬롯은 선택용 배경 이미지로
/// 바뀌어 구분되고(ApplySlotBackground), 각 슬롯의 라벨은 내용물(동물/지뢰/폭탄/빈칸/무기)에 맞는 텍스트로
/// 갱신된다. 무기 슬롯과 숫자 슬롯은 서로 배타적으로만 강조되므로(GameManager.IsWeaponSelected)
/// 항상 둘 중 하나만 강조된 채로 보인다.</summary>
public class InventoryUI : MonoBehaviour
{
    [SerializeField] private InventorySlotUI[] slots = new InventorySlotUI[GameManager.InventorySlotCount];

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

    private void Update()
    {
        if (GameManager.Instance == null) return;

        bool weaponSelected = GameManager.Instance.IsWeaponSelected;
        int selected = GameManager.Instance.SelectedSlotIndex;
        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlotUI slot = slots[i];
            if (slot == null) continue;

            InventorySlotData data = GameManager.Instance.GetSlot(i);

            ApplySlotBackground(slot, !weaponSelected && i == selected, FillColorFor(data.Type));
            if (slot.label != null) slot.label.text = BuildLabel(i, data);
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

    /// <summary>아이콘 스프라이트가 있으면 보여주고, 없으면(빈 슬롯, 또는 아직 아이콘이 지정되지 않은
    /// 지뢰/폭탄) 꺼서 기존의 색상 채움(fill)만 보이는 상태로 되돌린다.</summary>
    private static void UpdateIcon(Image icon, InventorySlotData data)
    {
        if (icon == null) return;

        if (data.Type != InventoryItemType.None && data.Icon != null)
        {
            icon.enabled = true;
            icon.sprite = data.Icon;
            icon.preserveAspect = true;
        }
        else
        {
            icon.enabled = false;
        }
    }

    private Color FillColorFor(InventoryItemType type) => type switch
    {
        InventoryItemType.Animal => animalFillColor,
        InventoryItemType.Mine => mineFillColor,
        InventoryItemType.Bomb => bombFillColor,
        _ => emptyFillColor
    };

    private static string BuildLabel(int index, InventorySlotData data)
    {
        string number = $"{index + 1}";
        return data.Type switch
        {
            InventoryItemType.Animal => $"{number}\n동물\n{data.AnimalWeight}kg",
            InventoryItemType.Mine => $"{number}\n지뢰",
            InventoryItemType.Bomb => $"{number}\n폭탄",
            _ => number
        };
    }
}
