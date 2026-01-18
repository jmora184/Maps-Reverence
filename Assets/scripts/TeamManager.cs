using System.Collections.Generic;
using UnityEngine;

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

    private void ShowTeamHint(Team team, string prefix)
    {
        if (!showTeamHints) return;
        if (team == null) return;

        // Count only non-null members (defensive)
        int count = 0;
        if (team.Members != null)
        {
            for (int i = 0; i < team.Members.Count; i++)
                if (team.Members[i] != null) count++;
        }

        // Uses HintSystem (auto-bootstraps) to show a toast if present in scene.
        // Example: "Team formed! Team 2 has 3 allies"
        HintSystem.Show($"{prefix} Team {team.Id} has {count} {(count == 1 ? "ally" : "allies")}.", teamHintDuration);
    }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
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
            teamA.Anchor = joinTarget; // ✅ keep star on the second ally
            return teamA;
        }

        // Neither in a team -> create new
        if (teamA == null && teamB == null)
        {
            var created = CreateTeam(leader, joinTarget);
            created.Anchor = joinTarget; // ✅
            ShowTeamHint(created, "Team formed!");
            ClearTeamPin(created);
            return created;
        }

        // One team exists -> add other
        if (teamA != null && teamB == null)
        {
            teamA.Add(joinTarget);
            teamA.Anchor = joinTarget; // ✅
            ShowTeamHint(teamA, "Team updated!");
            ClearTeamPin(teamA);
            return teamA;
        }

        if (teamA == null && teamB != null)
        {
            teamB.Add(leader);
            teamB.Anchor = joinTarget; // ✅
            ShowTeamHint(teamB, "Team updated!");
            ClearTeamPin(teamB);
            return teamB;
        }

        // Both exist but different -> merge B into A
        if (teamA != null && teamB != null && teamA != teamB)
        {
            for (int i = 0; i < teamB.Members.Count; i++)
                teamA.Add(teamB.Members[i]);

            teams.Remove(teamB);

            teamA.Anchor = joinTarget; // ✅
            ShowTeamHint(teamA, "Teams merged!");
            ClearTeamPin(teamA);
            ClearTeamPin(teamB);
            return teamA;
        }

        return null;
    }

    public Team CreateTeam(Transform a, Transform b)
    {
        if (a == null || b == null) return null;
        if (a == b) return null;

        Team t = new Team(nextId++, a, b);
        teams.Add(t);
        return t;
    }
}
