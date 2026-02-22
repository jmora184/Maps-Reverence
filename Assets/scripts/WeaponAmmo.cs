using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drop this on each weapon prefab (Rifle, Pistol).
///
/// IMPORTANT:
/// - Animator is OPTIONAL.
/// - If your reload is handled by a separate script (like PlayerWeaponReloadAnimation),
///   you can wire that script in the Inspector via "On Reload Requested" UnityEvent.
///
/// Firing script should call TryConsumeRound() BEFORE firing.
/// This script will auto-reload when the mag hits 0 (if reserve > 0),
/// unless you're out of all ammo.
/// </summary>
public class WeaponAmmo : MonoBehaviour
{
    [Header("Ammo Setup")]
    [Tooltip("Rounds per magazine (Rifle=30, Pistol=8).")]
    public int magazineSize = 30;

    [Tooltip("Total rounds the weapon starts with INCLUDING the loaded mag + reserve.")]
    public int totalAmmoStart = 1000;

    [Tooltip("Seconds to complete reload. Should match your reload animation length.")]
    public float reloadDuration = 1.4f;

    [Header("Reload Request (recommended)")]
    [Tooltip("Hook your existing reload animation script here (eg: PlayerWeaponReloadAnimation.StartReload).")]
    public UnityEvent OnReloadRequested;

    [Header("Animator (optional fallback)")]
    [Tooltip("Optional Animator on the weapon (or player). Leave empty if you use OnReloadRequested instead.")]
    public Animator animator;

    [Tooltip("Animator trigger parameter name to start reload.")]
    public string reloadTrigger = "Reload";

    [Tooltip("Optional bool parameter name to indicate reloading (leave blank to skip).")]
    public string isReloadingBool = "IsReloading";

    [Header("Behavior")]
    [Tooltip("If true, automatically reload when mag hits 0 (if reserve > 0).")]
    public bool autoReloadWhenEmpty = true;

    [Tooltip("If true, blocks TryConsumeRound while reloading.")]
    public bool blockFireWhileReloading = true;

    

    [Header("Manual Reload Input (optional)")]
    [Tooltip("If true, this component listens for a reload key press and starts reload (it will also trigger your OnReloadRequested animation event).")]
    public bool enableManualReloadKey = true;

    [Tooltip("Key to manually reload this weapon (set to None if you handle input elsewhere).")]
    public KeyCode reloadKey = KeyCode.N;
public int InMag { get; private set; }
    public int Reserve { get; private set; }
    public bool IsReloading { get; private set; }

    public event Action<WeaponAmmo> OnAmmoChanged;
    public event Action<WeaponAmmo> OnReloadStarted;
    public event Action<WeaponAmmo> OnReloadFinished;

    void Awake()
    {
        // If you DID want to use animator and forgot to assign it, try to find one.
        if (animator == null) animator = GetComponentInParent<Animator>();
        ResetAmmoToDefaults();
    }

    
    void Update()
    {
        if (!enableManualReloadKey) return;
        if (reloadKey == KeyCode.None) return;

        if (Input.GetKeyDown(reloadKey))
            StartReload();
    }

public void ResetAmmoToDefaults()
    {
        int total = Mathf.Max(0, totalAmmoStart);
        int load = Mathf.Min(magazineSize, total);
        InMag = load;
        Reserve = total - load;
        IsReloading = false;
        OnAmmoChanged?.Invoke(this);
    }

    /// <summary>
    /// Call this BEFORE firing. Returns true if a round was consumed.
    /// If mag becomes empty after consuming, it may auto-start reload.
    /// </summary>
    public bool TryConsumeRound()
    {
        if (blockFireWhileReloading && IsReloading) return false;

        if (InMag > 0)
        {
            InMag--;
            OnAmmoChanged?.Invoke(this);

            if (InMag == 0 && autoReloadWhenEmpty && Reserve > 0)
                StartReload();

            return true;
        }

        // No rounds in mag
        if (!IsReloading && Reserve > 0)
        {
            // If player tried to shoot empty mag, reload (even if autoReload disabled).
            StartReload();
        }

        return false;
    }

    public void StartReload()
    {
        if (IsReloading) return;
        if (Reserve <= 0) return;
        if (InMag >= magazineSize) return;

        StartCoroutine(ReloadRoutine());
    }

    IEnumerator ReloadRoutine()
    {
        IsReloading = true;

        // 1) Preferred: call your existing reload controller
        if (OnReloadRequested != null)
            OnReloadRequested.Invoke();

        // 2) Optional fallback: animator trigger
        if (animator != null)
        {
            if (!string.IsNullOrEmpty(isReloadingBool))
                animator.SetBool(isReloadingBool, true);

            if (!string.IsNullOrEmpty(reloadTrigger))
                animator.SetTrigger(reloadTrigger);
        }

        OnReloadStarted?.Invoke(this);
        OnAmmoChanged?.Invoke(this);

        yield return new WaitForSeconds(reloadDuration);

        // Fill mag from reserve
        int needed = magazineSize - InMag;
        int taken = Mathf.Min(needed, Reserve);
        InMag += taken;
        Reserve -= taken;

        IsReloading = false;

        if (animator != null && !string.IsNullOrEmpty(isReloadingBool))
            animator.SetBool(isReloadingBool, false);

        OnReloadFinished?.Invoke(this);
        OnAmmoChanged?.Invoke(this);
    }

    /// <summary>
    /// Optional: add ammo pickups etc.
    /// Adds to reserve (never exceeds int.MaxValue).
    /// </summary>
    public void AddReserve(int amount)
    {
        if (amount <= 0) return;
        long r = (long)Reserve + amount;
        Reserve = (int)Mathf.Clamp(r, 0, int.MaxValue);
        OnAmmoChanged?.Invoke(this);
    }
}
