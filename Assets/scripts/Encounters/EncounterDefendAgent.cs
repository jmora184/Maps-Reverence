using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple built-in Defend/Hold AI for quick proof-of-concept testing.
/// - "Hold": go to defend center and stop.
/// - "Defend": stay within defend radius; if pushed out, return.
/// Intended ONLY when you don't already have enemy/ally AI scripts.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EncounterDefendAgent : MonoBehaviour
{
    public Transform defendCenter;
    public float defendRadius = 0f; // 0 = pure hold
    public float arrivalDistance = 1.5f;

    private NavMeshAgent _agent;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    private void OnEnable()
    {
        GoToCenter();
    }

    private void Update()
    {
        if (_agent == null || !_agent.enabled) return;
        if (defendCenter == null) return;

        float dist = Vector3.Distance(transform.position, defendCenter.position);

        if (defendRadius <= 0.01f)
        {
            // Hold: stop after arriving.
            if (!_agent.pathPending && _agent.remainingDistance <= Mathf.Max(arrivalDistance, _agent.stoppingDistance + 0.05f))
            {
                _agent.isStopped = true;
            }
            return;
        }

        // Defend: if we drift beyond radius, return.
        if (dist > defendRadius)
        {
            _agent.isStopped = false;
            _agent.SetDestination(defendCenter.position);
        }
    }

    private void GoToCenter()
    {
        if (defendCenter == null) return;
        _agent.isStopped = false;
        _agent.SetDestination(defendCenter.position);
    }

    // Optional hooks if EncounterDirectorPOC uses SendMessage
    public void Encounter_SetBehavior(EncounterBehavior b)
    {
        enabled = (b == EncounterBehavior.Defend || b == EncounterBehavior.Hold);
    }

    public void Encounter_SetDefend(EncounterDirectorPOC.DefendPayload payload)
    {
        if (defendCenter == null)
        {
            var t = new GameObject("RuntimeDefendCenter").transform;
            t.hideFlags = HideFlags.HideInHierarchy;
            t.position = payload.center;
            defendCenter = t;
        }
        else
        {
            defendCenter.position = payload.center;
        }

        defendRadius = payload.radius;
        GoToCenter();
    }
}
