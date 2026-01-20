using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
    private bool chasing;

    [Header("Detection")]
    public float distanceToChase = 10f;
    public float distanceToLose = 15f;

    [Header("Combat Range")]
    [Tooltip("Preferred distance to keep from the current combat target while fighting.")]
    public float desiredAttackRange = 6f;

    [Tooltip("Small buffer around desiredAttackRange to prevent jitter (hysteresis).")]
    public float attackRangeBuffer = 0.75f;

    [Tooltip("NavMesh sample radius used when backing away from the target.")]
    public float backoffSampleRadius = 2.5f;

    [Tooltip("How quickly the enemy turns to face the target while fighting.")]
    public float faceTargetTurnSpeed = 10f;

    [Header("Chase Memory")]
    public float keepChasingTime = 5f;

    [Header("Refs")]
    public NavMeshAgent agent;
    public Animator anim;

    [Header("Shooting")]
    public GameObject bullet;
    public Transform firePoint;
    public float fireRate = 0.15f;
    public float waitBetweenShots = 2f;
    public float timeToShoot = 1f;

    private Vector3 startPoint;
    private float chaseCounter;

    private float fireCount;
    private float shotWaitCounter;
    private float shootTimeCounter;

    private bool wasShot;

    // NEW: allows enemy to fight allies OR the player.
    // If null, we fall back to the player.
    [Header("Target")]
    public Transform combatTarget;

    private void Start()
    {
        startPoint = transform.position;
        shootTimeCounter = timeToShoot;
        shotWaitCounter = waitBetweenShots;

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        // IMPORTANT: we rotate manually for aiming; avoid fighting the agent's auto-rotation.
        if (agent != null)
            agent.updateRotation = false;
    }

    private void Update()
    {
        Transform targetT = ResolveTarget();

        // If we lost our target (destroyed), stop combat cleanly.
        if (targetT == null)
        {
            ExitChaseAndReturnHome();
            return;
        }

        Vector3 targetPoint = targetT.position;
        targetPoint.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, targetPoint);

        if (!chasing)
        {
            if (dist < distanceToChase)
            {
                EnterChase();
            }

            if (chaseCounter > 0f)
            {
                chaseCounter -= Time.deltaTime;
                if (chaseCounter <= 0f && agent != null)
                {
                    agent.isStopped = false;
                    agent.SetDestination(startPoint);
                }
            }

            UpdateMoveAnimFromAgent();
            return;
        }

        // CHASING / COMBAT
        MaintainStandoff(targetPoint, dist);

        // Lose logic
        if (dist > distanceToLose)
        {
            if (!wasShot)
            {
                chasing = false;
                chaseCounter = keepChasingTime;
            }
        }
        else
        {
            wasShot = false;
        }

        // Shooting logic (kept close to your original timers)
        if (shotWaitCounter > 0f)
        {
            shotWaitCounter -= Time.deltaTime;
            if (shotWaitCounter <= 0f)
                shootTimeCounter = timeToShoot;

            // We might still be "in range" and stopped; animation is handled by UpdateMoveAnimFromAgent()
        }
        else
        {
            if (targetT.gameObject.activeInHierarchy)
            {
                shootTimeCounter -= Time.deltaTime;

                if (shootTimeCounter > 0f)
                {
                    // While shooting, we should be stopped (no hugging/pushing).
                    if (agent != null)
                    {
                        agent.isStopped = true;
                        agent.ResetPath();
                    }

                    fireCount -= Time.deltaTime;
                    if (fireCount <= 0f)
                    {
                        fireCount = fireRate;

                        if (firePoint != null)
                            firePoint.LookAt(targetT.position + new Vector3(0f, 0.5f, 0f));

                        // Only fire if we are roughly facing the target
                        Vector3 targetDir = targetT.position - transform.position;
                        float angle = Vector3.SignedAngle(targetDir, transform.forward, Vector3.up);
                        if (Mathf.Abs(angle) < 30f)
                        {
                            if (bullet != null && firePoint != null)
                                Instantiate(bullet, firePoint.position, firePoint.rotation);

                            if (anim != null)
                                anim.SetTrigger("fireShot");
                        }
                        else
                        {
                            // If we can't face target, wait before trying again.
                            shotWaitCounter = waitBetweenShots;
                        }
                    }
                }
                else
                {
                    shotWaitCounter = waitBetweenShots;
                }
            }
        }

        UpdateMoveAnimFromAgent();
    }

    private Transform ResolveTarget()
    {
        if (combatTarget != null)
            return combatTarget;

        // Default fallback: player
        if (playerController.instance != null)
            return playerController.instance.transform;

        return null;
    }

    private void EnterChase()
    {
        chasing = true;
        fireCount = 1f;
        shootTimeCounter = timeToShoot;
        shotWaitCounter = waitBetweenShots;
    }

    private void ExitChaseAndReturnHome()
    {
        chasing = false;
        combatTarget = null;

        if (agent != null)
        {
            agent.isStopped = false;
            agent.ResetPath();
            agent.SetDestination(startPoint);
        }

        UpdateMoveAnimFromAgent();
    }

    private void MaintainStandoff(Vector3 targetPoint, float dist)
    {
        if (agent == null) return;

        float range = Mathf.Max(0.5f, desiredAttackRange);
        if (!Mathf.Approximately(agent.stoppingDistance, range))
            agent.stoppingDistance = range;

        // Face target (flat)
        Vector3 flatDir = targetPoint - transform.position;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * faceTargetTurnSpeed);
        }

        // Too far: approach
        if (dist > range + attackRangeBuffer)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPoint);
            return;
        }

        // Too close: back off
        if (dist < range - attackRangeBuffer)
        {
            Vector3 away = (transform.position - targetPoint);
            away.y = 0f;
            away = away.sqrMagnitude < 0.001f ? transform.forward : away.normalized;

            float push = (range - dist) + 0.5f;
            Vector3 desired = transform.position + away * push;

            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, backoffSampleRadius, NavMesh.AllAreas))
                desired = hit.position;

            agent.isStopped = false;
            agent.SetDestination(desired);
            return;
        }

        // In range: stop (prevents "run into / hugging")
        agent.isStopped = true;
        agent.ResetPath();
    }

    private void UpdateMoveAnimFromAgent()
    {
        if (anim == null || agent == null) return;

        // More reliable than remainingDistance alone
        bool moving = !agent.isStopped && agent.hasPath && agent.remainingDistance > Mathf.Max(0.2f, agent.stoppingDistance + 0.05f);
        anim.SetBool("isMoving", moving);
    }

    public void GetShot()
    {
        wasShot = true;
        chasing = true;
    }

    // Optional helper so other scripts can retarget the enemy (e.g., when an ally shoots it).
    public void SetCombatTarget(Transform t)
    {
        combatTarget = t;
        chasing = true;
        wasShot = true;
        EnterChase();
    }
}
