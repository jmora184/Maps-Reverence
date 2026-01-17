using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// While JOIN is armed (CommandStateMachine.State.AddTargeting), hovering an ally icon or team star
/// shows a preview of the merged team size + a (placeholder) movement speed debuff.
///
/// This only affects tooltip UI; actual movement speed changes can be wired later.
/// </summary>
[DisallowMultipleComponent]
public class JoinPreviewHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [Header("UI")]
    [Tooltip("If set, only show while this camera is enabled (prevents FPS tooltips).")]
    public Camera onlyWhenCameraEnabled;

    [Tooltip("Optional extra pixel offset added on top of HoverHintUI.pixelOffset.")]
    public Vector2 extraPixelOffset = Vector2.zero;

    [Header("Join Preview")]
    [Tooltip("If true, also show a damage bonus line computed from AllyCombatStats curves.")]
    public bool showDamageBonus = true;

    [Tooltip("If no AllyCombatStats can be found, we fall back to these placeholder percents.")]
    public float fallbackMoveSpeedPenaltyPercent = 5f;

    [Tooltip("If no AllyCombatStats can be found, we fall back to this placeholder damage bonus percent.")]
    public float fallbackDamageBonusPercent = 10f;

    private Transform targetUnit;
    private Team targetTeam;

    private CommandStateMachine sm;
    private RectTransform rt;

    private bool hovering;

    private void Awake()
    {
        rt = transform as RectTransform;
    }

    public void BindToUnit(Transform unit)
    {
        targetUnit = unit;
        targetTeam = null;
    }

    public void BindToTeam(Team team)
    {
        targetTeam = team;
        targetUnit = null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        TryShow();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        HoverHintSystem.Hide(this);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        // Keep pointer tracking current (some setups don't have UIHoverHintTarget).
        HoverHintSystem.UpdatePointer(this, eventData.position);

        // Update message live while hovering (counts can change).
        if (hovering)
            TryShow();
    }

    private void TryShow()
    {
        if (!IsJoinPreviewContext(out var joinSourceGo))
            return;

        int sourceCount = GetGroupCount(joinSourceGo.transform);
        int targetCount = GetTargetCount();

        if (targetCount <= 0)
            return;

        // Don't show if hovering the same object as join source.
        if (IsSameAsJoinSource(joinSourceGo))
            return;

        // If both sides are already the same team, skip.
        if (AreInSameTeam(joinSourceGo.transform, GetTargetRepresentativeTransform()))
            return;

        int total = sourceCount + targetCount;

        GetJoinPreviewPercents(joinSourceGo, total, out int movePenaltyPct, out int damageBonusPct);

        string msg = $"Team total: {total}\nMovement speed -{movePenaltyPct}%";
        if (showDamageBonus)
            msg += $"\nDamage +{damageBonusPct}%";

        if (rt != null)
            HoverHintSystem.Show(this, rt, msg, extraPixelOffset);
    }

    private bool IsJoinPreviewContext(out GameObject joinSource)
    {
        joinSource = null;

        if (onlyWhenCameraEnabled != null && !onlyWhenCameraEnabled.enabled)
            return false;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null)
            return false;

        // Must be in join targeting.
        if (sm.CurrentState != CommandStateMachine.State.AddTargeting)
            return false;

        // Join must be armed and have a source.
        if (!sm.JoinArmed) // uses your existing CommandStateMachine flag
            return false;

        joinSource = sm.JoinSource;
        if (joinSource == null)
            return false;

        return true;
    }



    private void GetJoinPreviewPercents(GameObject joinSource, int combinedTeamSize, out int movePenaltyPct, out int damageBonusPct)
    {
        movePenaltyPct = Mathf.RoundToInt(fallbackMoveSpeedPenaltyPercent);
        damageBonusPct = Mathf.RoundToInt(fallbackDamageBonusPercent);

        if (combinedTeamSize <= 1)
        {
            movePenaltyPct = 0;
            damageBonusPct = 0;
            return;
        }

        // Find a stats template to evaluate curves from (same for every member).
        AllyCombatStats template = FindCombatStatsTemplate(joinSource != null ? joinSource.transform : null);
        if (template == null)
            template = FindCombatStatsTemplate(GetTargetRepresentativeTransform());

        if (template == null)
            return;

        // Evaluate curves for the hypothetical combined team size.
        float moveMult = EvaluateCurveSafe(template.moveSpeedMultiplierByTeamSize, combinedTeamSize, 1f);
        moveMult = Mathf.Clamp(moveMult, template.moveSpeedMultiplierClamp.x, template.moveSpeedMultiplierClamp.y);
        movePenaltyPct = Mathf.RoundToInt(Mathf.Clamp01(1f - moveMult) * 100f);

        float dmgMult = EvaluateCurveSafe(template.damageMultiplierByTeamSize, combinedTeamSize, 1f);
        dmgMult = Mathf.Clamp(dmgMult, template.damageMultiplierClamp.x, template.damageMultiplierClamp.y);
        damageBonusPct = Mathf.RoundToInt(Mathf.Max(0f, dmgMult - 1f) * 100f);
    }

    private AllyCombatStats FindCombatStatsTemplate(Transform worldTransform)
    {
        if (worldTransform == null) return null;

        // Direct on this transform
        AllyCombatStats s = worldTransform.GetComponent<AllyCombatStats>();
        if (s != null) return s;

        // If this transform is part of a team, try first member
        if (TeamManager.Instance != null)
        {
            Team t = TeamManager.Instance.GetTeamOf(worldTransform);
            if (t != null && t.Members != null)
            {
                for (int i = 0; i < t.Members.Count; i++)
                {
                    Transform m = t.Members[i];
                    if (m == null) continue;
                    s = m.GetComponent<AllyCombatStats>();
                    if (s != null) return s;
                }
            }
        }

        return null;
    }

    private static float EvaluateCurveSafe(AnimationCurve curve, float x, float fallback)
    {
        if (curve == null || curve.length == 0)
            return fallback;

        float v = curve.Evaluate(x);
        if (float.IsNaN(v) || float.IsInfinity(v))
            return fallback;

        return v;
    }

    private int GetTargetCount()
    {
        if (targetTeam != null)
            return (targetTeam.Members != null) ? targetTeam.Members.Count : 0;

        if (targetUnit != null)
            return GetGroupCount(targetUnit);

        return 0;
    }

    private Transform GetTargetRepresentativeTransform()
    {
        if (targetTeam != null)
        {
            if (targetTeam.Anchor != null) return targetTeam.Anchor;
            if (targetTeam.Members != null && targetTeam.Members.Count > 0) return targetTeam.Members[0];
            return null;
        }

        return targetUnit;
    }

    private bool IsSameAsJoinSource(GameObject joinSource)
    {
        if (joinSource == null) return false;

        // If we are bound to that exact unit.
        if (targetUnit != null && joinSource.transform == targetUnit)
            return true;

        // If we are a team star, compare against team anchor (or first member).
        if (targetTeam != null)
        {
            var rep = GetTargetRepresentativeTransform();
            if (rep != null && joinSource.transform == rep)
                return true;
        }

        return false;
    }

    private int GetGroupCount(Transform unit)
    {
        if (unit == null) return 0;

        if (TeamManager.Instance != null)
        {
            var team = TeamManager.Instance.GetTeamOf(unit);
            if (team != null && team.Members != null)
                return team.Members.Count;
        }

        return 1;
    }

    private bool AreInSameTeam(Transform a, Transform b)
    {
        if (a == null || b == null) return false;
        if (TeamManager.Instance == null) return false;

        var ta = TeamManager.Instance.GetTeamOf(a);
        var tb = TeamManager.Instance.GetTeamOf(b);

        return (ta != null && tb != null && ta == tb);
    }
}
