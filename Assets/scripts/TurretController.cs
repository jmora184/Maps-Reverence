using UnityEngine;

/// <summary>
/// SIMPLE TurretController (minimal, tag-driven, nearest-target, multi-tag):
/// - If turret Tag = Ally  -> shoots targets with tag allyShootsTag (default Enemy)
/// - If turret Tag = Enemy -> shoots targets with tags in enemyShootsTags (default Ally + Player)
/// - Finds the NEAREST target among allowed tags within sightRange
/// - Rotates yawTransform toward target (yaw only)
/// - Fires projectilePrefab from firePoint at fireRate
///
/// No line-of-sight, no aim cone, no projectile-speed overrides.
/// Uses whatever BulletController settings are already on the projectile prefab.
/// </summary>
[DisallowMultipleComponent]
public class TurretController : MonoBehaviour
{
    [Header("Team / Targeting")]
    [Tooltip("If ON, turret chooses who to shoot based on its own Tag.")]
    public bool autoSwitchTargetByTurretTag = true;

    [Tooltip("Used only when AutoSwitchTargetByTurretTag is OFF (single tag).")]
    public string targetTag = "Player";

    [Tooltip("When turret tag is Ally, it will shoot this tag.")]
    public string allyShootsTag = "Enemy";

    [Tooltip("When turret tag is Enemy, it will shoot these tags (any of them).")]
    public string[] enemyShootsTags = new string[] { "Ally", "Player" };

    [Tooltip("How far the turret can detect/track a target (rotates toward them).")]
    public float sightRange = 35f;

    [Tooltip("How far the turret can shoot. If <= 0, uses sightRange.")]
    public float fireRange = 30f;

    [Tooltip("How often (seconds) the turret re-scans to find the nearest valid target.")]
    public float retargetInterval = 0.25f;

    [Header("Rotation")]
    [Tooltip("The transform that rotates left/right (yaw). Example: swivle.")]
    public Transform yawTransform;

    [Tooltip("How fast to rotate toward the target (degrees per second).")]
    public float turnSpeed = 360f;

    [Header("Shooting")]
    public Transform firePoint;
    public GameObject projectilePrefab;

    [Tooltip("Shots per second.")]
    public float fireRate = 4f;

    [Tooltip("Optional: spawn projectile a tiny bit forward to avoid spawning inside colliders.")]
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
        Retarget(forceLog: false);
    }

    private void Update()
    {
        ResolveMode();

        if (Time.time >= _nextRetargetTime)
        {
            _nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetInterval);
            Retarget(forceLog: false);
        }

        if (_target == null) return;

        float dist = Vector3.Distance(transform.position, _target.position);

        if (dist > sightRange) return;

        RotateYawToward(_target.position);

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
            if (log) Debug.Log($"[TurretController] Mode switched to {_mode} (turret tag: '{gameObject.tag}')", this);
            _nextRetargetTime = 0f; // force immediate retarget
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
                    Debug.LogWarning("[TurretController] enemyShootsTags is empty. Add tags like 'Ally' and 'Player'.", this);
            }
            else
            {
                for (int i = 0; i < enemyShootsTags.Length; i++)
                    FindNearestWithTag(enemyShootsTags[i], origin, ref best, ref bestDist, forceLog);
            }
        }
        else // SingleTag
        {
            FindNearestWithTag(targetTag, origin, ref best, ref bestDist, forceLog);
        }

        _target = best;

        if ((log || forceLog) && _target == null)
        {
            string label = _mode == Mode.AllyMode ? allyShootsTag :
                           _mode == Mode.EnemyMode ? string.Join(", ", enemyShootsTags ?? new string[0]) :
                           targetTag;

            Debug.Log($"[TurretController] No targets found within sightRange={sightRange}. Looking for: {label}", this);
        }
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
                Debug.LogWarning($"[TurretController] Tag '{tagToFind}' does not exist. Add it in Unity Tags.", this);
            return;
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            var go = candidates[i];
            if (go == null) continue;
            if (go == gameObject) continue;

            float d = Vector3.Distance(origin, go.transform.position);
            if (d > sightRange) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = go.transform;
            }
        }
    }

    private void RotateYawToward(Vector3 worldPos)
    {
        if (yawTransform == null) return;

        Vector3 dir = worldPos - yawTransform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        yawTransform.rotation = Quaternion.RotateTowards(yawTransform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    private void TryFire()
    {
        if (firePoint == null || projectilePrefab == null)
        {
            if (log)
                Debug.LogWarning("[TurretController] Missing firePoint or projectilePrefab.", this);
            return;
        }

        if (fireRate <= 0f) return;

        float now = Time.time;
        float interval = 1f / fireRate;

        if (now < _nextFireTime) return;

        _nextFireTime = now + interval;

        Vector3 spawnPos = firePoint.position + firePoint.forward * muzzleForwardOffset;
        Quaternion spawnRot = firePoint.rotation;

        Instantiate(projectilePrefab, spawnPos, spawnRot);
    }
}
