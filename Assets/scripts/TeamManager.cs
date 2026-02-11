using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    private readonly List<Team> teams = new();
    public IReadOnlyList<Team> Teams => teams;

    private int nextId = 1;

    [Header("Hints (optional)")]
    [Tooltip("Show a hint toast when a team is formed or its member count changes.")]
    public bool showTeamHints = true;

    [Tooltip("How long the team hint stays on screen.")]
    public float teamHintDuration = 2.5f;

    [Header("Formation / Spacing")]
    [Tooltip("Default spacing radius (meters) around the team leader (Anchor). Larger = more spread.")]
    public float defaultFormationRadius = 2.4f;


    [Tooltip("When merging two existing teams, multiply FormationRadius to reduce center pile-ups. 1 = no change.")]
    public float mergeFormationRadiusMultiplier = 1.35f;



    [Header("Merge Staging (reduces collisions)")]
    [Tooltip("If true, a quick 2-step merge is used: first push members outward to unique staging points, then apply the final formation. This prevents everyone pathing through the exact center.")]
    public bool applyStagedMergeOnTeamMerge = true;

    [Tooltip("Seconds to wait between staging push-out and final formation. Keep small (0.1-0.25).")]
    public float mergeStagingDelay = 0.15f;

    [Tooltip("Staging radius multiplier relative to FormationRadius (used during merges only). Larger = less center pile-up.")]
    public float mergeStagingRadiusMultiplier = 1.8f;

    [Tooltip("Minimum staging radius in meters (used during merges only).")]
    public float mergeStagingMinRadius = 3.5f;

    [Tooltip("Extra radius added per ring when there are more members than one ring can hold.")]
    public float ringRadiusStep = 1.2f;

    [Tooltip("How many slots per ring around the leader. 6 is a nice default.")]
    public int slotsPerRing = 6;

    [Tooltip("NavMesh sample radius for formation slot positions.")]
    public float navmeshSampleRadius = 2.5f;

    [Tooltip("If true, we immediately issue SetDestination() orders to spread members when teams form/update/merge.")]
    public bool applyFormationOnTeamChange = true;




    [Header("Dynamic Formation Scaling")]
    [Tooltip("If true, formation slot spacing and ring sizes scale automatically with team size and NavMeshAgent radius. Recommended for large teams.")]
    public bool useDynamicFormationSpacing = true;

    [Tooltip("Base desired spacing (meters) between allies in formation (small teams).")]
    public float baseSlotSpacing = 1.6f;

    [Tooltip("Additional spacing added per sqrt(memberCount). Larger teams spread out more.")]
    public float spacingPerSqrtMember = 0.35f;

    [Tooltip("Minimum spacing between allies (meters).")]
    public float minSlotSpacing = 1.2f;

    [Tooltip("Extra buffer added on top of NavMeshAgent radius-derived spacing.")]
    public float extraAgentBuffer = 0.45f;

    [Tooltip("Minimum slots per ring when dynamic spacing is enabled.")]
    public int minSlotsPerRing = 6;

    [Tooltip("If true, logs formation diagnostics (min distance between slots) in the Console.")]
    public bool logFormationDiagnostics = false;

    [Header("Team Anchor (Leader)")]
    [Tooltip("If true, teams will automatically switch their Anchor (star/leader) to another alive member when the current Anchor is dead/disabled.")]
    public bool autoSwitchAnchorWhenDead = true;

    [Tooltip("How often (seconds) we prune dead members + validate/switch the team Anchor.")]
    public float anchorMaintenanceInterval = 0.25f;

    private float _anchorMaintenanceTimer;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        _anchorMaintenanceTimer = Mathf.Max(0.05f, anchorMaintenanceInterval);
    }

    private void Update()
    {
        if (!autoSwitchAnchorWhenDead) return;

        _anchorMaintenanceTimer -= Time.deltaTime;
        if (_anchorMaintenanceTimer > 0f) return;

        _anchorMaintenanceTimer = Mathf.Max(0.05f, anchorMaintenanceInterval);
        MaintainTeams();
    }

    private void MaintainTeams()
    {
        for (int ti = teams.Count - 1; ti >= 0; ti--)
        {
            Team team = teams[ti];
            if (team == null)
            {
                teams.RemoveAt(ti);
                continue;
            }

            // Prune dead/disabled members.
            if (team.Members != null)
            {
                for (int mi = team.Members.Count - 1; mi >= 0; mi--)
                {
                    Transform m = team.Members[mi];
                    if (m == null || !m.gameObject.activeInHierarchy)
                        team.Members.RemoveAt(mi);
                }
            }

            // If the team has no living members left, remove it.
            if (team.Members == null || team.Members.Count == 0)
            {
                ClearTeamPin(team);
                teams.RemoveAt(ti);
                continue;
            }

            // Ensure the Anchor is a living member.
            bool anchorValid = team.Anchor != null
                               && team.Anchor.gameObject.activeInHierarchy
                               && team.Members.Contains(team.Anchor);

            if (!anchorValid)
            {
                Transform newAnchor = FindFirstAliveMember(team);
                if (newAnchor != null)
                {
                    team.Anchor = newAnchor;
                    ApplyFormationIfEnabled(team);
                }
            }
        }
    }

    private Transform FindFirstAliveMember(Team team)
    {
        if (team == null || team.Members == null) return null;

        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform m = team.Members[i];
            if (m != null && m.gameObject.activeInHierarchy)
                return m;
        }
        return null;
    }

    private void ShowTeamHint(Team team, string prefix)
    {
        if (!showTeamHints) return;
        if (team == null) return;

        int count = 0;
        if (team.Members != null)
        {
            for (int i = 0; i < team.Members.Count; i++)
                if (team.Members[i] != null) count++;
        }

        HintSystem.Show($"{prefix} Team {team.Id} has {count} {(count == 1 ? "ally" : "allies")}.", teamHintDuration);
    }

    private void ClearTeamPin(Team team)
    {
        if (team == null) return;
        if (MoveDestinationMarkerSystem.Instance != null)
            MoveDestinationMarkerSystem.Instance.ClearForTeam(team);
    }

    public Team GetTeamOf(Transform unit)
    {
        if (unit == null) return null;

        for (int i = 0; i < teams.Count; i++)
        {
            var t = teams[i];
            if (t != null && t.Contains(unit))
                return t;
        }

        return null;
    }


    // Patrol must stop as soon as a unit becomes part of a team.
    private static void DisablePatrolIfPresent(Transform unit)
    {
        if (unit == null) return;
        var patrol = unit.GetComponent<AllyPatrolPingPong>();
        if (patrol != null)
            patrol.SetPatrolEnabled(false);
    }

    private static void DisablePatrolForTeam(Team team)
    {
        if (team == null) return;

        if (team.Anchor != null)
            DisablePatrolIfPresent(team.Anchor);

        if (team.Members == null) return;
        for (int i = 0; i < team.Members.Count; i++)
            DisablePatrolIfPresent(team.Members[i]);
    }

    public Team JoinUnits(Transform leader, Transform joinTarget)
    {
        if (leader == null || joinTarget == null) return null;
        if (leader == joinTarget) return null;

        Team teamA = GetTeamOf(leader);
        Team teamB = GetTeamOf(joinTarget);

        // Same team already
        if (teamA != null && teamA == teamB)
        {
            teamA.Anchor = joinTarget; // keep star on second ally
            DisablePatrolForTeam(teamA);
            ApplyFormationIfEnabled(teamA);
            return teamA;
        }

        // Neither in a team -> create new
        if (teamA == null && teamB == null)
        {
            var created = CreateTeam(leader, joinTarget);
            created.Anchor = joinTarget; // keep star on second ally
            created.FormationRadius = Mathf.Max(0.1f, defaultFormationRadius);

            ShowTeamHint(created, "Team formed!");
            ClearTeamPin(created);
            DisablePatrolForTeam(created);
            ApplyFormationIfEnabled(created);
            return created;
        }

        // One team exists -> add other
        if (teamA != null && teamB == null)
        {
            // ✅ RECRUIT: add the unit to the existing team, but DO NOT move the team Anchor.
            // The star should stay where it already is.
            teamA.Add(joinTarget);
            teamA.FormationRadius = Mathf.Max(0.1f, teamA.FormationRadius > 0f ? teamA.FormationRadius : defaultFormationRadius);

            ShowTeamHint(teamA, "Team updated!");
            ClearTeamPin(teamA);
            DisablePatrolForTeam(teamA);
            ApplyFormationIfEnabled(teamA);
            return teamA;
        }

        if (teamA == null && teamB != null)
        {
            // ✅ RECRUIT: add the unit to the existing team, but DO NOT move the team Anchor.
            // The star should stay where it already is.
            teamB.Add(leader);
            teamB.FormationRadius = Mathf.Max(0.1f, teamB.FormationRadius > 0f ? teamB.FormationRadius : defaultFormationRadius);

            ShowTeamHint(teamB, "Team updated!");
            ClearTeamPin(teamB);
            DisablePatrolForTeam(teamB);
            ApplyFormationIfEnabled(teamB);
            return teamB;
        }

        // Both exist but different -> merge B into A
        if (teamA != null && teamB != null && teamA != teamB)
        {
            for (int i = 0; i < teamB.Members.Count; i++)
                teamA.Add(teamB.Members[i]);

            teams.Remove(teamB);

            // Keep star on second ally (the one you clicked last)
            teamA.Anchor = joinTarget;
            teamA.FormationRadius = Mathf.Max(0.1f, teamA.FormationRadius > 0f ? teamA.FormationRadius : defaultFormationRadius);


            teamA.FormationRadius *= Mathf.Clamp(mergeFormationRadiusMultiplier, 1f, 3f);
            ShowTeamHint(teamA, "Teams merged!");
            ClearTeamPin(teamA);
            ClearTeamPin(teamB);
            DisablePatrolForTeam(teamA);
            ApplyFormationAfterMerge(teamA);
            return teamA;
        }

        return null;
    }

    public Team CreateTeam(Transform a, Transform b)
    {
        if (a == null || b == null) return null;
        if (a == b) return null;

        Team t = new Team(nextId++, a, b);
        t.FormationRadius = Mathf.Max(0.1f, defaultFormationRadius);
        teams.Add(t);
        DisablePatrolForTeam(t);
        return t;
    }


    // Track active staged-merge coroutines per team so we don't stack them.
    private readonly Dictionary<int, Coroutine> _activeMergeStaging = new Dictionary<int, Coroutine>();

    private void ApplyFormationAfterMerge(Team team)
    {
        if (!applyFormationOnTeamChange) return;
        if (team == null) return;

        if (!applyStagedMergeOnTeamMerge)
        {
            ApplyFormationAroundAnchor(team);
            return;
        }

        // Restart staged merge for this team (last call wins).
        if (_activeMergeStaging.TryGetValue(team.Id, out Coroutine running) && running != null)
            StopCoroutine(running);

        _activeMergeStaging[team.Id] = StartCoroutine(ApplyStagedMergeFormation(team));
    }

    private System.Collections.IEnumerator ApplyStagedMergeFormation(Team team)
    {
        if (team == null || team.Anchor == null) yield break;
        if (team.Members == null || team.Members.Count == 0) yield break;

        Vector3 center = team.Anchor.position;
        center.y = 0f;

        // Stage 1: push everyone (except the anchor) outward to unique staging points,
        // so they do NOT all try to path through the exact anchor position.

        // Compute a spacing-aware staging radius that grows with team size.
        int totalMembers = team.Members.Count;
        float s = Mathf.Sqrt(Mathf.Max(1, totalMembers));
        float desiredSpacing = Mathf.Max(minSlotSpacing, baseSlotSpacing + spacingPerSqrtMember * s);

        // Also respect NavMeshAgent radius (bigger agents need more spacing).
        float maxAgentRadius = 0.5f;
        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform t = team.Members[i];
            if (t == null) continue;
            NavMeshAgent a = t.GetComponent<NavMeshAgent>();
            if (a != null) maxAgentRadius = Mathf.Max(maxAgentRadius, a.radius);
        }
        float agentMinSpacing = (maxAgentRadius * 2.2f) + Mathf.Max(0f, extraAgentBuffer);
        desiredSpacing = Mathf.Max(desiredSpacing, agentMinSpacing);

        float baseR = team.FormationRadius > 0f ? team.FormationRadius : defaultFormationRadius;
        baseR = Mathf.Max(0.1f, baseR);

        // Staging radius: keep existing knobs, but also add a sqrt(teamSize) term so big merges spread out automatically.
        float stagingR = Mathf.Max(
            mergeStagingMinRadius,
            baseR * Mathf.Clamp(mergeStagingRadiusMultiplier, 1f, 10f),
            baseR + (desiredSpacing * s * 1.25f)
        );

        // Build a stable basis around the anchor to distribute points consistently.
        Vector3 right = team.Anchor.right; right.y = 0f;
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        Vector3 forward = Vector3.Cross(Vector3.up, right);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        // Collect members to stage (exclude anchor), stable order.
        List<Transform> toStage = new List<Transform>();
        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform t = team.Members[i];
            if (t == null) continue;
            if (t == team.Anchor) continue;
            toStage.Add(t);
        }
        toStage.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        int count = Mathf.Max(1, toStage.Count);
        float baseAngleDeg = (Mathf.Abs(team.Id) % 360); // stable offset per team
        float stepDeg = 360f / count;

        for (int i = 0; i < toStage.Count; i++)
        {
            Transform m = toStage[i];
            if (m == null) continue;

            float angle = (baseAngleDeg + (i * stepDeg)) * Mathf.Deg2Rad;

            Vector3 dir = (right * Mathf.Cos(angle)) + (forward * Mathf.Sin(angle));
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = right;

            Vector3 desired = center + dir.normalized * stagingR;

            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.5f, navmeshSampleRadius), NavMesh.AllAreas))
                desired = hit.position;

            NavMeshAgent agent = m.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                agent.isStopped = false;
                agent.SetDestination(desired);
            }
        }

        // Small delay so agents pick lanes before final formation slots are issued.
        if (mergeStagingDelay > 0f)
            yield return new WaitForSeconds(mergeStagingDelay);
        else
            yield return null;

        // Stage 2: apply the final formation slots.
        ApplyFormationAroundAnchor(team);

        // Done; clear handle.
        if (_activeMergeStaging.ContainsKey(team.Id))
            _activeMergeStaging[team.Id] = null;
    }

    private void ApplyFormationIfEnabled(Team team)
    {
        if (!applyFormationOnTeamChange) return;
        ApplyFormationAroundAnchor(team);
    }

    /// <summary>
    /// Spreads non-anchor members around the Anchor using rings of slots.
    /// Keeps Anchor (leader) in place, so the star stays on the leader (last-clicked ally).
    /// </summary>
    private void ApplyFormationAroundAnchor(Team team)
    {
        if (team == null) return;
        if (team.Anchor == null) return;
        if (team.Members == null || team.Members.Count == 0) return;

        Vector3 center = team.Anchor.position;
        center.y = 0f;

        float baseRadius = team.FormationRadius > 0f ? team.FormationRadius : defaultFormationRadius;
        baseRadius = Mathf.Max(0.1f, baseRadius);

        // Build a stable basis around the leader
        Vector3 right = team.Anchor.right; right.y = 0f;
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();

        Vector3 forward = Vector3.Cross(Vector3.up, right);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        // Collect members to slot (exclude anchor)
        List<Transform> membersToSlot = new List<Transform>();
        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform m = team.Members[i];
            if (m == null) continue;
            if (m == team.Anchor) continue;
            membersToSlot.Add(m);
        }

        if (membersToSlot.Count == 0) return;

        // Precompute slot positions (rings around the anchor).
        // Dynamic mode: spacing grows with team size (and agent radius) and ring capacities grow with circumference,
        // so large teams naturally create more rings and larger buffers away from the center.
        List<Vector3> slots = new List<Vector3>(membersToSlot.Count);

        int totalMembers = team.Members.Count;
        float s = Mathf.Sqrt(Mathf.Max(1, totalMembers));

        float desiredSpacing = Mathf.Max(minSlotSpacing, baseSlotSpacing + spacingPerSqrtMember * s);

        // Respect agent radius (bigger agents need more spacing).
        float maxAgentRadius = 0.5f;
        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform t = team.Members[i];
            if (t == null) continue;
            NavMeshAgent a = t.GetComponent<NavMeshAgent>();
            if (a != null) maxAgentRadius = Mathf.Max(maxAgentRadius, a.radius);
        }
        float agentMinSpacing = (maxAgentRadius * 2.2f) + Mathf.Max(0f, extraAgentBuffer);

        if (useDynamicFormationSpacing)
            desiredSpacing = Mathf.Max(desiredSpacing, agentMinSpacing);

        // Grow the first ring radius a bit as teams get larger to create a bigger buffer from center.
        float ring0Radius = baseRadius;
        if (useDynamicFormationSpacing)
        {
            ring0Radius = Mathf.Max(ring0Radius, desiredSpacing * Mathf.Max(1.2f, 0.9f + 0.35f * (s - 1f)));
        }

        int ring = 0;
        while (slots.Count < membersToSlot.Count)
        {
            float r = useDynamicFormationSpacing
                ? (ring0Radius + ring * desiredSpacing)
                : (baseRadius + ring * Mathf.Max(0.1f, ringRadiusStep));

            int cap = useDynamicFormationSpacing
                ? Mathf.Max(minSlotsPerRing, Mathf.FloorToInt((2f * Mathf.PI * Mathf.Max(0.25f, r)) / Mathf.Max(0.25f, desiredSpacing)))
                : Mathf.Max(1, slotsPerRing);

            float angleStep = 360f / Mathf.Max(1, cap);

            for (int i = 0; i < cap && slots.Count < membersToSlot.Count; i++)
            {
                float angle = (i * angleStep) * Mathf.Deg2Rad;

                Vector3 dir = (right * Mathf.Cos(angle)) + (forward * Mathf.Sin(angle));
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = right;

                Vector3 desired = center + dir.normalized * r;

                // Snap to navmesh
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.5f, navmeshSampleRadius), NavMesh.AllAreas))
                    desired = hit.position;

                slots.Add(desired);
            }

            ring++;
        }

        if (logFormationDiagnostics && slots.Count > 1)
        {
            float minD = float.MaxValue;
            for (int a = 0; a < slots.Count; a++)
            {
                for (int b = a + 1; b < slots.Count; b++)
                {
                    float d = Vector3.Distance(slots[a], slots[b]);
                    if (d < minD) minD = d;
                }
            }
            Debug.Log($"[TeamManager] Formation slots: team={team.Id} members={totalMembers} spacing≈{desiredSpacing:0.00} ring0≈{ring0Radius:0.00} minSlotDist={minD:0.00}");
        }

        // Smart slot assignment (greedy nearest):
        // This reduces criss-crossing and "run into each other at the center" behavior during merges.
        bool[] slotTaken = new bool[slots.Count];

        // Stable order: sort by instance id so assignment doesn't shuffle every frame.
        membersToSlot.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        });

        for (int i = 0; i < membersToSlot.Count; i++)
        {
            Transform m = membersToSlot[i];
            if (m == null) continue;

            int bestSlot = -1;
            float bestDist = float.MaxValue;

            Vector3 mp = m.position;
            mp.y = 0f;

            for (int si = 0; si < slots.Count; si++)
            {
                if (slotTaken[si]) continue;
                float d = (slots[si] - mp).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSlot = si;
                }
            }

            if (bestSlot < 0) bestSlot = 0;
            slotTaken[bestSlot] = true;

            Vector3 dest = slots[bestSlot];

            NavMeshAgent agent = m.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                agent.isStopped = false;
                agent.SetDestination(dest);
            }
        }
    }
}