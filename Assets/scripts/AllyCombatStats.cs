using UnityEngine;

/// <summary>
/// Per-ally combat + movement tuning.
/// 
/// This does NOT store stats on the Team. Instead, each ally computes its own multipliers
/// from its current team size, so the effect is applied individually to every member.
/// 
/// Usage (now / later):
/// - Add this component to each Ally unit prefab.
/// - Use GetDamageInt() when spawning bullets (set BulletController.Damage).
/// - Use GetMoveSpeed() when setting NavMeshAgent.speed.
/// 
/// Team size is pulled from TeamManager at runtime (no events required).
/// </summary>
[DisallowMultipleComponent]
public class AllyCombatStats : MonoBehaviour
{
    [Header("Base Stats (per ally)")]
    [Tooltip("Base bullet damage for this ally when solo (team size = 1).")]
    public int baseDamage = 2;

    [Tooltip("Base movement speed for this ally when solo (team size = 1). " +
             "If left at 0, we will try to read NavMeshAgent.speed in Awake().")]
    public float baseMoveSpeed = 0f;

    [Header("Scaling by Team Size (same for every member)")]
    [Tooltip("Damage multiplier as a function of team size. " +
             "X = team size (1..N), Y = multiplier (1.0 = no change).")]
    public AnimationCurve damageMultiplierByTeamSize = new AnimationCurve(
        new Keyframe(1, 1.00f),
        new Keyframe(2, 1.05f),
        new Keyframe(3, 1.10f),
        new Keyframe(4, 1.15f)
    );

    [Tooltip("Move speed multiplier as a function of team size. " +
             "X = team size (1..N), Y = multiplier (1.0 = no change).")]
    public AnimationCurve moveSpeedMultiplierByTeamSize = new AnimationCurve(
        new Keyframe(1, 1.00f),
        new Keyframe(2, 0.95f),
        new Keyframe(3, 0.90f),
        new Keyframe(4, 0.85f)
    );

    [Header("Limits (safety)")]
    [Tooltip("Clamp the evaluated damage multiplier into this range.")]
    public Vector2 damageMultiplierClamp = new Vector2(0.10f, 5.00f);

    [Tooltip("Clamp the evaluated speed multiplier into this range.")]
    public Vector2 moveSpeedMultiplierClamp = new Vector2(0.10f, 3.00f);

    // Cached lookups (purely for performance / debug)
    private int _cachedTeamSize = 1;
    private int _cachedTeamId = -1;
    private bool _hasTeamManager;

    private void Awake()
    {
        // Optional: auto-detect baseMoveSpeed from NavMeshAgent if not provided.
        if (baseMoveSpeed <= 0f)
        {
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
                baseMoveSpeed = agent.speed;
        }

        _hasTeamManager = (TeamManager.Instance != null);
        RefreshCache();
    }

    /// <summary>Returns the ally's current team size (solo = 1).</summary>
    public int GetTeamSize()
    {
        if (TeamManager.Instance == null) return 1;

        Team t = TeamManager.Instance.GetTeamOf(transform);
        if (t == null || t.Members == null) return 1;

        // Count non-null members defensively
        int count = 0;
        for (int i = 0; i < t.Members.Count; i++)
            if (t.Members[i] != null) count++;

        return Mathf.Max(1, count);
    }

    /// <summary>Current damage multiplier based on team size.</summary>
    public float GetDamageMultiplier()
    {
        int size = GetTeamSize();
        float m = EvaluateCurveSafe(damageMultiplierByTeamSize, size, 1f);
        return Mathf.Clamp(m, damageMultiplierClamp.x, damageMultiplierClamp.y);
    }

    /// <summary>Current movement speed multiplier based on team size.</summary>
    public float GetMoveSpeedMultiplier()
    {
        int size = GetTeamSize();
        float m = EvaluateCurveSafe(moveSpeedMultiplierByTeamSize, size, 1f);
        return Mathf.Clamp(m, moveSpeedMultiplierClamp.x, moveSpeedMultiplierClamp.y);
    }

    /// <summary>Convenience: final bullet damage for this ally at this moment.</summary>
    public int GetDamageInt()
    {
        float dmg = baseDamage * GetDamageMultiplier();
        return Mathf.Max(1, Mathf.RoundToInt(dmg));
    }

    /// <summary>Convenience: final move speed for this ally at this moment.</summary>
    public float GetMoveSpeed()
    {
        return Mathf.Max(0.01f, baseMoveSpeed * GetMoveSpeedMultiplier());
    }

    /// <summary>
    /// Optional helper: apply the current speed to a NavMeshAgent.
    /// Call this after team size changes, or periodically if you want.
    /// </summary>
    public void ApplyToAgent(UnityEngine.AI.NavMeshAgent agent)
    {
        if (agent == null) return;
        agent.speed = GetMoveSpeed();
    }

    /// <summary>
    /// Useful for UI/tooltips: returns how much speed is reduced as a percentage (0..100).
    /// Example: multiplier 0.95 => returns 5.
    /// </summary>
    public int GetMoveSpeedPenaltyPercentRounded()
    {
        float m = GetMoveSpeedMultiplier();
        float penalty = Mathf.Clamp01(1f - m);
        return Mathf.RoundToInt(penalty * 100f);
    }

    /// <summary>
    /// Useful for UI/tooltips: returns how much damage is increased as a percentage (0..500+).
    /// Example: multiplier 1.10 => returns 10.
    /// </summary>
    public int GetDamageBonusPercentRounded()
    {
        float m = GetDamageMultiplier();
        float bonus = Mathf.Max(0f, m - 1f);
        return Mathf.RoundToInt(bonus * 100f);
    }

    // ----------------- internals -----------------

    private void RefreshCache()
    {
        if (TeamManager.Instance == null)
        {
            _cachedTeamSize = 1;
            _cachedTeamId = -1;
            return;
        }

        Team t = TeamManager.Instance.GetTeamOf(transform);
        _cachedTeamId = t != null ? t.Id : -1;
        _cachedTeamSize = GetTeamSize();
    }

    private static float EvaluateCurveSafe(AnimationCurve curve, float x, float fallback)
    {
        if (curve == null || curve.length == 0)
            return fallback;

        // Guard against NaN/Inf
        float v = curve.Evaluate(x);
        if (float.IsNaN(v) || float.IsInfinity(v))
            return fallback;

        return v;
    }
}
