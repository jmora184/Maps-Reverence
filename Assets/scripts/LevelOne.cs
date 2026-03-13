using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// LevelOne - Spawn teams that march to an objective, but still fight if they get aggro.
///
/// This version is controller-aware (fixes your regression):
/// - Enemy2Controller enemies: HARD objective injection using private _combatTarget + chasing (works with their internal StopAgent/team leash behavior).
/// - MeleeEnemy2Controller enemies: march by setting their combatTarget to an objective proxy via public SetCombatTarget(proxy),
///   but STOP overriding as soon as they acquire a real target (player/ally/animal) so they can fight.
/// - DroneEnemyController: march by setting waypoints to an objective proxy while in Patrol; if it acquires a real combatTarget, we stop overriding.
/// - Fallback: NavMeshAgent march (simple SetDestination) for other units.
///
/// IMPORTANT:
/// - MoveTargetMode controls the objective: Transform / FixedPosition / Player.
/// - UI arrow still uses EncounterTeamAnchor.SetMoveTarget (direction only).
/// </summary>
public class LevelOne : MonoBehaviour
{
    [Header("Team Plans")]
    public List<TeamSpawnPlan> teams = new List<TeamSpawnPlan>();

    [Header("Ally Team Plans (LevelOne)")]
    [Tooltip("Optional ally teams to spawn. Allies do not patrol or use move-target mode in LevelOne.")]
    public List<AllySpawnPlan> allyTeams = new List<AllySpawnPlan>();

    [Header("Ally Team Registration")]
    [Tooltip("If enabled, spawned Ally teams are registered into TeamManager (same approach as EncounterDirectorPOC). This makes ally team star clicks + commands work like normal.")]
    public bool registerAllyTeamsWithTeamManager = true;


    [Header("Test Hotkey")]
    public bool enableHotkey = true;
    public KeyCode spawnAllKey = KeyCode.T;

    [Header("Enemy Team Icons (Command Mode)")]
    [Tooltip("UI prefab for an enemy team icon (root RectTransform that contains StarImage + OrbitalAnchor/ArrowImage + optional TMP).")]
    public RectTransform enemyTeamIconPrefab;

    [Tooltip("Where spawned enemy team icons will live in the UI hierarchy (e.g., MiniUI/EnemyIconParent).")]
    public RectTransform enemyTeamIconsParent;

    [Tooltip("Camera used for projecting world -> screen for the team icons (usually your CommandCamera).")]
    public Camera commandCamera;

    [Tooltip("If enabled, icons will only be visible when the Command Camera component is enabled and active.")]
    public bool onlyShowWhenCommandCameraEnabled = true;

    [Tooltip("World offset applied to the team anchor position when projecting to the UI.")]
    public Vector3 iconWorldOffset = new Vector3(0f, 2f, 0f);

    [Tooltip("Screen-space pixel offset applied after projection.")]
    public Vector2 iconScreenOffsetPixels = Vector2.zero;

    [Tooltip("If set (>0), this sets the icon root sizeDelta (square). Leave 0 to keep prefab size.")]
    public float iconRootSizePixels = 0f;

    [Tooltip("Base localScale for the icon root (UI).")]
    public float iconRootLocalScale = 1f;

    [Header("Icon Arrow Scaling")]
    [Tooltip("If true, ArrowImage sizeDelta scales with the team icon scale. If false, ArrowImage keeps its prefab size.")]
    public bool scaleArrowWithTeam = false;

    [Tooltip("If true, the arrow orbit radius scales with the team icon scale (using the prefab's orbitRadiusPixels as the base).")]
    public bool scaleArrowOrbitRadiusWithTeam = true;

    [Tooltip("Extra multiplier applied when scaling the arrow orbit radius.")]
    public float arrowOrbitRadiusMultiplier = 1f;

    [Header("Ally Team Icons (Optional)")]
    [Tooltip("UI prefab for an ally team icon. If null, allies will spawn without team icons.")]
    public RectTransform allyTeamIconPrefab;

    [Tooltip("Where spawned ally team icons will live in the UI hierarchy (e.g., MiniUI/AllyIconParent).")]
    public RectTransform allyTeamIconsParent;

    [Tooltip("Camera used for projecting world -> screen for the ally team icons. If null, uses Command Camera.")]
    public Camera allyIconCameraOverride;

    [Tooltip("World offset applied to the ally team anchor position when projecting to the UI.")]
    public Vector3 allyIconWorldOffset = new Vector3(0f, 2f, 0f);

    [Tooltip("Screen-space pixel offset applied after projection (ally icons).")]
    public Vector2 allyIconScreenOffsetPixels = Vector2.zero;

    [Tooltip("If enabled, ally icons will only be visible when the Command Camera component is enabled and active.")]
    public bool onlyShowAllyIconsWhenCommandCameraEnabled = true;

    [Tooltip("Multiplier applied to the ally team icon root scale (TeamStarIcon_UI). 1 = prefab scale.")]
    public float allyIconScaleMultiplier = 1f;





    // Runtime icon instances by team root
    private readonly Dictionary<Transform, TeamIconRuntimeData> _iconsByTeamRoot = new Dictionary<Transform, TeamIconRuntimeData>(32);

    private readonly Dictionary<Transform, TeamIconRuntimeData> _allyIconsByTeamRoot = new Dictionary<Transform, TeamIconRuntimeData>(16);

    // Track ally team members so the ally team star count is correct even if units are not parented under the team root.
    private readonly Dictionary<Transform, List<Transform>> _allyMembersByTeamRoot = new Dictionary<Transform, List<Transform>>(16);

    // Register spawned ALLY teams into TeamManager so your existing ally-team star/icon logic works.
    // Keyed by AllySpawnPlan.teamIndex.
    private readonly Dictionary<int, Transform> _allyTeamLeaders = new Dictionary<int, Transform>();
    private readonly Dictionary<int, Team> _allyTeams = new Dictionary<int, Team>();

    // Cached command state machine for icon click/targeting bridge binding.
    private CommandStateMachine _commandStateMachine;


    private void Update()
    {
        if (enableHotkey && Input.GetKeyDown(spawnAllKey))
            SpawnAllTeams();

        UpdateEnemyTeamIcons();
        UpdateAllyTeamIcons();
    }

    private CommandStateMachine ResolveCommandStateMachine()
    {
        if (_commandStateMachine != null) return _commandStateMachine;
#if UNITY_2023_1_OR_NEWER
        _commandStateMachine = UnityEngine.Object.FindFirstObjectByType<CommandStateMachine>();
#else
        _commandStateMachine = UnityEngine.Object.FindObjectOfType<CommandStateMachine>();
#endif
        return _commandStateMachine;
    }

    [ContextMenu("Spawn All Teams Now")]
    public void SpawnAllTeams()
    {
        if (teams == null || teams.Count == 0)
        {
            Debug.LogWarning("[LevelOne] No team plans configured.", this);
            return;
        }

        for (int i = 0; i < teams.Count; i++)
            SpawnTeam(i);

        if (allyTeams != null && allyTeams.Count > 0)
        {
            for (int i = 0; i < allyTeams.Count; i++)
                SpawnAllyTeam(i);
        }
    }

    public void SpawnTeam(int teamIndex)
    {
        SpawnTeamAndGetRuntime(teamIndex);
    }

    public SpawnedTeamRuntimeInfo SpawnTeamAndGetRuntime(int teamIndex)
    {
        if (teams == null || teamIndex < 0 || teamIndex >= teams.Count)
        {
            Debug.LogError($"[LevelOne] Invalid team index {teamIndex}.", this);
            return null;
        }

        TeamSpawnPlan plan = teams[teamIndex];
        if (plan == null)
        {
            Debug.LogError($"[LevelOne] Team plan at index {teamIndex} is null.", this);
            return null;
        }

        if (plan.spawnPoint == null)
        {
            Debug.LogError($"[LevelOne] Team plan {teamIndex} spawnPoint is not assigned.", this);
            return null;
        }

        bool usingEntries = plan.spawnEntries != null && plan.spawnEntries.Count > 0;
        if (!usingEntries && plan.enemyPrefab == null)
        {
            Debug.LogError($"[LevelOne] Team plan {teamIndex} has no spawnEntries and no enemyPrefab assigned.", this);
            return null;
        }

        plan.spawnSequence++;

        string safePrefix = string.IsNullOrWhiteSpace(plan.teamNamePrefix) ? "EnemyTeam_" : plan.teamNamePrefix;
        string baseName = string.IsNullOrWhiteSpace(plan.teamName)
            ? $"{safePrefix}{teamIndex + 1}_{plan.spawnSequence}"
            : $"{plan.teamName}_{plan.spawnSequence}";

        string teamRootName = $"{baseName}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

        var teamRootGO = new GameObject(teamRootName);
        teamRootGO.transform.position = plan.spawnPoint.position;

        var anchor = teamRootGO.AddComponent<EncounterTeamAnchor>();
        anchor.faction = EncounterDirectorPOC.Faction.Enemy;
        anchor.updateContinuously = true;
        anchor.smooth = true;
        anchor.smoothSpeed = plan.anchorSmoothSpeed;
        anchor.driveTransformPosition = false;

        ApplyEnemyIconScale(anchor, plan);
        EnsureIconForTeam(teamRootGO.transform, anchor, plan);

        if (plan.moveTargetMode != MoveTargetMode.None)
        {
            var uiFeeder = teamRootGO.AddComponent<PlannedTargetFeeder>();
            uiFeeder.mode = plan.moveTargetMode;
            uiFeeder.playerTag = plan.playerTag;
            uiFeeder.targetTransform = plan.targetTransform;
            uiFeeder.fixedWorldPosition = plan.fixedWorldPosition;
            uiFeeder.interval = Mathf.Max(0.05f, plan.updatePlannedTargetEvery);
            uiFeeder.updateContinuously = plan.updatePlannedTargetContinuously;
            uiFeeder.SetOnceNow();
        }

        var spawned = new List<GameObject>(32);
        if (usingEntries) SpawnFromEntries(plan, teamRootGO.transform, spawned);
        else SpawnLegacy(plan, teamRootGO.transform, spawned);

        if (plan.moveTargetMode != MoveTargetMode.None)
        {
            foreach (var unit in spawned)
            {
                if (unit == null) continue;

                if (plan.enableEnemy2HardMarch && HasMonoByName(unit, "Enemy2Controller"))
                {
                    var hard = unit.AddComponent<Enemy2HardMarchOverride>();
                    hard.mode = plan.moveTargetMode;
                    hard.playerTag = plan.playerTag;
                    hard.targetTransform = plan.targetTransform;
                    hard.fixedWorldPosition = plan.fixedWorldPosition;

                    hard.arriveDistance = Mathf.Max(0.1f, plan.objectiveArriveDistance);
                    hard.maxSeconds = Mathf.Max(0.1f, plan.objectiveMaxSeconds);
                    hard.updateInterval = Mathf.Max(0.05f, plan.objectiveUpdateInterval);

                    hard.disableTeamLeashWhileMarching = plan.disableEnemy2TeamLeashWhileMarching;
                    hard.debug = plan.debugMarch;
                    continue;
                }

                if (plan.enableMeleeMarch && HasMonoByName(unit, "MeleeEnemy2Controller"))
                {
                    var mm = unit.AddComponent<MeleeObjectiveMarchOverride>();
                    mm.mode = plan.moveTargetMode;
                    mm.playerTag = plan.playerTag;
                    mm.targetTransform = plan.targetTransform;
                    mm.fixedWorldPosition = plan.fixedWorldPosition;

                    mm.arriveDistance = Mathf.Max(0.1f, plan.objectiveArriveDistance);
                    mm.maxSeconds = Mathf.Max(0.1f, plan.objectiveMaxSeconds);
                    mm.updateInterval = Mathf.Max(0.05f, plan.objectiveUpdateInterval);

                    mm.debug = plan.debugMarch;
                    continue;
                }

                if (plan.enableDroneMarch && HasMonoByName(unit, "DroneEnemyController"))
                {
                    var dm = unit.AddComponent<DroneObjectiveMarchOverride>();
                    dm.mode = plan.moveTargetMode;
                    dm.playerTag = plan.playerTag;
                    dm.targetTransform = plan.targetTransform;
                    dm.fixedWorldPosition = plan.fixedWorldPosition;

                    dm.arriveDistance = Mathf.Max(0.1f, plan.objectiveArriveDistance);
                    dm.maxSeconds = Mathf.Max(0.1f, plan.objectiveMaxSeconds);
                    dm.updateInterval = Mathf.Max(0.05f, plan.objectiveUpdateInterval);

                    dm.debug = plan.debugMarch;
                    continue;
                }

                if (plan.enableFallbackNavAgentMarch)
                {
                    var nm = unit.AddComponent<NavAgentMarchToObjective>();
                    nm.mode = plan.moveTargetMode;
                    nm.playerTag = plan.playerTag;
                    nm.targetTransform = plan.targetTransform;
                    nm.fixedWorldPosition = plan.fixedWorldPosition;

                    nm.arriveDistance = Mathf.Max(0.1f, plan.objectiveArriveDistance);
                    nm.maxSeconds = Mathf.Max(0.1f, plan.objectiveMaxSeconds);
                    nm.updateInterval = Mathf.Max(0.05f, plan.objectiveUpdateInterval);

                    nm.aggroHoldSeconds = Mathf.Max(0f, plan.aggroHoldSeconds);
                    nm.debug = plan.debugMarch;
                }
            }
        }

        return new SpawnedTeamRuntimeInfo
        {
            teamIndex = teamIndex,
            plan = plan,
            teamRoot = teamRootGO.transform,
            spawnedUnits = spawned,
            anchor = anchor
        };
    }

    // UnityEvent-friendly wrappers
    public void SpawnTeam0() => SpawnTeam(0);
    public void SpawnTeam1() => SpawnTeam(1);
    public void SpawnTeam2() => SpawnTeam(2);
    public void SpawnTeam3() => SpawnTeam(3);
    public void SpawnTeam4() => SpawnTeam(4);


    private void ApplyEnemyIconScale(EncounterTeamAnchor anchor, TeamSpawnPlan plan)
    {
        if (anchor == null || plan == null) return;

        var t = anchor.GetType();

        bool anySet = false;

        anySet |= TrySetFieldOrProperty(t, anchor, "useFixedEnemyIconScale", plan.useFixedEnemyIconScale);
        anySet |= TrySetFieldOrProperty(t, anchor, "fixedEnemyIconScale", plan.fixedEnemyIconScale);
        anySet |= TrySetFieldOrProperty(t, anchor, "enemyIconScaleMultiplier", plan.enemyIconScaleMultiplier);

        // Some versions of the icon anchor use more generic names.
        anySet |= TrySetFieldOrProperty(t, anchor, "useFixedIconScale", plan.useFixedEnemyIconScale);
        anySet |= TrySetFieldOrProperty(t, anchor, "fixedIconScale", plan.fixedEnemyIconScale);
        anySet |= TrySetFieldOrProperty(t, anchor, "iconScaleMultiplier", plan.enemyIconScaleMultiplier);

        if (plan.debugEnemyIconScale && !anySet)
            Debug.LogWarning("[LevelOne] Could not apply enemy icon scale because EncounterTeamAnchor has no compatible scale fields/properties in this project version.", anchor);
    }

    private bool TrySetFieldOrProperty(Type t, object obj, string name, object value)
    {
        try
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                f.SetValue(obj, value);
                return true;
            }

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, value, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LevelOne] Failed setting {name} on {t.Name}: {ex.Message}", obj as UnityEngine.Object);
        }

        return false;
    }

    private void SpawnLegacy(TeamSpawnPlan plan, Transform parent, List<GameObject> spawned)
    {
        int count = Mathf.Max(0, plan.enemyCount);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = GetSpawnPosition(plan, plan.spawnPoint.position, plan.spawnSpreadRadius, 0f);
            var unit = Instantiate(plan.enemyPrefab, pos, Quaternion.identity);
            unit.name = $"{plan.enemyPrefab.name}_{parent.name}_{i + 1}";
            unit.transform.SetParent(parent, true);
            spawned.Add(unit);
        }
    }

    private void SpawnFromEntries(TeamSpawnPlan plan, Transform parent, List<GameObject> spawned)
    {
        int globalIndex = 0;
        foreach (var entry in plan.spawnEntries)
        {
            if (entry == null || entry.prefab == null) continue;
            int count = Mathf.Max(0, entry.count);

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = GetSpawnPosition(plan, plan.spawnPoint.position, plan.spawnSpreadRadius, entry.yOffset);
                var unit = Instantiate(entry.prefab, pos, Quaternion.identity);
                globalIndex++;
                unit.name = $"{entry.prefab.name}_{parent.name}_{globalIndex}";
                unit.transform.SetParent(parent, true);
                spawned.Add(unit);
            }
        }
    }

    private Vector3 GetSpawnPosition(TeamSpawnPlan plan, Vector3 origin, float spreadRadius, float yOffset)
    {
        Vector3 pos = origin + UnityEngine.Random.insideUnitSphere * spreadRadius;
        pos.y = origin.y + yOffset;

        if (plan.snapToNavMesh)
        {
            Vector3 sample = pos;
            sample.y = origin.y;
            if (NavMesh.SamplePosition(sample, out var hit, plan.navMeshSampleRadius, NavMesh.AllAreas))
            {
                pos.x = hit.position.x;
                pos.z = hit.position.z;
                pos.y = hit.position.y + yOffset;
            }
        }

        return pos;
    }

    private static bool HasMonoByName(GameObject go, string typeName)
    {
        var comps = go.GetComponents<MonoBehaviour>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (c.GetType().Name == typeName) return true;
        }
        return false;
    }

    private class PlannedTargetFeeder : MonoBehaviour
    {
        public MoveTargetMode mode = MoveTargetMode.Player;
        public string playerTag = "Player";
        public Transform targetTransform;
        public Vector3 fixedWorldPosition;

        public float interval = 0.25f;
        public bool updateContinuously = true;

        private EncounterTeamAnchor _anchor;
        private Transform _player;
        private float _next;

        private void Awake() => _anchor = GetComponent<EncounterTeamAnchor>();

        public void SetOnceNow()
        {
            if (_anchor == null) return;
            var pos = Resolve();
            if (pos.HasValue) _anchor.SetMoveTarget(pos.Value);
        }

        private void Update()
        {
            if (!updateContinuously) return;
            if (_anchor == null) return;
            if (Time.time < _next) return;
            _next = Time.time + interval;

            var pos = Resolve();
            if (pos.HasValue) _anchor.SetMoveTarget(pos.Value);
        }

        private Vector3? Resolve()
        {
            switch (mode)
            {
                case MoveTargetMode.Player:
                    if (_player == null)
                    {
                        var go = GameObject.FindGameObjectWithTag(playerTag);
                        if (go != null) _player = go.transform;
                    }
                    return _player != null ? _player.position : (Vector3?)null;

                case MoveTargetMode.Transform:
                    return targetTransform != null ? targetTransform.position : (Vector3?)null;

                case MoveTargetMode.FixedPosition:
                    return fixedWorldPosition;

                default:
                    return null;
            }
        }
    }

    public enum MoveTargetMode { None = 0, Player = 1, Transform = 2, FixedPosition = 3 }

    [Serializable]
    public class SpawnedTeamRuntimeInfo
    {
        public int teamIndex = -1;
        public TeamSpawnPlan plan;
        public Transform teamRoot;
        public List<GameObject> spawnedUnits = new List<GameObject>();
        public EncounterTeamAnchor anchor;
    }

    [Serializable]
    public class AllySpawnPlan
    {
        [Header("Identity")]
        [Tooltip("Team index used when registering with TeamManager (like EncounterDirectorPOC). Use 1,2,3...")]
        public int teamIndex = 1;
        public string teamNamePrefix = "AllyTeam_";
        public string teamName = "";

        [Header("Spawn")]
        public Transform spawnPoint;
        public GameObject allyPrefab;
        [Min(0)] public int allyCount = 4;
        [Tooltip("Random spawn radius around the spawnPoint.")]
        public float spawnRadius = 3f;
        public float yOffset = 0f;

        [Header("NavMesh")]
        public bool snapToNavMesh = true;
        public float navMeshSampleDistance = 6f;

        [Header("Parenting")]
        public bool parentUnderTeamRoot = true;

        [Header("Anchor Smoothing (UI)")]
        public float anchorSmoothSpeed = 10f;

        [HideInInspector] public int spawnSequence = 0;
    }

    [Serializable]
    public class TeamSpawnPlan
    {
        [Header("Team Identity")]
        public string teamName = "";
        public string teamNamePrefix = "EnemyTeam_";

        [Header("Hover Hint")]
        [Tooltip("Title shown when hovering this enemy team icon.")]
        public string hoverHintTitle = "Enemy Team";

        [Tooltip("Optional manual strength grade shown under the title, e.g. A+, B, C-, D-.")]
        public string strengthGrade = "";

        [HideInInspector] public int spawnSequence = 0;

        [Header("Spawn Location")]
        public Transform spawnPoint;
        public float spawnSpreadRadius = 6f;

        [Header("NavMesh Placement (spawn)")]
        public bool snapToNavMesh = true;
        public float navMeshSampleRadius = 6f;

        [Header("Anchor Smoothing (UI Icon)")]
        public float anchorSmoothSpeed = 10f;


        [Header("Enemy Team Icon Size (Enemy Only)")]
        [Tooltip("If true, forces the enemy team icon/star to a fixed scale (does not affect ally icons).")]
        public bool useFixedEnemyIconScale = false;

        [Tooltip("Fixed scale to use when useFixedEnemyIconScale is true.")]
        public float fixedEnemyIconScale = 1f;

        [Tooltip("Multiplies the enemy icon scale (works with fixed or size-based scaling).")]
        public float enemyIconScaleMultiplier = 1f;

        public bool debugEnemyIconScale = false;

        [Header("Objective Target")]
        public MoveTargetMode moveTargetMode = MoveTargetMode.Transform;
        public string playerTag = "Player";
        public Transform targetTransform;
        public Vector3 fixedWorldPosition;

        public float updatePlannedTargetEvery = 0.25f;
        public bool updatePlannedTargetContinuously = true;

        [Header("Spawn (Legacy)")]
        public GameObject enemyPrefab;
        [Min(1)] public int enemyCount = 5;

        [Header("Spawn Entries")]
        public List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

        [Header("March Settings")]
        public float objectiveArriveDistance = 3.5f;
        public float objectiveMaxSeconds = 180f;
        public float objectiveUpdateInterval = 0.25f;

        public float aggroHoldSeconds = 2f;
        public bool debugMarch = false;

        [Header("Enable Overrides")]
        public bool enableEnemy2HardMarch = true;
        public bool disableEnemy2TeamLeashWhileMarching = true;

        public bool enableMeleeMarch = true;
        public bool enableDroneMarch = true;

        public bool enableFallbackNavAgentMarch = true;
    }

    [Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Min(0)] public int count = 1;
        public float yOffset = 0f;
    }
    [Serializable]
    private class TeamIconRuntimeData
    {
        public Transform teamRoot;
        public EncounterTeamAnchor anchor;
        public RectTransform iconRoot;
        public string teamRootName;

        // Cached UI sub-refs (optional)
        public RectTransform starRect;
        public RectTransform arrowRect;
        public RectTransform flankTextRect;
        public MonoBehaviour arrowUi;

        // Cached sizing
        public Vector2 baseStarSize;
        public Vector2 baseArrowSize;
        public Vector3 baseFlankTextLocalScale;
        public Vector2 baseFlankTextAnchoredPosition;
        public bool baseFlankTextScaleCaptured;
        public bool baseFlankTextPositionCaptured;
        public bool baseSizesCaptured;

        // Cached arrow orbit radius (EnemyTeamDirectionArrowUI.orbitRadiusPixels)
        public float baseOrbitRadiusPixels;
        public bool baseOrbitRadiusCaptured;

        // Member tracking (optional)
        public List<Transform> members;

        // Cached count label refs (Legacy Text or TMP)
        public UnityEngine.UI.Text legacyCountText;
        public Component tmpCountText;
    }

    private void EnsureIconForTeam(Transform teamRoot, EncounterTeamAnchor anchor, TeamSpawnPlan plan)
    {
        if (!teamRoot) return;

        // If user hasn't wired UI refs, just skip silently (LevelOne can still run without icons).
        if (!enemyTeamIconPrefab || !enemyTeamIconsParent || !commandCamera)
            return;

        if (_iconsByTeamRoot.TryGetValue(teamRoot, out var data) && data != null && data.iconRoot)
            return;

        var icon = Instantiate(enemyTeamIconPrefab, enemyTeamIconsParent);
        icon.name = $"EnemyTeamIcon_{teamRoot.name}";
        icon.localScale = new Vector3(iconRootLocalScale, iconRootLocalScale, 1f);

        if (iconRootSizePixels > 0f)
            icon.sizeDelta = new Vector2(iconRootSizePixels, iconRootSizePixels);

        data = new TeamIconRuntimeData
        {
            teamRoot = teamRoot,
            anchor = anchor,
            iconRoot = icon,
            teamRootName = teamRoot.name
        };

        // Try to find expected children
        data.starRect = icon.transform.Find("StarImage")?.GetComponent<RectTransform>()
                        ?? icon.transform.Find("OrbitalAnchor/StarImage")?.GetComponent<RectTransform>();

        data.arrowRect = icon.transform.Find("OrbitalAnchor/ArrowImage")?.GetComponent<RectTransform>();
        data.flankTextRect = icon.transform.Find("FlankText")?.GetComponent<RectTransform>();
        if (!data.flankTextRect)
        {
            foreach (var rt in icon.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt != null && string.Equals(rt.name, "FlankText", StringComparison.Ordinal))
                {
                    data.flankTextRect = rt;
                    break;
                }
            }
        }

        // Cache base sizes so scaling is stable
        if (data.starRect)
        {
            data.baseStarSize = data.starRect.sizeDelta;
            data.baseSizesCaptured = true;
        }
        if (data.arrowRect)
        {
            data.baseArrowSize = data.arrowRect.sizeDelta;
            data.baseSizesCaptured = true;
        }
        if (data.flankTextRect)
        {
            data.baseFlankTextLocalScale = data.flankTextRect.localScale;
            data.baseFlankTextAnchoredPosition = data.flankTextRect.anchoredPosition;
            data.baseFlankTextScaleCaptured = true;
            data.baseFlankTextPositionCaptured = true;
        }

        // Bind arrow UI script if present
        var arrowUi = icon.GetComponentInChildren<MonoBehaviour>(includeInactive: true);
        // Better: specifically look by type name to avoid grabbing random MonoBehaviours
        foreach (var mb in icon.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.GetType().Name == "EnemyTeamDirectionArrowUI")
            {
                arrowUi = mb;
                break;
            }
        }
        data.arrowUi = arrowUi;

        if (data.arrowUi != null)
            BindArrowUi(data.arrowUi, anchor);

        // Bind the click/targeting bridge (so clicking the enemy team star behaves like EncounterPOC).
        BindEnemyIconTargetingBridge(icon, anchor, teamRoot, plan);

        // Cache base orbit radius so scaling stays consistent
        if (data.arrowUi != null)
            TryCaptureArrowOrbitRadius(data);

        // Apply initial scale based on plan
        ApplyPlanScaleToIcon(data, plan);

        _iconsByTeamRoot[teamRoot] = data;
    }

    private void UpdateEnemyTeamIcons()
    {
        if (_iconsByTeamRoot.Count == 0) return;
        if (!enemyTeamIconsParent || !commandCamera) return;

        bool show = true;
        if (onlyShowWhenCommandCameraEnabled)
            show = commandCamera != null && commandCamera.enabled && commandCamera.gameObject.activeInHierarchy;

        // We need the parent canvas for proper conversion
        Canvas canvas = enemyTeamIconsParent.GetComponentInParent<Canvas>();
        Camera uiCam = null;
        if (canvas != null)
        {
            // For Screen Space - Camera / World Space
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCam = canvas.worldCamera != null ? canvas.worldCamera : commandCamera;
        }

        // Cleanup dead keys
        var dead = ListPool<Transform>.Get();
        foreach (var kvp in _iconsByTeamRoot)
        {
            if (kvp.Key == null || kvp.Value == null || kvp.Value.iconRoot == null)
                dead.Add(kvp.Key);
        }
        for (int i = 0; i < dead.Count; i++)
            _iconsByTeamRoot.Remove(dead[i]);
        ListPool<Transform>.Release(dead);

        // Teams that have no live enemies: remove + destroy icon
        var emptyTeams = ListPool<Transform>.Get();

        foreach (var kvp in _iconsByTeamRoot)
        {
            var data = kvp.Value;
            if (data == null || !data.iconRoot) continue;

            data.iconRoot.gameObject.SetActive(show);
            if (!show) continue;

            Vector3 worldPos = ResolveEnemyTeamAnchorWorld(data) + iconWorldOffset;
            Vector3 screen = commandCamera.WorldToScreenPoint(worldPos);

            // Behind camera
            if (screen.z < 0.01f)
            {
                data.iconRoot.gameObject.SetActive(false);
                continue;
            }

            screen.x += iconScreenOffsetPixels.x;
            screen.y += iconScreenOffsetPixels.y;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(enemyTeamIconsParent, screen, uiCam, out var local))
                data.iconRoot.anchoredPosition = local;

            // Update enemy team member count label (best effort: uses childCount because enemies are typically parented under the team root).
            int live = GetLiveEnemyCount(data.teamRoot);
            SetIconCount(data, live);

            // If the team is wiped, destroy its UI (star+arrow) and stop tracking it.
            if (live <= 0)
            {
                DestroyEnemyTeamIconUI(data);
                emptyTeams.Add(kvp.Key);
                continue;
            }

            // Keep arrow UI bound (some scripts clear refs on enable/disable)
            if (data.arrowUi != null && data.anchor != null)
                BindArrowUi(data.arrowUi, data.anchor);
        }

        // Remove any wiped teams after iteration
        for (int i = 0; i < emptyTeams.Count; i++)
        {
            _iconsByTeamRoot.Remove(emptyTeams[i]);
        }
        ListPool<Transform>.Release(emptyTeams);
    }


    private void DestroyEnemyTeamIconUI(TeamIconRuntimeData data)
    {
        if (data == null) return;

        // Destroy the whole icon root (contains StarImage + ArrowImage + any orbit/bridge scripts).
        if (data.iconRoot != null)
            Destroy(data.iconRoot.gameObject);

        data.iconRoot = null;
        data.starRect = null;
        data.arrowRect = null;
        data.arrowUi = null;
        data.anchor = null;
        data.teamRoot = null;
    }

    // =========================
    // Ally Teams \(LevelOne\)
    // =========================

    public void SpawnAllyTeam(int allyTeamIndex)
    {
        if (allyTeams == null || allyTeamIndex < 0 || allyTeamIndex >= allyTeams.Count)
        {
            Debug.LogError($"[LevelOne] Invalid ally team index {allyTeamIndex}.", this);
            return;
        }

        AllySpawnPlan plan = allyTeams[allyTeamIndex];
        if (plan == null)
        {
            Debug.LogError($"[LevelOne] Ally plan at index {allyTeamIndex} is null.", this);
            return;
        }

        if (plan.spawnPoint == null)
        {
            Debug.LogError($"[LevelOne] Ally plan {allyTeamIndex} spawnPoint is not assigned.", this);
            return;
        }

        if (plan.allyPrefab == null)
        {
            Debug.LogError($"[LevelOne] Ally plan {allyTeamIndex} allyPrefab is not assigned.", this);
            return;
        }

        plan.spawnSequence++;

        string safePrefix = string.IsNullOrWhiteSpace(plan.teamNamePrefix) ? "AllyTeam_" : plan.teamNamePrefix;
        string baseName = string.IsNullOrWhiteSpace(plan.teamName)
            ? $"{safePrefix}{allyTeamIndex + 1}_{plan.spawnSequence}"
            : $"{plan.teamName}_{plan.spawnSequence}";

        string teamRootName = $"{baseName}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

        var teamRootGO = new GameObject(teamRootName);
        teamRootGO.transform.position = plan.spawnPoint.position;

        // UI centroid anchor
        var anchor = teamRootGO.AddComponent<EncounterTeamAnchor>();
        anchor.faction = EncounterDirectorPOC.Faction.Ally;
        anchor.updateContinuously = true;
        anchor.smooth = true;
        anchor.smoothSpeed = plan.anchorSmoothSpeed;
        anchor.driveTransformPosition = false;

        EnsureAllyIconForTeam(teamRootGO.transform, anchor);

        SpawnAllies(plan, teamRootGO.transform);
    }

    private void SpawnAllies(AllySpawnPlan plan, Transform teamRoot)
    {
        int count = Mathf.Max(0, plan.allyCount);
        if (count <= 0) return;

        Vector3 center = plan.spawnPoint.position;
        float radius = Mathf.Max(0f, plan.spawnRadius);
        float sampleDist = Mathf.Max(0.5f, plan.navMeshSampleDistance);

        for (int i = 0; i < count; i++)
        {
            Vector2 circle = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 pos = center + new Vector3(circle.x, 0f, circle.y);
            pos.y += plan.yOffset;

            if (plan.snapToNavMesh)
            {
                if (NavMesh.SamplePosition(pos, out var hit, sampleDist, NavMesh.AllAreas))
                    pos = hit.position + Vector3.up * plan.yOffset;
            }

            var go = Instantiate(plan.allyPrefab, pos, Quaternion.identity);
            if (plan.parentUnderTeamRoot && teamRoot != null)
                go.transform.SetParent(teamRoot, true);

            // Track members so the ally team icon count is correct.
            RegisterAllyMember(teamRoot, go.transform);

            // Register spawned ALLY members into TeamManager so your existing ally team star/icon + command UI works.
            if (registerAllyTeamsWithTeamManager)
                RegisterSpawnedAllyTeamMember(plan.teamIndex, go.transform);
        }
    }

    private void EnsureAllyIconForTeam(Transform teamRoot, EncounterTeamAnchor anchor)
    {
        if (!teamRoot) return;

        // If we're registering allies with TeamManager, let the existing TeamManager UI/icon system handle ally team stars.
        // This keeps clicks/commands behaving exactly like your EncounterDirectorPOC setup.
        if (registerAllyTeamsWithTeamManager && TeamManager.Instance != null)
            return;

        // If user hasn't wired ally UI refs, just skip silently.
        if (!allyTeamIconPrefab || !allyTeamIconsParent)
            return;

        Camera cam = allyIconCameraOverride != null ? allyIconCameraOverride : commandCamera;
        if (!cam) return;

        if (_allyIconsByTeamRoot.TryGetValue(teamRoot, out var data) && data != null && data.iconRoot)
            return;

        var icon = Instantiate(allyTeamIconPrefab, allyTeamIconsParent);
        icon.name = $"AllyTeamIcon_{teamRoot.name}";
        icon.localScale = new Vector3(iconRootLocalScale * allyIconScaleMultiplier, iconRootLocalScale * allyIconScaleMultiplier, 1f);

        if (iconRootSizePixels > 0f)
            icon.sizeDelta = new Vector2(iconRootSizePixels, iconRootSizePixels);

        data = new TeamIconRuntimeData
        {
            teamRoot = teamRoot,
            anchor = anchor,
            iconRoot = icon,
            teamRootName = teamRoot.name
        };

        _allyIconsByTeamRoot[teamRoot] = data;

        // Initialize member list + update label immediately (count may update again each frame).
        data.members = new List<Transform>(32);
        SetIconCount(data, GetLiveAllyCount(teamRoot));
    }

    private void UpdateAllyTeamIcons()
    {
        if (_allyIconsByTeamRoot.Count == 0) return;
        if (!allyTeamIconsParent) return;

        Camera cam = allyIconCameraOverride != null ? allyIconCameraOverride : commandCamera;
        if (!cam) return;

        bool show = true;
        if (onlyShowAllyIconsWhenCommandCameraEnabled)
            show = cam.enabled && cam.gameObject.activeInHierarchy;

        Canvas canvas = allyTeamIconsParent.GetComponentInParent<Canvas>();
        Camera uiCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = canvas.worldCamera != null ? canvas.worldCamera : cam;

        // Cleanup dead keys
        var dead = ListPool<Transform>.Get();
        foreach (var kvp in _allyIconsByTeamRoot)
        {
            if (kvp.Key == null || kvp.Value == null || kvp.Value.iconRoot == null)
                dead.Add(kvp.Key);
        }
        for (int i = 0; i < dead.Count; i++)
            _allyIconsByTeamRoot.Remove(dead[i]);
        ListPool<Transform>.Release(dead);

        foreach (var kvp in _allyIconsByTeamRoot)
        {
            var data = kvp.Value;
            if (data == null || !data.iconRoot) continue;

            data.iconRoot.gameObject.SetActive(show);
            if (!show) continue;

            Vector3 worldPos = ResolveAllyTeamAnchorWorld(data) + allyIconWorldOffset;
            Vector3 screen = cam.WorldToScreenPoint(worldPos);

            if (screen.z < 0.01f)
            {
                data.iconRoot.gameObject.SetActive(false);
                continue;
            }

            screen.x += allyIconScreenOffsetPixels.x;
            screen.y += allyIconScreenOffsetPixels.y;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(allyTeamIconsParent, screen, uiCam, out var local))
                data.iconRoot.anchoredPosition = local;

            // Update ally team member count label
            SetIconCount(data, GetLiveAllyCount(data.teamRoot));
        }
    }





    // -------------------------
    // Team anchor world position
    // -------------------------
    // LevelOne originally projected icons from teamRoot.position, but teamRoot is a static spawn anchor.
    // To make the team star follow the team as they move/fight, we resolve a live centroid each frame.

    private Vector3 ResolveEnemyTeamAnchorWorld(TeamIconRuntimeData data)
    {
        if (data == null) return Vector3.zero;

        // 1) Prefer centroid from live child members (enemies are parented under the enemy team root in LevelOne spawns).
        if (data.teamRoot != null)
        {
            var centroid = ComputeCentroidFromChildren(data.teamRoot);
            if (centroid.HasValue) return centroid.Value;
        }

        // 2) Fallback: if EncounterTeamAnchor exposes a world position, try to read it (best effort, no hard dependency).
        if (data.anchor != null)
        {
            if (TryGetVector3FromAnchor(data.anchor, out var v)) return v;
        }

        // 3) Ultimate fallback: teamRoot position.
        return data.teamRoot != null ? data.teamRoot.position : Vector3.zero;
    }

    private Vector3 ResolveAllyTeamAnchorWorld(TeamIconRuntimeData data)
    {
        if (data == null) return Vector3.zero;

        // 1) Prefer tracked member list (works even if allies are NOT parented under teamRoot).
        if (data.teamRoot != null && _allyMembersByTeamRoot.TryGetValue(data.teamRoot, out var list) && list != null && list.Count > 0)
        {
            var centroid = ComputeCentroidFromList(list);
            if (centroid.HasValue) return centroid.Value;
        }

        // 2) If they are parented, use children.
        if (data.teamRoot != null)
        {
            var centroid = ComputeCentroidFromChildren(data.teamRoot);
            if (centroid.HasValue) return centroid.Value;
        }

        // 3) Try anchor reflection
        if (data.anchor != null)
        {
            if (TryGetVector3FromAnchor(data.anchor, out var v)) return v;
        }

        return data.teamRoot != null ? data.teamRoot.position : Vector3.zero;
    }

    private Vector3? ComputeCentroidFromChildren(Transform root)
    {
        if (root == null) return null;

        Vector3 sum = Vector3.zero;
        int alive = 0;

        // Include only active children (dead/disabled units are ignored).
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c == null) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            sum += c.position;
            alive++;
        }

        if (alive <= 0) return null;
        return sum / alive;
    }

    private Vector3? ComputeCentroidFromList(List<Transform> list)
    {
        if (list == null) return null;

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
            if (!t.gameObject.activeInHierarchy) continue;
            sum += t.position;
            alive++;
        }

        if (alive <= 0) return null;
        return sum / alive;
    }

    private bool TryGetVector3FromAnchor(Component anchor, out Vector3 value)
    {
        value = Vector3.zero;
        if (anchor == null) return false;

        // Common field/property names we might have in EncounterTeamAnchor across iterations.
        // We do this via reflection to avoid hard dependencies on the exact API.
        string[] names =
        {
            "WorldPosition",
            "worldPosition",
            "SmoothedWorldPosition",
            "smoothedWorldPosition",
            "AnchorWorld",
            "anchorWorld",
            "Centroid",
            "centroid",
            "CurrentWorld",
            "currentWorld"
        };

        var t = anchor.GetType();
        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];

            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(Vector3))
            {
                try { value = (Vector3)f.GetValue(anchor); return true; } catch { }
            }

            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead && p.PropertyType == typeof(Vector3))
            {
                try { value = (Vector3)p.GetValue(anchor, null); return true; } catch { }
            }
        }

        return false;
    }


    // --- Ally icon count helpers ---
    private void RegisterAllyMember(Transform teamRoot, Transform member)
    {
        if (!teamRoot || !member) return;
        if (!_allyMembersByTeamRoot.TryGetValue(teamRoot, out var list) || list == null)
        {
            list = new List<Transform>(32);
            _allyMembersByTeamRoot[teamRoot] = list;
        }
        list.Add(member);
    }

    // Mirror EncounterDirectorPOC behavior: register spawned ally teams into TeamManager.
    // First spawned member becomes leader; when we have at least two, we create a Team.
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
                // Leader-based anchor like POC.
                team.Anchor = leader;
                _allyTeams[teamIndex] = team;
            }
            return;
        }

        // Add additional members WITHOUT triggering TeamManager's auto-formation.
        team.Add(member);
        team.Anchor = leader;
    }

    private int GetLiveAllyCount(Transform teamRoot)
    {
        if (!teamRoot) return 0;
        if (!_allyMembersByTeamRoot.TryGetValue(teamRoot, out var list) || list == null)
        {
            // Fallback: if they ARE parented, childCount is a decent proxy.
            return teamRoot.childCount;
        }

        int live = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            if (t == null)
            {
                list.RemoveAt(i);
                continue;
            }
            if (t.gameObject.activeInHierarchy) live++;
        }
        return live;
    }

    private int GetLiveEnemyCount(Transform teamRoot)
    {
        if (!teamRoot) return 0;
        // In LevelOne, enemies are typically parented under the enemy team root. Use childCount as a simple proxy.
        // If you later stop parenting enemies under the root, we can switch this to a tracked list like allies.
        int live = 0;
        for (int i = 0; i < teamRoot.childCount; i++)
        {
            var c = teamRoot.GetChild(i);
            if (c != null && c.gameObject.activeInHierarchy) live++;
        }
        return live;
    }

    private void CacheCountLabelRefs(TeamIconRuntimeData data)
    {
        if (data == null || data.iconRoot == null) return;

        // Legacy UI.Text
        if (data.legacyCountText == null)
            data.legacyCountText = data.iconRoot.GetComponentInChildren<UnityEngine.UI.Text>(true);

        // TMP_Text via reflection (avoid hard dependency)
        if (data.tmpCountText == null)
        {
            var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var c = data.iconRoot.GetComponentInChildren(tmpType, true);
                if (c != null) data.tmpCountText = c;
            }
        }
    }

    private void SetIconCount(TeamIconRuntimeData data, int count)
    {
        if (data == null) return;

        CacheCountLabelRefs(data);

        if (data.legacyCountText != null)
            data.legacyCountText.text = count.ToString();

        if (data.tmpCountText != null)
        {
            try
            {
                var p = data.tmpCountText.GetType().GetProperty("text");
                if (p != null && p.CanWrite) p.SetValue(data.tmpCountText, count.ToString(), null);
            }
            catch { /* ignore */ }
        }
    }

    private void BindArrowUi(MonoBehaviour arrowUi, EncounterTeamAnchor anchor)
    {
        if (arrowUi == null) return;

        var t = arrowUi.GetType();

        // Common field/property names seen in your project variants
        TrySetFieldOrProperty(t, arrowUi, "worldCamera", commandCamera);
        TrySetFieldOrProperty(t, arrowUi, "WorldCamera", commandCamera);

        TrySetFieldOrProperty(t, arrowUi, "teamAnchor", anchor);
        TrySetFieldOrProperty(t, arrowUi, "TeamAnchor", anchor);

        // Some builds use a move target provider component; LevelOne uses EncounterTeamAnchor.SetMoveTarget
        // so leaving MoveTargetProvider null is OK (velocity fallback / direction may still work).
    }

    private string BuildEnemyTeamHoverHint(TeamSpawnPlan plan)
    {
        string title = (plan != null && !string.IsNullOrWhiteSpace(plan.hoverHintTitle))
            ? plan.hoverHintTitle.Trim()
            : "Enemy Team";

        string grade = (plan != null && !string.IsNullOrWhiteSpace(plan.strengthGrade))
            ? plan.strengthGrade.Trim()
            : string.Empty;

        return string.IsNullOrWhiteSpace(grade)
            ? title
            : $"{title}\nStrength: {grade}";
    }

    private void BindEnemyIconTargetingBridge(RectTransform iconRoot, EncounterTeamAnchor anchor, Transform teamRoot, TeamSpawnPlan plan)
    {
        if (iconRoot == null) return;

        MonoBehaviour bridge = null;
        foreach (var mb in iconRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.GetType().Name == "EnemyTeamIconTargetingBridge")
            {
                bridge = mb;
                break;
            }
        }

        if (bridge == null) return;

        var sm = ResolveCommandStateMachine();
        var t = bridge.GetType();

        // Common reference names across your iterations
        TrySetFieldOrProperty(t, bridge, "commandStateMachine", sm);
        TrySetFieldOrProperty(t, bridge, "CommandStateMachine", sm);
        TrySetFieldOrProperty(t, bridge, "stateMachine", sm);

        // Some builds of the bridge expose only a Transform field named EnemyTeamAnchor
        // (shown in your inspector as "Enemy Team Anchor"). Support those names too.
        TrySetFieldOrProperty(t, bridge, "enemyTeamAnchor", anchor != null ? anchor.transform : null);
        TrySetFieldOrProperty(t, bridge, "EnemyTeamAnchor", anchor != null ? anchor.transform : null);

        TrySetFieldOrProperty(t, bridge, "teamAnchor", anchor);
        TrySetFieldOrProperty(t, bridge, "TeamAnchor", anchor);
        TrySetFieldOrProperty(t, bridge, "anchor", anchor);
        TrySetFieldOrProperty(t, bridge, "Anchor", anchor);

        TrySetFieldOrProperty(t, bridge, "teamRoot", teamRoot);
        TrySetFieldOrProperty(t, bridge, "TeamRoot", teamRoot);
        TrySetFieldOrProperty(t, bridge, "teamTransform", teamRoot);
        TrySetFieldOrProperty(t, bridge, "TeamTransform", teamRoot);

        string hoverHint = BuildEnemyTeamHoverHint(plan);
        TrySetFieldOrProperty(t, bridge, "hoverHintMessage", hoverHint);
        TrySetFieldOrProperty(t, bridge, "HoverHintMessage", hoverHint);
        TrySetFieldOrProperty(t, bridge, "message", hoverHint);
        TrySetFieldOrProperty(t, bridge, "Message", hoverHint);

        // If the bridge needs a faction flag
        TrySetFieldOrProperty(t, bridge, "faction", EncounterDirectorPOC.Faction.Enemy);
        TrySetFieldOrProperty(t, bridge, "Faction", EncounterDirectorPOC.Faction.Enemy);
    }

    private void ApplyPlanScaleToIcon(TeamIconRuntimeData data, TeamSpawnPlan plan)
    {
        if (data == null || data.iconRoot == null || plan == null) return;

        float s = 1f;
        if (plan.useFixedEnemyIconScale)
            s = Mathf.Max(0.01f, plan.fixedEnemyIconScale);

        s *= Mathf.Max(0.01f, plan.enemyIconScaleMultiplier);

        // Keep arrow orbit distance consistent
        TryApplyArrowOrbitRadius(data, s);

        // Prefer driving StarImage/ArrowImage via sizeDelta so it matches your UI expectations
        if (data.starRect)
        {
            if (!data.baseSizesCaptured || data.baseStarSize.sqrMagnitude < 0.001f)
                data.baseStarSize = data.starRect.sizeDelta;

            if (data.baseStarSize.sqrMagnitude > 0.001f)
                data.starRect.sizeDelta = data.baseStarSize * s;
            else
                data.starRect.localScale = new Vector3(s, s, 1f);
        }
        else
        {
            // Fallback: scale the icon root
            data.iconRoot.localScale = new Vector3(iconRootLocalScale * s, iconRootLocalScale * s, 1f);
        }

        if (data.arrowRect)
        {
            if (!data.baseSizesCaptured || data.baseArrowSize.sqrMagnitude < 0.001f)
                data.baseArrowSize = data.arrowRect.sizeDelta;

            if (scaleArrowWithTeam)
            {
                if (data.baseArrowSize.sqrMagnitude > 0.001f)
                    data.arrowRect.sizeDelta = data.baseArrowSize * s;
            }
        }

        if (data.flankTextRect)
        {
            if (!data.baseFlankTextScaleCaptured)
            {
                data.baseFlankTextLocalScale = data.flankTextRect.localScale;
                data.baseFlankTextScaleCaptured = true;
            }

            if (!data.baseFlankTextPositionCaptured)
            {
                data.baseFlankTextAnchoredPosition = data.flankTextRect.anchoredPosition;
                data.baseFlankTextPositionCaptured = true;
            }

            Vector3 baseScale = data.baseFlankTextScaleCaptured ? data.baseFlankTextLocalScale : Vector3.one;
            Vector2 basePos = data.baseFlankTextPositionCaptured ? data.baseFlankTextAnchoredPosition : data.flankTextRect.anchoredPosition;
            data.flankTextRect.localScale = new Vector3(baseScale.x * s, baseScale.y * s, baseScale.z);
            data.flankTextRect.anchoredPosition = basePos * s;
        }
    }


    private void TryCaptureArrowOrbitRadius(TeamIconRuntimeData data)
    {
        if (data == null || data.arrowUi == null) return;
        if (data.baseOrbitRadiusCaptured) return;

        try
        {
            var t = data.arrowUi.GetType();

            // Most common: field named orbitRadiusPixels
            var f = t.GetField("orbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(float))
            {
                data.baseOrbitRadiusPixels = (float)f.GetValue(data.arrowUi);
                data.baseOrbitRadiusCaptured = true;
                return;
            }

            // Fallback: property OrbitRadiusPixels / orbitRadiusPixels
            var p = t.GetProperty("orbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? t.GetProperty("OrbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p != null && p.PropertyType == typeof(float) && p.CanRead)
            {
                data.baseOrbitRadiusPixels = (float)p.GetValue(data.arrowUi, null);
                data.baseOrbitRadiusCaptured = true;
                return;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void TryApplyArrowOrbitRadius(TeamIconRuntimeData data, float scale)
    {
        if (!scaleArrowOrbitRadiusWithTeam) return;
        if (data == null || data.arrowUi == null) return;

        if (!data.baseOrbitRadiusCaptured)
            TryCaptureArrowOrbitRadius(data);

        if (!data.baseOrbitRadiusCaptured) return;

        float s = Mathf.Max(0.01f, scale);
        float m = Mathf.Max(0.01f, arrowOrbitRadiusMultiplier);
        float r = data.baseOrbitRadiusPixels * s * m;

        try
        {
            var t = data.arrowUi.GetType();

            var f = t.GetField("orbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(float))
            {
                f.SetValue(data.arrowUi, r);
                return;
            }

            var p = t.GetProperty("orbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? t.GetProperty("OrbitRadiusPixels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p != null && p.PropertyType == typeof(float) && p.CanWrite)
            {
                p.SetValue(data.arrowUi, r, null);
                return;
            }
        }
        catch
        {
            // ignore
        }
    }


    /// <summary>
    /// Tiny list pool to avoid GC when cleaning up icon dictionary.
    /// </summary>
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> _pool = new Stack<List<T>>(8);
        public static List<T> Get()
        {
            if (_pool.Count > 0)
            {
                var l = _pool.Pop();
                l.Clear();
                return l;
            }
            return new List<T>(8);
        }

        public static void Release(List<T> list)
        {
            if (list == null) return;
            list.Clear();
            if (_pool.Count < 32) _pool.Push(list);
        }
    }

}

/// <summary>
/// HARD march override for Enemy2Controller-based prefabs.
/// Injects objective as Enemy2Controller._combatTarget and sets chasing=true while not in real combat.
/// </summary>
public class Enemy2HardMarchOverride : MonoBehaviour
{
    public LevelOne.MoveTargetMode mode = LevelOne.MoveTargetMode.Transform;
    public string playerTag = "Player";
    public Transform targetTransform;
    public Vector3 fixedWorldPosition;

    public float arriveDistance = 3.5f;
    public float maxSeconds = 180f;
    public float updateInterval = 0.25f;

    public bool disableTeamLeashWhileMarching = true;
    public bool debug = false;

    private float _start;
    private float _next;

    private NavMeshAgent _agent;
    private MonoBehaviour _enemy2;
    private Behaviour _enemy2Behaviour;
    private GameObject _proxyGO;

    private FieldInfo _fCombatTarget;
    private FieldInfo _fChasing;
    private FieldInfo _fEnableTeamLeash;

    private bool _hadLeash;
    private bool _origLeash;

    private void Awake()
    {
        _start = Time.time;
        _agent = GetComponent<NavMeshAgent>();

        _enemy2 = FindController("Enemy2Controller");
        _enemy2Behaviour = _enemy2 as Behaviour;

        if (_enemy2 != null)
        {
            CacheEnemy2Fields();

            _proxyGO = new GameObject("ObjectiveProxy");
            _proxyGO.transform.SetParent(null, worldPositionStays: true);

            if (disableTeamLeashWhileMarching && _fEnableTeamLeash != null)
            {
                _hadLeash = true;
                _origLeash = (bool)_fEnableTeamLeash.GetValue(_enemy2);
                _fEnableTeamLeash.SetValue(_enemy2, false);
            }
        }

        UpdateProxyPosition();
        ApplyObjectiveInjection(force: true);
    }

    private void OnDestroy()
    {
        if (_enemy2 != null && _hadLeash && _fEnableTeamLeash != null)
            _fEnableTeamLeash.SetValue(_enemy2, _origLeash);

        if (_proxyGO != null) Destroy(_proxyGO);
    }

    private void LateUpdate()
    {
        if (Time.time - _start >= maxSeconds)
        {
            Destroy(this);
            return;
        }

        Vector3? obj = ResolveObjective();
        if (!obj.HasValue) return;

        if (Vector3.Distance(transform.position, obj.Value) <= arriveDistance)
        {
            ClearObjectiveInjection();
            Destroy(this);
            return;
        }

        if (Time.time < _next) return;
        _next = Time.time + Mathf.Max(0.05f, updateInterval);

        UpdateProxyPosition();
        ApplyObjectiveInjection(force: false);
    }

    private void UpdateProxyPosition()
    {
        if (_proxyGO == null) return;
        Vector3? obj = ResolveObjective();
        if (obj.HasValue) _proxyGO.transform.position = obj.Value;
    }

    private Vector3? ResolveObjective()
    {
        switch (mode)
        {
            case LevelOne.MoveTargetMode.Player:
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    return go != null ? go.transform.position : (Vector3?)null;
                }
            case LevelOne.MoveTargetMode.Transform:
                return targetTransform != null ? targetTransform.position : (Vector3?)null;
            case LevelOne.MoveTargetMode.FixedPosition:
                return fixedWorldPosition;
            default:
                return null;
        }
    }

    private bool InRealCombat()
    {
        if (_enemy2 == null) return false;

        if (_fCombatTarget != null)
        {
            var ct = _fCombatTarget.GetValue(_enemy2) as Transform;
            if (ct != null && (_proxyGO == null || ct != _proxyGO.transform))
                return true;
        }

        if (_fChasing != null)
        {
            try
            {
                bool chasing = (bool)_fChasing.GetValue(_enemy2);
                if (chasing)
                {
                    if (_fCombatTarget != null)
                    {
                        var ct = _fCombatTarget.GetValue(_enemy2) as Transform;
                        if (ct != null && _proxyGO != null && ct == _proxyGO.transform)
                            return false;
                    }
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    private void ApplyObjectiveInjection(bool force)
    {
        if (_enemy2 == null || _proxyGO == null) return;
        if (_enemy2Behaviour != null && !_enemy2Behaviour.enabled) return;
        if (InRealCombat()) return;

        if (_fCombatTarget != null) _fCombatTarget.SetValue(_enemy2, _proxyGO.transform);
        if (_fChasing != null) _fChasing.SetValue(_enemy2, true);

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(_proxyGO.transform.position);
        }

        if (debug && force) Debug.Log($"[Enemy2HardMarchOverride] {name} injecting objective proxy", this);
    }

    private void ClearObjectiveInjection()
    {
        if (_enemy2 == null) return;

        if (_fCombatTarget != null)
        {
            var ct = _fCombatTarget.GetValue(_enemy2) as Transform;
            if (_proxyGO != null && ct == _proxyGO.transform)
                _fCombatTarget.SetValue(_enemy2, null);
        }

        if (_fChasing != null) _fChasing.SetValue(_enemy2, false);
    }

    private MonoBehaviour FindController(string typeName)
    {
        var comps = GetComponents<MonoBehaviour>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (c.GetType().Name == typeName) return c;
        }
        return null;
    }

    private void CacheEnemy2Fields()
    {
        Type t = _enemy2.GetType();
        _fCombatTarget = t.GetField("_combatTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        _fChasing = t.GetField("chasing", BindingFlags.Instance | BindingFlags.NonPublic);
        _fEnableTeamLeash = t.GetField("enableTeamLeash", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}

/// <summary>
/// MeleeEnemy2Controller march override:
/// - Creates an objective proxy Transform.
/// - While melee has NO real combatTarget, we call MeleeEnemy2Controller.SetCombatTarget(proxy) so it walks/runs using its own animator logic.
/// - If its combatTarget becomes something else (shot/aggro), we stop overriding.
/// - On finish, we clear the proxy if still set.
/// </summary>
public class MeleeObjectiveMarchOverride : MonoBehaviour
{
    public LevelOne.MoveTargetMode mode = LevelOne.MoveTargetMode.Transform;
    public string playerTag = "Player";
    public Transform targetTransform;
    public Vector3 fixedWorldPosition;

    public float arriveDistance = 3.5f;
    public float maxSeconds = 180f;
    public float updateInterval = 0.25f;
    public bool debug = false;

    private float _start;
    private float _next;

    private MonoBehaviour _melee;
    private Behaviour _meleeBehaviour;

    private MethodInfo _mSetCombatTarget;
    private MethodInfo _mClearCombatTarget;
    private FieldInfo _fCombatTarget;

    private GameObject _proxyGO;

    private void Awake()
    {
        _start = Time.time;

        _melee = FindController("MeleeEnemy2Controller");
        _meleeBehaviour = _melee as Behaviour;

        if (_melee != null)
        {
            CacheMeleeMembers();

            _proxyGO = new GameObject("ObjectiveProxy_Melee");
            _proxyGO.transform.SetParent(null, true);

            UpdateProxyPosition();
            ForceMeleeChaseProxy(force: true);
        }
    }

    private void OnDestroy()
    {
        if (_proxyGO != null) Destroy(_proxyGO);
    }

    private void LateUpdate()
    {
        if (_melee == null) { Destroy(this); return; }
        if (_meleeBehaviour != null && !_meleeBehaviour.enabled) return;

        if (Time.time - _start >= maxSeconds)
        {
            RestoreAndStop();
            return;
        }

        Vector3? obj = ResolveObjective();
        if (!obj.HasValue) return;

        if (Vector3.Distance(transform.position, obj.Value) <= arriveDistance)
        {
            RestoreAndStop();
            return;
        }

        if (Time.time < _next) return;
        _next = Time.time + Mathf.Max(0.05f, updateInterval);

        UpdateProxyPosition();

        // If melee has a real target (not our proxy), stop overriding
        Transform current = ReadCombatTarget();
        if (current != null && _proxyGO != null && current != _proxyGO.transform)
        {
            if (debug) Debug.Log($"[MeleeObjectiveMarchOverride] {name} real target detected -> stop marching", this);
            Destroy(this);
            return;
        }

        ForceMeleeChaseProxy(force: false);
    }

    private void ForceMeleeChaseProxy(bool force)
    {
        if (_mSetCombatTarget == null || _proxyGO == null) return;
        // If no combat target, set proxy as target
        Transform current = ReadCombatTarget();
        if (current == null || (current == _proxyGO.transform))
        {
            _mSetCombatTarget.Invoke(_melee, new object[] { _proxyGO.transform });
            if (debug && force) Debug.Log($"[MeleeObjectiveMarchOverride] {name} set combatTarget=proxy", this);
        }
    }

    private void RestoreAndStop()
    {
        // Clear combat target only if it's still our proxy
        Transform current = ReadCombatTarget();
        if (_proxyGO != null && current == _proxyGO.transform)
        {
            if (_mClearCombatTarget != null) _mClearCombatTarget.Invoke(_melee, null);
            else if (_fCombatTarget != null) _fCombatTarget.SetValue(_melee, null);
        }
        Destroy(this);
    }

    private Transform ReadCombatTarget()
    {
        if (_fCombatTarget == null || _melee == null) return null;
        try { return _fCombatTarget.GetValue(_melee) as Transform; } catch { return null; }
    }

    private void UpdateProxyPosition()
    {
        if (_proxyGO == null) return;
        Vector3? obj = ResolveObjective();
        if (obj.HasValue) _proxyGO.transform.position = obj.Value;
    }

    private Vector3? ResolveObjective()
    {
        switch (mode)
        {
            case LevelOne.MoveTargetMode.Player:
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    return go != null ? go.transform.position : (Vector3?)null;
                }
            case LevelOne.MoveTargetMode.Transform:
                return targetTransform != null ? targetTransform.position : (Vector3?)null;
            case LevelOne.MoveTargetMode.FixedPosition:
                return fixedWorldPosition;
            default:
                return null;
        }
    }

    private MonoBehaviour FindController(string typeName)
    {
        var comps = GetComponents<MonoBehaviour>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (c.GetType().Name == typeName) return c;
        }
        return null;
    }

    private void CacheMeleeMembers()
    {
        Type t = _melee.GetType();
        _mSetCombatTarget = t.GetMethod("SetCombatTarget", BindingFlags.Instance | BindingFlags.Public);
        _mClearCombatTarget = t.GetMethod("ClearCombatTarget", BindingFlags.Instance | BindingFlags.Public);
        _fCombatTarget = t.GetField("combatTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }
}

/// <summary>
/// DroneEnemyController march override:
/// - Creates an objective proxy Transform.
/// - While drone has NO combatTarget, we set its waypoints = [proxy] and state = Patrol.
/// - If drone acquires a real combatTarget (auto-acquire), we stop overriding so it can fight.
/// </summary>
public class DroneObjectiveMarchOverride : MonoBehaviour
{
    public LevelOne.MoveTargetMode mode = LevelOne.MoveTargetMode.Transform;
    public string playerTag = "Player";
    public Transform targetTransform;
    public Vector3 fixedWorldPosition;

    public float arriveDistance = 3.5f;
    public float maxSeconds = 180f;
    public float updateInterval = 0.25f;
    public bool debug = false;

    private float _start;
    private float _next;

    private MonoBehaviour _drone;
    private Behaviour _droneBehaviour;

    private FieldInfo _fCombatTarget;
    private FieldInfo _fWaypoints;
    private FieldInfo _fState; // enum
    private FieldInfo _fWpIndex;
    private FieldInfo _fWpDir;

    private GameObject _proxyGO;

    private object _origWaypoints;
    private object _origState;

    private void Awake()
    {
        _start = Time.time;

        _drone = FindController("DroneEnemyController");
        _droneBehaviour = _drone as Behaviour;

        if (_drone != null)
        {
            CacheDroneMembers();

            _proxyGO = new GameObject("ObjectiveProxy_Drone");
            _proxyGO.transform.SetParent(null, true);

            UpdateProxyPosition();
            ForceDronePatrolToProxy(force: true);
        }
    }

    private void OnDestroy()
    {
        Restore();
        if (_proxyGO != null) Destroy(_proxyGO);
    }

    private void LateUpdate()
    {
        if (_drone == null) { Destroy(this); return; }
        if (_droneBehaviour != null && !_droneBehaviour.enabled) return;

        if (Time.time - _start >= maxSeconds)
        {
            Destroy(this);
            return;
        }

        Vector3? obj = ResolveObjective();
        if (!obj.HasValue) return;

        if (Vector3.Distance(transform.position, obj.Value) <= arriveDistance)
        {
            Destroy(this);
            return;
        }

        if (Time.time < _next) return;
        _next = Time.time + Mathf.Max(0.05f, updateInterval);

        UpdateProxyPosition();

        // If drone has a real combat target, stop overriding
        Transform ct = ReadCombatTarget();
        if (ct != null)
        {
            if (debug) Debug.Log($"[DroneObjectiveMarchOverride] {name} combatTarget acquired -> stop marching", this);
            Destroy(this);
            return;
        }

        ForceDronePatrolToProxy(force: false);
    }

    private void ForceDronePatrolToProxy(bool force)
    {
        if (_proxyGO == null || _drone == null || _fWaypoints == null) return;

        // Save originals once
        if (_origWaypoints == null && _fWaypoints != null) _origWaypoints = _fWaypoints.GetValue(_drone);
        if (_origState == null && _fState != null) _origState = _fState.GetValue(_drone);

        // Set waypoint list to proxy
        _fWaypoints.SetValue(_drone, new Transform[] { _proxyGO.transform });

        // Set patrol state if field exists
        if (_fState != null)
        {
            // Try to set enum to Patrol by name
            object patrol = Enum.Parse(_fState.FieldType, "Patrol");
            _fState.SetValue(_drone, patrol);
        }

        // Reset waypoint indices
        if (_fWpIndex != null) _fWpIndex.SetValue(_drone, 0);
        if (_fWpDir != null) _fWpDir.SetValue(_drone, 1);

        if (debug && force) Debug.Log($"[DroneObjectiveMarchOverride] {name} forcing waypoints=[proxy]", this);
    }

    private void Restore()
    {
        if (_drone == null) return;
        try
        {
            if (_fWaypoints != null && _origWaypoints != null) _fWaypoints.SetValue(_drone, _origWaypoints);
            if (_fState != null && _origState != null) _fState.SetValue(_drone, _origState);
        }
        catch { }
    }

    private Transform ReadCombatTarget()
    {
        if (_fCombatTarget == null || _drone == null) return null;
        try { return _fCombatTarget.GetValue(_drone) as Transform; } catch { return null; }
    }

    private void UpdateProxyPosition()
    {
        if (_proxyGO == null) return;
        Vector3? obj = ResolveObjective();
        if (obj.HasValue) _proxyGO.transform.position = obj.Value;
    }

    private Vector3? ResolveObjective()
    {
        switch (mode)
        {
            case LevelOne.MoveTargetMode.Player:
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    return go != null ? go.transform.position : (Vector3?)null;
                }
            case LevelOne.MoveTargetMode.Transform:
                return targetTransform != null ? targetTransform.position : (Vector3?)null;
            case LevelOne.MoveTargetMode.FixedPosition:
                return fixedWorldPosition;
            default:
                return null;
        }
    }

    private MonoBehaviour FindController(string typeName)
    {
        var comps = GetComponents<MonoBehaviour>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (c.GetType().Name == typeName) return c;
        }
        return null;
    }

    private void CacheDroneMembers()
    {
        Type t = _drone.GetType();
        _fCombatTarget = t.GetField("combatTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _fWaypoints = t.GetField("waypoints", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _fState = t.GetField("state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _fWpIndex = t.GetField("_wpIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        _fWpDir = t.GetField("_wpDir", BindingFlags.Instance | BindingFlags.NonPublic);
    }
}

/// <summary>
/// Fallback NavMeshAgent march.
/// Used only for units that aren't Enemy2/Melee2/Drone controllers.
/// </summary>
public class NavAgentMarchToObjective : MonoBehaviour
{
    public LevelOne.MoveTargetMode mode = LevelOne.MoveTargetMode.Transform;
    public string playerTag = "Player";
    public Transform targetTransform;
    public Vector3 fixedWorldPosition;

    public float arriveDistance = 3.5f;
    public float maxSeconds = 180f;
    public float updateInterval = 0.25f;
    public float aggroHoldSeconds = 2f;
    public bool debug = false;

    private float _start;
    private float _next;
    private float _aggroUntil;

    private NavMeshAgent _agent;

    private void Awake()
    {
        _start = Time.time;
        _agent = GetComponent<NavMeshAgent>();
    }

    private void LateUpdate()
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) { return; }
        if (Time.time - _start >= maxSeconds) { Destroy(this); return; }

        var obj = ResolveObjective();
        if (!obj.HasValue) return;

        if (Vector3.Distance(transform.position, obj.Value) <= arriveDistance)
        {
            Destroy(this);
            return;
        }

        if (Time.time < _next) return;
        _next = Time.time + Mathf.Max(0.05f, updateInterval);

        _agent.isStopped = false;
        _agent.SetDestination(obj.Value);
    }

    private Vector3? ResolveObjective()
    {
        switch (mode)
        {
            case LevelOne.MoveTargetMode.Player:
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    return go != null ? go.transform.position : (Vector3?)null;
                }
            case LevelOne.MoveTargetMode.Transform:
                return targetTransform != null ? targetTransform.position : (Vector3?)null;
            case LevelOne.MoveTargetMode.FixedPosition:
                return fixedWorldPosition;
            default:
                return null;
        }
    }
}
