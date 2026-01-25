using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple built-in Patrol AI for quick proof-of-concept testing.
/// - Uses NavMeshAgent to walk between waypoints.
/// - Intended ONLY when you don't already have enemy/ally AI scripts driving movement.
/// Turned on per group via EncounterDirectorPOC.SpawnGroup.useBuiltInAI.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EncounterPatrolAgent : MonoBehaviour
{
    public Transform[] waypoints;
    public float arrivalDistance = 1.5f;
    public float pauseSecondsAtWaypoint = 0.5f;

    private NavMeshAgent _agent;
    private int _index;
    private float _pauseUntil;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    private void OnEnable()
    {
        _index = 0;
        _pauseUntil = 0f;
        TrySetDestination();
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (_agent == null || !_agent.enabled) return;

        if (Time.time < _pauseUntil) return;

        if (!_agent.pathPending && _agent.remainingDistance <= Mathf.Max(arrivalDistance, _agent.stoppingDistance + 0.05f))
        {
            _pauseUntil = Time.time + pauseSecondsAtWaypoint;
            _index = (_index + 1) % waypoints.Length;
            TrySetDestination();
        }
    }

    private void TrySetDestination()
    {
        var wp = waypoints[_index];
        if (wp == null) return;
        _agent.SetDestination(wp.position);
    }

    // Optional hooks if EncounterDirectorPOC uses SendMessage
    public void Encounter_SetBehavior(EncounterBehavior b)
    {
        enabled = (b == EncounterBehavior.Patrol);
    }

    public void Encounter_SetPatrolPoints(Vector3[] points)
    {
        if (points == null || points.Length == 0) return;

        // Create runtime waypoints as hidden children so we can keep Transform[] API.
        var holder = new GameObject("RuntimePatrolPoints");
        holder.hideFlags = HideFlags.HideInHierarchy;
        holder.transform.SetParent(transform, false);

        waypoints = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var t = new GameObject($"P{i}").transform;
            t.SetParent(holder.transform, false);
            t.position = points[i];
            waypoints[i] = t;
        }
    }
}
