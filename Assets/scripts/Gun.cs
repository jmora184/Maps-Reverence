using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple gun data container used by your player/gun controller.
/// Added: reliable muzzle flash trigger so the flash shows on EVERY shot,
/// even when the ParticleSystem is already playing (common with high fire rate).
/// </summary>
public class Gun : MonoBehaviour
{
    [Header("Projectile")]
    public GameObject bullet;

    [Header("Firing")]
    public bool canAutoFire;
    [Tooltip("Shots per second. Example: 5 = one shot every 0.2s")]
    public float fireRate = 5f;

    [HideInInspector]
    public float fireCounter;

    [Header("Ammo")]
    public int currentAmmo, pickupAmount;

    [Header("References")]
    public Transform firePoint;

    [Header("Zoom / ADS")]
    public float zoomAmount;

    [Header("Accuracy / Spread (degrees)")]
    [Tooltip("Base spread in degrees when hip-firing (not aiming). Bigger = less accurate.")]
    public float hipSpreadDeg = 1.5f;

    [Tooltip("Base spread in degrees when aiming down sights (ADS). Usually much smaller than hipSpreadDeg.")]
    public float adsSpreadDeg = 0.35f;

    [Tooltip("How much extra spread is added per shot while holding fire (bloom).")]
    public float spreadPerShotDeg = 0.10f;

    [Tooltip("Maximum extra spread that can build up from sustained fire (bloom cap).")]
    public float maxExtraSpreadDeg = 2.0f;

    [Tooltip("How quickly extra spread recovers back to 0 when you stop firing (degrees per second).")]
    public float spreadRecoveryPerSec = 2.5f;


    [Header("Info")]
    public string gunName;

    [Header("VFX (optional)")]
    [Tooltip("Assign the ParticleSystem used as muzzle flash for this gun (pistol, rifle, etc.).")]
    public ParticleSystem muzzleFlashPS;

    [Tooltip("If true, we Stop/Clear then Play so it restarts every time (best for muzzle flashes).")]
    public bool restartMuzzleFlashEachShot = true;

    [Tooltip("Alternative mode: directly emit N particles each shot. Works best if your PS is set up as 'one puff'.")]
    public bool useEmitInsteadOfPlay = false;

    [Min(1)]
    public int emitCount = 1;

    private void Update()
    {
        if (fireCounter > 0f)
            fireCounter -= Time.deltaTime;
    }

    /// <summary>
    /// Call this from your firing code RIGHT AFTER a successful shot.
    /// </summary>
    public void TriggerMuzzleFlash()
    {
        if (muzzleFlashPS == null) return;

        // If you fire faster than the PS duration, Play() alone won't "re-trigger".
        // Stop+Clear then Play ensures it flashes every shot.
        if (useEmitInsteadOfPlay)
        {
            // Emit works even if it's already playing.
            muzzleFlashPS.Emit(emitCount);
            return;
        }

        if (restartMuzzleFlashEachShot)
            muzzleFlashPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else
            muzzleFlashPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        muzzleFlashPS.Play(true);
    }

    public void GetAmmo()
    {
        currentAmmo += pickupAmount;

        if (UIController.instance != null && UIController.instance.ammoText != null)
            UIController.instance.ammoText.text = "AMMO:" + currentAmmo;
    }
}
