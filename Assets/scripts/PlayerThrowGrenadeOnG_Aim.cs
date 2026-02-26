using UnityEngine;

/// <summary>
/// Press G to play the "throw_grenade" animation AND launch a grenade in the direction the player is looking.
/// - Uses camera forward as the throw direction.
/// - Optionally rotates the Soldier_marine body to match the camera yaw so the throw looks correct.
/// - Optionally snaps Soldier_marine to a specific transform (your screenshot values).
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

    private float _nextAllowedTime;

    private void Awake()
    {
        if (soldierMarineRoot != null && animator == null)
            animator = soldierMarineRoot.GetComponentInChildren<Animator>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(throwKey)) return;
        if (Time.time < _nextAllowedTime) return;

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

        // 2) Launch grenade (optional)
        if (grenadePrefab != null)
            SpawnAndThrowGrenade();

        if (cooldown > 0f)
            _nextAllowedTime = Time.time + cooldown;
    }

    private void MatchYawToCamera()
    {
        // Match only yaw (Y), ignore pitch so body doesn't try to flip.
        Vector3 camForward = playerCamera.transform.forward;
        camForward.y = 0f;

        if (camForward.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetYaw = Quaternion.LookRotation(camForward.normalized, Vector3.up);

        // Smooth rotate the soldier root (or snap if you want instant).
        soldierMarineRoot.rotation = Quaternion.Slerp(soldierMarineRoot.rotation, targetYaw, yawRotateSpeed * Time.deltaTime);
    }

    private void SpawnAndThrowGrenade()
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
            // fallback: spawn in front of camera
            spawnPos = playerCamera.transform.position + playerCamera.transform.forward * 0.6f;
            spawnRot = playerCamera.transform.rotation;
        }

        GameObject g = Instantiate(grenadePrefab, spawnPos, spawnRot);

        // Compute throw velocity from camera forward + upward boost
        Vector3 dir = playerCamera.transform.forward.normalized;
        Vector3 velocity = dir * throwSpeed + Vector3.up * upwardBoost;

        if (g.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Use velocity-based throw
            rb.linearVelocity = velocity;

            // Optional: add some spin so it looks thrown
            rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
        }
        else
        {
            // No rigidbody - just move it forward as a fallback
            Debug.LogWarning($"{nameof(PlayerThrowGrenadeOnG_Aim)}: Grenade prefab has no Rigidbody. Add one for a real throw.", g);
        }
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
}
