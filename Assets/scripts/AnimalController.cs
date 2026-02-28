using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AnimalController (simple AI)
/// - Patrol between waypoints at walkSpeed (default 1)
/// - When aggro (has a target), it uses runSpeed (default 3.5)
/// - Attacks when in range (Attack trigger) with optional timed hitbox window
///
/// Notes:
/// - Animator should have a Float parameter named "Speed" (or change speedParam)
/// - Your locomotion (idle/walk/run) should be driven by Speed thresholds
/// - Attack should be driven by an "Attack" Trigger (or change attackTrigger)
/// </summary>
[DisallowMultipleComponent]
public class AnimalController : MonoBehaviour
{
    public enum ReactionToDamage { Passive, FleeOnly, FightOnly, FleeWhenLow, FleeThenFight }
    private enum State { Patrol, Chase, Attack, Flee, Dead }

    [Header("Core")]
    public NavMeshAgent agent;
    public Animator animator;
    public AnimalHealth health;

    [Header("Hostility")]
    public bool hostileToPlayer;
    public bool hostileToAllies;
    public bool hostileToEnemies;
    public string playerTag = "Player";
    public string allyTag = "Ally";
    public string enemyTag = "Enemy";

    [Header("Ranges")]
    public float sightRange = 10f;
    public float chaseRange = 15f;
    public float attackRange = 1.8f;
    public float stopDistanceForAttack = 1.2f;

    [Header("Nav Speeds")]
    [Tooltip("Patrol speed (walk).")]
    public float walkSpeed = 1f;

    [Tooltip("Aggro speed (run): used for chase + flee + approaching attack.")]
    public float runSpeed = 3.5f;

    [Header("Patrol")]
    public Transform[] waypoints;
    public bool pingPong = true;
    public float waypointArriveDistance = 0.6f;
    public float patrolPauseSeconds = 0.5f;

    [Header("Attack")]
    public int attackDamage = 10;
    public float attackCooldown = 1.2f;

    [Tooltip("Child hitbox with trigger collider + AnimalAttackHitbox.")]
    public AnimalAttackHitbox attackHitbox;

    [Tooltip("If you don't want Animation Events, auto-open the hitbox window after triggering attack.")]
    public bool autoAttackWindow = true;
    public float autoWindowDelay = 0.15f;
    public float autoWindowDuration = 0.25f;

    [Header("Animator Params")]
    public string speedParam = "Speed";
    public string attackTrigger = "Attack";

    [Header("Damage Reaction")]
    public ReactionToDamage reaction = ReactionToDamage.FleeWhenLow;
    [Range(0.05f, 0.95f)] public float fleeWhenBelowHealthPct = 0.35f;
    public bool alwaysTargetAttacker = true;

    public bool IsAlive => health != null && !health.IsDead;
    public bool IsInAttackWindow => _attackWindowActive;

    private State _state = State.Patrol;
    private Transform _target;

    private int _wpIndex = 0;
    private int _wpDir = 1;
    private float _nextAttackTime = 0f;
    private float _patrolWaitUntil = 0f;

    private bool _attackWindowActive;
    private float _attackWindowEnd;

    private void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<AnimalHealth>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!health) health = GetComponent<AnimalHealth>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!attackHitbox) attackHitbox = GetComponentInChildren<AnimalAttackHitbox>();

        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDied += HandleDied;
        }

        ApplySpeedForState(_state);
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDied -= HandleDied;
        }
    }

    private void Update()
    {
        if (!IsAlive)
        {
            _state = State.Dead;
            ApplySpeedForState(_state);
            return;
        }

        UpdateAnimator();

        // Auto-close attack window
        if (_attackWindowActive && Time.time >= _attackWindowEnd)
            EndAttackWindow();

        switch (_state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Chase:  TickChase();  break;
            case State.Attack: TickAttack(); break;
            case State.Flee:   TickFlee();   break;
        }
    }

    private void UpdateAnimator()
    {
        if (!animator || string.IsNullOrWhiteSpace(speedParam)) return;

        // KEY FEATURE YOU ASKED FOR:
        // When aggro (chasing/attacking), keep locomotion in "run" even if we briefly stop to swing.
        float spd;
        if (_target && (_state == State.Chase || _state == State.Attack))
        {
            spd = (agent && agent.velocity.sqrMagnitude > 0.01f) ? agent.velocity.magnitude : runSpeed;
        }
        else
        {
            spd = agent ? agent.velocity.magnitude : 0f;
        }

        animator.SetFloat(speedParam, spd);
    }

    private void ApplySpeedForState(State s)
    {
        if (!agent) return;

        switch (s)
        {
            case State.Patrol:
                agent.speed = walkSpeed;
                break;

            case State.Chase:
            case State.Attack: // approaching + attacking is "aggro" => run
            case State.Flee:
                agent.speed = runSpeed;
                break;

            case State.Dead:
                agent.speed = 0f;
                break;
        }
    }

    // ----------------------
    // Patrol
    // ----------------------
    private void TickPatrol()
    {
        ApplySpeedForState(State.Patrol);

        TryAcquireTarget();
        if (_target)
        {
            _state = State.Chase;
            ApplySpeedForState(_state);
            return;
        }

        if (!agent || waypoints == null || waypoints.Length == 0) return;

        if (Time.time < _patrolWaitUntil)
        {
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;
        agent.stoppingDistance = 0f;

        var wp = waypoints[Mathf.Clamp(_wpIndex, 0, waypoints.Length - 1)];
        if (!wp) return;

        agent.SetDestination(wp.position);

        if (!agent.pathPending && agent.remainingDistance <= waypointArriveDistance)
        {
            _patrolWaitUntil = Time.time + patrolPauseSeconds;

            if (pingPong)
            {
                if (_wpIndex == waypoints.Length - 1) _wpDir = -1;
                else if (_wpIndex == 0) _wpDir = 1;
                _wpIndex += _wpDir;
            }
            else
            {
                _wpIndex = (_wpIndex + 1) % waypoints.Length;
            }
        }
    }

    // ----------------------
    // Chase
    // ----------------------
    private void TickChase()
    {
        // FIX: ensure agent is not stuck stopped when chasing
        if (agent != null) agent.isStopped = false;

        ApplySpeedForState(State.Chase);

        if (!_target)
        {
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        if (!agent)
        {
            _state = State.Attack;
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);

        if (dist > chaseRange || !IsHostileTo(_target))
        {
            _target = null;
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        agent.isStopped = false;
        agent.stoppingDistance = stopDistanceForAttack;
        agent.SetDestination(_target.position);

        if (dist <= attackRange)
        {
            _state = State.Attack;
            ApplySpeedForState(_state);
        }
    }

    // ----------------------
    // Attack
    // ----------------------
    private void TickAttack()
    {
        ApplySpeedForState(State.Attack);

        if (!_target)
        {
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);

        if (dist > attackRange * 1.15f)
        {
            _state = State.Chase;
            ApplySpeedForState(_state);
            return;
        }

        // Keep running approach until very close, then stop briefly to swing.
        if (agent)
        {
            agent.isStopped = false;
            agent.stoppingDistance = stopDistanceForAttack;
            agent.SetDestination(_target.position);

            if (dist <= stopDistanceForAttack + 0.15f)
                agent.isStopped = true;
        }

        // Face target
        Vector3 to = (_target.position - transform.position);
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f)
        {
            var rot = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
        }

        if (Time.time < _nextAttackTime) return;

        _nextAttackTime = Time.time + attackCooldown;

        if (animator && !string.IsNullOrWhiteSpace(attackTrigger))
            animator.SetTrigger(attackTrigger);

        if (autoAttackWindow)
            Invoke(nameof(BeginAttackWindow_Auto), autoWindowDelay);
    }

    // ----------------------
    // Flee
    // ----------------------
    private void TickFlee()
    {
        // FIX: ensure agent is not stuck stopped when fleeing
        if (agent != null) agent.isStopped = false;

        ApplySpeedForState(State.Flee);

        if (!agent)
        {
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        Vector3 away = (_target ? (transform.position - _target.position) : transform.forward);
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;

        Vector3 dest = transform.position + away.normalized * 8f;

        agent.isStopped = false;
        agent.stoppingDistance = 0f;
        agent.SetDestination(dest);

        if (_target)
        {
            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist >= Mathf.Max(attackRange * 2f, 6f))
            {
                if (reaction == ReactionToDamage.FleeThenFight && IsHostileTo(_target))
                {
                    _state = State.Chase;
                    ApplySpeedForState(_state);
                }
                else
                {
                    _target = null;
                    _state = State.Patrol;
                    ApplySpeedForState(_state);
                }
            }
        }
        else
        {
            _state = State.Patrol;
            ApplySpeedForState(_state);
        }
    }

    // ----------------------
    // Damage / death
    // ----------------------
    private void HandleDamaged(AnimalHealth h, int amount, Transform attacker)
    {
        // FIX: If we were paused/stopped in Patrol when hit, immediately resume movement so Chase/Flee works.
        if (agent != null) agent.isStopped = false;
        _patrolWaitUntil = 0f;

        if (!IsAlive) return;

        if (alwaysTargetAttacker && attacker)
            _target = attacker;

        switch (reaction)
        {
            case ReactionToDamage.Passive:
                if (attacker) _state = State.Flee;
                break;

            case ReactionToDamage.FleeOnly:
                _state = State.Flee;
                break;

            case ReactionToDamage.FightOnly:
                _state = (_target && IsHostileTo(_target)) ? State.Chase : State.Flee;
                break;

            case ReactionToDamage.FleeWhenLow:
                float pct = (float)h.CurrentHealth / Mathf.Max(1, h.maxHealth);
                if (pct <= fleeWhenBelowHealthPct) _state = State.Flee;
                else _state = (_target && IsHostileTo(_target)) ? State.Chase : State.Flee;
                break;

            case ReactionToDamage.FleeThenFight:
                _state = State.Flee;
                break;
        }

        ApplySpeedForState(_state);
    }

    private void HandleDied(AnimalHealth h, Transform killer)
    {
        _state = State.Dead;
        ApplySpeedForState(_state);

        if (agent)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        EndAttackWindow();
    }

    // ----------------------
    // Targeting
    // ----------------------
    private void TryAcquireTarget()
    {
        if (!IsAlive) return;
        if (_target) return;

        if (!hostileToPlayer && !hostileToAllies && !hostileToEnemies) return;

        var hits = Physics.OverlapSphere(transform.position, sightRange);
        Transform best = null;
        float bestD = float.MaxValue;

        foreach (var c in hits)
        {
            if (!c) continue;
            var t = c.transform;
            if (t == transform || t.IsChildOf(transform)) continue;
            if (!IsHostileTo(t)) continue;

            float d = Vector3.Distance(transform.position, t.position);
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }

        if (best)
        {
            _target = best;
            _state = State.Chase;
            ApplySpeedForState(_state);
        }
    }

    private bool IsHostileTo(Transform t)
    {
        if (!t) return false;
        if (hostileToPlayer && t.CompareTag(playerTag)) return true;
        if (hostileToAllies && t.CompareTag(allyTag)) return true;
        if (hostileToEnemies && t.CompareTag(enemyTag)) return true;
        return false;
    }

    // ----------------------
    // Attack window / hitbox
    // ----------------------
    private void BeginAttackWindow_Auto()
    {
        BeginAttackWindow();
        Invoke(nameof(EndAttackWindow), autoWindowDuration);
    }

    /// <summary>Call from Animation Event at bite/claw hit frame.</summary>
    public void BeginAttackWindow()
    {
        _attackWindowActive = true;
        _attackWindowEnd = Time.time + 999f;
        if (attackHitbox) attackHitbox.BeginSwing();
    }

    /// <summary>Call from Animation Event when swing ends.</summary>
    public void EndAttackWindow()
    {
        _attackWindowActive = false;
        _attackWindowEnd = 0f;
        if (attackHitbox) attackHitbox.EndSwing();

        CancelInvoke(nameof(BeginAttackWindow_Auto));
        CancelInvoke(nameof(EndAttackWindow));
    }

    /// <summary>Called by AnimalAttackHitbox while in the attack window.</summary>
    public bool TryDamageTarget(GameObject other)
    {
        if (!other) return false;
        var root = other.transform.root;
        if (!IsHostileTo(root)) return false;
        return AnimalDamageUtility.TryApplyDamage(root.gameObject, attackDamage, this.transform);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}
