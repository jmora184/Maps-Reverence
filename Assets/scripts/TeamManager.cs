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

    [Tooltip("Extra radius added per ring when there are more members than one ring can hold.")]
    public float ringRadiusStep = 1.2f;

    [Tooltip("How many slots per ring around the leader. 6 is a nice default.")]
    public int slotsPerRing = 6;

    [Tooltip("NavMesh sample radius for formation slot positions.")]
    public float navmeshSampleRadius = 2.5f;

    [Tooltip("If true, we immediately issue SetDestination() orders to spread members when teams form/update/merge.")]
    public bool applyFormationOnTeamChange = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
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
            ApplyFormationIfEnabled(created);
            return created;
        }

        // One team exists -> add other
        if (teamA != null && teamB == null)
        {
            teamA.Add(joinTarget);
            teamA.Anchor = joinTarget; // star on second ally
            teamA.FormationRadius = Mathf.Max(0.1f, teamA.FormationRadius > 0f ? teamA.FormationRadius : defaultFormationRadius);

            ShowTeamHint(teamA, "Team updated!");
            ClearTeamPin(teamA);
            ApplyFormationIfEnabled(teamA);
            return teamA;
        }

        if (teamA == null && teamB != null)
        {
            teamB.Add(leader);
            teamB.Anchor = joinTarget; // star on second ally
            teamB.FormationRadius = Mathf.Max(0.1f, teamB.FormationRadius > 0f ? teamB.FormationRadius : defaultFormationRadius);

            ShowTeamHint(teamB, "Team updated!");
            ClearTeamPin(teamB);
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

            ShowTeamHint(teamA, "Teams merged!");
            ClearTeamPin(teamA);
            ClearTeamPin(teamB);
            ApplyFormationIfEnabled(teamA);
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
        return t;
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

        int slotIndex = 0;

        for (int i = 0; i < team.Members.Count; i++)
        {
            Transform m = team.Members[i];
            if (m == null) continue;

            // Anchor stays where it is
            if (m == team.Anchor) continue;

            // Ring + angle
            int ring = slotIndex / Mathf.Max(1, slotsPerRing);
            int idxInRing = slotIndex % Mathf.Max(1, slotsPerRing);

            float angleStep = 360f / Mathf.Max(1, slotsPerRing);
            float angle = idxInRing * angleStep * Mathf.Deg2Rad;

            float r = baseRadius + ring * Mathf.Max(0.1f, ringRadiusStep);

            Vector3 dir = (right * Mathf.Cos(angle)) + (forward * Mathf.Sin(angle));
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = right;

            Vector3 desired = center + dir.normalized * r;

            // Snap to navmesh
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.5f, navmeshSampleRadius), NavMesh.AllAreas))
                desired = hit.position;

            // Issue destination
            NavMeshAgent agent = m.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                agent.isStopped = false;
                agent.SetDestination(desired);
            }

            slotIndex++;
        }
    }
}
