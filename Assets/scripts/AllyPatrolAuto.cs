using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Patrol that requires ZERO scene objects per ally.
/// - AutoOffsets: patrols between two positions computed from spawn + local offsets (default).
/// - UseTransforms: optional if you DO want explicit points.
/// 
/// Extra: while patrolling, forces Animator walk/run bools (isWalking=true, isRunning=false)
/// and sets NavMeshAgent.speed to patrolSpeed (e.g., 2).
/// 
/// NOTE: Does not touch AllyController / team / command systems.
/// </summary>
[DisallowMultipleComponent]
public class AllyPatrolAuto : MonoBehaviour
{
    public enum PatrolMode { AutoOffsets, UseTransforms }

    [Header("Mode")]
    public PatrolMode mode = PatrolMode.AutoOffsets;

    [Header("Auto Offsets (no objects needed)")]
    public Vector3 localOffsetA = new Vector3(0f, 0f, 12f);
    public Vector3 localOffsetB = new Vector3(0f, 0f, -12f);
    public bool randomizeYawOnStart = false;
    [Range(0f, 360f)] public float randomYawRange = 360f;

    [Header("Transform Points (optional)")]
    public Transform pointA;
    public Transform pointB;

    [Header("Timing")]
    public float arriveDistance = 0.8f;
    public float waitSeconds = 0.5f;

    [Header("Walk Speed")]
    [Tooltip("NavMeshAgent.speed while patrolling (you wanted 2).")]
    public float patrolSpeed = 2.0f;

    public bool restoreSpeedOnDisable = true;

    [Header("Animator (optional)")]
    [Tooltip("If assigned, we will force these bools while patrolling.")]
    public Animator animator;
    public string walkBool = "isWalking";
    public string runBool = "isRunning";
    public bool forceWalkBoolsWhilePatrolling = true;

    [Header("Enable/Disable")]
    public bool patrolEnabledOnStart = true;

    private NavMeshAgent _agent;
    private float _originalSpeed;

    private Vector3 _spawnPos;
    private Quaternion _spawnRot;
    private Quaternion _offsetRot;

    private Vector3 _worldA;
    private Vector3 _worldB;
    private Vector3 _currentTarget;

    private float _waitTimer;
    private bool _initialized;
    private bool _warnedMissingPoints;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Debug.LogError($"{name}: AllyPatrolAuto requires a NavMeshAgent.");
            enabled = false;
            return;
        }

        _originalSpeed = _agent.speed;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        Initialize();
        if (patrolEnabledOnStart) SetPatrolEnabled(true);
    }

    private void Initialize()
    {
        if (_initialized) return;

        _spawnPos = transform.position;
        _spawnRot = transform.rotation;

        _offsetRot = Quaternion.identity;
        if (randomizeYawOnStart && mode == PatrolMode.AutoOffsets)
        {
            float yaw = Random.Range(-randomYawRange * 0.5f, randomYawRange * 0.5f);
            _offsetRot = Quaternion.Euler(0f, yaw, 0f);
        }

        RecomputeWorldPoints();
        _initialized = true;
    }

    private void RecomputeWorldPoints()
    {
        // If user accidentally left Mode=UseTransforms but didn't assign points,
        // fall back to AutoOffsets instead of doing nothing.
        if (mode == PatrolMode.UseTransforms && (pointA == null || pointB == null))
        {
            if (!_warnedMissingPoints)
            {
                Debug.LogWarning($"{name}: AllyPatrolAuto is set to UseTransforms but PointA/PointB are not assigned. Falling back to AutoOffsets.");
                _warnedMissingPoints = true;
            }
            mode = PatrolMode.AutoOffsets;
        }

        if (mode == PatrolMode.UseTransforms)
        {
            _worldA = pointA.position;
            _worldB = pointB.position;
            return;
        }

        // AutoOffsets
        Vector3 a = _spawnRot * (_offsetRot * localOffsetA);
        Vector3 b = _spawnRot * (_offsetRot * localOffsetB);

        _worldA = SampleToNavMesh(_spawnPos + a, 4f);
        _worldB = SampleToNavMesh(_spawnPos + b, 4f);
    }

    private Vector3 SampleToNavMesh(Vector3 pos, float maxDist)
    {
        if (NavMesh.SamplePosition(pos, out var hit, maxDist, NavMesh.AllAreas))
            return hit.position;
        return pos;
    }

    private void Update()
    {
        if (!patrolEnabledOnStart) return;
        if (_agent == null) return;

        // Wait logic
        if (_waitTimer > 0f)
        {
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
                SetNextDestination();
            return;
        }

        // Ensure we have a path
        if (!_agent.hasPath && !_agent.pathPending)
        {
            SetNextDestination();
            return;
        }

        // Arrived?
        float arrive = Mathf.Max(arriveDistance, _agent.stoppingDistance + 0.05f);
        if (!_agent.pathPending && _agent.remainingDistance <= arrive)
        {
            if (_agent.velocity.sqrMagnitude < 0.01f)
            {
                _waitTimer = waitSeconds;
                _currentTarget = NearlyEqual(_currentTarget, _worldA) ? _worldB : _worldA;
            }
        }
    }

    private void LateUpdate()
    {
        if (!patrolEnabledOnStart) return;
        if (!forceWalkBoolsWhilePatrolling) return;
        if (animator == null) return;

        // Force walk bools while patrolling (overrides locomotion scripts that might set running).
        animator.SetBool(walkBool, true);
        animator.SetBool(runBool, false);
    }

    private bool NearlyEqual(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude < 0.01f;
    }

    private void SetNextDestination()
    {
        Initialize();
        RecomputeWorldPoints();

        if (_currentTarget == Vector3.zero)
        {
            float da = (transform.position - _worldA).sqrMagnitude;
            float db = (transform.position - _worldB).sqrMagnitude;
            _currentTarget = (da <= db) ? _worldA : _worldB;
        }

        _agent.speed = patrolSpeed;         // <- your requirement (2)
        _agent.isStopped = false;
        _agent.SetDestination(_currentTarget);
    }

    public void SetPatrolEnabled(bool enabled)
    {
        Initialize();
        patrolEnabledOnStart = enabled;

        if (!enabled)
        {
            _waitTimer = 0f;

            if (restoreSpeedOnDisable && _agent != null)
                _agent.speed = _originalSpeed;

            return;
        }

        _currentTarget = Vector3.zero;
        _waitTimer = 0f;
        SetNextDestination();
    }
}
