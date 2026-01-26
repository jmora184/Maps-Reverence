// 1/4/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public class AllyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    [Tooltip("How quickly the ally turns to face its movement direction when not in combat.")]
    public float moveTurnSpeed = 12f;
    [Tooltip("If target is set (e.g., joining a team), we will keep updating destination to follow it.")]
    public float travelFollowUpdateInterval = 0.15f;
    public Rigidbody theRB;
    public Transform target; // (used for dynamic travel follow, e.g., join in-route)
    private bool chasing;

    /// <summary>True while this ally is actively chasing/fighting an enemy.</summary>
    public bool IsChasing => chasing;

    private float travelFollowTimer;
    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;
    public NavMeshAgent agent;

    [Header("Combat")]
    public GameObject bullet;
    public Transform firePoint;
    public float fireRate = 0.5f;
    private float fireCount;

    [Header("Combat Range")]
    [Tooltip("Preferred distance to keep from the enemy while fighting.")]
    public float desiredAttackRange = 6f;

    [Tooltip("Small buffer around desiredAttackRange to prevent jitter (hysteresis).")]
    public float attackRangeBuffer = 0.75f;

    [Header("Dynamic Desired Range")]
    [Tooltip("If true, the ally will fluctuate its desired attack range between Min and Max while in combat.")]
    public bool fluctuateDesiredAttackRange = false;

    [Tooltip("Minimum desired attack range when fluctuation is enabled.")]
    public float desiredAttackRangeMin = 30f;

    [Tooltip("Maximum desired attack range when fluctuation is enabled.")]
    public float desiredAttackRangeMax = 50f;

    [Tooltip("How often (seconds) we pick a new desired range target while in combat.")]
    public float desiredAttackRangeRetargetInterval = 2.5f;

    [Tooltip("How quickly the desired range blends toward the chosen target.")]
    public float desiredAttackRangeLerpSpeed = 4f;

    // Runtime state for fluctuating range
    private float _desiredRangeCurrent = 0f;
    private float _desiredRangeTarget = 0f;
    private float _desiredRangeRetargetTimer = 0f;


    [Tooltip("NavMesh sample radius used when backing away from a target.")]
    public float backoffSampleRadius = 2.5f;

    [Tooltip("How quickly the unit turns to face the target while fighting.")]
    public float faceTargetTurnSpeed = 12f;

    [Header("Combat Dodge / Strafing")]
    [Tooltip("If true, the unit will strafe/circle while shooting instead of standing still at ideal range.")]
    public bool enableCombatStrafe = true;

    [Tooltip("How often (seconds) we pick a new strafe point around the target.")]
    public float strafeRepathInterval = 0.35f;

    [Tooltip("Seconds between changing strafe direction/angle.")]
    public float strafeChangeInterval = 1.25f;

    [Tooltip("Max random angle jitter added to the strafe direction (degrees).")]
    public float strafeAngleJitter = 25f;

    [Tooltip("NavMesh sample radius used when picking a strafe point.")]
    public float strafeSampleRadius = 2.5f;

    [Tooltip("Distance (meters) moved during each short combat burst (diagonal strafe/back-forward).")]
    public float combatBurstMoveDistance = 2.0f;

    [Tooltip("How much the burst includes moving toward/away from the target (0 = purely sideways).")]
    public float combatBurstRadialWeight = 0.65f;

    [Tooltip("How much the burst includes moving sideways around the target (0 = no strafing).")]
    public float combatBurstTangentialWeight = 1.0f;

    [Header("Formation Hold (Optional)")]
    [Tooltip("If true, and this ally is currently following a formation slot (target != null), combat movement is clamped so the ally stays near its slot instead of breaking formation.")]
    public bool holdFormationWhenFollowing = true;

    [Tooltip("Max distance (meters) the ally may drift away from its formation slot during combat.")]
    public float formationCombatTetherRadius = 2.5f;

    [Tooltip("If true, tether radius scales slightly with team size (helps large squads keep a cohesive line).")]
    public bool scaleTetherWithTeamSize = true;

    [Tooltip("Extra meters added to tether when scaling with team size. Effective tether = base + extra * sqrt(teamSize-1).")]
    public float tetherExtraPerSqrtMember = 0.35f;



    [Header("Manual Move Hold (Player Move Orders)")]
    [Tooltip("If true, a player-issued Move order creates a 'hold zone'. While holding, the ally will not chase enemies outside manualChaseLeashRadius from the hold point.")]
    public bool enableManualHoldZone = true;

    [Tooltip("Radius around the move destination that counts as the ally's 'hold area'. Used mainly for editor tuning / future UI. Combat leash uses manualChaseLeashRadius.")]
    public float manualHoldRadius = 6f;

    [Tooltip("Max distance from the hold point this ally is allowed to chase during combat (prevents chasing 50+ units away after a Move order).")]
    public float manualChaseLeashRadius = 12f;

    [Tooltip("If false, allies will NOT auto-acquire enemies while holding (they'll only fight if explicitly attacked/ordered).")]
    public bool manualHoldAllowsAutoAggro = true;

    [Header("Team Focus Fire")]
    [Tooltip("If true, allies in a team will sometimes prefer the same target as a nearby teammate (reduces split fire).")]
    public bool enableTeamFocusFire = true;

    [Tooltip("Max distance from this ally to a teammate's target for focus-fire to kick in.")]
    public float focusFireMaxTargetDistance = 12f;

    [Tooltip("Seconds between allowed focus-fire target switches (prevents oscillation).")]
    public float focusFireCooldown = 1.0f;

    [Header("Combat Engagement Pause")]
    [Tooltip("When the ally is in its desired range, it will pause (stand ground) for a bit while shooting instead of constantly strafing.")]
    public bool pauseWhileShootingInRange = true;

    [Tooltip("Min seconds to stand still while shooting (in-range).")]
    public float pauseShootMinSeconds = 2f;

    [Tooltip("Max seconds to stand still while shooting (in-range).")]
    public float pauseShootMaxSeconds = 3f;

    [Tooltip("After a pause, allow a short burst of strafing movement before pausing again (keeps combat from looking too robotic).")]
    public float pauseMoveBurstSeconds = 0.5f;

    [Header("Animation")]
    public Animator soldierAnimator;

    [Header("Animation Params")]
    [Tooltip("Bool parameter used to drive Idle/Run transitions (optional). Set to match your Animator Controller.")]
    public string runBoolParam = "isRunning";

    [Tooltip("Float parameter used for blend trees (optional). Set to match your Animator Controller.")]
    public string speedFloatParam = "Speed";

    // Animator parameter caching (avoids spamming errors if a param doesn't exist)
    private bool _animHasIsRunning;
    private int _animIsRunningHash;
    private bool _animHasSpeed;
    private int _animSpeedHash;


    [Header("Move Marker Auto-Clear")]
    [Tooltip("When close enough to the pinned destination, auto-clear the move marker for this ally.")]
    public bool autoClearMoveMarkerOnArrival = true;

    [Tooltip("Extra distance beyond stoppingDistance to consider 'arrived' for marker clearing.")]
    public float markerArrivalBuffer = 0.15f;

    [Tooltip("If true, allies that are currently in a team will use a larger arrival buffer (helps prevent stacking when moving a newly formed team).")]
    public bool useTeamArrivalBufferMultiplier = true;

    [Tooltip("Multiplier applied to markerArrivalBuffer when this ally is in a team. Set to 2 to double the buffer area.")]
    public float teamArrivalBufferMultiplier = 2f;

    // Per-ally stats (team size -> multipliers)
    private AllyCombatStats combatStats;

    // Pinned state (v1 driven by 'chasing')
    private AllyPinnedStatus pinnedStatus;

    // Track one enemy target so we don't fight every enemy in the scene at once
    private Transform currentEnemy;
    private bool forcedCombatOrder = false; // true when combat target was set by an explicit player attack order


    /// <summary>The enemy this ally is currently chasing (null when idle).</summary>
    public Transform CurrentEnemy => currentEnemy;

    // Manual hold runtime
    private bool _hasManualHold;
    private Vector3 _manualHoldPoint;

    // Focus fire runtime
    private float _nextFocusFireTime;

    // Combat pause runtime
    private float _combatPauseTimer;
    private float _combatMoveBurstTimer;


    // Combat strafe runtime
    private float _strafeRepathTimer;
    private float _strafeChangeTimer;
    private int _strafeSide = 1; // 1 = right, -1 = left
    private float _strafeAngleOffset;
    // Remember where we were going before we got pulled into combat
    // NOTE: for TEAM moves, the "current" destination can change while we are fighting.
    // So we prefer to resume using the latest pinned destination from MoveDestinationMarkerSystem when available.
    private Vector3 resumeDestination;
    private bool hasResumeDestination;
    private bool resumeWasFollowing;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (theRB == null) theRB = GetComponent<Rigidbody>();
        if (soldierAnimator == null) soldierAnimator = GetComponentInChildren<Animator>();

        // Root motion can "pin" the character in place (idle) even when a NavMeshAgent is trying to move it.
        // Disable it so the agent fully controls translation.
        if (soldierAnimator != null)
            soldierAnimator.applyRootMotion = false;

        // We'll handle rotation ourselves:
        // - normal travel: face movement direction
        // - combat: face the target smoothly
        if (agent != null)
            agent.updateRotation = false;

        CacheAnimatorParams();

        combatStats = GetComponent<AllyCombatStats>();
        pinnedStatus = GetComponent<AllyPinnedStatus>();

        // De-sync strafing so squads don't move in unison when they start attacking on the same frame.
        _strafeRepathTimer = Random.Range(0f, Mathf.Max(0.05f, strafeRepathInterval));
        _strafeChangeTimer = Random.Range(0f, Mathf.Max(0.1f, strafeChangeInterval));
        _strafeSide = (Random.value < 0.5f) ? -1 : 1;
        _strafeAngleOffset = Random.Range(-strafeAngleJitter, strafeAngleJitter);
    }

    private void CacheAnimatorParams()
    {
        _animHasIsRunning = false;
        _animHasSpeed = false;

        if (soldierAnimator == null) return;

        _animIsRunningHash = Animator.StringToHash(string.IsNullOrEmpty(runBoolParam) ? "isRunning" : runBoolParam);
        _animSpeedHash = Animator.StringToHash(string.IsNullOrEmpty(speedFloatParam) ? "Speed" : speedFloatParam);

        // Cache whether these parameters exist so we don't log errors every frame.
        var ps = soldierAnimator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (!_animHasIsRunning && p.type == AnimatorControllerParameterType.Bool && p.name == (string.IsNullOrEmpty(runBoolParam) ? "isRunning" : runBoolParam))
                _animHasIsRunning = true;
            if (!_animHasSpeed && p.type == AnimatorControllerParameterType.Float && p.name == (string.IsNullOrEmpty(speedFloatParam) ? "Speed" : speedFloatParam))
                _animHasSpeed = true;
        }
    }

    private void Start()
    {
        // Initialize agent speed.
        // IMPORTANT: don't accidentally override a NavMeshAgent speed you tuned in the Inspector.
        // If moveSpeed is 0, we treat the agent's current speed as the baseline.
        if (agent != null)
        {
            if (combatStats != null)
            {
                // Prefer explicit moveSpeed if you set it, otherwise keep the current agent.speed.
                float baseline = agent.speed;
                if (moveSpeed > 0f) baseline = moveSpeed;

                if (combatStats.baseMoveSpeed <= 0f)
                    combatStats.baseMoveSpeed = baseline;

                combatStats.ApplyToAgent(agent);
            }
            else if (moveSpeed > 0f)
            {
                agent.speed = moveSpeed;
            }
        }
    }

    // -------------------- MANUAL HOLD API --------------------
    public void SetManualHoldPoint(Vector3 point)
    {
        _hasManualHold = true;
        _manualHoldPoint = point;
    }

    public void ClearManualHoldPoint()
    {
        _hasManualHold = false;
    }

    public bool HasManualHoldPoint => _hasManualHold;
    public Vector3 ManualHoldPoint => _manualHoldPoint;
    // -------------------- COMBAT STATE (read-only) --------------------
    // Exposed so CommandExecutor can avoid overriding movement while this unit is actively fighting,
    // and to allow "attack" orders to force a specific combat target.
    public Transform CurrentEnemyTarget => currentEnemy;

    /// <summary>
    /// Force this ally to engage a specific enemy target immediately (used by Attack/Follow orders).
    /// Clears follow-slot targeting so combat movement (standoff/strafe) is fully controlled by this script.
    /// </summary>
    public void ForceCombatTarget(Transform enemy)
    {
        if (enemy == null) return;

        // Mark this as an explicit player attack order so we ignore formation/slot tethering while fighting.
        forcedCombatOrder = true;

        // Best-effort: clear any formation slot / follow target, but follow systems may re-assign it.
        target = null;
        // Attack orders should not resume old destinations after combat.
        resumeWasFollowing = false;
        hasResumeDestination = false;

        chasing = true;
        currentEnemy = enemy;

        // Reset combat pause cycle when we start/force a new engagement.
        _combatPauseTimer = 0f;
        _combatMoveBurstTimer = 0f;
    }

    private void Update()
    {
        // Rotation:
        // If we let the agent rotate while we also rotate to face targets in combat,
        // the character can end up "running backwards" or snapping.
        // So we always rotate manually.
        if (agent != null)
        {
            agent.updateRotation = false;

            if (!chasing)
            {
                Vector3 vel = agent.desiredVelocity;
                vel.y = 0f;
                if (vel.sqrMagnitude > 0.001f)
                {
                    Quaternion look = Quaternion.LookRotation(vel.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * moveTurnSpeed);
                }
            }
        }

        // Keep agent speed in sync with team size (only when it changes).
        // (Prevents unexpected slowdowns if you tune speed in the Inspector.)
        if (agent != null && combatStats != null)
        {
            float desired = combatStats.GetMoveSpeed();
            if (!Mathf.Approximately(agent.speed, desired))
                agent.speed = desired;
        }

        // If we were chasing but the enemy was destroyed, stop chasing and resume.
        if (chasing && currentEnemy == null)
        {
            StopChasingAndResume();
        }

        // Acquire / validate target
        if (!chasing)
        {
            TryAcquireEnemy();
        }
        else
        {
            // If enemy ran far enough away, stop chasing and resume.
            if (currentEnemy == null)
            {
                StopChasingAndResume();
            }
            else
            {
                float dist = Vector3.Distance(transform.position, currentEnemy.position);

                // If we're in a manual hold zone (player-issued Move), never chase outside the leash.
                if (enableManualHoldZone && _hasManualHold)
                {
                    float leash = Mathf.Max(0.1f, manualChaseLeashRadius);
                    float dSelf = Vector3.Distance(transform.position, _manualHoldPoint);
                    float dEnemy = Vector3.Distance(currentEnemy.position, _manualHoldPoint);

                    if (dSelf > leash || dEnemy > leash)
                    {
                        StopChasingAndResume();
                        return;
                    }
                }

                if (dist > distanceToLose)
                {
                    StopChasingAndResume();
                }
                else
                {
                    ChaseAndShoot(currentEnemy);
                }
            }
        }

        // Follow travel target when not chasing (e.g., join in-route following a moving team)
        if (!chasing && target != null && agent != null)
        {
            travelFollowTimer -= Time.deltaTime;
            if (travelFollowTimer <= 0f)
            {
                agent.SetDestination(target.position);
                travelFollowTimer = Mathf.Max(0.05f, travelFollowUpdateInterval);
            }
        }

        // Face movement direction while traveling (prevents "running backwards" when agent rotation is off).
        if (!chasing && agent != null)
        {
            Vector3 v = agent.desiredVelocity;
            v.y = 0f;
            if (v.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(v.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * moveTurnSpeed);
            }
        }

        // Running animation
        // IMPORTANT:
        // During combat strafing we keep stoppingDistance ~= desiredAttackRange (ex: 6m).
        // That means agent.remainingDistance is often <= stoppingDistance even while the agent is moving,
        // which made "isRunning" stay false and the ally looked Idle while strafing.
        // Drive running from actual movement (velocity) instead.
        if (soldierAnimator != null && agent != null)
        {
            // Use actual movement to drive locomotion animation (works for combat strafing)
            float speed = 0f;
            if (!agent.isStopped)
            {
                // desiredVelocity leads during acceleration; velocity is actual.
                speed = Mathf.Max(agent.desiredVelocity.magnitude, agent.velocity.magnitude);
            }

            bool isMoving = speed > 0.05f;

            if (_animHasIsRunning)
                soldierAnimator.SetBool(_animIsRunningHash, isMoving);

            // If your controller uses a Speed float blend-tree, support that too.
            if (_animHasSpeed)
                soldierAnimator.SetFloat(_animSpeedHash, speed);
        }


        // Auto-clear the pinned move marker when we arrive at our pinned destination (fixed-point moves).
        // This avoids leaving move pins behind after the ally finishes moving.
        if (autoClearMoveMarkerOnArrival && !chasing && target == null && agent != null && !agent.pathPending)
        {
            // Only clear when we actually have a pinned destination for this unit.
            if (TryGetLatestPinnedDestination(out Vector3 pinnedDest))
            {
                float buffer = Mathf.Max(0f, markerArrivalBuffer);
                if (useTeamArrivalBufferMultiplier && TeamManager.Instance != null && TeamManager.Instance.GetTeamOf(this.transform) != null)
                {
                    buffer *= Mathf.Max(0.1f, teamArrivalBufferMultiplier);
                }
                float arriveDist = Mathf.Max(agent.stoppingDistance, 0.05f) + buffer;
                float d = Vector3.Distance(transform.position, pinnedDest);

                // We also require the agent to have effectively stopped to avoid clearing too early.
                bool stopped = agent.velocity.sqrMagnitude < 0.01f && agent.desiredVelocity.sqrMagnitude < 0.01f;
                if (d <= arriveDist && stopped)
                {
                    TryClearPinnedMoveMarker();
                }
            }
        }


        // Update pinned state (v1: pinned while chasing).
        if (pinnedStatus != null)
            pinnedStatus.SetPinned(chasing);
    }

    private void TryAcquireEnemy()
    {
        // If we're holding a player-issued move position and auto-aggro is disabled, do nothing.
        if (enableManualHoldZone && _hasManualHold && !manualHoldAllowsAutoAggro)
            return;

        Transform best = null;
        float bestDist = float.MaxValue;

        // 1) Team focus-fire: if a nearby teammate is already chasing something, prefer that target (with cooldown).
        if (enableTeamFocusFire && Time.time >= _nextFocusFireTime && TeamManager.Instance != null)
        {
            Team team = TeamManager.Instance.GetTeamOf(this.transform);
            if (team != null && team.Members != null && team.Members.Count > 1)
            {
                float maxTargetDist = Mathf.Max(0.1f, focusFireMaxTargetDistance);

                for (int i = 0; i < team.Members.Count; i++)
                {
                    Transform m = team.Members[i];
                    if (m == null || m == this.transform) continue;

                    AllyController other = m.GetComponent<AllyController>();
                    if (other == null || !other.IsChasing) continue;

                    Transform enemy = other.CurrentEnemy;
                    if (enemy == null) continue;

                    float d = Vector3.Distance(transform.position, enemy.position);
                    if (d > distanceToChase) continue;
                    if (d > maxTargetDist) continue;
                    if (d >= bestDist) continue;

                    // Manual-hold leash filter (don't focus-fire something outside our hold zone leash).
                    if (enableManualHoldZone && _hasManualHold)
                    {
                        float leash = Mathf.Max(0.1f, manualChaseLeashRadius);
                        float dEnemyToHold = Vector3.Distance(enemy.position, _manualHoldPoint);
                        if (dEnemyToHold > leash) continue;
                    }

                    bestDist = d;
                    best = enemy;
                }

                if (best != null)
                    _nextFocusFireTime = Time.time + Mathf.Max(0f, focusFireCooldown);
            }
        }

        // 2) Normal nearest-enemy search (fallback)
        if (best == null)
        {
            GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

            for (int i = 0; i < enemies.Length; i++)
            {
                var go = enemies[i];
                if (go == null) continue;

                Transform t = go.transform;
                float d = Vector3.Distance(transform.position, t.position);
                if (d >= distanceToChase) continue;
                if (d >= bestDist) continue;

                if (enableManualHoldZone && _hasManualHold)
                {
                    float leash = Mathf.Max(0.1f, manualChaseLeashRadius);
                    float dEnemyToHold = Vector3.Distance(t.position, _manualHoldPoint);
                    if (dEnemyToHold > leash) continue;
                }

                bestDist = d;
                best = t;
            }
        }

        if (best != null)
        {
            // entering chase: remember where we were going before combat
            resumeWasFollowing = (target != null);
            if (agent != null)
            {
                resumeDestination = agent.destination;
                hasResumeDestination = true;
            }

            chasing = true;
            currentEnemy = best;

            // Reset combat pause cycle when we start a new engagement.
            _combatPauseTimer = 0f;
            _combatMoveBurstTimer = 0f;
        }
    }


    private void StopChasingAndResume()
    {
        chasing = false;
        currentEnemy = null;

        forcedCombatOrder = false;

        // Reset dynamic range for next engagement
        _desiredRangeCurrent = 0f;
        _desiredRangeTarget = 0f;
        _desiredRangeRetargetTimer = 0f;

        // If we were following a dynamic target (e.g., joining a moving team),
        // don't restore a stale point destination; the follow logic below will resume naturally.
        if (resumeWasFollowing && target != null)
        {
            hasResumeDestination = false;
            return;
        }

        if (agent == null) { hasResumeDestination = false; return; }

        // If we have a manual hold point (player Move order), always return to it after combat.
        if (enableManualHoldZone && _hasManualHold && target == null)
        {
            agent.isStopped = false;
            agent.SetDestination(_manualHoldPoint);
            hasResumeDestination = false;
            return;
        }

        // ✅ KEY FIX:
        // If this unit has a pinned move destination (team/ally move pins), resume to the LATEST pinned destination.
        // This solves: "ally resumes to original team spot after combat even if team destination was updated".
        if (TryGetLatestPinnedDestination(out Vector3 pinnedDest))
        {
            agent.isStopped = false;
            agent.SetDestination(pinnedDest);
            hasResumeDestination = false;
            return;
        }

        // Otherwise, resume the previously commanded destination (e.g., move-to-point).
        if (hasResumeDestination)
        {
            agent.isStopped = false;
            agent.SetDestination(resumeDestination);
            hasResumeDestination = false;
        }
    }

    private bool TryGetLatestPinnedDestination(out Vector3 dest)
    {
        dest = default;

        // If your marker system isn't present, nothing to do.
        if (MoveDestinationMarkerSystem.Instance == null) return false;

        var inst = MoveDestinationMarkerSystem.Instance;
        var type = inst.GetType();

        // 1) If a public method exists, prefer it (future-proof).
        // We try common names without requiring you to change other files.
        // bool TryGetPinnedDestination(Transform unit, out Vector3 destination)
        // bool TryGetDestinationFor(Transform unit, out Vector3 destination)
        try
        {
            MethodInfo mi =
                type.GetMethod("TryGetPinnedDestination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetMethod("TryGetDestinationFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetMethod("TryGetPinnedFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (mi != null)
            {
                object[] args = new object[] { this.transform, dest };
                // out param comes back in args[1]
                object okObj = mi.Invoke(inst, args);
                if (okObj is bool ok && ok)
                {
                    if (args[1] is Vector3 v)
                    {
                        dest = v;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore and fall back to reflection on fields
        }

        // 2) Fallback: reflect private Dictionary<Transform, Pinned> pinnedByUnit and read pinned.destination
        try
        {
            var field = type.GetField("pinnedByUnit", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return false;

            var dictObj = field.GetValue(inst);
            if (dictObj == null) return false;

            // Dictionary<TKey,TValue> implements non-generic IDictionary
            var dict = dictObj as IDictionary;
            if (dict == null) return false;

            if (!dict.Contains(this.transform)) return false;

            var pinned = dict[this.transform];
            if (pinned == null) return false;

            var pType = pinned.GetType();

            // destination is a field in your current MoveDestinationMarkerSystem.Pinned class
            var destField = pType.GetField("destination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (destField != null && destField.FieldType == typeof(Vector3))
            {
                dest = (Vector3)destField.GetValue(pinned);
                return true;
            }

            // or destination as a property
            var destProp = pType.GetProperty("destination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (destProp != null && destProp.PropertyType == typeof(Vector3))
            {
                dest = (Vector3)destProp.GetValue(pinned);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }


    private void TryClearPinnedMoveMarker()
    {
        if (MoveDestinationMarkerSystem.Instance == null) return;

        var inst = MoveDestinationMarkerSystem.Instance;
        var type = inst.GetType();

        // Try a few common method signatures without hard dependencies.
        // Preferred (your project has used this pattern):
        //   ClearForUnits(GameObject[] units)
        // Also supports:
        //   ClearForUnit(Transform unit)
        //   ClearForUnits(Transform[] units)
        //   ClearFor(Transform unit)
        //   Unpin(Transform unit)
        try
        {
            // 1) ClearForUnits(GameObject[])
            var m = type.GetMethod("ClearForUnits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1)
                {
                    if (p[0].ParameterType == typeof(GameObject[]))
                    {
                        m.Invoke(inst, new object[] { new[] { this.gameObject } });
                        return;
                    }
                    if (p[0].ParameterType == typeof(Transform[]))
                    {
                        m.Invoke(inst, new object[] { new[] { this.transform } });
                        return;
                    }
                }
            }

            // 2) ClearForUnit(Transform)
            m = type.GetMethod("ClearForUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }

            // 3) ClearFor(Transform)
            m = type.GetMethod("ClearFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }

            // 4) Unpin(Transform)
            m = type.GetMethod("Unpin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }
        }
        catch
        {
            // If reflection fails, do nothing. Marker will remain until cleared elsewhere.
        }
    }





    private float GetEffectiveFormationTether()
    {
        float tether = Mathf.Max(0f, formationCombatTetherRadius);
        if (!scaleTetherWithTeamSize || tether <= 0f) return tether;

        int teamSize = 0;

        // Use reflection to avoid hard-coupling to a specific Team API shape.
        try
        {
            if (TeamManager.Instance != null)
            {
                var tm = TeamManager.Instance;
                var tmType = tm.GetType();

                var getTeamOf = tmType.GetMethod("GetTeamOf", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getTeamOf != null)
                {
                    object teamObj = getTeamOf.Invoke(tm, new object[] { this.transform });
                    if (teamObj != null)
                    {
                        var tType = teamObj.GetType();

                        // Common: Members (List<Transform> or List<AllyController> etc.)
                        var membersField = tType.GetField("Members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (membersField != null)
                        {
                            if (membersField.GetValue(teamObj) is System.Collections.ICollection col)
                                teamSize = col.Count;
                        }
                        else
                        {
                            var membersProp = tType.GetProperty("Members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (membersProp != null)
                            {
                                var val = membersProp.GetValue(teamObj);
                                if (val is System.Collections.ICollection col2)
                                    teamSize = col2.Count;
                            }
                        }

                        // Fallback: "members" lowercase
                        if (teamSize == 0)
                        {
                            var membersField2 = tType.GetField("members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (membersField2 != null)
                            {
                                if (membersField2.GetValue(teamObj) is System.Collections.ICollection col3)
                                    teamSize = col3.Count;
                            }
                            else
                            {
                                var membersProp2 = tType.GetProperty("members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (membersProp2 != null)
                                {
                                    var val2 = membersProp2.GetValue(teamObj);
                                    if (val2 is System.Collections.ICollection col4)
                                        teamSize = col4.Count;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore; use base tether
        }

        if (teamSize > 1)
            tether += Mathf.Max(0f, tetherExtraPerSqrtMember) * Mathf.Sqrt(teamSize - 1);

        return tether;
    }

    private Vector3 ClampCombatMoveToFormation(Vector3 desired, Transform formationSlot)
    {
        if (!holdFormationWhenFollowing) return desired;
        if (formationSlot == null) return desired;

        float tether = GetEffectiveFormationTether();
        if (tether <= 0.001f) return formationSlot.position;

        Vector3 anchor = formationSlot.position;
        Vector3 off = desired - anchor;
        off.y = 0f;

        float mag = off.magnitude;
        if (mag <= tether) return desired;

        Vector3 clamped = anchor + off.normalized * tether;
        clamped.y = desired.y;
        return clamped;
    }
    private float GetDesiredAttackRangeNow()
    {
        // Default behavior (no fluctuation)
        float baseRange = Mathf.Max(0.5f, desiredAttackRange);

        if (!fluctuateDesiredAttackRange)
            return baseRange;

        // Clamp authoring values
        float minR = Mathf.Max(0.5f, desiredAttackRangeMin);
        float maxR = Mathf.Max(minR, desiredAttackRangeMax);

        // Lazy init on first combat tick
        if (_desiredRangeCurrent <= 0f)
        {
            _desiredRangeCurrent = Mathf.Clamp(baseRange, minR, maxR);
            _desiredRangeTarget = _desiredRangeCurrent;
            _desiredRangeRetargetTimer = Random.Range(0.15f, Mathf.Max(0.25f, desiredAttackRangeRetargetInterval));
        }

        // Retarget periodically
        _desiredRangeRetargetTimer -= Time.deltaTime;
        if (_desiredRangeRetargetTimer <= 0f)
        {
            _desiredRangeTarget = Random.Range(minR, maxR);
            // Add some jitter so a whole squad doesn't retarget on the exact same frame
            float jitter = Mathf.Clamp(desiredAttackRangeRetargetInterval * 0.35f, 0f, 1.25f);
            _desiredRangeRetargetTimer = Mathf.Max(0.25f, desiredAttackRangeRetargetInterval + Random.Range(-jitter, jitter));
        }

        float lerpSpeed = Mathf.Max(0.1f, desiredAttackRangeLerpSpeed);
        _desiredRangeCurrent = Mathf.Lerp(_desiredRangeCurrent, _desiredRangeTarget, Time.deltaTime * lerpSpeed);
        return Mathf.Clamp(_desiredRangeCurrent, minR, maxR);
    }

    void ChaseAndShoot(Transform enemy)
    {
        if (enemy == null) return;

        Vector3 targetPoint = enemy.position;

        // If we're following a formation slot (PlayerSquadFollowSystem sets AllyController.target to a slot transform),
        // keep combat movement tethered to that slot so we don't break formation.
        Transform formationSlot = (!forcedCombatOrder && target != null) ? target : null;

        // Maintain a stable standoff distance instead of running into the target's center.
        // NOTE: We manage standoff in code; do NOT set agent.stoppingDistance = desiredAttackRange.
        // If stoppingDistance is huge (ex: 24), the agent will think it's "already arrived" at most strafe points
        // and it won't move (and your run animation never triggers). Keep stoppingDistance small.
        if (agent != null)
        {
            float range = GetDesiredAttackRangeNow();
            // Let the agent actually travel to strafe points.
            // We still maintain standoff ourselves via range/buffer logic.
            float combatStop = Mathf.Max(0.1f, range * 0.25f);
            if (!Mathf.Approximately(agent.stoppingDistance, combatStop))
                agent.stoppingDistance = combatStop;

            // Face target (flat) while fighting.
            Vector3 flatDir = targetPoint - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * faceTargetTurnSpeed);
            }

            float dist = Vector3.Distance(transform.position, targetPoint);

            // Too far: move in toward a ring around the enemy (not the exact center).
            if (dist > range + attackRangeBuffer)
            {
                // Out of ideal range: reset pause cycle so we don't get stuck "paused" while needing to reposition.
                _combatPauseTimer = 0f;
                _combatMoveBurstTimer = 0f;

                agent.isStopped = false;
                Vector3 toward = (targetPoint - transform.position);
                toward.y = 0f;
                if (toward.sqrMagnitude < 0.001f) toward = transform.forward;

                Vector3 ringPoint = targetPoint - toward.normalized * range;


                // Spread approach points a bit per-ally so teams don't all stack on the same ring point.
                Vector3 right = Vector3.Cross(Vector3.up, toward.normalized);
                float spread = Mathf.Clamp(range * 0.15f, 1f, 6f);
                float seed = (GetInstanceID() * 0.1234f);
                float side = Mathf.Sin(seed) * spread;
                ringPoint += right * side;
                ringPoint = ClampCombatMoveToFormation(ringPoint, formationSlot);
                agent.SetDestination(ringPoint);
            }
            // Too close: back off a bit.
            else if (dist < range - attackRangeBuffer)
            {
                // Too close: reset pause cycle so we can immediately back off.
                _combatPauseTimer = 0f;
                _combatMoveBurstTimer = 0f;

                Vector3 away = (transform.position - targetPoint);
                away.y = 0f;
                away = away.sqrMagnitude < 0.001f ? transform.forward : away.normalized;

                float push = (range - dist) + 0.5f;
                Vector3 desired = transform.position + away * push;

                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, backoffSampleRadius, NavMesh.AllAreas))
                    desired = hit.position;

                desired = ClampCombatMoveToFormation(desired, formationSlot);
                agent.isStopped = false;
                agent.SetDestination(desired);
            }
            // In range: optionally strafe/circle while shooting (more dynamic combat).
            else
            {
                if (enableCombatStrafe)
                {
                    // NEW: Pause / stand-ground behavior to reduce constant running around.
                    if (pauseWhileShootingInRange)
                    {
                        // Initialize pause timer on first entry.
                        if (_combatPauseTimer <= 0f && _combatMoveBurstTimer <= 0f)
                            _combatPauseTimer = Random.Range(Mathf.Max(0f, pauseShootMinSeconds), Mathf.Max(pauseShootMinSeconds, pauseShootMaxSeconds));

                        // 1) Stand still and shoot for 2-3 seconds
                        if (_combatPauseTimer > 0f)
                        {
                            _combatPauseTimer -= Time.deltaTime;
                            agent.isStopped = true;
                            agent.ResetPath();
                        }
                        // 2) Then allow a short movement burst (strafe repath) before pausing again
                        else
                        {
                            if (_combatMoveBurstTimer <= 0f)
                            {
                                _combatMoveBurstTimer = Mathf.Max(0.05f, pauseMoveBurstSeconds);
                                _strafeRepathTimer = 0f; // force a new strafe point immediately
                            }

                            _combatMoveBurstTimer -= Time.deltaTime;

                            // Normal strafe timers
                            _strafeRepathTimer -= Time.deltaTime;
                            _strafeChangeTimer -= Time.deltaTime;

                            if (_strafeChangeTimer <= 0f)
                            {
                                // Occasionally flip sides so it doesn't look too robotic.
                                if (Random.value < 0.35f) _strafeSide *= -1;
                                _strafeAngleOffset = Random.Range(-strafeAngleJitter, strafeAngleJitter);
                                _strafeChangeTimer = Mathf.Max(0.1f, strafeChangeInterval);
                            }

                            if (_strafeRepathTimer <= 0f)
                            {
                                Vector3 toTarget = (targetPoint - transform.position);
                                toTarget.y = 0f;
                                if (toTarget.sqrMagnitude < 0.001f)
                                    toTarget = transform.forward;

                                Vector3 forwardToTarget = toTarget.normalized;

                                // Tangential (left/right) component around the target
                                Quaternion rot = Quaternion.AngleAxis((_strafeSide * 90f) + _strafeAngleOffset, Vector3.up);
                                Vector3 tangent = rot * forwardToTarget;

                                // Radial (toward/away) component to create diagonal motion ("left/right" + "up/down")
                                float radialSign = Random.Range(-1f, 1f); // negative = away, positive = toward
                                Vector3 moveDir = (tangent * Mathf.Max(0f, combatBurstTangentialWeight)) +
                                                 (forwardToTarget * radialSign * Mathf.Max(0f, combatBurstRadialWeight));

                                if (moveDir.sqrMagnitude < 0.0001f)
                                    moveDir = tangent;

                                moveDir.Normalize();

                                float step = Mathf.Max(0.25f, combatBurstMoveDistance);
                                Vector3 desired = transform.position + moveDir * step;

                                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, strafeSampleRadius, NavMesh.AllAreas))
                                    desired = hit.position;

                                desired = ClampCombatMoveToFormation(desired, formationSlot);
                                agent.isStopped = false;
                                agent.SetDestination(desired);
                                _strafeRepathTimer = Mathf.Max(0.05f, strafeRepathInterval);
                            }

                            // End burst → start a new pause cycle
                            if (_combatMoveBurstTimer <= 0f)
                            {
                                _combatMoveBurstTimer = 0f;
                                _combatPauseTimer = Random.Range(Mathf.Max(0f, pauseShootMinSeconds), Mathf.Max(pauseShootMinSeconds, pauseShootMaxSeconds));
                                agent.isStopped = true;
                                agent.ResetPath();
                            }
                        }
                    }
                    else
                    {
                        // Original continuous strafe behavior
                        _strafeRepathTimer -= Time.deltaTime;
                        _strafeChangeTimer -= Time.deltaTime;

                        if (_strafeChangeTimer <= 0f)
                        {
                            // Occasionally flip sides so it doesn't look too robotic.
                            if (Random.value < 0.35f) _strafeSide *= -1;
                            _strafeAngleOffset = Random.Range(-strafeAngleJitter, strafeAngleJitter);
                            _strafeChangeTimer = Mathf.Max(0.1f, strafeChangeInterval);
                        }

                        if (_strafeRepathTimer <= 0f)
                        {
                            Vector3 fromTarget = (transform.position - targetPoint);
                            fromTarget.y = 0f;
                            if (fromTarget.sqrMagnitude < 0.001f)
                                fromTarget = transform.forward;

                            // Tangent around the target (left/right) plus small random angle jitter.
                            Quaternion rot = Quaternion.AngleAxis((_strafeSide * 90f) + _strafeAngleOffset, Vector3.up);
                            Vector3 strafeDir = rot * fromTarget.normalized;

                            Vector3 desired = targetPoint + strafeDir.normalized * range;
                            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, strafeSampleRadius, NavMesh.AllAreas))
                                desired = hit.position;

                            desired = ClampCombatMoveToFormation(desired, formationSlot);
                            agent.isStopped = false;
                            agent.SetDestination(desired);
                            _strafeRepathTimer = Mathf.Max(0.05f, strafeRepathInterval);
                        }
                    }
                }
                else
                {
                    // No strafe: stand still at ideal range.
                    _combatPauseTimer = 0f;
                    _combatMoveBurstTimer = 0f;

                    agent.isStopped = true;
                    agent.ResetPath();
                }
            }
        }

        // Fire
        fireCount -= Time.deltaTime;
        if (fireCount > 0) return;

        fireCount = fireRate;

        if (firePoint != null)
            firePoint.LookAt(targetPoint + new Vector3(0f, 0.5f, 0f));

        Vector3 targetDir = targetPoint - transform.position;
        float angle = Vector3.SignedAngle(targetDir, transform.forward, Vector3.up);

        if (Mathf.Abs(angle) < 30f)
        {
            if (bullet != null && firePoint != null)
            {
                GameObject spawned = Instantiate(bullet, firePoint.position, firePoint.rotation);

                // Set bullet damage from AllyCombatStats (team size scaling).
                if (combatStats != null)
                {
                    BulletController bc = spawned.GetComponent<BulletController>();
                    if (bc != null)
                        bc.Damage = combatStats.GetDamageInt();
                }
            }

            if (soldierAnimator != null)
                soldierAnimator.SetTrigger("Shoot");
        }
    }
}
