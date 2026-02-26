using UnityEngine;

/// <summary>
/// Put this on each weapon GameObject (Rifle, Pistol).
/// When the weapon becomes active (enabled), it sets the CrosshairRecoilUI global multiplier.
/// This lets you have stronger pistol recoil without changing your shooting code.
/// </summary>
public class WeaponCrosshairRecoilSettings : MonoBehaviour
{
    [Tooltip("How strong crosshair recoil should be while this weapon is active. 1 = default.")]
    [Range(0.1f, 5f)]
    public float recoilMultiplier = 1f;

    [Tooltip("If true, resets multiplier back to 1 when this weapon is disabled.")]
    public bool resetOnDisable = true;

    private void OnEnable()
    {
        if (CrosshairRecoilUI.Instance != null)
            CrosshairRecoilUI.Instance.SetGlobalIntensity(recoilMultiplier);
    }

    private void OnDisable()
    {
        if (!resetOnDisable) return;

        if (CrosshairRecoilUI.Instance != null)
            CrosshairRecoilUI.Instance.SetGlobalIntensity(1f);
    }
}
