using System;
using System.Collections;
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

    [Header("Drone Spawn Height")]
    [Tooltip("If true, any spawned prefab that contains a DroneEnemyController will be raised by droneSpawnYOffset on Y.")]
    public bool raiseDroneSpawns = true;

    [Tooltip("How many units to add to Y when spawning drones (e.g., 25).")]
    public float droneSpawnYOffset = 25f;

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

    [Header("Enemy Inbound Warning UI")]
    [Tooltip("Optional warning UI to show ONLY when a reinforcement/backup enemy group spawns.")]
    public GameObject enemyInboundWarningUI;

    [Tooltip("How long to show the inbound warning UI.")]
    public float enemyInboundWarningDuration = 2f;

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


    [Header("Enemy Team Icon Manual Size")]
    [Tooltip("If enabled, ignores team-size scaling and uses fixedEnemyIconScale for the enemy team star/icon.")]
    public bool useFixedEnemyIconScale = false;

    [Tooltip("Fixed scale to use when useFixedEnemyIconScale is enabled.")]
    public float fixedEnemyIconScale = 1f;

    [Tooltip("Additional multiplier applied on top of the computed/fixed scale (enemy icons only).")]
    public float enemyIconScaleMultiplier = 1f;

    [Header("Enemy Team Icon Debug")]
    public bool debugLogEnemyIconScale = false;
    public float debugEnemyIconScaleInterval = 1.0f;

    // runtime caches
    private readonly Dictionary<string, Transform> _teamRoots = new Dictionary<string, Transform>();

    // One icon per enemy team
    private readonly Dictionary<string, EnemyTeamIconRuntime> _enemyTeamIcons = new Dictionary<string, EnemyTeamIconRuntime>();

    // Track LIVE members per enemy team so icon follows their centroid
    private readonly Dictionary<string, List<Transform>> _enemyTeamMembers = new Dictionary<string, List<Transform>>();

    // Per-team manual scale overrides for enemy team icons (keyed by team root name)
    [Serializable]
    private struct EnemyIconScaleOverride
    {
        public bool enabled;
        public float scale;
        public float multiplier;
    }

    private readonly Dictionary<string, EnemyIconScaleOverride> _enemyIconScaleOverrides = new Dictionary<string, EnemyIconScaleOverride>();

    private readonly Dictionary<string, float> _nextEnemyIconScaleLogTime = new Dictionary<string, float>();



    // Spawned ALLY teams (POC): register groups with TeamManager so your existing ally-team star/icon logic works.
    // Keyed by SpawnGroup.teamIndex.
    private readonly Dictionary<int, Transform> _allyTeamLeaders = new Dictionary<int, Transform>();
    private readonly Dictionary<int, Team> _allyTeams = new Dictionary<int, Team>();
    private Canvas _iconsCanvas;


    private RectTransform _resolvedEnemyTeamIconsParent;
    private Coroutine _enemyInboundWarningRoutine;

    private class EncounterReinforcementRuntime
    {
        public Transform watchedTeamRoot;
        public readonly HashSet<EnemyHealthController> watchedMembers = new HashSet<EnemyHealthController>();
        public int backupGroupIndex = -1;
        public int deathsRequired = 3;
        public int deathCount = 0;
        public bool triggered = false;
        public Transform backupSpawnPointOverride;
        public bool debugLogs = false;
    }

    private readonly List<EncounterReinforcementRuntime> _reinforcementRuntimes = new List<EncounterReinforcementRuntime>();

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

    private void OnEnable()
    {
        EnemyHealthController.OnAnyEnemyDied += HandleAnyEnemyDied;
        DroneEnemyController.OnAnyDroneDied += HandleAnyDroneDied;
    }

    private void OnDisable()
    {
        EnemyHealthController.OnAnyEnemyDied -= HandleAnyEnemyDied;
        DroneEnemyController.OnAnyDroneDied -= HandleAnyDroneDied;
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
        _reinforcementRuntimes.Clear();

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

            EnsureEnemyTeamIcon(teamRootName, teamRoot, prefabToUse, false);
        }
    }

    private void LateUpdate()
    {
        UpdateEnemyTeamIcons();
    }

    private GameObject PickPrefabForSpawn(SpawnGroup group, int spawnIndex)
    {
        if (group.prefabOptions == null || group.prefabOptions.Length == 0)
            return group.prefab;

        // Clean nulls (optional)
        // If mode is Single, always use first entry.
        if (group.prefabMode == SpawnPrefabMode.Single)
            return group.prefabOptions[0];

        if (group.prefabMode == SpawnPrefabMode.Cycle)
        {
            int i = Mathf.Abs(spawnIndex) % group.prefabOptions.Length;
            return group.prefabOptions[i];
        }

        int r = UnityEngine.Random.Range(0, group.prefabOptions.Length);
        return group.prefabOptions[r];
    }

    private GameObject[] BuildPrefabSequenceForGroup(SpawnGroup group)
    {
        // Priority 1: exact quantities
        if (group.prefabQuantities != null && group.prefabQuantities.Length > 0)
        {
            var list = new List<GameObject>(64);

            for (int i = 0; i < group.prefabQuantities.Length; i++)
            {
                var pq = group.prefabQuantities[i];
                if (pq.prefab == null) continue;

                int c = Mathf.Max(0, pq.count);
                for (int k = 0; k < c; k++)
                    list.Add(pq.prefab);
            }

            if (list.Count == 0) return null;

            if (group.shuffleQuantities)
            {
                // Fisher–Yates shuffle
                for (int i = 0; i < list.Count - 1; i++)
                {
                    int j = UnityEngine.Random.Range(i, list.Count);
                    var tmp = list[i];
                    list[i] = list[j];
                    list[j] = tmp;
                }
            }

            return list.ToArray();
        }

        // Priority 2: prefabOptions + mode
        if (group.prefabOptions != null && group.prefabOptions.Length > 0)
        {
            int total = Mathf.Max(0, group.count);
            if (total == 0) return null;

            var list = new GameObject[total];
            for (int i = 0; i < total; i++)
                list[i] = PickPrefabForSpawn(group, i);
            return list;
        }

        // Priority 3: legacy single prefab
        if (group.prefab != null)
        {
            int total = Mathf.Max(0, group.count);
            if (total == 0) return null;

            var list = new GameObject[total];
            for (int i = 0; i < total; i++)
                list[i] = group.prefab;
            return list;
        }

        return null;
    }

    private Vector3[] ResolvePatrolPointsWorld(SpawnGroup group)
    {
        if (group.patrolPoints == null || group.patrolPoints.Length == 0) return null;

        var pts = new Vector3[group.patrolPoints.Length];
        for (int i = 0; i < group.patrolPoints.Length; i++)
        {
            var t = group.patrolPoints[i];
            pts[i] = (t != null) ? t.position : transform.position;
        }
        return pts;
    }



    private void SpawnGroups(SpawnGroup[] groups, string teamPrefix, Faction faction)
    {
        if (groups == null) return;

        for (int g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            if (faction == Faction.Enemy && group.spawnOnlyAsReinforcement)
                continue;

            SpawnGroupByIndex(groups, g, teamPrefix, faction, null, null);
        }
    }

    private Transform SpawnGroupByIndex(SpawnGroup[] groups, int groupIndex, string teamPrefix, Faction faction, Vector3? runtimeObjectivePosition, Transform forcedSpawnPoint)
    {
        if (groups == null || groupIndex < 0 || groupIndex >= groups.Length) return null;

        var group = groups[groupIndex];
        if (!group.enabled) return null;

        Transform teamRoot = null;
        string teamRootName = null;

        if (createTeamRoots && group.teamIndex > 0)
        {
            teamRootName = $"{teamPrefix}{group.teamIndex}";
            teamRoot = GetOrCreateTeamRoot(teamRootName, faction);

            if (faction == Faction.Enemy && group.overrideEnemyTeamIconScale && !string.IsNullOrEmpty(teamRootName))
            {
                var ov = new EnemyIconScaleOverride
                {
                    enabled = true,
                    scale = (group.manualEnemyTeamIconScale > 0f) ? group.manualEnemyTeamIconScale : 1f,
                    multiplier = (group.manualEnemyTeamIconScaleMultiplier > 0f) ? group.manualEnemyTeamIconScaleMultiplier : 1f
                };
                _enemyIconScaleOverrides[teamRootName] = ov;
                if (EnemyTeamIconSystem.Instance != null)
                    EnemyTeamIconSystem.Instance.SetTeamScaleOverride(teamRootName, ov.scale, ov.multiplier, true);
            }

            // Important: reinforcement-only enemy groups do NOT exist during the startup icon pass,
            // so make sure their star/icon is created at the moment the team root is spawned.
            if (faction == Faction.Enemy && spawnEnemyTeamIcons && !string.IsNullOrEmpty(teamRootName) && teamRoot != null)
            {
                var prefabToUse = group.teamIconPrefabOverride != null ? group.teamIconPrefabOverride : defaultEnemyTeamIconPrefab;
                if (prefabToUse != null)
                    EnsureEnemyTeamIcon(teamRootName, teamRoot, prefabToUse, group.spawnOnlyAsReinforcement);
            }
        }

        if (forcedSpawnPoint != null)
        {
            group.spawnPoints = new Transform[] { forcedSpawnPoint };
            group.spawnAreaCenter = null;
            group.spawnAreaRadius = 0f;
        }

        var prefabSequence = BuildPrefabSequenceForGroup(group);
        if (prefabSequence == null || prefabSequence.Length == 0)
        {
            Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] SpawnGroup {groupIndex} has no prefab(s) assigned or count is 0.", this);
            return teamRoot;
        }

        List<GameObject> spawnedMembers = faction == Faction.Enemy ? new List<GameObject>(prefabSequence.Length) : null;

        int count = prefabSequence.Length;
        for (int i = 0; i < count; i++)
        {
            var spawnPose = ResolveSpawnPoseWithSeparation(group, i);
            var chosenPrefab = prefabSequence[i];
            if (chosenPrefab == null)
            {
                Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Group {groupIndex} has a null prefab in its sequence.", this);
                continue;
            }

            var spawnPos = spawnPose.position;
            if (raiseDroneSpawns && PrefabIsDrone(chosenPrefab))
                spawnPos += Vector3.up * droneSpawnYOffset;

            var go = Instantiate(chosenPrefab, spawnPos, spawnPose.rotation);

            if (teamRoot != null)
            {
                go.transform.SetParent(teamRoot, true);

                if (faction == Faction.Enemy && !string.IsNullOrEmpty(teamRootName))
                    RegisterEnemyTeamMember(teamRootName, go.transform);
            }

            if (faction == Faction.Enemy)
                spawnedMembers?.Add(go);

            if (faction == Faction.Ally && group.teamIndex > 0)
                RegisterSpawnedAllyTeamMember(group.teamIndex, go.transform);

            ApplyFactionTag(go, group);
            BroadcastBehavior(go, group, teamRoot, runtimeObjectivePosition);
        }

        if (faction == Faction.Enemy && group.enableReinforcementTrigger)
            RegisterEncounterReinforcement(group, teamRoot, spawnedMembers, groupIndex);

        return teamRoot;
    }

    private void RegisterEncounterReinforcement(SpawnGroup group, Transform watchedTeamRoot, List<GameObject> spawnedMembers, int sourceGroupIndex)
    {
        if (watchedTeamRoot == null)
        {
            if (group.reinforcementDebugLogs)
                Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Reinforcement trigger on enemy group {sourceGroupIndex} needs a team root (teamIndex > 0).", this);
            return;
        }

        int backupIndex = group.reinforcementEnemyGroupIndex;
        if (enemyGroups == null || backupIndex < 0 || backupIndex >= enemyGroups.Length)
        {
            if (group.reinforcementDebugLogs)
                Debug.LogWarning($"[{nameof(EncounterDirectorPOC)}] Reinforcement backup group index {backupIndex} is out of range for source enemy group {sourceGroupIndex}.", this);
            return;
        }

        var rt = new EncounterReinforcementRuntime
        {
            watchedTeamRoot = watchedTeamRoot,
            backupGroupIndex = backupIndex,
            deathsRequired = Mathf.Max(0, group.reinforcementDeathsRequired),
            backupSpawnPointOverride = group.reinforcementSpawnPointOverride,
            debugLogs = group.reinforcementDebugLogs
        };

        if (spawnedMembers != null)
        {
            for (int i = 0; i < spawnedMembers.Count; i++)
            {
                var go = spawnedMembers[i];
                if (go == null) continue;
                var hp = go.GetComponent<EnemyHealthController>();
                if (hp != null) rt.watchedMembers.Add(hp);
            }
        }

        _reinforcementRuntimes.Add(rt);

        if (rt.debugLogs)
            Debug.Log($"[{nameof(EncounterDirectorPOC)}] Watching enemy group {sourceGroupIndex} team '{watchedTeamRoot.name}' for {rt.deathsRequired} deaths. Backup group index = {rt.backupGroupIndex}.", this);

        if (rt.deathsRequired <= 0)
            TriggerEncounterReinforcement(rt);
    }

    private void HandleAnyEnemyDied(EnemyHealthController dead)
    {
        if (dead == null || _reinforcementRuntimes.Count == 0) return;

        for (int i = _reinforcementRuntimes.Count - 1; i >= 0; i--)
        {
            var rt = _reinforcementRuntimes[i];
            if (rt == null || rt.triggered) continue;

            bool belongs = rt.watchedMembers.Contains(dead);
            if (!belongs && rt.watchedTeamRoot != null)
                belongs = dead.transform.IsChildOf(rt.watchedTeamRoot);

            if (!belongs) continue;

            rt.deathCount++;
            if (rt.debugLogs)
                Debug.Log($"[{nameof(EncounterDirectorPOC)}] Counted enemy death {rt.deathCount}/{rt.deathsRequired} for watched team '{(rt.watchedTeamRoot != null ? rt.watchedTeamRoot.name : "<null>")}'.", this);

            if (rt.deathCount >= rt.deathsRequired)
                TriggerEncounterReinforcement(rt);
        }
    }

    private void HandleAnyDroneDied(DroneEnemyController dead)
    {
        if (dead == null || _reinforcementRuntimes.Count == 0) return;

        for (int i = _reinforcementRuntimes.Count - 1; i >= 0; i--)
        {
            var rt = _reinforcementRuntimes[i];
            if (rt == null || rt.triggered) continue;

            bool belongs = rt.watchedTeamRoot != null && dead.transform.IsChildOf(rt.watchedTeamRoot);
            if (!belongs) continue;

            rt.deathCount++;
            if (rt.debugLogs)
                Debug.Log($"[{nameof(EncounterDirectorPOC)}] Counted drone death {rt.deathCount}/{rt.deathsRequired} for watched team '{(rt.watchedTeamRoot != null ? rt.watchedTeamRoot.name : "<null>")}'.", this);

            if (rt.deathCount >= rt.deathsRequired)
                TriggerEncounterReinforcement(rt);
        }
    }

    private void TriggerEncounterReinforcement(EncounterReinforcementRuntime rt)
    {
        if (rt == null || rt.triggered) return;
        rt.triggered = true;

        Transform spawned = SpawnGroupByIndex(enemyGroups, rt.backupGroupIndex, enemyTeamRootPrefix, Faction.Enemy, null, rt.backupSpawnPointOverride);

        if (spawned != null)
            ShowEnemyInboundWarningUI();

        if (rt.debugLogs)
            Debug.Log(spawned != null
                ? $"[{nameof(EncounterDirectorPOC)}] Spawned reinforcement enemy group {rt.backupGroupIndex} using its own group behavior/objective data."
                : $"[{nameof(EncounterDirectorPOC)}] Failed to spawn reinforcement enemy group {rt.backupGroupIndex}.", this);
    }

    private void ShowEnemyInboundWarningUI()
    {
        if (enemyInboundWarningUI == null)
            return;

        if (_enemyInboundWarningRoutine != null)
            StopCoroutine(_enemyInboundWarningRoutine);

        _enemyInboundWarningRoutine = StartCoroutine(ShowEnemyInboundWarningRoutine());
    }

    private IEnumerator ShowEnemyInboundWarningRoutine()
    {
        SetEnemyInboundWarningVisible(true);
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, enemyInboundWarningDuration));
        SetEnemyInboundWarningVisible(false);
        _enemyInboundWarningRoutine = null;
    }

    private void SetEnemyInboundWarningVisible(bool visible)
    {
        if (enemyInboundWarningUI == null)
            return;

        enemyInboundWarningUI.SetActive(visible);

        if (visible)
        {
            var rect = enemyInboundWarningUI.transform as RectTransform;
            if (rect != null)
                rect.SetAsLastSibling();

            var groups = enemyInboundWarningUI.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group == null) continue;
                group.alpha = 1f;
                group.interactable = false;
                group.blocksRaycasts = false;
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

    private void BroadcastBehavior(GameObject go, SpawnGroup group, Transform teamRoot, Vector3? runtimeDefendCenterOverride = null)
    {
        if (go == null) return;

        // Always broadcast behavior enum (existing hooks in EncounterPatrolAgent / EncounterDefendAgent)
        EncounterBehavior effectiveBehavior = runtimeDefendCenterOverride.HasValue ? EncounterBehavior.Defend : group.initialBehavior;
        go.SendMessage("Encounter_SetBehavior", effectiveBehavior, SendMessageOptions.DontRequireReceiver);

        // --- Patrol route points ---
        // If patrolPoints are provided, broadcast them so any script (including EncounterPatrolAgent) can consume them.
        var patrolWorld = ResolvePatrolPointsWorld(group);
        if (!runtimeDefendCenterOverride.HasValue && patrolWorld != null && patrolWorld.Length > 0)
        {
            go.SendMessage("Encounter_SetPatrolPoints", patrolWorld, SendMessageOptions.DontRequireReceiver);

            // Controller-aware routing (Enemy2 / MeleeEnemy2 / Drone) so real AI prefabs can follow routes/objectives
            if (group.enableControllerAwareRouting)
                EnsureObjectiveRouter(go, group, teamRoot, patrolWorld, Vector3.zero, 0f, false);

            // Optionally add built-in Patrol agent (NavMeshAgent based) ONLY if the unit doesn't already have a movement controller.
            if (group.addBuiltInPatrolAgentIfMissing && group.initialBehavior == EncounterBehavior.Patrol)
            {
                EnsureBuiltInPatrol(go);
            }

            // Optionally set team anchor planned destination so the team direction arrow points along the route.
            if (group.updateTeamAnchorPlannedDestination && teamRoot != null)
            {
                var anchor = teamRoot.GetComponent<EncounterTeamAnchor>();
                if (anchor != null)
                    anchor.SetMoveTarget(patrolWorld[0]);
            }
        }

        // --- Hold / Defend center ---
        // For quick POC, we drive hold/defend by broadcasting a DefendPayload.
        // If you don't assign defendCenter, we fall back to spawnAreaCenter/teamRoot/EncounterDirector position.
        if (runtimeDefendCenterOverride.HasValue || group.initialBehavior == EncounterBehavior.Hold || group.initialBehavior == EncounterBehavior.Defend)
        {
            Vector3 center;
            if (runtimeDefendCenterOverride.HasValue)
            {
                center = runtimeDefendCenterOverride.Value;
            }
            else if (group.defendCenter != null) center = group.defendCenter.position;
            else if (group.spawnAreaCenter != null) center = group.spawnAreaCenter.position;
            else if (group.spawnPoints != null && group.spawnPoints.Length > 0 && group.spawnPoints[0] != null) center = group.spawnPoints[0].position;
            else if (teamRoot != null) center = teamRoot.position;
            else center = transform.position;

            bool treatAsDefend = runtimeDefendCenterOverride.HasValue || group.initialBehavior == EncounterBehavior.Defend;
            float radius = treatAsDefend ? Mathf.Max(0f, group.defendRadius) : 0f;

            go.SendMessage("Encounter_SetDefend", new DefendPayload(center, radius), SendMessageOptions.DontRequireReceiver);

            // Controller-aware routing (Enemy2 / MeleeEnemy2 / Drone) so real AI prefabs can move to hold/defend
            if (group.enableControllerAwareRouting)
                EnsureObjectiveRouter(go, group, teamRoot, null, center, radius, true);

            if (group.addBuiltInDefendAgentIfMissing && (runtimeDefendCenterOverride.HasValue || group.initialBehavior == EncounterBehavior.Hold || group.initialBehavior == EncounterBehavior.Defend))
            {
                EnsureBuiltInDefend(go);
            }

            // Arrow points to defend/hold center
            if (teamRoot != null)
            {
                var anchor = teamRoot.GetComponent<EncounterTeamAnchor>();
                if (anchor != null)
                    anchor.SetMoveTarget(center);
            }
        }
    }

    private void EnsureBuiltInPatrol(GameObject go)
    {
        if (go == null) return;

        // Only add if a NavMeshAgent exists AND no known movement controllers exist.
        if (go.GetComponent<NavMeshAgent>() == null) return;
        if (HasAnyMovementController(go)) return;

        if (go.GetComponent<EncounterPatrolAgent>() == null)
            go.AddComponent<EncounterPatrolAgent>();
    }

    private void EnsureBuiltInDefend(GameObject go)
    {
        if (go == null) return;

        if (go.GetComponent<NavMeshAgent>() == null) return;
        if (HasAnyMovementController(go)) return;

        if (go.GetComponent<EncounterDefendAgent>() == null)
            go.AddComponent<EncounterDefendAgent>();
    }


    private void EnsureObjectiveRouter(GameObject go, SpawnGroup group, Transform teamRoot, Vector3[] patrolWorld, Vector3 defendCenter, float defendRadius, bool isDefend)
    {
        if (go == null) return;

        // Only add router if prefab has one of the supported controllers OR you explicitly want navmesh fallback routing.
        bool hasEnemy2 = go.GetComponentInChildren<Enemy2Controller>(true) != null;
        bool hasMelee2 = go.GetComponentInChildren<MeleeEnemy2Controller>(true) != null;
        bool hasDrone = go.GetComponentInChildren<DroneEnemyController>(true) != null;

        if (!hasEnemy2 && !hasMelee2 && !hasDrone)
        {
            // If no controller, router is still useful to drive a plain NavMeshAgent (same as EncounterPatrolAgent/DefendAgent).
            if (!group.enableRouterForPlainNavMeshAgents) return;
            if (go.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) == null) return;
        }

        var router = go.GetComponent<EncounterObjectiveRouter>();
        if (router == null) router = go.AddComponent<EncounterObjectiveRouter>();

        // Configure
        router.loopPatrol = group.patrolLoop;
        router.pingPong = group.patrolPingPong;
        router.arriveDistance = Mathf.Max(0.1f, group.routerArriveDistance);
        router.updateInterval = Mathf.Max(0.05f, group.routerUpdateInterval);
        router.maxSeconds = Mathf.Max(0.5f, group.routerMaxSeconds);

        if (isDefend)
            router.SetDefend(defendCenter, defendRadius);
        else if (patrolWorld != null && patrolWorld.Length > 0)
            router.SetPatrol(patrolWorld);
    }
    private bool HasAnyMovementController(GameObject go)
    {
        // If any of these exist, the prefab is already driving movement (we should NOT add built-in agents).
        if (go.GetComponentInChildren<Enemy2Controller>(true) != null) return true;
        if (go.GetComponentInChildren<MeleeEnemy2Controller>(true) != null) return true;
        if (go.GetComponentInChildren<DroneEnemyController>(true) != null) return true;

        // Extend here as you add more controllers.
        return false;
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

    private void EnsureEnemyTeamIcon(string teamRootName, Transform teamRoot, RectTransform prefabToUse, bool showDirectionArrow)
    {
        if (_enemyTeamIcons.TryGetValue(teamRootName, out var rt) && rt != null)
        {
            if (rt.iconRect != null && rt.prefabUsed == prefabToUse)
            {
                rt.teamRoot = teamRoot;
                SetEnemyTeamArrowVisible(rt.iconRect, showDirectionArrow);

                if (showDirectionArrow)
                {
                    var existingAnchor = teamRoot != null ? teamRoot.GetComponent<EncounterTeamAnchor>() : null;
                    var existingArrow = GetOrCreateEnemyTeamArrow(rt.iconRect);
                    if (existingAnchor != null && existingArrow != null)
                    {
                        var existingCam = commandCamera != null ? commandCamera : (Camera.main != null ? Camera.main : null);
                        existingArrow.Bind(existingAnchor, existingCam);
                    }
                }

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

        string hoverHint = BuildEnemyTeamHoverHint(teamRootName);
        bridge.hoverHintMessage = hoverHint;
        bridge.Bind(teamRoot);

        // --- Direction arrow (UI-only) ---
        // This does NOT drive enemy movement. It only reads the team's anchor position and/or movement.
        // IMPORTANT: The arrow must live on a child (eg. ArrowImage), NOT on the root icon image,
        // otherwise it will overwrite the star sprite and you'll "lose" the team icon.
        var anchor = teamRoot != null ? teamRoot.GetComponent<EncounterTeamAnchor>() : null;
        if (showDirectionArrow && anchor != null)
        {
            var arrow = GetOrCreateEnemyTeamArrow(iconGO);
            if (arrow != null)
            {
                // Use commandCamera if available; otherwise fall back to the Canvas camera or Main.
                var cam = commandCamera != null ? commandCamera : (Camera.main != null ? Camera.main : null);
                arrow.Bind(anchor, cam);
            }
        }

        SetEnemyTeamArrowVisible(iconGO, showDirectionArrow);

        var runtime = new EnemyTeamIconRuntime
        {
            teamRootName = teamRootName,
            teamRoot = teamRoot,
            iconRect = iconGO,
            prefabUsed = prefabToUse
        };

        _enemyTeamIcons[teamRootName] = runtime;
    }



    private string BuildEnemyTeamHoverHint(string teamRootName)
    {
        if (enemyGroups != null)
        {
            for (int i = 0; i < enemyGroups.Length; i++)
            {
                var group = enemyGroups[i];
                if (!group.enabled || group.teamIndex <= 0) continue;

                string expectedRoot = $"{enemyTeamRootPrefix}{group.teamIndex}";
                if (!string.Equals(expectedRoot, teamRootName, StringComparison.Ordinal))
                    continue;

                string title = !string.IsNullOrWhiteSpace(group.hoverHintTitle)
                    ? group.hoverHintTitle.Trim()
                    : "Enemy Team";

                string grade = !string.IsNullOrWhiteSpace(group.strengthGrade)
                    ? group.strengthGrade.Trim()
                    : string.Empty;

                return string.IsNullOrWhiteSpace(grade)
                    ? title
                    : $"{title}\nStrength: {grade}";
            }
        }

        return "Enemy Team";
    }

    private EnemyTeamDirectionArrowUI GetOrCreateEnemyTeamArrow(RectTransform iconRoot)
    {
        if (iconRoot == null) return null;

        // Prefer an existing arrow component on a CHILD object (ArrowImage), not on the root team icon.
        var existingArrows = iconRoot.GetComponentsInChildren<EnemyTeamDirectionArrowUI>(true);
        for (int i = 0; i < existingArrows.Length; i++)
        {
            var existing = existingArrows[i];
            if (existing != null && existing.transform != iconRoot)
                return existing;
        }

        // Otherwise, try to find a child that should host the arrow graphic.
        Transform arrowHost = FindNamedDescendant(iconRoot, "ArrowImage");
        if (arrowHost == null) arrowHost = FindNamedDescendant(iconRoot, "Arrow");
        if (arrowHost == null) arrowHost = FindNamedDescendant(iconRoot, "ArrowGraphic");

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

    private static Transform FindNamedDescendant(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;

        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t != null && t != root && string.Equals(t.name, name, StringComparison.Ordinal))
                return t;
        }

        return null;
    }

    private void SetEnemyTeamArrowVisible(RectTransform iconRoot, bool visible)
    {
        if (iconRoot == null) return;

        // Disable/enable all arrow scripts without accidentally hiding the ROOT icon object.
        var arrows = iconRoot.GetComponentsInChildren<EnemyTeamDirectionArrowUI>(true);
        for (int i = 0; i < arrows.Length; i++)
        {
            var arrow = arrows[i];
            if (arrow == null) continue;

            arrow.enabled = visible;

            if (arrow.transform != iconRoot)
                arrow.gameObject.SetActive(visible);
        }

        // Also explicitly hide/show any known arrow graphic children on the prefab.
        Transform arrowHost = FindNamedDescendant(iconRoot, "ArrowImage");
        if (arrowHost == null) arrowHost = FindNamedDescendant(iconRoot, "Arrow");
        if (arrowHost == null) arrowHost = FindNamedDescendant(iconRoot, "ArrowGraphic");

        if (arrowHost != null)
            arrowHost.gameObject.SetActive(visible);
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

        List<string> deadTeams = null;

        foreach (var kvp in _enemyTeamIcons)
        {
            var data = kvp.Value;
            if (data == null || data.iconRect == null)
            {
                if (deadTeams == null) deadTeams = new List<string>();
                deadTeams.Add(kvp.Key);
                continue;
            }

            int liveCount = GetEnemyTeamLiveMemberCount(data.teamRootName);
            if (liveCount <= 0)
            {
                if (data.iconRect != null)
                    Destroy(data.iconRect.gameObject);

                if (deadTeams == null) deadTeams = new List<string>();
                deadTeams.Add(kvp.Key);
                continue;
            }

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

            float s;

            if (_enemyIconScaleOverrides.TryGetValue(data.teamRootName, out var ov) && ov.enabled)
            {
                s = (ov.scale > 0f) ? ov.scale : 1f;
                if (ov.multiplier > 0f) s *= ov.multiplier;
            }
            else
            {
                if (useFixedEnemyIconScale)
                {
                    s = fixedEnemyIconScale;
                }
                else if (scaleEnemyTeamIconsBySize)
                {
                    s = ComputeEnemyTeamIconScale(liveCount);
                }
                else
                {
                    s = 1f;
                }

                s *= enemyIconScaleMultiplier;
            }

            data.iconRect.localScale = Vector3.one;
            ApplyEnemyTeamStarImageScale(data.iconRect, s);
            MaybeLogEnemyIconScale(data.teamRootName, liveCount, s, data.iconRect);
            data.iconRect.SetAsLastSibling();
        }

        if (deadTeams != null)
        {
            for (int i = 0; i < deadTeams.Count; i++)
            {
                string key = deadTeams[i];
                _enemyTeamIcons.Remove(key);
                _enemyTeamMembers.Remove(key);
                _enemyIconScaleOverrides.Remove(key);
                _nextEnemyIconScaleLogTime.Remove(key);
            }
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

                if (!t.gameObject.activeInHierarchy)
                    continue;

                sum += t.position;
                alive++;
            }

            if (alive > 0)
                return sum / alive;
        }

        return fallbackTeamRoot != null ? fallbackTeamRoot.position : transform.position;
    }



    // --- Enemy Team Icon scaling helpers (StarImage-based) ---
    // Some UI scripts keep the icon root scale at (1,1,1). To ensure the visual skull/star scales,
    // we scale the child "StarImage" RectTransform directly.
    private readonly Dictionary<int, RectTransform> _enemyIconStarCache = new Dictionary<int, RectTransform>();

    private void ApplyEnemyTeamStarImageScale(RectTransform iconRoot, float s)
    {
        if (iconRoot == null) return;

        int id = iconRoot.GetInstanceID();
        if (!_enemyIconStarCache.TryGetValue(id, out var starRect) || starRect == null)
        {
            starRect = FindChildRectByName(iconRoot, "StarImage");
            _enemyIconStarCache[id] = starRect;
        }

        if (starRect != null)
        {
            starRect.localScale = new Vector3(s, s, 1f);
        }
    }

    private static RectTransform FindChildRectByName(RectTransform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;

        // Includes inactive
        var rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i] != null && rects[i].name == childName)
                return rects[i];
        }
        return null;
    }

    // ---------------- Data ----------------

    [Serializable]
    public struct SpawnGroup
    {
        [Header("Enabled")]
        public bool enabled;

        [Header("Spawn")]
        [Tooltip("Legacy single prefab. Leave empty if using prefabQuantities or prefabOptions.")]
        public GameObject prefab;

        [Tooltip("Optional: exact quantities per prefab (Enemy / Assault Droid / Drone). Takes priority over prefabOptions/count.")]
        public PrefabQuantity[] prefabQuantities;

        [Tooltip("Optional: multiple prefabs. Used if prefabQuantities is empty.")]
        public GameObject[] prefabOptions;

        public SpawnPrefabMode prefabMode;

        [Tooltip("If prefabQuantities is empty: total number to spawn (legacy).")]
        public int count;

        [Tooltip("If using prefabQuantities, shuffle the spawn order so types are mixed.")]
        public bool shuffleQuantities;
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

        [Header("Enemy Team Hover Hint")]
        [Tooltip("Main hover title shown when hovering this enemy team icon.")]
        public string hoverHintTitle;

        [Tooltip("Optional manual strength grade, for example A+, A, B-, C, D-. Leave blank to omit the second line.")]
        public string strengthGrade;

        [Tooltip("If enabled (Enemy only), this group will set a manual scale for that enemy team's star/icon (per teamIndex).")]
        public bool overrideEnemyTeamIconScale;

        [Tooltip("Manual enemy team icon scale to apply for this teamIndex when overrideEnemyTeamIconScale is enabled.")]
        public float manualEnemyTeamIconScale;

        [Tooltip("Optional additional multiplier applied with manualEnemyTeamIconScale (Enemy only).")]
        public float manualEnemyTeamIconScaleMultiplier;


        [Header("Behavior")]
        public EncounterBehavior initialBehavior;


        [Tooltip("Optional route points for Patrol. If set, we broadcast Encounter_SetPatrolPoints(Vector3[]) to spawned units.")]
        public Transform[] patrolPoints;

        [Tooltip("If true and patrolPoints are set, adds a built-in patrol agent if the unit does not already have one.")]
        public bool addBuiltInPatrolAgentIfMissing;

        [Tooltip("If true, sets the team anchor planned destination to the first patrol point (for UI direction arrows).")]
        public bool updateTeamAnchorPlannedDestination;

        [Header("Defend / Hold (Optional)")]
        [Tooltip("Optional: center point for Hold/Defend. If null, uses spawnAreaCenter / first spawnPoint / team root / director.")]
        public Transform defendCenter;

        [Tooltip("Defend radius. Used only when initialBehavior=Defend. (0 = Hold behavior).")]
        public float defendRadius;

        [Tooltip("If true and initialBehavior is Hold/Defend, adds EncounterDefendAgent if the unit does not already have its own movement controller.")]
        public bool addBuiltInDefendAgentIfMissing;

        [Header("Controller-Aware Routing (Recommended)")]
        [Tooltip("If true, EncounterDirectorPOC will add EncounterObjectiveRouter to units so Enemy2/Melee/Drone controllers can follow patrol/defend objectives without disabling their AI.")]
        public bool enableControllerAwareRouting;

        [Tooltip("If true, EncounterObjectiveRouter may also drive plain NavMeshAgents that do not have Enemy2/Melee/Drone controllers.")]
        public bool enableRouterForPlainNavMeshAgents;

        [Tooltip("Patrol route looping behavior. If both loop and pingpong are false, route is one-way and stops at last point.")]
        public bool patrolLoop;

        [Tooltip("Patrol ping-pong behavior. If true, route reverses at ends.")]
        public bool patrolPingPong;

        [Tooltip("How close to a route point / defend center counts as arrived.")]
        public float routerArriveDistance;

        [Tooltip("How often to update controller targets / destinations while routing.")]
        public float routerUpdateInterval;

        [Tooltip("Safety timeout: stop routing after this many seconds (0 means never).")]
        public float routerMaxSeconds;


        [Header("Encounter Spawn Control")]
        [Tooltip("If enabled, this enemy group will NOT spawn during SpawnAll/SpawnOnStart. It can still be spawned later as a reinforcement backup group.")]
        public bool spawnOnlyAsReinforcement;

        [Header("Reinforcement Trigger (Enemy Only)")]
        [Tooltip("If enabled, this spawned enemy group is watched. After enough deaths from this exact group, a backup enemy group is spawned.")]
        public bool enableReinforcementTrigger;

        [Tooltip("How many deaths from this exact spawned group are required before the backup group spawns.")]
        public int reinforcementDeathsRequired;

        [Tooltip("Index into Enemy Groups for the backup group that should spawn when the threshold is reached.")]
        public int reinforcementEnemyGroupIndex;

        [Tooltip("Optional: override spawn point for the backup group. Leave empty to use the backup group's own spawn settings.")]
        public Transform reinforcementSpawnPointOverride;

        [Tooltip("Enable logs for this group's reinforcement trigger.")]
        public bool reinforcementDebugLogs;

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

    [Serializable]
    public struct PrefabQuantity
    {
        public GameObject prefab;
        public int count;
    }

    public enum SpawnPrefabMode
    {
        Single = 0,
        Random = 1,
        Cycle = 2
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

                if (!t.gameObject.activeInHierarchy)
                    continue;

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

    private bool PrefabIsDrone(GameObject prefab)
    {
        if (prefab == null) return false;
        return prefab.GetComponentInChildren<DroneEnemyController>(true) != null;
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