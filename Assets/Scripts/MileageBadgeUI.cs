using TMPro;
using UnityEngine;

/// <summary>ShelterScene의 보유 마일리지 배지(나무 배경 + 노란 숫자). GameManager.Mileage를 읽어서 숫자만 보여 주고
/// 값은 절대 바꾸지 않는다. GameManager에는 마일리지 변경 이벤트가 없어서 매 프레임 값을 비교하고, 바뀐 프레임에만
/// 글자를 다시 만든다 - 구매/정산 직후 같은 프레임 안에 반영된다.</summary>
public class MileageBadgeUI : MonoBehaviour
{
    [SerializeField] private TMP_Text mileageText;

    private int shownMileage = int.MinValue;
    private bool warnedMissingManager;

    private void OnEnable()
    {
        shownMileage = int.MinValue;
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        if (mileageText == null) return;

        int mileage = 0;
        if (GameManager.Instance != null)
        {
            mileage = GameManager.Instance.Mileage;
        }
        else if (!warnedMissingManager)
        {
            warnedMissingManager = true;
            Debug.LogWarning($"{name}: GameManager가 없어 보유 마일리지를 0으로 표시한다.");
        }

        if (mileage == shownMileage) return;
        shownMileage = mileage;
        mileageText.SetText("{0}", mileage);
    }
}
