using UnityEngine;
using UnityEngine.AI;

/// NPC controller (combat/defend/etc) + dialogue animation helpers.
/// This version:
/// - Prevents NPC from targeting the Player during enemy scans
/// - Forces talking OFF while Hostile/Dead (cannot talk while aggro)
public class NPCController : MonoBehaviour
{
    public enum State { Idle, Hostile, Dead }

    [Header("State")]
    public State state = State.Idle;

    [Header("References")]
    public Animator animator;
    public NavMeshAgent agent;
    public Transform firePoint;
    public GameObject bulletPrefab;

    [Header("Combat")]
    public float aggroRange = 18f;
    public float shootRange = 14f;
    public float fireRate = 0.25f;
    public float faceTargetTurnSpeed = 12f;

    [Header("Weapon (optional)")]
    public bool startWithWeaponHidden = true;
    public bool showWeaponWhenHostile = true;
    public GameObject weaponObject; // ex: w_usp45

    [Header("Defend nearby enemies")]
    public bool defendAgainstEnemies = true;
    public float defendEnemyScanRange = 30f;
    public float defendScanInterval = 0.5f;

    [Tooltip("Layers considered for enemy scan. IMPORTANT: set this to your Enemy layer (recommended).")]
    public LayerMask enemyLayers = ~0;

    [Tooltip("Tag used to identify enemies (fallback + optional strict filtering).")]
    public string enemyTag = "Enemy";

    [Tooltip("If true, scan results must match enemyTag (either on the collider, or on its root). This prevents targeting the Player by mistake.")]
    public bool requireEnemyTagMatch = true;

    [Tooltip("Player tag. Used to explicitly ignore the player in scans.")]
    public string playerTag = "Player";

    [Header("Animator Params (AllyRun compatible)")]
    public string speedParam = "Speed";
    public string isWalkingParam = "isWalking";
    public string isRunningParam = "isRunning";

    [Header("Dialogue Animator Param")]
    [Tooltip("Create a BOOL parameter with this name in the Animator, and use it for AnyState->talking transitions.")]
    public string talkingBoolParam = "Talking";

    [Tooltip("Optional: if your controller uses a Trigger to enter talking, set it here.")]
    public string talkingTriggerParam = "";

    [Header("Tuning")]
    public float runSpeedThreshold = 2.2f;

    // internal
    Transform _target;
    float _nextFireTime;
    float _scanTimer;
    bool _isTalking;

    int _hashSpeed, _hashIsWalking, _hashIsRunning, _hashTalkingBool, _hashTalkingTrigger;
    bool _hasSpeed, _hasIsWalking, _hasIsRunning, _hasTalkingBool, _hasTalkingTrigger;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!agent) agent = GetComponent<NavMeshAgent>();

        CacheAnimatorParams();

        if (animator)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        if (startWithWeaponHidden && weaponObject != null)
            weaponObject.SetActive(false);
    }

    void Update()
    {
        if (state == State.Dead) return;

        UpdateLocomotion();

        // Validate target (destroyed/disabled)
        if (_target == null || !_target.gameObject.activeInHierarchy)
            _target = null;

        // Passive defense scan (only while not already hostile)
        if (defendAgainstEnemies && state != State.Hostile)
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = defendScanInterval;
                TryAcquireEnemyTarget();
            }
        }

        if (state != State.Hostile) return;
        if (_target == null) return;

        float dist = Vector3.Distance(transform.position, _target.position);

        // Lose aggro
        if (dist > aggroRange * 1.35f)
        {
            _target = null;
            return;
        }

        FaceTarget(_target);

        if (dist <= shootRange)
            TryShoot();
    }

    void UpdateLocomotion()
    {
        if (!animator || !agent) return;

        float speed = agent.velocity.magnitude;

        if (_hasSpeed) animator.SetFloat(_hashSpeed, speed);

        bool walking = speed > 0.05f;
        bool running = speed > runSpeedThreshold;

        if (_hasIsWalking) animator.SetBool(_hashIsWalking, walking);
        if (_hasIsRunning) animator.SetBool(_hashIsRunning, running);
    }

    void CacheAnimatorParams()
    {
        if (!animator) return;

        _hasSpeed = _hasIsWalking = _hasIsRunning = _hasTalkingBool = _hasTalkingTrigger = false;

        _hashSpeed = Animator.StringToHash(speedParam);
        _hashIsWalking = Animator.StringToHash(isWalkingParam);
        _hashIsRunning = Animator.StringToHash(isRunningParam);
        _hashTalkingBool = Animator.StringToHash(talkingBoolParam);
        _hashTalkingTrigger = string.IsNullOrWhiteSpace(talkingTriggerParam) ? 0 : Animator.StringToHash(talkingTriggerParam);

        foreach (var p in animator.parameters)
        {
            if (p.name == speedParam && p.type == AnimatorControllerParameterType.Float) _hasSpeed = true;
            if (p.name == isWalkingParam && p.type == AnimatorControllerParameterType.Bool) _hasIsWalking = true;
            if (p.name == isRunningParam && p.type == AnimatorControllerParameterType.Bool) _hasIsRunning = true;

            if (p.name == talkingBoolParam && p.type == AnimatorControllerParameterType.Bool) _hasTalkingBool = true;
            if (!string.IsNullOrWhiteSpace(talkingTriggerParam) && p.name == talkingTriggerParam && p.type == AnimatorControllerParameterType.Trigger) _hasTalkingTrigger = true;
        }
    }

    // ---------------------------
    // Dialogue animation API
    // ---------------------------
    /// Force stops talking regardless of current state (used when going hostile).
    void ForceStopTalking()
    {
        _isTalking = false;

        if (!animator) return;

        if (_hasTalkingBool)
            animator.SetBool(_hashTalkingBool, false);
    }

    /// Sets talking state. IMPORTANT: NPC can only talk while Idle.
    public void SetTalking(bool talking)
    {
        // Can't talk while Hostile/Dead.
        if (talking && state != State.Idle)
            return;

        _isTalking = talking;

        if (!animator) return;

        if (_hasTalkingBool)
            animator.SetBool(_hashTalkingBool, talking);

        // Optional trigger support (fires only on start)
        if (talking && _hasTalkingTrigger)
            animator.SetTrigger(_hashTalkingTrigger);
    }

    public void BeginDialogue()
    {
        if (state != State.Idle) return;
        SetTalking(true);
    }

    public void EndDialogue()
    {
        SetTalking(false);
    }

    // ---------------------------
    // Aggro / damage hooks
    // ---------------------------
    public void BecomeHostile()
    {
        // As soon as we're hostile, stop talking.
        ForceStopTalking();

        state = State.Hostile;
        if (showWeaponWhenHostile && weaponObject != null) weaponObject.SetActive(true);
    }

    public void BecomeHostile(Transform attacker)
    {
        BecomeHostile();
        _target = attacker;
    }

    public void OnTookDamage(Transform attacker)
    {
        if (state == State.Dead) return;
        BecomeHostile(attacker);
    }

    public void OnDeath()
    {
        state = State.Dead;
        _target = null;
        ForceStopTalking();
        if (agent) agent.isStopped = true;
    }

    // Compatibility with older NPCHealth versions
    public void OnDied() => OnDeath();

    // ---------------------------
    // Targeting / combat
    // ---------------------------
    void TryAcquireEnemyTarget()
    {
        Transform best = FindNearestEnemy(defendEnemyScanRange);
        if (best != null)
        {
            _target = best;
            BecomeHostile(); // draw weapon + hostile state + stop talking
        }
    }

    Transform FindNearestEnemy(float range)
    {
        float bestDist = float.MaxValue;
        Transform best = null;

        Collider[] hits = Physics.OverlapSphere(transform.position, range, enemyLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (!col) continue;

            Transform t = col.transform;
            if (!t) continue;

            // Ignore self
            if (t.root == transform.root) continue;

            // Explicitly ignore the player (common cause of "NPC shoots me immediately")
            if (!string.IsNullOrEmpty(playerTag) && (t.CompareTag(playerTag) || t.root.CompareTag(playerTag)))
                continue;

            // Optional strict filtering: require Enemy tag on collider OR root
            if (requireEnemyTagMatch && !string.IsNullOrEmpty(enemyTag))
            {
                bool taggedEnemy = t.CompareTag(enemyTag) || t.root.CompareTag(enemyTag);
                if (!taggedEnemy) continue;
            }

            float d = (t.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t.root; // prefer the root transform as the target
            }
        }

        return best;
    }

    void FaceTarget(Transform t)
    {
        Vector3 to = t.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return;

        Quaternion desired = Quaternion.LookRotation(to);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * faceTargetTurnSpeed);
    }

    void TryShoot()
    {
        if (!bulletPrefab || !firePoint) return;
        if (Time.time < _nextFireTime) return;

        _nextFireTime = Time.time + fireRate;

        GameObject b = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

        // set owner if bullet supports it
        var bullet = b.GetComponent<BulletController>();
        if (bullet != null)
            bullet.owner = transform;
    }
}
