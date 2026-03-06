using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks committed TEAM-vs-TEAM attack orders for flank UI.
///
/// Rules:
/// - Counts UNIQUE attacking teams, not individual units.
/// - A flank only exists when 2+ unique hostile teams are committed to the same defender team.
/// - Source of truth is ATTACK ORDERS, not shots fired.
/// </summary>
public class TeamFlankIndicatorSystem : MonoBehaviour
{
    public static TeamFlankIndicatorSystem Instance { get; private set; }

    [Header("Enemy Team Discovery")]
    [Tooltip("Enemy team roots are typically named EnemyTeam_1, EnemyTeam_2, ...")]
    public string enemyTeamRootPrefix = "EnemyTeam_";

    [Header("Cleanup")]
    [Tooltip("How often to prune dead / missing teams from the registry.")]
    public float cleanupInterval = 0.5f;

    // Attacker Team Id -> Defender key
    private readonly Dictionary<int, Transform> _defenderByAttackerTeamId = new Dictionary<int, Transform>();

    // Defender key -> unique attacker Team Ids
    private readonly Dictionary<Transform, HashSet<int>> _attackerTeamIdsByDefender = new Dictionary<Transform, HashSet<int>>();

    // Team Id -> Team reference (for alive checks)
    private readonly Dictionary<int, Team> _teamById = new Dictionary<int, Team>();

    private float _nextCleanupTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= _nextCleanupTime)
        {
            _nextCleanupTime = Time.unscaledTime + Mathf.Max(0.1f, cleanupInterval);
            PruneInvalidEntries();
        }
    }

    public void RegisterCommittedAttackOrder(IReadOnlyList<GameObject> attackers, Transform defenderTarget)
    {
        if (attackers == null || attackers.Count == 0 || defenderTarget == null) return;
        if (TeamManager.Instance == null) return;

        Transform defenderKey = ResolveDefenderKey(defenderTarget);
        if (defenderKey == null) return;

        HashSet<int> distinctAttackerTeamIds = new HashSet<int>();

        for (int i = 0; i < attackers.Count; i++)
        {
            GameObject go = attackers[i];
            if (go == null) continue;

            Transform unit = go.transform;
            if (unit == null) continue;

            Team team = TeamManager.Instance.GetTeamOf(unit);
            if (team == null) continue;
            if (!TeamHasAliveMembers(team)) continue;

            int teamId = team.Id;
            if (!distinctAttackerTeamIds.Add(teamId))
                continue;

            _teamById[teamId] = team;

            if (_defenderByAttackerTeamId.TryGetValue(teamId, out Transform oldDefender) && oldDefender != null && oldDefender != defenderKey)
            {
                if (_attackerTeamIdsByDefender.TryGetValue(oldDefender, out HashSet<int> oldSet) && oldSet != null)
                {
                    oldSet.Remove(teamId);
                    if (oldSet.Count == 0)
                        _attackerTeamIdsByDefender.Remove(oldDefender);
                }
            }

            _defenderByAttackerTeamId[teamId] = defenderKey;

            if (!_attackerTeamIdsByDefender.TryGetValue(defenderKey, out HashSet<int> set) || set == null)
            {
                set = new HashSet<int>();
                _attackerTeamIdsByDefender[defenderKey] = set;
            }

            set.Add(teamId);
        }
    }

    public void UnregisterAttackingTeams(IReadOnlyList<GameObject> attackers)
    {
        if (attackers == null || attackers.Count == 0) return;
        if (TeamManager.Instance == null) return;

        HashSet<int> distinctAttackerTeamIds = new HashSet<int>();

        for (int i = 0; i < attackers.Count; i++)
        {
            GameObject go = attackers[i];
            if (go == null) continue;

            Team team = TeamManager.Instance.GetTeamOf(go.transform);
            if (team == null) continue;

            int teamId = team.Id;
            if (!distinctAttackerTeamIds.Add(teamId))
                continue;

            UnregisterAttackingTeam(teamId);
        }
    }

    public void UnregisterAttackingTeam(int attackerTeamId)
    {
        if (!_defenderByAttackerTeamId.TryGetValue(attackerTeamId, out Transform defenderKey) || defenderKey == null)
            return;

        _defenderByAttackerTeamId.Remove(attackerTeamId);

        if (_attackerTeamIdsByDefender.TryGetValue(defenderKey, out HashSet<int> set) && set != null)
        {
            set.Remove(attackerTeamId);
            if (set.Count == 0)
                _attackerTeamIdsByDefender.Remove(defenderKey);
        }
    }

    public int GetCommittedAttackerTeamCount(Transform defenderTarget)
    {
        if (defenderTarget == null) return 0;

        Transform defenderKey = ResolveDefenderKey(defenderTarget);
        if (defenderKey == null) return 0;

        if (!_attackerTeamIdsByDefender.TryGetValue(defenderKey, out HashSet<int> set) || set == null)
            return 0;

        int aliveCount = 0;
        foreach (int teamId in set)
        {
            if (_teamById.TryGetValue(teamId, out Team team) && team != null && TeamHasAliveMembers(team))
                aliveCount++;
        }

        return aliveCount;
    }

    public Transform ResolveDefenderKey(Transform defenderTarget)
    {
        if (defenderTarget == null) return null;

        Transform t = defenderTarget;
        while (t != null)
        {
            if (!string.IsNullOrEmpty(t.name) && t.name.StartsWith(enemyTeamRootPrefix))
                return t;
            t = t.parent;
        }

        return defenderTarget;
    }

    private void PruneInvalidEntries()
    {
        List<int> deadAttackers = null;

        foreach (KeyValuePair<int, Transform> kv in _defenderByAttackerTeamId)
        {
            bool remove = false;

            if (kv.Value == null)
            {
                remove = true;
            }
            else if (!_teamById.TryGetValue(kv.Key, out Team team) || team == null || !TeamHasAliveMembers(team))
            {
                remove = true;
            }

            if (remove)
            {
                if (deadAttackers == null) deadAttackers = new List<int>();
                deadAttackers.Add(kv.Key);
            }
        }

        if (deadAttackers != null)
        {
            for (int i = 0; i < deadAttackers.Count; i++)
                UnregisterAttackingTeam(deadAttackers[i]);
        }

        List<Transform> deadDefenders = null;
        foreach (KeyValuePair<Transform, HashSet<int>> kv in _attackerTeamIdsByDefender)
        {
            if (kv.Key == null)
            {
                if (deadDefenders == null) deadDefenders = new List<Transform>();
                deadDefenders.Add(kv.Key);
            }
        }

        if (deadDefenders != null)
        {
            for (int i = 0; i < deadDefenders.Count; i++)
                _attackerTeamIdsByDefender.Remove(deadDefenders[i]);
        }
    }

    private bool TeamHasAliveMembers(Team team)
    {
        if (team == null || team.Members == null || team.Members.Count == 0)
            return false;

        for (int i = 0; i < team.Members.Count; i++)
        {
            if (team.Members[i] != null)
                return true;
        }

        return false;
    }
}
