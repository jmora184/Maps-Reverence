using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Level One (Minimal)
/// - Press T to spawn an enemy team (root + N enemies) at a chosen spawn point.
/// - DOES NOT manage icons directly.
/// - Your existing EnemyTeamIconSystem (in the scene) will automatically spawn the team star + arrow
///   because it scans for EncounterTeamAnchor each LateUpdate.
/// - This script also feeds the anchor a "planned destination" = player position so the arrow points at the player.
/// </summary>
public class LevelOne_Minimal : MonoBehaviour
{
    [Header("Enemy Spawn")]
    public GameObject enemyPrefab;                 // Set to Assault_droid
    [Min(1)] public int enemyCount = 5;
    public Transform spawnPoint;                   // Pick your spawn location
    public float spawnSpreadRadius = 6f;

    [Header("Input")]
    public KeyCode spawnKey = KeyCode.T;

    [Header("Player Target")]
    public string playerTag = "Player";
    [Tooltip("If enabled, the team anchor's planned destination is updated to the player so the arrow points at them.")]
    public bool pointArrowToPlayer = true;
    public float updatePlannedTargetEvery = 0.25f;

    [Header("NavMesh Placement")]
    public bool snapToNavMesh = true;
    public float navMeshSampleRadius = 6f;

    private int _wave = 0;

    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
            SpawnEnemyTeam();
    }

    [ContextMenu("Spawn Enemy Team Now")]
    public void SpawnEnemyTeam()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("[LevelOne_Minimal] Enemy Prefab is not assigned (set to Assault_droid).", this);
            return;
        }
        if (spawnPoint == null)
        {
            Debug.LogError("[LevelOne_Minimal] Spawn Point is not assigned.", this);
            return;
        }

        _wave++;

        // Create a team root
        var teamRootGO = new GameObject($"EnemyTeam_Level1_{_wave}");
        teamRootGO.transform.position = spawnPoint.position;

        // Add anchor (this is what EnemyTeamIconSystem looks for)
        var anchor = teamRootGO.AddComponent<EncounterTeamAnchor>();
        anchor.faction = EncounterDirectorPOC.Faction.Enemy;
        anchor.updateContinuously = true;
        anchor.smooth = true;
        anchor.smoothSpeed = 10f;
        anchor.driveTransformPosition = false; // IMPORTANT: don't drag children

        // Feed planned destination so arrow points at player
        if (pointArrowToPlayer)
        {
            var feeder = teamRootGO.AddComponent<PlannedTargetToPlayerFeeder>();
            feeder.playerTag = playerTag;
            feeder.interval = Mathf.Max(0.05f, updatePlannedTargetEvery);
        }

        // Spawn enemies as children
        for (int i = 0; i < enemyCount; i++)
        {
            Vector3 pos = spawnPoint.position + UnityEngine.Random.insideUnitSphere * spawnSpreadRadius;
            pos.y = spawnPoint.position.y;

            if (snapToNavMesh && NavMesh.SamplePosition(pos, out var hit, navMeshSampleRadius, NavMesh.AllAreas))
                pos = hit.position;

            var enemy = Instantiate(enemyPrefab, pos, Quaternion.identity);
            enemy.name = $"{enemyPrefab.name}_L1_{_wave}_{i + 1}";
            enemy.transform.SetParent(teamRootGO.transform, true);
        }

        // Debug: confirm icon system exists and will see this anchor
        if (EnemyTeamIconSystem.Instance == null)
        {
            Debug.LogWarning("[LevelOne_Minimal] EnemyTeamIconSystem.Instance is NULL. The team anchor spawned, but no icon system is active in the scene.", this);
        }
        else
        {
            Debug.Log("[LevelOne_Minimal] Spawned team + EncounterTeamAnchor. EnemyTeamIconSystem should spawn the star/arrow automatically.", this);
        }
    }

    /// <summary>
    /// Continuously updates EncounterTeamAnchor's planned destination to the player's position,
    /// purely for UI direction arrow purposes.
    /// </summary>
    private class PlannedTargetToPlayerFeeder : MonoBehaviour
    {
        public string playerTag = "Player";
        public float interval = 0.25f;

        private EncounterTeamAnchor _anchor;
        private Transform _player;
        private float _next;

        private void Awake()
        {
            _anchor = GetComponent<EncounterTeamAnchor>();
        }

        private void Update()
        {
            if (_anchor == null) return;

            if (_player == null)
            {
                var go = GameObject.FindGameObjectWithTag(playerTag);
                if (go != null) _player = go.transform;
            }
            if (_player == null) return;

            if (Time.time >= _next)
            {
                _next = Time.time + interval;
                _anchor.SetMoveTarget(_player.position);
            }
        }
    }
}
