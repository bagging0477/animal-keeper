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
            WeaponType.Net => "장착: 포획망",
            WeaponType.TranquilizerGun => BuildTranquilizerAmmoText(),
            _ => "장착: 없음 (스카우트)"
        };
    }

    private static string BuildTranquilizerAmmoText()
    {
        int ammo = GameManager.Instance != null ? GameManager.Instance.TranquilizerAmmo : 0;
        int max = GameManager.Instance != null ? GameManager.Instance.MaxTranquilizerAmmo : 10;
        return $"마취화살: {ammo}/{max}";
    }
}
