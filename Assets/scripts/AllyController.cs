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
    // Start is called before the first frame update
    public NavMeshAgent agent;
    public float keepChasingTime = 2f;
    private float chaseCounter;

    public GameObject bullet;
    public Transform firePoint;

    public float fireRate;
    private float fireCount;
    private GameObject[] objs;
    private Vector3 targetPoint, startPoint;

    void Start()
    {
        startPoint = transform.position;
    }

    // Update is called once per frame
    void Update()
    {

        //targetPoint = Player2Controller.instance.transform.position;      

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
                //transform.LookAt(Player2Controller.instance.transform.position);
                //theRB.velocity = transform.forward * moveSpeed;
                if (Vector3.Distance(transform.position, targetPoint) > distanceToStop)
                {
                    agent.destination = targetPoint;
                }
                else
                {
                    agent.destination = transform.position;
                }

                if (Vector3.Distance(transform.position, targetPoint) < distanceToLose)
                {
                    chasing = false;
                    chaseCounter = keepChasingTime;
                }

                fireCount -= Time.deltaTime;

                if (fireCount <= 0)
                {
                    fireCount = fireRate;

                    Instantiate(bullet, firePoint.position, firePoint.rotation);
                }
            }
        }


        //if (!chasing)
        //{
        //if(Vector3.Distance(transform.position, targetPoint) < distanceToChase)
        //{
        //    chasing = true;

        //    //fireCount = 1f;
        //}

        //if(chaseCounter > 0)
        //{
        //    chaseCounter -= Time.deltaTime;

        //    if (chaseCounter <= 0)
        //    {
        //        agent.destination = startPoint;
        //    }
        //}

        //}
    }
}
