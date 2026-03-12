using UnityEngine;

/// <summary>
/// Simple turret:
/// - Finds nearest valid target
/// - Rotates one head transform directly toward that target in full 3D
/// - Fires projectile from firePoint using firePoint.rotation
///
/// Use this when you do NOT want separate yaw/pitch parts.
/// Just set headTransform to the part that should visually face the target.
/// </summary>
[DisallowMultipleComponent]
public class TurretController : MonoBehaviour
{
    [Header("Team / Targeting")]
    [Tooltip("If ON, turret chooses who to shoot based on its own Tag.")]
    public bool autoSwitchTargetByTurretTag = true;

    [Tooltip("Used only when AutoSwitchTargetByTurretTag is OFF.")]
    public string targetTag = "Player";

    [Tooltip("When turret tag is Ally, it will shoot this tag.")]
    public string allyShootsTag = "Enemy";

    [Tooltip("When turret tag is Enemy, it will shoot these tags.")]
    public string[] enemyShootsTags = new string[] { "Ally", "Player" };

    [Header("Detection")]
    public float sightRange = 35f;
    public float fireRange = 30f;
    public float retargetInterval = 0.25f;

    [Header("Rotation")]
    [Tooltip("Assign the turret part that should face the target. Usually your head object.")]
    public Transform headTransform;
    public float turnSpeed = 360f;

    [Tooltip("If the model faces backward when aiming, turn this on.")]
    public bool flipForward = false;

    [Header("Shooting")]
    public Transform firePoint;
    public GameObject projectilePrefab;
    public float fireRate = 4f;
    public float muzzleForwardOffset = 0.05f;

    [Header("Debug")]
    public bool log = false;

    private Transform _target;
    private float _nextFireTime;
    private float _nextRetargetTime;

    private enum Mode { SingleTag, AllyMode, EnemyMode }
    private Mode _mode = Mode.SingleTag;

    private void Start()
    {
        if (fireRange <= 0f) fireRange = sightRange;
        ResolveMode();
        Retarget(false);
    }

    private void Update()
    {
        ResolveMode();

        if (Time.time >= _nextRetargetTime)
        {
            _nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetInterval);
            Retarget(false);
        }

        if (_target == null) return;

        float dist = Vector3.Distance(transform.position, _target.position);
        if (dist > sightRange) return;

        RotateTowardTarget(_target);

        if (dist <= fireRange)
            TryFire();
    }

    private void ResolveMode()
    {
        Mode newMode;

        if (!autoSwitchTargetByTurretTag)
            newMode = Mode.SingleTag;
        else if (CompareTag("Ally"))
            newMode = Mode.AllyMode;
        else if (CompareTag("Enemy"))
            newMode = Mode.EnemyMode;
        else
            newMode = Mode.SingleTag;

        if (newMode != _mode)
        {
            _mode = newMode;
            _nextRetargetTime = 0f;

            if (log)
                Debug.Log($"[TurretController] Mode switched to {_mode} (tag: {gameObject.tag})", this);
        }
    }

    private void Retarget(bool forceLog)
    {
        Transform best = null;
        float bestDist = float.PositiveInfinity;
        Vector3 origin = transform.position;

        if (_mode == Mode.AllyMode)
        {
            FindNearestWithTag(allyShootsTag, origin, ref best, ref bestDist, forceLog);
        }
        else if (_mode == Mode.EnemyMode)
        {
            if (enemyShootsTags == null || enemyShootsTags.Length == 0)
            {
                if (log || forceLog)
                    Debug.LogWarning("[TurretController] enemyShootsTags is empty.", this);
            }
            else
            {
                for (int i = 0; i < enemyShootsTags.Length; i++)
                    FindNearestWithTag(enemyShootsTags[i], origin, ref best, ref bestDist, forceLog);
            }
        }
        else
        {
            FindNearestWithTag(targetTag, origin, ref best, ref bestDist, forceLog);
        }

        _target = best;
    }

    private void FindNearestWithTag(string tagToFind, Vector3 origin, ref Transform best, ref float bestDist, bool forceLog)
    {
        if (string.IsNullOrWhiteSpace(tagToFind)) return;

        GameObject[] candidates;
        try
        {
            candidates = GameObject.FindGameObjectsWithTag(tagToFind);
        }
        catch
        {
            if (log || forceLog)
                Debug.LogWarning($"[TurretController] Tag '{tagToFind}' does not exist.", this);
            return;
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            var go = candidates[i];
            if (go == null || go == gameObject) continue;

            float d = Vector3.Distance(origin, go.transform.position);
            if (d > sightRange) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = go.transform;
            }
        }
    }

    private void RotateTowardTarget(Transform target)
    {
        if (target == null || headTransform == null) return;

        Vector3 dir = target.position - headTransform.position;
        if (dir.sqrMagnitude <= 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

        if (flipForward)
            targetRot *= Quaternion.Euler(0f, 180f, 0f);

        headTransform.rotation = Quaternion.RotateTowards(
            headTransform.rotation,
            targetRot,
            turnSpeed * Time.deltaTime
        );
    }

    private void TryFire()
    {
        if (firePoint == null || projectilePrefab == null) return;
        if (fireRate <= 0f) return;

        float interval = 1f / fireRate;
        if (Time.time < _nextFireTime) return;

        _nextFireTime = Time.time + interval;

        Vector3 spawnPos = firePoint.position + firePoint.forward * muzzleForwardOffset;
        Instantiate(projectilePrefab, spawnPos, firePoint.rotation);
    }
}
