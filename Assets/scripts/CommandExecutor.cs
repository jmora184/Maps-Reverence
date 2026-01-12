using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class CommandExecutor : MonoBehaviour
{
    [Header("Refs")]
    public CommandStateMachine sm;

    [Header("Move Settings")]
    public bool useFormation = true;
    public float formationSpacing = 1.6f;
    public int formationColumns = 4;

    [Header("Join Settings")]
    public float joinArriveThreshold = 0.35f; // how close leader must get to target to be "arrived"

    private Coroutine joinMoveRoutine;

    private void Awake()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
    }

    private void OnEnable()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm != null)
        {
            sm.OnMoveRequested += HandleMoveRequested;
            sm.OnAddRequested += HandleAddRequested;
            sm.OnSplitRequested += HandleSplitRequested;
        }
    }

    private void OnDisable()
    {
        if (sm != null)
        {
            sm.OnMoveRequested -= HandleMoveRequested;
            sm.OnAddRequested -= HandleAddRequested;
            sm.OnSplitRequested -= HandleSplitRequested;
        }
    }

    private void HandleMoveRequested(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        ExecuteMoveOrder(selection, destination);
    }

    // ✅ Needed by CommandQueue.FlushMoves(executor)
    public void ExecuteMoveOrder(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        if (selection == null || selection.Count == 0) return;

        List<NavMeshAgent> agents = new();
        for (int i = 0; i < selection.Count; i++)
        {
            var go = selection[i];
            if (go == null) continue;

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null) agent = go.GetComponentInChildren<NavMeshAgent>();

            if (agent != null && agent.isActiveAndEnabled)
                agents.Add(agent);
        }

        if (agents.Count == 0) return;

        // No formation or single unit
        if (!useFormation || agents.Count == 1)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a == null) continue;

                a.isStopped = false;
                a.SetDestination(destination);
            }
            return;
        }

        // Formation
        int cols = Mathf.Max(1, formationColumns);
        float spacing = Mathf.Max(0.1f, formationSpacing);

        int count = agents.Count;
        int rows = Mathf.CeilToInt(count / (float)cols);

        float width = (cols - 1) * spacing;
        float height = (rows - 1) * spacing;

        for (int i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            if (agent == null) continue;

            int r = i / cols;
            int c = i % cols;

            float x = (c * spacing) - width * 0.5f;
            float z = (r * spacing) - height * 0.5f;

            Vector3 targetPos = destination + new Vector3(x, 0f, z);

            // Snap to nearest navmesh
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                targetPos = hit.position;

            agent.isStopped = false;
            agent.SetDestination(targetPos);
        }
    }

    private void HandleAddRequested(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (clickedUnit == null) return;
        if (sm == null) return;

        // ✅ JOIN PATH
        if (sm.JoinArmed && sm.JoinSource != null)
        {
            GameObject leaderGO = sm.JoinSource;
            GameObject targetGO = clickedUnit;

            if (leaderGO == null || targetGO == null) return;
            if (leaderGO == targetGO) return;

            if (!leaderGO.CompareTag("Ally") || !targetGO.CompareTag("Ally"))
            {
                Debug.Log($"[Join] Ignored: must click Ally -> Ally. leader={leaderGO.name} target={targetGO.name}");
                return;
            }

            if (TeamManager.Instance == null)
            {
                Debug.LogError("[Join] TeamManager.Instance is NULL. Add TeamManager to the scene.");
                return;
            }

            // ✅ Create/merge team immediately so overlay sees it
            Team team = TeamManager.Instance.JoinUnits(leaderGO.transform, targetGO.transform);
            Debug.Log($"[Join] JoinUnits called. Team={(team != null ? team.Id.ToString() : "null")}");

            // ✅ Move the leader to the target, and mark as in-route so selection can block
            if (joinMoveRoutine != null)
                StopCoroutine(joinMoveRoutine);

            joinMoveRoutine = StartCoroutine(MoveLeaderToJoinTargetRoutine(leaderGO, targetGO));

            return;
        }

        // If not join-armed, ignore (other add behaviors can go here later)
    }

    private IEnumerator MoveLeaderToJoinTargetRoutine(GameObject leaderGO, GameObject targetGO)
    {
        if (leaderGO == null || targetGO == null) yield break;

        var leaderT = leaderGO.transform;
        var targetT = targetGO.transform;

        // mark as in-route for join
        var marker = leaderGO.GetComponent<JoinRouteMarker>();
        if (marker == null) marker = leaderGO.AddComponent<JoinRouteMarker>();
        marker.Begin(targetT);

        var agent = leaderGO.GetComponent<NavMeshAgent>();
        if (agent == null) agent = leaderGO.GetComponentInChildren<NavMeshAgent>();

        // If no agent, just snap
        if (agent == null || !agent.isActiveAndEnabled)
        {
            leaderT.position = targetT.position;

            marker.End();
            Destroy(marker);

            joinMoveRoutine = null;
            yield break;
        }

        agent.isStopped = false;
        agent.SetDestination(targetT.position);

        float threshold = Mathf.Max(joinArriveThreshold, agent.stoppingDistance + 0.05f);

        while (leaderGO != null && targetGO != null)
        {
            float d = Vector3.Distance(leaderT.position, targetT.position);
            if (d <= threshold) break;

            yield return null;
        }

        // Arrived (or one got destroyed)
        if (leaderGO != null && targetGO != null)
        {
            // Optional: snap to exact position if you want tight join
            // leaderT.position = targetT.position;
        }

        // Clear marker
        if (marker != null)
        {
            marker.End();
            Destroy(marker);
        }

        joinMoveRoutine = null;
    }

    // -------------------- SPLIT (placeholder) --------------------
    private void HandleSplitRequested(IReadOnlyList<GameObject> selection)
    {
        Debug.Log("CommandExecutor: SPLIT requested (not implemented yet).");
    }
}
