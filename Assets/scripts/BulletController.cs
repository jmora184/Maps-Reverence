using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BulletController : MonoBehaviour
{
    public float moveSpeed, lifeTime;

    public Rigidbody theRB;

    public GameObject impactEffect;

    public int Damage = 2;

    public bool damageEnemy, damagePlayer;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    //comp
    void Update()
    {
        theRB.velocity = transform.forward * moveSpeed;

        lifeTime -= Time.deltaTime;
        if (lifeTime <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {

        if (other.gameObject.tag == "Enemy" && damageEnemy)
        {
            //Destroy(other.gameObject);
            other.gameObject.GetComponent<EnemyHealthController>().DamageEnemy(Damage);
        }

        if (other.gameObject.tag == "HeadShot" && damageEnemy)
        {
            //Destroy(other.gameObject);
            other.transform.parent.GetComponent<EnemyHealthController>().DamageEnemy(Damage+2);
            Debug.Log("headshot");
        }

        if (other.gameObject.tag=="Player" && damagePlayer)
        {
            Debug.Log("hit player at" + transform.position);
            PlayerHealthController.instance.DamagePlayer(Damage);
        }
        Destroy(gameObject);
        Instantiate(impactEffect, transform.position + (transform.forward * (-moveSpeed) * Time.deltaTime), transform.rotation);
    }
}
