// 1/4/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;
using UnityEngine.AI;

public class AllyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    public Rigidbody theRB;
    public Transform target; // (unused legacy)
    private bool chasing;
    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;
    public NavMeshAgent agent;

    [Header("Combat")]
    public GameObject bullet;
    public Transform firePoint;
    public float fireRate = 0.5f;
    private float fireCount;

    [Header("Animation")]
    public Animator soldierAnimator;

    // Per-ally stats (team size -> multipliers)
    private AllyCombatStats combatStats;

    // Pinned state (v1 driven by 'chasing')
    private AllyPinnedStatus pinnedStatus;

    // NEW: track one enemy target so we don't fight every enemy in the scene at once
    private Transform currentEnemy;

    // NEW: remember where we were going before we got pulled into combat
    private Vector3 resumeDestination;
    private bool hasResumeDestination;

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
        // Initialize agent speed from AllyCombatStats if present,
        // otherwise fall back to the legacy moveSpeed field.
        if (agent != null)
        {
            if (combatStats != null)
            {
                if (combatStats.baseMoveSpeed <= 0f && moveSpeed > 0f)
                    combatStats.baseMoveSpeed = moveSpeed;

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
        // Keep agent speed in sync with team size (cheap + simple).
        if (agent != null && combatStats != null)
            agent.speed = combatStats.GetMoveSpeed();

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

        // Running animation
        if (soldierAnimator != null && agent != null)
            soldierAnimator.SetBool("isRunning", agent.velocity.magnitude > 0.1f);

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

        // Resume the previously commanded destination (e.g., moving to join the team).
        if (agent != null && hasResumeDestination)
        {
            agent.destination = resumeDestination;
            hasResumeDestination = false;
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
