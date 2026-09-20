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

/// <summary>5칸 인벤토리 + 클래스 고정 무기 슬롯을 화면에 그린다. 선택된 슬롯은 테두리(frame) 색이
/// 강조색으로 바뀌어 구분되고, 각 슬롯의 라벨은 내용물(동물/지뢰/폭탄/빈칸/무기)에 맞는 텍스트로
/// 갱신된다. 무기 슬롯과 숫자 슬롯은 서로 배타적으로만 강조되므로(GameManager.IsWeaponSelected)
/// 항상 둘 중 하나만 강조된 채로 보인다.</summary>
public class InventoryUI : MonoBehaviour
{
    [SerializeField] private InventorySlotUI[] slots = new InventorySlotUI[GameManager.InventorySlotCount];

    [Header("클래스 고정 무기 슬롯 (스카우트는 무기가 없어 자동으로 숨겨짐)")]
    [SerializeField] private GameObject weaponSlotRoot;
    [SerializeField] private InventorySlotUI weaponSlot;

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

            if (slot.frame != null) slot.frame.color = !weaponSelected && i == selected ? selectedFrameColor : unselectedFrameColor;
            if (slot.fill != null) slot.fill.color = FillColorFor(data.Type);
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

        if (weaponSlot.frame != null) weaponSlot.frame.color = weaponSelected ? selectedFrameColor : unselectedFrameColor;
        if (weaponSlot.fill != null) weaponSlot.fill.color = occupiedFillColor;
        if (weaponSlot.label != null) weaponSlot.label.text = equipped == WeaponType.Net ? "Q\n포획망" : "Q\n마취총";
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
