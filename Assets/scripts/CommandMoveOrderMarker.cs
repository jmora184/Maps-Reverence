using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Marks a unit as "busy" executing a MOVE order so Command Mode can block re-selecting it
/// until it is close enough to its destination.
/// </summary>
public class CommandMoveOrderMarker : MonoBehaviour
{
    public bool inRoute;
    public Vector3 destination;

    [Tooltip("Extra distance beyond NavMeshAgent.stoppingDistance that still counts as busy.")]
    public float stopBuffer = 0.6f;

    [Tooltip("If something else changes agent.destination far from our commanded destination, the order is considered overridden.")]
    public float destinationOverrideTolerance = 1.0f;

    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = GetComponentInChildren<NavMeshAgent>();
    }

    public void Begin(Vector3 dest, float stopBufferOverride, float overrideTolerance)
    {
        destination = dest;
        stopBuffer = stopBufferOverride;
        destinationOverrideTolerance = overrideTolerance;
        inRoute = true;

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = GetComponentInChildren<NavMeshAgent>();
        }
    }

    public void End()
    {
        inRoute = false;
    }

    private void Update()
    {
        if (!inRoute) return;
        if (agent == null || !agent.isActiveAndEnabled)
        {
            End();
            return;
        }

        // If another system takes over the agent destination, don't soft-lock selection forever.
        if (Vector3.Distance(agent.destination, destination) > destinationOverrideTolerance)
        {
            End();
            return;
        }

        if (agent.pathPending) return;

        float remain = agent.remainingDistance;
        if (float.IsNaN(remain) || float.IsInfinity(remain))
        {
            End();
            return;
        }

        float threshold = Mathf.Max(agent.stoppingDistance, 0.05f) + stopBuffer;
        if (remain <= threshold)
            End();
    }
}
