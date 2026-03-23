using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AnimalController (simple AI)
/// - Patrol between waypoints at walkSpeed (default 1), or optionally wander randomly in a radius
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

    [Header("Auto Aggro")]
    [Tooltip("If true, the animal will periodically look for nearby hostile targets (based on Hostility toggles). If false, it will only become aggressive via damage reaction / external target assignment.")]
    public bool autoAggroNearbyHostiles = true;

    [Tooltip("If true, auto-aggro can acquire the Player when within range. Default false so animals won't attack just because they see the player.")]
    public bool autoAggroPlayers = false;

    [Tooltip("If true, auto-aggro can acquire Allies when within range.")]
    public bool autoAggroAllies = false;

    [Tooltip("If true, auto-aggro can acquire Enemies when within range. Default true for 'wildlife attacks nearby enemies'.")]
    public bool autoAggroEnemies = true;

    [Tooltip("Search radius for auto-aggro. If <= 0, sightRange will be used.")]
    public float autoAggroRange = 0f;

    [Tooltip("Optional layer filtering for auto-aggro scans. Leave as Everything unless you want to restrict scans.")]
    public LayerMask autoAggroLayerMask = ~0;

    [Tooltip("If true, targets must be visible (raycast line-of-sight) to be acquired.")]
    public bool requireLineOfSight = false;

    [Tooltip("Layers considered as occluders for line-of-sight checks.")]
    public LayerMask lineOfSightBlockers = ~0;

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
    [Tooltip("If true, the animal will patrol by picking random NavMesh points inside Wander Radius instead of using waypoints.")]
    public bool useRandomWander = true;

    [Tooltip("Optional center for random wandering. If left empty, the animal's spawn position is used.")]
    public Transform wanderCenter;

    [Tooltip("How far from the wander center the animal is allowed to roam.")]
    public float wanderRadius = 100f;

    [Tooltip("How close the animal must get to a random wander point before choosing a new one.")]
    public float wanderArriveDistance = 1.5f;

    [Tooltip("How long to pause after reaching a random wander point.")]
    public float wanderPauseSeconds = 0.5f;

    [Tooltip("How far from the random point NavMesh.SamplePosition is allowed to search.")]
    public float wanderSampleDistance = 12f;

    public Transform[] waypoints;
    public bool pingPong = true;
    public float waypointArriveDistance = 0.6f;
    public float patrolPauseSeconds = 0.5f;

    [Header("Attack")]
    public int attackDamage = 10;
    public float attackCooldown = 1.2f;

    [Header("Footsteps")]
    public AudioClip moveFootstepSfx;
    public AudioSource footstepAudioSource;
    public float footstepSfxVolume = 1f;
    public float walkFootstepInterval = 0.55f;
    public float runFootstepInterval = 0.32f;
    public float minVelocityForFootsteps = 0.15f;
    public bool requireAgentPathForFootsteps = true;

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
    private float _nextFootstepTime;

    private Vector3 _spawnPosition;
    private bool _hasWanderDestination;
    private Vector3 _currentWanderDestination;

    private void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<AnimalHealth>();
        animator = GetComponentInChildren<Animator>();
        footstepAudioSource = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!health) health = GetComponent<AnimalHealth>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!footstepAudioSource) footstepAudioSource = GetComponent<AudioSource>();

        _spawnPosition = transform.position;

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
        UpdateFootsteps();

        // Auto-close attack window
        if (_attackWindowActive && Time.time >= _attackWindowEnd)
            EndAttackWindow();

        switch (_state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Chase: TickChase(); break;
            case State.Attack: TickAttack(); break;
            case State.Flee: TickFlee(); break;
        }
    }

    private void UpdateAnimator()
    {
        if (!animator || string.IsNullOrWhiteSpace(speedParam)) return;

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

    private void UpdateFootsteps()
    {
        if (!moveFootstepSfx)
        {
            _nextFootstepTime = 0f;
            return;
        }

        if (!agent || !agent.enabled)
        {
            _nextFootstepTime = 0f;
            return;
        }

        bool isMoving = agent.velocity.magnitude > minVelocityForFootsteps;

        if (requireAgentPathForFootsteps && (!agent.hasPath || agent.isStopped))
            isMoving = false;

        if (!isMoving)
        {
            _nextFootstepTime = 0f;
            return;
        }

        bool useRunCadence = (_state == State.Chase || _state == State.Flee || _state == State.Attack);
        float interval = useRunCadence ? Mathf.Max(0.05f, runFootstepInterval) : Mathf.Max(0.05f, walkFootstepInterval);

        if (_nextFootstepTime > Time.time)
            return;

        PlayFootstepSfx();
        _nextFootstepTime = Time.time + interval;
    }

    public void PlayFootstepSfx()
    {
        if (!moveFootstepSfx) return;

        AudioSource source = footstepAudioSource;
        if (!source) source = GetComponent<AudioSource>();
        if (!source) return;

        source.PlayOneShot(moveFootstepSfx, footstepSfxVolume);
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

        if (!agent) return;

        if (useRandomWander)
        {
            TickRandomWanderPatrol();
            return;
        }

        if (waypoints == null || waypoints.Length == 0) return;

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

    private void TickRandomWanderPatrol()
    {
        if (!agent) return;

        if (Time.time < _patrolWaitUntil)
        {
            agent.isStopped = true;
            return;
        }

        if (_hasWanderDestination)
        {
            agent.isStopped = false;
            agent.stoppingDistance = 0f;

            if (!agent.pathPending && agent.remainingDistance <= wanderArriveDistance)
            {
                _hasWanderDestination = false;
                _patrolWaitUntil = Time.time + wanderPauseSeconds;
                agent.isStopped = true;
                return;
            }

            if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                _hasWanderDestination = false;
            }

            return;
        }

        if (TryGetRandomWanderPoint(out Vector3 nextPoint))
        {
            _currentWanderDestination = nextPoint;
            _hasWanderDestination = true;
            agent.isStopped = false;
            agent.stoppingDistance = 0f;
            agent.SetDestination(_currentWanderDestination);
        }
        else
        {
            _patrolWaitUntil = Time.time + 0.5f;
            agent.isStopped = true;
        }
    }

    private bool TryGetRandomWanderPoint(out Vector3 point)
    {
        Vector3 center = GetWanderCenter();

        for (int i = 0; i < 12; i++)
        {
            Vector2 random2D = Random.insideUnitCircle * Mathf.Max(0.1f, wanderRadius);
            Vector3 candidate = center + new Vector3(random2D.x, 0f, random2D.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(0.5f, wanderSampleDistance), NavMesh.AllAreas))
            {
                if (Vector3.Distance(center, hit.position) <= wanderRadius + 0.5f)
                {
                    point = hit.position;
                    return true;
                }
            }
        }

        point = transform.position;
        return false;
    }

    private Vector3 GetWanderCenter()
    {
        return wanderCenter ? wanderCenter.position : _spawnPosition;
    }

    // ----------------------
    // Chase
    // ----------------------
    private void TickChase()
    {
        // ensure agent is not stuck stopped when chasing
        if (agent != null) agent.isStopped = false;

        ApplySpeedForState(State.Chase);

        if (!_target)
        {
            ClearPatrolDestination();
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
            ClearPatrolDestination();
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
            ClearPatrolDestination();
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
        // ensure agent is not stuck stopped when fleeing
        if (agent != null) agent.isStopped = false;

        ApplySpeedForState(State.Flee);

        if (!agent)
        {
            ClearPatrolDestination();
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
                    ClearPatrolDestination();
                    _state = State.Patrol;
                    ApplySpeedForState(_state);
                }
            }
        }
        else
        {
            ClearPatrolDestination();
            _state = State.Patrol;
            ApplySpeedForState(_state);
        }
    }

    // ----------------------
    // Damage / death
    // ----------------------
    private void HandleDamaged(AnimalHealth h, int amount, Transform attacker)
    {
        // If we were paused/stopped in Patrol when hit, immediately resume movement so Chase/Flee works.
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
        _nextFootstepTime = 0f;
    }

    // ----------------------
    // Targeting
    // ----------------------
    private void TryAcquireTarget()
    {
        if (!IsAlive) return;
        if (_target) return;

        // If we're not auto-aggroing, we only fight when provoked (damage reaction) or when another system assigns a target.
        if (!autoAggroNearbyHostiles) return;
        if (!autoAggroPlayers && !autoAggroAllies && !autoAggroEnemies) return;

        float range = (autoAggroRange > 0f) ? autoAggroRange : sightRange;

        var hits = Physics.OverlapSphere(transform.position, range, autoAggroLayerMask, QueryTriggerInteraction.Ignore);
        Transform best = null;
        float bestD = float.MaxValue;

        foreach (var c in hits)
        {
            if (!c) continue;
            var t = c.transform;

            // Skip self / own hierarchy
            if (t == transform || t.IsChildOf(transform)) continue;

            // Only consider configured hostile factions
            if (!IsAutoAggroHostileTo(t)) continue;

            // Optional LOS check
            if (requireLineOfSight)
            {
                Vector3 origin = transform.position + Vector3.up * 0.75f;
                Vector3 targetPos = t.position + Vector3.up * 0.75f;
                Vector3 dir = (targetPos - origin);
                float dist = dir.magnitude;

                if (dist > 0.01f)
                {
                    dir /= dist;
                    // If something blocks the ray before we reach the target, skip it.
                    if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
                    {
                        // If we hit something that isn't the target (or its children), LOS is blocked.
                        if (hit.transform != t && !hit.transform.IsChildOf(t))
                            continue;
                    }
                }
            }

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

    private bool IsAutoAggroHostileTo(Transform t)
    {
        if (!t) return false;
        if (autoAggroPlayers && t.CompareTag(playerTag)) return true;
        if (autoAggroAllies && t.CompareTag(allyTag)) return true;
        if (autoAggroEnemies && t.CompareTag(enemyTag)) return true;
        return false;
    }

    private bool IsHostileTo(Transform t)
    {
        if (!t) return false;
        if (hostileToPlayer && t.CompareTag(playerTag)) return true;
        if (hostileToAllies && t.CompareTag(allyTag)) return true;
        if (hostileToEnemies && t.CompareTag(enemyTag)) return true;
        return false;
    }

    private void ClearPatrolDestination()
    {
        _hasWanderDestination = false;
        _patrolWaitUntil = 0f;
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
    }

    /// <summary>Call from Animation Event when swing ends.</summary>
    public void EndAttackWindow()
    {
        _attackWindowActive = false;
        _attackWindowEnd = 0f;

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

        if (useRandomWander)
        {
            Gizmos.color = Color.cyan;
            Vector3 center = wanderCenter ? wanderCenter.position : transform.position;
            Gizmos.DrawWireSphere(center, wanderRadius);
        }
    }
#endif
}
