using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Automatic player-side ally push helper.
/// Put this on the PLAYER.
///
/// Behavior:
/// - No button press required.
/// - While the player is physically moving into a nearby ally, that ally is gently pushed
///   in the SAME direction the player is moving.
/// - Works for forward, backward, left, right, and diagonals.
/// - Does not require changes to AllyController.
///
/// Notes:
/// - Uses NavMeshAgent.Warp when possible to stay friendly with agent-driven movement.
/// - Intended as a gentle "get out of the way" helper, not physics shoving.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAllyPushAnyDirection : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("How close an ally must be to be considered pushable.")]
    [SerializeField] private float searchRadius = 1.7f;

    [Tooltip("How close to the player's movement lane the ally must be.")]
    [SerializeField] private float laneWidth = 1.15f;

    [Tooltip("Optional layer filter for ally detection. Leave as Everything if unsure.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [Header("Push")]
    [Tooltip("How far each push step moves the ally.")]
    [SerializeField] private float pushStepDistance = 0.3f;

    [Tooltip("NavMesh sample radius for the target push point.")]
    [SerializeField] private float navMeshSampleRadius = 2f;

    [Tooltip("Minimum seconds between pushes on the same ally.")]
    [SerializeField] private float perAllyPushInterval = 0.05f;

    [Tooltip("Minimum seconds between any two push attempts.")]
    [SerializeField] private float globalPushInterval = 0.015f;

    [Header("Player Movement Gate")]
    [Tooltip("Minimum actual player movement speed required to push allies.")]
    [SerializeField] private float minPlayerMoveSpeed = 0.08f;

    [Tooltip("If true, we only push allies that are roughly in the direction the player is moving.")]
    [SerializeField] private bool requireAllyInMoveDirection = true;

    [Range(-1f, 1f)]
    [Tooltip("Dot threshold for how aligned the ally must be with player movement. Lower allows wider push angles.")]
    [SerializeField] private float moveDirectionDotThreshold = -0.15f;

    [Header("Optional Filters")]
    [Tooltip("If true, only objects tagged Ally are considered.")]
    [SerializeField] private bool requireAllyTag = false;

    [SerializeField] private string allyTag = "Ally";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawDebug = false;

    private readonly Collider[] _hits = new Collider[24];
    private readonly Dictionary<int, float> _nextAllowedPushTimeByAlly = new Dictionary<int, float>();

    private Vector3 _lastPosition;
    private bool _hasLastPosition;
    private float _nextGlobalPushTime;

    private void Awake()
    {
        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private void Update()
    {
        Vector3 moveDir;
        float moveSpeed;
        GetPlayerMovement(out moveDir, out moveSpeed);

        if (Time.time < _nextGlobalPushTime)
        {
            CachePosition();
            return;
        }

        if (moveSpeed < Mathf.Max(0.001f, minPlayerMoveSpeed) || moveDir.sqrMagnitude < 0.0001f)
        {
            CachePosition();
            return;
        }

        Transform ally = FindBestPushableAlly(moveDir);
        if (ally != null)
        {
            TryPushAlly(ally, moveDir);
        }

        CachePosition();
    }

    private void CachePosition()
    {
        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private void GetPlayerMovement(out Vector3 moveDir, out float moveSpeed)
    {
        moveDir = Vector3.zero;
        moveSpeed = 0f;

        if (!_hasLastPosition)
            return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;

        moveSpeed = delta.magnitude / dt;
        if (delta.sqrMagnitude > 0.000001f)
            moveDir = delta.normalized;
    }

    private Transform FindBestPushableAlly(Vector3 moveDir)
    {
        Vector3 center = transform.position;
        int count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.1f, searchRadius), _hits, detectionMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            Collider hit = _hits[i];
            if (hit == null) continue;

            Transform candidate = ResolveAllyRoot(hit);
            if (candidate == null) continue;
            if (candidate == transform) continue;

            NavMeshAgent agent = candidate.GetComponent<NavMeshAgent>() ?? candidate.GetComponentInParent<NavMeshAgent>();
            if (agent == null || !agent.isActiveAndEnabled) continue;

            Vector3 toAlly = candidate.position - center;
            toAlly.y = 0f;

            float dist = toAlly.magnitude;
            if (dist > Mathf.Max(0.1f, searchRadius))
                continue;

            Vector3 toAllyDir = dist > 0.001f ? (toAlly / dist) : moveDir;

            if (requireAllyInMoveDirection)
            {
                float dirDot = Vector3.Dot(moveDir, toAllyDir);
                if (dirDot < moveDirectionDotThreshold)
                    continue;
            }

            // Distance from ally to player's movement lane.
            float along = Mathf.Max(0f, Vector3.Dot(toAlly, moveDir));
            Vector3 closestPointOnLane = center + moveDir * along;
            float laneDistance = Vector3.Distance(
                new Vector3(candidate.position.x, 0f, candidate.position.z),
                new Vector3(closestPointOnLane.x, 0f, closestPointOnLane.z));

            if (laneDistance > Mathf.Max(0.1f, laneWidth))
                continue;

            // Prefer allies closer to the player and more centered in the move lane.
            float score = (-dist * 2f) - (laneDistance * 1.5f);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private Transform ResolveAllyRoot(Collider col)
    {
        if (col == null) return null;

        Transform t = col.transform;

        if (requireAllyTag)
        {
            if (t.CompareTag(allyTag)) return t;

            Transform p = t.parent;
            while (p != null)
            {
                if (p.CompareTag(allyTag))
                    return p;
                p = p.parent;
            }

            return null;
        }

        AllyController ally = col.GetComponent<AllyController>() ?? col.GetComponentInParent<AllyController>();
        if (ally != null) return ally.transform;

        NavMeshAgent agent = col.GetComponent<NavMeshAgent>() ?? col.GetComponentInParent<NavMeshAgent>();
        if (agent != null) return agent.transform;

        return null;
    }

    private void TryPushAlly(Transform ally, Vector3 moveDir)
    {
        if (ally == null) return;
        if (moveDir.sqrMagnitude < 0.0001f) return;

        int id = ally.GetInstanceID();
        if (_nextAllowedPushTimeByAlly.TryGetValue(id, out float nextTime) && Time.time < nextTime)
            return;

        NavMeshAgent agent = ally.GetComponent<NavMeshAgent>() ?? ally.GetComponentInParent<NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled)
            return;

        Vector3 start = ally.position;
        Vector3 targetPoint = start + moveDir.normalized * Mathf.Max(0.05f, pushStepDistance);

        bool moved = false;

        if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navHit, Mathf.Max(0.1f, navMeshSampleRadius), NavMesh.AllAreas))
        {
            moved = agent.Warp(navHit.position);
            if (!moved)
            {
                ally.position = navHit.position;
                moved = true;
            }
        }

        if (moved)
        {
            _nextAllowedPushTimeByAlly[id] = Time.time + Mathf.Max(0.01f, perAllyPushInterval);
            _nextGlobalPushTime = Time.time + Mathf.Max(0.005f, globalPushInterval);

            if (debugLogs)
                Debug.Log($"[PlayerAllyPushAnyDirection] Pushed ally '{ally.name}' along player movement.", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, searchRadius);

        if (Application.isPlaying && _hasLastPosition)
        {
            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;

            if (delta.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = delta.normalized;
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Gizmos.DrawLine(transform.position, transform.position + dir * 2f);
            }
        }
    }
}
