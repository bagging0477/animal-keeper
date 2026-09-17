using UnityEngine;
using UnityEngine.UI;

/// <summary>보유 마일리지 텍스트를 매 프레임 갱신한다. 예전에는 ShelterShop이 구매가 일어날 때만
/// 갱신했지만, 상점이 UI 버튼에서 걸어다니는 상호작용 오브젝트로 바뀌면서 구매 로직과
/// 분리해 항상 최신 값을 보여주게 했다.</summary>
public class MileageDisplayUI : MonoBehaviour
{
    [SerializeField] private Text mileageText;

    private int lastMileage = int.MinValue;

    private void Update()
    {
        if (mileageText == null) return;

        int mileage = GameManager.Instance != null ? GameManager.Instance.Mileage : 0;

        // 마일리지가 그대로인 프레임에는 문자열을 새로 만들지 않는다 - 매 프레임 대입은 값이
        // 그대로여도 GC 할당과 캔버스 리빌드를 유발한다.
        if (mileage == lastMileage) return;

        mileageText.text = $"보유 마일리지: {mileage}";
        lastMileage = mileage;
    }
}
