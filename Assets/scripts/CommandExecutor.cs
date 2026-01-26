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

    [Tooltip("Extra spacing multiplier applied when issuing a move order for a Team (keeps team members from pushing into the same spot).")]
    public float teamMoveSpacingMultiplier = 2.0f;

    public int formationColumns = 4;


    [Header("Formation Pattern")]
    public FormationPattern formationPattern = FormationPattern.Grid;

    [Tooltip("If true, LINE/WEDGE (and oriented GRID) will rotate to face the move direction (from the selection center toward the destination/target).")]
    public bool orientFormationToMoveDirection = true;

    public enum FormationPattern
    {
        Grid,
        Line,
        Wedge
    }

    // Easy wiring from UI Button onClick() without extra scripts
    public void SetFormationGrid() => formationPattern = FormationPattern.Grid;
    public void SetFormationLine() => formationPattern = FormationPattern.Line;
    public void SetFormationWedge() => formationPattern = FormationPattern.Wedge;

    [Header("Team Move Settings")]
    [Tooltip("If true, move orders given to any team member will move the entire team.")]
    public bool expandMoveToWholeTeam = true;

    [Tooltip("If true, agents are ordered deterministically before formation is applied (prevents formation 'shuffling').")]
    public bool stableFormationOrdering = true;

    [Header("Follow Settings")]
    [Tooltip("If true, FOLLOW orders given to any team member will apply to the entire team.")]
    public bool expandFollowToWholeTeam = true;

    [Header("Follow Formation (Team)")]
    [Tooltip("If true, team FOLLOW orders keep members in formation while chasing a target (prevents everyone piling onto the same center point).")]
    public bool useFormationWhileFollowingTeam = true;

    [Tooltip("How often we refresh formation destinations while following a moving target (seconds).")]
    public float followFormationRefreshInterval = 0.15f;

    [Tooltip("NavMesh sample radius used when placing formation slots while following.")]
    public float followFormationNavmeshSampleRadius = 2.5f;


    [Header("Move Hints (optional)")]
    [Tooltip("Show a hint when a move order is issued.")]
    public bool showMoveHints = true;

    [Tooltip("How long the move hint stays on screen.")]
    public float moveHintDuration = 1.8f;

    [Header("Destination Pin Auto-Clear")]
    [Tooltip("If true, clears destination pin markers when the unit/team arrives at its move destination.")]
    public bool clearDestinationPinsOnArrival = true;

    [Tooltip("If true, clears destination pin markers when a FOLLOW/ATTACK target is destroyed (so pins don't linger after combat).")]
    public bool clearDestinationPinsOnTargetDestroyed = true;

    [Tooltip("How often we check for arrival to clear pins (seconds).")]
    public float pinArrivalCheckInterval = 0.15f;

    [Tooltip("Extra buffer added to NavMeshAgent.stoppingDistance when deciding that an agent has arrived.")]
    public float pinArrivalStopBuffer = 0.6f;

    [Header("Team Arrival Scaling")]
    [Tooltip("If true, arrival tolerance for TEAM moves grows with team size (helps large teams that can't all fit on the exact center).")]
    public bool scaleArrivalBufferWithTeamSize = true;

    [Tooltip("Extra arrival buffer added when clearing TEAM pins. The extra grows as: perSqrtMember * sqrt(teamSize - 1).")]
    public float teamArrivalExtraBufferPerSqrtMember = 0.35f;

    [Tooltip("Clamp for the extra buffer added from team size.")]
    public float teamArrivalMaxExtraBuffer = 3.0f;
    [Header("Stop On Arrival")]
    [Tooltip("If true, when a move is considered complete (pins cleared), we also stop agents and clear their paths to prevent 'running in place' from crowding/avoidance.")]
    public bool stopAgentsOnArrival = true;


    [Header("Join Settings")]
    [Tooltip("Desired spacing (meters) from the team leader (the last-clicked ally) when joining. Larger = more spread.")]
    public float joinLeaderRadius = 2.0f;

    [Tooltip("Extra buffer added to stoppingDistance when considering the joiner \"arrived\" at their offset slot.")]
    public float joinArriveThreshold = 0.35f;

    [Tooltip("How often we refresh the join destination while following a moving leader (seconds).")]
    public float joinFollowRefreshInterval = 0.15f;

    [Header("Join Scaling")]
    [Tooltip("If true, join tolerances scale gently with team size (helps larger teams where members bump each other).")]
    public bool scaleJoinWithTeamSize = true;

    [Tooltip("Extra join leader radius added as: perSqrtMember * sqrt(teamSize - 1).")]
    public float joinExtraLeaderRadiusPerSqrtMember = 0.25f;

    [Tooltip("Clamp for extra join leader radius from team size.")]
    public float joinMaxExtraLeaderRadius = 2.0f;

    [Tooltip("Extra arrive threshold added as: perSqrtMember * sqrt(teamSize - 1).")]
    public float joinExtraArriveThresholdPerSqrtMember = 0.35f;

    [Tooltip("Clamp for extra arrive threshold from team size.")]
    public float joinMaxExtraArriveThreshold = 2.0f;

    [Header("Join Stop (optional)")]
    [Tooltip("If true, when the joiner reaches their slot we stop and clear path to prevent running-in-place from crowding/avoidance.")]
    public bool stopJoinerOnJoinComplete = true;

    // -------------------- JOIN ROUTE (for DirectionArrowPreview) --------------------
    // DirectionArrowPreview.cs looks for these names (via reflection) to show an arrow while a join leader is in-route.
    public bool IsJoinMoveInProgress { get; private set; }
    public Transform JoinMoveLeader { get; private set; }
    public Transform JoinMoveTarget { get; private set; }

    private Coroutine joinMoveRoutine;

    private Coroutine followFormationRoutine;

    private Coroutine pinClearRoutine;

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
    }

    private void HandleMoveRequested(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        ExecuteMoveOrder(selection, destination);
    }

    private void HandleFollowRequested(IReadOnlyList<GameObject> selection, GameObject targetUnit)
    {
        ExecuteFollowOrder(selection, targetUnit);
    }

    // ✅ Needed by CommandQueue.FlushMoves(executor)
    public void ExecuteMoveOrder(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        if (selection == null || selection.Count == 0) return;

        // ✅ Team expansion (team members move together)
        List<GameObject> expanded = ExpandSelectionForTeams(selection);

        // Any new command breaks TEAM hold-ground.
        // Cancel any ongoing team-follow formation routine (move order overrides follow).
        StopFollowFormationRoutine();

        // New move order overrides any pending arrival watcher.
        StopPinClearRoutine();

        // IMPORTANT: a point-move replaces any "follow" travel targets.
        ClearTravelFollowTargets(expanded);

        // Hint: team / unit move feedback
        ShowMoveHint(expanded);

        List<NavMeshAgent> agents = GatherAgents(expanded);
        if (agents.Count == 0) return;

        // If this move order is for a single team selection, increase formation spacing so members don't
        // try to occupy the same center point (especially right after join).
        float spacing = formationSpacing;
        if (TeamManager.Instance != null && TryGetSingleTeamForSelection(expanded, out Team selTeam) && selTeam != null)
        {
            spacing = formationSpacing * Mathf.Max(1f, teamMoveSpacingMultiplier);
        }

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

                // Manual hold: move orders create a local hold zone so allies don't chase enemies far away.
                var ally = a.GetComponent<AllyController>();
                if (ally != null)
                    ally.SetManualHoldPoint(destination);
            }

            StartPinClearRoutine(expanded, agents);
            return;
        }

        // Formation
        int cols = Mathf.Max(1, formationColumns);
        spacing = Mathf.Max(0.1f, spacing);

        ApplyFormationToAgents(
            agents,
            destination,
            spacing,
            cols,
            sampleRadius: 2.5f,
            forceUnstop: true
        );

        StartPinClearRoutine(expanded, agents);
    }

    /// <summary>
    /// Follow a moving target (enemy) by setting each selected ally's AllyController.target.
    /// The AllyController will update the NavMesh destination toward the target's current position.
    /// </summary>
    public void ExecuteFollowOrder(IReadOnlyList<GameObject> selection, GameObject targetUnit)
    {
        if (selection == null || selection.Count == 0) return;
        if (targetUnit == null) return;

        // New follow order overrides any prior follow-formation routine
        StopFollowFormationRoutine();

        // Follow order overrides any pending arrival watcher (destination may change).
        StopPinClearRoutine();

        // Expand selection to whole team if requested
        List<GameObject> expanded = (expandFollowToWholeTeam ? ExpandSelectionForTeams(selection) : UniqueNonNull(selection));

        // Any new command breaks TEAM hold-ground.
        Transform targetT = targetUnit != null ? targetUnit.transform : null;

        bool isCombatTarget = targetUnit != null && (targetUnit.CompareTag("Enemy") || targetUnit.CompareTag("Boss"));
        if (targetT == null) return;


        // If we're attacking/following an enemy, make it aggro onto an ally so it doesn't ignore allies and only react to the player.
        // We'll pick a stable attacker transform (team anchor / first ally in selection).
        Transform attackerT = null;
        if (TeamManager.Instance != null && TryGetSingleTeamForSelection(expanded, out Team selTeam) && selTeam != null)
        {
            if (selTeam.Anchor != null) attackerT = selTeam.Anchor;
            else if (selTeam.Members != null && selTeam.Members.Count > 0 && selTeam.Members[0] != null) attackerT = selTeam.Members[0];
        }
        if (attackerT == null)
        {
            for (int i = 0; i < expanded.Count; i++)
            {
                var go = expanded[i];
                if (go == null) continue;
                if (!go.CompareTag("Ally")) continue;
                attackerT = go.transform;
                break;
            }
        }

        if (attackerT != null)
            TryAggroEnemyToAttacker(targetT, attackerT);
        // If a follow starts, it should cancel any join-route coroutine (so arrows/state don't lie).
        // (Optional safety; doesn't change join team membership.)
        if (joinMoveRoutine != null)
        {
            StopCoroutine(joinMoveRoutine);
            joinMoveRoutine = null;
            ClearJoinRouteState();
        }

        // -------------------- COMBAT FOLLOW / ATTACK --------------------
        // If the follow target is an enemy/boss, we want AllyController to run its own standoff + strafe combat.
        // Team formation-follow tends to override combat movement (it keeps setting destinations), so we bypass it here.
        if (isCombatTarget)
        {
            // Stop any previous formation-follow coroutine so it doesn't fight our combat movement.
            StopFollowFormationRoutine();

            // Force selected allies to engage this target directly.
            for (int i = 0; i < expanded.Count; i++)
            {
                var go = expanded[i];
                if (go == null) continue;
                if (!go.CompareTag("Ally")) continue;

                var ally = go.GetComponent<AllyController>();
                if (ally != null)
                {
                    ally.target = null;
                    ally.ForceCombatTarget(targetT);
                }
            }

            // Clear the destination pin when the target is destroyed, so attack/follow pins don't linger.
            if (TeamManager.Instance != null && TryGetSingleTeamForSelection(expanded, out Team atkTeam) && atkTeam != null)
                StartFollowPinClearRoutine_Team(atkTeam, targetT);

            if (showMoveHints)
                HintSystem.Show("Attacking target.", moveHintDuration);

            return;
        }

        // ✅ Team formation-follow: keep members in formation while chasing to prevent piling onto one center point.
        if (useFormationWhileFollowingTeam &&
            TeamManager.Instance != null &&
            TryGetSingleTeamForSelection(expanded, out Team team) &&
            team != null)
        {
            // Prevent AllyController from forcing everyone to chase the exact same center point.
            for (int i = 0; i < expanded.Count; i++)
            {
                var go = expanded[i];
                if (go == null) continue;
                if (!go.CompareTag("Ally")) continue;

                var ally = go.GetComponent<AllyController>();
                if (ally != null)
                {
                    ally.target = null;
                }
            }

            followFormationRoutine = StartCoroutine(FollowTargetAsFormationRoutine(team, targetT));

            // Clear the destination pin when the target is destroyed, so attack/follow pins don't linger.
            StartFollowPinClearRoutine_Team(team, targetT);

            if (showMoveHints)
                HintSystem.Show("Following target (formation).", moveHintDuration);

            return;
        }

        // Default single-unit (or non-team) follow: set AllyController.target and kick the agent once.
        for (int i = 0; i < expanded.Count; i++)
        {
            var go = expanded[i];
            if (go == null) continue;
            if (!go.CompareTag("Ally")) continue;

            var ally = go.GetComponent<AllyController>();
            if (ally != null)
            {
                ally.ClearManualHoldPoint();
                ally.target = targetT;
            }

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null) agent = go.GetComponentInChildren<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled)
            {
                agent.isStopped = false;
                agent.SetDestination(targetT.position);
            }
        }

        // For non-team follow, still clear any pin that may have been placed for this attack/follow.
        StartFollowPinClearRoutine_Units(expanded, targetT);

        if (showMoveHints)
        {
            // Keep it simple; reuse moveHintDuration.
            HintSystem.Show("Following target.", moveHintDuration);
        }

    }

    // -------------------- ENEMY AGGRO HELPERS --------------------
    /// <summary>
    /// Ensure an enemy switches its combat target to the attacker (ally) so it doesn't ignore allies and only chase the player.
    /// Supports both Enemy2Controller and EnemyController (if present).
    /// </summary>
    private void TryAggroEnemyToAttacker(Transform enemyTransform, Transform attackerTransform)
    {
        if (enemyTransform == null || attackerTransform == null) return;

        // Your project currently uses Enemy2Controller on enemies (per inspector), but we support both just in case.
        var e2 = enemyTransform.GetComponentInParent<Enemy2Controller>();
        if (e2 != null)
        {
            e2.SetCombatTarget(attackerTransform);
            return;
        }

        var e1 = enemyTransform.GetComponentInParent<EnemyController>();
        if (e1 != null)
        {
            // If your EnemyController uses a different API, adjust here.
            // We try common method names safely via reflection-free direct calls only.
            // (No-op if missing in this project.)
            try { e1.SendMessage("SetCombatTarget", attackerTransform, SendMessageOptions.DontRequireReceiver); }
            catch { /* ignore */ }
        }
    }

    private void ClearTravelFollowTargets(IReadOnlyList<GameObject> expanded)
    {
        if (expanded == null) return;

        for (int i = 0; i < expanded.Count; i++)
        {
            var go = expanded[i];
            if (go == null) continue;
            if (!go.CompareTag("Ally")) continue;

            var ally = go.GetComponent<AllyController>();
            if (ally != null && ally.target != null)
                ally.target = null;
        }
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

            // Join is a new command: break TEAM hold-ground for both sides.
            if (leaderGO == targetGO) return;

            // Allow JOIN -> Player to convert a single unteamed ally into a Player follower.
            if (targetGO.CompareTag("Player"))
            {
                // Only allow a single ally (not an entire team selection).
                int selCount = selection != null ? selection.Count : 0;
                if (selCount > 1)
                {
                    HintSystem.Show("Select a single ally to join the player.", 2f);
                    return;
                }

                if (!leaderGO.CompareTag("Ally"))
                {
                    Debug.Log($"[Join->Player] Ignored: leader must be an Ally. leader={leaderGO.name}");
                    return;
                }

                // Keep teams separate from player followers.
                if (TeamManager.Instance != null)
                {
                    Team leaderTeamForFollowerCheck = TeamManager.Instance.GetTeamOf(leaderGO.transform);
                    if (leaderTeamForFollowerCheck != null)
                    {
                        HintSystem.Show("Can't add a teamed ally as a player follower.", 2f);
                        return;
                    }
                }

                // Add to player followers (uses the follower-slot system).
                var follow = PlayerSquadFollowSystem.EnsureExists();
                bool added = follow.TryAddFollowerDirect(leaderGO.transform, showBlockedHint: true);
                if (added)
                    HintSystem.Show("Ally joined you.", 1.5f);

                return;
            }

            if (!leaderGO.CompareTag("Ally") || !targetGO.CompareTag("Ally"))
            {
                Debug.Log($"[Join] Ignored: must click Ally -> Ally (or Player). leader={leaderGO.name} target={targetGO.name}");
                return;
            }

            if (TeamManager.Instance == null)
            {
                Debug.LogError("[Join] TeamManager.Instance is NULL. Add TeamManager to the scene.");
                return;
            }
            // Detect TEAM recruit: if a whole team is selected and you click an unteamed ally,
            // we add them to the team BUT move the recruit to the team (not the team to the recruit).
            Team leaderTeam = TeamManager.Instance.GetTeamOf(leaderGO.transform);
            Team targetTeam = TeamManager.Instance.GetTeamOf(targetGO.transform);

            bool recruitIntoTeam = false;
            Transform recruitMoveTarget = null;

            if (leaderTeam != null && targetTeam == null && selection != null && selection.Count > 1)
            {
                // Ensure the selection represents a single team (the leader's team).
                List<GameObject> expandedSel = ExpandSelectionForTeams(selection);
                if (TryGetSingleTeamForSelection(expandedSel, out Team selTeam) && selTeam != null && selTeam == leaderTeam)
                {
                    recruitIntoTeam = true;

                    // Prefer moving recruits to the team's Anchor (star holder), else fall back to the join source.
                    recruitMoveTarget = leaderTeam.Anchor != null ? leaderTeam.Anchor : leaderGO.transform;
                }
            }

            // ✅ Create/merge team immediately so overlay sees it
            Team team = TeamManager.Instance.JoinUnits(leaderGO.transform, targetGO.transform);
            Debug.Log($"[Join] JoinUnits called. Team={(team != null ? team.Id.ToString() : "null")}");

            // ✅ Move: normal join = move leader -> clicked target.
            // TEAM recruit = move clicked recruit -> existing team anchor.
            GameObject moveLeaderGO = leaderGO;
            GameObject moveTargetGO = targetGO;

            if (recruitIntoTeam && recruitMoveTarget != null)
            {
                moveLeaderGO = targetGO;                    // recruit walks
                moveTargetGO = recruitMoveTarget.gameObject; // to the team
            }

            // ✅ Move the joiner to the target, and mark as in-route so selection can block
            if (joinMoveRoutine != null)
            {
                StopCoroutine(joinMoveRoutine);
                joinMoveRoutine = null;
                ClearJoinRouteState();
            }

            // Expose join-route state for DirectionArrowPreview
            IsJoinMoveInProgress = true;
            JoinMoveLeader = moveLeaderGO.transform;

            // Join is a new player command; clear any manual hold zone on the leader.
            var leaderAlly = leaderGO.GetComponent<AllyController>();
            if (leaderAlly != null)
                leaderAlly.ClearManualHoldPoint();
            JoinMoveTarget = moveTargetGO.transform;

            joinMoveRoutine = StartCoroutine(MoveLeaderToJoinTargetRoutine(moveLeaderGO, moveTargetGO));

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

        // ---- JOIN WITH SPACING ----
        // We keep the team leader as the last-clicked ally (targetGO).
        // The joiner (leaderGO) moves to an offset "slot" around the leader instead of stacking on top.

        // Scale join tolerances with team size (helps larger teams where agents bump each other).
        int joinTeamSize = 2;
        if (scaleJoinWithTeamSize && TeamManager.Instance != null)
        {
            Team t = TeamManager.Instance.GetTeamOf(targetT);
            if (t != null && t.Members != null) joinTeamSize = Mathf.Max(2, t.Members.Count);
        }

        float scaledJoinLeaderRadius = joinLeaderRadius;
        float scaledJoinArriveThreshold = joinArriveThreshold;
        if (scaleJoinWithTeamSize)
        {
            float sqrtN = Mathf.Sqrt(Mathf.Max(0, joinTeamSize - 1));
            float extraR = joinExtraLeaderRadiusPerSqrtMember * sqrtN;
            extraR = Mathf.Min(extraR, joinMaxExtraLeaderRadius);
            scaledJoinLeaderRadius += Mathf.Max(0f, extraR);

            float extraA = joinExtraArriveThresholdPerSqrtMember * sqrtN;
            extraA = Mathf.Min(extraA, joinMaxExtraArriveThreshold);
            scaledJoinArriveThreshold += Mathf.Max(0f, extraA);
        }
        Vector3 approachDir = (leaderT.position - targetT.position);
        approachDir.y = 0f;

        if (approachDir.sqrMagnitude < 0.0001f)
        {
            // Fallback to a stable direction if they start overlapped.
            approachDir = targetT.right;
            approachDir.y = 0f;
            if (approachDir.sqrMagnitude < 0.0001f) approachDir = Vector3.right;
        }

        approachDir = approachDir.normalized;

        Vector3 GetJoinSlot()
        {
            Vector3 desired = targetT.position + approachDir * Mathf.Max(0.1f, scaledJoinLeaderRadius);

            // Snap to nearest navmesh so the joiner doesn't get stuck aiming off-mesh.
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                desired = hit.position;

            return desired;
        }

        agent.isStopped = false;
        Vector3 slotPos = GetJoinSlot();
        agent.SetDestination(slotPos);

        // IMPORTANT: Do NOT set AllyController.target here, because that would force the joiner
        // to chase the leader's exact position and stack on top. We refresh the offset destination instead.
        var leaderAlly = leaderGO.GetComponent<AllyController>();

        float threshold = Mathf.Max(scaledJoinArriveThreshold, agent.stoppingDistance + 0.05f);
        float joinFollowRefreshTimer = 0f;

        while (leaderGO != null && targetGO != null)
        {
            // Recompute the slot (leader may move).
            slotPos = GetJoinSlot();

            float d = Vector3.Distance(leaderT.position, slotPos);
            if (d <= threshold)
                break;

            // Refresh the destination periodically so we keep a stable offset as the leader moves.
            joinFollowRefreshTimer -= Time.deltaTime;
            if (joinFollowRefreshTimer <= 0f)
            {
                agent.SetDestination(slotPos);
                joinFollowRefreshTimer = Mathf.Max(0.05f, joinFollowRefreshInterval);
            }

            yield return null;
        }

        // Stop the joiner so it doesn't "run in place" from crowding/avoidance.
        if (stopJoinerOnJoinComplete)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.autoBraking = true;
        }

        // (No AllyController.target follow for join-with-spacing.)

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

    private void StopFollowFormationRoutine()
    {
        if (followFormationRoutine != null)
        {
            StopCoroutine(followFormationRoutine);
            followFormationRoutine = null;
        }
    }

    private void StopPinClearRoutine()
    {
        if (pinClearRoutine != null)
        {
            StopCoroutine(pinClearRoutine);
            pinClearRoutine = null;
        }
    }

    private void StartPinClearRoutine(List<GameObject> expandedSelection, List<NavMeshAgent> agents)
    {
        if (!clearDestinationPinsOnArrival) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;
        if (agents == null || agents.Count == 0) return;

        // Prefer team-wide clearing when this move represents a single team.
        if (TeamManager.Instance != null && TryGetSingleTeamForSelection(expandedSelection, out Team team) && team != null)
        {
            pinClearRoutine = StartCoroutine(ClearPinsWhenTeamArrivesRoutine(team, new List<NavMeshAgent>(agents)));
        }
        else
        {
            // Fallback: clear pins for these units when they arrive.
            pinClearRoutine = StartCoroutine(ClearPinsWhenUnitsArriveRoutine(expandedSelection, new List<NavMeshAgent>(agents)));
        }
    }

    private void StartFollowPinClearRoutine_Team(Team team, Transform targetT)
    {
        if (!clearDestinationPinsOnTargetDestroyed) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;
        if (team == null) return;
        if (targetT == null) return;

        // Follow order overrides any pending arrival watcher.
        StopPinClearRoutine();
        pinClearRoutine = StartCoroutine(ClearPinsWhenFollowTargetDestroyed_TeamRoutine(team, targetT));
    }

    private void StartFollowPinClearRoutine_Units(List<GameObject> units, Transform targetT)
    {
        if (!clearDestinationPinsOnTargetDestroyed) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;
        if (units == null || units.Count == 0) return;
        if (targetT == null) return;

        // Follow order overrides any pending arrival watcher.
        StopPinClearRoutine();
        pinClearRoutine = StartCoroutine(ClearPinsWhenFollowTargetDestroyed_UnitsRoutine(new List<GameObject>(units), targetT));
    }

    private IEnumerator ClearPinsWhenFollowTargetDestroyed_TeamRoutine(Team team, Transform targetT)
    {
        if (team == null) yield break;
        float interval = Mathf.Max(0.05f, pinArrivalCheckInterval);

        // Wait until the target is destroyed / lost.
        while (targetT != null)
            yield return new WaitForSeconds(interval);

        if (MoveDestinationMarkerSystem.Instance != null)
        {
            MoveDestinationMarkerSystem.Instance.ClearForTeam(team);

            // Also clear any per-member pins that might have been used.
            if (team.Members != null && team.Members.Count > 0)
            {
                List<GameObject> gos = new List<GameObject>(team.Members.Count);
                for (int i = 0; i < team.Members.Count; i++)
                {
                    var tr = team.Members[i];
                    if (tr == null) continue;
                    gos.Add(tr.gameObject);
                }

                if (gos.Count > 0)
                    MoveDestinationMarkerSystem.Instance.ClearForUnits(gos.ToArray());
            }
        }

        pinClearRoutine = null;
    }

    private IEnumerator ClearPinsWhenFollowTargetDestroyed_UnitsRoutine(List<GameObject> units, Transform targetT)
    {
        float interval = Mathf.Max(0.05f, pinArrivalCheckInterval);

        while (targetT != null)
            yield return new WaitForSeconds(interval);

        if (MoveDestinationMarkerSystem.Instance != null && units != null && units.Count > 0)
            MoveDestinationMarkerSystem.Instance.ClearForUnits(units.ToArray());

        pinClearRoutine = null;
    }

    private IEnumerator ClearPinsWhenTeamArrivesRoutine(Team team, List<NavMeshAgent> agents)
    {
        if (team == null) yield break;
        if (agents == null || agents.Count == 0) yield break;

        float interval = Mathf.Max(0.05f, pinArrivalCheckInterval);

        // Let paths compute first.
        yield return null;

        while (true)
        {
            // If we lost the marker system, abort.
            if (MoveDestinationMarkerSystem.Instance == null)
                break;

            // Prune dead/disabled agents.
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                var a = agents[i];
                if (a == null || !a.isActiveAndEnabled)
                    agents.RemoveAt(i);
            }

            if (agents.Count == 0)
                break;

            float extraStop = 0f;
            if (scaleArrivalBufferWithTeamSize)
            {
                int n = agents.Count;
                extraStop = teamArrivalExtraBufferPerSqrtMember * Mathf.Sqrt(Mathf.Max(0, n - 1));
                extraStop = Mathf.Min(extraStop, teamArrivalMaxExtraBuffer);
            }

            bool allArrived = true;
            for (int i = 0; i < agents.Count; i++)
            {
                if (!IsAgentArrived(agents[i], extraStop))
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
            {
                if (stopAgentsOnArrival) StopAgents(agents);
                break;
            }

            yield return new WaitForSeconds(interval);
        }

        // Clear the team's destination pin(s).
        if (MoveDestinationMarkerSystem.Instance != null)
        {
            MoveDestinationMarkerSystem.Instance.ClearForTeam(team);

            // Also clear any per-member pins that might have been used.
            if (team.Members != null && team.Members.Count > 0)
            {
                List<GameObject> gos = new List<GameObject>(team.Members.Count);
                for (int i = 0; i < team.Members.Count; i++)
                {
                    var tr = team.Members[i];
                    if (tr == null) continue;
                    gos.Add(tr.gameObject);
                }

                if (gos.Count > 0)
                    MoveDestinationMarkerSystem.Instance.ClearForUnits(gos.ToArray());
            }
        }

        pinClearRoutine = null;
    }

    private IEnumerator ClearPinsWhenUnitsArriveRoutine(List<GameObject> units, List<NavMeshAgent> agents)
    {
        if (agents == null || agents.Count == 0) yield break;
        float interval = Mathf.Max(0.05f, pinArrivalCheckInterval);

        // Let paths compute first.
        yield return null;

        while (true)
        {
            if (MoveDestinationMarkerSystem.Instance == null)
                break;

            // Prune dead/disabled agents.
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                var a = agents[i];
                if (a == null || !a.isActiveAndEnabled)
                    agents.RemoveAt(i);
            }

            if (agents.Count == 0)
                break;

            bool allArrived = true;
            for (int i = 0; i < agents.Count; i++)
            {
                if (!IsAgentArrived(agents[i], 0f))
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
            {
                if (stopAgentsOnArrival) StopAgents(agents);
                break;
            }

            yield return new WaitForSeconds(interval);
        }

        if (MoveDestinationMarkerSystem.Instance != null && units != null && units.Count > 0)
            MoveDestinationMarkerSystem.Instance.ClearForUnits(units.ToArray());

        pinClearRoutine = null;
    }


    private void StopAgents(List<NavMeshAgent> agents)
    {
        if (agents == null) return;
        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null || !a.isActiveAndEnabled) continue;

            // Stop movement and clear remaining path so animation/velocity settle.
            a.isStopped = true;
            a.ResetPath();

            // Ensure braking is enabled when we stop.
            a.autoBraking = true;
        }
    }

    private bool IsAgentArrived(NavMeshAgent agent)
    {
        return IsAgentArrived(agent, 0f);
    }

    private bool IsAgentArrived(NavMeshAgent agent, float extraStopBuffer)
    {
        if (agent == null || !agent.isActiveAndEnabled) return true;

        // If path is still being computed, not arrived.
        if (agent.pathPending) return false;

        // If no path and not moving, consider arrived.
        if (!agent.hasPath && agent.velocity.sqrMagnitude < 0.01f) return true;

        float remain = agent.remainingDistance;
        if (float.IsInfinity(remain) || float.IsNaN(remain))
            return agent.velocity.sqrMagnitude < 0.01f;

        float threshold = Mathf.Max(agent.stoppingDistance, 0.05f) + pinArrivalStopBuffer + Mathf.Max(0f, extraStopBuffer);
        return remain <= threshold;
    }



    private IEnumerator FollowTargetAsFormationRoutine(Team team, Transform targetT)
    {
        if (team == null) yield break;

        // Gather agents from the team (not from selection), so merges/joins stay consistent.
        List<NavMeshAgent> agents = new List<NavMeshAgent>();
        if (team.Members != null)
        {
            for (int i = 0; i < team.Members.Count; i++)
            {
                var m = team.Members[i];
                if (m == null) continue;

                var a = m.GetComponent<NavMeshAgent>();
                if (a == null) a = m.GetComponentInChildren<NavMeshAgent>();
                if (a != null && a.isActiveAndEnabled)
                    agents.Add(a);
            }
        }

        if (agents.Count <= 1) yield break;

        // Stable ordering so slots don't shuffle as you chase
        if (stableFormationOrdering)
        {
            agents.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return a.transform.GetInstanceID().CompareTo(b.transform.GetInstanceID());
            });
        }

        float spacing = formationSpacing * Mathf.Max(1f, teamMoveSpacingMultiplier);
        spacing = Mathf.Max(0.25f, spacing);

        int cols = Mathf.Max(1, formationColumns);
        float interval = Mathf.Max(0.05f, followFormationRefreshInterval);
        float sampleR = Mathf.Max(0.5f, followFormationNavmeshSampleRadius);

        // While target exists, keep nudging the team into formation slots around the target position.
        while (targetT != null)
        {
            Vector3 center = targetT.position;
            center.y = 0f;

            ApplyFormationToAgents(agents, center, spacing, cols, sampleRadius: sampleR, forceUnstop: false);

            yield return new WaitForSeconds(interval);
        }

        // Target was destroyed / lost: "settle" the team into a spaced formation around the current anchor
        // to prevent everyone trying to occupy the same center point after combat.
        Vector3 settle = (team.Anchor != null ? team.Anchor.position : agents[0].transform.position);
        settle.y = 0f;

        ApplyFormationToAgents(agents, settle, spacing, cols, sampleRadius: sampleR, forceUnstop: true);

        followFormationRoutine = null;
    }


    private void ApplyFormationToAgents(List<NavMeshAgent> agents, Vector3 center, float spacing, int cols, float sampleRadius, bool forceUnstop)
    {
        if (agents == null || agents.Count == 0) return;

        // Default axes (world-aligned)
        Vector3 rightAxis = Vector3.right;
        Vector3 backAxis = Vector3.forward;

        if (orientFormationToMoveDirection)
        {
            Vector3 agentsCenter = ComputeAgentsCenter(agents);
            Vector3 forward = center - agentsCenter;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.0001f)
            {
                forward.Normalize();
                rightAxis = Vector3.Cross(Vector3.up, forward);
                if (rightAxis.sqrMagnitude < 0.0001f) rightAxis = Vector3.right;
                rightAxis.Normalize();

                // Depth axis points *back* from the destination/target, so formations "trail" behind the move direction.
                backAxis = -forward;
            }
        }

        switch (formationPattern)
        {
            case FormationPattern.Line:
                ApplyLineFormationToAgents(agents, center, spacing, sampleRadius, forceUnstop, rightAxis);
                break;

            case FormationPattern.Wedge:
                ApplyWedgeFormationToAgents(agents, center, spacing, sampleRadius, forceUnstop, rightAxis, backAxis);
                break;

            default:
                ApplyGridFormationToAgents(agents, center, spacing, cols, sampleRadius, forceUnstop, rightAxis, backAxis);
                break;
        }
    }

    private static Vector3 ComputeAgentsCenter(List<NavMeshAgent> agents)
    {
        if (agents == null || agents.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        int n = 0;

        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null) continue;
            sum += a.transform.position;
            n++;
        }

        if (n <= 0) return Vector3.zero;
        Vector3 c = sum / n;
        c.y = 0f;
        return c;
    }

    private static void ApplyGridFormationToAgents(
        List<NavMeshAgent> agents,
        Vector3 center,
        float spacing,
        int cols,
        float sampleRadius,
        bool forceUnstop,
        Vector3 rightAxis,
        Vector3 backAxis)
    {
        if (agents == null || agents.Count == 0) return;

        cols = Mathf.Max(1, cols);
        spacing = Mathf.Max(0.1f, spacing);

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

            Vector3 targetPos = center + (rightAxis * x) + (backAxis * z);

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                targetPos = hit.position;

            if (forceUnstop)
                agent.isStopped = false;

            agent.SetDestination(targetPos);

            // Manual hold: formation move slots also create hold zones per-unit.
            var ally = agent.GetComponent<AllyController>();
            if (ally != null)
                ally.SetManualHoldPoint(targetPos);

        }
    }

    private static void ApplyLineFormationToAgents(
        List<NavMeshAgent> agents,
        Vector3 center,
        float spacing,
        float sampleRadius,
        bool forceUnstop,
        Vector3 rightAxis)
    {
        if (agents == null || agents.Count == 0) return;

        spacing = Mathf.Max(0.1f, spacing);

        int count = agents.Count;
        float width = (count - 1) * spacing;

        for (int i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            if (agent == null) continue;

            float x = (i * spacing) - width * 0.5f;
            Vector3 targetPos = center + (rightAxis * x);

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                targetPos = hit.position;

            if (forceUnstop)
                agent.isStopped = false;

            agent.SetDestination(targetPos);

            // Manual hold: formation move slots also create hold zones per-unit.
            var ally = agent.GetComponent<AllyController>();
            if (ally != null)
                ally.SetManualHoldPoint(targetPos);

        }
    }

    private static void ApplyWedgeFormationToAgents(
        List<NavMeshAgent> agents,
        Vector3 center,
        float spacing,
        float sampleRadius,
        bool forceUnstop,
        Vector3 rightAxis,
        Vector3 backAxis)
    {
        if (agents == null || agents.Count == 0) return;

        spacing = Mathf.Max(0.1f, spacing);

        // Slot 0 = tip of the wedge at center. Every row behind has 2 slots (left/right).
        for (int i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            if (agent == null) continue;

            Vector3 targetPos = center;

            if (i > 0)
            {
                int k = i - 1;
                int row = (k / 2) + 1; // 1,2,3...
                bool left = (k % 2 == 0);

                float side = left ? -1f : 1f;
                float x = side * row * spacing;
                float z = row * spacing;

                targetPos = center + (rightAxis * x) + (backAxis * z);
            }

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                targetPos = hit.position;

            if (forceUnstop)
                agent.isStopped = false;

            agent.SetDestination(targetPos);

            // Manual hold: formation move slots also create hold zones per-unit.
            var ally = agent.GetComponent<AllyController>();
            if (ally != null)
                ally.SetManualHoldPoint(targetPos);

        }
    }
    // -------------------- SPLIT (placeholder) --------------------
    private void HandleSplitRequested(IReadOnlyList<GameObject> selection)
    {
        Debug.Log("CommandExecutor: SPLIT requested (not implemented yet).");
    }
}