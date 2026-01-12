using System.Collections.Generic;
using UnityEngine;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    private readonly List<Team> teams = new();
    public IReadOnlyList<Team> Teams => teams;

    private int nextId = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
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
            return created;
        }

        // One team exists -> add other
        if (teamA != null && teamB == null)
        {
            teamA.Add(joinTarget);
            teamA.Anchor = joinTarget; // ✅
            return teamA;
        }

        if (teamA == null && teamB != null)
        {
            teamB.Add(leader);
            teamB.Anchor = joinTarget; // ✅
            return teamB;
        }

        // Both exist but different -> merge B into A
        if (teamA != null && teamB != null && teamA != teamB)
        {
            for (int i = 0; i < teamB.Members.Count; i++)
                teamA.Add(teamB.Members[i]);

            teams.Remove(teamB);

            teamA.Anchor = joinTarget; // ✅
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
