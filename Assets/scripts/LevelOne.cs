using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

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

    [Header("Test Hotkey")]
    public bool enableHotkey = true;
    public KeyCode spawnAllKey = KeyCode.T;

    private void Update()
    {
        if (enableHotkey && Input.GetKeyDown(spawnAllKey))
            SpawnAllTeams();
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
    }

    public void SpawnTeam(int teamIndex)
    {
        if (teams == null || teamIndex < 0 || teamIndex >= teams.Count)
        {
            Debug.LogError($"[LevelOne] Invalid team index {teamIndex}.", this);
            return;
        }

        TeamSpawnPlan plan = teams[teamIndex];
        if (plan == null)
        {
            Debug.LogError($"[LevelOne] Team plan at index {teamIndex} is null.", this);
            return;
        }

        if (plan.spawnPoint == null)
        {
            Debug.LogError($"[LevelOne] Team plan {teamIndex} spawnPoint is not assigned.", this);
            return;
        }

        bool usingEntries = plan.spawnEntries != null && plan.spawnEntries.Count > 0;
        if (!usingEntries && plan.enemyPrefab == null)
        {
            Debug.LogError($"[LevelOne] Team plan {teamIndex} has no spawnEntries and no enemyPrefab assigned.", this);
            return;
        }

        plan.spawnSequence++;

        string safePrefix = string.IsNullOrWhiteSpace(plan.teamNamePrefix) ? "EnemyTeam_" : plan.teamNamePrefix;
        string baseName = string.IsNullOrWhiteSpace(plan.teamName)
            ? $"{safePrefix}{teamIndex + 1}_{plan.spawnSequence}"
            : $"{plan.teamName}_{plan.spawnSequence}";

        string teamRootName = $"{baseName}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

        var teamRootGO = new GameObject(teamRootName);
        teamRootGO.transform.position = plan.spawnPoint.position;

        // UI centroid anchor
        var anchor = teamRootGO.AddComponent<EncounterTeamAnchor>();
        anchor.faction = EncounterDirectorPOC.Faction.Enemy;
        anchor.updateContinuously = true;
        anchor.smooth = true;
        anchor.smoothSpeed = plan.anchorSmoothSpeed;
        anchor.driveTransformPosition = false;

        // UI arrow target
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

        // Spawn units
        var spawned = new List<GameObject>(32);
        if (usingEntries) SpawnFromEntries(plan, teamRootGO.transform, spawned);
        else SpawnLegacy(plan, teamRootGO.transform, spawned);

        if (plan.moveTargetMode == MoveTargetMode.None) return;

        // Attach per-controller march overrides
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

    // UnityEvent-friendly wrappers
    public void SpawnTeam0() => SpawnTeam(0);
    public void SpawnTeam1() => SpawnTeam(1);
    public void SpawnTeam2() => SpawnTeam(2);
    public void SpawnTeam3() => SpawnTeam(3);
    public void SpawnTeam4() => SpawnTeam(4);

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
    public class TeamSpawnPlan
    {
        [Header("Team Identity")]
        public string teamName = "";
        public string teamNamePrefix = "EnemyTeam_";
        [HideInInspector] public int spawnSequence = 0;

        [Header("Spawn Location")]
        public Transform spawnPoint;
        public float spawnSpreadRadius = 6f;

        [Header("NavMesh Placement (spawn)")]
        public bool snapToNavMesh = true;
        public float navMeshSampleRadius = 6f;

        [Header("Anchor Smoothing (UI Icon)")]
        public float anchorSmoothSpeed = 10f;

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
            ForceMeleeChaseProxy(force:true);
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

        ForceMeleeChaseProxy(force:false);
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
            ForceDronePatrolToProxy(force:true);
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

        ForceDronePatrolToProxy(force:false);
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
