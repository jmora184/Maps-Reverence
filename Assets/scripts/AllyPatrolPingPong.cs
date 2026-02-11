using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Ping-pong patrol that is resilient to other scripts overwriting agent speed/rotation.
/// - Walks patrolDistance forward from spawn, then back.
/// - While patrolling: HARD-locks NavMeshAgent.speed = walkSpeed (Update + LateUpdate).
/// - While patrolling: HARD-locks Animator bools: isWalking=true, isRunning=false (Update + LateUpdate).
/// - While patrolling: disables agent.updateRotation and rotates the visual model toward movement.
/// - When patrol is disabled OR component is disabled: restores agent.updateRotation and sets speed to runSpeed,
///   and releases animator bools (sets isWalking=false, isRunning=false once).
/// </summary>
[DisallowMultipleComponent]
public class AllyPatrolPingPong : MonoBehaviour
{
    [Header("Patrol")]
    public float patrolDistance = 20f;
    public float arriveDistance = 0.8f;
    public float waitSeconds = 0.35f;
    public float navMeshSampleRadius = 4f;

    [Header("Speeds")]
    public float walkSpeed = 2f;
    public float runSpeed = 10f;

    [Header("Turning")]
    [Tooltip("Rotate the VISUAL (model) toward movement so it visibly turns around.")]
    public Transform visualRoot;
    [Tooltip("Rotation speed while patrolling.")]
    public float turnSpeed = 12f;

    [Header("Animator (optional)")]
    public Animator animator;
    public string walkBool = "isWalking";
    public string runBool = "isRunning";

    [Header("Enable/Disable")]
    public bool patrolEnabledOnStart = true;

    private NavMeshAgent _agent;

    private Vector3 _startPos;
    private Quaternion _startRot;
    private Vector3 _endA;
    private Vector3 _endB;
    private Vector3 _currentTarget;

    private float _waitTimer;
    private bool _initialized;

    // Restore values
    private float _originalSpeed;
    private bool _originalUpdateRotation;
    private float _originalAngularSpeed;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogError($"{name}: AllyPatrolPingPong requires a NavMeshAgent.");
            enabled = false;
            return;
        }

        _originalSpeed = _agent.speed;
        _originalUpdateRotation = _agent.updateRotation;
        _originalAngularSpeed = _agent.angularSpeed;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (visualRoot == null)
        {
            // Prefer the animator transform (usually the model root), else first child, else self.
            if (animator != null) visualRoot = animator.transform;
            else if (transform.childCount > 0) visualRoot = transform.GetChild(0);
            else visualRoot = transform;
        }
    }

    private void Start()
    {
        InitializeEndpoints();

        if (patrolEnabledOnStart) SetPatrolEnabled(true);
        else ApplyRunState();
    }

    private void OnDisable()
    {
        // If the component is disabled (or destroyed), restore run state so we don't leave the ally "stuck walking".
        ApplyRunState();
        ReleaseAnimatorBools();
    }

    private void InitializeEndpoints()
    {
        if (_initialized) return;

        _startPos = transform.position;
        _startRot = transform.rotation;

        RecomputeEndpoints();

        // pick nearer
        float da = (transform.position - _endA).sqrMagnitude;
        float db = (transform.position - _endB).sqrMagnitude;
        _currentTarget = (da <= db) ? _endA : _endB;

        _initialized = true;
    }

    private void RecomputeEndpoints()
    {
        Vector3 forward = _startRot * Vector3.forward;
        Vector3 rawA = _startPos + forward * patrolDistance;
        Vector3 rawB = _startPos - forward * patrolDistance;

        _endA = SampleToNavMesh(rawA);
        _endB = SampleToNavMesh(rawB);

        // If sampling collapses both points to nearly the same spot (common when near navmesh edges),
        // fall back to a sideways patrol line so we still ping-pong.
        if ((_endA - _endB).sqrMagnitude < 1.0f)
        {
            Vector3 right = _startRot * Vector3.right;
            rawA = _startPos + right * patrolDistance;
            rawB = _startPos - right * patrolDistance;
            _endA = SampleToNavMesh(rawA);
            _endB = SampleToNavMesh(rawB);
        }
    }

    private Vector3 SampleToNavMesh(Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
            return hit.position;
        return pos;
    }

    private void Update()
    {
        if (!patrolEnabledOnStart) return;
        if (_agent == null) return;

        // Ensure patrol keeps control even if other scripts fight it.
        ForceWalkStateHard();

        if (_waitTimer > 0f)
        {
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
                GoToCurrentTarget();
            return;
        }

        // If no path, set one.
        if (!_agent.hasPath && !_agent.pathPending)
        {
            GoToCurrentTarget();
        }

        // Arrived check (use world distance to target to avoid remainingDistance weirdness)
        float dist = Vector3.Distance(transform.position, _currentTarget);
        if (dist <= arriveDistance)
        {
            // Flip and wait once we're basically stopped
            if (_agent.velocity.sqrMagnitude < 0.02f)
            {
                _waitTimer = waitSeconds;
                _currentTarget = NearlyEqual(_currentTarget, _endA) ? _endB : _endA;
            }
        }
        else
        {
            // If we have a target but agent isn't moving, re-issue destination
            if (!_agent.pathPending && _agent.velocity.sqrMagnitude < 0.0004f)
                GoToCurrentTarget();
        }
    }

    private void LateUpdate()
    {
        if (!patrolEnabledOnStart) return;
        ForceWalkStateHard();
        RotateVisualTowardMovement();
    }

    private void ForceWalkStateHard()
    {
        // Hard-lock agent speed
        if (_agent.isStopped) _agent.isStopped = false;
        if (_agent.speed != walkSpeed) _agent.speed = walkSpeed;

        // Hard-lock animator bools
        if (animator != null)
        {
            animator.SetBool(walkBool, true);
            animator.SetBool(runBool, false); // ALWAYS false while patrolling
        }
    }

    private void ReleaseAnimatorBools()
    {
        // Release to allow your locomotion script to drive the animator again.
        if (animator != null)
        {
            animator.SetBool(walkBool, false);
            animator.SetBool(runBool, false);
        }
    }

    private void RotateVisualTowardMovement()
    {
        if (visualRoot == null) return;

        // Prefer desiredVelocity; if it's tiny, face the target point.
        Vector3 dir = _agent.desiredVelocity;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = (_currentTarget - transform.position);
            dir.y = 0f;
        }

        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    private bool NearlyEqual(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.01f;

    private void GoToCurrentTarget()
    {
        InitializeEndpoints();

        // If patrolDistance changed at runtime, recompute endpoints.
        RecomputeEndpoints();

        // Ensure current target is one of the endpoints.
        if (!NearlyEqual(_currentTarget, _endA) && !NearlyEqual(_currentTarget, _endB))
        {
            float da = (transform.position - _endA).sqrMagnitude;
            float db = (transform.position - _endB).sqrMagnitude;
            _currentTarget = (da <= db) ? _endA : _endB;
        }

        _agent.isStopped = false;
        _agent.speed = walkSpeed;
        _agent.SetDestination(_currentTarget);
    }

    private void ApplyRunState()
    {
        if (_agent == null) return;

        // Restore agent rotation behavior and speed.
        _agent.updateRotation = _originalUpdateRotation;
        _agent.angularSpeed = _originalAngularSpeed;

        // Use requested run speed (10) when NOT patrolling.
        _agent.speed = runSpeed > 0f ? runSpeed : _originalSpeed;
    }

    public void SetPatrolEnabled(bool enabled)
    {
        InitializeEndpoints();
        patrolEnabledOnStart = enabled;

        if (!enabled)
        {
            _waitTimer = 0f;
            ApplyRunState();
            ReleaseAnimatorBools();
            return;
        }

        _waitTimer = 0f;

        // While patrolling, we control rotation via visualRoot
        _agent.updateRotation = false;
        _agent.angularSpeed = 999f;

        // Pick nearer endpoint and go
        float da = (transform.position - _endA).sqrMagnitude;
        float db = (transform.position - _endB).sqrMagnitude;
        _currentTarget = (da <= db) ? _endA : _endB;

        _agent.isStopped = false;
        _agent.speed = walkSpeed;
        _agent.SetDestination(_currentTarget);

        ForceWalkStateHard();
    }
}
