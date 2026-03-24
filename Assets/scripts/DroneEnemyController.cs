using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// DroneEnemyController
/// - Uses NavMeshAgent for patrol/combat pathing
/// - Snaps to the nearest NavMesh on enable/start so floating drones can still move
/// - Keeps hover via NavMeshAgent.baseOffset
/// - Uses horizontal arrival checks for ground waypoints
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
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
    public bool returnFireWhenHitFromFar = true;
    public float damageAggroHoldSeconds = 8f;
    public float damageAggroLoseOverrideDistance = 80f;

    [Header("Aim")]
    public string aimPointChildName = "AimPoint";
    public float fallbackAimYOffset = 1.2f;
    public float projectileLeadStrength = 0.6f;

    [Header("Flight / Hover")]
    [Tooltip("Visual/base hover height above the baked NavMesh / ground.")]
    public float hoverHeight = 6f;

    [Tooltip("If true, tries to hover relative to target's Y during combat by adjusting baseOffset.")]
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
    public float orbitStrength = 2.5f;
    public float orbitDirection = 1f;
    public bool randomizeOrbitOnAggro = true;

    [Header("Weapon")]
    public Transform muzzle;
    public GameObject projectilePrefab;
    public float projectileSpeed = 35f;
    public float hitscanRange = 60f;
    public float fireCooldown = 0.25f;
    public int damagePerShot = 5;
    public bool requireLineOfSight = true;
    public LayerMask losBlockLayers = ~0;

    [Header("Shot Audio (Optional)")]
    public AudioSource shotAudioSource;
    public AudioClip shotSFX;
    [Range(0f, 2f)] public float shotVolume = 1f;
    public bool randomizeShotPitch = true;
    public float minShotPitch = 0.97f;
    public float maxShotPitch = 1.03f;

    [Header("Death Audio (Optional)")]
    [Tooltip("Optional source whose 3D/spatial settings will be copied onto the detached death audio object.")]
    public AudioSource deathAudioSourceTemplate;
    public AudioClip deathSFX;
    [Range(0f, 2f)] public float deathVolume = 1f;
    public bool randomizeDeathPitch = true;
    public float minDeathPitch = 0.97f;
    public float maxDeathPitch = 1.03f;

    [Header("Patrol")]
    public Transform[] waypoints;
    public bool pingPong = true;
    public float arriveDistance = 1.2f;
    public float waitSecondsAtPoint = 0f;

    [Header("NavMesh Attach")]
    [Tooltip("When enabled, the drone will snap to the nearest NavMesh point on enable/start.")]
    public bool snapToNavMeshOnEnable = true;

    [Tooltip("Search radius for finding the nearest NavMesh point from the drone's spawn position.")]
    public float navMeshSnapRadius = 80f;

    [Tooltip("If the drone ever loses NavMesh binding during play, try snapping again.")]
    public bool retrySnapWhenOffNavMesh = true;

    [Tooltip("How often to retry NavMesh snapping when off-mesh.")]
    public float retrySnapInterval = 0.5f;

    [Header("Health / Death")]
    public int maxHealth = 50;
    public int currentHealth = 50;
    public GameObject explosionPrefab;
    public Vector3 explosionOffset = new Vector3(0f, 1.2f, 0f);
    public bool disableCollidersOnDeath = true;
    public bool destroyOnDeath = true;
    public float destroyDelay = 2.5f;

    [Header("Debug")]
    public bool drawGizmos = true;

    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private int _wpIndex = 0;
    private int _wpDir = 1;
    private float _nextFireTime = 0f;
    private float _lostTargetTimer = 0f;
    private bool _waiting = false;
    private float _nextAcquireTime = 0f;
    private float _damageAggroUntil = 0f;
    private bool _isDead = false;
    private Vector3 _lastMoveDir = Vector3.forward;
    private Vector3 _lastRequestedDestination;
    private float _nextSnapRetryTime = 0f;

    private static readonly Collider[] _overlapBuffer = new Collider[64];

    public static event Action<DroneEnemyController> OnAnyDroneDied;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();

        if (currentHealth <= 0)
            currentHealth = maxHealth;

        if (_agent != null)
        {
            _agent.speed = moveSpeed;
            _agent.acceleration = acceleration;
            _agent.angularSpeed = 120f;
            _agent.stoppingDistance = 0f;
            _agent.updateRotation = false;
            _agent.autoBraking = true;
            _agent.baseOffset = hoverHeight;
        }

        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.isKinematic = true;
        }

        ResolveShotAudioSourceIfNeeded();
    }

    private void Start()
    {
        TrySnapToNavMeshImmediate();
    }

    private void OnEnable()
    {
        if (_isDead) return;

        state = (combatTarget != null) ? DroneState.Combat : DroneState.Patrol;
        _lostTargetTimer = 0f;
        _waiting = false;
        _nextFireTime = Time.time + UnityEngine.Random.Range(0f, Mathf.Max(0.05f, fireCooldown));
        _nextAcquireTime = Time.time + UnityEngine.Random.Range(0f, Mathf.Max(0.05f, autoAcquireInterval));
        _nextSnapRetryTime = Time.time;

        if (_agent != null && _agent.enabled)
        {
            _agent.baseOffset = hoverHeight;

            // Snap first so we do not call NavMeshAgent movement APIs while the agent
            // is still unbound from the NavMesh.
            TrySnapToNavMeshImmediate();

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.ResetPath();
            }
        }
    }

    private void Update()
    {
        if (_isDead || state == DroneState.Disabled)
            return;

        SyncAgentSettings();
        EnsureOnNavMeshIfNeeded();
        UpdateHoverOffset();

        if (Time.time >= _nextAcquireTime)
        {
            _nextAcquireTime = Time.time + Mathf.Max(0.05f, autoAcquireInterval);

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
                    if (newD + 1.0f < curD)
                        combatTarget = better;
                }
            }
        }

        switch (state)
        {
            case DroneState.Patrol:
                TickPatrol();
                break;
            case DroneState.Combat:
                TickCombat();
                break;
        }

        UpdateFacing();
    }

    #region Damage / Health

    public void TakeDamage(int amount)
    {
        if (_isDead || amount <= 0)
            return;

        currentHealth -= amount;

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

    public void GetShot(GameObject attacker)
    {
        if (_isDead || attacker == null) return;
        GetShot(attacker.transform);
    }

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
        if (_isDead || target == null) return;

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
            orbitDirection = (UnityEngine.Random.value < 0.5f) ? -1f : 1f;
    }

    public void ClearCombatTarget()
    {
        combatTarget = null;
        _lostTargetTimer = 0f;
        state = DroneState.Patrol;
        StopAgent();
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
                return !string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag);
            case TargetingMode.AlliesOnly:
                return !string.IsNullOrEmpty(allyTag) && t.CompareTag(allyTag);
            case TargetingMode.PlayerAndAllies:
                return (!string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag)) ||
                       (!string.IsNullOrEmpty(allyTag) && t.CompareTag(allyTag));
        }

        return false;
    }

    #endregion

    #region Patrol

    private void TickPatrol()
    {
        if (_waiting)
        {
            StopAgent();
            return;
        }

        if (waypoints == null || waypoints.Length == 0 || _wpIndex < 0 || _wpIndex >= waypoints.Length || waypoints[_wpIndex] == null)
        {
            StopAgent();
            return;
        }

        Vector3 rawWaypoint = waypoints[_wpIndex].position;
        Vector3 navTarget = GetBestNavDestination(rawWaypoint);
        MoveAgentTo(navTarget);

        if (HasReachedAgentDestination(navTarget, arriveDistance))
        {
            AdvanceWaypoint();
            if (waitSecondsAtPoint > 0f && gameObject.activeInHierarchy)
                StartCoroutine(PatrolWait());
        }
    }

    private IEnumerator PatrolWait()
    {
        _waiting = true;
        StopAgent();
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
                _wpIndex = Mathf.Max(0, waypoints.Length - 2);
                _wpDir = -1;
            }
            else if (_wpIndex < 0)
            {
                _wpIndex = Mathf.Min(1, waypoints.Length - 1);
                _wpDir = 1;
            }

            _wpIndex = Mathf.Clamp(_wpIndex, 0, waypoints.Length - 1);
        }
        else
        {
            _wpIndex++;
            if (_wpIndex >= waypoints.Length)
                _wpIndex = 0;
        }
    }

    #endregion

    #region Combat

    private void TickCombat()
    {
        if (combatTarget == null)
        {
            Transform found = AcquireTarget();
            if (found != null)
            {
                combatTarget = found;
            }
            else
            {
                state = DroneState.Patrol;
                return;
            }
        }

        float dist = Vector3.Distance(transform.position, combatTarget.position);

        float loseDist = distanceToLose;
        if (returnFireWhenHitFromFar && Time.time <= _damageAggroUntil && damageAggroLoseOverrideDistance > 0f)
            loseDist = Mathf.Max(loseDist, damageAggroLoseOverrideDistance);

        if (dist > loseDist)
        {
            _lostTargetTimer += Time.deltaTime;
            if (_lostTargetTimer >= keepChasingTime)
            {
                ClearCombatTarget();
                return;
            }
        }
        else
        {
            _lostTargetTimer = 0f;
        }

        Vector3 desiredPos = ComputeCombatDesiredPosition(combatTarget.position, dist);
        Vector3 navTarget = GetBestNavDestination(desiredPos);
        MoveAgentTo(navTarget);

        TryShoot(combatTarget, dist);
    }

    private Vector3 ComputeCombatDesiredPosition(Vector3 targetWorldPos, float distToTarget)
    {
        Vector3 flatToTarget = targetWorldPos - transform.position;
        flatToTarget.y = 0f;
        Vector3 flatDir = (flatToTarget.sqrMagnitude > 0.0001f) ? flatToTarget.normalized : transform.forward;

        Vector3 standoffPoint = targetWorldPos - flatDir * standoffDistance;

        if (distToTarget < tooCloseDistance)
        {
            return targetWorldPos - flatDir * (standoffDistance + (tooCloseDistance - distToTarget) * 2f);
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
        if (muzzle == null) muzzle = transform;
        if (dist > hitscanRange) return;

        Vector3 origin = muzzle.position;
        Vector3 aimPoint = GetAimPoint(target);

        if (requireLineOfSight)
        {
            Vector3 dir = aimPoint - origin;
            float len = dir.magnitude;
            if (len < 0.001f) return;
            dir /= len;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, Mathf.Min(len, hitscanRange), losBlockLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsHitTarget(hit.transform, target)) return;
            }
        }

        FireOnce(target, origin, aimPoint);
        _nextFireTime = Time.time + Mathf.Max(0.01f, fireCooldown);
    }

    private void FireOnce(Transform target, Vector3 origin, Vector3 aimPoint)
    {
        if (projectilePrefab != null)
        {
            Vector3 finalAim = aimPoint;

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
            TriggerShotSound();
        }
        else
        {
            Vector3 dir = (aimPoint - origin).normalized;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, hitscanRange, ~0, QueryTriggerInteraction.Ignore))
            {
                hit.transform.SendMessage("TakeDamage", damagePerShot, SendMessageOptions.DontRequireReceiver);
                hit.transform.SendMessage("GetShot", gameObject, SendMessageOptions.DontRequireReceiver);
                TriggerShotSound();
            }
        }
    }

    private void ResolveShotAudioSourceIfNeeded()
    {
        if (shotAudioSource == null)
            shotAudioSource = GetComponent<AudioSource>();
    }

    private void TriggerShotSound()
    {
        if (shotAudioSource == null || shotSFX == null)
            return;

        if (randomizeShotPitch)
            shotAudioSource.pitch = UnityEngine.Random.Range(minShotPitch, maxShotPitch);
        else
            shotAudioSource.pitch = 1f;

        shotAudioSource.PlayOneShot(shotSFX, shotVolume);
    }

    private void PlayDetachedDeathSound()
    {
        if (deathSFX == null)
            return;

        GameObject audioObj = new GameObject(name + "_DeathAudio");
        audioObj.transform.position = transform.position;

        AudioSource src = audioObj.AddComponent<AudioSource>();
        CopyDeathAudioSettings(src);

        if (randomizeDeathPitch)
            src.pitch = UnityEngine.Random.Range(minDeathPitch, maxDeathPitch);
        else
            src.pitch = 1f;

        src.clip = deathSFX;
        src.Play();

        Destroy(audioObj, Mathf.Max(0.1f, deathSFX.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch))) + 0.25f);
    }

    private void CopyDeathAudioSettings(AudioSource destination)
    {
        if (destination == null)
            return;

        AudioSource template = deathAudioSourceTemplate;
        if (template == null)
            template = shotAudioSource;
        if (template == null)
            template = GetComponent<AudioSource>();

        if (template != null)
        {
            destination.outputAudioMixerGroup = template.outputAudioMixerGroup;
            destination.mute = template.mute;
            destination.bypassEffects = template.bypassEffects;
            destination.bypassListenerEffects = template.bypassListenerEffects;
            destination.bypassReverbZones = template.bypassReverbZones;
            destination.priority = template.priority;
            destination.volume = template.volume * deathVolume;
            destination.panStereo = template.panStereo;
            destination.spatialBlend = template.spatialBlend;
            destination.reverbZoneMix = template.reverbZoneMix;
            destination.dopplerLevel = template.dopplerLevel;
            destination.spread = template.spread;
            destination.rolloffMode = template.rolloffMode;
            destination.minDistance = template.minDistance;
            destination.maxDistance = template.maxDistance;
            destination.ignoreListenerVolume = template.ignoreListenerVolume;
            destination.ignoreListenerPause = template.ignoreListenerPause;
            destination.playOnAwake = false;
            destination.loop = false;
        }
        else
        {
            destination.playOnAwake = false;
            destination.loop = false;
            destination.spatialBlend = 1f;
            destination.rolloffMode = AudioRolloffMode.Linear;
            destination.minDistance = 4f;
            destination.maxDistance = 35f;
            destination.volume = deathVolume;
        }
    }

    private Vector3 GetAimPoint(Transform target)
    {
        if (target == null) return transform.position + transform.forward * 10f;

        if (!string.IsNullOrEmpty(aimPointChildName))
        {
            Transform ap = FindChildByName(target, aimPointChildName);
            if (ap != null) return ap.position;
        }

        Collider c = target.GetComponentInChildren<Collider>();
        if (c != null) return c.bounds.center;

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
        return hit == target || hit.IsChildOf(target);
    }

    #endregion

    #region NavMesh Movement

    private void SyncAgentSettings()
    {
        if (_agent == null) return;

        _agent.speed = moveSpeed;
        _agent.acceleration = acceleration;
        _agent.updateRotation = false;
        _agent.baseOffset = hoverHeight;
    }

    private void EnsureOnNavMeshIfNeeded()
    {
        if (_agent == null || !_agent.enabled)
            return;

        if (_agent.isOnNavMesh)
            return;

        if (!retrySnapWhenOffNavMesh)
            return;

        if (Time.time < _nextSnapRetryTime)
            return;

        _nextSnapRetryTime = Time.time + Mathf.Max(0.05f, retrySnapInterval);
        TrySnapToNavMeshImmediate();
    }

    private bool TrySnapToNavMeshImmediate()
    {
        if (_agent == null || !_agent.enabled)
            return false;

        if (_agent.isOnNavMesh)
            return true;

        if (!snapToNavMeshOnEnable)
            return false;

        float radius = Mathf.Max(0.5f, navMeshSnapRadius);
        Vector3 searchOrigin = transform.position;

        if (NavMesh.SamplePosition(searchOrigin, out NavMeshHit hit, radius, _agent.areaMask))
        {
            transform.position = hit.position;
            _agent.Warp(hit.position);
            _agent.baseOffset = hoverHeight;
            _lastRequestedDestination = hit.position;
            return _agent.isOnNavMesh;
        }

        return false;
    }

    private void UpdateHoverOffset()
    {
        if (_agent == null) return;

        float desiredOffset = hoverHeight;

        if (hoverRelativeToTargetY && combatTarget != null)
        {
            float groundY = GetGroundYNearPosition(transform.position, out bool foundGround);
            if (foundGround)
                desiredOffset = Mathf.Max(0f, (combatTarget.position.y + targetHeightOffset) - groundY);
        }

        _agent.baseOffset = desiredOffset;
    }

    private void MoveAgentTo(Vector3 worldPos)
    {
        if (_agent == null || !_agent.enabled)
            return;

        if (!_agent.isOnNavMesh)
            return;

        _agent.isStopped = false;

        if ((_lastRequestedDestination - worldPos).sqrMagnitude > 0.04f)
        {
            _agent.SetDestination(worldPos);
            _lastRequestedDestination = worldPos;
        }
    }

    private void StopAgent()
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        _agent.isStopped = true;
        _agent.ResetPath();
    }

    private bool HasReachedAgentDestination(Vector3 worldPos, float threshold)
    {
        float finalThreshold = Mathf.Max(0.05f, threshold);

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (_agent.pathPending)
                return false;

            if (_agent.hasPath)
            {
                if (_agent.remainingDistance > finalThreshold)
                    return false;

                Vector3 flatAgent = _agent.nextPosition;
                Vector3 flatTarget = worldPos;
                flatAgent.y = 0f;
                flatTarget.y = 0f;
                return Vector3.Distance(flatAgent, flatTarget) <= finalThreshold + 0.25f;
            }
        }

        Vector3 a = transform.position;
        Vector3 b = worldPos;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b) <= finalThreshold;
    }

    private Vector3 GetBestNavDestination(Vector3 desiredWorldPos)
    {
        if (_agent != null && _agent.enabled)
        {
            float sampleRadius = Mathf.Max(2f, _agent.radius * 6f);
            if (NavMesh.SamplePosition(desiredWorldPos, out NavMeshHit hit, sampleRadius, _agent.areaMask))
                return hit.position;
        }

        return desiredWorldPos;
    }

    private void UpdateFacing()
    {
        Vector3 dir = Vector3.zero;

        if (_agent != null && _agent.enabled)
            dir = _agent.desiredVelocity;

        if (dir.sqrMagnitude < 0.001f && combatTarget != null)
            dir = GetAimPoint(combatTarget) - transform.position;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return;

        _lastMoveDir = dir.normalized;
        Quaternion targetRot = Quaternion.LookRotation(_lastMoveDir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }

    private float GetGroundYNearPosition(Vector3 pos, out bool found)
    {
        found = false;

        if (groundLayers.value != 0)
        {
            Vector3 origin = new Vector3(pos.x, pos.y + 50f, pos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                found = true;
                return hit.point.y;
            }
        }

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            found = true;
            return _agent.nextPosition.y;
        }

        return pos.y;
    }

    #endregion

    #region Death

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        state = DroneState.Disabled;
        combatTarget = null;

        if (_agent != null)
        {
            if (_agent.enabled && _agent.isOnNavMesh)
                _agent.ResetPath();
            _agent.enabled = false;
        }

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        if (disableCollidersOnDeath)
        {
            foreach (Collider col in GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        PlayDetachedDeathSound();

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position + explosionOffset, Quaternion.identity);

        OnAnyDroneDied?.Invoke(this);

        enabled = false;

        if (destroyOnDeath)
            Destroy(gameObject, Mathf.Max(0.05f, destroyDelay));
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

        if (waypoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawSphere(waypoints[i].position, 0.25f);
                if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                    Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
            }
        }
    }

    #endregion
}
