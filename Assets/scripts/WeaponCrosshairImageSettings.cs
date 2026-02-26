using UnityEngine;

/// <summary>
/// Put this on each weapon GameObject (Rifle, Pistol).
/// When the weapon becomes active (enabled), it sets the Crosshair sprite/scale.
/// Works best if your weapon switching enables/disables weapon GameObjects.
/// </summary>
public class WeaponCrosshairImageSettings : MonoBehaviour
{
    [Header("Crosshair Look For This Weapon")]
    public Sprite crosshairSprite;

    [Tooltip("Optional: scale the crosshair image (1 = default).")]
    [Range(0.1f, 4f)]
    public float crosshairScale = 1f;

    [Tooltip("Optional: set bloom alpha (0..1). Set to -1 to leave unchanged.")]
    [Range(-1f, 1f)]
    public float bloomAlpha = -1f;

    [Header("Behavior")]
    [Tooltip("If true, resets crosshair to default when this weapon is disabled.")]
    public bool resetOnDisable = false;

    private void OnEnable()
    {
        if (CrosshairImageManager.Instance != null && crosshairSprite != null)
            CrosshairImageManager.Instance.SetCrosshair(crosshairSprite, crosshairScale, bloomAlpha);
    }

    private void OnDisable()
    {
        if (!resetOnDisable) return;

        if (CrosshairImageManager.Instance != null)
            CrosshairImageManager.Instance.ResetToDefault();
    }
}
