using System;
using UnityEngine;

/// <summary>
/// Press G to play the "throw_grenade" animation AND launch a grenade in the direction the player is looking.
/// - Uses camera forward as the throw direction.
/// - Optionally rotates the Soldier_marine body to match the camera yaw so the throw looks correct.
/// - Optionally snaps Soldier_marine to a specific transform.
/// - Tracks grenade ammo and exposes an event so UI can react when grenade count changes.
///
/// Attach to Player (or any manager object).
/// </summary>
public class PlayerThrowGrenadeOnG_Aim : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root transform of the Soldier_marine rig (has the Animator).")]
    public Transform soldierMarineRoot;

    [Tooltip("Animator on Soldier_marine (auto-found if left null).")]
    public Animator animator;

    [Tooltip("Camera the player aims with (Main Camera).")]
    public Camera playerCamera;

    [Tooltip("Where the grenade spawns (recommended: an empty at the right hand / weaponSocket). If null, uses camera position.")]
    public Transform grenadeSpawn;

    [Tooltip("Grenade prefab to instantiate. Should have a Rigidbody for physics throw.")]
    public GameObject grenadePrefab;

    [Header("Input")]
    public KeyCode throwKey = KeyCode.G;

    [Header("Animator Trigger")]
    public string triggerName = "throw_grenade";

    [Header("Throw Settings")]
    [Tooltip("Initial forward speed of the grenade.")]
    public float throwSpeed = 12f;

    [Tooltip("Extra upward boost (adds arc).")]
    public float upwardBoost = 2.5f;

    [Tooltip("Optional cooldown (seconds) to prevent spam. 0 = none.")]
    public float cooldown = 0.35f;

    [Header("Grenade Ammo")]
    [Tooltip("If true, the player must have grenades available to throw.")]
    public bool useLimitedGrenades = true;

    [Tooltip("How many grenades the player starts with.")]
    public int startingGrenades = 3;

    [Tooltip("Maximum grenades the player can carry.")]
    public int maxGrenades = 3;

    [Tooltip("Current grenade count at runtime.")]
    public int currentGrenades = 3;

    [Header("Movement Inheritance")]
    [Tooltip("Adds some of the player's current movement velocity into the grenade throw so you can't outrun it.")]
    public float inheritedVelocityMultiplier = 1f;

    [Header("Match Body To Aim")]
    [Tooltip("Rotate the soldier to match camera yaw (left/right) so the throw faces where you look.")]
    public bool matchCameraYaw = true;

    [Tooltip("How fast the soldier rotates to match yaw (higher = snappier).")]
    public float yawRotateSpeed = 20f;

    [Header("Snap Transform Before Throw (Optional)")]
    public bool snapBeforeThrow = false;
    public bool useLocalTransform = true;

    // Default values match your screenshot (edit as needed)
    public Vector3 targetPosition = new Vector3(-0.06f, -1.789f, -1.2f);
    public Vector3 targetRotationEuler = Vector3.zero;
    public Vector3 targetScale = new Vector3(2f, 2f, 2f);

    [Header("Throw Audio (Optional)")]
    [Tooltip("AudioSource used to play the grenade throw sound. If empty and auto-find is enabled, this script will look on this object and its children.")]
    public AudioSource throwAudioSource;

    [Tooltip("Sound effect played when the grenade is successfully thrown.")]
    public AudioClip throwSFX;

    [Range(0f, 2f)]
    public float throwVolume = 1f;

    [Tooltip("If enabled, auto-finds an AudioSource when one is not assigned.")]
    public bool autoFindThrowAudioSource = true;

    [Header("Debug")]
    public bool debugLogs = false;

    /// <summary>
    /// Fired whenever grenade count changes. Sends currentGrenades and maxGrenades.
    /// </summary>
    public event Action<int, int> OnGrenadeCountChanged;

    private float _nextAllowedTime;
    private Rigidbody _ownerRb;
    private CharacterController _ownerCharacterController;

    private void Awake()
    {
        if (soldierMarineRoot != null && animator == null)
            animator = soldierMarineRoot.GetComponentInChildren<Animator>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;

        _ownerRb = GetComponent<Rigidbody>();
        _ownerCharacterController = GetComponent<CharacterController>();

        maxGrenades = Mathf.Max(0, maxGrenades);
        startingGrenades = Mathf.Clamp(startingGrenades, 0, maxGrenades > 0 ? maxGrenades : startingGrenades);
        currentGrenades = useLimitedGrenades ? startingGrenades : Mathf.Max(currentGrenades, startingGrenades);

        if (autoFindThrowAudioSource && throwAudioSource == null)
            throwAudioSource = GetComponentInChildren<AudioSource>(true);

        NotifyGrenadeCountChanged();
    }

    private void OnEnable()
    {
        NotifyGrenadeCountChanged();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(throwKey)) return;
        if (Time.time < _nextAllowedTime) return;

        if (useLimitedGrenades && currentGrenades <= 0)
        {
            if (debugLogs)
                Debug.Log($"{nameof(PlayerThrowGrenadeOnG_Aim)}: No grenades left.", this);
            return;
        }

        if (soldierMarineRoot == null)
        {
            Debug.LogError($"{nameof(PlayerThrowGrenadeOnG_Aim)}: soldierMarineRoot is not assigned.", this);
            return;
        }

        if (playerCamera == null)
        {
            Debug.LogError($"{nameof(PlayerThrowGrenadeOnG_Aim)}: playerCamera is not assigned (and no Main Camera found).", this);
            return;
        }

        if (animator == null)
        {
            animator = soldierMarineRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Animator not found on Soldier_marine.", this);
                return;
            }
        }

        if (snapBeforeThrow)
            SnapSoldier();

        if (matchCameraYaw)
            MatchYawToCamera();

        // 1) Trigger the throw animation
        animator.ResetTrigger(triggerName);
        animator.SetTrigger(triggerName);

        // 2) Launch grenade
        if (grenadePrefab != null)
        {
            bool threw = SpawnAndThrowGrenade();
            if (threw)
            {
                PlayThrowSound();

                if (useLimitedGrenades)
                {
                    currentGrenades = Mathf.Max(0, currentGrenades - 1);
                    NotifyGrenadeCountChanged();
                }

                if (debugLogs)
                    Debug.Log($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Grenade thrown. Remaining: {currentGrenades}/{maxGrenades}", this);
            }
        }

        if (cooldown > 0f)
            _nextAllowedTime = Time.time + cooldown;
    }

    private void MatchYawToCamera()
    {
        Vector3 camForward = playerCamera.transform.forward;
        camForward.y = 0f;

        if (camForward.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetYaw = Quaternion.LookRotation(camForward.normalized, Vector3.up);
        soldierMarineRoot.rotation = Quaternion.Slerp(soldierMarineRoot.rotation, targetYaw, yawRotateSpeed * Time.deltaTime);
    }

    private bool SpawnAndThrowGrenade()
    {
        Vector3 spawnPos;
        Quaternion spawnRot;

        if (grenadeSpawn != null)
        {
            spawnPos = grenadeSpawn.position;
            spawnRot = grenadeSpawn.rotation;
        }
        else
        {
            spawnPos = playerCamera.transform.position + playerCamera.transform.forward * 0.6f;
            spawnRot = playerCamera.transform.rotation;
        }

        GameObject g = Instantiate(grenadePrefab, spawnPos, spawnRot);
        if (g == null) return false;

        if (g.TryGetComponent<GrenadeProjectile>(out GrenadeProjectile projectile))
            projectile.SetOwner(gameObject);

        Vector3 dir = playerCamera.transform.forward.normalized;
        Vector3 velocity = dir * throwSpeed + Vector3.up * upwardBoost + GetOwnerVelocity() * inheritedVelocityMultiplier;

        if (g.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = velocity;
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * 4f, ForceMode.VelocityChange);

            if (debugLogs)
                Debug.Log($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Throw velocity = {velocity}", this);
        }
        else
        {
            Debug.LogWarning($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Grenade prefab has no Rigidbody. Add one for a real throw.", g);
        }

        return true;
    }

    private void PlayThrowSound()
    {
        if (throwSFX == null) return;

        if (throwAudioSource == null && autoFindThrowAudioSource)
            throwAudioSource = GetComponentInChildren<AudioSource>(true);

        if (throwAudioSource == null)
        {
            if (debugLogs)
                Debug.LogWarning($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Throw SFX assigned but no AudioSource is available.", this);
            return;
        }

        throwAudioSource.PlayOneShot(throwSFX, throwVolume);
    }

    private Vector3 GetOwnerVelocity()
    {
        if (_ownerRb != null)
            return _ownerRb.linearVelocity;

        if (_ownerCharacterController != null)
            return _ownerCharacterController.velocity;

        return Vector3.zero;
    }

    private void SnapSoldier()
    {
        if (useLocalTransform)
        {
            soldierMarineRoot.localPosition = targetPosition;
            soldierMarineRoot.localRotation = Quaternion.Euler(targetRotationEuler);
            soldierMarineRoot.localScale = targetScale;
        }
        else
        {
            soldierMarineRoot.position = targetPosition;
            soldierMarineRoot.rotation = Quaternion.Euler(targetRotationEuler);
            soldierMarineRoot.localScale = targetScale;
        }
    }

    public void RefillGrenadesToFull()
    {
        currentGrenades = maxGrenades > 0 ? maxGrenades : currentGrenades;
        NotifyGrenadeCountChanged();

        if (debugLogs)
            Debug.Log($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Grenades refilled to {currentGrenades}/{maxGrenades}", this);
    }

    public bool TryAddGrenades(int amount)
    {
        if (amount <= 0) return false;

        int old = currentGrenades;
        int cappedMax = maxGrenades > 0 ? maxGrenades : int.MaxValue;
        currentGrenades = Mathf.Clamp(currentGrenades + amount, 0, cappedMax);

        bool changed = currentGrenades != old;
        if (changed)
        {
            NotifyGrenadeCountChanged();

            if (debugLogs)
                Debug.Log($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Added {amount} grenades. Now {currentGrenades}/{maxGrenades}", this);
        }

        return changed;
    }

    public bool HasGrenades()
    {
        return !useLimitedGrenades || currentGrenades > 0;
    }

    public int GetCurrentGrenades()
    {
        return currentGrenades;
    }

    public int GetMaxGrenades()
    {
        return maxGrenades;
    }

    private void NotifyGrenadeCountChanged()
    {
        OnGrenadeCountChanged?.Invoke(currentGrenades, maxGrenades);
    }
}
