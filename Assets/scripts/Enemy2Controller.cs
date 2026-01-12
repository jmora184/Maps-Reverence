using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Enemy2Controller : MonoBehaviour
{
    private bool chasing;

    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;

    private Vector3 targetPoint, startPoint;
    public float keepChasingTime = 5f;
    private float chaseCounter;
    public NavMeshAgent agent;

    public GameObject bullet;
    public Transform firePoint;

    public float fireRate, waitBetweenShots = 2f, timeToShoot = 1f;
    private float fireCount, shotWaitCounter, shootTimeCounter;

    public Animator anim;
    private bool wasShot;

    public GameObject directionSprite;

    // NEW: current target
    private Transform currentTarget;

    void Start()
    {
        startPoint = transform.position;
        shootTimeCounter = timeToShoot;
        shotWaitCounter = waitBetweenShots;

        if (directionSprite != null)
            directionSprite.SetActive(false);
    }

    void Update()
    {
        // NEW: choose player or closest ally
        currentTarget = FindClosestTarget();
        if (currentTarget == null) return;

        targetPoint = currentTarget.position;
        targetPoint.y = transform.position.y;

        if (!chasing)
        {
            if (Vector3.Distance(transform.position, targetPoint) < distanceToChase)
            {
                chasing = true;

                fireCount = 1f;
                shootTimeCounter = timeToShoot;
                shotWaitCounter = waitBetweenShots;
            }

            if (chaseCounter > 0)
            {
                chaseCounter -= Time.deltaTime;

                if (chaseCounter <= 0)
                    agent.destination = startPoint;
            }

            if (agent.remainingDistance < .25f)
            {
                anim.SetBool("isMoving", false);
                if (directionSprite != null) directionSprite.SetActive(false);
            }
            else
            {
                anim.SetBool("isMoving", true);
                if (directionSprite != null && Input.GetKeyDown(KeyCode.Tab))
                    directionSprite.SetActive(true);
            }
        }
        else
        {
            if (Vector3.Distance(transform.position, targetPoint) > distanceToStop)
            {
                agent.destination = targetPoint;
                if (directionSprite != null && Input.GetKeyDown(KeyCode.Tab))
                    directionSprite.SetActive(true);
            }
            else
            {
                agent.destination = transform.position;
                if (directionSprite != null) directionSprite.SetActive(false);
            }

            if (Vector3.Distance(transform.position, targetPoint) > distanceToLose)
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

            if (shotWaitCounter > 0)
            {
                shotWaitCounter -= Time.deltaTime;
                if (shotWaitCounter <= 0)
                    shootTimeCounter = timeToShoot;

                anim.SetBool("isMoving", true);
                if (directionSprite != null && Input.GetKeyDown(KeyCode.Tab))
                    directionSprite.SetActive(true);
            }
            else
            {
                // shoot at current target (player OR ally)
                if (currentTarget != null && currentTarget.gameObject.activeInHierarchy)
                {
                    shootTimeCounter -= Time.deltaTime;

                    if (shootTimeCounter > 0)
                    {
                        fireCount -= Time.deltaTime;

                        if (fireCount <= 0)
                        {
                            fireCount = fireRate;

                            firePoint.LookAt(currentTarget.position + new Vector3(0f, 0.5f, 0f));

                            // If you later instantiate bullets, do it here.
                            // Example:
                            // Instantiate(bullet, firePoint.position, firePoint.rotation);
                        }
                    }
                }
            }
        }
    }

    // NEW: closest of (player + all allies)
    private Transform FindClosestTarget()
    {
        Transform best = null;
        float bestDist = float.MaxValue;

        // Player
        if (Player2Controller.instance != null && Player2Controller.instance.gameObject.activeInHierarchy)
        {
            float d = Vector3.Distance(transform.position, Player2Controller.instance.transform.position);
            best = Player2Controller.instance.transform;
            bestDist = d;
        }

        // Allies
        var allies = GameObject.FindGameObjectsWithTag("Ally");
        foreach (var a in allies)
        {
            if (a == null || !a.activeInHierarchy) continue;

            float d = Vector3.Distance(transform.position, a.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = a.transform;
            }
        }

        return best;
    }
}
