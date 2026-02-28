using System.Collections;
using UnityEngine;

/// <summary>
/// DroneEnemyController (v4)
/// Adds:
/// - "Return fire" aggro window like your Enemy2Controller (damage aggro hold time + lose-distance override)
/// - Built-in health + TakeDamage + Die pipeline
/// - On death: trigger die animation, rotate 90° on Z, and fall to ground (Rigidbody gravity if present)
///
/// Aiming:
/// - Shoots at target AimPoint (child named "AimPoint") if present; otherwise collider center; otherwise target.position + offset.
///
/// NOTES:
/// - For best results, give the DroneRoot a Rigidbody (gravity OFF, isKinematic ON while alive).
///   On death we flip it to non-kinematic + gravity ON so it falls.
/// - Give the DroneRoot a simple collider (Capsule/Box) so physics and bullets work.
/// </summary>
[DisallowMultipleComponent]
public class DroneEnemyController : MonoBehaviour
{
    public enum DroneState { Patrol, Combat, Disabled }
    public enum TargetingMode { PlayerOnly, AlliesOnly, PlayerAndAllies }

    [Header("State")]
    public DroneState state = DroneState.Patrol;

    [Header("Targeting")]
    public Transform combatTarget;
    public TargetingMode targetingMode = TargetingMode.PlayerAndAllies;

    public string playerTag = "Player";
    public string allyTag = "Ally";

    [Tooltip("If non-zero, only colliders on these layers will be considered as potential targets.")]
    public LayerMask targetLayers = 0;

    public float distanceToChase = 25f;
    public float distanceToLose = 35f;
    public float keepChasingTime = 2f;

    [Header("Aggro Gates")]
    public bool onlyAggroIfTargetInRange = true;
    public float aggroFromDamageMaxDistance = 0f;

    [Header("Auto Acquire")]
    public float autoAcquireInterval = 0.5f;
    public bool allowRetargeting = true;

    [Header("Return Fire When Hit From Far")]
    [Tooltip("If true, when hit, we keep aggro for a short time and are allowed to not drop chase until farther away.")]
    public bool returnFireWhenHitFromFar = true;

    [Tooltip("How many seconds after taking damage we keep aggro even if the target is far beyond distanceToLose.")]
    public float damageAggroHoldSeconds = 8f;

    [Tooltip("While within the damage aggro window, we won't drop chase until the target is at least this far. If 0, uses distanceToLose.")]
    public float damageAggroLoseOverrideDistance = 80f;

    [Header("Aim")]
    public string aimPointChildName = "AimPoint";
    public float fallbackAimYOffset = 1.2f;

    [Tooltip("If projectilePrefab is used and this is > 0, applies basic leading based on target Rigidbody velocity.")]
    public float projectileLeadStrength = 0.6f;

    [Header("Flight / Hover")]
    public float hoverHeight = 6f;

    [Tooltip("If true, hover relative to target's Y (target.y + targetHeightOffset) instead of ground.")]
    public bool hoverRelativeToTargetY = false;

    public float targetHeightOffset = 3f;
    public LayerMask groundLayers;

    public float moveSpeed = 6f;
    public float acceleration = 8f;
    public float turnSpeed = 10f;
    public float maxVerticalSpeed = 6f;

    [Header("Combat Movement")]
    public float standoffDistance = 10f;
    public float tooCloseDistance = 6f;

    [Tooltip("Orbiting around target while in combat (0 disables).")]
    public float orbitStrength = 2.5f;

    [Tooltip("1 = clockwise, -1 = counter-clockwise.")]
    public float orbitDirection = 1f;

    public bool randomizeOrbitOnAggro = true;

    [Header("Weapon")]
    [Tooltip("FirePoint / Muzzle transform (where lasers/projectiles spawn from).")]
    public Transform muzzle;

    [Tooltip("If assigned, projectile will be spawned here and given initial velocity.")]
    public GameObject projectilePrefab;

    public float projectileSpeed = 35f;
    public float hitscanRange = 60f;
    public float fireCooldown = 0.25f;
    public int damagePerShot = 5;

    public bool requireLineOfSight = true;
    public LayerMask losBlockLayers = ~0;

    [Header("Patrol")]
    public Transform[] waypoints;
    public bool pingPong = true;
    public float arriveDistance = 1.2f;
    public float waitSecondsAtPoint = 0f;

    [Header("Health / Death")]
    public int maxHealth = 50;
    public int currentHealth = 50;

    [Tooltip("Animator on the drone root or child (optional).")]
    public Animator animator;

    [Tooltip("Animator trigger name for death.")]
    public string deathTriggerName = "Die";

    [Tooltip("Rotate this many degrees around Z when dying (90 = tip over sideways).")]
    public float deathRotateZDegrees = 90f;

    [Tooltip("If true, uses Rigidbody gravity fall on death when a Rigidbody exists.")]
    public bool usePhysicsFallOnDeath = true;

    [Tooltip("If no Rigidbody (or physics fall disabled), we will lerp down to ground over this duration.")]
    public float nonPhysicsFallDuration = 0.6f;

    [Tooltip("Destroy the drone GameObject after death.")]
    public bool destroyOnDeath = true;

    public float destroyDelay = 6f;

    [Header("Debug")]
    public bool drawGizmos = true;

    // Internals
    private Rigidbody _rb;
    private Vector3 _velocity;
    private int _wpIndex = 0;
    private int _wpDir = 1;
    private float _nextFireTime = 0f;
    private float _lostTargetTimer = 0f;
    private bool _waiting = false;

    private float _nextAcquireTime = 0f;
    private float _damageAggroUntil = 0f;

    private bool _isDead = false;

    private static readonly Collider[] _overlapBuffer = new Collider[64];

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (currentHealth <= 0) currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        if (_isDead) return;

        state = (combatTarget != null) ? DroneState.Combat : DroneState.Patrol;
        _lostTargetTimer = 0f;
        _waiting = false;
        _nextFireTime = Time.time + Random.Range(0f, fireCooldown);
        _nextAcquireTime = Time.time + Random.Range(0f, autoAcquireInterval);
    }

    private void Update()
    {
        if (_isDead || state == DroneState.Disabled) return;

        // Auto-acquire targets periodically
        if (Time.time >= _nextAcquireTime)
        {
            _nextAcquireTime = Time.time + autoAcquireInterval;

            if (combatTarget == null)
            {
                Transform t = AcquireTarget();
                if (t != null) SetCombatTarget(t);
            }
            else if (allowRetargeting)
            {
                Transform better = AcquireTarget();
                if (better != null && better != combatTarget)
                {
                    float curD = Vector3.Distance(transform.position, combatTarget.position);
                    float newD = Vector3.Distance(transform.position, better.position);
                    if (newD + 1.0f < curD) combatTarget = better; // small hysteresis
                }
            }
        }

        switch (state)
        {
            case DroneState.Patrol: TickPatrol(); break;
            case DroneState.Combat: TickCombat(); break;
        }
    }

    #region Damage / Health

    /// <summary>
    /// Generic damage entrypoint (SendMessage-compatible).
    /// You can call drone.SendMessage("TakeDamage", amount) or call directly.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (_isDead) return;
        if (amount <= 0) return;

        currentHealth -= amount;

        // Return-fire behavior
        if (returnFireWhenHitFromFar)
            _damageAggroUntil = Time.time + Mathf.Max(0f, damageAggroHoldSeconds);

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
    }

    #endregion

    #region Public Aggro API

    /// <summary>Call from your bullet/damage code to make the drone fight back.</summary>
    public void GetShot(GameObject attacker)
    {
        if (_isDead) return;
        if (attacker == null) return;
        GetShot(attacker.transform);
    }

    /// <summary>Preferred overload: pass shooter transform.</summary>
    public void GetShot(Transform attacker)
    {
        if (_isDead) return;

        if (returnFireWhenHitFromFar)
            _damageAggroUntil = Time.time + Mathf.Max(0f, damageAggroHoldSeconds);

        if (attacker == null) return;

        float maxAggroDist = (aggroFromDamageMaxDistance > 0f) ? aggroFromDamageMaxDistance : distanceToChase;
        if (onlyAggroIfTargetInRange && Vector3.Distance(transform.position, attacker.position) > maxAggroDist)
            return;

        SetCombatTarget(attacker);
    }

    public void SetCombatTarget(Transform target)
    {
        if (_isDead) return;
        if (target == null) return;

        if (onlyAggroIfTargetInRange)
        {
            float max = (aggroFromDamageMaxDistance > 0f) ? aggroFromDamageMaxDistance : distanceToChase;
            if (Vector3.Distance(transform.position, target.position) > max)
                return;
        }

        combatTarget = target;
        state = DroneState.Combat;
        _lostTargetTimer = 0f;
        _waiting = false;

        if (randomizeOrbitOnAggro)
            orbitDirection = (Random.value < 0.5f) ? -1f : 1f;
    }

    public void ClearCombatTarget()
    {
        combatTarget = null;
        _lostTargetTimer = 0f;
        state = DroneState.Patrol;
    }

    #endregion

    #region Target Acquisition

    private Transform AcquireTarget()
    {
        float radius = distanceToChase;
        Vector3 pos = transform.position;

        int mask = (targetLayers.value != 0) ? targetLayers.value : ~0;
        int hitCount = Physics.OverlapSphereNonAlloc(pos, radius, _overlapBuffer, mask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider c = _overlapBuffer[i];
            if (c == null) continue;

            Transform candidate = (c.transform.root != null) ? c.transform.root : c.transform;
            if (!IsValidTarget(candidate)) continue;

            float d = Vector3.Distance(pos, candidate.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsValidTarget(Transform t)
    {
        if (t == null) return false;
        if (t == transform || t.IsChildOf(transform)) return false;

        switch (targetingMode)
        {
            case TargetingMode.PlayerOnly:
                return (!string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag));

            case TargetingMode.AlliesOnly:
                return (!string.IsNullOrEmpty(allyTag) && t.CompareTag(allyTag));

            case TargetingMode.PlayerAndAllies:
                return
                    (!string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag)) ||
                    (!string.IsNullOrEmpty(allyTag) && t.CompareTag(allyTag));
        }

        return false;
    }

    #endregion

    #region Patrol

    private void TickPatrol()
    {
        if (_waiting) return;

        Vector3 targetPos;
        if (waypoints != null && waypoints.Length > 0 && waypoints[_wpIndex] != null)
            targetPos = ComputeHoverPosition(waypoints[_wpIndex].position, null);
        else
            targetPos = ComputeHoverPosition(transform.position, null);

        MoveTowards(targetPos);

        if (waypoints != null && waypoints.Length > 0 && waypoints[_wpIndex] != null)
        {
            float d = Vector3.Distance(transform.position, targetPos);
            if (d <= arriveDistance)
            {
                AdvanceWaypoint();
                if (waitSecondsAtPoint > 0f && gameObject.activeInHierarchy)
                    StartCoroutine(PatrolWait());
            }
        }

        FaceDirection(_velocity.sqrMagnitude > 0.001f ? _velocity : transform.forward);
    }

    private IEnumerator PatrolWait()
    {
        _waiting = true;
        yield return new WaitForSeconds(waitSecondsAtPoint);
        _waiting = false;
    }

    private void AdvanceWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        if (pingPong)
        {
            _wpIndex += _wpDir;
            if (_wpIndex >= waypoints.Length)
            {
                _wpIndex = waypoints.Length - 2;
                _wpDir = -1;
            }
            else if (_wpIndex < 0)
            {
                _wpIndex = 1;
                _wpDir = 1;
            }
            _wpIndex = Mathf.Clamp(_wpIndex, 0, waypoints.Length - 1);
        }
        else
        {
            _wpIndex++;
            if (_wpIndex >= waypoints.Length) _wpIndex = 0;
        }
    }

    #endregion

    #region Combat

    private void TickCombat()
    {
        if (combatTarget == null)
        {
            Transform found = AcquireTarget();
            if (found != null) combatTarget = found;
            else { state = DroneState.Patrol; return; }
        }

        float dist = Vector3.Distance(transform.position, combatTarget.position);

        // Lose-target logic (damage aggro can override lose distance)
        float loseDist = distanceToLose;
        if (returnFireWhenHitFromFar && Time.time <= _damageAggroUntil)
        {
            if (damageAggroLoseOverrideDistance > 0f)
                loseDist = Mathf.Max(loseDist, damageAggroLoseOverrideDistance);
        }

        if (dist > loseDist)
        {
            _lostTargetTimer += Time.deltaTime;
            if (_lostTargetTimer >= keepChasingTime) { ClearCombatTarget(); return; }
        }
        else _lostTargetTimer = 0f;

        Vector3 desiredPos = ComputeCombatDesiredPosition(combatTarget.position, dist);
        MoveTowards(desiredPos);

        Vector3 toTarget = GetAimPoint(combatTarget) - transform.position;
        if (toTarget.sqrMagnitude > 0.001f) FaceDirection(toTarget);

        TryShoot(combatTarget, dist);
    }

    private Vector3 ComputeCombatDesiredPosition(Vector3 targetWorldPos, float distToTarget)
    {
        Vector3 flatToTarget = (targetWorldPos - transform.position);
        flatToTarget.y = 0f;
        Vector3 flatDir = (flatToTarget.sqrMagnitude > 0.0001f) ? flatToTarget.normalized : transform.forward;

        Vector3 standoffPoint = targetWorldPos - flatDir * standoffDistance;
        standoffPoint = ComputeHoverPosition(standoffPoint, combatTarget);

        if (distToTarget < tooCloseDistance)
        {
            Vector3 backoff = targetWorldPos - flatDir * (standoffDistance + (tooCloseDistance - distToTarget) * 2f);
            backoff = ComputeHoverPosition(backoff, combatTarget);
            return backoff;
        }

        if (orbitStrength > 0.01f)
        {
            Vector3 perp = Vector3.Cross(Vector3.up, flatDir).normalized * orbitStrength * orbitDirection;
            standoffPoint += perp;
        }

        return standoffPoint;
    }

    private void TryShoot(Transform target, float dist)
    {
        if (Time.time < _nextFireTime) return;

        if (muzzle == null) muzzle = transform; // fallback if you forgot to assign FirePoint
        if (dist > hitscanRange) return;

        Vector3 origin = muzzle.position;
        Vector3 aimPoint = GetAimPoint(target);

        if (requireLineOfSight)
        {
            Vector3 dir = (aimPoint - origin);
            float len = dir.magnitude;
            if (len < 0.001f) return;
            dir /= len;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, Mathf.Min(len, hitscanRange), losBlockLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsHitTarget(hit.transform, target)) return;
            }
        }

        FireOnce(target, origin, aimPoint);
        _nextFireTime = Time.time + fireCooldown;
    }

    private void FireOnce(Transform target, Vector3 origin, Vector3 aimPoint)
    {
        if (projectilePrefab != null)
        {
            Vector3 finalAim = aimPoint;

            // Optional: lead aim for moving targets
            if (projectileLeadStrength > 0f)
            {
                Rigidbody trgRb = target.GetComponentInChildren<Rigidbody>();
                if (trgRb != null)
                {
                    float dist = Vector3.Distance(origin, aimPoint);
                    float time = (projectileSpeed > 0.01f) ? (dist / projectileSpeed) : 0f;
                    finalAim += trgRb.linearVelocity * time * projectileLeadStrength;
                }
            }

            Vector3 aimDir = (finalAim - origin).normalized;

            Quaternion rot = Quaternion.LookRotation(aimDir, Vector3.up);
            GameObject proj = Instantiate(projectilePrefab, origin, rot);

            Rigidbody prb = proj.GetComponent<Rigidbody>();
            if (prb != null)
                prb.linearVelocity = aimDir * projectileSpeed;
            else
                proj.SendMessage("SetVelocity", aimDir * projectileSpeed, SendMessageOptions.DontRequireReceiver);

            proj.SendMessage("SetOwner", gameObject, SendMessageOptions.DontRequireReceiver);
            proj.SendMessage("SetDamage", damagePerShot, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            // Hitscan fallback
            Vector3 dir = (aimPoint - origin).normalized;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, hitscanRange, ~0, QueryTriggerInteraction.Ignore))
            {
                hit.transform.SendMessage("TakeDamage", damagePerShot, SendMessageOptions.DontRequireReceiver);
                hit.transform.SendMessage("GetShot", gameObject, SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    private Vector3 GetAimPoint(Transform target)
    {
        if (target == null) return transform.position + transform.forward * 10f;

        // 1) Prefer explicit AimPoint child (deep search)
        if (!string.IsNullOrEmpty(aimPointChildName))
        {
            Transform ap = FindChildByName(target, aimPointChildName);
            if (ap != null) return ap.position;
        }

        // 2) Collider bounds center
        Collider c = target.GetComponentInChildren<Collider>();
        if (c != null) return c.bounds.center;

        // 3) Fallback
        return target.position + Vector3.up * fallbackAimYOffset;
    }

    private Transform FindChildByName(Transform root, string childName)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == childName)
                return all[i];
        }
        return null;
    }

    private bool IsHitTarget(Transform hit, Transform target)
    {
        if (hit == null || target == null) return false;
        if (hit == target) return true;
        return hit.IsChildOf(target);
    }

    #endregion

    #region Movement

    private void MoveTowards(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        Vector3 desiredVel = (to.sqrMagnitude > 0.0001f) ? to.normalized * moveSpeed : Vector3.zero;

        float dist = to.magnitude;
        if (dist < 2f)
        {
            float t = Mathf.InverseLerp(0f, 2f, dist);
            desiredVel *= t;
        }

        _velocity = Vector3.Lerp(_velocity, desiredVel, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

        if (maxVerticalSpeed > 0f)
            _velocity.y = Mathf.Clamp(_velocity.y, -maxVerticalSpeed, maxVerticalSpeed);

        Vector3 nextPos = transform.position + _velocity * Time.deltaTime;

        if (_rb != null && !_rb.isKinematic)
            _rb.MovePosition(nextPos);
        else
            transform.position = nextPos;
    }

    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0f; // yaw-only to keep drone level. Remove this line if you want pitch/tilt.
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }

    private Vector3 ComputeHoverPosition(Vector3 referencePos, Transform target)
    {
        float desiredY = referencePos.y;

        if (hoverRelativeToTargetY && target != null)
        {
            desiredY = target.position.y + targetHeightOffset;
        }
        else if (groundLayers.value != 0)
        {
            Vector3 origin = new Vector3(referencePos.x, referencePos.y + 50f, referencePos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, groundLayers, QueryTriggerInteraction.Ignore))
                desiredY = hit.point.y + hoverHeight;
            else
                desiredY = transform.position.y;
        }
        else
        {
            desiredY = transform.position.y;
        }

        return new Vector3(referencePos.x, desiredY, referencePos.z);
    }

    #endregion

    #region Death

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        state = DroneState.Disabled;
        combatTarget = null;
        _velocity = Vector3.zero;

        // Trigger die animation
        if (animator != null && !string.IsNullOrEmpty(deathTriggerName))
            animator.SetTrigger(deathTriggerName);

        // Rotate 90 degrees on Z (tip over)
        transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, deathRotateZDegrees);

        // Stop/disable "alive" physics settings
        if (_rb != null && usePhysicsFallOnDeath)
        {
            // If you had it kinematic while alive, make it dynamic now.
            _rb.isKinematic = false;
            _rb.useGravity = true;

            // Optional: a tiny nudge so it starts falling/settling
            _rb.AddForce(Vector3.down * 1.5f, ForceMode.VelocityChange);
        }
        else
        {
            // No rigidbody: do a simple ground drop
            StartCoroutine(FallToGroundNonPhysics());
        }

        // Disable this script so it stops thinking.
        enabled = false;

        if (destroyOnDeath)
            Destroy(gameObject, Mathf.Max(0.1f, destroyDelay));
    }

    private IEnumerator FallToGroundNonPhysics()
    {
        Vector3 start = transform.position;
        Vector3 end = start;

        // Raycast down to find ground; if not found, just drop a bit.
        if (Physics.Raycast(start + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            end = hit.point;
        else
            end = start + Vector3.down * 3f;

        float dur = Mathf.Max(0.05f, nonPhysicsFallDuration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        transform.position = end;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, distanceToChase);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, distanceToLose);

        if (combatTarget != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, GetAimPoint(combatTarget));
        }

        if (muzzle != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(muzzle.position, 0.08f);
            Vector3 a = (combatTarget != null) ? GetAimPoint(combatTarget) : (muzzle.position + muzzle.forward * 1.5f);
            Gizmos.DrawLine(muzzle.position, a);
        }
    }

    #endregion
}
