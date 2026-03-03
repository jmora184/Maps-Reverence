using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// EncounterObjectiveRouter (FAST STEER + TEAM ARROW)
///
/// This is the full movement router (nothing removed):
/// - Enemy2Controller: injects proxy into private _combatTarget and sets chasing=true while NOT in real combat.
/// - MeleeEnemy2Controller: calls SetCombatTarget(proxy) while no real combat target exists.
/// - DroneEnemyController: sets waypoints=[proxy], forces state=Patrol while no combat target exists.
/// - Fallback: drives NavMeshAgent.SetDestination for plain agents.
///
/// PLUS:
/// - Updates the team's EncounterTeamAnchor.SetMoveTarget(...) so your enemy direction arrow sprite shows where the team is headed.
/// - Steering happens EVERY frame (prevents Enemy2Controller from briefly falling back to player and "looking back").
/// - Patrol-index advancement is throttled by updateInterval for performance.
///
/// Notes:
/// - This router is meant to be added to spawned units (e.g., by EncounterDirectorPOC_UPGRADED2).
/// - It does NOT disable AI controllers; it only steers them when they are not already engaged in real combat.
/// </summary>
public class EncounterObjectiveRouter : MonoBehaviour
{
    [Header("Route")]
    public bool loopPatrol = true;
    public bool pingPong = false;

    [Tooltip("How close to a route point / defend center counts as arrived.")]
    public float arriveDistance = 3.5f;

    [Tooltip("How often to advance patrol index / do expensive route bookkeeping. Steering still happens every frame.")]
    public float updateInterval = 0.25f;

    [Tooltip("Safety timeout. 0 means never timeout.")]
    public float maxSeconds = 0f;

    // Route state
    private float _start;
    private float _next;

    private Vector3[] _patrol;
    private int _idx = 0;
    private int _dir = 1;

    private bool _isDefend;
    private Vector3 _defendCenter;
    private float _defendRadius;

    private GameObject _proxyGO;

    // Team anchor for arrow direction
    private EncounterTeamAnchor _teamAnchor;

    // Enemy2 reflection
    private MonoBehaviour _enemy2;
    private Behaviour _enemy2B;
    private FieldInfo _e2_combatTarget;
    private FieldInfo _e2_chasing;
    private FieldInfo _e2_leash;
    private bool _e2_hadLeash;
    private bool _e2_origLeash;

    // Melee2 reflection
    private MonoBehaviour _melee2;
    private Behaviour _melee2B;
    private MethodInfo _m2_setCombatTarget;
    private MethodInfo _m2_clearCombatTarget;
    private FieldInfo _m2_combatTarget;

    // Drone reflection
    private MonoBehaviour _drone;
    private Behaviour _droneB;
    private FieldInfo _d_combatTarget;
    private FieldInfo _d_waypoints;
    private FieldInfo _d_state;
    private FieldInfo _d_wpIndex;
    private FieldInfo _d_wpDir;
    private object _d_origWaypoints;
    private object _d_origState;

    // Fallback agent
    private NavMeshAgent _agent;

    private void Awake()
    {
        _start = Time.time;
        _agent = GetComponent<NavMeshAgent>();

        // Find the team anchor up the hierarchy so we can update direction arrow
        _teamAnchor = GetComponentInParent<EncounterTeamAnchor>();

        _enemy2 = FindMono("Enemy2Controller");
        _enemy2B = _enemy2 as Behaviour;
        if (_enemy2 != null) CacheEnemy2();

        _melee2 = FindMono("MeleeEnemy2Controller");
        _melee2B = _melee2 as Behaviour;
        if (_melee2 != null) CacheMelee2();

        _drone = FindMono("DroneEnemyController");
        _droneB = _drone as Behaviour;
        if (_drone != null) CacheDrone();

        _proxyGO = new GameObject("EncounterObjectiveProxy");
        _proxyGO.transform.SetParent(null, true);

        // Disable Enemy2 leash while routing (prevents StopAgent/ResetPath idle freezes)
        if (_enemy2 != null && _e2_leash != null)
        {
            try
            {
                _e2_hadLeash = true;
                _e2_origLeash = (bool)_e2_leash.GetValue(_enemy2);
                _e2_leash.SetValue(_enemy2, false);
            }
            catch { }
        }

        UpdateProxyToCurrentTarget();
        SteerNow(); // initial kick
    }

    private void OnDestroy()
    {
        if (_proxyGO != null) Destroy(_proxyGO);

        if (_enemy2 != null && _e2_hadLeash && _e2_leash != null)
        {
            try { _e2_leash.SetValue(_enemy2, _e2_origLeash); } catch { }
        }

        // restore drone if we touched it
        if (_drone != null)
        {
            try
            {
                if (_d_waypoints != null && _d_origWaypoints != null) _d_waypoints.SetValue(_drone, _d_origWaypoints);
                if (_d_state != null && _d_origState != null) _d_state.SetValue(_drone, _d_origState);
            }
            catch { }
        }
    }

    private void LateUpdate()
    {
        if (maxSeconds > 0f && (Time.time - _start) >= maxSeconds)
        {
            Destroy(this);
            return;
        }

        // Patrol index advancement at throttled rate (cheap)
        if (Time.time >= _next)
        {
            _next = Time.time + Mathf.Max(0.05f, updateInterval);

            if (!_isDefend && _patrol != null && _patrol.Length > 0)
            {
                Vector3 target = _patrol[Mathf.Clamp(_idx, 0, _patrol.Length - 1)];
                if (Vector3.Distance(transform.position, target) <= arriveDistance)
                    AdvancePatrolIndex();
            }
        }

        // IMPORTANT: steer every frame so Enemy2Controller can't briefly fall back to player and "look back"
        UpdateProxyToCurrentTarget();
        SteerNow();
    }

    public void SetPatrol(Vector3[] patrolWorld)
    {
        _isDefend = false;
        _defendCenter = Vector3.zero;
        _defendRadius = 0f;

        _patrol = patrolWorld;
        _idx = 0;
        _dir = 1;

        UpdateProxyToCurrentTarget();
        SteerNow();
    }

    public void SetDefend(Vector3 center, float radius)
    {
        _isDefend = true;
        _defendCenter = center;
        _defendRadius = Mathf.Max(0f, radius);

        _patrol = null;
        _idx = 0;
        _dir = 1;

        UpdateProxyToCurrentTarget();
        SteerNow();
    }

    private void AdvancePatrolIndex()
    {
        if (_patrol == null || _patrol.Length == 0) return;

        if (pingPong)
        {
            int next = _idx + _dir;
            if (next < 0 || next >= _patrol.Length)
            {
                _dir *= -1;
                next = _idx + _dir;
                next = Mathf.Clamp(next, 0, _patrol.Length - 1);
            }
            _idx = next;
            return;
        }

        if (loopPatrol)
        {
            _idx = (_idx + 1) % _patrol.Length;
            return;
        }

        _idx = Mathf.Min(_idx + 1, _patrol.Length - 1);
    }

    private void UpdateProxyToCurrentTarget()
    {
        if (_proxyGO == null) return;

        Vector3 dest = transform.position;

        if (_isDefend)
        {
            dest = _defendCenter;
        }
        else if (_patrol != null && _patrol.Length > 0)
        {
            dest = _patrol[Mathf.Clamp(_idx, 0, _patrol.Length - 1)];
        }

        _proxyGO.transform.position = dest;

        // Feed team arrow direction
        if (_teamAnchor != null)
            _teamAnchor.SetMoveTarget(dest);
    }

    private void SteerNow()
    {
        // If any controller has a real combat target, stop steering (combat should take over)
        if (HasRealCombatTarget()) return;

        // Enemy2: inject combat target + chasing
        if (_enemy2 != null && (_enemy2B == null || _enemy2B.enabled))
        {
            if (_e2_combatTarget != null) SafeSet(_enemy2, _e2_combatTarget, _proxyGO.transform);
            if (_e2_chasing != null) SafeSet(_enemy2, _e2_chasing, true);

            // extra push for agents
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_proxyGO.transform.position);
            }
            return;
        }

        // Melee2: set combat target proxy
        if (_melee2 != null && (_melee2B == null || _melee2B.enabled))
        {
            if (_m2_setCombatTarget != null)
            {
                _m2_setCombatTarget.Invoke(_melee2, new object[] { _proxyGO.transform });
            }
            else if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_proxyGO.transform.position);
            }
            return;
        }

        // Drone: set waypoints=[proxy], state=Patrol
        if (_drone != null && (_droneB == null || _droneB.enabled))
        {
            try
            {
                if (_d_waypoints != null)
                {
                    if (_d_origWaypoints == null) _d_origWaypoints = _d_waypoints.GetValue(_drone);
                    _d_waypoints.SetValue(_drone, new Transform[] { _proxyGO.transform });
                }

                if (_d_state != null)
                {
                    if (_d_origState == null) _d_origState = _d_state.GetValue(_drone);
                    object patrol = Enum.Parse(_d_state.FieldType, "Patrol");
                    _d_state.SetValue(_drone, patrol);
                }

                if (_d_wpIndex != null) _d_wpIndex.SetValue(_drone, 0);
                if (_d_wpDir != null) _d_wpDir.SetValue(_drone, 1);
            }
            catch { }
            return;
        }

        // Fallback: NavMeshAgent direct
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(_proxyGO.transform.position);
        }
    }

    private bool HasRealCombatTarget()
    {
        // Enemy2: real combat if _combatTarget != null and not our proxy
        if (_enemy2 != null && (_enemy2B == null || _enemy2B.enabled) && _e2_combatTarget != null)
        {
            var ct = SafeGet<Transform>(_enemy2, _e2_combatTarget);
            if (ct != null && _proxyGO != null && ct != _proxyGO.transform) return true;
        }

        // Melee2: combatTarget != null and not our proxy
        if (_melee2 != null && (_melee2B == null || _melee2B.enabled) && _m2_combatTarget != null)
        {
            var ct = SafeGet<Transform>(_melee2, _m2_combatTarget);
            if (ct != null && _proxyGO != null && ct != _proxyGO.transform) return true;
        }

        // Drone: combatTarget != null
        if (_drone != null && (_droneB == null || _droneB.enabled) && _d_combatTarget != null)
        {
            var ct = SafeGet<Transform>(_drone, _d_combatTarget);
            if (ct != null) return true;
        }

        return false;
    }

    private MonoBehaviour FindMono(string typeName)
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

    private void CacheEnemy2()
    {
        Type t = _enemy2.GetType();
        _e2_combatTarget = t.GetField("_combatTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        _e2_chasing = t.GetField("chasing", BindingFlags.Instance | BindingFlags.NonPublic);
        _e2_leash = t.GetField("enableTeamLeash", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private void CacheMelee2()
    {
        Type t = _melee2.GetType();
        _m2_setCombatTarget = t.GetMethod("SetCombatTarget", BindingFlags.Instance | BindingFlags.Public);
        _m2_clearCombatTarget = t.GetMethod("ClearCombatTarget", BindingFlags.Instance | BindingFlags.Public);
        _m2_combatTarget = t.GetField("combatTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private void CacheDrone()
    {
        Type t = _drone.GetType();
        _d_combatTarget = t.GetField("combatTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _d_waypoints = t.GetField("waypoints", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _d_state = t.GetField("state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _d_wpIndex = t.GetField("_wpIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        _d_wpDir = t.GetField("_wpDir", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private static void SafeSet(object obj, FieldInfo f, object value)
    {
        try { if (obj != null && f != null) f.SetValue(obj, value); } catch { }
    }

    private static T SafeGet<T>(object obj, FieldInfo f) where T : class
    {
        try { return (obj != null && f != null) ? f.GetValue(obj) as T : null; } catch { return null; }
    }
}
