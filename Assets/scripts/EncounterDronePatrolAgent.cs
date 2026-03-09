using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// EncounterDronePatrolAgent (v5)
/// - Supports encounter-provided patrol points
/// - Can auto-generate random patrol points
/// - Keeps runtime waypoints in world space (not parented under the moving drone)
/// - IMPORTANT: runtime waypoints are stored on/near the NavMesh ground, NOT at the drone hover height.
///   The DroneEnemyController handles hovering via NavMeshAgent.baseOffset / hoverHeight.
/// </summary>
[DisallowMultipleComponent]
public class EncounterDronePatrolAgent : MonoBehaviour
{
    [Header("Mode")]
    public bool useEncounterProvidedPoints = true;
    public bool autoGenerateRandomPatrolOnStart = true;

    [Header("Runtime Waypoint Creation")]
    public bool createRuntimeWaypoints = true;
    public string waypointContainerName = "EncounterWaypoints";
    public bool pingPong = true;

    [Header("Random Patrol")]
    public int randomPointCount = 4;
    public float randomPatrolRadius = 18f;
    public bool snapRandomPointsToNavMesh = true;
    public float randomPointNavMeshSampleRadius = 20f;
    public float minSpacingBetweenRandomPoints = 4f;
    public int maxRandomPointAttemptsPerPoint = 12;

    [Header("Waypoint Height")]
    [Tooltip("If true, runtime patrol points are kept on the NavMesh / ground height. This is the recommended setting for NavMesh drones.")]
    public bool keepWaypointsOnGround = true;

    [Tooltip("If keepWaypointsOnGround is false, add this offset to the incoming/generated point Y.")]
    public float altitudeOffset = 0f;

    [Header("Debug")]
    public bool logPatrolSetup = false;

    private DroneEnemyController _drone;
    private Transform _container;
    private readonly List<Transform> _runtimePoints = new List<Transform>(16);
    private Vector3 _anchorPosition;
    private bool _receivedEncounterPoints;

    private void Awake()
    {
        _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) _drone = GetComponentInParent<DroneEnemyController>();
        _anchorPosition = transform.position;
        EnsureContainer();
    }

    private void Start()
    {
        if (_drone == null) return;

        bool hasExistingWaypoints = _drone.waypoints != null && _drone.waypoints.Length > 0;
        if (!_receivedEncounterPoints && !hasExistingWaypoints && autoGenerateRandomPatrolOnStart)
            GenerateRandomPatrol();
    }

    private void EnsureContainer()
    {
        if (!createRuntimeWaypoints) return;
        if (_container != null) return;

        GameObject existing = GameObject.Find(waypointContainerName + "_" + gameObject.GetInstanceID());
        if (existing != null)
        {
            _container = existing.transform;
            return;
        }

        var go = new GameObject(waypointContainerName + "_" + gameObject.GetInstanceID());
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        _container = go.transform;
    }

    private void ClearRuntimePoints()
    {
        for (int i = 0; i < _runtimePoints.Count; i++)
        {
            if (_runtimePoints[i] != null)
                Destroy(_runtimePoints[i].gameObject);
        }
        _runtimePoints.Clear();
    }

    private void ApplyRuntimePointsToDrone()
    {
        if (_drone == null) return;

        _drone.waypoints = _runtimePoints.ToArray();
        _drone.pingPong = pingPong;
        _drone.ClearCombatTarget();
        _drone.state = DroneEnemyController.DroneState.Patrol;

        if (logPatrolSetup)
            Debug.Log($"[EncounterDronePatrolAgent] Applied {_runtimePoints.Count} patrol points to {_drone.name}.", this);
    }

    private Transform CreateWaypoint(string name, Vector3 worldPos)
    {
        var wp = new GameObject(name);
        wp.transform.SetParent(_container, true);
        wp.transform.position = worldPos;
        return wp.transform;
    }

    private Vector3 ResolveWaypointPosition(Vector3 input)
    {
        Vector3 p = input;

        if (keepWaypointsOnGround)
        {
            float sampleRadius = Mathf.Max(1f, randomPointNavMeshSampleRadius);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                p = hit.position;
        }
        else
        {
            p.y += altitudeOffset;
        }

        return p;
    }

    public void GenerateRandomPatrol()
    {
        if (_drone == null || !createRuntimeWaypoints)
            return;

        EnsureContainer();
        ClearRuntimePoints();

        int count = Mathf.Max(2, randomPointCount);
        Vector3 center = _anchorPosition;

        for (int i = 0; i < count; i++)
        {
            Vector3 chosen = center;
            bool found = false;

            for (int attempt = 0; attempt < Mathf.Max(1, maxRandomPointAttemptsPerPoint); attempt++)
            {
                Vector2 circle = Random.insideUnitCircle * Mathf.Max(1f, randomPatrolRadius);
                Vector3 candidate = new Vector3(center.x + circle.x, center.y, center.z + circle.y);

                if (snapRandomPointsToNavMesh)
                {
                    if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(0.5f, randomPointNavMeshSampleRadius), NavMesh.AllAreas))
                        candidate = hit.position;
                    else
                        continue;
                }

                candidate = ResolveWaypointPosition(candidate);

                if (IsFarEnoughFromExisting(candidate))
                {
                    chosen = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                float angle = (360f / count) * i * Mathf.Deg2Rad;
                Vector3 fallback = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(2f, randomPatrolRadius * 0.65f);
                chosen = ResolveWaypointPosition(fallback);
            }

            _runtimePoints.Add(CreateWaypoint($"RandomWP_{i}", chosen));
        }

        ApplyRuntimePointsToDrone();
    }

    private bool IsFarEnoughFromExisting(Vector3 candidate)
    {
        float minSq = Mathf.Max(0f, minSpacingBetweenRandomPoints) * Mathf.Max(0f, minSpacingBetweenRandomPoints);

        for (int i = 0; i < _runtimePoints.Count; i++)
        {
            if (_runtimePoints[i] == null) continue;

            Vector3 a = _runtimePoints[i].position;
            Vector3 b = candidate;
            a.y = 0f;
            b.y = 0f;

            if ((a - b).sqrMagnitude < minSq)
                return false;
        }

        return true;
    }

    public void Encounter_SetPatrolPoints(Vector3[] points)
    {
        if (_drone == null) _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) return;

        _receivedEncounterPoints = points != null && points.Length > 0;

        if (points == null || points.Length == 0)
        {
            if (autoGenerateRandomPatrolOnStart)
                GenerateRandomPatrol();
            else
                _drone.waypoints = null;
            return;
        }

        if (!createRuntimeWaypoints) return;

        EnsureContainer();
        ClearRuntimePoints();

        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = ResolveWaypointPosition(points[i]);
            _runtimePoints.Add(CreateWaypoint($"WP_{i}", p));
        }

        ApplyRuntimePointsToDrone();
    }

    public void Encounter_SetBehavior(EncounterBehavior behavior)
    {
        if (_drone == null) _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) return;

        if (behavior.ToString() == "Patrol")
        {
            if ((_drone.waypoints == null || _drone.waypoints.Length == 0) && autoGenerateRandomPatrolOnStart)
                GenerateRandomPatrol();

            _drone.ClearCombatTarget();
            _drone.state = DroneEnemyController.DroneState.Patrol;
            return;
        }

        _drone.ClearCombatTarget();
        _drone.waypoints = null;
        _drone.state = DroneEnemyController.DroneState.Patrol;
    }

    public void RegenerateRandomPatrolNow()
    {
        GenerateRandomPatrol();
    }
}
