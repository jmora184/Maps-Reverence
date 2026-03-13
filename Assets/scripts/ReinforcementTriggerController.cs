using System.Collections.Generic;
using UnityEngine;

public class ReinforcementTriggerController : MonoBehaviour
{
    private Transform _watchedTeamRoot;
    private readonly HashSet<EnemyHealthController> _watchedMembers = new HashSet<EnemyHealthController>();
    private LevelOne _levelOne;
    private int _backupPlanIndex = -1;
    private int _deathsRequired = 3;
    private int _deathCount = 0;
    private bool _triggered = false;
    private Transform _backupSpawnOverride;
    private bool _debug = false;
    private BaseCaptureController _baseCaptureController;

    public static ReinforcementTriggerController Create(GameObject owner, Transform watchedTeamRoot, List<GameObject> watchedMembers, LevelOne levelOne, int backupPlanIndex, int deathsRequired, Transform backupSpawnOverride, bool debug, BaseCaptureController baseCaptureController = null)
    {
        if (owner == null || watchedTeamRoot == null || levelOne == null || backupPlanIndex < 0)
            return null;

        var c = owner.AddComponent<ReinforcementTriggerController>();
        c.Initialize(watchedTeamRoot, watchedMembers, levelOne, backupPlanIndex, deathsRequired, backupSpawnOverride, debug, baseCaptureController);
        return c;
    }

    public void Initialize(Transform watchedTeamRoot, List<GameObject> watchedMembers, LevelOne levelOne, int backupPlanIndex, int deathsRequired, Transform backupSpawnOverride, bool debug, BaseCaptureController baseCaptureController = null)
    {
        _watchedTeamRoot = watchedTeamRoot;
        _levelOne = levelOne;
        _backupPlanIndex = backupPlanIndex;
        _deathsRequired = Mathf.Max(1, deathsRequired);
        _backupSpawnOverride = backupSpawnOverride;
        _debug = debug;
        _baseCaptureController = baseCaptureController;

        _watchedMembers.Clear();
        if (watchedMembers != null)
        {
            for (int i = 0; i < watchedMembers.Count; i++)
            {
                var go = watchedMembers[i];
                if (go == null) continue;
                var hp = go.GetComponent<EnemyHealthController>();
                if (hp != null) _watchedMembers.Add(hp);
            }
        }
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

    private void HandleAnyEnemyDied(EnemyHealthController dead)
    {
        if (_triggered || dead == null) return;

        bool belongs = _watchedMembers.Contains(dead);
        if (!belongs && _watchedTeamRoot != null)
            belongs = dead.transform.IsChildOf(_watchedTeamRoot);

        if (!belongs) return;

        _deathCount++;
        if (_debug)
            Debug.Log($"[ReinforcementTriggerController] Counted death {_deathCount}/{_deathsRequired} for watched team '{(_watchedTeamRoot != null ? _watchedTeamRoot.name : "<null>")}'.", this);

        if (_deathCount < _deathsRequired) return;
        TriggerBackup();
    }


    private void HandleAnyDroneDied(DroneEnemyController dead)
    {
        if (_triggered || dead == null) return;

        bool belongs = _watchedTeamRoot != null && dead.transform.IsChildOf(_watchedTeamRoot);
        if (!belongs) return;

        _deathCount++;
        if (_debug)
            Debug.Log($"[ReinforcementTriggerController] Counted drone death {_deathCount}/{_deathsRequired} for watched team '{(_watchedTeamRoot != null ? _watchedTeamRoot.name : "<null>")}'.", this);

        if (_deathCount < _deathsRequired) return;
        TriggerBackup();
    }

    private void TriggerBackup()
    {
        if (_triggered || _levelOne == null || _backupPlanIndex < 0) return;
        _triggered = true;

        Transform originalSpawn = null;
        LevelOne.TeamSpawnPlan plan = null;
        if (_backupPlanIndex >= 0 && _backupPlanIndex < _levelOne.teams.Count)
            plan = _levelOne.teams[_backupPlanIndex];

        if (plan != null)
        {
            originalSpawn = plan.spawnPoint;
            if (_backupSpawnOverride != null)
                plan.spawnPoint = _backupSpawnOverride;

            if (_watchedTeamRoot != null)
            {
                plan.moveTargetMode = LevelOne.MoveTargetMode.FixedPosition;
                plan.fixedWorldPosition = _watchedTeamRoot.position;
                plan.targetTransform = null;
            }
        }

        var runtime = _levelOne.SpawnTeamAndGetRuntime(_backupPlanIndex);

        if (runtime != null && _baseCaptureController != null)
            _baseCaptureController.ShowEnemyInboundWarning();

        if (_debug)
            Debug.Log(runtime != null ? $"[ReinforcementTriggerController] Spawned backup team from plan index {_backupPlanIndex}." : $"[ReinforcementTriggerController] Failed to spawn backup team from plan index {_backupPlanIndex}.", this);

        if (plan != null)
            plan.spawnPoint = originalSpawn;

        Destroy(this);
    }
}
