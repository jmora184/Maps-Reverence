using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EncounterDronePatrolAgent (v3)
/// Fixes compile errors by NOT referencing EncounterBehavior values that may not exist in your project.
/// Many projects only define EncounterBehavior.Patrol (and maybe a couple others).
///
/// This agent now:
/// - Treats Patrol as "enable drone patrol/waypoints"
/// - Treats ANY other behavior value as "stop patrol and hover" (safe default)
///
/// Messages supported:
/// - Encounter_SetPatrolPoints(Vector3[] points)
/// - Encounter_SetBehavior(EncounterBehavior behavior)
/// </summary>
[DisallowMultipleComponent]
public class EncounterDronePatrolAgent : MonoBehaviour
{
    [Header("Waypoint Creation")]
    public bool createRuntimeWaypoints = true;
    public string waypointContainerName = "EncounterWaypoints";
    public bool pingPong = true;

    [Header("Altitude")]
    public bool keepCurrentAltitude = true;
    public float altitudeOffset = 0f;

    private DroneEnemyController _drone;
    private Transform _container;
    private readonly List<Transform> _runtimePoints = new List<Transform>(16);

    private void Awake()
    {
        _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) _drone = GetComponentInParent<DroneEnemyController>();
        EnsureContainer();
    }

    private void EnsureContainer()
    {
        if (!createRuntimeWaypoints) return;
        if (_container != null) return;

        var existing = transform.Find(waypointContainerName);
        if (existing != null) { _container = existing; return; }

        var go = new GameObject(waypointContainerName);
        go.transform.SetParent(transform, false);
        _container = go.transform;
    }

    // Called by EncounterDirectorPOC via SendMessage
    public void Encounter_SetPatrolPoints(Vector3[] points)
    {
        if (_drone == null) _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) return;

        if (points == null || points.Length == 0)
        {
            _drone.waypoints = null;
            return;
        }

        if (!createRuntimeWaypoints) return;

        EnsureContainer();

        // Clear old runtime points
        for (int i = 0; i < _runtimePoints.Count; i++)
        {
            if (_runtimePoints[i] != null)
                Destroy(_runtimePoints[i].gameObject);
        }
        _runtimePoints.Clear();

        float y = _drone.transform.position.y + altitudeOffset;

        for (int i = 0; i < points.Length; i++)
        {
            var wp = new GameObject($"WP_{i}");
            wp.transform.SetParent(_container, false);

            Vector3 p = points[i];
            if (keepCurrentAltitude) p.y = y;

            wp.transform.position = p;
            _runtimePoints.Add(wp.transform);
        }

        _drone.waypoints = _runtimePoints.ToArray();
        _drone.pingPong = pingPong;

        // Restart patrol cleanly
        _drone.ClearCombatTarget();
        _drone.state = DroneEnemyController.DroneState.Patrol;
    }

    // Called by EncounterDirectorPOC via SendMessage
    public void Encounter_SetBehavior(EncounterBehavior behavior)
    {
        if (_drone == null) _drone = GetComponentInChildren<DroneEnemyController>();
        if (_drone == null) return;

        // Only handle Patrol explicitly. Anything else -> stop patrolling (hover).
        if (behavior.ToString() == "Patrol") // safest across enum variants
        {
            _drone.ClearCombatTarget();
            _drone.state = DroneEnemyController.DroneState.Patrol;
            return;
        }

        // Default: hover in place
        _drone.ClearCombatTarget();
        _drone.waypoints = null;
        _drone.state = DroneEnemyController.DroneState.Patrol;
    }
}
