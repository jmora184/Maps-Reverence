using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Melee-only enemy controller that can optionally patrol via EnemyWaypointPatrol when idle,
/// and switches to chase + melee attack when it has a combat target.
///
/// - Tag your enemy as: Enemy
/// - Requires: NavMeshAgent + Animator
/// - Optional: EnemyWaypointPatrol (will be disabled while in combat)
///
/// Aggro entry points (compatible with many of your existing scripts):
///   - SetCombatTarget(Transform target)
///   - GetShot(Transform attacker)   // alias to SetCombatTarget
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemy2Controller : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Optional explicit player target. If null, will try to find GameObject tagged 'Player'.")]
    public Transform player;

    [Tooltip("If true, the enemy will auto-aggro the player when within distanceToChase (line of sight not required). If false, it only aggroes via GetShot/SetCombatTarget.")]
    public bool autoAggroPlayerByDistance = true;

    [Tooltip("Distance at which the enemy will start chasing the player (only used if autoAggroPlayerByDistance = true)."), Min(0f)]
    public float distanceToChase = 22f;

    [Tooltip("Distance at which the enemy will give up and return to patrol/idle if it can't reach the target."), Min(0f)]
    public float distanceToLose = 40f;

    [Header("Melee")]
    [Tooltip("How close we try to get before attacking. Should roughly match your melee damage script's attackRange."), Min(0.1f)]
    public float desiredMeleeRange = 2.2f;

    [Tooltip("How often we trigger the attack animation while in range."), Min(0.05f)]
    public float attackCooldown = 1.1f;

    [Tooltip("Extra buffer added to desiredMeleeRange before we consider we are 'in range'."), Min(0f)]
    public float inRangeBuffer = 0.25f;

    [Header("Animator Params")]
    [Tooltip("Animator bool used to drive locomotion blend tree / walk cycle.")]
    public string isMovingBool = "isMoving";

    [Tooltip("Animator trigger that starts a melee attack animation.")]
    public string attackTrigger = "attack";

    [Header("Patrol Integration (Optional)")]
    [Tooltip("If present, this component will be disabled during combat and re-enabled when combat ends.")]
    public Behaviour waypointPatrolBehaviour; // Assign EnemyWaypointPatrol here (or leave empty to auto-find)

    [Tooltip("When returning to patrol, send a message named 'ResetPatrol' (no-arg). Useful if your patrol script needs to restart.")]
    public bool sendResetPatrolMessageOnReturn = true;

    private NavMeshAgent agent;
    private Animator anim;

    private Transform combatTarget;
    private float nextAttackTime;
    private float lastSeenTargetTime;
    private const float TargetGraceSeconds = 1.25f; // brief grace to avoid target flicker

    private bool patrolWasEnabledBeforeCombat;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();

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

        // Make stopping distance match melee range so NavMeshAgent stops cleanly before attacking.
        if (agent != null)
            agent.stoppingDistance = Mathf.Max(0.1f, desiredMeleeRange);
    }

    private void Update()
    {
        if (agent == null) return;

        // If we don't have a combat target, optionally auto-aggro the player by distance.
        if (combatTarget == null && autoAggroPlayerByDistance && player != null)
        {
            float d = Vector3.Distance(transform.position, player.position);
            if (d <= distanceToChase)
                SetCombatTarget(player);
        }

        if (combatTarget != null)
        {
            CombatTick();
        }
        else
        {
            // Idle/patrol mode: keep moving bool synced if patrol is moving us.
            SetMovingBool(agent.velocity.sqrMagnitude > 0.05f);
        }
    }

    private void CombatTick()
    {
        if (combatTarget == null) return;

        // If target got destroyed/disabled
        if (!combatTarget.gameObject.activeInHierarchy)
        {
            EndCombatAndReturnToPatrol();
            return;
        }

        float dist = Vector3.Distance(transform.position, combatTarget.position);

        // Lose target if too far for too long
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

        // Chase target until in melee range
        agent.isStopped = false;
        agent.stoppingDistance = Mathf.Max(0.1f, desiredMeleeRange);
        agent.SetDestination(combatTarget.position);

        bool inRange = dist <= (desiredMeleeRange + inRangeBuffer);

        if (inRange)
        {
            // Stop and attack
            agent.isStopped = true;
            SetMovingBool(false);

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
            SetMovingBool(true);
        }
    }

    private void TriggerAttack()
    {
        if (anim == null) return;
        if (string.IsNullOrEmpty(attackTrigger)) return;

        anim.ResetTrigger(attackTrigger);
        anim.SetTrigger(attackTrigger);
    }

    private void SetMovingBool(bool isMoving)
    {
        if (anim == null) return;
        if (string.IsNullOrEmpty(isMovingBool)) return;
        anim.SetBool(isMovingBool, isMoving);
    }

    /// <summary>
    /// External call used by bullets/animals/allies to force this enemy to aggro a target.
    /// </summary>
    public void SetCombatTarget(Transform target)
    {
        if (target == null) return;

        combatTarget = target;
        lastSeenTargetTime = Time.time;

        // Disable patrol while in combat
        if (waypointPatrolBehaviour != null)
        {
            patrolWasEnabledBeforeCombat = waypointPatrolBehaviour.enabled;
            waypointPatrolBehaviour.enabled = false;
        }
    }

    /// <summary>
    /// Compatibility alias. Many of your systems call GetShot(attacker).
    /// </summary>
    public void GetShot(Transform attacker)
    {
        SetCombatTarget(attacker);
    }

    /// <summary>
    /// Force the enemy to stop fighting and return to patrol/idle.
    /// </summary>
    public void ClearCombatTarget()
    {
        EndCombatAndReturnToPatrol();
    }

    private void EndCombatAndReturnToPatrol()
    {
        combatTarget = null;

        if (agent != null)
            agent.isStopped = false;

        if (waypointPatrolBehaviour != null)
        {
            waypointPatrolBehaviour.enabled = patrolWasEnabledBeforeCombat;

            if (sendResetPatrolMessageOnReturn && waypointPatrolBehaviour.enabled)
            {
                // No hard dependency on method signature.
                waypointPatrolBehaviour.SendMessage("ResetPatrol", SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    // Optional helper for other scripts
    public Transform GetCurrentTarget() => combatTarget;
}
