using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

/// <summary>
/// Proof-of-concept "Level/Encounter Director".
/// - Spawns Ally and Enemy groups at scene start.
/// - Optionally parents them under team roots (e.g., EnemyTeam_1) to establish "teams" without relying on existing TeamManager code.
///
/// Enemy Team Icons (Command Mode)
/// - Each Enemy Group can optionally specify a UI prefab for that team's icon.
/// - Icons are spawned once per Enemy Team root and follow the LIVE centroid of the team's members (spawned enemies).
///
/// NEW (Fix): Spawn Separation
/// - When multiple units spawn from the same spawn point or within a small area, they can overlap.
/// - This script now offsets each spawn using a ring pattern + NavMesh.SamplePosition so they begin separated immediately.
/// </summary>
public class EncounterDirectorPOC : MonoBehaviour
{
    [Header("Encounter")]
    public string encounterName = "CurrentLevel_POC";
    public bool spawnOnStart = true;

    [Header("Teams / Parenting")]
    [Tooltip("Creates team root GameObjects and parents spawned units under them for organization + future team-anchor UI.")]
    public bool createTeamRoots = true;

    [Tooltip("Root name prefix for enemy teams. Example: EnemyTeam_1, EnemyTeam_2 ...")]
    public string enemyTeamRootPrefix = "EnemyTeam_";

    [Tooltip("Root name prefix for ally teams. Example: AllyTeam_1, AllyTeam_2 ...")]
    public string allyTeamRootPrefix = "AllyTeam_";

    [Tooltip("Optional parent under which all team roots will be created (keeps Hierarchy tidy).")]
    public Transform teamRootsParent;

    [Header("Enemy Groups")]
    public SpawnGroup[] enemyGroups;

    [Header("Ally Groups")]
    public SpawnGroup[] allyGroups;

    [Header("Enemy Team Icons (Command Mode)")]
    public bool spawnEnemyTeamIcons = true;

    [Tooltip("Default team icon prefab used when an Enemy Group does not specify its own Team Icon Prefab Override. (UI prefab with RectTransform)")]
    public RectTransform defaultEnemyTeamIconPrefab;

    [Tooltip("Where spawned team icons will live in the UI hierarchy. Use a child under your MiniUI canvas, e.g. MiniUI/EnemyTeamIcons.")]
    public RectTransform enemyTeamIconsParent;

    [Tooltip("Camera used for projecting world -> screen for the team icons. Usually your CommandCamera.")]
    public Camera commandCamera;

    [Tooltip("If enabled, icons will only be visible when the Command Camera component is enabled and active.")]
    public bool onlyShowWhenCommandCameraEnabled = true;

    [Header("Enemy Team Icon Placement")]
    public Vector3 iconWorldOffset = new Vector3(0f, 2f, 0f);
    public Vector2 iconScreenOffsetPixels = Vector2.zero;

    [Header("Enemy Team Icon Scaling (by team size)")]
    [Tooltip("If enabled, enemy team icons scale up as the team size grows.")]
    public bool scaleEnemyTeamIconsBySize = true;

    [Tooltip("Scale when team size == 1.")]
    public float enemyIconBaseScale = 1f;

    [Tooltip("Growth factor. Start with 0.20 to confirm it works, then tune down (0.04-0.08).")]
    public float enemyIconGrowth = 0.20f;

    [Tooltip("Minimum clamp for the icon scale.")]
    public float enemyIconMinScale = 0.90f;

    [Tooltip("Maximum clamp for the icon scale.")]
    public float enemyIconMaxScale = 2.00f;

    [Tooltip("If true, uses sqrt growth (nice for larger teams). If false, linear growth.")]
    public bool enemyIconUseSqrt = true;

    [Header("Enemy Team Icon Debug")]
    public bool debugLogEnemyIconScale = false;
    public float debugEnemyIconScaleInterval = 1.0f;

    // runtime caches
    private readonly Dictionary<string, Transform> _teamRoots = new Dictionary<string, Transform>();

    // One icon per enemy team
    private readonly Dictionary<string, EnemyTeamIconRuntime> _enemyTeamIcons = new Dictionary<string, EnemyTeamIconRuntime>();

    // Track LIVE members per enemy team so icon follows their centroid
    private readonly Dictionary<string, List<Transform>> _enemyTeamMembers = new Dictionary<string, List<Transform>>();

    private readonly Dictionary<string, float> _nextEnemyIconScaleLogTime = new Dictionary<string, float>();



    // Spawned ALLY teams (POC): register groups with TeamManager so your existing ally-team star/icon logic works.
    // Keyed by SpawnGroup.teamIndex.
    private readonly Dictionary<int, Transform> _allyTeamLeaders = new Dictionary<int, Transform>();
    private readonly Dictionary<int, Team> _allyTeams = new Dictionary<int, Team>();
    private Canvas _iconsCanvas;


    private RectTransform _resolvedEnemyTeamIconsParent;

    private RectTransform ResolveEnemyTeamIconsParent()
    {
        if (enemyTeamIconsParent != null)
            return enemyTeamIconsParent;

        if (_resolvedEnemyTeamIconsParent != null)
            return _resolvedEnemyTeamIconsParent;

        // If you don't want a dedicated "EnemyTeamIcons" root in the UI hierarchy,
        // we can fall back to CommandOverlayUI's canvasRoot automatically.
        CommandOverlayUI overlay = null;
#if UNITY_2023_1_OR_NEWER
        overlay = UnityEngine.Object.FindFirstObjectByType<CommandOverlayUI>();
#else
        overlay = UnityEngine.Object.FindObjectOfType<CommandOverlayUI>();
#endif
        if (overlay != null && overlay.canvasRoot != null)
        {
            _resolvedEnemyTeamIconsParent = overlay.canvasRoot;
            return _resolvedEnemyTeamIconsParent;
        }

        return null;
    }

    private void Start()
    {
        if (!spawnOnStart) return;

        SpawnAll();

        if (spawnEnemyTeamIcons)
            RefreshEnemyTeamIcons();
    }

    [ContextMenu("Spawn All (POC)")]
    public void SpawnAll()
    {
        // Clear cached members so repeat spawns don't accumulate old references (POC convenience)
        _enemyTeamMembers.Clear();

        _allyTeamLeaders.Clear();
        _allyTeams.Clear();
        SpawnGroups(enemyGroups, enemyTeamRootPrefix, Faction.Enemy);
        SpawnGroups(allyGroups, allyTeamRootPrefix, Faction.Ally);
    }

    [ContextMenu("Refresh Enemy Team Icons (POC)")]
    public void RefreshEnemyTeamIcons()
    {
        if (!spawnEnemyTeamIcons) return;

        var parent = ResolveEnemyTeamIconsParent();
        if (parent == null)
        {
            Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Enemy Team Icons Parent is not assigned and CommandOverlayUI canvasRoot could not be found. Icons will not spawn.", this);
            return;
        }

        _iconsCanvas = parent.GetComponentInParent<Canvas>();
        if (_iconsCanvas == null)
        {
            Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Enemy Team Icons Parent is not under a Canvas. Icons will not position correctly.", this);
        }

        if (enemyGroups == null) return;

        for (int g = 0; g < enemyGroups.Length; g++)
        {
            var group = enemyGroups[g];
            if (!group.enabled) continue;
            if (group.teamIndex <= 0) continue;

            var teamRootName = $"{enemyTeamRootPrefix}{group.teamIndex}";
            if (!_teamRoots.TryGetValue(teamRootName, out var teamRoot) || teamRoot == null)
            {
                var found = GameObject.Find(teamRootName);
                if (found != null) teamRoot = found.transform;
                if (teamRoot == null)
                {
                    Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Could not find team root '{teamRootName}' for enemy group {g}.", this);
                    continue;
                }

                _teamRoots[teamRootName] = teamRoot;
            }

            var prefabToUse = group.teamIconPrefabOverride != null ? group.teamIconPrefabOverride : defaultEnemyTeamIconPrefab;
            if (prefabToUse == null)
            {
                Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] No team icon prefab available for enemy group {g} (Team {group.teamIndex}). Assign Team Icon Prefab Override or set Default Enemy Team Icon Prefab.", this);
                continue;
            }

            EnsureEnemyTeamIcon(teamRootName, teamRoot, prefabToUse);
        }
    }

    private void LateUpdate()
    {
        UpdateEnemyTeamIcons();
    }

    private void SpawnGroups(SpawnGroup[] groups, string teamPrefix, Faction faction)
    {
        if (groups == null) return;

        for (int g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            if (!group.enabled) continue;

            if (group.prefab == null)
            {
                Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Group {g} has no prefab assigned.", this);
                continue;
            }

            Transform teamRoot = null;
            string teamRootName = null;

            if (createTeamRoots && group.teamIndex > 0)
            {
                teamRootName = $"{teamPrefix}{group.teamIndex}";
                teamRoot = GetOrCreateTeamRoot(teamRootName, faction);
            }

            int count = Mathf.Max(0, group.count);
            for (int i = 0; i < count; i++)
            {
                var spawnPose = ResolveSpawnPoseWithSeparation(group, i);
                var go = Instantiate(group.prefab, spawnPose.position, spawnPose.rotation);

                if (teamRoot != null)
                {
                    go.transform.SetParent(teamRoot, true);

                    // Track live enemy members so the icon follows their centroid.
                    if (faction == Faction.Enemy && !string.IsNullOrEmpty(teamRootName))
                        RegisterEnemyTeamMember(teamRootName, go.transform);
                }


                // Register spawned ALLY groups into TeamManager so the ally team icon/star
                // can follow a leader (first spawned) and keep working with your existing command UI.
                if (faction == Faction.Ally && group.teamIndex > 0)
                    RegisterSpawnedAllyTeamMember(group.teamIndex, go.transform);

                ApplyFactionTag(go, group);
                BroadcastBehavior(go, group);
            }
        }
    }

    private void RegisterEnemyTeamMember(string teamRootName, Transform member)
    {
        if (member == null) return;

        if (!_enemyTeamMembers.TryGetValue(teamRootName, out var list) || list == null)
        {
            list = new List<Transform>(8);
            _enemyTeamMembers[teamRootName] = list;
        }

        if (!list.Contains(member))
            list.Add(member);
    }

    private void RegisterSpawnedAllyTeamMember(int teamIndex, Transform member)
    {
        if (teamIndex <= 0) return;
        if (member == null) return;
        if (TeamManager.Instance == null) return;

        // First spawned member becomes the leader.
        if (!_allyTeamLeaders.TryGetValue(teamIndex, out var leader) || leader == null)
        {
            _allyTeamLeaders[teamIndex] = member;
            return;
        }

        // Create the Team the moment we have at least two members.
        if (!_allyTeams.TryGetValue(teamIndex, out var team) || team == null)
        {
            team = TeamManager.Instance.CreateTeam(leader, member);
            if (team != null)
            {
                // Spawned ally teams are LEADER-based (star sits on the leader).
                team.Anchor = leader;
                _allyTeams[teamIndex] = team;
            }
            return;
        }

        // Add additional members WITHOUT triggering TeamManager's auto-formation.
        // This keeps spawned teams "stay put" at their spawn positions.
        team.Add(member);
        team.Anchor = leader;
    }


    // ---------------- Spawn Separation ----------------

    private (Vector3 position, Quaternion rotation) ResolveSpawnPoseWithSeparation(SpawnGroup group, int index)
    {
        // Get the base position first (exact spawn point OR random within radius).
        var basePose = ResolveSpawnPose(group, index);
        var basePos = basePose.position;

        // If this is the first unit, use the base position as-is.
        if (index == 0)
            return basePose;

        // Spacing (if 0, fall back to something reasonable)
        float spacing = group.spawnSeparation <= 0.01f ? 1.25f : group.spawnSeparation;

        // Ring pattern: spread units around the base. Uses a golden-angle spiral for nicer distribution.
        Vector2 offset2D = GoldenAngleOffset(index, spacing);

        // Try a few times, sampling NavMesh (best effort).
        int attempts = Mathf.Max(1, group.maxSeparationAttempts);
        for (int a = 0; a < attempts; a++)
        {
            // Slightly increase radius each attempt so we can find a nearby valid spot.
            float radiusScale = 1f + (a * 0.35f);
            Vector3 candidate = basePos + new Vector3(offset2D.x, 0f, offset2D.y) * radiusScale;

            if (group.snapToNavMesh)
            {
                if (TrySampleNavMesh(candidate, spacing * 2f, out var navPos))
                    return (navPos, basePose.rotation);
            }
            else
            {
                return (candidate, basePose.rotation);
            }

            // If NavMesh sampling fails, rotate the offset a bit and try again.
            offset2D = Rotate2D(offset2D, 35f);
        }

        // Final fallback: base position (better than nothing).
        return basePose;
    }

    private static Vector2 GoldenAngleOffset(int index, float spacing)
    {
        // Golden angle in radians (~2.399963)
        const float golden = 2.39996323f;

        // Spiral radius grows with sqrt(index)
        float r = spacing * Mathf.Sqrt(index);
        float theta = index * golden;

        return new Vector2(Mathf.Cos(theta) * r, Mathf.Sin(theta) * r);
    }

    private static Vector2 Rotate2D(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private static bool TrySampleNavMesh(Vector3 near, float maxDistance, out Vector3 sampled)
    {
        if (NavMesh.SamplePosition(near, out var hit, Mathf.Max(0.5f, maxDistance), NavMesh.AllAreas))
        {
            sampled = hit.position;
            return true;
        }

        sampled = near;
        return false;
    }

    // ---------------- Base Spawn Pose ----------------

    private (Vector3 position, Quaternion rotation) ResolveSpawnPose(SpawnGroup group, int index)
    {
        // Spawn point list takes priority.
        if (group.spawnPoints != null && group.spawnPoints.Length > 0)
        {
            var t = group.spawnPoints[Mathf.Abs(index) % group.spawnPoints.Length];
            if (t != null)
                return (t.position, t.rotation);
        }

        // Otherwise use area center +/- radius.
        var center = group.spawnAreaCenter != null ? group.spawnAreaCenter.position : transform.position;
        var pos = center;

        if (group.spawnAreaRadius > 0.01f)
        {
            var r = UnityEngine.Random.insideUnitSphere;
            r.y = 0f;
            pos = center + r.normalized * UnityEngine.Random.Range(0f, group.spawnAreaRadius);
        }

        return (pos, Quaternion.identity);
    }

    private Transform GetOrCreateTeamRoot(string name, Faction faction)
    {
        if (_teamRoots.TryGetValue(name, out var existing) && existing != null)
            return existing;

        var rootGO = new GameObject(name);

        if (teamRootsParent != null)
            rootGO.transform.SetParent(teamRootsParent, false);
        else
            rootGO.transform.SetParent(transform, false);


        // If this is an Enemy team root, add a centroid anchor so members can leash to it
        // and so the UI icon (and future logic) has a stable "team position" reference.
        if (faction == Faction.Enemy)
        {
            var anchor = rootGO.GetComponent<EncounterTeamAnchor>();
            if (anchor == null) anchor = rootGO.AddComponent<EncounterTeamAnchor>();
            anchor.faction = Faction.Enemy;
            anchor.updateContinuously = true;
            anchor.smooth = true;
            anchor.smoothSpeed = 10f;
        }

        _teamRoots[name] = rootGO.transform;
        return rootGO.transform;
    }

    private void ApplyFactionTag(GameObject go, SpawnGroup group)
    {
        if (!string.IsNullOrWhiteSpace(group.overrideUnityTag))
        {
            TrySetTag(go, group.overrideUnityTag);
            return;
        }

        if (!string.IsNullOrWhiteSpace(group.fallbackTagIfUntagged) && go.tag == "Untagged")
            TrySetTag(go, group.fallbackTagIfUntagged);
    }

    private void TrySetTag(GameObject go, string tagName)
    {
        try { go.tag = tagName; }
        catch (Exception) { /* Tag not defined in Tag Manager; ignore */ }
    }

    private void BroadcastBehavior(GameObject go, SpawnGroup group)
    {
        go.SendMessage("Encounter_SetBehavior", group.initialBehavior, SendMessageOptions.DontRequireReceiver);
    }

    // ---------------- Enemy Team Icons ----------------

    [Serializable]
    private class EnemyTeamIconRuntime
    {
        public string teamRootName;
        public Transform teamRoot;
        public RectTransform iconRect;
        public RectTransform prefabUsed;
    }

    private void EnsureEnemyTeamIcon(string teamRootName, Transform teamRoot, RectTransform prefabToUse)
    {
        if (_enemyTeamIcons.TryGetValue(teamRootName, out var rt) && rt != null)
        {
            if (rt.iconRect != null && rt.prefabUsed == prefabToUse)
            {
                rt.teamRoot = teamRoot;
                return;
            }

            if (rt.iconRect != null)
                Destroy(rt.iconRect.gameObject);

            _enemyTeamIcons.Remove(teamRootName);
        }

        var parent = ResolveEnemyTeamIconsParent();
        if (parent == null) return;

        var iconGO = Instantiate(prefabToUse, parent);
        iconGO.name = $"EnemyTeamIcon_{teamRootName}";
        // Ensure team icon renders above unit icons in the same canvas.
        iconGO.SetAsLastSibling();

        // Bind targeting bridge so this team icon supports hover preview + click commit targeting.
        // Team root Transform acts as the persistent EnemyTeamAnchor.
        var bridge = iconGO.GetComponent<EnemyTeamIconTargetingBridge>();
        if (bridge == null)
            bridge = iconGO.gameObject.AddComponent<EnemyTeamIconTargetingBridge>();
        bridge.Bind(teamRoot);

        // --- Direction arrow (UI-only) ---
        // This does NOT drive enemy movement. It only reads the team's anchor position and/or movement.
        // IMPORTANT: The arrow must live on a child (eg. ArrowImage), NOT on the root icon image,
        // otherwise it will overwrite the star sprite and you'll "lose" the team icon.
        var anchor = teamRoot != null ? teamRoot.GetComponent<EncounterTeamAnchor>() : null;
        if (anchor != null)
        {
            var arrow = GetOrCreateEnemyTeamArrow(iconGO);
            if (arrow != null)
            {
                // Use commandCamera if available; otherwise fall back to the Canvas camera or Main.
                var cam = commandCamera != null ? commandCamera : (Camera.main != null ? Camera.main : null);
                arrow.Bind(anchor, cam);
            }
        }

        var runtime = new EnemyTeamIconRuntime
        {
            teamRootName = teamRootName,
            teamRoot = teamRoot,
            iconRect = iconGO,
            prefabUsed = prefabToUse
        };

        _enemyTeamIcons[teamRootName] = runtime;
    }



    private EnemyTeamDirectionArrowUI GetOrCreateEnemyTeamArrow(RectTransform iconRoot)
    {
        if (iconRoot == null) return null;

        // Prefer an existing arrow component in children (your prefab likely already has one on ArrowImage).
        var existing = iconRoot.GetComponentInChildren<EnemyTeamDirectionArrowUI>(true);
        if (existing != null)
            return existing;

        // Otherwise, try to find a child that should host the arrow graphic.
        Transform arrowHost = iconRoot.Find("ArrowImage");
        if (arrowHost == null) arrowHost = iconRoot.Find("Arrow");
        if (arrowHost == null) arrowHost = iconRoot.Find("ArrowGraphic");

        if (arrowHost == null)
        {
            // Create a dedicated child so we NEVER steal the root icon's Image component.
            var go = new GameObject("ArrowImage", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(iconRoot, false);
            arrowHost = go.transform;

            var rt = (RectTransform)arrowHost;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(32f, 32f);
        }
        else
        {
            // Make sure the host has an Image so the arrow can render.
            if (arrowHost.GetComponent<Image>() == null)
                arrowHost.gameObject.AddComponent<Image>();
        }

        var arrow = arrowHost.GetComponent<EnemyTeamDirectionArrowUI>();
        if (arrow == null)
            arrow = arrowHost.gameObject.AddComponent<EnemyTeamDirectionArrowUI>();

        // Auto-wire refs (should now bind to ArrowImage instead of the star icon).
        arrow.TryAutoWire();
        return arrow;
    }
    private void UpdateEnemyTeamIcons()
    {
        if (!spawnEnemyTeamIcons) return;
        if (_enemyTeamIcons.Count == 0) return;

        var parentRect = ResolveEnemyTeamIconsParent();
        if (parentRect == null) return;

        if (commandCamera == null) return;

        bool show = true;
        if (onlyShowWhenCommandCameraEnabled)
            show = commandCamera.enabled && commandCamera.gameObject.activeInHierarchy;


        Camera uiCamera = null;
        if (_iconsCanvas != null && _iconsCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = _iconsCanvas.worldCamera != null ? _iconsCanvas.worldCamera : commandCamera;
        }

        foreach (var kvp in _enemyTeamIcons)
        {
            var data = kvp.Value;
            if (data == null || data.iconRect == null) continue;

            if (!show)
            {
                if (data.iconRect.gameObject.activeSelf)
                    data.iconRect.gameObject.SetActive(false);
                continue;
            }

            Vector3 anchorWorld = ResolveEnemyTeamAnchorWorld(data.teamRootName, data.teamRoot);
            var worldPos = anchorWorld + iconWorldOffset;
            var screen = commandCamera.WorldToScreenPoint(worldPos);

            if (screen.z <= 0.01f)
            {
                if (data.iconRect.gameObject.activeSelf)
                    data.iconRect.gameObject.SetActive(false);
                continue;
            }

            if (!data.iconRect.gameObject.activeSelf)
                data.iconRect.gameObject.SetActive(true);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, uiCamera, out var local))
            {
                local += iconScreenOffsetPixels;
                data.iconRect.anchoredPosition = local;
            }
            // Scale by live team size (applied here, once per frame).
            if (scaleEnemyTeamIconsBySize)
            {
                int liveCount = GetEnemyTeamLiveMemberCount(data.teamRootName);
                float s = ComputeEnemyTeamIconScale(liveCount);
                data.iconRect.localScale = new Vector3(s, s, 1f);
                MaybeLogEnemyIconScale(data.teamRootName, liveCount, s, data.iconRect);
            }



            // Keep above other UI elements that may be rebuilt/reordered.
            data.iconRect.SetAsLastSibling();
        }
    }

    private Vector3 ResolveEnemyTeamAnchorWorld(string teamRootName, Transform fallbackTeamRoot)
    {
        if (_enemyTeamMembers.TryGetValue(teamRootName, out var list) && list != null && list.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            int alive = 0;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                sum += t.position;
                alive++;
            }

            if (alive > 0)
                return sum / alive;
        }

        return fallbackTeamRoot != null ? fallbackTeamRoot.position : transform.position;
    }

    // ---------------- Data ----------------

    [Serializable]
    public struct SpawnGroup
    {
        [Header("Enabled")]
        public bool enabled;

        [Header("Spawn")]
        public GameObject prefab;
        public int count;

        [Tooltip("If set, spawns cycle through these points. Takes priority over spawn area.")]
        public Transform[] spawnPoints;

        [Tooltip("If spawnPoints is empty, spawns within radius around this transform (or EncounterDirector if null).")]
        public Transform spawnAreaCenter;

        [Tooltip("Radius used when spawnPoints is empty.")]
        public float spawnAreaRadius;

        [Header("Spawn Separation (Fix)")]
        [Tooltip("How far apart units spawn when multiple units are spawned for this group.")]
        public float spawnSeparation;

        [Tooltip("If true, tries to place separated spawns onto the NavMesh near the candidate point.")]
        public bool snapToNavMesh;

        [Tooltip("How many attempts to find a nearby valid separated position (NavMesh sampling).")]
        public int maxSeparationAttempts;

        [Header("Team")]
        [Tooltip("0 = no team parenting. 1 = team root #1, 2 = team root #2, etc.")]
        public int teamIndex;

        [Tooltip("Optional. If this group is an Enemy team (teamIndex > 0), you can override the icon prefab used for that team.")]
        public RectTransform teamIconPrefabOverride;

        [Header("Behavior")]
        public EncounterBehavior initialBehavior;

        [Header("Tagging (Optional)")]
        public string overrideUnityTag;
        public string fallbackTagIfUntagged;
    }


    [Serializable]
    public struct DefendPayload
    {
        public Vector3 center;
        public float radius;

        public DefendPayload(Vector3 c, float r)
        {
            center = c;
            radius = r;
        }
    }

    public enum Faction
    {
        Enemy,
        Ally
    }

    // ---------------- Enemy Team Icon Scaling Helpers ----------------

    private float ComputeEnemyTeamIconScale(int teamSize)
    {
        int c = Mathf.Max(1, teamSize);

        float s;
        if (enemyIconUseSqrt)
            s = enemyIconBaseScale + enemyIconGrowth * (Mathf.Sqrt(c) - 1f);
        else
            s = enemyIconBaseScale + enemyIconGrowth * (c - 1f);

        return Mathf.Clamp(s, enemyIconMinScale, enemyIconMaxScale);
    }

    /// <summary>
    /// Returns the live (non-null) member count for a team and cleans null references.
    /// </summary>
    private int GetEnemyTeamLiveMemberCount(string teamRootName)
    {
        if (string.IsNullOrEmpty(teamRootName)) return 0;

        if (_enemyTeamMembers.TryGetValue(teamRootName, out var list) && list != null)
        {
            int alive = 0;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                alive++;
            }

            return alive;
        }

        return 0;
    }

    private void MaybeLogEnemyIconScale(string teamRootName, int count, float scale, RectTransform iconRect)
    {
        if (!debugLogEnemyIconScale) return;
        float interval = Mathf.Max(0.05f, debugEnemyIconScaleInterval);

        float now = Time.unscaledTime;
        if (_nextEnemyIconScaleLogTime.TryGetValue(teamRootName, out float next) && now < next)
            return;

        _nextEnemyIconScaleLogTime[teamRootName] = now + interval;

        Debug.Log($"[EncounterDirectorPOC] Enemy icon '{teamRootName}' count={count} scale={scale:0.00} rectScale={iconRect.localScale}", this);
    }

}

public enum EncounterBehavior
{
    None = 0,
    Hold = 1,
    Patrol = 2,
    Defend = 3,
    Hunt = 4,
    Search = 5
}