using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Executes move / join / split commands coming from CommandStateMachine.
/// 
/// ✅ Team-aware movement:
/// If the player has selected an Ally that belongs to a Team, we automatically expand the move order
/// to include ALL members of that Team (even if only the team anchor was selected).
/// </summary>
public class CommandExecutor : MonoBehaviour
{
    [Header("Refs")]
    public CommandStateMachine sm;

    [Header("Move Settings")]
    public bool useFormation = true;
    public float formationSpacing = 1.6f;


    [Header("Follow (Move-to-Target)")]
    [Tooltip("How often to refresh destinations while following a moving target. Lower = tighter follow, higher = fewer path recalcs.")]
    public float followUpdateInterval = 0.15f;

    // Agent -> follow coroutine + target + optional offset (for keeping formation while following)
    private readonly Dictionary<NavMeshAgent, Coroutine> followRoutines = new Dictionary<NavMeshAgent, Coroutine>();
    private readonly Dictionary<NavMeshAgent, Transform> followTargets = new Dictionary<NavMeshAgent, Transform>();
    private readonly Dictionary<NavMeshAgent, Vector3> followOffsets = new Dictionary<NavMeshAgent, Vector3>();
    public int formationColumns = 4;

    [Header("Team Move Settings")]
    [Tooltip("If true, move orders given to any team member will move the entire team.")]
    public bool expandMoveToWholeTeam = true;

    [Tooltip("If true, agents are ordered deterministically before formation is applied (prevents formation 'shuffling').")]
    public bool stableFormationOrdering = true;



    [Header("Move Hints (optional)")]
    [Tooltip("Show a hint when a move order is issued.")]
    public bool showMoveHints = true;

    [Tooltip("How long the move hint stays on screen.")]
    public float moveHintDuration = 1.8f;

    [Header("Join Settings")]
    public float joinArriveThreshold = 0.35f; // how close leader must get to target to be "arrived"


    // -------------------- JOIN ROUTE (for DirectionArrowPreview) --------------------
    // DirectionArrowPreview.cs looks for these names (via reflection) to show an arrow while a join leader is in-route.
    public bool IsJoinMoveInProgress { get; private set; }
    public Transform JoinMoveLeader { get; private set; }
    public Transform JoinMoveTarget { get; private set; }

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
            sm.OnFollowRequested += HandleFollowRequested;
            sm.OnAddRequested += HandleAddRequested;
            sm.OnSplitRequested += HandleSplitRequested;
        }
    }

    private void OnDisable()
    {
        if (sm != null)
        {
            sm.OnMoveRequested -= HandleMoveRequested;
            sm.OnFollowRequested -= HandleFollowRequested;
            sm.OnAddRequested -= HandleAddRequested;
            sm.OnSplitRequested -= HandleSplitRequested;
        }

        StopAllFollows();
    }

    private void HandleMoveRequested(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        ExecuteMoveOrder(selection, destination);
    }

    private void HandleFollowRequested(IReadOnlyList<GameObject> selection, GameObject targetUnit)
    {
        if (targetUnit == null) return;
        ExecuteFollowOrder(selection, targetUnit.transform);
    }


    // ✅ Needed by CommandQueue.FlushMoves(executor)
    public void ExecuteMoveOrder(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        if (selection == null || selection.Count == 0) return;

        // ✅ Team expansion (team members move together)
        List<GameObject> expanded = ExpandSelectionForTeams(selection);


        // Hint: team / unit move feedback
        ShowMoveHint(expanded);

        List<NavMeshAgent> agents = GatherAgents(expanded);
        if (agents.Count == 0) return;


        // If any of these units were following a target, stop that follow now.
        for (int i = 0; i < agents.Count; i++)
            StopFollow(agents[i]);

        // Optional stable ordering so formation offsets don't shuffle
        if (stableFormationOrdering && agents.Count > 1)
        {
            agents.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                int ida = a.transform.GetInstanceID();
                int idb = b.transform.GetInstanceID();
                return ida.CompareTo(idb);
            });
        }

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



    // -------------------- FOLLOW (move-to-target) --------------------

    public void ExecuteFollowOrder(IReadOnlyList<GameObject> selection, Transform target)
    {
        if (selection == null || selection.Count == 0) return;
        if (target == null) return;

        // ✅ Team expansion (team members follow together)
        List<GameObject> expanded = ExpandSelectionForTeams(selection);

        List<NavMeshAgent> agents = GatherAgents(expanded);
        if (agents.Count == 0) return;

        // Optional stable ordering so formation offsets don't shuffle
        if (stableFormationOrdering && agents.Count > 1)
        {
            agents.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                int ida = a.transform.GetInstanceID();
                int idb = b.transform.GetInstanceID();
                return ida.CompareTo(idb);
            });
        }

        // Compute per-agent offsets for follow (so groups keep formation around the target)
        // If formation is disabled, offsets are zero.
        List<Vector3> offsets = new List<Vector3>(agents.Count);
        if (!useFormation || agents.Count == 1)
        {
            for (int i = 0; i < agents.Count; i++)
                offsets.Add(Vector3.zero);
        }
        else
        {
            int cols = Mathf.Max(1, formationColumns);
            float spacing = Mathf.Max(0.1f, formationSpacing);

            int count = agents.Count;
            int rows = Mathf.CeilToInt(count / (float)cols);

            float width = (cols - 1) * spacing;
            float height = (rows - 1) * spacing;

            for (int i = 0; i < agents.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;

                float x = (col * spacing) - (width * 0.5f);
                float z = (row * spacing) - (height * 0.5f);
                offsets.Add(new Vector3(x, 0f, z));
            }
        }

        for (int i = 0; i < agents.Count; i++)
        {
            NavMeshAgent agent = agents[i];
            if (agent == null) continue;

            Vector3 offset = (i >= 0 && i < offsets.Count) ? offsets[i] : Vector3.zero;

            StartFollow(agent, target, offset);
        }
    }

    private void StartFollow(NavMeshAgent agent, Transform target, Vector3 offset)
    {
        if (agent == null) return;

        // Replace any existing follow
        StopFollow(agent);

        if (target == null) return;

        followTargets[agent] = target;
        followOffsets[agent] = offset;
        followRoutines[agent] = StartCoroutine(FollowRoutine(agent));
    }

    private void StopFollow(NavMeshAgent agent)
    {
        if (agent == null) return;

        if (followRoutines.TryGetValue(agent, out Coroutine routine) && routine != null)
            StopCoroutine(routine);

        followRoutines.Remove(agent);
        followTargets.Remove(agent);
        followOffsets.Remove(agent);
    }

    private void StopAllFollows()
    {
        // Stop coroutines first
        foreach (var kv in followRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }

        followRoutines.Clear();
        followTargets.Clear();
        followOffsets.Clear();
    }

    private IEnumerator FollowRoutine(NavMeshAgent agent)
    {
        // Reuse wait object to reduce GC
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.02f, followUpdateInterval));

        while (agent != null)
        {
            if (!followTargets.TryGetValue(agent, out Transform target) || target == null)
                break;

            // If the target gets disabled, treat it as gone.
            if (!target.gameObject.activeInHierarchy)
                break;

            Vector3 offset = Vector3.zero;
            followOffsets.TryGetValue(agent, out offset);

            Vector3 desired = target.position + offset;

            // Snap to navmesh if possible
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                desired = hit.position;

            agent.isStopped = false;
            agent.SetDestination(desired);

            yield return wait;
        }

        // Cleanup (agent may be null if destroyed)
        if (agent != null)
            StopFollow(agent);
    }


    private bool TryGetSingleTeamForSelection(IReadOnlyList<GameObject> expandedSelection, out Team team)
    {
        team = null;
        if (expandedSelection == null || expandedSelection.Count == 0) return false;
        if (TeamManager.Instance == null) return false;

        // Find first ally's team (if any)
        Team firstTeam = null;
        for (int i = 0; i < expandedSelection.Count; i++)
        {
            var go = expandedSelection[i];
            if (go == null) continue;
            if (!go.CompareTag("Ally")) continue;

            firstTeam = TeamManager.Instance.GetTeamOf(go.transform);
            break;
        }

        if (firstTeam == null) return false;

        // Ensure every ally in selection is in the same team
        for (int i = 0; i < expandedSelection.Count; i++)
        {
            var go = expandedSelection[i];
            if (go == null) continue;
            if (!go.CompareTag("Ally")) continue;

            Team t = TeamManager.Instance.GetTeamOf(go.transform);
            if (t != firstTeam) return false;
        }

        team = firstTeam;
        return true;
    }

    private void ShowMoveHint(IReadOnlyList<GameObject> expandedSelection)
    {
        if (!showMoveHints) return;

        // Prefer "Team X moving to location." if this looks like a team move
        if (TryGetSingleTeamForSelection(expandedSelection, out Team team) && team != null)
        {
            HintSystem.Show($"Team {team.Id} moving to location.", moveHintDuration);
            return;
        }

        // Fallback: "Units moving to location."
        int count = expandedSelection != null ? expandedSelection.Count : 0;
        if (count > 0)
            HintSystem.Show($"{count} {(count == 1 ? "unit" : "units")} moving to location.", moveHintDuration);
        else
            HintSystem.Show("Moving to location.", moveHintDuration);
    }

    private List<GameObject> ExpandSelectionForTeams(IReadOnlyList<GameObject> selection)
    {
        // If off, just return a cleaned list (unique, non-null).
        if (!expandMoveToWholeTeam || TeamManager.Instance == null)
            return UniqueNonNull(selection);

        HashSet<Transform> added = new HashSet<Transform>();
        List<GameObject> expanded = new List<GameObject>(selection.Count);

        for (int i = 0; i < selection.Count; i++)
        {
            var go = selection[i];
            if (go == null) continue;

            // Only allies participate in Team logic
            if (go.CompareTag("Ally"))
            {
                Team team = TeamManager.Instance.GetTeamOf(go.transform);
                if (team != null && team.Members != null && team.Members.Count > 0)
                {
                    for (int m = 0; m < team.Members.Count; m++)
                    {
                        var t = team.Members[m];
                        if (t == null) continue;
                        if (added.Add(t))
                            expanded.Add(t.gameObject);
                    }
                    continue;
                }
            }

            // Not an ally OR not in a team -> include the object itself
            if (added.Add(go.transform))
                expanded.Add(go);
        }

        return expanded;
    }

    private static List<GameObject> UniqueNonNull(IReadOnlyList<GameObject> selection)
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        List<GameObject> list = new List<GameObject>();

        for (int i = 0; i < selection.Count; i++)
        {
            var go = selection[i];
            if (go == null) continue;
            if (set.Add(go)) list.Add(go);
        }

        return list;
    }

    private static List<NavMeshAgent> GatherAgents(IReadOnlyList<GameObject> selection)
    {
        List<NavMeshAgent> agents = new List<NavMeshAgent>();

        for (int i = 0; i < selection.Count; i++)
        {
            var go = selection[i];
            if (go == null) continue;

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null) agent = go.GetComponentInChildren<NavMeshAgent>();

            if (agent != null && agent.isActiveAndEnabled)
                agents.Add(agent);
        }

        return agents;
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
            {
                StopCoroutine(joinMoveRoutine);
                joinMoveRoutine = null;
                ClearJoinRouteState();
            }

            // Expose join-route state for DirectionArrowPreview
            IsJoinMoveInProgress = true;
            JoinMoveLeader = leaderGO.transform;
            JoinMoveTarget = targetGO.transform;

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
            ClearJoinRouteState();
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

        // Clear marker
        if (marker != null)
        {
            marker.End();
            Destroy(marker);
        }

        joinMoveRoutine = null;
        ClearJoinRouteState();
    }

    private void ClearJoinRouteState()
    {
        IsJoinMoveInProgress = false;
        JoinMoveLeader = null;
        JoinMoveTarget = null;
    }

    // -------------------- SPLIT (placeholder) --------------------
    private void HandleSplitRequested(IReadOnlyList<GameObject> selection)
    {
        Debug.Log("CommandExecutor: SPLIT requested (not implemented yet).");
    }
}
