using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AlienBossController (Animal-style)
/// Goals (per your rules):
/// 1) Auto-aggro ENEMIES in sight range. Do NOT auto-aggro Player/Allies by sight.
///    Player/Allies only become targets when they PROVOKE the boss (shoot/damage him).
/// 2) Boss has health (AlienBossHealth) and tag "Boss".
/// 3) Damage system is range-based (handled by AlienBossMeleeDamageRange) - no collider hitboxes required.
/// 4) Animator parameters:
///      - Die trigger when health reaches 0
///      - agro trigger once when boss becomes hostile (latch)
///      - pound trigger when attacking PLAYER
///      - swing trigger when attacking ENEMY or ALLY
///      - Speed float drives locomotion blend (walk/run thresholds set in Animator)
/// 5) Patrol between waypoints like your animal/enemy.
/// 
/// IMPORTANT ABOUT WAYPOINTS:
/// You keep waypoints as children (location/location2 under the boss root).
/// That only works reliably if we treat them as "design-time markers" and cache their world targets.
/// This controller supports that:
///   - If a waypoint is a child of the boss, we cache its LOCAL position at Awake and convert to a
///     FIXED world position using the boss spawn anchor (initial transform.position).
///   - This makes child waypoints behave like your animal setup (organized under the boss), while
///     still being fixed patrol points in the scene.
/// </summary>
[DisallowMultipleComponent]
public class AlienBossController : MonoBehaviour
{
    private enum State { Patrol, Chase, Attack, Dead }

    [Header("References")]
    public NavMeshAgent agent;
    public Animator animator;
    public AlienBossHealth health;

    [Header("Tags")]
    public string playerTag = "Player";
    public string allyTag = "Ally";
    public string enemyTag = "Enemy";

    [Header("Hostility Rules")]
    [Tooltip("Boss will naturally be hostile to enemies when they are in sight range.")]
    public bool autoAggroEnemies = true;

    [Tooltip("Boss will NOT auto-aggro player by sight.")]
    public bool allowAutoAggroPlayer = false;

    [Tooltip("Boss will NOT auto-aggro allies by sight.")]
    public bool allowAutoAggroAllies = false;

    [Tooltip("If true, taking damage will always switch target to the attacker (or nearest player fallback).")]
    public bool alwaysTargetAttacker = true;

    [Header("Ranges")]
    public float sightRange = 14f;
    public float chaseRange = 22f;
    public float attackRange = 2.2f;
    public float stopDistanceForAttack = 1.35f;

    [Header("Nav Speeds")]
    public float walkSpeed = 5f;
    public float runSpeed = 15f;

    [Header("Patrol")]
    [Tooltip("You can keep these as children under the boss for organization.")]
    public Transform[] waypoints;
    public bool pingPong = true;
    public float waypointArriveDistance = 3f;
    public float patrolPauseSeconds = 0.5f;

    [Header("Attack")]
    public float attackCooldown = 1.35f;

    [Header("Audio - Aggro")]
    public AudioClip aggroSfx;
    public AudioSource aggroAudioSource;
    [Range(0f, 1f)] public float aggroSfxVolume = 1f;

    [Header("Audio - Footsteps")]
    public AudioClip footstepSfx;
    public AudioSource footstepAudioSource;
    [Range(0f, 1f)] public float footstepSfxVolume = 1f;
    public float walkFootstepInterval = 0.55f;
    public float runFootstepInterval = 0.3f;
    public float minVelocityForFootsteps = 0.15f;
    public bool requireAgentPathForFootsteps = true;

    [Header("Animator Params")]
    public string speedParam = "Speed";
    public string aggroTrigger = "agro";
    public string poundTrigger = "attackPound";
    public string swingTrigger = "attackSwing";
    public string dieTrigger = "Die";
    public string isAggroBool = "IsAggro";

    [Tooltip("If true, we play the agro animation only once when hostility starts.")]
    public bool playAggroOnce = true;

    // Runtime
    private State _state = State.Patrol;
    private Transform _target;

    private int _wpIndex = 0;
    private int _wpDir = 1;
    private float _patrolWaitUntil = 0f;
    private float _nextAttackTime = 0f;

    // Aggro latch so we don't replay agro
    private bool _aggroLatched = false;
    private float _nextFootstepTime = 0f;

    // Waypoint caching so child waypoints still work
    private Vector3 _spawnAnchorWorld;
    private Vector3[] _cachedWaypointWorld;

    public Transform CurrentTarget => _target;
    public bool IsAlive => health != null && !health.IsDead;

    private void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<AlienBossHealth>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!health) health = GetComponent<AlienBossHealth>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!aggroAudioSource) aggroAudioSource = GetComponent<AudioSource>();
        if (!aggroAudioSource) aggroAudioSource = GetComponentInChildren<AudioSource>();
        if (!footstepAudioSource) footstepAudioSource = GetComponent<AudioSource>();
        if (!footstepAudioSource) footstepAudioSource = GetComponentInChildren<AudioSource>();

        _spawnAnchorWorld = transform.position;
        CacheWaypoints();

        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDied += HandleDied;
        }

        ApplySpeedForState(_state);

        // Ensure animator starts non-aggro unless you intentionally set it
        if (animator && !string.IsNullOrWhiteSpace(isAggroBool))
            animator.SetBool(isAggroBool, false);
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDied -= HandleDied;
        }
    }

    private void CacheWaypoints()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            _cachedWaypointWorld = null;
            return;
        }

        _cachedWaypointWorld = new Vector3[waypoints.Length];

        for (int i = 0; i < waypoints.Length; i++)
        {
            var wp = waypoints[i];
            if (!wp)
            {
                _cachedWaypointWorld[i] = transform.position;
                continue;
            }

            // If it's parented under the boss, treat it like your animal setup:
            // use its localPosition relative to the boss spawn anchor (fixed).
            if (wp.IsChildOf(transform))
            {
                _cachedWaypointWorld[i] = _spawnAnchorWorld + wp.localPosition;
            }
            else
            {
                // If it's in the scene somewhere else, just use its world position.
                _cachedWaypointWorld[i] = wp.position;
            }
        }
    }

    private void Update()
    {
        if (!IsAlive)
        {
            _state = State.Dead;
            ApplySpeedForState(_state);
            UpdateAnimatorSpeed();
            StopFootsteps();
            return;
        }

        UpdateAnimatorSpeed();
        UpdateFootsteps();

        switch (_state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Chase:  TickChase();  break;
            case State.Attack: TickAttack(); break;
        }
    }

    private void UpdateAnimatorSpeed()
    {
        if (!animator || string.IsNullOrWhiteSpace(speedParam)) return;

        float spd = agent ? agent.velocity.magnitude : 0f;
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
            case State.Attack:
                agent.speed = runSpeed;
                break;

            case State.Dead:
                agent.speed = 0f;
                break;
        }
    }

    // ----------------------
    // Provocation entry points
    // ----------------------
    public void GetShot(Transform attacker)
    {
        if (!IsAlive) return;

        attacker = NormalizeAttacker(attacker);
        if (!attacker)
        {
            // fallback to nearest player in range
            attacker = FindNearestWithTag(playerTag, chaseRange);
        }

        if (attacker)
            AcquireTarget(attacker, provoked: true);
    }

    public void SetCombatTarget(Transform t)
    {
        if (!IsAlive) return;
        t = NormalizeAttacker(t);
        if (t) AcquireTarget(t, provoked: true);
    }

    // ----------------------
    // Patrol
    // ----------------------
    private void TickPatrol()
    {
        ApplySpeedForState(State.Patrol);

        // Acquire targets if any (enemies auto, player/ally only if allowed)
        TryAcquireAutoTarget();
        if (_target)
        {
            _state = State.Chase;
            ApplySpeedForState(_state);
            return;
        }

        if (!agent) return;
        if (_cachedWaypointWorld == null || _cachedWaypointWorld.Length == 0) return;

        if (Time.time < _patrolWaitUntil)
        {
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;

        // For large bosses, don't require perfect precision
        float arriveThreshold = Mathf.Max(
            waypointArriveDistance,
            agent.radius * 1.2f,
            agent.stoppingDistance + 0.2f
        );

        // set a sane stopping distance for patrol so it can "arrive"
        agent.stoppingDistance = Mathf.Max(agent.radius * 0.4f, 0.1f);

        _wpIndex = Mathf.Clamp(_wpIndex, 0, _cachedWaypointWorld.Length - 1);
        Vector3 dest = _cachedWaypointWorld[_wpIndex];

        if (!agent.hasPath || Vector3.Distance(agent.destination, dest) > 0.25f)
            agent.SetDestination(dest);

        bool arrived =
            !agent.pathPending &&
            agent.hasPath &&
            agent.remainingDistance <= arriveThreshold;

        if (arrived)
        {
            _patrolWaitUntil = Time.time + patrolPauseSeconds;

            if (pingPong)
            {
                if (_cachedWaypointWorld.Length <= 1)
                {
                    _wpIndex = 0;
                    _wpDir = 1;
                }
                else
                {
                    if (_wpIndex >= _cachedWaypointWorld.Length - 1) _wpDir = -1;
                    else if (_wpIndex <= 0) _wpDir = 1;

                    _wpIndex += _wpDir;
                    _wpIndex = Mathf.Clamp(_wpIndex, 0, _cachedWaypointWorld.Length - 1);
                }
            }
            else
            {
                _wpIndex = (_wpIndex + 1) % _cachedWaypointWorld.Length;
            }
        }
    }

    // ----------------------
    // Chase
    // ----------------------
    private void TickChase()
    {
        ApplySpeedForState(State.Chase);

        if (!_target)
        {
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);

        // drop target if it runs too far away (or target became invalid)
        if (dist > chaseRange)
        {
            _target = null;
            _state = State.Patrol;
            ApplySpeedForState(_state);
            return;
        }

        if (agent)
        {
            agent.isStopped = false;
            agent.stoppingDistance = stopDistanceForAttack;
            agent.SetDestination(_target.position);
        }

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

        if (dist > attackRange * 1.2f)
        {
            _state = State.Chase;
            ApplySpeedForState(_state);
            return;
        }

        // close in and stop
        if (agent)
        {
            agent.isStopped = false;
            agent.stoppingDistance = stopDistanceForAttack;
            agent.SetDestination(_target.position);

            if (dist <= stopDistanceForAttack + 0.15f)
                agent.isStopped = true;
        }

        // Face target (helps when the model/rig doesn't rotate nicely)
        FaceTarget(_target.position);

        if (Time.time < _nextAttackTime) return;
        _nextAttackTime = Time.time + attackCooldown;

        TriggerAttackAnimation(_target);
    }

    private void FaceTarget(Vector3 targetPos)
    {
        Vector3 to = targetPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion rot = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
    }

    private void TriggerAttackAnimation(Transform t)
    {
        if (!animator || !t) return;

        // Player => pound. Enemy/Ally => swing.
        if (t.CompareTag(playerTag))
        {
            if (!string.IsNullOrWhiteSpace(poundTrigger)) animator.SetTrigger(poundTrigger);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(swingTrigger)) animator.SetTrigger(swingTrigger);
        }
    }

    // ----------------------
    // Targeting
    // ----------------------
    private void TryAcquireAutoTarget()
    {
        if (_target) return;

        if (!autoAggroEnemies && !allowAutoAggroPlayer && !allowAutoAggroAllies)
            return;

        Collider[] hits = Physics.OverlapSphere(transform.position, sightRange, ~0, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestD = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (!c) continue;

            Transform t = c.transform;
            if (!t) continue;

            // Prefer root object
            t = t.root != null ? t.root : t;

            if (t == transform || t.IsChildOf(transform)) continue;

            if (!IsAutoAggroCandidate(t)) continue;

            float d = Vector3.Distance(transform.position, t.position);
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }

        if (best)
            AcquireTarget(best, provoked: false);
    }

    private bool IsAutoAggroCandidate(Transform t)
    {
        if (!t) return false;

        if (autoAggroEnemies && t.CompareTag(enemyTag)) return true;
        if (allowAutoAggroPlayer && t.CompareTag(playerTag)) return true;
        if (allowAutoAggroAllies && t.CompareTag(allyTag)) return true;

        return false;
    }

    private void AcquireTarget(Transform t, bool provoked)
    {
        if (!t) return;

        bool wasIdle = (_target == null);
        _target = t;
        _state = State.Chase;
        ApplySpeedForState(_state);

        // Latch hostile mode + play agro once (if enabled)
        if (animator && !string.IsNullOrWhiteSpace(isAggroBool))
            animator.SetBool(isAggroBool, true);

        bool shouldPlayAggro = playAggroOnce && !_aggroLatched && (provoked || wasIdle);

        if (shouldPlayAggro && animator && !string.IsNullOrWhiteSpace(aggroTrigger))
        {
            animator.SetTrigger(aggroTrigger);
            PlayAggroSfx();
            _aggroLatched = true;
        }
        else if (!playAggroOnce && animator && !string.IsNullOrWhiteSpace(aggroTrigger) && (provoked || wasIdle))
        {
            animator.SetTrigger(aggroTrigger);
            PlayAggroSfx();
        }
    }

    // ----------------------
    // Health callbacks
    // ----------------------
    private void HandleDamaged(AlienBossHealth h, int amount, Transform attacker)
    {
        if (!IsAlive) return;

        // resume from patrol pause
        _patrolWaitUntil = 0f;
        if (agent) agent.isStopped = false;

        if (!alwaysTargetAttacker) return;

        attacker = NormalizeAttacker(attacker);

        // If attacker is missing (common), pick nearest player in chase range as provocation target.
        if (!attacker)
            attacker = FindNearestWithTag(playerTag, chaseRange);

        if (attacker)
            AcquireTarget(attacker, provoked: true);
    }

    private void HandleDied(AlienBossHealth h, Transform killer)
    {
        _state = State.Dead;
        ApplySpeedForState(_state);

        if (agent)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        StopFootsteps();

        if (animator && !string.IsNullOrWhiteSpace(dieTrigger))
            animator.SetTrigger(dieTrigger);

        _target = null;
    }

    // ----------------------
    // Helpers
    // ----------------------
    public void PlayAggroSfx()
    {
        PlayClip(aggroAudioSource, aggroSfx, aggroSfxVolume);
    }

    public void PlayFootstepSfx()
    {
        PlayClip(footstepAudioSource, footstepSfx, footstepSfxVolume);
    }

    private void UpdateFootsteps()
    {
        if (!agent || !footstepSfx)
            return;

        if (_state == State.Dead)
        {
            StopFootsteps();
            return;
        }

        bool hasPath = !requireAgentPathForFootsteps || (agent.enabled && agent.hasPath);
        bool moving = agent.enabled && !agent.isStopped && hasPath && agent.velocity.magnitude >= minVelocityForFootsteps;

        if (!moving)
        {
            _nextFootstepTime = 0f;
            return;
        }

        float interval = (_state == State.Patrol) ? walkFootstepInterval : runFootstepInterval;
        interval = Mathf.Max(0.05f, interval);

        if (_nextFootstepTime <= 0f)
            _nextFootstepTime = Time.time + interval;

        if (Time.time >= _nextFootstepTime)
        {
            PlayFootstepSfx();
            _nextFootstepTime = Time.time + interval;
        }
    }

    private void StopFootsteps()
    {
        _nextFootstepTime = 0f;
    }

    private void PlayClip(AudioSource src, AudioClip clip, float volume)
    {
        if (!clip) return;

        if (!src)
        {
            src = GetComponent<AudioSource>();
            if (!src) src = GetComponentInChildren<AudioSource>();
        }

        if (src)
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private Transform NormalizeAttacker(Transform t)
    {
        if (!t) return null;

        // Bullets often pass a child (gun/camera). Prefer the top-most root.
        Transform r = t.root != null ? t.root : t;

        // If still nested oddly, climb to top
        while (r.parent != null) r = r.parent;

        return r;
    }

    private Transform FindNearestWithTag(string tag, float maxRange)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        GameObject[] objs;
        try { objs = GameObject.FindGameObjectsWithTag(tag); }
        catch { return null; }

        if (objs == null || objs.Length == 0) return null;

        Transform best = null;
        float bestD = float.MaxValue;

        Vector3 p = transform.position;

        for (int i = 0; i < objs.Length; i++)
        {
            var go = objs[i];
            if (!go) continue;

            float d = Vector3.Distance(p, go.transform.position);
            if (d <= maxRange && d < bestD)
            {
                bestD = d;
                best = go.transform;
            }
        }

        return best;
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
