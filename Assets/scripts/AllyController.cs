// 1/4/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AllyController : MonoBehaviour
{
    public float moveSpeed;
    public Rigidbody theRB;
    public Transform target;
    private bool chasing;
    public float distanceToChase = 10f, distanceToLose = 15f, distanceToStop = 2f;
    public NavMeshAgent agent;
    public float keepChasingTime = 2f;
    private float chaseCounter;

    public GameObject bullet;
    public Transform firePoint;

    public float fireRate;
    private float fireCount;
    private GameObject[] objs;
    private Vector3 targetPoint, startPoint;

    // Reference to the Animator component
    public Animator soldierAnimator;

    void Start()
    {
        startPoint = transform.position;
    }

    void Update()
    {
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
                    if (chaseCounter <= 0)
                    {
                        agent.destination = startPoint;
                    }
                }
            }
            else
            {
                if (Vector3.Distance(transform.position, targetPoint) > distanceToStop)
                {
                    agent.destination = targetPoint;
                }
                else
                {
                    agent.destination = transform.position;
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

                    firePoint.LookAt(targetPoint + new Vector3(0f, 0.5f, 0f));

                    // Check the angle to the enemy
                    Vector3 targetDir = targetPoint - transform.position;
                    float angle = Vector3.SignedAngle(targetDir, transform.forward, Vector3.up);
                    if (Mathf.Abs(angle) < 30f)
                    {
                        Instantiate(bullet, firePoint.position, firePoint.rotation);
                        soldierAnimator.SetTrigger("Shoot"); // Trigger shooting animation
                    }
                }
            }

            // Check if the agent is moving and update the running animation
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
}