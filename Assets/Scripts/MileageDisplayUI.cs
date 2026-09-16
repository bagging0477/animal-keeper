using UnityEngine;
using UnityEngine.UI;

/// <summary>보유 마일리지 텍스트를 매 프레임 갱신한다. 예전에는 ShelterShop이 구매가 일어날 때만
/// 갱신했지만, 상점이 UI 버튼에서 걸어다니는 상호작용 오브젝트로 바뀌면서 구매 로직과
/// 분리해 항상 최신 값을 보여주게 했다.</summary>
public class MileageDisplayUI : MonoBehaviour
{
    [SerializeField] private Text mileageText;

    private void Update()
    {
        if (mileageText == null) return;

        int mileage = GameManager.Instance != null ? GameManager.Instance.Mileage : 0;
        mileageText.text = $"보유 마일리지: {mileage}";
    }
}
