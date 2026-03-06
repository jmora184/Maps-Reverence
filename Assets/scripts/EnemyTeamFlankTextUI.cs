using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// Persistent visible flank text for enemy team icons.
/// 
/// Safe design:
/// - READ ONLY. Does not modify movement, click handling, or attack registration.
/// - Auto-binds to the enemy team root from EnemyTeamIconTargetingBridge if present.
/// - Auto-finds a TMP child named "FlankText" unless assigned manually.
/// - Reads committed attackers from AttackTargetIndicatorSystem via reflection.
/// - Converts committed attackers into DISTINCT ally-team count using TeamManager.
/// 
/// Formula:
/// flank bonus = max(0, distinctCommittedTeams - 1)
/// 
/// Examples:
/// - 1 ally team attacking this enemy team = ""
/// - 2 ally teams attacking this enemy team = "+1 Flank"
/// - 3 ally teams attacking this enemy team = "+2 Flank"
/// </summary>
[DisallowMultipleComponent]
public class EnemyTeamFlankTextUI : MonoBehaviour
{
    [Header("Binding")]
    [Tooltip("Enemy team root/anchor represented by this icon. Auto-filled from EnemyTeamIconTargetingBridge if present.")]
    public Transform enemyTarget;

    [Header("Text")]
    [Tooltip("Optional explicit TMP reference. If empty, this script auto-finds a child named FlankText.")]
    public TMP_Text flankText;

    [Tooltip("Child object name to auto-find when flankText is not assigned.")]
    public string flankTextChildName = "FlankText";

    [Tooltip("Hide the text object when there is no flank bonus.")]
    public bool hideWhenZero = true;

    [Tooltip("Text format. {0} = flank bonus count.")]
    public string textFormat = "+{0} Flank";

    [Header("Refresh")]
    [Tooltip("How often to refresh the visible text.")]
    public float refreshInterval = 0.15f;

    private float _nextRefreshTime;
    private EnemyTeamIconTargetingBridge _bridge;
    private EnemyFlankBonusHoverHint _hoverHint;

    private FieldInfo _attackersByEnemyField;
    private bool _reflectionCached;

    private void Awake()
    {
        _bridge = GetComponent<EnemyTeamIconTargetingBridge>();
        _hoverHint = GetComponent<EnemyFlankBonusHoverHint>();
        ResolveFlankText();
        ResolveEnemyTarget();
        CacheReflection();
        ApplyTextImmediate();
    }

    private void OnEnable()
    {
        _nextRefreshTime = 0f;
        ResolveFlankText();
        ResolveEnemyTarget();
        ApplyTextImmediate();
    }

    private void Update()
    {
        if (refreshInterval < 0.02f) refreshInterval = 0.02f;
        if (Time.unscaledTime < _nextRefreshTime) return;

        _nextRefreshTime = Time.unscaledTime + refreshInterval;

        ResolveEnemyTarget();
        ApplyTextImmediate();
    }

    public void Bind(Transform target)
    {
        enemyTarget = target;
        ApplyTextImmediate();
    }

    private void ResolveEnemyTarget()
    {
        if (enemyTarget != null) return;

        if (_bridge == null) _bridge = GetComponent<EnemyTeamIconTargetingBridge>();
        if (_bridge != null && _bridge.enemyTeamAnchor != null)
        {
            enemyTarget = _bridge.enemyTeamAnchor;
            return;
        }

        if (_hoverHint == null) _hoverHint = GetComponent<EnemyFlankBonusHoverHint>();
        if (_hoverHint != null && _hoverHint.enemyTarget != null)
        {
            enemyTarget = _hoverHint.enemyTarget;
            return;
        }
    }

    private void ResolveFlankText()
    {
        if (flankText != null) return;

        Transform t = transform.Find(flankTextChildName);
        if (t == null)
        {
            // Common prefab layout fallback.
            t = FindDeepChild(transform, flankTextChildName);
        }

        if (t != null)
            flankText = t.GetComponent<TMP_Text>();
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == childName) return c;
            Transform nested = FindDeepChild(c, childName);
            if (nested != null) return nested;
        }
        return null;
    }

    private void CacheReflection()
    {
        if (_reflectionCached) return;
        _reflectionCached = true;

        Type t = typeof(AttackTargetIndicatorSystem);
        _attackersByEnemyField = t.GetField("attackersByEnemy", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void ApplyTextImmediate()
    {
        ResolveFlankText();
        if (flankText == null) return;

        int flankBonus = GetFlankBonusCount(enemyTarget);
        bool show = flankBonus > 0;

        flankText.text = show ? string.Format(textFormat, flankBonus) : string.Empty;

        if (hideWhenZero)
        {
            if (flankText.gameObject.activeSelf != show)
                flankText.gameObject.SetActive(show);
        }
        else
        {
            if (!flankText.gameObject.activeSelf)
                flankText.gameObject.SetActive(true);
        }
    }

    private int GetFlankBonusCount(Transform enemy)
    {
        int teamCount = GetDistinctCommittedTeamCount(enemy);
        return Mathf.Max(0, teamCount - 1);
    }

    private int GetDistinctCommittedTeamCount(Transform enemy)
    {
        if (enemy == null) return 0;

        var indicator = AttackTargetIndicatorSystem.Instance;
        if (indicator == null) return 0;

        CacheReflection();
        if (_attackersByEnemyField == null) return 0;

        object raw = _attackersByEnemyField.GetValue(indicator);
        if (raw is not Dictionary<Transform, HashSet<Transform>> attackersByEnemy) return 0;
        if (!attackersByEnemy.TryGetValue(enemy, out var attackers) || attackers == null || attackers.Count == 0)
            return 0;

        HashSet<int> distinctKeys = new HashSet<int>();
        TeamManager tm = TeamManager.Instance;

        foreach (var attacker in attackers)
        {
            if (attacker == null) continue;
            if (!IsAllyLike(attacker)) continue;

            int key = 0;
            if (tm != null)
            {
                Team team = tm.GetTeamOf(attacker);
                if (team != null)
                    key = team.Id;
            }

            // Fallback: unteamed ally counts as its own unique contributor.
            if (key == 0)
                key = -Mathf.Abs(attacker.GetInstanceID());

            distinctKeys.Add(key);
        }

        return distinctKeys.Count;
    }

    private static bool IsAllyLike(Transform t)
    {
        if (t == null) return false;

        if (t.CompareTag("Ally")) return true;
        if (t.GetComponent<AllyController>() != null) return true;

        // Optional fallback for helper/variant objects in some prefabs.
        string n = t.name;
        if (!string.IsNullOrEmpty(n) && n.IndexOf("ally", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }
}
