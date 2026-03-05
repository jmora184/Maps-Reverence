using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Enemy team icon spawner/controller for Command Mode UI.
///
/// This version adds per-team manual scale overrides (by team root name),
/// and FIXES the compile error you hit by including RebuildTeamOverrideMap().
///
/// Notes:
/// - Your icons are UI (RectTransform/Image). Many setups keep parent scale at (1,1,1),
///   so we apply the manual scale to the StarImage child by sizeDelta each LateUpdate.
/// - EncounterDirectorPOC can push overrides per team via SetTeamIconScaleOverride().
/// </summary>
[DisallowMultipleComponent]
public class EnemyTeamIconSystem : MonoBehaviour
{
    [Serializable]
    public class TeamScaleOverride
    {
        [Tooltip("Team root name in the scene hierarchy, e.g. EnemyTeam_2")]
        public string teamRootName;

        [Tooltip("Manual scale for this team icon (1 = default size)")]
        public float scale = 1f;
    }

    public static EnemyTeamIconSystem Instance { get; private set; }

    [Header("Prefab + Parent")]
    [Tooltip("Icon prefab (RectTransform) to instantiate per team.")]
    public RectTransform iconPrefab;

    [Tooltip("UI parent for instantiated icons (RectTransform under your Command Mode canvas).")]
    public RectTransform iconParent;

    [Header("Camera / Projection")]
    [Tooltip("World camera used for WorldToScreenPoint (usually your CommandCamera).")]
    public Camera worldCamera;

    [Header("Icon Size (optional)")]
    [Tooltip("If > 0, forces the star image size in pixels before scaling.")]
    public float iconSizePixels = 0f;

    [Tooltip("Local scale applied to the icon root (kept small; StarImage gets the real scaling).")]
    public float iconLocalScale = 1f;

    [Header("Per-Team Manual Overrides")]
    [Tooltip("Optional list of per-team scale overrides (keyed by team root name). EncounterDirectorPOC can also set these at runtime.")]
    public List<TeamScaleOverride> teamScaleOverrides = new List<TeamScaleOverride>();

    // Runtime map for fast lookup
    private readonly Dictionary<string, float> _teamScaleMap = new Dictionary<string, float>(StringComparer.Ordinal);

    // Track spawned icons by team root
    private readonly Dictionary<Transform, RectTransform> _iconsByTeamRoot = new Dictionary<Transform, RectTransform>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        RebuildTeamOverrideMap();
    }

    void OnValidate()
    {
        // Keep map in sync when editing in inspector
        RebuildTeamOverrideMap();
    }

    /// <summary>
    /// Call this from EncounterDirectorPOC when you want per-team manual scale.
    /// teamRootName should match the actual root transform name (ex: EnemyTeam_2).
    /// </summary>
    public void SetTeamIconScaleOverride(string teamRootName, float scale)
    {
        if (string.IsNullOrWhiteSpace(teamRootName)) return;

        scale = Mathf.Max(0.01f, scale);
        _teamScaleMap[teamRootName] = scale;

        // Also mirror into list for inspector visibility (best effort).
        bool found = false;
        for (int i = 0; i < teamScaleOverrides.Count; i++)
        {
            if (string.Equals(teamScaleOverrides[i].teamRootName, teamRootName, StringComparison.Ordinal))
            {
                teamScaleOverrides[i].scale = scale;
                found = true;
                break;
            }
        }
        if (!found)
        {
            teamScaleOverrides.Add(new TeamScaleOverride { teamRootName = teamRootName, scale = scale });
        }
    }

    /// <summary>
    /// Backwards-compatible alias (EncounterDirectorPOC expects this name).
    /// </summary>
    public void SetTeamScaleOverride(string teamRootName, float scale)
    {
        SetTeamIconScaleOverride(teamRootName, scale);
    }

    /// <summary>
    /// Backwards-compatible overload. Treats scale*multiplier as final scale.
    /// Optional iconSizePixelsOverride (>0) will set the base iconSizePixels used by this system.
    /// </summary>
    public void SetTeamScaleOverride(string teamRootName, float scale, float multiplier, float iconSizePixelsOverride)
    {
        if (iconSizePixelsOverride > 0f)
            iconSizePixels = iconSizePixelsOverride;

        SetTeamIconScaleOverride(teamRootName, scale * multiplier);
    }

    /// <summary>
    /// Backwards-compatible overload. Treats scale*multiplier as final scale.
    /// The bool is accepted for API compatibility; this system already applies scaling via sizeDelta.
    /// </summary>
    public void SetTeamScaleOverride(string teamRootName, float scale, float multiplier, bool useSizeDeltaOverride)
    {
        // useSizeDeltaOverride intentionally ignored (this implementation always prefers sizeDelta when possible).
        SetTeamIconScaleOverride(teamRootName, scale * multiplier);
    }


    /// <summary>
    /// IMPORTANT: this method was missing and caused your CS0103 error.
    /// It rebuilds the runtime dictionary from the inspector list.
    /// </summary>
    private void RebuildTeamOverrideMap()
    {
        _teamScaleMap.Clear();

        if (teamScaleOverrides == null) return;

        for (int i = 0; i < teamScaleOverrides.Count; i++)
        {
            var ovr = teamScaleOverrides[i];
            if (ovr == null) continue;
            if (string.IsNullOrWhiteSpace(ovr.teamRootName)) continue;

            _teamScaleMap[ovr.teamRootName.Trim()] = Mathf.Max(0.01f, ovr.scale);
        }
    }

    /// <summary>
    /// Register or update an icon instance for a team root.
    /// Safe to call multiple times.
    /// </summary>
    public RectTransform EnsureIconForTeam(Transform teamRoot)
    {
        if (!teamRoot) return null;

        if (_iconsByTeamRoot.TryGetValue(teamRoot, out var existing) && existing)
            return existing;

        if (!iconPrefab || !iconParent)
            return null;

        var icon = Instantiate(iconPrefab, iconParent);
        icon.name = $"EnemyTeamIcon_{teamRoot.name}";
        icon.localScale = new Vector3(iconLocalScale, iconLocalScale, 1f);

        _iconsByTeamRoot[teamRoot] = icon;
        return icon;
    }

    /// <summary>
    /// Applies per-team scale to the StarImage child (preferred) or the root as fallback.
    /// This is called every LateUpdate so it wins over scripts that normalize scale.
    /// </summary>
    private void ApplyScaleToIcon(RectTransform iconRoot, string teamRootName)
    {
        if (!iconRoot) return;

        float s = 1f;
        if (!string.IsNullOrEmpty(teamRootName) && _teamScaleMap.TryGetValue(teamRootName, out var mapScale))
            s = mapScale;

        s = Mathf.Max(0.01f, s);

        // Try to find StarImage child
        var star = iconRoot.transform.Find("OrbitalAnchor/StarImage") as Transform;
        RectTransform starRect = star ? star.GetComponent<RectTransform>() : null;

        if (starRect)
        {
            // Drive by sizeDelta (more reliable for UI than localScale in your setup)
            if (iconSizePixels > 0f)
                starRect.sizeDelta = new Vector2(iconSizePixels, iconSizePixels);

            // If base size is 0 for some reason, fall back to localScale
            if (starRect.sizeDelta.sqrMagnitude > 0.001f)
            {
                starRect.sizeDelta = starRect.sizeDelta.normalized * 0f + (starRect.sizeDelta * s);
            }
            else
            {
                starRect.localScale = new Vector3(s, s, 1f);
            }
        }
        else
        {
            // Fallback: scale root
            iconRoot.localScale = new Vector3(iconLocalScale * s, iconLocalScale * s, 1f);
        }
    }

    void LateUpdate()
    {
        // Keep overrides current if list is edited at runtime
        // (cheap: dictionary already built; this does nothing unless list changed via inspector)
        // If you want, you can comment this out.
        // RebuildTeamOverrideMap();

        // Update each icon's scale. (Positioning/following handled elsewhere in your project.)
        foreach (var kvp in _iconsByTeamRoot)
        {
            var teamRoot = kvp.Key;
            var iconRoot = kvp.Value;
            if (!teamRoot || !iconRoot) continue;

            ApplyScaleToIcon(iconRoot, teamRoot.name);
        }
    }
}
