using UnityEngine;
using UnityEngine.UI;

public class EquippedWeaponUI : MonoBehaviour
{
    [SerializeField] private Text weaponText;

    private WeaponType lastEquipped = (WeaponType)(-1);
    private int lastAmmo = int.MinValue;

    private void Update()
    {
        if (weaponText == null) return;

        WeaponType equipped = GameManager.Instance != null ? GameManager.Instance.EquippedWeapon : WeaponType.None;
        int ammo = equipped == WeaponType.TranquilizerGun && GameManager.Instance != null
            ? GameManager.Instance.TranquilizerAmmo
            : 0;

        // 무기/탄약이 그대로인 프레임에는 문자열을 새로 만들지 않는다 - 매 프레임 대입은 값이
        // 그대로여도 GC 할당과 캔버스 리빌드를 유발한다.
        if (equipped == lastEquipped && ammo == lastAmmo) return;

        weaponText.text = equipped switch
        {
            WeaponType.Net => "장착: 포획망",
            WeaponType.TranquilizerGun => BuildTranquilizerAmmoText(),
            _ => "장착: 없음 (스카우트)"
        };
        lastEquipped = equipped;
        lastAmmo = ammo;
    }

    private static string BuildTranquilizerAmmoText()
    {
        int ammo = GameManager.Instance != null ? GameManager.Instance.TranquilizerAmmo : 0;
        int max = GameManager.Instance != null ? GameManager.Instance.MaxTranquilizerAmmo : 10;
        return $"마취화살: {ammo}/{max}";
    }
}
