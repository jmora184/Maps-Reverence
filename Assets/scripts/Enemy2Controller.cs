
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
    public float pauseShootMinSeconds = 0.5f;

    [Tooltip("Max seconds to pause and hold position while shooting.")]
    public float pauseShootMaxSeconds = 0.8f;

    [Tooltip("Min seconds of movement/strafe between pause windows.")]
    public float pauseMoveBurstMinSeconds = 0.8f;

    [Tooltip("Max seconds of movement/strafe between pause windows.")]
    public float pauseMoveBurstMaxSeconds = 1.4f;

    [Tooltip("If true, the pause system only runs when we are within the desired range band (in-range).")]
    public bool pauseOnlyWhenInRange = true;

    [Header("Pause Cooldown (Optional)")]
    [Tooltip("If enabled, the in-range pause window can only START once per cooldown period (so the enemy idles only occasionally).")]
    public bool usePauseCooldown = true;

    [Tooltip("Minimum seconds between pause windows (randomized per pause).")]
    public float pauseCooldownMinSeconds = 8f;

    [Tooltip("Maximum seconds between pause windows (randomized per pause).")]
    public float pauseCooldownMaxSeconds = 12f;

    [Tooltip("If enabled, each enemy gets a random initial cooldown offset so squads don't pause in sync.")]
    public bool randomizeInitialPauseOffset = true;

    [Header("Ally-Like Combat Movement")]
    [Tooltip("If false, we behave like AllyController: outer/inner thresholds use only desiredRange +/- attackRangeBuffer (no extra slack).")]
    public bool useApproachBackoffSlack = false;

    [Tooltip("Max per-enemy sideways offset when approaching to reduce stacking (scaled by range).")]
    public float approachSpreadScale = 0.15f;

    [Tooltip("Min/Max approach spread (world units).")]
    public float approachSpreadMin = 1f;
    public float approachSpreadMax = 6f;

    [Header("Combat Burst Strafing (Ally Style)")]
    [Tooltip("When using pauseWhileShootingInRange, movement bursts use a diagonal blend of tangential + radial motion.")]
    public float combatBurstMoveDistance = 6f;
    [Tooltip("Weight of the radial (toward/away) component during burst movement.")]
    public float combatBurstRadialWeight = 0.65f;
    [Tooltip("Weight of the tangential (left/right) component during burst movement.")]
    public float combatBurstTangentialWeight = 1f;

    [Header("Combat Engagement Pause (Ally Style)")]
    [Tooltip("After receiving a combat target via orders/damage, temporarily disable the in-range pause so the enemy keeps repositioning.")]
    public float forceStrafeAfterAggroSeconds = 6f;

    [Header("Animator Params (Optional)")]
    [Tooltip("If your animator uses AllyController params, set runBoolParam to 'isRunning' and speedFloatParam to 'Speed'.")]
    public string runBoolParam = "isRunning";
    [Tooltip("Legacy bool param some enemy animators use.")]
    public string legacyMoveBoolParam = "isMoving";
    public string speedFloatParam = "Speed";

    // Ally-style pause timers (separate from legacy TickPauseCycle).
    private float _combatPauseTimer = 0f;
    private float _combatMoveBurstTimer = 0f;
    private float _forceStrafeUntilTime = 0f;


    private float _nextPauseAllowedTime = 0f;
    [Header("Aim / Fire Gate")]
    [Tooltip("If true, we only fire when our aim is within fireConeDegrees of the target (prevents shooting backwards).")]
    public bool useFireConeGate = true;

    [Tooltip("Cone half-angle in degrees for allowed firing. 30 is very strict; 60-90 feels better for moving gunfights.")]
    [Range(5f, 180f)]
    public float fireConeDegrees = 75f;

    [Tooltip("If true, the firing cone uses firePoint.forward (aim) instead of transform.forward (body).")]
    public bool useFirePointForAimGate = true;




    [Header("Aim Targeting")]
    [Tooltip("If the target has a child Transform with this name, we aim at it (recommended: add AimPoint on Player/Allies at chest height).")]
    public string aimPointChildName = "AimPoint";

    [Tooltip("If no AimPoint is found, aim at the target collider bounds center (usually torso).")]
    public bool useColliderBoundsForAim = true;

    [Tooltip("Fallback vertical offset added to target.position if AimPoint and collider are missing.")]
    public float aimHeightOffset = 1.2f;

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

    [Header("Muzzle Flash (Simple Toggle)")]
    [Tooltip("Assign your Muzzle Flash GameObject (mesh spheres/cubes). It can live anywhere; we'll move it to firePoint when firing.")]
    public GameObject muzzleFlashObject;
    [Range(0.005f, 0.2f)]
    [Tooltip("How long the muzzle flash stays visible per shot (seconds). Typical 0.03 - 0.08")]
    public float muzzleFlashDuration = 0.05f;
    [Tooltip("If true, we snap the muzzle flash object to firePoint position/rotation when firing.")]
    public bool muzzleFlashFollowFirePoint = true;
    [Tooltip("Optional local offset from firePoint when snapping.")]
    public Vector3 muzzleFlashLocalOffset = Vector3.zero;

    [Header("Muzzle Flash Debug")]
    public bool debugMuzzleFlash = false;
    private Coroutine _muzzleFlashRoutine;
    public Animator anim;

    [Header("Death")]
    [Tooltip("Optional: if present, we will use this to play death animation + disable AI/nav + cleanup instead of vanishing instantly.")]
    public MnR.DeathController deathController;

    [Tooltip("Fallback Animator Trigger name if no DeathController is present.")]
    public string deathTriggerName = "Die";

    [Tooltip("If true, this Enemy2Controller disables itself after death so no AI logic continues running.")]
    public bool disableThisAIOnDeath = true;

    public bool IsDead => _isDead || (deathController != null && deathController.IsDead);
    private bool _isDead;


    [Header("Shooting")]
    public float fireRate = 0.5f;
    public float waitBetweenShots = 2f;
    public float timeToShoot = 1f;


    [Tooltip("How many bullets to fire per burst (e.g., 3).")]
    public int shotsPerBurst = 3;

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


    // Burst runtime
    private int _burstRemaining;
    private float _burstShotTimer;
    // Targeting
    private Transform _combatTarget;

    [Header("Idle Patrol (optional)")]
    [Tooltip("If enabled, this enemy will patrol waypoints when NOT chasing and NOT fighting.")]
    [SerializeField] private bool enableIdlePatrol = false;

    [Tooltip("Waypoint patrol component on this enemy (recommended). Leave null to disable.")]
    [SerializeField] private EnemyWaypointPatrol idlePatrol;

    [Tooltip("Seconds to wait after losing combat/chase before resuming patrol.")]
    [SerializeField] private float patrolResumeDelay = 0.75f;

    private float _patrolResumeAt = -1f;
    private AllyHealth _subscribedAllyHealth;
    // ally (or player) we are currently fighting
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
        if (deathController == null) deathController = GetComponent<MnR.DeathController>();

        if (deathController != null)
            deathController.OnDied += HandleDeathControllerDied;

        // Root motion can move the character even when agent is stopped.
        if (anim != null) anim.applyRootMotion = false;

        // We rotate manually.
        if (agent != null) agent.updateRotation = false;

        ResolvePlayerFallback();
        ResolveTeamAnchor();

        // De-sync pause cooldown across squad members so they don't all idle at once.
        if (usePauseCooldown && randomizeInitialPauseOffset)
        {
            float cdMax = Mathf.Max(pauseCooldownMinSeconds, pauseCooldownMaxSeconds);
            if (cdMax > 0f)
            {
                _nextPauseAllowedTime = Time.time + Random.Range(0f, cdMax);
            }
        }
    }

    private void OnDestroy()
    {
        if (deathController != null)
            deathController.OnDied -= HandleDeathControllerDied;
    }

    void Start()
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

        // Ally-style pause cycle (used by ChaseAndFightLikeAlly).
        _combatPauseTimer = 0f;
        _combatMoveBurstTimer = 0f;

        // When we newly aggro (damage/orders), stay active for a bit (don't immediately enter stand-still pause).
        _forceStrafeUntilTime = Time.time + Mathf.Max(0f, forceStrafeAfterAggroSeconds);

    }

    private bool IsWithinAggroRange(Transform t, float maxDistance)
    {
        if (!onlyAggroIfTargetInRange) return true;
        if (t == null) return false;
        return Vector3.Distance(transform.position, t.position) <= Mathf.Max(0.1f, maxDistance);
    }

    private void SetCombatTargetInternal(Transform t)
    {
        // If we previously subscribed to an ally target death event, remove it.
        if (_subscribedAllyHealth != null)
        {
            _subscribedAllyHealth.OnDied -= OnCombatTargetDied;
            _subscribedAllyHealth = null;
        }


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
        if (_isDead) return;
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
        if (_isDead) return;
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
        // If DeathController has already marked us dead, do nothing.
        if (IsDead)
        {
            if (disableThisAIOnDeath) enabled = false;
            return;
        }

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

        // Passive aggro scan (optional): if we do NOT already have a combat target, look for nearby allies.
        if (_combatTarget == null)
        {
            _allyScanTimer -= Time.deltaTime;
            if (_allyScanTimer <= 0f)
            {
                _allyScanTimer = allyScanInterval;

                Transform nearbyAlly = FindNearestAggroAlly();
                if (nearbyAlly != null)
                    SetCombatTarget(nearbyAlly);
            }
        }

        Transform target = _combatTarget != null ? _combatTarget : _playerFallback;

        // If target is dead/disabled/inactive, drop it so we don't keep firing at last known position forever.
        if (TargetIsDeadOrInvalid(target))
        {
            ClearCombatTarget();
            return;
        }

        if (target == null)
        {
            StopAgent();
            SetMovingAnim(false);
            UpdateSpeedParam();
            return;
        }

        Vector3 targetPoint = target.position;
        targetPoint.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, targetPoint);

        // Acquire chase if close enough (only when we don't have an aggro target).
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
                    UpdateSpeedParam();
                    return;
                }
            }
            else
            {
                _combatLoseCounter = 0f;
            }
        }


        // Optional: idle waypoint patrol (keeps enemies moving when not engaged).
        // IMPORTANT: Without this, Enemy2Controller stops/resets the agent every frame while !chasing,
        // which will cancel any external patrol script that sets destinations.
        if (enableIdlePatrol && idlePatrol != null)
        {
            bool hasCombat = (_combatTarget != null);
            bool engagedOrReturning = hasCombat || chasing || _returningToTeam;

            if (engagedOrReturning)
            {
                // Stop patrol immediately when combat/chase begins.
                if (idlePatrol.PatrolEnabled) idlePatrol.SetPatrolEnabled(false);
                _patrolResumeAt = Time.time + patrolResumeDelay;
            }
            else
            {
                // Resume patrol after a short delay once fully idle.
                if (_patrolResumeAt < 0f) _patrolResumeAt = Time.time;

                if (Time.time >= _patrolResumeAt)
                {
                    if (!idlePatrol.PatrolEnabled) idlePatrol.SetPatrolEnabled(true);

                    // Let patrol drive movement; do NOT StopAgent() here.
                    bool moving = (agent != null && agent.velocity.sqrMagnitude > 0.01f);
                    SetMovingAnim(moving);
                    UpdateSpeedParam();
                    return;
                }
                else
                {
                    if (idlePatrol.PatrolEnabled) idlePatrol.SetPatrolEnabled(false);
                }
            }
        }
        if (!chasing)
        {
            StopAgent();
            SetMovingAnim(false);
            UpdateSpeedParam();
            return;
        }

        // Ally-like combat movement + shooting.
        ChaseAndFightLikeAlly(target);

        // Animator speed float (optional).
        UpdateSpeedParam();
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


    private void ChaseAndFightLikeAlly(Transform target)
    {
        if (agent == null || !agent.isActiveAndEnabled || target == null) return;

        Vector3 aimPoint = GetAimPosition(target);

        // Face target (flat) while fighting.
        Vector3 flatDir = aimPoint - transform.position;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * faceTargetTurnSpeed);
        }

        // Range band support (same concept as AllyController).
        bool useBand = TryGetCombatRangeBand(out float bandMin, out float bandMax);

        // Update desired range periodically (if enabled).
        UpdateDesiredRangeTick(inCombat: true);

        float range = GetDesiredRange();
        if (useBand) range = Mathf.Clamp(range, bandMin, bandMax);

        float dist = Vector3.Distance(transform.position, aimPoint);

        // Ally-like thresholds: just +/- buffer (optionally include slack if enabled).
        float inner = range - attackRangeBuffer;
        float outer = range + attackRangeBuffer;

        if (useApproachBackoffSlack)
        {
            inner = (range - attackRangeBuffer) - Mathf.Max(0f, backoffSlack);
            outer = (range + attackRangeBuffer) + Mathf.Max(0f, approachSlack);
        }

        // Too far: move in toward a ring around the target (not the exact center).
        if ((useBand && !enableDynamicDesiredAttackRange) ? (dist > bandMax + attackRangeBuffer) : (dist > outer))
        {
            // Out of ideal range: reset Ally-style pause cycle so we can immediately reposition.
            _combatPauseTimer = 0f;
            _combatMoveBurstTimer = 0f;

            Vector3 toward = (aimPoint - transform.position);
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.001f) toward = transform.forward;

            float approachRange = (useBand && !enableDynamicDesiredAttackRange) ? bandMax : range;

            Vector3 ringPoint = aimPoint - toward.normalized * approachRange;

            // Per-enemy spread to reduce stacking (matches AllyController feel).
            Vector3 right = Vector3.Cross(Vector3.up, toward.normalized);
            float spread = Mathf.Clamp(approachRange * Mathf.Max(0f, approachSpreadScale), approachSpreadMin, approachSpreadMax);
            float seed = (GetInstanceID() * 0.1234f);
            float side = Mathf.Sin(seed) * spread;
            ringPoint += right * side;

            if (useBand)
                ringPoint = ClampToCombatRangeBandOnNavMesh(ringPoint, aimPoint, bandMin, bandMax, strafeSampleRadius);

            SetDestinationSafe(ringPoint, approachRange);
            SetMovingAnim(true);

            // Optionally keep shooting while repositioning.
            if (allowShootingWhileMoving && dist <= (outer + Mathf.Max(0f, shootWhileMovingExtraDistance)))
                HandleShooting(target);

            return;
        }

        // Too close: back off a bit.
        if ((useBand && !enableDynamicDesiredAttackRange) ? (dist < bandMin - attackRangeBuffer) : (dist < inner))
        {
            _combatPauseTimer = 0f;
            _combatMoveBurstTimer = 0f;

            Vector3 away = (transform.position - aimPoint);
            away.y = 0f;
            away = away.sqrMagnitude < 0.001f ? transform.forward : away.normalized;

            float backoffBase = (useBand && !enableDynamicDesiredAttackRange) ? bandMin : range;

            float push = (backoffBase - dist) + 0.5f;

            if (maxBackoffStep > 0.01f)
                push = Mathf.Clamp(push, 1f, maxBackoffStep);
            else
                push = Mathf.Max(1f, push);

            Vector3 desired = transform.position + away * push;

            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, backoffSampleRadius, NavMesh.AllAreas))
                desired = hit.position;

            if (useBand)
                desired = ClampToCombatRangeBandOnNavMesh(desired, aimPoint, bandMin, bandMax, backoffSampleRadius);

            SetDestinationSafe(desired, backoffBase);
            SetMovingAnim(true);

            // Optionally keep shooting while repositioning.
            if (allowShootingWhileMoving && dist <= (outer + Mathf.Max(0f, shootWhileMovingExtraDistance)))
                HandleShooting(target);

            return;
        }

        // In range: optionally strafe/circle while shooting (more dynamic combat).
        if (enableCombatStrafe)
        {
            bool pauseEpisodeActive = (_combatPauseTimer > 0f) || (_combatMoveBurstTimer > 0f);
            float _cooldownMax = Mathf.Max(pauseCooldownMinSeconds, pauseCooldownMaxSeconds);
            bool cooldownReady = (!usePauseCooldown) || (_cooldownMax <= 0f) || (Time.time >= _nextPauseAllowedTime);
            bool allowPause = pauseWhileShootingInRange && (Time.time >= _forceStrafeUntilTime) && (pauseEpisodeActive || cooldownReady);

            if (allowPause)
            {
                // Initialize pause timer on first entry (and start cooldown gate).
                if (_combatPauseTimer <= 0f && _combatMoveBurstTimer <= 0f)
                {
                    _combatPauseTimer = Random.Range(Mathf.Max(0f, pauseShootMinSeconds), Mathf.Max(pauseShootMinSeconds, pauseShootMaxSeconds));
                    if (usePauseCooldown)
                    {
                        float cdMax = Mathf.Max(pauseCooldownMinSeconds, pauseCooldownMaxSeconds);
                        if (cdMax > 0f)
                        {
                            float cdMin = Mathf.Max(0f, Mathf.Min(pauseCooldownMinSeconds, pauseCooldownMaxSeconds));
                            _nextPauseAllowedTime = Time.time + Random.Range(cdMin, cdMax);
                        }
                    }
                }

                // 1) Stand still and shoot for a short duration
                if (_combatPauseTimer > 0f)
                {
                    _combatPauseTimer -= Time.deltaTime;
                    StopAgent();
                    SetMovingAnim(false);
                    HandleShooting(target);
                    return;
                }

                // 2) Movement burst window
                if (_combatMoveBurstTimer <= 0f)
                {
                    _combatMoveBurstTimer = Random.Range(Mathf.Max(0.05f, pauseMoveBurstMinSeconds), Mathf.Max(pauseMoveBurstMinSeconds, pauseMoveBurstMaxSeconds));
                    _strafeRepathTimer = 0f; // force a new strafe point immediately
                }

                _combatMoveBurstTimer -= Time.deltaTime;

                _strafeRepathTimer -= Time.deltaTime;
                _strafeChangeTimer -= Time.deltaTime;

                if (_strafeChangeTimer <= 0f)
                {
                    if (Random.value < 0.35f) _strafeSide *= -1;
                    _strafeAngleOffset = Random.Range(-strafeAngleJitter, strafeAngleJitter);
                    _strafeChangeTimer = Mathf.Max(0.1f, strafeChangeInterval);
                }

                if (_strafeRepathTimer <= 0f)
                {
                    Vector3 toTarget = (aimPoint - transform.position);
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude < 0.001f)
                        toTarget = transform.forward;

                    Vector3 forwardToTarget = toTarget.normalized;

                    // Pick a point on a ring around the target (same idea as the continuous strafe),
                    // but during burst windows. This avoids "forward/back only" bursts when NavMesh sampling
                    // snaps near the radial line.
                    Quaternion rot = Quaternion.AngleAxis((_strafeSide * 90f) + _strafeAngleOffset, Vector3.up);

                    // Use the direction from the TARGET to US, then rotate it to get a circle point.
                    Vector3 fromTarget = (transform.position - aimPoint);
                    fromTarget.y = 0f;
                    if (fromTarget.sqrMagnitude < 0.001f)
                        fromTarget = -(forwardToTarget); // fallback
                    fromTarget.Normalize();

                    // Tangential direction around the target.
                    Vector3 ringDir = (rot * fromTarget).normalized;

                    // Choose radius: keep within the band if available, else use desired range.
                    float radius = range;
                    if (useBand)
                    {
                        Vector3 curFlat = (transform.position - aimPoint);
                        curFlat.y = 0f;
                        float cur = curFlat.magnitude;
                        radius = Mathf.Clamp(cur, bandMin, bandMax);
                    }
                    else
                    {
                        radius = Mathf.Max(0.5f, range);
                    }

                    // Optional small radial variation (keeps motion lively but still mostly sideways).
                    float radialJitter = Random.Range(-1f, 1f) * Mathf.Max(0f, attackRangeBuffer) * 0.25f;
                    radius = Mathf.Max(0.5f, radius + radialJitter);

                    Vector3 desired = aimPoint + ringDir * radius;

                    // Sample onto NavMesh near that ring point.
                    if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.25f, strafeSampleRadius), NavMesh.AllAreas))
                        desired = hit.position;

                    if (useBand)
                        desired = ClampToCombatRangeBandOnNavMesh(desired, aimPoint, bandMin, bandMax, Mathf.Max(0.25f, strafeSampleRadius));

                    SetDestinationSafe(desired, radius);
                    _strafeRepathTimer = Mathf.Max(0.05f, strafeRepathInterval);
                }

                SetMovingAnim(true);
                HandleShooting(target);

                // End burst → return to normal combat movement. Cooldown (if enabled) prevents immediate re-pause.
                if (_combatMoveBurstTimer <= 0f)
                {
                    _combatMoveBurstTimer = 0f;
                    _combatPauseTimer = 0f;
                }

                return;
            }

            // Continuous strafe (no pause): circle around the target at the preferred range.
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
                Vector3 fromTarget = (transform.position - aimPoint);
                fromTarget.y = 0f;
                if (fromTarget.sqrMagnitude < 0.001f)
                    fromTarget = transform.forward;

                Quaternion rot = Quaternion.AngleAxis((_strafeSide * 90f) + _strafeAngleOffset, Vector3.up);
                Vector3 strafeDir = rot * fromTarget.normalized;

                float strafeRingRadius = useBand ? Mathf.Clamp(range, bandMin, bandMax) : range;

                Vector3 desired = aimPoint + strafeDir.normalized * strafeRingRadius;
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, strafeSampleRadius, NavMesh.AllAreas))
                    desired = hit.position;

                if (useBand)
                    desired = ClampToCombatRangeBandOnNavMesh(desired, aimPoint, bandMin, bandMax, strafeSampleRadius);

                SetDestinationSafe(desired, range);
                _strafeRepathTimer = Mathf.Max(0.05f, strafeRepathInterval);
            }

            SetMovingAnim(true);
            HandleShooting(target);
            return;
        }

        // No strafe: hold position and shoot.
        StopAgent();
        SetMovingAnim(false);
        HandleShooting(target);
    }

    private bool TryGetCombatRangeBand(out float minRange, out float maxRange)
    {
        minRange = Mathf.Max(0.5f, desiredAttackRangeMin);
        maxRange = Mathf.Max(minRange, desiredAttackRangeMax);

        if (!enableDesiredAttackRangeBand) return false;
        if (maxRange <= minRange + 0.01f) return false;

        return true;
    }

    private Vector3 ClampToCombatRangeBandOnNavMesh(Vector3 desired, Vector3 targetPoint, float minRange, float maxRange, float sampleRadius)
    {
        // Adjust 'desired' so its horizontal distance to targetPoint stays within [minRange, maxRange],
        // then sample onto the NavMesh to avoid unreachable points.
        Vector3 flat = desired - targetPoint;
        flat.y = 0f;

        if (flat.sqrMagnitude < 0.0001f)
        {
            flat = (transform.position - targetPoint);
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                flat = transform.forward;
        }

        float mag = flat.magnitude;
        float clamped = Mathf.Clamp(mag, minRange, maxRange);

        Vector3 adjusted = targetPoint + flat.normalized * clamped;
        adjusted.y = desired.y;

        if (NavMesh.SamplePosition(adjusted, out NavMeshHit hit, Mathf.Max(0.25f, sampleRadius), NavMesh.AllAreas))
            adjusted = hit.position;

        return adjusted;
    }

    private void UpdateSpeedParam()
    {
        if (anim == null) return;
        if (agent == null) return;

        // If the animator has a Speed float (like AllyController), drive it from agent velocity.
        float speed = agent.velocity.magnitude;

        var ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type == AnimatorControllerParameterType.Float && ps[i].name == speedFloatParam)
            {
                anim.SetFloat(speedFloatParam, speed);
                return;
            }
        }
    }
    void HandleShooting(Transform target)
    {
        if (_isDead) return;
        if (bullet == null || firePoint == null || target == null) return;
        if (TargetIsDeadOrInvalid(target)) return;

        float dt = Time.deltaTime;

        // If we're mid-burst, keep firing until we finish.
        if (_burstRemaining > 0)
        {
            _burstShotTimer -= dt;
            if (_burstShotTimer > 0f)
                return;

            FireOnceAt(target);

            _burstRemaining--;

            if (_burstRemaining > 0)
            {
                // Seconds between bullets inside a burst.
                _burstShotTimer = Mathf.Max(0.02f, fireRate);
            }
            else
            {
                // Burst finished: apply cooldown + optional windup for next burst.
                shotWaitCounter = Mathf.Max(0f, waitBetweenShots);
                shootTimeCounter = Mathf.Max(0f, timeToShoot);
            }

            return;
        }

        // Between-burst cooldown
        if (shotWaitCounter > 0f)
        {
            shotWaitCounter -= dt;
            return;
        }

        // Optional windup before starting a burst
        shootTimeCounter -= dt;
        if (shootTimeCounter > 0f)
            return;

        // Start a new burst
        _burstRemaining = Mathf.Max(1, shotsPerBurst);
        _burstShotTimer = 0f;

        // Fire first bullet immediately
        FireOnceAt(target);
        _burstRemaining--;

        if (_burstRemaining > 0)
            _burstShotTimer = Mathf.Max(0.02f, fireRate);
        else
        {
            shotWaitCounter = Mathf.Max(0f, waitBetweenShots);
            shootTimeCounter = Mathf.Max(0f, timeToShoot);
        }
    }

    private void FireOnceAt(Transform target)
    {
        if (bullet == null || firePoint == null || target == null) return;
        if (TargetIsDeadOrInvalid(target)) return;

        // Aim
        Vector3 aimPos = GetAimPosition(target);
        firePoint.LookAt(aimPos);

        // Aim gate: allow firing when our aim is reasonably aligned with the target.
        Vector3 aimFrom = firePoint.position;
        Vector3 aimFwd = (useFirePointForAimGate ? firePoint.forward : transform.forward);
        Vector3 toAim = aimPos - aimFrom;

        // Ignore vertical angle for gating (helps on slopes / uneven ground)
        toAim.y = 0f;

        float aimAngle = 0f;
        if (toAim.sqrMagnitude > 0.0001f)
            aimAngle = Vector3.Angle(aimFwd, toAim.normalized);

        bool canFire = !useFireConeGate || aimAngle <= Mathf.Clamp(fireConeDegrees, 1f, 179f);

        if (!canFire) return;

        Instantiate(bullet, firePoint.position, firePoint.rotation);
        if (debugMuzzleFlash) Debug.Log($"[Enemy2Controller] Shot fired -> triggering muzzle flash on {name}", this);
        TriggerMuzzleFlashSimple();
        if (anim != null) anim.SetTrigger("fireShot");
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

        // We manage standoff ourselves; keep stoppingDistance small so the agent can still travel to strafe points.
        float combatStop = Mathf.Clamp(Mathf.Max(0.5f, range) * 0.25f, 0.1f, 1.25f);
        if (!Mathf.Approximately(agent.stoppingDistance, combatStop))
            agent.stoppingDistance = combatStop;

        agent.SetDestination(pos);
    }

    private void SetMovingAnim(bool moving)
    {
        if (anim == null) return;

        // Prefer AllyController-style bool param if present.
        var ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type == AnimatorControllerParameterType.Bool && ps[i].name == runBoolParam)
            {
                anim.SetBool(runBoolParam, moving);
                return;
            }
        }

        // Fallback: legacy enemy param.
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type == AnimatorControllerParameterType.Bool && ps[i].name == legacyMoveBoolParam)
            {
                anim.SetBool(legacyMoveBoolParam, moving);
                return;
            }
        }
    }


    private void SetShootingAnim(bool shooting)
    {
        if (anim == null) return;

        // Preferred: bool parameter "fireShot"
        bool hasFireBool = false;
        bool hasShootTrigger = false;
        var ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == "fireShot" && ps[i].type == AnimatorControllerParameterType.Bool)
                hasFireBool = true;
            if ((ps[i].name == "Shoot" || ps[i].name == "fireShot") && ps[i].type == AnimatorControllerParameterType.Trigger)
                hasShootTrigger = true;
        }

        if (hasFireBool)
        {
            anim.SetBool("fireShot", shooting);
        }
        else if (shooting && hasShootTrigger)
        {
            // If your controller uses a trigger instead of a bool
            if (HasParamTrigger(anim, "Shoot")) anim.SetTrigger("Shoot");
            else if (HasParamTrigger(anim, "fireShot")) anim.SetTrigger("fireShot");
        }
    }

    private bool HasParamTrigger(Animator animator, string paramName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(paramName)) return false;
        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == paramName && ps[i].type == AnimatorControllerParameterType.Trigger) return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        // Helpful debug rings
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, allyAggroRadius);
    }
    // -------------------- DEATH --------------------
    private void HandleDeathControllerDied()
    {
        // DeathController was triggered (usually by EnemyHealthController). Make sure this AI cannot keep acting.
        _isDead = true;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        // Clear animator params that can force transitions back to shoot/run.
        if (anim != null)
        {
            if (HasParamBool(anim, "isMoving")) anim.SetBool("isMoving", false);
            if (HasParamBool(anim, "fireShot")) anim.SetBool("fireShot", false);
        }

        if (disableThisAIOnDeath)
            enabled = false;
    }


    /// <summary>
    /// Call this from your health script when HP reaches 0.
    /// Plays the Animator death trigger via DeathController (preferred) or directly on Animator (fallback).
    /// Also disables NavMeshAgent so the enemy stops moving/shooting.
    /// </summary>
    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        // Stop movement immediately.
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        // Use the shared pipeline if present.
        if (deathController != null)
        {
            deathController.Die();
        }
        else
        {
            // Fallback: just trigger the animator.
            if (anim != null && !string.IsNullOrWhiteSpace(deathTriggerName))
                anim.SetTrigger(deathTriggerName);
        }

        // Prevent any further AI updates.
        if (disableThisAIOnDeath)
            enabled = false;
    }

    /// <summary>Alias for callers that already have a "Kill" style method name.</summary>
    public void OnKilled() => Die();




    private Vector3 GetAimPosition(Transform target)
    {
        if (target == null) return transform.position + transform.forward * 10f;

        // 1) AimPoint child (best)
        if (!string.IsNullOrWhiteSpace(aimPointChildName))
        {
            var ap = target.Find(aimPointChildName);
            if (ap != null) return ap.position;
        }

        // 2) Collider bounds center (good fallback)
        if (useColliderBoundsForAim)
        {
            Collider best = null;
            var cols = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                if (cols[i].isTrigger) continue;
                best = cols[i];
                break;
            }

            if (best != null) return best.bounds.center;
        }

        // 3) Fallback offset from pivot (feet)
        return target.position + Vector3.up * Mathf.Max(0f, aimHeightOffset);
    }



    private static bool HasParamBool(Animator animator, string paramName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(paramName)) return false;
        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == paramName && ps[i].type == AnimatorControllerParameterType.Bool)
                return true;
        }
        return false;
    }



    void ClearCombatTarget()
    {
        // Unsubscribe from old target events
        if (_subscribedAllyHealth != null)
        {
            _subscribedAllyHealth.OnDied -= OnCombatTargetDied;
            _subscribedAllyHealth = null;
        }

        _combatTarget = null;
        chasing = false;
        _combatLoseCounter = 0f;
        chaseCounter = 0f;

        // Stop shooting immediately
        shootTimeCounter = 0f;
        shotWaitCounter = 0f;
        fireCount = 0f;

        StopAgent();
        SetMovingAnim(false);

        // If your animator uses fireShot bool, force it off
        SetShootingAnim(false);
    }

    void OnCombatTargetDied()
    {
        ClearCombatTarget();
    }

    private bool TargetIsDeadOrInvalid(Transform t)
    {
        if (t == null) return true;

        // If the target object is disabled, treat as invalid.
        if (!t.gameObject.activeInHierarchy) return true;

        // If the target has a DeathController and is dead, invalid.
        var dc = t.GetComponentInParent<MnR.DeathController>();
        if (dc != null && dc.IsDead) return true;

        // If the target is an ally with AllyHealth and is dead, invalid.
        var ah = t.GetComponentInParent<AllyHealth>();
        if (ah != null && ah.IsDead) return true;

        return false;
    }


    private void TriggerMuzzleFlashSimple()
    {
        if (muzzleFlashObject == null) return;

        if (muzzleFlashFollowFirePoint && firePoint != null)
        {
            muzzleFlashObject.transform.position = firePoint.TransformPoint(muzzleFlashLocalOffset);
            muzzleFlashObject.transform.rotation = firePoint.rotation;
        }

        if (muzzleFlashObject.activeSelf) muzzleFlashObject.SetActive(false);
        if (debugMuzzleFlash) Debug.Log($"[Enemy2Controller] MuzzleFlash OFF: {muzzleFlashObject.name}", muzzleFlashObject);

        muzzleFlashObject.SetActive(true);
        if (debugMuzzleFlash) Debug.Log($"[Enemy2Controller] MuzzleFlash ON: {muzzleFlashObject.name}", muzzleFlashObject);

        if (_muzzleFlashRoutine != null)
            StopCoroutine(_muzzleFlashRoutine);

        _muzzleFlashRoutine = StartCoroutine(DisableMuzzleFlashAfterDelay());
    }

    private IEnumerator DisableMuzzleFlashAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.005f, muzzleFlashDuration));
        if (muzzleFlashObject != null)
            muzzleFlashObject.SetActive(false);
        if (debugMuzzleFlash) Debug.Log($"[Enemy2Controller] MuzzleFlash OFF: {muzzleFlashObject.name}", muzzleFlashObject);
        _muzzleFlashRoutine = null;
    }

    // Optional: call this from an Animation Event on the fire frame
    public void AnimEvent_MuzzleFlash()
    {
        TriggerMuzzleFlashSimple();
    }
}