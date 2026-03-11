using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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

    [Header("Base Icon Swap (MiniUI)")]
    [Tooltip("Current icon object already placed over the base in your MiniUI.")]
    public RectTransform currentBaseIcon;

    [Tooltip("Prefab to spawn when the base becomes friendly.")]
    public RectTransform capturedBaseIconPrefab;

    [Tooltip("Optional explicit parent for the replacement icon. If empty, uses the current icon's parent.")]
    public RectTransform replacementIconParent;

    [Tooltip("If true, the replacement icon copies anchored position / rotation / scale from the old icon.")]
    public bool preserveBaseIconLayout = true;

    [Tooltip("If true, the old icon is destroyed after replacement.")]
    public bool destroyOldBaseIcon = true;

    [Header("Ally Team Spawn")]
    public AllyTeamConfig allyTeam = new AllyTeamConfig();

    [Header("Enemy Team Counterattack")]
    public EnemyTeamConfig enemyTeam = new EnemyTeamConfig();

    [SerializeField, Tooltip("Runtime only: true after this base has been captured once.")]
    private bool hasCaptured = false;

    private UnityAction _cachedActivationListener;
    private UnityEvent _hookedActivationEvent;
    private bool _isSubscribed;

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

        [Header("Spawn")]
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
        SpawnAllyTeamImmediate();

        float delay = Mathf.Max(0f, enemySpawnDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        SpawnEnemyCounterattack();
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

    private void SwapBaseIcon()
    {
        if (capturedBaseIconPrefab == null)
            return;

        RectTransform parent = replacementIconParent;
        if (parent == null && currentBaseIcon != null)
            parent = currentBaseIcon.parent as RectTransform;

        if (parent == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseCaptureController] Could not swap base icon because there is no parent for the replacement icon.", this);
            return;
        }

        RectTransform newIcon = Instantiate(capturedBaseIconPrefab, parent);
        newIcon.name = capturedBaseIconPrefab.name;

        if (currentBaseIcon != null && preserveBaseIconLayout)
        {
            newIcon.anchorMin = currentBaseIcon.anchorMin;
            newIcon.anchorMax = currentBaseIcon.anchorMax;
            newIcon.pivot = currentBaseIcon.pivot;
            newIcon.anchoredPosition = currentBaseIcon.anchoredPosition;
            newIcon.sizeDelta = currentBaseIcon.sizeDelta;
            newIcon.localRotation = currentBaseIcon.localRotation;
            newIcon.localScale = currentBaseIcon.localScale;
            newIcon.SetSiblingIndex(currentBaseIcon.GetSiblingIndex());
        }

        if (currentBaseIcon != null && destroyOldBaseIcon)
            Destroy(currentBaseIcon.gameObject);

        currentBaseIcon = newIcon;
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

    private void SpawnEnemyCounterattack()
    {
        bool usingEntries = enemyTeam.spawnEntries != null && enemyTeam.spawnEntries.Count > 0;

        if (enemyTeam.spawnPoint == null)
        {
            Debug.LogWarning("[BaseCaptureController] Enemy spawn skipped because Enemy Spawn Point is not assigned.", this);
            return;
        }

        if (!usingEntries && enemyTeam.enemyPrefab == null)
        {
            Debug.LogWarning("[BaseCaptureController] Enemy spawn skipped because neither Enemy Prefab nor Spawn Entries are assigned.", this);
            return;
        }

        if (!usingEntries && enemyTeam.enemyCount <= 0)
        {
            if (debugLogs)
                Debug.Log("[BaseCaptureController] Enemy count is 0, so no counterattack team will be spawned.", this);
            return;
        }

        var plan = new LevelOne.TeamSpawnPlan
        {
            teamName = enemyTeam.teamName,
            teamNamePrefix = string.IsNullOrWhiteSpace(enemyTeam.teamNamePrefix) ? "CapturedBaseEnemy_" : enemyTeam.teamNamePrefix,
            spawnPoint = enemyTeam.spawnPoint,
            spawnSpreadRadius = Mathf.Max(0f, enemyTeam.spawnSpreadRadius),
            snapToNavMesh = enemyTeam.snapToNavMesh,
            navMeshSampleRadius = Mathf.Max(0.1f, enemyTeam.navMeshSampleRadius),
            anchorSmoothSpeed = Mathf.Max(0.01f, enemyTeam.anchorSmoothSpeed),
            useFixedEnemyIconScale = enemyTeam.useFixedEnemyIconScale,
            fixedEnemyIconScale = Mathf.Max(0.01f, enemyTeam.fixedEnemyIconScale),
            enemyIconScaleMultiplier = Mathf.Max(0.01f, enemyTeam.enemyIconScaleMultiplier),
            debugEnemyIconScale = enemyTeam.debugEnemyIconScale,
            moveTargetMode = enemyTeam.moveTargetMode,
            playerTag = string.IsNullOrWhiteSpace(enemyTeam.playerTag) ? "Player" : enemyTeam.playerTag,
            targetTransform = enemyTeam.targetTransform,
            fixedWorldPosition = enemyTeam.fixedWorldPosition,
            updatePlannedTargetEvery = Mathf.Max(0.05f, enemyTeam.updatePlannedTargetEvery),
            updatePlannedTargetContinuously = enemyTeam.updatePlannedTargetContinuously,
            enemyPrefab = enemyTeam.enemyPrefab,
            enemyCount = Mathf.Max(1, enemyTeam.enemyCount),
            objectiveArriveDistance = Mathf.Max(0.1f, enemyTeam.objectiveArriveDistance),
            objectiveMaxSeconds = Mathf.Max(0.1f, enemyTeam.objectiveMaxSeconds),
            objectiveUpdateInterval = Mathf.Max(0.05f, enemyTeam.objectiveUpdateInterval),
            aggroHoldSeconds = Mathf.Max(0f, enemyTeam.aggroHoldSeconds),
            debugMarch = enemyTeam.debugMarch,
            enableEnemy2HardMarch = enemyTeam.enableEnemy2HardMarch,
            disableEnemy2TeamLeashWhileMarching = enemyTeam.disableEnemy2TeamLeashWhileMarching,
            enableMeleeMarch = enemyTeam.enableMeleeMarch,
            enableDroneMarch = enemyTeam.enableDroneMarch,
            enableFallbackNavAgentMarch = enemyTeam.enableFallbackNavAgentMarch,
            spawnEntries = new List<LevelOne.SpawnEntry>()
        };

        if (usingEntries)
        {
            for (int i = 0; i < enemyTeam.spawnEntries.Count; i++)
            {
                var src = enemyTeam.spawnEntries[i];
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

        if (levelOne.teams == null)
            levelOne.teams = new List<LevelOne.TeamSpawnPlan>();

        int index = levelOne.teams.Count;
        levelOne.teams.Add(plan);
        levelOne.SpawnTeam(index);
    }
}
