using TMPro;
using UnityEngine;

/// <summary>
/// Put this on a UI object (Canvas).
/// Assign a TMP_Text and (optionally) a WeaponAmmo source.
/// 
/// Robust behavior:
/// - If weaponAmmo isn't assigned, it will try to auto-bind from Player2Controller.activeWeaponAmmo.
/// - It refreshes every frame as a safe fallback (so even if events aren't wired, UI still matches reality).
/// - If WeaponAmmo is assigned, it will also subscribe to OnAmmoChanged for efficiency.
/// </summary>
public class AmmoHUD : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text ammoText;

    [Header("Source (optional)")]
    public WeaponAmmo weaponAmmo;

    [Tooltip("If true, auto-binds to Player2Controller.activeWeaponAmmo when weaponAmmo is null.")]
    public bool autoBindFromPlayer = true;

    [Tooltip("Format: {0}=inMag, {1}=reserve, {2}=magSize")]
    public string format = "{0}/{2}   |   {1}";

    Player2Controller cachedPlayer;

    void Awake()
    {
        if (ammoText == null)
            ammoText = GetComponentInChildren<TMP_Text>(true);
    }

    void OnEnable()
    {
        Hook(weaponAmmo);
        Refresh();
    }

    void OnDisable()
    {
        Unhook();
    }

    void Update()
    {
        // Fallback: keep UI accurate even if events weren't wired.
        if (autoBindFromPlayer && weaponAmmo == null)
            TryAutoBind();

        Refresh();
    }

    void TryAutoBind()
    {
        if (cachedPlayer == null)
            cachedPlayer = FindFirstObjectByType<Player2Controller>();

        if (cachedPlayer != null && cachedPlayer.activeWeaponAmmo != null)
            Hook(cachedPlayer.activeWeaponAmmo);
    }

    public void Hook(WeaponAmmo newSource)
    {
        if (weaponAmmo == newSource) return;

        Unhook();
        weaponAmmo = newSource;

        if (weaponAmmo != null)
            weaponAmmo.OnAmmoChanged += HandleAmmoChanged;

        Refresh();
    }

    void Unhook()
    {
        if (weaponAmmo != null)
            weaponAmmo.OnAmmoChanged -= HandleAmmoChanged;
    }

    void HandleAmmoChanged(WeaponAmmo a) => Refresh();

    public void Refresh()
    {
        if (ammoText == null) return;

        if (weaponAmmo == null)
        {
            ammoText.text = "--";
            return;
        }

        ammoText.text = string.Format(format, weaponAmmo.InMag, weaponAmmo.Reserve, weaponAmmo.magazineSize);
    }
}
