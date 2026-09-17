using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>숫자키 1~5로 인벤토리 슬롯을 선택한다. 선택 상태 자체는 GameManager에 있으므로,
/// 이 컴포넌트는 입력을 그쪽으로 전달하기만 한다 - VillageScene(지뢰/폭탄 사용, 동물 줍기)과
/// TruckScene(케이지에 납품할 동물 고르기) 양쪽의 Player에 붙는다.</summary>
public class InventorySlotSelector : MonoBehaviour
{
    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || GameManager.Instance == null) return;

        if (kb.digit1Key.wasPressedThisFrame) GameManager.Instance.SelectSlot(0);
        else if (kb.digit2Key.wasPressedThisFrame) GameManager.Instance.SelectSlot(1);
        else if (kb.digit3Key.wasPressedThisFrame) GameManager.Instance.SelectSlot(2);
        else if (kb.digit4Key.wasPressedThisFrame) GameManager.Instance.SelectSlot(3);
        else if (kb.digit5Key.wasPressedThisFrame) GameManager.Instance.SelectSlot(4);
    }
}
