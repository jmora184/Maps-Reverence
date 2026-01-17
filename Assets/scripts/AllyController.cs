// 1/4/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AllyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    public Rigidbody theRB;
    public Transform target;
    private bool chasing;
    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;
    public NavMeshAgent agent;
    public float keepChasingTime = 2f;
    private float chaseCounter;

    [Header("Combat")]
    public GameObject bullet;
    public Transform firePoint;

    public float fireRate;
    private float fireCount;

    private GameObject[] objs;
    private Vector3 targetPoint, startPoint;

    [Header("Animation")]
    public Animator soldierAnimator;

    // NEW: per-ally stats (team size -> multipliers)
    private AllyCombatStats combatStats;

    // NEW: pinned state (v1 driven by 'chasing')
    private AllyPinnedStatus pinnedStatus;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (theRB == null) theRB = GetComponent<Rigidbody>();
        if (soldierAnimator == null) soldierAnimator = GetComponentInChildren<Animator>();

        combatStats = GetComponent<AllyCombatStats>();
        pinnedStatus = GetComponent<AllyPinnedStatus>();
    }

    void Start()
    {
        startPoint = transform.position;

        // Initialize agent speed from AllyCombatStats if present,
        // otherwise fall back to the legacy moveSpeed field.
        if (agent != null)
        {
            if (combatStats != null)
            {
                // If you were previously tuning moveSpeed in the inspector,
                // use it as baseMoveSpeed (only if baseMoveSpeed isn't already set).
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

    void Update()
    {
        // Keep agent speed in sync with team size (cheap + simple).
        if (agent != null && combatStats != null)
            agent.speed = combatStats.GetMoveSpeed();

        objs = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var x in objs)
        {
            targetPoint = x.transform.position;
            targetPoint.y = x.transform.position.y;

            if (!chasing)
            {
                if (Vector3.Distance(transform.position, targetPoint) < distanceToChase)
                {
                    chasing = true;
                }

                if (chaseCounter > 0)
                {
                    chaseCounter -= Time.deltaTime;
                    if (chaseCounter <= 0 && agent != null)
                    {
                        agent.destination = startPoint;
                    }
                }
            }
            else
            {
                if (agent != null)
                {
                    if (Vector3.Distance(transform.position, targetPoint) > distanceToStop)
                    {
                        agent.destination = targetPoint;
                    }
                    else
                    {
                        agent.destination = transform.position;
                    }
                }

                if (Vector3.Distance(transform.position, targetPoint) > distanceToLose)
                {
                    chasing = false;
                    chaseCounter = keepChasingTime;
                }

                fireCount -= Time.deltaTime;

                if (fireCount <= 0)
                {
                    fireCount = fireRate;

                    if (firePoint != null)
                        firePoint.LookAt(targetPoint + new Vector3(0f, 0.5f, 0f));

                    // Check the angle to the enemy
                    Vector3 targetDir = targetPoint - transform.position;
                    float angle = Vector3.SignedAngle(targetDir, transform.forward, Vector3.up);
                    if (Mathf.Abs(angle) < 30f)
                    {
                        if (bullet != null && firePoint != null)
                        {
                            GameObject spawned = Instantiate(bullet, firePoint.position, firePoint.rotation);

                            // NEW: Set bullet damage from AllyCombatStats (team size scaling).
                            if (combatStats != null)
                            {
                                BulletController bc = spawned.GetComponent<BulletController>();
                                if (bc != null)
                                    bc.Damage = combatStats.GetDamageInt();
                            }
                        }

                        if (soldierAnimator != null)
                            soldierAnimator.SetTrigger("Shoot"); // Trigger shooting animation
                    }
                }
            }

            // Check if the agent is moving and update the running animation
            if (soldierAnimator != null && agent != null)
            {
                if (agent.velocity.magnitude > 0.1f)
                {
                    soldierAnimator.SetBool("isRunning", true); // Start running animation
                }
                else
                {
                    soldierAnimator.SetBool("isRunning", false); // Stop running animation
                }
            }
        }

        // Update pinned state after processing enemies (v1: pinned while chasing).
        if (pinnedStatus != null)
            pinnedStatus.SetPinned(chasing);
    }
}
