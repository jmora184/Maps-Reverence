using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Put this on the PLAYER.
/// 
/// Purpose:
/// - Make allies "give way" when the player physically walks into them,
///   like pushing a small box out of the way.
/// - Does NOT modify AllyController.
/// - Uses actual collider overlap / penetration instead of button presses or search logic.
/// 
/// How it works:
/// - Each frame, if the player has moved, we check nearby colliders.
/// - If the player is overlapping an ally collider, we compute the penetration direction.
/// - We then move the ally out of the way using NavMeshAgent.Warp (or transform fallback).
/// 
/// Why this approach:
/// - True Rigidbody physics fights with NavMeshAgent movement.
/// - This gives a simple "player pushes ally aside" feel without changing ally AI.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAllyBodyPush : MonoBehaviour
{
    [Header("Push Feel")]
    [Tooltip("Extra distance added on top of the overlap resolution so the ally gives way a bit more.")]
    [SerializeField] private float extraPushDistance = 0.08f;

    [Tooltip("Maximum distance an ally can be moved in one frame.")]
    [SerializeField] private float maxPushPerFrame = 0.45f;

    [Tooltip("Minimum player movement speed required before push happens.")]
    [SerializeField] private float minPlayerMoveSpeed = 0.02f;

    [Tooltip("NavMesh sample radius around the desired pushed position.")]
    [SerializeField] private float navMeshSampleRadius = 1.25f;

    [Header("Detection")]
    [Tooltip("Extra search padding around the player's collider bounds.")]
    [SerializeField] private float boundsPadding = 0.25f;

    [Tooltip("Optional layer mask for what we can test against. Leave as Everything if unsure.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly Collider[] _myColliders = new Collider[16];
    private readonly Collider[] _nearby = new Collider[32];

    private int _myColliderCount;
    private Vector3 _lastPosition;
    private bool _hasLastPosition;

    private void Awake()
    {
        _myColliderCount = GetComponentsInChildren(true, _myColliders);

        if (_myColliderCount <= 0)
        {
            Debug.LogWarning("[PlayerAllyBodyPush] No player colliders found on this object or its children.", this);
        }

        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private void LateUpdate()
    {
        Vector3 moveDelta = GetMoveDeltaThisFrame();
        float moveSpeed = moveDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);

        if (moveSpeed < minPlayerMoveSpeed)
        {
            CachePosition();
            return;
        }

        Bounds searchBounds = BuildPlayerBounds();
        searchBounds.Expand(boundsPadding * 2f);

        int count = Physics.OverlapBoxNonAlloc(
            searchBounds.center,
            searchBounds.extents,
            _nearby,
            transform.rotation,
            detectionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = _nearby[i];
            if (other == null) continue;

            AllyController ally = other.GetComponent<AllyController>() ?? other.GetComponentInParent<AllyController>();
            if (ally == null) continue;
            if (ally.transform == transform) continue;

            TryPushAllyFromOverlap(ally, other, moveDelta);
        }

        CachePosition();
    }

    private void TryPushAllyFromOverlap(AllyController ally, Collider allyCollider, Vector3 playerMoveDelta)
    {
        if (ally == null || allyCollider == null) return;

        NavMeshAgent agent = ally.agent != null ? ally.agent : ally.GetComponent<NavMeshAgent>();
        Transform allyTransform = ally.transform;

        Vector3 accumulatedPush = Vector3.zero;
        bool foundOverlap = false;

        for (int i = 0; i < _myColliderCount; i++)
        {
            Collider myCol = _myColliders[i];
            if (myCol == null) continue;
            if (!myCol.enabled) continue;

            if (Physics.ComputePenetration(
                myCol, myCol.transform.position, myCol.transform.rotation,
                allyCollider, allyCollider.transform.position, allyCollider.transform.rotation,
                out Vector3 direction, out float distance))
            {
                foundOverlap = true;

                // direction is the direction to move the FIRST collider (player) out of overlap.
                // So to move the ALLY out of the way, push opposite that direction.
                Vector3 push = (-direction.normalized) * (distance + extraPushDistance);
                accumulatedPush += push;
            }
        }

        if (!foundOverlap)
            return;

        // Blend in the player's actual movement direction so it feels like the ally is being
        // shoved in the direction the player is trying to go.
        Vector3 moveDir = playerMoveDelta;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.000001f)
        {
            moveDir.Normalize();
            accumulatedPush += moveDir * extraPushDistance;
        }

        accumulatedPush.y = 0f;

        if (accumulatedPush.sqrMagnitude < 0.000001f)
            return;

        Vector3 pushDir = accumulatedPush.normalized;
        float pushDist = Mathf.Min(accumulatedPush.magnitude, Mathf.Max(0.01f, maxPushPerFrame));

        Vector3 desired = allyTransform.position + pushDir * pushDist;

        bool moved = false;

        if (agent != null && agent.isActiveAndEnabled)
        {
            if (NavMesh.SamplePosition(desired, out NavMeshHit navHit, Mathf.Max(0.05f, navMeshSampleRadius), NavMesh.AllAreas))
            {
                moved = agent.Warp(navHit.position);
                if (!moved)
                {
                    allyTransform.position = navHit.position;
                    moved = true;
                }
            }
        }
        else
        {
            allyTransform.position = desired;
            moved = true;
        }

        if (debugLogs && moved)
        {
            Debug.Log($"[PlayerAllyBodyPush] Pushed ally '{ally.name}'", this);
        }
    }

    private Bounds BuildPlayerBounds()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        for (int i = 0; i < _myColliderCount; i++)
        {
            Collider c = _myColliders[i];
            if (c == null || !c.enabled) continue;

            if (!hasBounds)
            {
                bounds = c.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }

        if (!hasBounds)
            bounds = new Bounds(transform.position, Vector3.one);

        return bounds;
    }

    private Vector3 GetMoveDeltaThisFrame()
    {
        if (!_hasLastPosition)
            return Vector3.zero;

        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;
        return delta;
    }

    private void CachePosition()
    {
        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    private int GetComponentsInChildren<T>(bool includeInactive, T[] buffer) where T : Component
    {
        T[] found = gameObject.GetComponentsInChildren<T>(includeInactive);
        int count = Mathf.Min(found.Length, buffer.Length);

        for (int i = 0; i < count; i++)
            buffer[i] = found[i];

        return count;
    }
}
