using UnityEngine;

/// <summary>
/// Drop-in solution: changes crosshair sprite/size based on which gun is currently active,
/// WITHOUT requiring you to edit your weapon switch code.
///
/// Put this on Player (or any always-active object).
/// Assign your pistol & rifle weapon GameObjects + the desired sprites/scales.
///
/// It will watch which weapon is active (activeInHierarchy) and apply the proper crosshair.
/// </summary>
public class WeaponCrosshairAutoSwitcher : MonoBehaviour
{
    [Header("Weapon References (recommended)")]
    public GameObject pistolWeaponObject;
    public GameObject rifleWeaponObject;

    [Header("Pistol Crosshair")]
    public Sprite pistolCrosshair;
    [Range(0.1f, 4f)] public float pistolScale = 0.6f;
    [Range(-1f, 1f)] public float pistolBloomAlpha = -1f;

    [Header("Rifle Crosshair")]
    public Sprite rifleCrosshair;
    [Range(0.1f, 4f)] public float rifleScale = 1.0f;
    [Range(-1f, 1f)] public float rifleBloomAlpha = -1f;

    [Header("Behavior")]
    [Tooltip("How often to check for weapon changes (seconds). 0 = every frame.")]
    public float pollInterval = 0.05f;

    [Tooltip("If no weapon is detected active, reset to default.")]
    public bool resetToDefaultWhenUnknown = false;

    private float _nextPollTime = 0f;
    private int _lastWeaponId = -999;

    private void Awake()
    {
        // Try to auto-find if user didn't assign, using Player2Controller if present.
        if (pistolWeaponObject == null || rifleWeaponObject == null)
        {
            var p2 = GetComponentInParent<Player2Controller>();
            if (p2 == null && Player2Controller.instance != null) p2 = Player2Controller.instance;

            // In your project, p2.guns is Gun[] (components), not GameObject[].
            if (p2 != null && p2.guns != null)
            {
                foreach (var gun in p2.guns)
                {
                    if (gun == null) continue;

                    GameObject go = gun.gameObject;
                    string n = go.name.ToLowerInvariant();

                    if (pistolWeaponObject == null && n.Contains("pistol"))
                        pistolWeaponObject = go;

                    if (rifleWeaponObject == null && (n.Contains("rifle") || n.Contains("ar") || n.Contains("assault")))
                        rifleWeaponObject = go;
                }
            }
        }
    }

    private void Update()
    {
        if (pollInterval > 0f && Time.time < _nextPollTime) return;
        _nextPollTime = Time.time + pollInterval;

        int weaponId = GetCurrentWeaponId();
        if (weaponId == _lastWeaponId) return;
        _lastWeaponId = weaponId;

        ApplyCrosshairForWeaponId(weaponId);
    }

    private int GetCurrentWeaponId()
    {
        // 0 = pistol, 1 = rifle, -1 unknown
        if (pistolWeaponObject != null && pistolWeaponObject.activeInHierarchy) return 0;
        if (rifleWeaponObject != null && rifleWeaponObject.activeInHierarchy) return 1;

        // Fallback: try Player2Controller activeWeaponAmmo naming
        var p2 = GetComponentInParent<Player2Controller>();
        if (p2 == null && Player2Controller.instance != null) p2 = Player2Controller.instance;

        if (p2 != null && p2.activeWeaponAmmo != null)
        {
            string n = p2.activeWeaponAmmo.gameObject.name.ToLowerInvariant();
            if (n.Contains("pistol")) return 0;
            if (n.Contains("rifle") || n.Contains("ar") || n.Contains("assault")) return 1;
        }

        return -1;
    }

    private void ApplyCrosshairForWeaponId(int weaponId)
    {
        var mgr = CrosshairImageManager.Instance;
        if (mgr == null) return;

        if (weaponId == 0 && pistolCrosshair != null)
        {
            mgr.SetCrosshair(pistolCrosshair, pistolScale, pistolBloomAlpha);
            return;
        }

        if (weaponId == 1 && rifleCrosshair != null)
        {
            mgr.SetCrosshair(rifleCrosshair, rifleScale, rifleBloomAlpha);
            return;
        }

        if (resetToDefaultWhenUnknown)
            mgr.ResetToDefault();
    }
}
