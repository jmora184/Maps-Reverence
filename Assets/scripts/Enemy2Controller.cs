using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy AI (Enemy2Controller) with:
/// - Safe player resolution (no NullReference if playerController.instance is null)
/// - Aggro onto the last attacker (ally) via GetShot(attacker) or SetCombatTarget(attacker)
/// - Passive aggro onto nearby allies that pass close by (optional scan)
/// - Standoff range + backoff so the enemy doesn't "hug" and get stuck
/// - Root motion disabled at runtime (prevents sliding into targets while agent is stopped)
/// </summary>
public class Enemy2Controller : MonoBehaviour
{
    [Header("Chase / Perception")]
    public float distanceToChase = 10f;
    public float distanceToLose = 15f;

    [Header("Aggro Gates")]
    [Tooltip("If true, the enemy will ONLY switch to an attacker/combat target when they are within the ranges below.")]
    public bool onlyAggroIfTargetInRange = true;

    [Tooltip("Max distance to accept a new combat target from damage (GetShot(attacker)). If 0, uses distanceToChase.")]
    public float aggroFromDamageMaxDistance = 0f;

    [Tooltip("Max distance to accept a new combat target from external orders/calls (SetCombatTarget). If 0, uses distanceToChase.")]
    public float aggroFromOrdersMaxDistance = 0f;

    [Tooltip("Legacy stop distance (kept for Inspector compatibility).")]
    public float distanceToStop = 2f;

    [Tooltip("How long to keep chasing after losing the target.")]
    public float keepChasingTime = 2f;

    [Header("Ally Aggro (passive)")]
    [Tooltip("If an ally passes within this distance, the enemy will aggro even if the ally never shot.")]
    public float allyAggroRadius = 8f;

    [Tooltip("If the current combat ally gets farther than this for keepChasingTime, we drop them and fall back.")]
    public float allyLoseRadius = 15f;

    [Tooltip("How often we scan for nearby allies (seconds).")]
    public float allyScanInterval = 0.25f;

    [Tooltip("Optional: limit which colliders are considered in the scan (set to your Ally layer).")]
    public LayerMask allyLayerMask = ~0;

    [Tooltip("Optional: if true, require a clear line of sight to aggro an ally.")]
    public bool requireLineOfSightToAggro = false;

    [Tooltip("Line of sight check uses this mask. Usually Everything except UI.")]
    public LayerMask lineOfSightMask = ~0;

    [Header("Combat Range")]
    [Tooltip("Preferred distance to keep from the current combat target while fighting.")]
    public float desiredAttackRange = 6f;


    [Tooltip("If true, use a min/max band instead of a single desiredAttackRange.")]
    public bool enableDesiredAttackRangeBand = false;

    [Tooltip("Minimum desired combat distance when using a range band.")]
    public float desiredAttackRangeMin = 4f;

    [Tooltip("Maximum desired combat distance when using a range band.")]
    public float desiredAttackRangeMax = 8f;

    [Header("Dynamic Desired Range")]
    [Tooltip("If true, we periodically pick a new preferred distance (inside the band) every few seconds.")]
    public bool enableDynamicDesiredAttackRange = false;

    [Tooltip("Minimum seconds between re-picking the desired range.")]
    public float desiredRangeUpdateIntervalMin = 2f;

    [Tooltip("Maximum seconds between re-picking the desired range.")]
    public float desiredRangeUpdateIntervalMax = 3f;

    [Tooltip("If true, we only update the desired range while in combat/chasing.")]
    public bool updateDesiredRangeOnlyWhenInCombat = true;

    [Tooltip("Buffer around desiredAttackRange to prevent jitter (hysteresis).")]
    public float attackRangeBuffer = 0.75f;

    [Tooltip("Extra distance past the outer ring before we step forward (prevents yo-yo).")]
    public float approachSlack = 2f;

    [Tooltip("Extra distance inside the inner ring before we back off (prevents jitter).")]
    public float backoffSlack = 1f;

    [Tooltip("Caps how far a single backoff move can step (prevents runaway pushing/retreat loops).")]
    public float maxBackoffStep = 4f;

    [Header("Shooting While Moving")]
    [Tooltip("If true, the enemy can keep firing while approaching/backing off (prevents silent backpedal loops).")]
    public bool allowShootingWhileMoving = true;

    [Tooltip("Extra distance beyond the outer ring where we still allow shooting while moving.")]
    public float shootWhileMovingExtraDistance = 3f;

    [Tooltip("NavMesh sample radius used when backing away.")]
    public float backoffSampleRadius = 2.5f;

    [Tooltip("Turn speed when facing the target.")]
    public float faceTargetTurnSpeed = 10f;

    [Tooltip("How often we repath while approaching/backing off (seconds).")]
    public float combatRepathInterval = 0.2f;


    [Header("Pause While Shooting (In Range)")]
    [Tooltip("If true, the enemy will periodically pause in place (for a short duration) while shooting in-range, then reposition briefly.")]
    public bool pauseWhileShootingInRange = true;

    [Tooltip("Min seconds to pause and hold position while shooting.")]
    public float pauseShootMinSeconds = 0.9f;

    [Tooltip("Max seconds to pause and hold position while shooting.")]
    public float pauseShootMaxSeconds = 1.3f;

    [Tooltip("Min seconds of movement/strafe between pause windows.")]
    public float pauseMoveBurstMinSeconds = 0.4f;

    [Tooltip("Max seconds of movement/strafe between pause windows.")]
    public float pauseMoveBurstMaxSeconds = 0.8f;

    [Tooltip("If true, the pause system only runs when we are within the desired range band (in-range).")]
    public bool pauseOnlyWhenInRange = true;


    [Header("Aim / Fire Gate")]
    [Tooltip("If true, we only fire when our aim is within fireConeDegrees of the target (prevents shooting backwards).")]
    public bool useFireConeGate = true;

    [Tooltip("Cone half-angle in degrees for allowed firing. 30 is very strict; 60-90 feels better for moving gunfights.")]
    [Range(5f, 180f)]
    public float fireConeDegrees = 75f;

    [Tooltip("If true, the firing cone uses firePoint.forward (aim) instead of transform.forward (body).")]
    public bool useFirePointForAimGate = true;


    [Header("Combat Dodge / Strafing")]
    [Tooltip("If true, the enemy will strafe/circle while shooting instead of standing still.")]
    public bool enableCombatStrafe = true;

    [Tooltip("How often (seconds) we pick a new strafe point around the target.")]
    public float strafeRepathInterval = 0.35f;

    [Tooltip("Seconds between changing strafe direction/angle.")]
    public float strafeChangeInterval = 1.25f;

    [Tooltip("Max random angle jitter added to the strafe direction (degrees).")]
    public float strafeAngleJitter = 25f;

    [Tooltip("NavMesh sample radius used when picking a strafe point.")]
    public float strafeSampleRadius = 2.5f;

    [Header("Team Leash (Stay Near Team)")]
    [Tooltip("If true and this enemy is under an EnemyTeam_* root, it will try to stay within a radius of the team anchor/centroid.")]
    public bool enableTeamLeash = true;

    [Tooltip("Soft radius: if the enemy gets beyond this distance from the team anchor, it will start returning.")]
    public float teamLeashRadius = 22f;

    [Tooltip("Return stop radius (hysteresis). Enemy stops returning when it gets within this distance of the team anchor.")]
    public float teamLeashReturnRadius = 16f;

    [Tooltip("Hard limit: if the enemy is chasing/fighting and exceeds this distance from the team anchor, it will break off and return.")]
    public float teamLeashHardLimit = 30f;

    [Tooltip("How often we repath while returning to the team anchor (seconds).")]
    public float teamLeashRepathInterval = 0.35f;

    [Tooltip("If true, the enemy will break combat when it leaves teamLeashRadius (not just teamLeashHardLimit).")]
    public bool breakCombatWhenOutsideSoftRadius = false;
    [Header("Refs")]
    public NavMeshAgent agent;


    [Header("Water Slow")]
    [Tooltip("If true, this enemy's NavMeshAgent speed is multiplied while inside a WaterSlowZone trigger.")]
    public bool enableWaterSlow = true;
    [Range(0.05f, 1f)]
    public float defaultWaterSpeedMultiplier = 0.65f;

    // Runtime multiplier set by WaterSlowZone (1 = normal).
    private float _waterSpeedMultiplier = 1f;

    private float _baseAgentSpeed = 0f;
    public GameObject bullet;
    public Transform firePoint;
    public Animator anim;

    [Header("Shooting")]
    public float fireRate = 0.5f;
    public float waitBetweenShots = 2f;
    public float timeToShoot = 1f;

    // --- runtime ---

    // Desired range runtime
    private float _currentDesiredAttackRange = -1f;
    private float _nextDesiredRangeUpdateTime = 0f;

    // Pause / move burst runtime
    private float _pauseShootTimer = 0f;
    private float _pauseMoveBurstTimer = 0f;

    private bool chasing;
    private float chaseCounter;

    private float fireCount;
    private float shotWaitCounter;
    private float shootTimeCounter;

    // Targeting
    private Transform _combatTarget;     // ally (or player) we are currently fighting
    private Transform _playerFallback;   // resolved player transform if available
    private float _repathTimer;


    // Combat strafe runtime
    private float _strafeRepathTimer;
    private float _strafeChangeTimer;
    private int _strafeSide = 1; // 1 = right, -1 = left
    private float _strafeAngleOffset;
    // Passive ally aggro
    private float _allyScanTimer;
    private float _combatLoseCounter;
    private readonly Collider[] _allyHits = new Collider[24];

    // Team leash runtime
    private EncounterTeamAnchor _teamAnchor;
    private bool _returningToTeam;
    private float _leashRepathTimer;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent != null && _baseAgentSpeed <= 0f) _baseAgentSpeed = agent.speed;
        if (anim == null) anim = GetComponentInChildren<Animator>();

        // Root motion can move the character even when agent is stopped.
        if (anim != null) anim.applyRootMotion = false;

        // We rotate manually.
        if (agent != null) agent.updateRotation = false;

        ResolvePlayerFallback();
        ResolveTeamAnchor();
    }

    private void Start()
    {
        shootTimeCounter = timeToShoot;
        shotWaitCounter = waitBetweenShots;

        // Defaults: if not set in Inspector, use distanceToChase as the "awareness" radius.
        if (aggroFromDamageMaxDistance <= 0f) aggroFromDamageMaxDistance = distanceToChase;
        if (aggroFromOrdersMaxDistance <= 0f) aggroFromOrdersMaxDistance = distanceToChase;


        // Initialize desired range & pause cycle so combat doesn't start with invalid timers.
        _currentDesiredAttackRange = desiredAttackRange;
        ResetDesiredRangeNow();
        _pauseShootTimer = 0f;
        _pauseMoveBurstTimer = 0f;

    }

    private bool IsWithinAggroRange(Transform t, float maxDistance)
    {
        if (!onlyAggroIfTargetInRange) return true;
        if (t == null) return false;
        return Vector3.Distance(transform.position, t.position) <= Mathf.Max(0.1f, maxDistance);
    }

    private void SetCombatTargetInternal(Transform t)
    {
        _combatTarget = t;
        chasing = true;
        chaseCounter = 0f;
        _combatLoseCounter = 0f;

        // reset shooting cadence so it doesn't "stall" on first aggro
        fireCount = 0f;
        shootTimeCounter = timeToShoot;
        shotWaitCounter = 0f;

        // Re-pick a preferred distance and restart pause cycle for this engagement.
        ResetDesiredRangeNow();
        _pauseShootTimer = 0f;
        _pauseMoveBurstTimer = 0f;


        if (agent != null && agent.isActiveAndEnabled)
            agent.isStopped = false;
    }

    private void ResolvePlayerFallback()
    {
        // Prefer your singleton if present.
        if (playerController.instance != null)
        {
            _playerFallback = playerController.instance.transform;
            return;
        }

        // Otherwise, fall back to tag search.
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _playerFallback = p.transform;
    }

    private void ResolveTeamAnchor()
    {
        // Prefer a centroid anchor on the team root (added by EncounterDirectorPOC when creating teams).
        _teamAnchor = GetComponentInParent<EncounterTeamAnchor>();

        // If you aren't using EncounterTeamAnchor, you can still keep enableTeamLeash off.
    }

    /// <summary>
    /// Explicitly set the combat target (e.g., the ally that shot this enemy).
    /// </summary>
    public void SetCombatTarget(Transform t)
    {
        if (t == null) return;

        // IMPORTANT: Prevent global "telepathy" aggro when some other system tries to set the target
        // (e.g., selecting an enemy from across the map).
        if (!IsWithinAggroRange(t, aggroFromOrdersMaxDistance))
            return;

        SetCombatTargetInternal(t);
    }

    /// <summary>
    /// Backwards compatible: existing code may call GetShot() with no args.
    /// If you can, call GetShot(attackerTransform) for proper ally aggro.
    /// </summary>
    public void GetShot()
    {
        // Without an attacker, at least start chasing (fallback to player if available).
        chasing = true;
        chaseCounter = 0f;

        fireCount = 0f;
        shootTimeCounter = timeToShoot;
        shotWaitCounter = 0f;
    }

    /// <summary>
    /// Preferred overload: pass the shooter (ally) so the enemy fights them instead of tunneling to the player.
    /// </summary>
    public void GetShot(Transform attacker)
    {
        if (attacker != null)
        {
            // Damage aggro can have its own radius, separate from SetCombatTarget orders.
            // This prevents an enemy from "snapping" to an attacker anywhere on the map.
            if (!IsWithinAggroRange(attacker, aggroFromDamageMaxDistance))
                return;

            SetCombatTargetInternal(attacker);
            return;
        }

        GetShot();
    }

    /// <summary>
    /// Called by WaterSlowZone triggers. Multiplier is clamped and applied to this enemy's NavMeshAgent speed.
    /// </summary>
    public void SetWaterSlow(bool inWater, float speedMultiplier)
    {
        if (!enableWaterSlow) return;

        if (inWater)
            _waterSpeedMultiplier = Mathf.Clamp(speedMultiplier > 0f ? speedMultiplier : defaultWaterSpeedMultiplier, 0.05f, 1f);
        else
            _waterSpeedMultiplier = 1f;
    }

    private float GetWaterMult()
    {
        return enableWaterSlow ? _waterSpeedMultiplier : 1f;
    }

    private void ApplyWaterSlowToAgent()
    {
        if (agent == null) return;
        if (_baseAgentSpeed <= 0f) _baseAgentSpeed = agent.speed;

        float desired = _baseAgentSpeed * GetWaterMult();
        if (!Mathf.Approximately(agent.speed, desired))
            agent.speed = desired;
    }


    private void Update()
    {
        // Keep trying to resolve player if needed (scene load / respawn).
        if (_playerFallback == null)
            ResolvePlayerFallback();

        // Apply water slow (if any) before movement decisions.
        ApplyWaterSlowToAgent();

        // Team leash: keep members from wandering too far from their EnemyTeam anchor/centroid.
        // If this returns true, we are currently returning to the team and should skip normal combat logic this frame.
        if (UpdateTeamLeash())
            return;

        ResolveTeamAnchor();

        // Passive aggro: if we do NOT already have a combat target, look for nearby allies.
        // Right now this script only auto-acquires based on distance to the CURRENT target,
        // which is usually the player (see original logic). That is why allies passing by are ignored.
        // This scan fixes that.
        if (_combatTarget == null)
        {
            _allyScanTimer -= Time.deltaTime;
            if (_allyScanTimer <= 0f)
            {
                _allyScanTimer = allyScanInterval;

                Transform nearbyAlly = FindNearestAggroAlly();
                if (nearbyAlly != null)
                {
                    SetCombatTarget(nearbyAlly);
                }
            }
        }

        // If our combat target got destroyed, fall back to player.
        if (_combatTarget == null)
        {
            // nothing to do here, we'll fall back below
        }

        Transform target = _combatTarget != null ? _combatTarget : _playerFallback;

        if (target == null)
        {
            // No target exists yet -> park safely.
            StopAgent();
            SetMovingAnim(false);
            return;
        }

        Vector3 targetPoint = target.position;
        targetPoint.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, targetPoint);

        // Acquire chase if close enough (only when we don't have an aggro target).
        // NOTE: In the original code, this only checked distance to the PLAYER fallback,
        // so allies passing by would never trigger. (target == _playerFallback in that case)
        if (_combatTarget == null && !chasing)
        {
            if (dist <= distanceToChase)
            {
                chasing = true;
                chaseCounter = 0f;
            }
        }

        // Lose chase only when we're chasing the fallback player and far away.
        if (_combatTarget == null && chasing && dist >= distanceToLose)
        {
            chaseCounter += Time.deltaTime;
            if (chaseCounter >= keepChasingTime)
            {
                chasing = false;
                chaseCounter = 0f;
            }
        }

        // If we are chasing an ALLY combat target and they leave the area, drop them.
        if (_combatTarget != null)
        {
            if (dist >= allyLoseRadius)
            {
                _combatLoseCounter += Time.deltaTime;
                if (_combatLoseCounter >= keepChasingTime)
                {
                    _combatTarget = null;
                    chasing = false;
                    _combatLoseCounter = 0f;

                    StopAgent();
                    SetMovingAnim(false);
                    return;
                }
            }
            else
            {
                _combatLoseCounter = 0f;
            }
        }

        if (!chasing)
        {
            StopAgent();
            SetMovingAnim(false);
            return;
        }

        // Face target (flat)
        Vector3 flatDir = targetPoint - transform.position;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * faceTargetTurnSpeed);
        }

        // Update desired range periodically (if enabled). We treat Min/Max as the "hard" in-range band,
        // and the current desired range as a soft preference within that band.
        bool inCombatNow = chasing;
        UpdateDesiredRangeTick(inCombatNow);

        float range = GetDesiredRange(); float inner = Mathf.Max(0.1f, range - attackRangeBuffer - Mathf.Max(0f, backoffSlack));
        float outer = range + attackRangeBuffer + Mathf.Max(0f, approachSlack);

        _repathTimer -= Time.deltaTime;
        bool canRepath = _repathTimer <= 0f;

        if (dist > outer)
        {
            TickPauseCycle(inRange: false);

            if (canRepath)
            {
                // Approach to the ring around target (not its exact center).
                Vector3 toward = (targetPoint - transform.position);
                toward.y = 0f;

                if (toward.sqrMagnitude < 0.0001f)
                    toward = transform.forward;

                Vector3 ringPoint = targetPoint - toward.normalized * range;

                SetDestinationSafe(ringPoint, range);
                _repathTimer = combatRepathInterval;
            }

            SetMovingAnim(true);

            // Optionally keep shooting while repositioning (feels more like a gunfight).
            if (allowShootingWhileMoving && dist <= (outer + Mathf.Max(0f, shootWhileMovingExtraDistance)))
                HandleShooting(target);
        }
        else if (dist < inner)
        {
            TickPauseCycle(inRange: false);

            if (canRepath)
            {
                // Too close: back off a bit.
                Vector3 away = (transform.position - targetPoint);
                away.y = 0f;

                if (away.sqrMagnitude < 0.0001f)
                    away = -transform.forward;

                float push = (range - dist) + 0.5f;

                if (maxBackoffStep > 0.01f)
                    push = Mathf.Clamp(push, 1f, maxBackoffStep);
                else
                    push = Mathf.Max(1f, push);

                Vector3 desired = transform.position + away.normalized * push;

                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, backoffSampleRadius, NavMesh.AllAreas))
                    desired = hit.position;

                SetDestinationSafe(desired, range);
                _repathTimer = combatRepathInterval;
            }

            SetMovingAnim(true);

            // Optionally keep shooting while repositioning (feels more like a gunfight).
            if (allowShootingWhileMoving && dist <= (outer + Mathf.Max(0f, shootWhileMovingExtraDistance)))
                HandleShooting(target);
        }
        else
        {

            // Pause/move-burst cycle while in-range (optional).
            TickPauseCycle(inRange: true);

            if (IsInShootPause())
            {
                // Hold position and shoot for a short duration.
                StopAgent();
                SetMovingAnim(false);
                HandleShooting(target);
                return;
            }
            // In range: optionally strafe/circle while shooting (more dynamic combat).
            if (enableCombatStrafe)
            {
                _strafeRepathTimer -= Time.deltaTime;
                _strafeChangeTimer -= Time.deltaTime;

                if (_strafeChangeTimer <= 0f)
                {
                    if (Random.value < 0.35f) _strafeSide *= -1;
                    _strafeAngleOffset = Random.Range(-strafeAngleJitter, strafeAngleJitter);
                    _strafeChangeTimer = Mathf.Max(0.1f, strafeChangeInterval);
                }

                if (agent != null && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.05f))
                    _strafeRepathTimer = 0f;

                if (_strafeRepathTimer <= 0f)
                {
                    Vector3 fromTarget = (transform.position - targetPoint);
                    fromTarget.y = 0f;
                    if (fromTarget.sqrMagnitude < 0.001f)
                        fromTarget = transform.forward;

                    Quaternion rot = Quaternion.AngleAxis((_strafeSide * 90f) + _strafeAngleOffset, Vector3.up);
                    Vector3 strafeDir = rot * fromTarget.normalized;

                    Vector3 desired = targetPoint + strafeDir.normalized * range;
                    if (NavMesh.SamplePosition(desired, out NavMeshHit hit, strafeSampleRadius, NavMesh.AllAreas))
                        desired = hit.position;

                    SetDestinationSafe(desired, range);
                    _strafeRepathTimer = Mathf.Max(0.05f, strafeRepathInterval);
                }

                SetMovingAnim(true);
                HandleShooting(target);
            }
            else
            {
                // In range: stop and shoot.
                StopAgent();
                SetMovingAnim(false);
                HandleShooting(target);
            }
        }
    }


    /// <summary>
    /// Keeps this enemy within a radius of its team anchor (centroid) when part of an EnemyTeam_* root.
    /// Returns true when we are currently overriding behavior to return to the team.
    /// </summary>
    private bool UpdateTeamLeash()
    {
        if (!enableTeamLeash) return false;

        // Must have an anchor to leash to.
        if (_teamAnchor == null)
        {
            // In case parent roots were created after Awake (rare), try to resolve again.
            ResolveTeamAnchor();
            if (_teamAnchor == null) return false;
        }

        Vector3 anchor = _teamAnchor.AnchorWorldPosition;
        anchor.y = transform.position.y;

        float distToAnchor = Vector3.Distance(transform.position, anchor);

        // If we're in combat/chasing and we break the hard limit, drop combat and return.
        bool hasCombat = (_combatTarget != null);

        float soft = Mathf.Max(0.1f, teamLeashRadius);
        float stop = Mathf.Clamp(teamLeashReturnRadius, 0.05f, soft);
        float hard = Mathf.Max(soft, teamLeashHardLimit);

        if (hasCombat)
        {
            if (distToAnchor > hard || (breakCombatWhenOutsideSoftRadius && distToAnchor > soft))
            {
                _combatTarget = null;
                chasing = false;
                _returningToTeam = true;
                _leashRepathTimer = 0f;

                // Stop any current path so the return path wins.
                StopAgent();
            }
        }

        // If we are not in combat but wandered outside the soft radius, start returning.
        if (!hasCombat && distToAnchor > soft)
        {
            _returningToTeam = true;
        }

        if (_returningToTeam)
        {
            // When close enough, stop returning and resume normal behavior.
            if (distToAnchor <= stop)
            {
                _returningToTeam = false;
                StopAgent();
                SetMovingAnim(false);
                return false;
            }

            _leashRepathTimer -= Time.deltaTime;
            if (_leashRepathTimer <= 0f)
            {
                // Move toward the anchor point.
                SetDestinationSafe(anchor, 1.5f);
                _leashRepathTimer = Mathf.Max(0.05f, teamLeashRepathInterval);
            }

            // Face the anchor while returning (helps animation/aiming look less weird).
            Vector3 dir = anchor - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * faceTargetTurnSpeed);
            }

            SetMovingAnim(true);
            return true;
        }

        return false;
    }

    private Transform FindNearestAggroAlly()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, allyAggroRadius, _allyHits, allyLayerMask);
        if (count <= 0) return null;

        Transform best = null;
        float bestSqr = float.MaxValue;

        Vector3 myPos = transform.position;
        myPos.y = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider c = _allyHits[i];
            if (c == null) continue;

            // We only consider actual allies.
            // This assumes your ally root has an AllyController.
            AllyController ally = c.GetComponentInParent<AllyController>();
            if (ally == null) continue;
            if (!ally.gameObject.activeInHierarchy) continue;

            Transform t = ally.transform;

            // Optional line of sight
            if (requireLineOfSightToAggro)
            {
                Vector3 eye = transform.position + Vector3.up * 1.3f;
                Vector3 tgt = t.position + Vector3.up * 1.0f;
                Vector3 dir = (tgt - eye);
                float dist = dir.magnitude;
                if (dist > 0.001f)
                {
                    dir /= dist;
                    if (Physics.Raycast(eye, dir, out RaycastHit hit, dist, lineOfSightMask))
                    {
                        // If we hit something that isn't the ally, no LOS.
                        if (hit.transform != t && !hit.transform.IsChildOf(t))
                            continue;
                    }
                }
            }

            Vector3 p = t.position;
            p.y = 0f;
            float sqr = (p - myPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }

        return best;
    }


// -------------------- Desired Range + Pause While Shooting --------------------

private float GetDesiredRange()
    {
        float r = desiredAttackRange;

        if (enableDesiredAttackRangeBand)
        {
            float min = Mathf.Max(0.5f, desiredAttackRangeMin);
            float max = Mathf.Max(min, desiredAttackRangeMax);

            if (_currentDesiredAttackRange < 0f)
                _currentDesiredAttackRange = Mathf.Clamp(r, min, max);

            r = Mathf.Clamp(_currentDesiredAttackRange, min, max);
        }

        return Mathf.Max(0.5f, r);
    }

    private void ResetDesiredRangeNow()
    {
        if (enableDesiredAttackRangeBand)
        {
            float min = Mathf.Max(0.5f, desiredAttackRangeMin);
            float max = Mathf.Max(min, desiredAttackRangeMax);
            _currentDesiredAttackRange = Random.Range(min, max);
        }
        else
        {
            _currentDesiredAttackRange = Mathf.Max(0.5f, desiredAttackRange);
        }

        // schedule next update
        float a = Mathf.Max(0.05f, desiredRangeUpdateIntervalMin);
        float b = Mathf.Max(a, desiredRangeUpdateIntervalMax);
        float interval = enableDynamicDesiredAttackRange ? Random.Range(a, b) : 999999f;
        _nextDesiredRangeUpdateTime = Time.time + interval;
    }

    private void UpdateDesiredRangeTick(bool inCombat)
    {
        if (!enableDynamicDesiredAttackRange) return;
        if (updateDesiredRangeOnlyWhenInCombat && !inCombat) return;

        // Don't change the preferred range while we are in the "shoot pause" window,
        // otherwise it can immediately force a reposition and visually cancel the pause.
        if (pauseWhileShootingInRange && _pauseShootTimer > 0f) return;

        if (Time.time < _nextDesiredRangeUpdateTime) return;

        ResetDesiredRangeNow();
    }

    private void TickPauseCycle(bool inRange)
    {
        if (!pauseWhileShootingInRange)
        {
            _pauseShootTimer = 0f;
            _pauseMoveBurstTimer = 0f;
            return;
        }

        if (pauseOnlyWhenInRange && !inRange)
        {
            _pauseShootTimer = 0f;
            _pauseMoveBurstTimer = 0f;
            return;
        }

        float dt = Time.deltaTime;

        if (_pauseShootTimer > 0f)
        {
            _pauseShootTimer -= dt;
            if (_pauseShootTimer <= 0f)
            {
                _pauseMoveBurstTimer = Random.Range(
                    Mathf.Max(0f, pauseMoveBurstMinSeconds),
                    Mathf.Max(pauseMoveBurstMinSeconds, pauseMoveBurstMaxSeconds));
            }

            return;
        }

        if (_pauseMoveBurstTimer > 0f)
        {
            _pauseMoveBurstTimer -= dt;
            if (_pauseMoveBurstTimer <= 0f)
            {
                _pauseShootTimer = Random.Range(
                    Mathf.Max(0f, pauseShootMinSeconds),
                    Mathf.Max(pauseShootMinSeconds, pauseShootMaxSeconds));
            }

            return;
        }

        // Start with a pause window.
        _pauseShootTimer = Random.Range(
            Mathf.Max(0f, pauseShootMinSeconds),
            Mathf.Max(pauseShootMinSeconds, pauseShootMaxSeconds));
    }

    private bool IsInShootPause()
    {
        return pauseWhileShootingInRange && _pauseShootTimer > 0f;
    }

    void HandleShooting(Transform target)
    {
        if (bullet == null || firePoint == null) return;

        if (shotWaitCounter > 0f)
        {
            shotWaitCounter -= Time.deltaTime;
            return;
        }

        shootTimeCounter -= Time.deltaTime;
        if (shootTimeCounter > 0f)
            return;

        fireCount -= Time.deltaTime;
        if (fireCount > 0f)
            return;

        fireCount = fireRate;

        // Aim
        Vector3 aimPos = target.position + new Vector3(0f, 0.5f, 0f);
        firePoint.LookAt(aimPos);

        // Aim gate: allow firing when our aim is reasonably aligned with the target.
        // Using firePoint.forward makes ranged combat feel better while strafing.
        Vector3 aimFrom = firePoint.position;
        Vector3 aimFwd = (useFirePointForAimGate ? firePoint.forward : transform.forward);
        Vector3 toAim = aimPos - aimFrom;

        // Ignore vertical angle for gating (helps on slopes / uneven ground)
        toAim.y = 0f;

        float aimAngle = 0f;
        if (toAim.sqrMagnitude > 0.0001f)
            aimAngle = Vector3.Angle(aimFwd, toAim.normalized);

        bool canFire = !useFireConeGate || aimAngle <= Mathf.Clamp(fireConeDegrees, 1f, 179f);

        if (canFire)
        {
            Instantiate(bullet, firePoint.position, firePoint.rotation);
            if (anim != null) anim.SetTrigger("fireShot");
        }

        // reset cadence windows
        shotWaitCounter = waitBetweenShots;
        shootTimeCounter = timeToShoot;
    }

    private void StopAgent()
    {
        if (agent == null || !agent.isActiveAndEnabled) return;
        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    private void SetDestinationSafe(Vector3 pos, float range)
    {
        if (agent == null || !agent.isActiveAndEnabled) return;

        agent.isStopped = false;
        agent.autoBraking = false;

        // Avoid destination thrash (prevents "skipping").
        if (agent.hasPath)
        {
            Vector3 d = agent.destination;
            d.y = pos.y;
            if ((d - pos).sqrMagnitude < 0.04f)
                return;
        }

        // Ensure stopping distance is not zero.
        // NOTE: we still manage standoff ourselves; this is just for nicer braking.
        agent.stoppingDistance = Mathf.Clamp(range * 0.05f, 0.1f, 1.5f);

        agent.SetDestination(pos);
    }

    private void SetMovingAnim(bool moving)
    {
        if (anim == null) return;
        // Your animator param is "isMoving" (per screenshot).
        anim.SetBool("isMoving", moving);
    }

    private void OnDrawGizmosSelected()
    {
        // Helpful debug rings
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, allyAggroRadius);
    }
}