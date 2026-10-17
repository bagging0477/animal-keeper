using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>숫자키(1부터 인벤토리 칸 수까지, 최대 9)로 인벤토리 슬롯을, Q로 클래스 고정 무기 슬롯을 선택한다. 칸 수보다 큰
/// 숫자키는 아무 일도 하지 않는다. 선택 상태 자체는 GameManager에 있으므로, 이 컴포넌트는 입력을 그쪽으로 전달하기만 한다 -
/// VillageScene(지뢰/폭탄 사용, 무기 사용, 동물 줍기)과 TruckScene(케이지에 납품할 동물 고르기) 양쪽의 Player에 붙는다.</summary>
public class InventorySlotSelector : MonoBehaviour
{
    private const int MaxDigitKeys = 9;

    private void Update()
    {
        Keyboard kb = GameplayInput.IsBlocked ? null : Keyboard.current;
        if (kb == null || GameManager.Instance == null) return;

        int keyCount = Mathf.Min(GameManager.Instance.InventorySlotCount, MaxDigitKeys);
        for (int i = 0; i < keyCount; i++)
        {
            if (!kb[Key.Digit1 + i].wasPressedThisFrame) continue;
            GameManager.Instance.SelectSlot(i);
            return;
        }

        if (kb.qKey.wasPressedThisFrame) GameManager.Instance.SelectWeapon();
    }
}
