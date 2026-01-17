using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Displays a green "Flank bonus" hover hint when:
/// - You are in Command Mode move-targeting (Move has been clicked)
/// - An ALLY TEAM is selected (2+ units)
/// - You hover an enemy (or enemy team) that has fewer units than the selected ally team
///
/// This script is intentionally light on compile-time dependencies: it uses reflection to
/// read selection/state from your existing CommandStateMachine + TeamManager if present.
///
/// Setup:
/// 1) Add this component to your ENEMY icon prefab (the UI RectTransform with the Button/Image).
/// 2) When you spawn/bind the enemy icon, call Bind(enemyTransform).
///
/// Example (where you already bind health UI):
///     icon.GetComponent<EnemyFlankBonusHoverHint>()?.Bind(enemy);
/// </summary>
[DisallowMultipleComponent]
public class EnemyFlankBonusHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [Header("Data")]
    [Tooltip("The world Transform this icon represents. Set via Bind().")]
    public Transform enemyTarget;

    [Tooltip("Optional: reads enemy health from an EnemyHealthController on the target.")]
    public bool showEnemyHealthLine = true;

    [Header("Hint")]
    [Tooltip("Only show flank bonus while move-targeting is active.")]
    public bool requireMoveTargeting = true;

    [Tooltip("Minimum size to consider the ally selection a team.")]
    public int minAllyTeamSize = 2;

    [Tooltip("Message shown when flank bonus applies. Rich text supported.")]
    public string flankBonusMessage = "<color=#34c759>Flank bonus</color>";

    [Tooltip("Label for the enemy health line. {0} will be replaced with an integer percent.")]
    public string enemyHealthFormat = "<color=#d1d1d6>Enemy HP: {0}%</color>";

    [Tooltip("How often (seconds) to refresh the tooltip while hovering (helps if health changes without moving the mouse).")]
    public float hoverRefreshInterval = 0.25f;

    private float _nextRefreshTime;

    // If present in your project, we will use it to get health.
    private EnemyHealthController _health;

    private RectTransform _anchor;
    private bool _hovering;

    private void Awake()
    {
        _anchor = GetComponent<RectTransform>();
        if (_anchor == null)
            _anchor = GetComponentInChildren<RectTransform>();
    }

    public void Bind(Transform enemy)
    {
        enemyTarget = enemy;

        // Hook health if available.
        UnhookHealth();
        if (enemyTarget != null)
        {
            _health = enemyTarget.GetComponentInParent<EnemyHealthController>();
            if (_health == null) _health = enemyTarget.GetComponentInChildren<EnemyHealthController>();
            HookHealth();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovering = true;
        _nextRefreshTime = 0f;
        UpdateAndMaybeShow(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!_hovering) return;
        UpdateAndMaybeShow(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        HoverHintSystem.Hide(this);
    }

    private void OnDisable()
    {
        _hovering = false;
        HoverHintSystem.Hide(this);
    }

    private void OnDestroy()
    {
        UnhookHealth();
    }

    private void UpdateAndMaybeShow(PointerEventData e)
    {
        if (_anchor == null) return;

        // Keep the hover UI following the cursor.
        HoverHintSystem.UpdatePointer(this, e.position);

        if (ShouldShowFlankBonus())
            HoverHintSystem.Show(this, _anchor, BuildMessage());
        else
            HoverHintSystem.Hide(this);

        // Periodic refresh while hovering (covers cases where health updates without pointer movement).
        if (_hovering && hoverRefreshInterval > 0f && Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + hoverRefreshInterval;
            if (ShouldShowFlankBonus())
                HoverHintSystem.Show(this, _anchor, BuildMessage());
        }
    }

    private string BuildMessage()
    {
        if (!showEnemyHealthLine)
            return flankBonusMessage;

        int hpPct = GetEnemyHealthPercent();
        if (hpPct < 0)
            return flankBonusMessage;

        return flankBonusMessage + "\n" + string.Format(enemyHealthFormat, hpPct);
    }

    private int GetEnemyHealthPercent()
    {
        if (_health == null) return -1;

        try
        {
            float t = Mathf.Clamp01(_health.Health01());
            return Mathf.RoundToInt(t * 100f);
        }
        catch
        {
            return -1;
        }
    }

    private void HookHealth()
    {
        if (_health == null) return;
        try
        {
            _health.OnHealth01Changed -= OnEnemyHealth01Changed;
            _health.OnHealth01Changed += OnEnemyHealth01Changed;
        }
        catch { /* ignore */ }
    }

    private void UnhookHealth()
    {
        if (_health == null) return;
        try
        {
            _health.OnHealth01Changed -= OnEnemyHealth01Changed;
        }
        catch { /* ignore */ }
        _health = null;
    }

    private void OnEnemyHealth01Changed(float _)
    {
        if (!_hovering) return;
        if (_anchor == null) return;
        if (!ShouldShowFlankBonus()) { HoverHintSystem.Hide(this); return; }

        HoverHintSystem.Show(this, _anchor, BuildMessage());
    }

    private bool ShouldShowFlankBonus()
    {
        if (enemyTarget == null) return false;

        if (requireMoveTargeting && !IsMoveTargetingActive())
            return false;

        int allyCount = GetSelectedAllyTeamCount();
        if (allyCount < minAllyTeamSize)
            return false;

        int enemyCount = GetTeamCountForUnit(enemyTarget);

        // "Flank bonus" rule for now: enemy has fewer units than your selected ally team.
        return enemyCount > 0 && enemyCount < allyCount;
    }

    // ---------------------------
    // Selection + state (reflection)
    // ---------------------------

    private bool IsMoveTargetingActive()
    {
        // Heuristic: find a CommandStateMachine in scene and check common properties/fields.
        var sm = FindFirstComponentByTypeName("CommandStateMachine");
        if (sm == null) return false;

        // 1) Common boolean properties
        if (TryGetBool(sm, "IsMoveTargeting", out bool b1) && b1) return true;
        if (TryGetBool(sm, "InMoveTargeting", out bool b2) && b2) return true;
        if (TryGetBool(sm, "MoveTargeting", out bool b3) && b3) return true;

        // 2) Common enum-ish properties (CurrentState/State)
        if (TryGetStringified(sm, "CurrentState", out string s1) && LooksLikeMoveTargeting(s1)) return true;
        if (TryGetStringified(sm, "State", out string s2) && LooksLikeMoveTargeting(s2)) return true;

        // 3) Try method name patterns
        if (TryCallBoolMethod(sm, "IsInMoveTargeting", out bool m1) && m1) return true;
        if (TryCallBoolMethod(sm, "IsMoveTargeting", out bool m2) && m2) return true;

        return false;
    }

    private static bool LooksLikeMoveTargeting(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        // Match "MoveTargeting" / "MoveTarget" / "Move" targeting states.
        s = s.Trim();
        if (s.Equals("MoveTargeting", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0 &&
            s.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private int GetSelectedAllyTeamCount()
    {
        // Try to get an explicit selected team first.
        var sm = FindFirstComponentByTypeName("CommandStateMachine");
        if (sm == null)
            return 0;

        // A) SelectedTeam property/field
        object selectedTeamObj = GetMemberValue(sm, "SelectedTeam") ?? GetMemberValue(sm, "CurrentTeam");
        if (selectedTeamObj != null)
        {
            int teamCount = GetTeamMemberCountFromTeamObject(selectedTeamObj);
            if (teamCount > 0) return teamCount;
        }

        // B) Selection list (Transforms/GameObjects)
        object selectionObj = GetMemberValue(sm, "Selection") ??
                              GetMemberValue(sm, "CurrentSelection") ??
                              GetMemberValue(sm, "selection");

        var selectedUnits = ExtractTransforms(selectionObj);
        if (selectedUnits != null && selectedUnits.Count > 0)
        {
            // Prefer TeamManager's team count if available.
            int teamCount = GetTeamCountForUnit(selectedUnits[0]);
            if (teamCount > 0) return teamCount;

            // Fallback: treat selection count as team size.
            return selectedUnits.Count;
        }

        // C) SelectionCount property (int)
        if (TryGetInt(sm, "SelectionCount", out int selCount))
            return selCount;

        return 0;
    }

    private int GetTeamCountForUnit(Transform unit)
    {
        if (unit == null) return 0;

        // Use TeamManager.GetTeamOf(unit) if it exists.
        var teamManager = FindFirstComponentByTypeName("TeamManager");
        if (teamManager != null)
        {
            try
            {
                var tmType = teamManager.GetType();
                var method = tmType.GetMethod("GetTeamOf", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    var teamObj = method.Invoke(teamManager, new object[] { unit });
                    int count = GetTeamMemberCountFromTeamObject(teamObj);
                    if (count > 0) return count;
                }
            }
            catch { /* ignore */ }
        }

        // If unit has a Team component/reference directly.
        var teamComponent = unit.GetComponent("Team");
        if (teamComponent != null)
        {
            int count = GetTeamMemberCountFromTeamObject(teamComponent);
            if (count > 0) return count;
        }

        // Default: single unit.
        return 1;
    }

    private static int GetTeamMemberCountFromTeamObject(object teamObj)
    {
        if (teamObj == null) return 0;

        // Common pattern: Team.Members is List<Transform> or similar.
        object members = null;
        var t = teamObj.GetType();

        var prop = t.GetProperty("Members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null) members = prop.GetValue(teamObj);
        if (members == null)
        {
            var field = t.GetField("Members", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) members = field.GetValue(teamObj);
        }

        if (members == null) return 0;

        // ICollection / IReadOnlyCollection
        if (members is System.Collections.ICollection c) return c.Count;

        // Try Count property
        var countProp = members.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProp != null && countProp.PropertyType == typeof(int))
        {
            try { return (int)countProp.GetValue(members); } catch { }
        }

        return 0;
    }

    // ---------------------------
    // Reflection helpers
    // ---------------------------

    private static Component FindFirstComponentByTypeName(string typeName)
    {
        var type = FindTypeByName(typeName);
        if (type == null) return null;
        return UnityEngine.Object.FindObjectOfType(type) as Component;
    }

    private static Type FindTypeByName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        // Fast path
        var t = Type.GetType(typeName);
        if (t != null) return t;

        // Search all loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                t = assemblies[i].GetTypes().FirstOrDefault(x => x.Name == typeName);
                if (t != null) return t;
            }
            catch { /* some assemblies may throw */ }
        }

        return null;
    }

    private static bool TryGetBool(object obj, string memberName, out bool value)
    {
        value = false;
        object v = GetMemberValue(obj, memberName);
        if (v is bool b) { value = b; return true; }
        return false;
    }

    private static bool TryGetInt(object obj, string memberName, out int value)
    {
        value = 0;
        object v = GetMemberValue(obj, memberName);
        if (v is int i) { value = i; return true; }
        return false;
    }

    private static bool TryGetStringified(object obj, string memberName, out string s)
    {
        s = null;
        object v = GetMemberValue(obj, memberName);
        if (v == null) return false;
        s = v.ToString();
        return !string.IsNullOrEmpty(s);
    }

    private static bool TryCallBoolMethod(object obj, string methodName, out bool value)
    {
        value = false;
        if (obj == null) return false;

        try
        {
            var t = obj.GetType();
            var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return false;
            if (m.ReturnType != typeof(bool)) return false;
            if (m.GetParameters().Length != 0) return false;

            value = (bool)m.Invoke(obj, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object GetMemberValue(object obj, string memberName)
    {
        if (obj == null || string.IsNullOrEmpty(memberName)) return null;

        var t = obj.GetType();

        var prop = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null)
        {
            try { return prop.GetValue(obj); } catch { }
        }

        var field = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            try { return field.GetValue(obj); } catch { }
        }

        return null;
    }

    private static List<Transform> ExtractTransforms(object selectionObj)
    {
        if (selectionObj == null) return null;

        if (selectionObj is IEnumerable enumerable)
        {
            var list = new List<Transform>();
            foreach (var item in enumerable)
            {
                if (item is Transform tr) list.Add(tr);
                else if (item is GameObject go) list.Add(go.transform);
                else if (item is Component c) list.Add(c.transform);
            }

            return list.Count > 0 ? list : null;
        }

        return null;
    }
}
