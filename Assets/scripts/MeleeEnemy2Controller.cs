using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Melee-only enemy controller (walk/run + melee + take_damage) with optional patrol integration.
///
/// Key Animator hookup:
/// - Float:   Speed        (Idle -> Walk can use Speed > 0.1)
/// - Bool:    isWalking    (optional)
/// - Bool:    isRunning    (optional)
/// - Trigger: attack
/// - Trigger: take_damage
/// - Trigger: Die          (optional) OR Bool: isDead
///
/// IMPORTANT:
/// This controller does NOT manage HP. Your health script should call:
///   - TakeDamage(...) for hit-reaction
///   - Die() when HP reaches 0
///
/// Fixes:
/// - Stops all movement on death (NavMeshAgent + root motion + rigidbody/character controller)
/// - Optional corpse lifetime (auto-destroy after N seconds)
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemy2Controller : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float walkSpeed = 2.0f;
    public float runSpeed = 4.5f;

    [Header("Animator Drive")]
    public string speedFloatParam = "Speed";
    public string isWalkingBoolParam = "isWalking";
    public string isRunningBoolParam = "isRunning";
    public float movingThreshold = 0.10f;

    [Header("Targeting")]
    public Transform player;
    public bool autoAggroPlayerByDistance = true;
    [Min(0f)] public float distanceToChase = 22f;
    [Min(0f)] public float distanceToLose = 40f;


    

    [Header("Sight / Auto Aggro (like Animal)")]
    [Tooltip("If true, auto-aggro uses sightRange (and optional line-of-sight) instead of simple distanceToChase. " +
             "This makes the enemy behave more like the Animal controller's sight-based acquisition.")]
    public bool useSightAggro = true;

    [Tooltip("How far the enemy can 'see' a target for auto-aggro (Unity units).")]
    [Min(0f)] public float sightRange = 18f;

    [Tooltip("How often (seconds) to check for sight-based auto-aggro. Lower is more responsive but costs more.")]
    [Min(0.02f)] public float sightScanInterval = 0.20f;

    [Tooltip("If true, the target must be visible (raycast line-of-sight) to be acquired via sight.")]
    public bool requireLineOfSightForSightAggro = false;

    [Tooltip("Layers that can block line-of-sight checks (walls/terrain/etc).")]
    public LayerMask sightLineOfSightBlockers = ~0;

    [Tooltip("Optional height offset for the enemy's 'eyes' when doing line-of-sight raycasts.")]
    public float sightEyeHeight = 1.4f;

    [Tooltip("Optional height offset for the target point when doing line-of-sight raycasts.")]
    public float sightTargetHeight = 1.2f;

[Tooltip("If > 0, being shot will only aggro if the attacker is within this distance. Set to 0 for unlimited (old behavior).")]
    [Min(0f)] public float shotAggroDistance = 60f;
    [Header("Melee")]
    [Min(0.1f)] public float desiredMeleeRange = 2.2f;
    [Min(0.05f)] public float attackCooldown = 1.1f;
    [Min(0f)] public float inRangeBuffer = 0.25f;

    [Header("Attack Audio (Optional)")]
    public AudioSource attackAudioSource;
    public AudioClip attackSFX;
    [Min(0f)] public float attackVolume = 1f;
    public bool randomizeAttackPitch = true;
    public float minAttackPitch = 0.96f;
    public float maxAttackPitch = 1.04f;

    [Header("Run Audio (Optional)")]
    public AudioSource runAudioSource;
    public AudioClip runLoopSFX;
    [Min(0f)] public float runVolume = 1f;
    public float runPitch = 1f;

    [Header("Animator Triggers")]
    public string attackTrigger = "attack";
    public string takeDamageTrigger = "take_damage";
    [Min(0f)] public float takeDamageTriggerCooldown = 0.12f;


    [Header("Hit-Reaction Throttling")]
    [Tooltip("Optional: prevents auto-fire from re-triggering take_damage every bullet. If enabled, we won't re-trigger while already in the take_damage state and we enforce a minimum interval.")]
    public bool throttleTakeDamageReactions = true;

    [Tooltip("Animator state name for your take_damage clip/state (Layer 0). Used to avoid re-triggering while the flinch is already playing. Leave empty to skip the state check.")]
    public string takeDamageStateName = "take_damage";

    [Tooltip("If throttleTakeDamageReactions is true, this is the minimum time between take_damage triggers (seconds).")]
    [Min(0f)] public float takeDamageMinInterval = 0.40f;
    [Header("Death (stop movement)")]
    [Tooltip("Animator trigger to play on death (leave empty if you use isDead bool instead).")]
    public string dieTrigger = "Die";

    [Tooltip("Animator bool to set true on death (leave empty if you only use die trigger).")]
    public string isDeadBoolParam = "isDead";

    [Tooltip("Disables NavMeshAgent on death so it cannot keep moving/repushing on the NavMesh.")]
    public bool disableNavMeshAgentOnDeath = true;

    [Tooltip("Disables CharacterController (if present) on death. Helps prevent sliding.")]
    public bool disableCharacterControllerOnDeath = true;

    [Tooltip("If there is a Rigidbody, make it kinematic + zero velocity on death.")]
    public bool makeRigidbodyKinematicOnDeath = true;

    [Tooltip("Disables Animator root motion on death. Helps if your death clip moves the root forward.")]
    public bool disableRootMotionOnDeath = true;

    [Tooltip("Extra safety: lock the corpse XZ position after death (prevents any sliding).")]
    public bool lockCorpseXZAfterDeath = true;

    [Tooltip("If lockCorpseXZAfterDeath is true, allow Y to change (fall to ground). Usually keep true.")]
    public bool allowYMovementWhenLocked = true;

    [Tooltip("How long the corpse lasts before being destroyed. 0 = never auto-destroy.")]
    [Min(0f)] public float corpseLifetimeSeconds = 0f;

    [Header("Patrol Integration (Optional)")]
    public Behaviour waypointPatrolBehaviour; // Assign EnemyWaypointPatrol here (or leave empty to auto-find)
    public bool sendResetPatrolMessageOnReturn = true;

    private NavMeshAgent agent;
    private Animator anim;

    private Transform combatTarget;
    private float nextAttackTime;
    private float lastSeenTargetTime;
    private float nextSightScanTime;
    private const float TargetGraceSeconds = 1.25f;

    private bool patrolWasEnabledBeforeCombat;
    private float lastTakeDamageTriggerTime = -999f;

    private bool isDead = false;
    private Vector3 deathPos;
    private Quaternion deathRot;

    private Rigidbody rb;
    private CharacterController characterController;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        characterController = GetComponent<CharacterController>();

        ResolveAttackAudioSourceIfNeeded();
        ResolveRunAudioSourceIfNeeded();

        if (waypointPatrolBehaviour == null)
        {
            // Try to auto-find a patrol component by name (no hard dependency).
            foreach (var b in GetComponents<Behaviour>())
            {
                if (b == null) continue;
                var n = b.GetType().Name;
                if (n == "EnemyWaypointPatrol" || n.Contains("Waypoint"))
                {
                    waypointPatrolBehaviour = b;
                    break;
                }
            }
        }
    }

    private void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (agent != null)
        {
            agent.stoppingDistance = Mathf.Max(0.1f, desiredMeleeRange);
            ApplyWalkSpeed();
        }

        DriveAnimatorLocomotion(0f, hasCombatTarget: false);
    }

    private void Update()
    {
        if (isDead) return;
        if (agent == null) return;

        // Auto-aggro when we have no target yet
        if (combatTarget == null && player != null)
        {
            if (autoAggroPlayerByDistance)
            {
                // Sight-style acquisition (like AnimalController) if enabled; otherwise simple distance check.
                if (useSightAggro)
                {
                    if (Time.time >= nextSightScanTime)
                    {
                        nextSightScanTime = Time.time + Mathf.Max(0.02f, sightScanInterval);

                        if (IsWithinSightRange(player) && (!requireLineOfSightForSightAggro || HasLineOfSight(player)))
                            SetCombatTarget(player);
                    }
                }
                else
                {
                    float d = Vector3.Distance(transform.position, player.position);
                    if (d <= distanceToChase)
                        SetCombatTarget(player);
                }
            }
        }

        if (combatTarget != null)
        {
            ApplyRunSpeed();
            CombatTick();
        }
        else
        {
            ApplyWalkSpeed();
            DriveAnimatorLocomotion(agent.velocity.magnitude, hasCombatTarget: false);
        }

        UpdateRunLoopAudio();
    }

    private void LateUpdate()
    {
        if (!isDead) return;

        // Extra safety to prevent any movement after death (root motion, physics nudges, etc.)
        if (lockCorpseXZAfterDeath)
        {
            Vector3 p = transform.position;
            p.x = deathPos.x;
            p.z = deathPos.z;
            if (!allowYMovementWhenLocked) p.y = deathPos.y;
            transform.position = p;
        }
    }

    
    private bool IsWithinSightRange(Transform t)
    {
        if (t == null) return false;
        float r = useSightAggro ? sightRange : distanceToChase;
        if (r <= 0f) return true; // 0 means unlimited
        return Vector3.SqrMagnitude(t.position - transform.position) <= r * r;
    }

    private bool HasLineOfSight(Transform t)
    {
        if (t == null) return false;

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0f, sightEyeHeight);
        Vector3 target = t.position + Vector3.up * Mathf.Max(0f, sightTargetHeight);
        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;

        dir /= dist;

        // If we hit something before the target, LOS is blocked.
        return !Physics.Raycast(origin, dir, dist, sightLineOfSightBlockers, QueryTriggerInteraction.Ignore);
    }

private void CombatTick()
    {
        if (combatTarget == null) return;

        if (!combatTarget.gameObject.activeInHierarchy)
        {
            EndCombatAndReturnToPatrol();
            return;
        }

        float dist = Vector3.Distance(transform.position, combatTarget.position);

        if (dist > distanceToLose)
        {
            if (Time.time - lastSeenTargetTime > TargetGraceSeconds)
            {
                EndCombatAndReturnToPatrol();
                return;
            }
        }
        else
        {
            lastSeenTargetTime = Time.time;
        }

        agent.isStopped = false;
        agent.stoppingDistance = Mathf.Max(0.1f, desiredMeleeRange);
        agent.SetDestination(combatTarget.position);

        bool inRange = dist <= (desiredMeleeRange + inRangeBuffer);

        if (inRange)
        {
            agent.isStopped = true;
            DriveAnimatorLocomotion(0f, hasCombatTarget: true);

            // Face target (y-only)
            Vector3 look = combatTarget.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(look);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            }

            if (Time.time >= nextAttackTime)
            {
                TriggerAttack();
                nextAttackTime = Time.time + attackCooldown;
            }
        }
        else
        {
            DriveAnimatorLocomotion(agent.velocity.magnitude, hasCombatTarget: true);
        }
    }

    private void DriveAnimatorLocomotion(float velocityMag, bool hasCombatTarget)
    {
        if (anim == null) return;

        if (!string.IsNullOrEmpty(speedFloatParam))
            anim.SetFloat(speedFloatParam, velocityMag);

        bool moving = velocityMag > movingThreshold;

        if (!string.IsNullOrEmpty(isWalkingBoolParam))
            anim.SetBool(isWalkingBoolParam, moving && !hasCombatTarget && !isDead);

        if (!string.IsNullOrEmpty(isRunningBoolParam))
            anim.SetBool(isRunningBoolParam, moving && hasCombatTarget && !isDead);
    }

    private void ApplyWalkSpeed()
    {
        if (agent == null) return;
        float s = Mathf.Max(0f, walkSpeed);
        if (!Mathf.Approximately(agent.speed, s))
            agent.speed = s;
    }

    private void ApplyRunSpeed()
    {
        if (agent == null) return;
        float s = Mathf.Max(0f, runSpeed);
        if (!Mathf.Approximately(agent.speed, s))
            agent.speed = s;
    }

    private void TriggerAttack()
    {
        if (anim == null) return;
        if (string.IsNullOrEmpty(attackTrigger)) return;

        anim.ResetTrigger(attackTrigger);
        anim.SetTrigger(attackTrigger);
        TriggerAttackSound();
    }

    private void ResolveRunAudioSourceIfNeeded()
    {
        if (runAudioSource != null) return;

        var localSources = GetComponents<AudioSource>();
        foreach (var s in localSources)
        {
            if (s != null && s != attackAudioSource)
            {
                runAudioSource = s;
                return;
            }
        }

        var childSources = GetComponentsInChildren<AudioSource>();
        foreach (var s in childSources)
        {
            if (s != null && s != attackAudioSource)
            {
                runAudioSource = s;
                return;
            }
        }

        // Fallback: share the same source only if no separate source exists.
        if (runAudioSource == null)
            runAudioSource = attackAudioSource;
    }

    private void UpdateRunLoopAudio()
    {
        if (runLoopSFX == null)
        {
            StopRunLoopSound();
            return;
        }

        if (runAudioSource == null)
            ResolveRunAudioSourceIfNeeded();
        if (runAudioSource == null) return;

        bool shouldPlay = !isDead && combatTarget != null && agent != null && !agent.isStopped && agent.velocity.magnitude > movingThreshold;

        if (!shouldPlay)
        {
            StopRunLoopSound();
            return;
        }

        if (runAudioSource.clip != runLoopSFX)
            runAudioSource.clip = runLoopSFX;

        runAudioSource.loop = true;
        runAudioSource.playOnAwake = false;
        runAudioSource.volume = runVolume;
        runAudioSource.pitch = runPitch;

        if (!runAudioSource.isPlaying)
            runAudioSource.Play();
    }

    private void StopRunLoopSound()
    {
        if (runAudioSource == null) return;
        if (runAudioSource.isPlaying && runAudioSource.clip == runLoopSFX)
            runAudioSource.Stop();
    }

    private void ResolveAttackAudioSourceIfNeeded()
    {
        if (attackAudioSource != null) return;
        attackAudioSource = GetComponent<AudioSource>();
        if (attackAudioSource == null)
            attackAudioSource = GetComponentInChildren<AudioSource>();
    }

    private void TriggerAttackSound()
    {
        if (attackSFX == null) return;
        if (attackAudioSource == null)
            ResolveAttackAudioSourceIfNeeded();
        if (attackAudioSource == null) return;

        float originalPitch = attackAudioSource.pitch;
        if (randomizeAttackPitch)
        {
            float low = Mathf.Min(minAttackPitch, maxAttackPitch);
            float high = Mathf.Max(minAttackPitch, maxAttackPitch);
            attackAudioSource.pitch = Random.Range(low, high);
        }

        attackAudioSource.PlayOneShot(attackSFX, attackVolume);
        attackAudioSource.pitch = originalPitch;
    }

    private void TriggerTakeDamage()
    {
        if (anim == null) return;
        if (string.IsNullOrEmpty(takeDamageTrigger)) return;

        // Throttle auto-fire "flinch spam":
        // - Don't re-trigger while the take_damage state is playing OR queued in a transition (optional)
        // - Enforce a minimum interval between triggers
        if (throttleTakeDamageReactions)
        {
            if (!string.IsNullOrEmpty(takeDamageStateName))
            {
                // Check ALL animator layers, and both current + next states (during transitions).
                for (int layer = 0; layer < anim.layerCount; layer++)
                {
                    var cur = anim.GetCurrentAnimatorStateInfo(layer);
                    if (cur.IsName(takeDamageStateName) && cur.normalizedTime < 0.95f)
                        return;

                    if (anim.IsInTransition(layer))
                    {
                        var nxt = anim.GetNextAnimatorStateInfo(layer);
                        if (nxt.IsName(takeDamageStateName))
                            return;
                    }
                }
            }

            float minInterval = Mathf.Max(takeDamageTriggerCooldown, takeDamageMinInterval);
            if (Time.time < lastTakeDamageTriggerTime + minInterval)
                return;
        }
        else
        {
            if (Time.time < lastTakeDamageTriggerTime + takeDamageTriggerCooldown)
                return;
        }

        lastTakeDamageTriggerTime = Time.time;

        anim.ResetTrigger(takeDamageTrigger);
        anim.SetTrigger(takeDamageTrigger);
    }

    public void SetCombatTarget(Transform target)
    {
        if (isDead) return;
        if (target == null) return;

        combatTarget = target;
        lastSeenTargetTime = Time.time;

        ApplyRunSpeed();

        if (waypointPatrolBehaviour != null)
        {
            patrolWasEnabledBeforeCombat = waypointPatrolBehaviour.enabled;
            waypointPatrolBehaviour.enabled = false;
        }
    }

    public void GetShot(Transform attacker)
    {
        if (isDead) return;
        if (!ShouldAggroFromShotAttacker(attacker)) return;
        SetCombatTarget(attacker);
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;
        TriggerTakeDamage();
    }

    public void TakeDamage(float damage, Transform attacker)
    {
        if (isDead) return;
        TriggerTakeDamage();
        
        if (attacker != null)
        {
            if (shotAggroDistance <= 0f || Vector3.Distance(transform.position, attacker.position) <= shotAggroDistance)
                SetCombatTarget(attacker);
        }
    }

    public void OnHit(Transform attacker)
    {
        if (isDead) return;
        TriggerTakeDamage();
        
        if (attacker != null)
        {
            if (shotAggroDistance <= 0f || Vector3.Distance(transform.position, attacker.position) <= shotAggroDistance)
                SetCombatTarget(attacker);
        }
    }

    /// <summary>
    /// Call this from your health script when HP reaches 0.
    /// Stops movement, kills pathing, prevents root motion sliding, and optionally destroys the corpse later.
    /// </summary>
    public void Die()
    {
        if (isDead) return;
        isDead = true;
        StopRunLoopSound();

        deathPos = transform.position;
        deathRot = transform.rotation;

        combatTarget = null;
        autoAggroPlayerByDistance = false;

        if (waypointPatrolBehaviour != null)
            waypointPatrolBehaviour.enabled = false;

        // Stop NavMeshAgent hard
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;

            // Prevent agent from re-applying transforms this frame
            agent.updatePosition = false;
            agent.updateRotation = false;

            if (disableNavMeshAgentOnDeath && agent.enabled)
                agent.enabled = false;
        }

        // Stop character controller from pushing/stepping
        if (disableCharacterControllerOnDeath && characterController != null && characterController.enabled)
            characterController.enabled = false;

        // Stop rigidbody motion
        if (makeRigidbodyKinematicOnDeath && rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Animator death params
        if (anim != null)
        {
            if (disableRootMotionOnDeath)
                anim.applyRootMotion = false;

            if (!string.IsNullOrEmpty(speedFloatParam))
                anim.SetFloat(speedFloatParam, 0f);

            if (!string.IsNullOrEmpty(isWalkingBoolParam))
                anim.SetBool(isWalkingBoolParam, false);

            if (!string.IsNullOrEmpty(isRunningBoolParam))
                anim.SetBool(isRunningBoolParam, false);

            if (!string.IsNullOrEmpty(isDeadBoolParam))
                anim.SetBool(isDeadBoolParam, true);

            if (!string.IsNullOrEmpty(dieTrigger))
            {
                anim.ResetTrigger(dieTrigger);
                anim.SetTrigger(dieTrigger);
            }
        }

        // Optional corpse cleanup
        if (corpseLifetimeSeconds > 0f)
            StartCoroutine(DestroyAfterSeconds(corpseLifetimeSeconds));
    }

    private IEnumerator DestroyAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Destroy(gameObject);
    }

    private bool ShouldAggroFromShotAttacker(Transform attacker)
    {
        if (attacker == null) return false;

        // Surgical fix: ignore accidental bullet hits from other Enemy-tagged units.
        if (attacker.CompareTag("Enemy"))
            return false;

        if (shotAggroDistance > 0f && Vector3.Distance(transform.position, attacker.position) > shotAggroDistance)
            return false;

        return true;
    }

    public void ClearCombatTarget() => EndCombatAndReturnToPatrol();

    private void EndCombatAndReturnToPatrol()
    {
        if (isDead) return;

        StopRunLoopSound();
        combatTarget = null;

        if (agent != null)
        {
            agent.isStopped = false;
            ApplyWalkSpeed();
        }

        if (waypointPatrolBehaviour != null)
        {
            waypointPatrolBehaviour.enabled = patrolWasEnabledBeforeCombat;

            if (sendResetPatrolMessageOnReturn && waypointPatrolBehaviour.enabled)
                waypointPatrolBehaviour.SendMessage("ResetPatrol", SendMessageOptions.DontRequireReceiver);
        }
    }

    public Transform GetCurrentTarget() => combatTarget;
    public bool IsDead() => isDead;
}
