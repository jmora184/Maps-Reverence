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
        theRB.linearVelocity = transform.forward * moveSpeed;

        lifeTime -= Time.deltaTime;
        if (lifeTime <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {

        Debug.Log("BULLET HIT: " + other.name + " tag=" + other.tag + " layer=" + other.gameObject.layer);
        // Damage ENEMY
        if (damageEnemy && other.CompareTag("Enemy"))
        {
            var eh = other.GetComponentInParent<EnemyHealthController>();
            if (eh != null) eh.DamageEnemy(Damage);
        }

        // Headshot (enemy child collider)
        if (damageEnemy && other.CompareTag("HeadShot"))
        {
            var eh = other.GetComponentInParent<EnemyHealthController>();
            if (eh != null) eh.DamageEnemy(Damage + 2);
            Debug.Log("headshot");
        }

        // Damage ALLY
        if (damageEnemy && other.CompareTag("Ally"))
        {
            var ah = other.GetComponentInParent<AllyHealth>();
            if (ah != null) ah.DamageAlly(Damage);
        }

        // Damage PLAYER
        if (damagePlayer && other.CompareTag("Player"))
        {
            PlayerHealthController.instance.DamagePlayer(Damage);
        }

        Destroy(gameObject);
        Instantiate(impactEffect, transform.position, transform.rotation);
    }
}
