using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Put this on the PLAYER root that has the CharacterController.
/// 
/// Purpose:
/// - When the player physically runs into an ally, the ally gives way.
/// - No button press.
/// - No AllyController changes.
/// - Uses CharacterController collision (OnControllerColliderHit), which is the correct hook
///   for your player setup.
/// 
/// Why this is different:
/// - Your player is NOT a Rigidbody pusher; it is a CharacterController.
/// - So the clean solution is to react to actual controller hits and nudge the ally's NavMeshAgent
///   in the same horizontal direction the player is moving.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAllyControllerPush : MonoBehaviour
{
    [Header("Push")]
    [SerializeField] private float pushMultiplier = 1.15f;
    [SerializeField] private float minPushPerHit = 0.05f;
    [SerializeField] private float maxPushPerHit = 0.30f;

    [Header("Fallback")]
    [SerializeField] private float navMeshSampleRadius = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private CharacterController _characterController;
    private readonly Dictionary<int, int> _lastPushFrameByAlly = new Dictionary<int, int>();

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        if (_characterController == null)
        {
            Debug.LogWarning("[PlayerAllyControllerPush] No CharacterController found on this object.", this);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (_characterController == null) return;
        if (hit.collider == null) return;

        // Ignore floor / steep downward collision noise.
        if (hit.moveDirection.y < -0.3f)
            return;

        AllyController ally = hit.collider.GetComponent<AllyController>() ?? hit.collider.GetComponentInParent<AllyController>();
        if (ally == null) return;
        if (!ally.gameObject.activeInHierarchy) return;

        NavMeshAgent agent = ally.agent != null ? ally.agent : ally.GetComponent<NavMeshAgent>();
        if (agent == null) return;
        if (!agent.isActiveAndEnabled) return;

        int allyId = ally.GetInstanceID();
        if (_lastPushFrameByAlly.TryGetValue(allyId, out int lastFrame) && lastFrame == Time.frameCount)
            return;

        _lastPushFrameByAlly[allyId] = Time.frameCount;

        Vector3 pushDir = hit.moveDirection;
        pushDir.y = 0f;

        if (pushDir.sqrMagnitude < 0.0001f)
        {
            Vector3 vel = _characterController.velocity;
            vel.y = 0f;
            if (vel.sqrMagnitude < 0.0001f)
                return;

            pushDir = vel.normalized;
        }
        else
        {
            pushDir.Normalize();
        }

        Vector3 playerVel = _characterController.velocity;
        playerVel.y = 0f;
        float playerSpeed = playerVel.magnitude;

        float pushDistance = Mathf.Clamp(playerSpeed * Time.deltaTime * pushMultiplier, minPushPerHit, maxPushPerHit);
        Vector3 offset = pushDir * pushDistance;

        bool moved = false;

        // Best feel: move the NavMeshAgent a small amount in-place.
        if (agent.isOnNavMesh)
        {
            agent.Move(offset);
            moved = true;
        }
        else
        {
            Vector3 desired = ally.transform.position + offset;
            if (NavMesh.SamplePosition(desired, out NavMeshHit navHit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                moved = agent.Warp(navHit.position);
                if (!moved)
                {
                    ally.transform.position = navHit.position;
                    moved = true;
                }
            }
        }

        if (debugLogs && moved)
        {
            Debug.Log($"[PlayerAllyControllerPush] Pushed ally '{ally.name}' by {offset.magnitude:F3}", this);
        }
    }
}
