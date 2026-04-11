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
/// - Reserve ammo can be color-coded without changing the in-mag text.
/// </summary>
public class AmmoHUD : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text ammoText;

    [Header("Source (optional)")]
    public WeaponAmmo weaponAmmo;

    [Tooltip("If true, auto-binds to Player2Controller.activeWeaponAmmo when weaponAmmo is null.")]
    public bool autoBindFromPlayer = true;

    [Tooltip("Format: {0}=inMag, {1}=reserve (or colored reserve), {2}=magSize")]
    public string format = "{0}/{2}   |   {1}";

    [Header("Reserve Color States")]
    [Tooltip("If true, only the reserve ammo text changes color based on reserve amount.")]
    public bool colorReserveAmmo = true;

    [Tooltip("Color used for the top third of reserve ammo.")]
    public Color fullReserveColor = Color.green;

    [Tooltip("Color used for the middle third of reserve ammo.")]
    public Color midReserveColor = Color.yellow;

    [Tooltip("Color used for the bottom third of reserve ammo.")]
    public Color lowReserveColor = Color.red;

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

        string reserveDisplay = weaponAmmo.Reserve.ToString();

        if (colorReserveAmmo)
        {
            Color reserveColor = GetReserveColor();
            string colorHex = ColorUtility.ToHtmlStringRGB(reserveColor);
            reserveDisplay = $"<color=#{colorHex}>{reserveDisplay}</color>";
        }

        ammoText.text = string.Format(format, weaponAmmo.InMag, reserveDisplay, weaponAmmo.magazineSize);
    }

    Color GetReserveColor()
    {
        if (weaponAmmo == null)
            return midReserveColor;

        int maxReserveAmmo = Mathf.Max(0, weaponAmmo.totalAmmoStart - weaponAmmo.magazineSize);
        if (maxReserveAmmo <= 0)
            return midReserveColor;

        float reservePercent = (float)weaponAmmo.Reserve / maxReserveAmmo;

        if (reservePercent <= 0.33333334f)
            return lowReserveColor;

        if (reservePercent <= 0.6666667f)
            return midReserveColor;

        return fullReserveColor;
    }
}
