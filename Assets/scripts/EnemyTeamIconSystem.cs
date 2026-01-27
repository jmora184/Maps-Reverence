using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Read-only UI system that displays one icon per Enemy Team root.
///
/// Compatibility note:
/// - Some versions of EncounterTeamAnchor do NOT expose an 'anchorTransform' field.
/// - This script does not depend on that field; it excludes a child named "Anchor" when counting members.
///
/// Setup (quick):
/// 1) Under your UI Canvas (or CommandOverlay canvas), create an empty: EnemyTeamIconSystem
/// 2) Add this component.
/// 3) Create an icon prefab (UI -> Image) and add EnemyTeamIconUI to it.
/// 4) Assign iconPrefab + iconParent.
/// 5) Assign worldCamera = your command camera (or main camera for now).
/// </summary>
public class EnemyTeamIconSystem : MonoBehaviour
{
    public static EnemyTeamIconSystem Instance { get; private set; }

    [Header("Prefab + Parent")]
    public EnemyTeamIconUI iconPrefab;
    public RectTransform iconParent;

    [Header("Cameras / Projection")]
    [Tooltip("Camera used to project world points to screen points. For command mode icons, use your command camera if you have one.")]
    public Camera worldCamera;

    [Header("Placement")]
    [Tooltip("World-space offset added to the team's anchor position (e.g., to raise above ground).")]
    public Vector3 worldOffset = new Vector3(0f, 1.8f, 0f);

    [Tooltip("Screen-space pixel offset after projection.")]
    public Vector2 screenOffsetPixels = Vector2.zero;

    [Header("Visibility")]
    [Tooltip("If true, only show icons when command mode is active. You can wire this later.")]
    public bool onlyShowInCommandMode = false;

    [Tooltip("Optional: a Behaviour that indicates command mode active by being enabled (e.g., your command camera component).")]
    public Behaviour commandModeEnabledIndicator;

    private readonly Dictionary<EncounterTeamAnchor, EnemyTeamIconUI> _iconsByAnchor = new Dictionary<EncounterTeamAnchor, EnemyTeamIconUI>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void LateUpdate()
    {
        if (onlyShowInCommandMode)
        {
            bool active = commandModeEnabledIndicator != null ? commandModeEnabledIndicator.enabled : false;
            if (!active)
            {
                foreach (var kv in _iconsByAnchor)
                    if (kv.Value != null) kv.Value.SetVisible(false);
                return;
            }
        }

        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;

        RefreshAnchors();

        foreach (var kv in _iconsByAnchor)
        {
            var anchor = kv.Key;
            var icon = kv.Value;
            if (anchor == null || icon == null) continue;

            // Enemy teams only (if your EncounterTeamAnchor has this field)
            // If you removed faction, just comment this out.
            if (anchor.faction != EncounterDirectorPOC.Faction.Enemy)
            {
                icon.SetVisible(false);
                continue;
            }

            Vector3 worldPos = anchor.AnchorWorldPosition + worldOffset;
            Vector3 screen = worldCamera.WorldToScreenPoint(worldPos);

            if (screen.z < 0.01f)
            {
                icon.SetVisible(false);
                continue;
            }

            icon.SetScreenPosition((Vector2)screen + screenOffsetPixels);
            icon.SetVisible(true);

            icon.SetCount(CountTeamMembers(anchor.transform));
        }
    }

    private int CountTeamMembers(Transform teamRoot)
    {
        if (teamRoot == null) return 0;

        int c = 0;
        for (int i = 0; i < teamRoot.childCount; i++)
        {
            var t = teamRoot.GetChild(i);
            if (t == null) continue;

            // Exclude a helper child if present
            if (t.name == "Anchor") continue;

            c++;
        }
        return c;
    }

    private void RefreshAnchors()
    {
        // POC approach: find anchors in scene.
        // Later we can register directly from EncounterDirectorPOC for zero searching.
        var anchors = GameObject.FindObjectsOfType<EncounterTeamAnchor>(true);

        // Add missing
        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            if (a == null) continue;
            if (_iconsByAnchor.ContainsKey(a)) continue;

            var icon = CreateIconFor(a);
            _iconsByAnchor[a] = icon;
        }

        // Remove dead
        var dead = new List<EncounterTeamAnchor>();
        foreach (var kv in _iconsByAnchor)
        {
            if (kv.Key == null || kv.Value == null)
                dead.Add(kv.Key);
        }
        for (int i = 0; i < dead.Count; i++)
            _iconsByAnchor.Remove(dead[i]);
    }

    private EnemyTeamIconUI CreateIconFor(EncounterTeamAnchor anchor)
    {
        if (iconPrefab == null || iconParent == null)
        {
            Debug.LogWarning("[EnemyTeamIconSystem] iconPrefab or iconParent not set.", this);
            return null;
        }

        var icon = Instantiate(iconPrefab, iconParent);
        icon.name = $"EnemyTeamIcon_{anchor.gameObject.name}";
        icon.SetVisible(true);

        // Enable attack preview + click targeting on the Enemy Team icon.
        // This binds the UI icon to the team root (EncounterTeamAnchor's Transform) so CommandOverlayUI can treat it like an enemy target.
        var bridge = icon.GetComponent<EnemyTeamIconTargetingBridge>();
        if (bridge == null) bridge = icon.gameObject.AddComponent<EnemyTeamIconTargetingBridge>();
        bridge.hoverHintMessage = "Enemy Team";
        bridge.Bind(anchor.transform);

        return icon;
    }
}