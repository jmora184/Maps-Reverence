using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading;
using UnityEngine.AI;

public class level1Controller : MonoBehaviour
{

    private UnityEngine.AI.NavMeshAgent nav;
    public UnityEngine.AI.NavMeshAgent agent;
    List<string> enemyList = new List<string>();
    GameObject enemySoldier;
    GameObject enemy;
    public bool moving = true;


    void Start()
    {

  

    }

    // Update is called once per frame
    void Update()
    {
        enemy = GameObject.Find("Enemy");
        GameObject enemy2 = GameObject.Find("Enemy5");
        enemyList.Add("Enemy4");
        enemyList.Add("Enemy5");
        //run(army1, army2);
        MiniUI.instance.test.SetActive(false);
        if (moving)
        {
            foreach (var x in enemyList)
            {
                enemySoldier = GameObject.Find(x);

                nav = enemySoldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                //nav.SetDestination(secondSoldier.transform.position);
                nav.destination = enemy.transform.position;
                if (Vector3.Distance(enemySoldier.transform.position, enemy.transform.position) < 2f)
                {
                    Debug.Log("true");
                    GameObject arrow = GameObject.Find("Enemy5");
                    GameObject arrow2 = arrow.transform.Find("Arrow").gameObject;
                    arrow2.SetActive(false);
                    nav.destination = enemySoldier.transform.position;
                    moving = false;

                }
                else
                {
                    nav.destination = enemy.transform.position;
                }

            }
        }

    }
}
