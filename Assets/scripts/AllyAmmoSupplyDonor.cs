using UnityEngine;

/// <summary>
/// Attach to an ally root. Handles one-time ammo donation to the player,
/// then downgrades that ally to a pistol setup for the rest of its life.
///
/// Convenience:
/// - Auto-finds AllyController / AllyActivationGate / AudioSource if left empty.
/// - Auto-finds child weapon objects by name if left empty.
///
/// This version is hardened so prompt eligibility still works even if refs were
/// not manually wired in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class AllyAmmoSupplyDonor : MonoBehaviour
{
    [Header("Core Refs")]
    public AllyController allyController;
    public AllyActivationGate activationGate;

    [Header("One-Time Donation")]
    [SerializeField] private bool hasDonatedAmmo = false;

    [Header("Weapon Downgrade")]
    [Tooltip("Any current rifle/shotgun/sniper weapon child objects to disable after donating ammo.")]
    public GameObject[] weaponObjectsToDisableOnDonate;

    [Tooltip("Pistol child object to enable after donating ammo.")]
    public GameObject pistolWeaponObjectToEnable;

    [Tooltip("Bullet prefab the ally should use after donating ammo.")]
    public GameObject pistolBulletPrefab;

    [Tooltip("Halve shots-per-second by multiplying AllyController.fireRate by this amount. For cooldown-based fireRate, use 2.")]
    public float donatedFireRateMultiplier = 2f;

    [Header("Auto-Find Weapon Objects By Name")]
    [Tooltip("If true, and the weapon object refs are empty, this script auto-finds them under the ally by name.")]
    public bool autoFindWeaponObjectsByName = true;

    [Tooltip("Default current weapon child name to disable after donating ammo.")]
    public string defaultWeaponToDisableName = "w_scar";

    [Tooltip("Default pistol child name to enable after donating ammo.")]
    public string defaultPistolWeaponName = "w_glock";

    [Header("Optional Audio")]
    public AudioClip donateSfx;
    public AudioSource donateAudioSource;
    [Range(0f, 2f)] public float donateVolume = 1f;

    private float _originalFireRate = -1f;
    private bool _cachedOriginalFireRate = false;

    public bool HasDonatedAmmo => hasDonatedAmmo;

    private void Awake()
    {
        EnsureCoreRefs();
        AutoResolveWeaponObjectsIfNeeded();

        if (hasDonatedAmmo)
            ApplySavedDonationState(true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsureCoreRefs();
            AutoResolveWeaponObjectsIfNeeded();
        }
    }
#endif

    public bool IsEligibleForDonation(Transform player, float range)
    {
        EnsureCoreRefs();

        if (hasDonatedAmmo) return false;
        if (!gameObject.activeInHierarchy) return false;
        if (player == null) return false;

        // Prompt eligibility should not fail just because AllyController wasn't manually assigned.
        // If an activation gate exists, require that the ally is active.
        if (activationGate != null && !activationGate.IsActive) return false;

        float dist = Vector3.Distance(player.position, transform.position);
        return dist <= Mathf.Max(0.01f, range);
    }

    public bool TryDonateToPlayer(WeaponAmmo rifleAmmo, WeaponAmmo pistolAmmo, int rifleRounds, int pistolRounds)
    {
        EnsureCoreRefs();

        if (hasDonatedAmmo) return false;
        if (!gameObject.activeInHierarchy) return false;
        if (activationGate != null && !activationGate.IsActive) return false;

        if (rifleAmmo != null && rifleRounds > 0)
            rifleAmmo.AddReserve(rifleRounds);

        if (pistolAmmo != null && pistolRounds > 0)
            pistolAmmo.AddReserve(pistolRounds);

        ApplySavedDonationState(true);
        PlayDonateSfx();
        return true;
    }

    public void ApplySavedDonationState(bool donated)
    {
        EnsureCoreRefs();
        AutoResolveWeaponObjectsIfNeeded();

        hasDonatedAmmo = donated;
        if (!hasDonatedAmmo)
            return;

        ApplyPostDonationDowngrade();
    }

    public void ResetDonationState()
    {
        hasDonatedAmmo = false;
    }

    private void ApplyPostDonationDowngrade()
    {
        EnsureCoreRefs();
        AutoResolveWeaponObjectsIfNeeded();

        if (weaponObjectsToDisableOnDonate != null)
        {
            for (int i = 0; i < weaponObjectsToDisableOnDonate.Length; i++)
            {
                var go = weaponObjectsToDisableOnDonate[i];
                if (go != null) go.SetActive(false);
            }
        }

        if (pistolWeaponObjectToEnable != null)
            pistolWeaponObjectToEnable.SetActive(true);

        if (allyController != null)
        {
            if (!_cachedOriginalFireRate)
            {
                _originalFireRate = allyController.fireRate;
                _cachedOriginalFireRate = true;
            }

            if (pistolBulletPrefab != null)
                allyController.bullet = pistolBulletPrefab;

            allyController.fireRate = Mathf.Max(0.01f, _originalFireRate * Mathf.Max(1f, donatedFireRateMultiplier));
        }
    }

    private void PlayDonateSfx()
    {
        if (donateSfx == null) return;

        if (donateAudioSource == null)
            donateAudioSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>(true);

        if (donateAudioSource != null)
            donateAudioSource.PlayOneShot(donateSfx, Mathf.Clamp(donateVolume, 0f, 2f));
    }

    private void EnsureCoreRefs()
    {
        if (allyController == null)
            allyController = GetComponent<AllyController>() ?? GetComponentInParent<AllyController>() ?? GetComponentInChildren<AllyController>(true);

        if (activationGate == null)
            activationGate = GetComponent<AllyActivationGate>() ?? GetComponentInParent<AllyActivationGate>() ?? GetComponentInChildren<AllyActivationGate>(true);

        if (donateAudioSource == null)
            donateAudioSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>(true);
    }

    private void AutoResolveWeaponObjectsIfNeeded()
    {
        if (!autoFindWeaponObjectsByName) return;

        bool needsDisableWeapon = weaponObjectsToDisableOnDonate == null || weaponObjectsToDisableOnDonate.Length == 0 || weaponObjectsToDisableOnDonate[0] == null;
        bool needsPistolWeapon = pistolWeaponObjectToEnable == null;

        if (needsDisableWeapon && !string.IsNullOrWhiteSpace(defaultWeaponToDisableName))
        {
            GameObject foundDefault = FindChildGameObjectRecursive(transform, defaultWeaponToDisableName);
            if (foundDefault != null)
                weaponObjectsToDisableOnDonate = new GameObject[] { foundDefault };
        }

        if (needsPistolWeapon && !string.IsNullOrWhiteSpace(defaultPistolWeaponName))
        {
            pistolWeaponObjectToEnable = FindChildGameObjectRecursive(transform, defaultPistolWeaponName);
        }
    }

    private static GameObject FindChildGameObjectRecursive(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName)) return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null) continue;
            if (t == root) continue;
            if (t.name == exactName)
                return t.gameObject;
        }

        return null;
    }
}
