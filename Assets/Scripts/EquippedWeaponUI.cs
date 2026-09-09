using UnityEngine;
using UnityEngine.UI;

public class EquippedWeaponUI : MonoBehaviour
{
    [SerializeField] private Text weaponText;

    private void Update()
    {
        if (weaponText == null) return;

        WeaponType equipped = GameManager.Instance != null ? GameManager.Instance.EquippedWeapon : WeaponType.None;
        weaponText.text = equipped switch
        {
            WeaponType.Net => "장착: 포획망 (1)",
            WeaponType.TranquilizerGun => "장착: 마취총 (2)",
            _ => "장착: 없음"
        };
    }
}
