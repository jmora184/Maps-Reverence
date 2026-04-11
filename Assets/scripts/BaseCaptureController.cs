using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Hooks into BaseActivator.onActivated and turns a captured base into:
/// - immediate Ally team spawn
/// - delayed Enemy team counterattack spawn
/// - UI base icon swap (Enemy Base -> Ally Base, or any prefab you choose)
/// - active BaseRefillZone
///
/// Designed to reuse your existing LevelOne spawning + team icon + ally registration flow,
/// so spawned teams behave like your normal LevelOne teams.
/// </summary>
[DisallowMultipleComponent]
public class BaseCaptureController : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("Usually the BaseActivator on this same base object.")]
    public BaseActivator baseActivator;

    [Tooltip("Existing LevelOne in the scene. We reuse its spawn logic so ally commands + enemy team icons behave like your normal LevelOne setup.")]
    public LevelOne levelOne;

    [Tooltip("Optional refill zone to enable after the base is captured.")]
    public BaseRefillZone refillZone;

    [Header("Capture Flow")]
    [Tooltip("Enemy team waits this long after capture before spawning.")]
    public float enemySpawnDelay = 10f;

    [Tooltip("If true, the refill zone component is disabled until this base is captured.")]
    public bool disableRefillZoneUntilCaptured = true;

    [Tooltip("Log helpful debug info while wiring/testing.")]
    public bool debugLogs = false;

    [Header("Enemy Inbound Warning UI")]
    [Tooltip("Optional warning UI to show when this BaseCaptureController spawns enemy teams.")]
    public GameObject enemyInboundWarningUI;

    [Tooltip("How long to show the enemy inbound warning UI.")]
    public float enemyInboundWarningDuration = 2f;

    [Header("Base Icon Swap (MiniUI)")]
    [Tooltip("Current icon object already placed over the base in your MiniUI.")]
    public RectTransform currentBaseIcon;

    [Tooltip("Prefab to spawn when the base becomes friendly.")]
    public RectTransform capturedBaseIconPrefab;

    [Tooltip("Optional explicit parent for the replacement icon. If empty, uses the current icon's parent.")]
    public RectTransform replacementIconParent;

    [Tooltip("If true, the replacement icon copies anchored position / rotation / scale from the old icon.")]
    public bool preserveBaseIconLayout = true;

    [Tooltip("Optional UI transform to copy placement from instead of using the old enemy icon. Useful when you want to manually place the captured ally icon.")]
    public Transform capturedBaseIconPlacementTarget;

    [Tooltip("If true and a placement target is assigned, the captured icon copies that target's anchors / position / size / rotation / scale.")]
    public bool useCapturedIconPlacementTarget = false;

    [Tooltip("If true, the old icon is destroyed after replacement.")]
    public bool destroyOldBaseIcon = true;

    [Header("Save / Load")]
    [Tooltip("Optional stable save/load id for this base capture controller.")]
    public string saveId = "";

    [Header("Ally Team Spawn")]
    public AllyTeamConfig allyTeam = new AllyTeamConfig();

    [Header("Turret Ownership Switch")]
    [Tooltip("Optional helper that disables enemy turrets and enables ally turrets when this base is captured.")]
    public BaseTurretSwitcher baseTurretSwitcher;

    [Header("Enemy Team Counterattack")]
    [Tooltip("Optional legacy single counterattack team. Used only if Enemy Teams list is empty.")]
    public EnemyTeamConfig enemyTeam = new EnemyTeamConfig();

    [Tooltip("Spawn multiple enemy counterattack teams after the base is captured. If this list has entries, it is used instead of the legacy single Enemy Team field above.")]
    public List<EnemyTeamConfig> enemyTeams = new List<EnemyTeamConfig>();

    [SerializeField, Tooltip("Runtime only: true after this base has been captured once.")]
    private bool hasCaptured = false;

    public bool HasCaptured => hasCaptured;

    private UnityAction _cachedActivationListener;
    private UnityEvent _hookedActivationEvent;
    private bool _isSubscribed;
    private Coroutine _enemyInboundWarningRoutine;

    [Serializable]
    public class AllyTeamConfig
    {
        [Header("Identity")]
        [Tooltip("Used when registering spawned allies with TeamManager.")]
        public int teamIndex = 1;
        public string teamNamePrefix = "CapturedBaseAlly_";
        public string teamName = "";

        [Header("Spawn")]
        public Transform spawnPoint;
        public GameObject allyPrefab;
        [Min(0)] public int allyCount = 4;
        public float spawnRadius = 3f;
        public float yOffset = 0f;

        [Header("NavMesh")]
        public bool snapToNavMesh = true;
        public float navMeshSampleDistance = 6f;

        [Header("Parenting / UI")]
        public bool parentUnderTeamRoot = true;
        public float anchorSmoothSpeed = 10f;
    }

    [Serializable]
    public class EnemySpawnEntryData
    {
        public GameObject prefab;
        [Min(0)] public int count = 1;
        public float yOffset = 0f;
    }

    [Serializable]
    public class EnemyTeamConfig
    {
        [Header("Identity")]
        public string teamName = "";
        public string teamNamePrefix = "CapturedBaseEnemy_";

        [Header("Hover Hint")]
        [Tooltip("Hover title shown when this spawned enemy team star/icon is hovered.")]
        public string hoverHintTitle = "Enemy Team";

        [Tooltip("Optional strength grade shown under the hover title, e.g. A+, B, C-. Leave blank to show only the title.")]
        public string strengthGrade = "";

        [Header("Spawn")]
        [Tooltip("If true, this team spawns during the normal post-capture wave after enemySpawnDelay. If false, it stays dormant until a reinforcement trigger spawns it.")]
        public bool spawnAfterCaptureDelay = true;

        public Transform spawnPoint;
        public float spawnSpreadRadius = 6f;
        public bool snapToNavMesh = true;
        public float navMeshSampleRadius = 6f;
        public float anchorSmoothSpeed = 10f;

        [Header("Enemy Prefabs")]
        [Tooltip("Legacy single enemy prefab. Ignored if Spawn Entries has items.")]
        public GameObject enemyPrefab;
        [Min(1)] public int enemyCount = 5;

        [Tooltip("Optional mixed enemy list like LevelOne spawn entries.")]
        public List<EnemySpawnEntryData> spawnEntries = new List<EnemySpawnEntryData>();

        [Header("Enemy Team Star/Icon Scale")]
        public bool useFixedEnemyIconScale = false;
        public float fixedEnemyIconScale = 1f;
        public float enemyIconScaleMultiplier = 1f;
        public bool debugEnemyIconScale = false;

        [Header("Destination / Objective")]
        public LevelOne.MoveTargetMode moveTargetMode = LevelOne.MoveTargetMode.Transform;
        public string playerTag = "Player";
        public Transform targetTransform;
        public Vector3 fixedWorldPosition;
        public float updatePlannedTargetEvery = 0.25f;
        public bool updatePlannedTargetContinuously = true;

        [Header("March Settings")]
        public float objectiveArriveDistance = 3.5f;
        public float objectiveMaxSeconds = 180f;
        public float objectiveUpdateInterval = 0.25f;
        public float aggroHoldSeconds = 2f;
        public bool debugMarch = false;

        [Header("Reinforcements")]
        [Tooltip("If true, deaths from this spawned team can trigger a linked backup team.")]
        public bool enableReinforcementTrigger = false;

        [Min(1), Tooltip("How many deaths from this exact spawned team are required before its backup team spawns.")]
        public int reinforcementDeathsRequired = 3;

        [Tooltip("Index inside the Enemy Teams list of the backup team to spawn when the death threshold is reached. Use -1 to disable.")]
        public int reinforcementEnemyTeamListIndex = -1;

        [Tooltip("Optional override spawn point for the backup team. If empty, the backup team's own spawn point is used.")]
        public Transform reinforcementSpawnPointOverride;

        [Header("Enable Overrides")]
        public bool enableEnemy2HardMarch = true;
        public bool disableEnemy2TeamLeashWhileMarching = true;
        public bool enableMeleeMarch = true;
        public bool enableDroneMarch = true;
        public bool enableFallbackNavAgentMarch = true;
    }

    private void Reset()
    {
        if (baseActivator == null)
            baseActivator = GetComponent<BaseActivator>() ?? GetComponentInParent<BaseActivator>();

        if (refillZone == null)
            refillZone = GetComponentInChildren<BaseRefillZone>(true);

#if UNITY_2023_1_OR_NEWER
        if (levelOne == null)
            levelOne = UnityEngine.Object.FindFirstObjectByType<LevelOne>();
#else
        if (levelOne == null)
            levelOne = UnityEngine.Object.FindObjectOfType<LevelOne>();
#endif
    }

    private void Awake()
    {
        AutoResolveReferences();

        if (disableRefillZoneUntilCaptured && refillZone != null && !hasCaptured)
            refillZone.enabled = false;
    }

    private void OnEnable()
    {
        AutoResolveReferences();
        HookBaseActivator();
    }

    private void OnDisable()
    {
        UnhookBaseActivator();
    }

    [ContextMenu("Trigger Capture Now")]
    public void TriggerCaptureNow()
    {
        HandleBaseActivated();
    }

    public void ApplySavedCapturedStateForLoad()
    {
        if (hasCaptured)
            return;

        if (levelOne == null)
            AutoResolveReferences();

        if (levelOne == null)
        {
            Debug.LogError("[BaseCaptureController] Cannot apply saved capture state because LevelOne could not be resolved.", this);
            return;
        }

        hasCaptured = true;
        SwapBaseIcon();
        EnableRefillZone();
        RefillCurrentZoneOccupants();
        SwitchBaseTurrets();
        SpawnAllyTeamImmediate();
        RegisterEnemyCounterattackPlansOnly();
    }

    private string BuildAllyTeamSaveId()
    {
        return string.IsNullOrWhiteSpace(saveId) ? "" : $"{saveId.Trim()}__ally";
    }

    private string BuildEnemyTeamSaveId(int configIndex)
    {
        return string.IsNullOrWhiteSpace(saveId) ? "" : $"{saveId.Trim()}__enemy_{Mathf.Max(0, configIndex)}";
    }

    private void AutoResolveReferences()
    {
        if (baseActivator == null)
            baseActivator = GetComponent<BaseActivator>() ?? GetComponentInParent<BaseActivator>();

        if (refillZone == null)
            refillZone = GetComponentInChildren<BaseRefillZone>(true);

#if UNITY_2023_1_OR_NEWER
        if (levelOne == null)
            levelOne = UnityEngine.Object.FindFirstObjectByType<LevelOne>();
#else
        if (levelOne == null)
            levelOne = UnityEngine.Object.FindObjectOfType<LevelOne>();
#endif
    }

    private void HookBaseActivator()
    {
        if (_isSubscribed || baseActivator == null)
            return;

        _hookedActivationEvent = GetPrivateUnityEvent(baseActivator, "onActivated");
        if (_hookedActivationEvent == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseCaptureController] Could not hook BaseActivator.onActivated automatically. You can still call TriggerCaptureNow() from a UnityEvent if needed.", this);
            return;
        }

        if (_cachedActivationListener == null)
            _cachedActivationListener = HandleBaseActivated;

        _hookedActivationEvent.AddListener(_cachedActivationListener);
        _isSubscribed = true;

        if (debugLogs)
            Debug.Log("[BaseCaptureController] Hooked BaseActivator.onActivated successfully.", this);
    }

    private void UnhookBaseActivator()
    {
        if (!_isSubscribed || _hookedActivationEvent == null || _cachedActivationListener == null)
            return;

        _hookedActivationEvent.RemoveListener(_cachedActivationListener);
        _hookedActivationEvent = null;
        _isSubscribed = false;
    }

    private static UnityEvent GetPrivateUnityEvent(object target, string fieldName)
    {
        if (target == null || string.IsNullOrWhiteSpace(fieldName))
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = target.GetType();

        while (t != null)
        {
            var field = t.GetField(fieldName, flags);
            if (field != null && typeof(UnityEvent).IsAssignableFrom(field.FieldType))
                return field.GetValue(target) as UnityEvent;

            t = t.BaseType;
        }

        return null;
    }

    private void HandleBaseActivated()
    {
        if (hasCaptured)
            return;

        if (levelOne == null)
        {
            Debug.LogError("[BaseCaptureController] No LevelOne reference found. Assign your scene's LevelOne so this base can spawn ally/enemy teams using the normal system.", this);
            return;
        }

        hasCaptured = true;
        StartCoroutine(CaptureRoutine());
    }

    private IEnumerator CaptureRoutine()
    {
        if (debugLogs)
            Debug.Log("[BaseCaptureController] Base captured. Swapping icon, enabling refill zone, and spawning ally team.", this);

        SwapBaseIcon();
        EnableRefillZone();
        RefillCurrentZoneOccupants();
        SwitchBaseTurrets();
        SpawnAllyTeamImmediate();

        float delay = Mathf.Max(0f, enemySpawnDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        SpawnEnemyCounterattacks();
    }

    private void EnableRefillZone()
    {
        if (refillZone == null)
            return;

        refillZone.enabled = true;

        // If the refill zone GameObject itself was disabled in the hierarchy, wake it up too.
        if (!refillZone.gameObject.activeSelf)
            refillZone.gameObject.SetActive(true);
    }

    private void RefillCurrentZoneOccupants()
    {
        if (refillZone == null || !refillZone.isActiveAndEnabled)
            return;

        refillZone.RefillOccupantsImmediately();
    }

    private void SwapBaseIcon()
    {
        if (capturedBaseIconPrefab == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseCaptureController] Captured Base Icon Prefab is not assigned, so no ally base icon can be spawned.", this);
            return;
        }

        RectTransform parent = replacementIconParent;
        if (parent == null && currentBaseIcon != null)
            parent = currentBaseIcon.parent as RectTransform;

        if (parent == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseCaptureController] Could not swap base icon because there is no parent for the replacement icon.", this);
            return;
        }

        int siblingIndex = parent.childCount;
        Vector2 anchoredPosition = Vector2.zero;
        Vector2 sizeDelta = Vector2.zero;
        Vector2 anchorMin = new Vector2(0.5f, 0.5f);
        Vector2 anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        Quaternion localRotation = Quaternion.identity;
        Vector3 localScale = Vector3.one;

        RectTransform layoutSource = null;
        Transform placementTransform = null;

        if (useCapturedIconPlacementTarget && capturedBaseIconPlacementTarget != null)
        {
            placementTransform = capturedBaseIconPlacementTarget;
            layoutSource = capturedBaseIconPlacementTarget as RectTransform;
        }
        else if (currentBaseIcon != null)
        {
            placementTransform = currentBaseIcon;
            layoutSource = currentBaseIcon;
        }

        if (placementTransform != null)
        {
            siblingIndex = placementTransform.GetSiblingIndex();
            localRotation = placementTransform.localRotation;
            localScale = placementTransform.localScale;
        }

        if (layoutSource != null)
        {
            anchoredPosition = layoutSource.anchoredPosition;
            sizeDelta = layoutSource.sizeDelta;
            anchorMin = layoutSource.anchorMin;
            anchorMax = layoutSource.anchorMax;
            pivot = layoutSource.pivot;
        }

        RectTransform newIcon = Instantiate(capturedBaseIconPrefab, parent);
        newIcon.name = capturedBaseIconPrefab.name;
        newIcon.gameObject.SetActive(true);

        if (preserveBaseIconLayout && layoutSource != null)
        {
            newIcon.anchorMin = anchorMin;
            newIcon.anchorMax = anchorMax;
            newIcon.pivot = pivot;
            newIcon.anchoredPosition = anchoredPosition;
            newIcon.sizeDelta = sizeDelta;
            newIcon.localRotation = localRotation;
            newIcon.localScale = localScale;
            newIcon.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1)));
        }
        else
        {
            newIcon.anchoredPosition3D = Vector3.zero;
            newIcon.localRotation = Quaternion.identity;
            newIcon.localScale = Vector3.one;
        }

        // Make sure the UI graphic on the spawned icon is actually enabled/visible.
        var graphic = newIcon.GetComponent<Graphic>() ?? newIcon.GetComponentInChildren<Graphic>(true);
        if (graphic != null)
        {
            graphic.enabled = true;
            graphic.gameObject.SetActive(true);
        }

        if (currentBaseIcon != null && destroyOldBaseIcon)
            Destroy(currentBaseIcon.gameObject);

        currentBaseIcon = newIcon;

        if (debugLogs)
        {
            string layoutSourceName = placementTransform != null ? placementTransform.name : "none";
            Debug.Log($"[BaseCaptureController] Swapped base icon to '{currentBaseIcon.name}' under parent '{parent.name}' using layout source '{layoutSourceName}'.", this);
        }
    }

    private void SwitchBaseTurrets()
    {
        if (baseTurretSwitcher == null)
            return;

        baseTurretSwitcher.SwitchToAlly();

        if (debugLogs)
            Debug.Log("[BaseCaptureController] Switched base turrets from enemy to ally.", this);
    }

    private void SpawnAllyTeamImmediate()
    {
        if (allyTeam.spawnPoint == null)
        {
            Debug.LogWarning("[BaseCaptureController] Ally spawn skipped because Ally Spawn Point is not assigned.", this);
            return;
        }

        if (allyTeam.allyPrefab == null)
        {
            Debug.LogWarning("[BaseCaptureController] Ally spawn skipped because Ally Prefab is not assigned.", this);
            return;
        }

        if (allyTeam.allyCount <= 0)
        {
            if (debugLogs)
                Debug.Log("[BaseCaptureController] Ally count is 0, so no ally team will be spawned.", this);
            return;
        }

        var plan = new LevelOne.AllySpawnPlan
        {
            teamIndex = allyTeam.teamIndex,
            teamNamePrefix = string.IsNullOrWhiteSpace(allyTeam.teamNamePrefix) ? "CapturedBaseAlly_" : allyTeam.teamNamePrefix,
            teamName = allyTeam.teamName,
            saveId = BuildAllyTeamSaveId(),
            spawnPoint = allyTeam.spawnPoint,
            allyPrefab = allyTeam.allyPrefab,
            allyCount = Mathf.Max(0, allyTeam.allyCount),
            spawnRadius = Mathf.Max(0f, allyTeam.spawnRadius),
            yOffset = allyTeam.yOffset,
            snapToNavMesh = allyTeam.snapToNavMesh,
            navMeshSampleDistance = Mathf.Max(0.1f, allyTeam.navMeshSampleDistance),
            parentUnderTeamRoot = allyTeam.parentUnderTeamRoot,
            anchorSmoothSpeed = Mathf.Max(0.01f, allyTeam.anchorSmoothSpeed)
        };

        if (levelOne.allyTeams == null)
            levelOne.allyTeams = new List<LevelOne.AllySpawnPlan>();

        int index = levelOne.allyTeams.Count;
        levelOne.allyTeams.Add(plan);
        levelOne.SpawnAllyTeam(index);
    }

    private List<int> RegisterEnemyCounterattackPlans(List<EnemyTeamConfig> configs)
    {
        var levelPlanIndices = new List<int>(configs != null ? configs.Count : 0);

        if (configs == null || configs.Count == 0)
            return levelPlanIndices;

        if (levelOne.teams == null)
            levelOne.teams = new List<LevelOne.TeamSpawnPlan>();

        for (int cfgIndex = 0; cfgIndex < configs.Count; cfgIndex++)
        {
            EnemyTeamConfig cfg = configs[cfgIndex];
            if (cfg == null)
            {
                levelPlanIndices.Add(-1);
                continue;
            }

            bool usingEntries = cfg.spawnEntries != null && cfg.spawnEntries.Count > 0;

            if (cfg.spawnPoint == null)
            {
                Debug.LogWarning($"[BaseCaptureController] Enemy spawn skipped for team {cfgIndex + 1} because Enemy Spawn Point is not assigned.", this);
                levelPlanIndices.Add(-1);
                continue;
            }

            if (!usingEntries && cfg.enemyPrefab == null)
            {
                Debug.LogWarning($"[BaseCaptureController] Enemy spawn skipped for team {cfgIndex + 1} because neither Enemy Prefab nor Spawn Entries are assigned.", this);
                levelPlanIndices.Add(-1);
                continue;
            }

            if (!usingEntries && cfg.enemyCount <= 0)
            {
                if (debugLogs)
                    Debug.Log($"[BaseCaptureController] Enemy count is 0 for team {cfgIndex + 1}, so that counterattack team will not be spawned.", this);
                levelPlanIndices.Add(-1);
                continue;
            }

            var plan = new LevelOne.TeamSpawnPlan
            {
                teamName = cfg.teamName,
                teamNamePrefix = string.IsNullOrWhiteSpace(cfg.teamNamePrefix) ? "CapturedBaseEnemy_" : cfg.teamNamePrefix,
                saveId = BuildEnemyTeamSaveId(cfgIndex),
                hoverHintTitle = string.IsNullOrWhiteSpace(cfg.hoverHintTitle) ? "Enemy Team" : cfg.hoverHintTitle,
                strengthGrade = string.IsNullOrWhiteSpace(cfg.strengthGrade) ? "" : cfg.strengthGrade,
                spawnPoint = cfg.spawnPoint,
                spawnSpreadRadius = Mathf.Max(0f, cfg.spawnSpreadRadius),
                snapToNavMesh = cfg.snapToNavMesh,
                navMeshSampleRadius = Mathf.Max(0.1f, cfg.navMeshSampleRadius),
                anchorSmoothSpeed = Mathf.Max(0.01f, cfg.anchorSmoothSpeed),
                useFixedEnemyIconScale = cfg.useFixedEnemyIconScale,
                fixedEnemyIconScale = Mathf.Max(0.01f, cfg.fixedEnemyIconScale),
                enemyIconScaleMultiplier = Mathf.Max(0.01f, cfg.enemyIconScaleMultiplier),
                debugEnemyIconScale = cfg.debugEnemyIconScale,
                moveTargetMode = cfg.moveTargetMode,
                playerTag = string.IsNullOrWhiteSpace(cfg.playerTag) ? "Player" : cfg.playerTag,
                targetTransform = cfg.targetTransform,
                fixedWorldPosition = cfg.fixedWorldPosition,
                updatePlannedTargetEvery = Mathf.Max(0.05f, cfg.updatePlannedTargetEvery),
                updatePlannedTargetContinuously = cfg.updatePlannedTargetContinuously,
                enemyPrefab = cfg.enemyPrefab,
                enemyCount = Mathf.Max(1, cfg.enemyCount),
                objectiveArriveDistance = Mathf.Max(0.1f, cfg.objectiveArriveDistance),
                objectiveMaxSeconds = Mathf.Max(0.1f, cfg.objectiveMaxSeconds),
                objectiveUpdateInterval = Mathf.Max(0.05f, cfg.objectiveUpdateInterval),
                aggroHoldSeconds = Mathf.Max(0f, cfg.aggroHoldSeconds),
                debugMarch = cfg.debugMarch,
                enableEnemy2HardMarch = cfg.enableEnemy2HardMarch,
                disableEnemy2TeamLeashWhileMarching = cfg.disableEnemy2TeamLeashWhileMarching,
                enableMeleeMarch = cfg.enableMeleeMarch,
                enableDroneMarch = cfg.enableDroneMarch,
                enableFallbackNavAgentMarch = cfg.enableFallbackNavAgentMarch,
                spawnEntries = new List<LevelOne.SpawnEntry>()
            };

            if (usingEntries)
            {
                for (int i = 0; i < cfg.spawnEntries.Count; i++)
                {
                    var src = cfg.spawnEntries[i];
                    if (src == null || src.prefab == null || src.count <= 0)
                        continue;

                    plan.spawnEntries.Add(new LevelOne.SpawnEntry
                    {
                        prefab = src.prefab,
                        count = Mathf.Max(0, src.count),
                        yOffset = src.yOffset
                    });
                }
            }

            int index = levelOne.teams.Count;
            levelOne.teams.Add(plan);
            levelPlanIndices.Add(index);
        }

        return levelPlanIndices;
    }

    private void RegisterEnemyCounterattackPlansOnly()
    {
        var configs = new List<EnemyTeamConfig>();

        if (enemyTeams != null)
        {
            for (int i = 0; i < enemyTeams.Count; i++)
            {
                if (enemyTeams[i] != null)
                    configs.Add(enemyTeams[i]);
            }
        }

        if (configs.Count == 0 && enemyTeam != null)
            configs.Add(enemyTeam);

        if (configs.Count == 0)
        {
            if (debugLogs)
                Debug.Log("[BaseCaptureController] No enemy counterattack teams configured.", this);
            return;
        }

        RegisterEnemyCounterattackPlans(configs);
    }

    private void SpawnEnemyCounterattacks()
    {
        bool spawnedAnyEnemyTeam = false;
        var configs = new List<EnemyTeamConfig>();

        if (enemyTeams != null)
        {
            for (int i = 0; i < enemyTeams.Count; i++)
            {
                if (enemyTeams[i] != null)
                    configs.Add(enemyTeams[i]);
            }
        }

        if (configs.Count == 0 && enemyTeam != null)
            configs.Add(enemyTeam);

        if (configs.Count == 0)
        {
            if (debugLogs)
                Debug.Log("[BaseCaptureController] No enemy counterattack teams configured.", this);
            return;
        }

        var levelPlanIndices = RegisterEnemyCounterattackPlans(configs);

        for (int cfgIndex = 0; cfgIndex < configs.Count; cfgIndex++)
        {
            EnemyTeamConfig cfg = configs[cfgIndex];
            if (cfg == null)
                continue;

            int planIndex = cfgIndex < levelPlanIndices.Count ? levelPlanIndices[cfgIndex] : -1;
            if (planIndex < 0)
                continue;

            if (!cfg.spawnAfterCaptureDelay)
                continue;

            var runtime = levelOne.SpawnTeamAndGetRuntime(planIndex);
            if (runtime == null)
                continue;

            spawnedAnyEnemyTeam = true;

            if (cfg.enableReinforcementTrigger && cfg.reinforcementEnemyTeamListIndex >= 0)
            {
                int backupCfgIndex = cfg.reinforcementEnemyTeamListIndex;
                int backupPlanIndex = (backupCfgIndex >= 0 && backupCfgIndex < levelPlanIndices.Count) ? levelPlanIndices[backupCfgIndex] : -1;
                if (backupPlanIndex >= 0)
                {
                    Transform backupSpawn = cfg.reinforcementSpawnPointOverride != null ? cfg.reinforcementSpawnPointOverride : null;
                    CreateReinforcementWatcher(runtime.teamRoot, runtime.spawnedUnits, backupPlanIndex, Mathf.Max(1, cfg.reinforcementDeathsRequired), backupSpawn);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning($"[BaseCaptureController] Reinforcement backup index {cfg.reinforcementEnemyTeamListIndex} is invalid for trigger team '{cfg.teamName}'.", this);
                }
            }
        }

        if (spawnedAnyEnemyTeam)
            ShowEnemyInboundWarningUI();
    }

    public void ShowEnemyInboundWarning()
    {
        ShowEnemyInboundWarningUI();
    }

    private void ShowEnemyInboundWarningUI()
    {
        if (!hasCaptured)
            return;

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

    private void CreateReinforcementWatcher(Transform watchedTeamRoot, List<GameObject> watchedUnits, int backupPlanIndex, int deathsRequired, Transform backupSpawnOverride)
    {
        var watcher = gameObject.AddComponent<BaseCaptureReinforcementWatcher>();
        watcher.Initialize(this, watchedTeamRoot, watchedUnits, levelOne, backupPlanIndex, Mathf.Max(1, deathsRequired), backupSpawnOverride, debugLogs);
    }

    private sealed class BaseCaptureReinforcementWatcher : MonoBehaviour
    {
        private BaseCaptureController _owner;
        private Transform _watchedTeamRoot;
        private List<GameObject> _watchedUnits;
        private LevelOne _levelOne;
        private int _backupPlanIndex = -1;
        private int _deathsRequired = 1;
        private Transform _backupSpawnOverride;
        private bool _debugLogs;
        private bool _triggered;
        private int _initialAliveCount;
        private float _nextCheckTime;

        public void Initialize(BaseCaptureController owner, Transform watchedTeamRoot, List<GameObject> watchedUnits, LevelOne levelOne, int backupPlanIndex, int deathsRequired, Transform backupSpawnOverride, bool debugLogs)
        {
            _owner = owner;
            _watchedTeamRoot = watchedTeamRoot;
            _watchedUnits = watchedUnits ?? new List<GameObject>();
            _levelOne = levelOne;
            _backupPlanIndex = backupPlanIndex;
            _deathsRequired = Mathf.Max(1, deathsRequired);
            _backupSpawnOverride = backupSpawnOverride;
            _debugLogs = debugLogs;
            _initialAliveCount = CountAlive(_watchedUnits);
            _nextCheckTime = Time.time + 0.1f;

            if (_debugLogs)
                Debug.Log($"[BaseCaptureController] Watching reinforcement trigger for team '{(_watchedTeamRoot != null ? _watchedTeamRoot.name : "<null>")}'. Need {_deathsRequired} deaths. Backup plan index = {_backupPlanIndex}.", owner);
        }

        private void Update()
        {
            if (_triggered || _owner == null || _levelOne == null)
            {
                if (_triggered) Destroy(this);
                return;
            }

            if (Time.time < _nextCheckTime)
                return;

            _nextCheckTime = Time.time + 0.15f;

            int aliveNow = CountAlive(_watchedUnits);
            int deaths = Mathf.Max(0, _initialAliveCount - aliveNow);

            if (_debugLogs)
            {
                // Log only near threshold to avoid spam.
                if (deaths > 0 && deaths >= _deathsRequired - 1)
                    Debug.Log($"[BaseCaptureController] Reinforcement deaths counted {deaths}/{_deathsRequired} for watched team '{(_watchedTeamRoot != null ? _watchedTeamRoot.name : "<null>")}'.", _owner);
            }

            if (deaths >= _deathsRequired)
                Trigger();
        }

        private void Trigger()
        {
            if (_triggered) return;
            _triggered = true;

            if (_backupPlanIndex < 0 || _backupPlanIndex >= _levelOne.teams.Count)
            {
                if (_debugLogs)
                    Debug.LogWarning($"[BaseCaptureController] Reinforcement backup plan index {_backupPlanIndex} is out of range.", _owner);
                Destroy(this);
                return;
            }

            Vector3 objective = _owner != null ? _owner.transform.position : Vector3.zero;
            if (_watchedTeamRoot != null)
            {
                var watchedAnchor = _watchedTeamRoot.GetComponent<EncounterTeamAnchor>();
                if (watchedAnchor != null && watchedAnchor.HasMoveTarget)
                    objective = watchedAnchor.MoveTarget;
                else
                    objective = _watchedTeamRoot.position;
            }

            var plan = _levelOne.teams[_backupPlanIndex];
            if (plan == null)
            {
                if (_debugLogs)
                    Debug.LogWarning($"[BaseCaptureController] Reinforcement backup plan {_backupPlanIndex} is null.", _owner);
                Destroy(this);
                return;
            }

            var savedSpawnPoint = plan.spawnPoint;
            var savedMode = plan.moveTargetMode;
            var savedPlayerTag = plan.playerTag;
            var savedTargetTransform = plan.targetTransform;
            var savedFixedWorldPosition = plan.fixedWorldPosition;
            var savedUpdateEvery = plan.updatePlannedTargetEvery;
            var savedContinuous = plan.updatePlannedTargetContinuously;

            try
            {
                if (_backupSpawnOverride != null)
                    plan.spawnPoint = _backupSpawnOverride;

                plan.moveTargetMode = LevelOne.MoveTargetMode.FixedPosition;
                plan.playerTag = string.IsNullOrWhiteSpace(plan.playerTag) ? "Player" : plan.playerTag;
                plan.targetTransform = null;
                plan.fixedWorldPosition = objective;
                plan.updatePlannedTargetEvery = 0.25f;
                plan.updatePlannedTargetContinuously = false;

                var runtime = _levelOne.SpawnTeamAndGetRuntime(_backupPlanIndex);
                if (runtime != null && runtime.anchor != null)
                    runtime.anchor.SetMoveTarget(objective);

                if (runtime != null && _owner != null)
                    _owner.ShowEnemyInboundWarning();

                if (_debugLogs)
                    Debug.Log(runtime != null
                        ? $"[BaseCaptureController] Spawned reinforcement backup plan {_backupPlanIndex} toward {objective}."
                        : $"[BaseCaptureController] Failed to spawn reinforcement backup plan {_backupPlanIndex}.", _owner);
            }
            finally
            {
                plan.spawnPoint = savedSpawnPoint;
                plan.moveTargetMode = savedMode;
                plan.playerTag = savedPlayerTag;
                plan.targetTransform = savedTargetTransform;
                plan.fixedWorldPosition = savedFixedWorldPosition;
                plan.updatePlannedTargetEvery = savedUpdateEvery;
                plan.updatePlannedTargetContinuously = savedContinuous;
            }

            Destroy(this);
        }

        private static int CountAlive(List<GameObject> units)
        {
            if (units == null || units.Count == 0) return 0;

            int alive = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var go = units[i];
                if (go == null)
                    continue;

                if (go.activeInHierarchy)
                    alive++;
            }
            return alive;
        }
    }

    private void SetEnemyInboundWarningVisible(bool visible)
    {
        if (enemyInboundWarningUI == null)
            return;

        if (visible)
        {
            enemyInboundWarningUI.SetActive(true);

            var rect = enemyInboundWarningUI.transform as RectTransform;
            if (rect != null)
                rect.SetAsLastSibling();

            var groups = enemyInboundWarningUI.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var group in groups)
            {
                if (group == null)
                    continue;

                group.alpha = 1f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var graphics = enemyInboundWarningUI.GetComponentsInChildren<Graphic>(true);
            foreach (var graphic in graphics)
            {
                if (graphic == null)
                    continue;

                graphic.enabled = true;
                var c = graphic.color;
                c.a = 1f;
                graphic.color = c;
            }

            var tmpTexts = enemyInboundWarningUI.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var tmp in tmpTexts)
            {
                if (tmp == null)
                    continue;

                tmp.enabled = true;
                var c = tmp.color;
                c.a = 1f;
                tmp.color = c;
            }
        }
        else
        {
            enemyInboundWarningUI.SetActive(false);
        }
    }
}
