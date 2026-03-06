using UnityEngine;

/// <summary>
/// Press G to trigger the grenade throw animation, then release the grenade with an Animation Event.
/// Designed for camera-based throwing in an FPS setup.
/// 
/// Animation setup:
/// - Add an Animation Event on the exact release frame that calls: ReleaseGrenade
/// - Add an Animation Event near the end of the throw animation that calls: EndThrow
/// </summary>
public class PlayerGrenadeThrow : MonoBehaviour
{
    [Header("References")]
    public GameObject grenadePrefab;
    public Transform throwOrigin;
    public Camera playerCamera;
    public Animator animator;

    [Header("Input")]
    public KeyCode throwKey = KeyCode.G;
    public string throwTriggerName = "ThrowGrenade";

    [Header("Throw")]
    public float throwForce = 30f;
    public float upwardForce = 5f;
    [Tooltip("How much of the player's current movement velocity gets added to the grenade throw.")]
    public float ownerVelocityInfluence = 1f;
    [Tooltip("Extra forward push so the grenade starts slightly in front of the camera point.")]
    public float spawnForwardOffset = 0.35f;
    [Tooltip("Optional spin for a more natural throw.")]
    public float randomSpin = 5f;

    [Header("Cooldown / Ammo")]
    public float throwCooldown = 1.0f;
    public bool useLimitedGrenades = false;
    public int grenadeCount = 999;

    [Header("State")]
    public bool blockThrowWhileAlreadyThrowing = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private bool isThrowing;
    private float nextThrowTime;

    private Rigidbody ownerRb;
    private CharacterController ownerCharacterController;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = Camera.main;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        ownerRb = GetComponent<Rigidbody>();
        ownerCharacterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(throwKey)) return;
        TryStartThrow();
    }

    public bool TryStartThrow()
    {
        if (Time.time < nextThrowTime) return false;
        if (blockThrowWhileAlreadyThrowing && isThrowing) return false;
        if (grenadePrefab == null)
        {
            Debug.LogWarning("[PlayerGrenadeThrow] No grenadePrefab assigned.", this);
            return false;
        }

        if (throwOrigin == null)
        {
            Debug.LogWarning("[PlayerGrenadeThrow] No throwOrigin assigned.", this);
            return false;
        }

        if (playerCamera == null)
        {
            Debug.LogWarning("[PlayerGrenadeThrow] No playerCamera assigned.", this);
            return false;
        }

        if (useLimitedGrenades && grenadeCount <= 0)
        {
            if (debugLogs) Debug.Log("[PlayerGrenadeThrow] No grenades left.", this);
            return false;
        }

        isThrowing = true;
        nextThrowTime = Time.time + throwCooldown;

        if (animator != null && !string.IsNullOrWhiteSpace(throwTriggerName))
        {
            animator.ResetTrigger(throwTriggerName);
            animator.SetTrigger(throwTriggerName);
        }
        else
        {
            // Fallback if no animator is assigned: release immediately.
            ReleaseGrenade();
            EndThrow();
        }

        return true;
    }

    /// <summary>
    /// Call this from an Animation Event on the exact grenade release frame.
    /// </summary>
    public void ReleaseGrenade()
    {
        if (grenadePrefab == null || throwOrigin == null || playerCamera == null)
            return;

        Vector3 forward = playerCamera.transform.forward.normalized;
        Vector3 spawnPos = throwOrigin.position + forward * spawnForwardOffset;
        Quaternion spawnRot = Quaternion.LookRotation(forward, Vector3.up);

        GameObject grenadeObj = Instantiate(grenadePrefab, spawnPos, spawnRot);

        GrenadeProjectile projectile = grenadeObj.GetComponent<GrenadeProjectile>();
        if (projectile != null)
        {
            projectile.SetOwner(gameObject);
        }

        Rigidbody grenadeRb = grenadeObj.GetComponent<Rigidbody>();
        if (grenadeRb != null)
        {
            Vector3 inheritedVelocity = GetOwnerVelocity() * ownerVelocityInfluence;
            Vector3 launchVelocity = forward * throwForce + Vector3.up * upwardForce + inheritedVelocity;

            grenadeRb.linearVelocity = Vector3.zero;
            grenadeRb.angularVelocity = Vector3.zero;
            grenadeRb.linearVelocity = launchVelocity;

            if (randomSpin > 0f)
            {
                grenadeRb.AddTorque(Random.insideUnitSphere * randomSpin, ForceMode.VelocityChange);
            }

            if (debugLogs)
            {
                Debug.Log(
                    $"[PlayerGrenadeThrow] Threw grenade. BaseForward={throwForce:F2}, Upward={upwardForce:F2}, Inherited={inheritedVelocity.magnitude:F2}, FinalVelocity={launchVelocity}",
                    this
                );
            }
        }
        else
        {
            Debug.LogWarning("[PlayerGrenadeThrow] Grenade prefab is missing a Rigidbody.", grenadeObj);
        }

        if (useLimitedGrenades)
        {
            grenadeCount = Mathf.Max(0, grenadeCount - 1);
        }
    }

    /// <summary>
    /// Call this from an Animation Event near the end of the throw animation.
    /// </summary>
    public void EndThrow()
    {
        isThrowing = false;
    }

    public void AddGrenades(int amount)
    {
        grenadeCount += Mathf.Max(0, amount);
    }

    public int GetGrenadeCount()
    {
        return grenadeCount;
    }

    private Vector3 GetOwnerVelocity()
    {
        // Prefer Rigidbody if present.
        if (ownerRb != null)
            return ownerRb.linearVelocity;

        // Then CharacterController velocity for FPS controllers.
        if (ownerCharacterController != null)
            return ownerCharacterController.velocity;

        return Vector3.zero;
    }
}
