// 1/4/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

public class AllyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    [Tooltip("If target is set (e.g., joining a team), we will keep updating destination to follow it.")]
    public float travelFollowUpdateInterval = 0.15f;
    public Rigidbody theRB;
    public Transform target; // (used for dynamic travel follow, e.g., join in-route)
    private bool chasing;
    private float travelFollowTimer;
    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;
    public NavMeshAgent agent;

    [Header("Combat")]
    public GameObject bullet;
    public Transform firePoint;
    public float fireRate = 0.5f;
    private float fireCount;

    [Header("Animation")]
    public Animator soldierAnimator;


    [Header("Move Marker Auto-Clear")]
    [Tooltip("When close enough to the pinned destination, auto-clear the move marker for this ally.")]
    public bool autoClearMoveMarkerOnArrival = true;

    [Tooltip("Extra distance beyond stoppingDistance to consider 'arrived' for marker clearing.")]
    public float markerArrivalBuffer = 0.15f;

    // Per-ally stats (team size -> multipliers)
    private AllyCombatStats combatStats;

    // Pinned state (v1 driven by 'chasing')
    private AllyPinnedStatus pinnedStatus;

    // Track one enemy target so we don't fight every enemy in the scene at once
    private Transform currentEnemy;

    // Remember where we were going before we got pulled into combat
    // NOTE: for TEAM moves, the "current" destination can change while we are fighting.
    // So we prefer to resume using the latest pinned destination from MoveDestinationMarkerSystem when available.
    private Vector3 resumeDestination;
    private bool hasResumeDestination;
    private bool resumeWasFollowing;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (theRB == null) theRB = GetComponent<Rigidbody>();
        if (soldierAnimator == null) soldierAnimator = GetComponentInChildren<Animator>();

        combatStats = GetComponent<AllyCombatStats>();
        pinnedStatus = GetComponent<AllyPinnedStatus>();
    }

    private void Start()
    {
        // Initialize agent speed.
        // IMPORTANT: don't accidentally override a NavMeshAgent speed you tuned in the Inspector.
        // If moveSpeed is 0, we treat the agent's current speed as the baseline.
        if (agent != null)
        {
            if (combatStats != null)
            {
                // Prefer explicit moveSpeed if you set it, otherwise keep the current agent.speed.
                float baseline = agent.speed;
                if (moveSpeed > 0f) baseline = moveSpeed;

                if (combatStats.baseMoveSpeed <= 0f)
                    combatStats.baseMoveSpeed = baseline;

                combatStats.ApplyToAgent(agent);
            }
            else if (moveSpeed > 0f)
            {
                agent.speed = moveSpeed;
            }
        }
    }

    private void Update()
    {
        // Keep agent speed in sync with team size (only when it changes).
        // (Prevents unexpected slowdowns if you tune speed in the Inspector.)
        if (agent != null && combatStats != null)
        {
            float desired = combatStats.GetMoveSpeed();
            if (!Mathf.Approximately(agent.speed, desired))
                agent.speed = desired;
        }

        // If we were chasing but the enemy was destroyed, stop chasing and resume.
        if (chasing && currentEnemy == null)
        {
            StopChasingAndResume();
        }

        // Acquire / validate target
        if (!chasing)
        {
            TryAcquireEnemy();
        }
        else
        {
            // If enemy ran far enough away, stop chasing and resume.
            if (currentEnemy == null)
            {
                StopChasingAndResume();
            }
            else
            {
                float dist = Vector3.Distance(transform.position, currentEnemy.position);
                if (dist > distanceToLose)
                {
                    StopChasingAndResume();
                }
                else
                {
                    ChaseAndShoot(currentEnemy);
                }
            }
        }

        // Follow travel target when not chasing (e.g., join in-route following a moving team)
        if (!chasing && target != null && agent != null)
        {
            travelFollowTimer -= Time.deltaTime;
            if (travelFollowTimer <= 0f)
            {
                agent.SetDestination(target.position);
                travelFollowTimer = Mathf.Max(0.05f, travelFollowUpdateInterval);
            }
        }

        // Running animation
        // NOTE: NavMeshAgent.velocity can stay ~0 briefly while path is pending / accelerating,
        // which makes the unit "slide" in Idle before switching to Run. Use desiredVelocity + path state instead.
        if (soldierAnimator != null && agent != null)
        {
            bool wantsToMove = !agent.isStopped && !agent.pathPending && agent.hasPath;
            bool farEnough = wantsToMove && agent.remainingDistance > (Mathf.Max(agent.stoppingDistance, 0.05f) + 0.05f);
            bool shouldRun = farEnough && (agent.desiredVelocity.sqrMagnitude > 0.01f || agent.velocity.sqrMagnitude > 0.01f);
            soldierAnimator.SetBool("isRunning", shouldRun);
        }


        // Auto-clear the pinned move marker when we arrive at our pinned destination (fixed-point moves).
        // This avoids leaving move pins behind after the ally finishes moving.
        if (autoClearMoveMarkerOnArrival && !chasing && target == null && agent != null && !agent.pathPending)
        {
            // Only clear when we actually have a pinned destination for this unit.
            if (TryGetLatestPinnedDestination(out Vector3 pinnedDest))
            {
                float arriveDist = Mathf.Max(agent.stoppingDistance, 0.05f) + Mathf.Max(0f, markerArrivalBuffer);
                float d = Vector3.Distance(transform.position, pinnedDest);

                // We also require the agent to have effectively stopped to avoid clearing too early.
                bool stopped = agent.velocity.sqrMagnitude < 0.01f && agent.desiredVelocity.sqrMagnitude < 0.01f;
                if (d <= arriveDist && stopped)
                {
                    TryClearPinnedMoveMarker();
                }
            }
        }


        // Update pinned state (v1: pinned while chasing).
        if (pinnedStatus != null)
            pinnedStatus.SetPinned(chasing);
    }

    private void TryAcquireEnemy()
    {
        // Find nearest enemy within distanceToChase
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        Transform best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < enemies.Length; i++)
        {
            var go = enemies[i];
            if (go == null) continue;

            float d = Vector3.Distance(transform.position, go.transform.position);
            if (d < distanceToChase && d < bestDist)
            {
                bestDist = d;
                best = go.transform;
            }
        }

        if (best != null)
        {
            // entering chase: remember where we were going before combat
            resumeWasFollowing = (target != null);
            if (agent != null)
            {
                resumeDestination = agent.destination;
                hasResumeDestination = true;
            }

            chasing = true;
            currentEnemy = best;
        }
    }

    private void StopChasingAndResume()
    {
        chasing = false;
        currentEnemy = null;

        // If we were following a dynamic target (e.g., joining a moving team),
        // don't restore a stale point destination; the follow logic below will resume naturally.
        if (resumeWasFollowing && target != null)
        {
            hasResumeDestination = false;
            return;
        }

        if (agent == null) { hasResumeDestination = false; return; }

        // ✅ KEY FIX:
        // If this unit has a pinned move destination (team/ally move pins), resume to the LATEST pinned destination.
        // This solves: "ally resumes to original team spot after combat even if team destination was updated".
        if (TryGetLatestPinnedDestination(out Vector3 pinnedDest))
        {
            agent.isStopped = false;
            agent.SetDestination(pinnedDest);
            hasResumeDestination = false;
            return;
        }

        // Otherwise, resume the previously commanded destination (e.g., move-to-point).
        if (hasResumeDestination)
        {
            agent.isStopped = false;
            agent.SetDestination(resumeDestination);
            hasResumeDestination = false;
        }
    }

    private bool TryGetLatestPinnedDestination(out Vector3 dest)
    {
        dest = default;

        // If your marker system isn't present, nothing to do.
        if (MoveDestinationMarkerSystem.Instance == null) return false;

        var inst = MoveDestinationMarkerSystem.Instance;
        var type = inst.GetType();

        // 1) If a public method exists, prefer it (future-proof).
        // We try common names without requiring you to change other files.
        // bool TryGetPinnedDestination(Transform unit, out Vector3 destination)
        // bool TryGetDestinationFor(Transform unit, out Vector3 destination)
        try
        {
            MethodInfo mi =
                type.GetMethod("TryGetPinnedDestination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetMethod("TryGetDestinationFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetMethod("TryGetPinnedFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (mi != null)
            {
                object[] args = new object[] { this.transform, dest };
                // out param comes back in args[1]
                object okObj = mi.Invoke(inst, args);
                if (okObj is bool ok && ok)
                {
                    if (args[1] is Vector3 v)
                    {
                        dest = v;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore and fall back to reflection on fields
        }

        // 2) Fallback: reflect private Dictionary<Transform, Pinned> pinnedByUnit and read pinned.destination
        try
        {
            var field = type.GetField("pinnedByUnit", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return false;

            var dictObj = field.GetValue(inst);
            if (dictObj == null) return false;

            // Dictionary<TKey,TValue> implements non-generic IDictionary
            var dict = dictObj as IDictionary;
            if (dict == null) return false;

            if (!dict.Contains(this.transform)) return false;

            var pinned = dict[this.transform];
            if (pinned == null) return false;

            var pType = pinned.GetType();

            // destination is a field in your current MoveDestinationMarkerSystem.Pinned class
            var destField = pType.GetField("destination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (destField != null && destField.FieldType == typeof(Vector3))
            {
                dest = (Vector3)destField.GetValue(pinned);
                return true;
            }

            // or destination as a property
            var destProp = pType.GetProperty("destination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (destProp != null && destProp.PropertyType == typeof(Vector3))
            {
                dest = (Vector3)destProp.GetValue(pinned);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }


    private void TryClearPinnedMoveMarker()
    {
        if (MoveDestinationMarkerSystem.Instance == null) return;

        var inst = MoveDestinationMarkerSystem.Instance;
        var type = inst.GetType();

        // Try a few common method signatures without hard dependencies.
        // Preferred (your project has used this pattern):
        //   ClearForUnits(GameObject[] units)
        // Also supports:
        //   ClearForUnit(Transform unit)
        //   ClearForUnits(Transform[] units)
        //   ClearFor(Transform unit)
        //   Unpin(Transform unit)
        try
        {
            // 1) ClearForUnits(GameObject[])
            var m = type.GetMethod("ClearForUnits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1)
                {
                    if (p[0].ParameterType == typeof(GameObject[]))
                    {
                        m.Invoke(inst, new object[] { new[] { this.gameObject } });
                        return;
                    }
                    if (p[0].ParameterType == typeof(Transform[]))
                    {
                        m.Invoke(inst, new object[] { new[] { this.transform } });
                        return;
                    }
                }
            }

            // 2) ClearForUnit(Transform)
            m = type.GetMethod("ClearForUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }

            // 3) ClearFor(Transform)
            m = type.GetMethod("ClearFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }

            // 4) Unpin(Transform)
            m = type.GetMethod("Unpin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null)
            {
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(Transform))
                {
                    m.Invoke(inst, new object[] { this.transform });
                    return;
                }
            }
        }
        catch
        {
            // If reflection fails, do nothing. Marker will remain until cleared elsewhere.
        }
    }


    private void ChaseAndShoot(Transform enemy)
    {
        if (enemy == null) return;

        Vector3 targetPoint = enemy.position;

        // Move toward enemy (stop close)
        if (agent != null)
        {
            if (Vector3.Distance(transform.position, targetPoint) > distanceToStop)
                agent.destination = targetPoint;
            else
                agent.destination = transform.position;
        }

        // Fire
        fireCount -= Time.deltaTime;
        if (fireCount > 0) return;

        fireCount = fireRate;

        if (firePoint != null)
            firePoint.LookAt(targetPoint + new Vector3(0f, 0.5f, 0f));

        Vector3 targetDir = targetPoint - transform.position;
        float angle = Vector3.SignedAngle(targetDir, transform.forward, Vector3.up);

        if (Mathf.Abs(angle) < 30f)
        {
            if (bullet != null && firePoint != null)
            {
                GameObject spawned = Instantiate(bullet, firePoint.position, firePoint.rotation);

                // Set bullet damage from AllyCombatStats (team size scaling).
                if (combatStats != null)
                {
                    BulletController bc = spawned.GetComponent<BulletController>();
                    if (bc != null)
                        bc.Damage = combatStats.GetDamageInt();
                }
            }

            if (soldierAnimator != null)
                soldierAnimator.SetTrigger("Shoot");
        }
    }
}
