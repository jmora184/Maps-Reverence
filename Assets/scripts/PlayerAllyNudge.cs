using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Automatic player-side ally nudge helper.
/// Put this on the PLAYER (not the ally).
///
/// Goal:
/// - Let the player gently "push" nearby allies out of the way without changing AllyController.
/// - Uses NavMeshAgent.Warp when possible so this stays friendly with agent-driven movement.
/// - Only nudges while the player is actually moving and an ally is very close/in front.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAllyNudge : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("How far from the player we search for allies to nudge.")]
    [SerializeField] private float searchRadius = 1.75f;

    [Tooltip("How far in front of the player we prefer to look for a blocking ally.")]
    [SerializeField] private float forwardCheckDistance = 1.35f;

    [Tooltip("Optional facing reference. If empty, uses this transform forward.")]
    [SerializeField] private Transform directionReference;

    [Tooltip("Optional layer filter for ally detection. Leave as Everything if unsure.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [Header("Nudge")]
    [Tooltip("How far to move the ally when nudged.")]
    [SerializeField] private float nudgeDistance = 1.5f;

    [Tooltip("Small sideways preference so allies slide off the player instead of straight backward.")]
    [SerializeField] private float sideBias = 0.65f;

    [Tooltip("NavMesh sample radius for the target nudge point.")]
    [SerializeField] private float navMeshSampleRadius = 2f;

    [Tooltip("Minimum seconds between nudges on the same ally.")]
    [SerializeField] private float perAllyCooldown = 0.45f;

    [Tooltip("Minimum seconds between any two nudges.")]
    [SerializeField] private float globalCooldown = 0.12f;

    [Header("Movement Gate")]
    [Tooltip("Only auto-nudge while the player is actually trying to move.")]
    [SerializeField] private bool requirePlayerMovement = true;

    [Tooltip("Approx movement speed needed before auto-nudge is allowed.")]
    [SerializeField] private float minPlayerMoveSpeed = 0.1f;

    [Tooltip("If true, only nudge allies roughly in front of the player.")]
    [SerializeField] private bool preferFrontTargets = true;

    [Range(-1f, 1f)]
    [Tooltip("How much the target must be in front of the player. 0 = any front half, 0.35 is a reasonable default.")]
    [SerializeField] private float frontDotThreshold = 0.1f;

    [Header("Optional Filters")]
    [Tooltip("If true, only colliders with the Ally tag count.")]
    [SerializeField] private bool requireAllyTag = false;

    [SerializeField] private string allyTag = "Ally";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawDebug = false;

    private readonly Collider[] _hits = new Collider[24];
    private readonly Dictionary<int, float> _nextAllowedNudgeTimeByAlly = new Dictionary<int, float>();

    private Vector3 _lastPosition;
    private bool _hasLastPosition;
    private float _nextGlobalNudgeTime;

    private void Awake()
    {
        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private void Update()
    {
        if (Time.time < _nextGlobalNudgeTime)
        {
            CachePosition();
            return;
        }

        if (requirePlayerMovement && !IsPlayerMoving())
        {
            CachePosition();
            return;
        }

        Transform ally = FindBestBlockingAlly();
        if (ally != null)
        {
            TryAutoNudge(ally);
        }

        CachePosition();
    }

    private void CachePosition()
    {
        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private bool IsPlayerMoving()
    {
        if (!_hasLastPosition)
            return false;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;

        float speed = delta.magnitude / dt;
        return speed >= Mathf.Max(0f, minPlayerMoveSpeed);
    }

    private Transform FindBestBlockingAlly()
    {
        Vector3 center = transform.position;
        int count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.1f, searchRadius), _hits, detectionMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestScore = float.MinValue;

        Vector3 forward = GetFlatForward();
        Vector3 aheadPoint = center + forward * Mathf.Max(0f, forwardCheckDistance);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _hits[i];
            if (hit == null) continue;

            Transform candidate = ResolveAllyRoot(hit);
            if (candidate == null) continue;
            if (candidate == transform) continue;

            NavMeshAgent agent = candidate.GetComponent<NavMeshAgent>() ?? candidate.GetComponentInParent<NavMeshAgent>();
            if (agent == null) continue;

            Vector3 toCandidate = candidate.position - center;
            toCandidate.y = 0f;

            float dist = toCandidate.magnitude;
            if (dist > Mathf.Max(0.1f, searchRadius))
                continue;

            if (preferFrontTargets)
            {
                Vector3 dir = dist > 0.001f ? (toCandidate / dist) : forward;
                float dot = Vector3.Dot(forward, dir);
                if (dot < frontDotThreshold)
                    continue;
            }

            // Score closer allies and allies nearer the "ahead" point higher.
            float aheadDist = Vector3.Distance(new Vector3(candidate.position.x, 0f, candidate.position.z), new Vector3(aheadPoint.x, 0f, aheadPoint.z));
            float score = (-dist * 2f) - aheadDist;

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

    private void TryAutoNudge(Transform ally)
    {
        if (ally == null) return;

        int id = ally.GetInstanceID();
        if (_nextAllowedNudgeTimeByAlly.TryGetValue(id, out float nextTime) && Time.time < nextTime)
            return;

        NavMeshAgent agent = ally.GetComponent<NavMeshAgent>() ?? ally.GetComponentInParent<NavMeshAgent>();
        if (agent == null) return;
        if (!agent.isActiveAndEnabled) return;

        Vector3 playerPos = transform.position;
        Vector3 allyPos = ally.position;

        Vector3 away = allyPos - playerPos;
        away.y = 0f;

        Vector3 forward = GetFlatForward();
        if (away.sqrMagnitude < 0.0001f)
            away = forward;

        away.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, away).normalized;
        float sideSign = Vector3.Dot(right, forward) >= 0f ? 1f : -1f;

        Vector3 preferredDir = (away + right * sideBias * sideSign).normalized;
        if (preferredDir.sqrMagnitude < 0.0001f)
            preferredDir = away;

        Vector3 targetPoint = allyPos + preferredDir * Mathf.Max(0.1f, nudgeDistance);

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
        else
        {
            if (debugLogs)
                Debug.Log("[PlayerAllyNudge] No NavMesh point found for nudge target.", this);
        }

        if (moved)
        {
            _nextAllowedNudgeTimeByAlly[id] = Time.time + Mathf.Max(0.01f, perAllyCooldown);
            _nextGlobalNudgeTime = Time.time + Mathf.Max(0.01f, globalCooldown);

            if (debugLogs)
                Debug.Log($"[PlayerAllyNudge] Nudged ally: {ally.name}", this);
        }
    }

    private Vector3 GetFlatForward()
    {
        Transform source = directionReference != null ? directionReference : transform;
        Vector3 forward = source.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;

        Vector3 center = transform.position;
        Vector3 forward = Application.isPlaying ? GetFlatForward() : transform.forward;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(center, searchRadius);

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(center, center + forward.normalized * forwardCheckDistance);
    }
}
