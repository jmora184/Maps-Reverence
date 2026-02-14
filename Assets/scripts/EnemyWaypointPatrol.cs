using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy waypoint patrol (v4) - mirrors AllyPatrolPingPong behavior.
///
/// Key fixes vs earlier attempts:
/// - Does NOT rely on NavMeshAgent.hasPath to advance.
///   If the current waypoint is at/near the enemy's current position (common when using Spawn_ points),
///   NavMeshAgent may report hasPath=false and velocity=0 (because you're already "there").
///   We instead use world-distance arrival checks, so it will immediately advance to the next waypoint.
/// - Re-issues SetDestination only when needed (no path and not pending).
/// - Optional ping-pong or loop.
/// - Optional pause at waypoint.
/// </summary>
[DisallowMultipleComponent]
public class EnemyWaypointPatrol : MonoBehaviour
{
    [Header("Waypoints")]
    public Transform[] waypoints;

    [Tooltip("If true: 0->1->2->...->last->...->1->0. If false: loop 0->1->...->last->0.")]
    public bool pingPong = true;

    [Header("Patrol Settings")]
    public bool patrolEnabledOnStart = true;

    [Tooltip("Arrived when world distance to waypoint <= this.")]
    public float arriveDistance = 0.8f;

    [Tooltip("Optional wait at each waypoint (seconds).")]
    public float waitSecondsAtPoint = 0f;

    [Tooltip("Optional patrol speed override. Set <=0 to keep agent's existing speed.")]
    public float patrolSpeedOverride = 2f;

    [Header("NavMesh Safety")]
    [Tooltip("If the agent spawns off the NavMesh, warp it to the closest point.")]
    public bool autoWarpToNavMeshOnEnable = true;

    [Tooltip("Radius used for sampling the agent position onto the NavMesh.")]
    public float warpSampleRadius = 20f;

    [Tooltip("Radius used for sampling waypoint positions onto the NavMesh before setting destination.")]
    public float waypointSampleRadius = 12f;

    [Header("Debug")]
    public bool drawGizmos = true;
    public bool verboseLogs = false;

    // Public compatibility hooks
    public bool PatrolEnabled => _enabled;
    public bool IsPatrolling => _enabled && waypoints != null && waypoints.Length > 0;

    private NavMeshAgent _agent;
    private bool _enabled;

    private int _index;
    private int _dir = 1;
    private float _waitUntil = -1f;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        SetPatrolEnabled(patrolEnabledOnStart);
    }

    public void SetPatrolEnabled(bool enabled)
    {
        _enabled = enabled;

        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        if (_agent == null)
        {
            Log("No NavMeshAgent found.");
            _enabled = false;
            return;
        }

        if (!_enabled)
        {
            _waitUntil = -1f;
            return;
        }

        if (waypoints == null || waypoints.Length == 0)
        {
            Log("No waypoints assigned.");
            _enabled = false;
            return;
        }

        // Ensure agent is on NavMesh.
        if (autoWarpToNavMeshOnEnable && !_agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, warpSampleRadius, NavMesh.AllAreas))
            {
                bool warped = _agent.Warp(hit.position);
                Log($"Warped to NavMesh -> {warped} at {hit.position}");
            }
            else
            {
                Log("Agent is off NavMesh and SamplePosition failed. Is your NavMesh baked?");
            }
        }

        // Start at closest waypoint.
        _index = FindClosestWaypointIndex();
        _dir = 1;
        _waitUntil = -1f;

        // If we are already basically at that waypoint (common with Spawn_ points), advance immediately.
        if (AtCurrentWaypoint())
            AdvanceIndex();

        GoToCurrentTarget();
    }

    private void Update()
    {
        if (!_enabled) return;
        if (_agent == null || !_agent.enabled) return;
        if (waypoints == null || waypoints.Length == 0) return;
        if (!_agent.isOnNavMesh) return;

        _agent.isStopped = false;
        _agent.updateRotation = true;

        if (patrolSpeedOverride > 0f)
            _agent.speed = patrolSpeedOverride;

        // Waiting at a waypoint.
        if (_waitUntil > 0f)
        {
            if (Time.time >= _waitUntil)
            {
                _waitUntil = -1f;
                AdvanceIndex();
                GoToCurrentTarget();
            }
            return;
        }

        // Arrival check via world distance (robust even when hasPath is false).
        if (AtCurrentWaypoint())
        {
            if (waitSecondsAtPoint > 0f)
            {
                _waitUntil = Time.time + waitSecondsAtPoint;
                return;
            }

            AdvanceIndex();
            GoToCurrentTarget();
            return;
        }

        // If no path, set one.
        if (!_agent.hasPath && !_agent.pathPending)
        {
            GoToCurrentTarget();
        }
    }

    private bool AtCurrentWaypoint()
    {
        Transform t = GetCurrentWaypoint();
        if (t == null) return true;

        Vector3 a = transform.position;
        Vector3 b = t.position;
        a.y = 0f; b.y = 0f;

        float dist = Vector3.Distance(a, b);
        float thresh = Mathf.Max(0.05f, arriveDistance);

        return dist <= thresh;
    }

    private void GoToCurrentTarget()
    {
        Transform t = GetCurrentWaypoint();
        if (t == null) return;

        Vector3 goal = t.position;

        // Snap goal to NavMesh for reliability.
        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, waypointSampleRadius, NavMesh.AllAreas))
            goal = hit.position;

        _agent.SetDestination(goal);

        Log($"SetDestination -> {goal} (wp index {_index}) hasPath={_agent.hasPath} pending={_agent.pathPending}");
    }

    private Transform GetCurrentWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return null;
        int safe = Mathf.Clamp(_index, 0, waypoints.Length - 1);
        return waypoints[safe];
    }

    private void AdvanceIndex()
    {
        if (waypoints == null || waypoints.Length <= 1) return;

        if (pingPong)
        {
            if (_index <= 0) _dir = 1;
            if (_index >= waypoints.Length - 1) _dir = -1;
            _index += _dir;
        }
        else
        {
            _index = (_index + 1) % waypoints.Length;
        }
    }

    private int FindClosestWaypointIndex()
    {
        Vector3 p = transform.position; p.y = 0f;
        int best = 0;
        float bestD = float.PositiveInfinity;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Vector3 w = waypoints[i].position; w.y = 0f;
            float d = (w - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    private void Log(string msg)
    {
        if (!verboseLogs) return;
        Debug.Log($"[EnemyWaypointPatrol] {name}: {msg}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || waypoints == null) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 0.25f);

            int j = i + 1;
            if (j < waypoints.Length && waypoints[j] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[j].position);
        }
    }
}
