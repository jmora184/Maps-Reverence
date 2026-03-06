using System;
using System.Collections.Generic;
using UnityEngine;

public class GrenadeProjectile : MonoBehaviour
{
    [Header("Fuse")]
    public float fuseTime = 2.5f;
    public bool explodeOnDirectImpact = false;
    public float impactMinSpeedToExplode = 10f;

    [Header("Explosion")]
    public float damage = 60f;
    public float radius = 8f;
    public float explosionForce = 750f;
    public float upwardsModifier = 0.25f;
    public LayerMask hitMask = ~0;
    public GameObject explosionVfx;
    public AudioSource explosionAudioSourcePrefab;

    [Header("Damage Falloff")]
    public bool useDamageFalloff = true;
    public float minimumDamageMultiplier = 0.25f;

    [Header("Owner / Collision")]
    [Tooltip("Ignore collision with the owner briefly right after spawn so the grenade does not instantly hit the player while still allowing blast damage later.")]
    public float ignoreOwnerCollisionTime = 0.15f;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool drawDebugRadius = false;

    private Rigidbody rb;
    private bool hasExploded;
    private float spawnTime;

    private GameObject owner;
    private Collider[] ownerColliders;
    private Collider[] myColliders;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        spawnTime = Time.time;

        if (rb == null)
        {
            Debug.LogError("[GrenadeProjectile] Missing Rigidbody on grenade prefab.", this);
        }

        myColliders = GetComponentsInChildren<Collider>(true);
    }

    private void Start()
    {
        Invoke(nameof(Explode), fuseTime);
        RefreshOwnerCollisionIgnore();
    }

    private void Update()
    {
        if (owner != null && ownerColliders != null && Time.time - spawnTime >= ignoreOwnerCollisionTime)
        {
            RestoreOwnerCollisions();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasExploded || rb == null) return;

        if (explodeOnDirectImpact)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed >= impactMinSpeedToExplode && Time.time - spawnTime > 0.05f)
            {
                if (debugLogs)
                {
                    Debug.Log($"[GrenadeProjectile] Direct impact explosion on {collision.collider.name} at speed {impactSpeed:F2}", this);
                }

                Explode();
            }
        }
    }

    public void SetOwner(GameObject newOwner)
    {
        owner = newOwner;
        ownerColliders = owner != null ? owner.GetComponentsInChildren<Collider>(true) : null;
        RefreshOwnerCollisionIgnore();
    }

    private void RefreshOwnerCollisionIgnore()
    {
        if (ownerColliders == null || myColliders == null) return;

        bool ignore = Time.time - spawnTime < ignoreOwnerCollisionTime;

        for (int i = 0; i < myColliders.Length; i++)
        {
            Collider a = myColliders[i];
            if (a == null) continue;

            for (int j = 0; j < ownerColliders.Length; j++)
            {
                Collider b = ownerColliders[j];
                if (b == null) continue;

                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private void RestoreOwnerCollisions()
    {
        if (ownerColliders == null || myColliders == null) return;

        for (int i = 0; i < myColliders.Length; i++)
        {
            Collider a = myColliders[i];
            if (a == null) continue;

            for (int j = 0; j < ownerColliders.Length; j++)
            {
                Collider b = ownerColliders[j];
                if (b == null) continue;

                Physics.IgnoreCollision(a, b, false);
            }
        }

        ownerColliders = null;
    }

    public void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        CancelInvoke(nameof(Explode));

        Vector3 center = transform.position;

        if (debugLogs)
        {
            Debug.Log($"[GrenadeProjectile] Exploding at {center} with radius {radius}", this);
        }

        if (explosionVfx != null)
        {
            Instantiate(explosionVfx, center, Quaternion.identity);
        }

        if (explosionAudioSourcePrefab != null)
        {
            AudioSource spawnedAudio = Instantiate(explosionAudioSourcePrefab, center, Quaternion.identity);
            Destroy(spawnedAudio.gameObject, spawnedAudio.clip != null ? spawnedAudio.clip.length + 0.25f : 2f);
        }

        Collider[] hits = Physics.OverlapSphere(center, radius, hitMask, QueryTriggerInteraction.Collide);
        HashSet<GameObject> processedRoots = new HashSet<GameObject>();

        foreach (Collider hit in hits)
        {
            if (hit == null) continue;

            GameObject processedRoot = hit.transform.root != null ? hit.transform.root.gameObject : hit.gameObject;
            if (processedRoot == gameObject) continue;
            if (!processedRoots.Add(processedRoot)) continue;

            float distance = Vector3.Distance(center, ClosestPointOrTransform(hit, center));
            float appliedDamage = CalculateDamage(distance);
            int damageInt = Mathf.Max(1, Mathf.RoundToInt(appliedDamage));

            if (debugLogs)
            {
                Debug.Log($"[GrenadeProjectile] Hit collider '{hit.name}', processedRoot '{processedRoot.name}', distance {distance:F2}, damage {damageInt}", processedRoot);
            }

            bool damaged = TryApplyDamage(hit, damageInt);

            if (debugLogs && !damaged)
            {
                Debug.LogWarning($"[GrenadeProjectile] No supported damage receiver found from collider '{hit.name}' under root '{processedRoot.name}'", processedRoot);
            }

            Rigidbody hitRb = hit.attachedRigidbody;
            if (hitRb != null && !hitRb.isKinematic)
            {
                hitRb.AddExplosionForce(explosionForce, center, radius, upwardsModifier, ForceMode.Impulse);
            }
        }

        Destroy(gameObject);
    }

    private float CalculateDamage(float distance)
    {
        if (!useDamageFalloff) return damage;

        float t = Mathf.Clamp01(distance / Mathf.Max(radius, 0.001f));
        float multiplier = Mathf.Lerp(1f, minimumDamageMultiplier, t);
        return damage * multiplier;
    }

    private Vector3 ClosestPointOrTransform(Collider col, Vector3 position)
    {
        try
        {
            return col.ClosestPoint(position);
        }
        catch
        {
            return col.transform.position;
        }
    }

    private bool TryApplyDamage(Collider hit, int amount)
    {
        if (amount <= 0) amount = 1;

        // Explicit support for known project scripts first.
        PlayerVitals playerVitals = hit.GetComponent<PlayerVitals>() ?? hit.GetComponentInParent<PlayerVitals>();
        if (playerVitals != null)
        {
            playerVitals.TakeDamage(amount);
            return true;
        }

        AllyHealth allyHealth = hit.GetComponent<AllyHealth>() ?? hit.GetComponentInParent<AllyHealth>();
        if (allyHealth != null)
        {
            allyHealth.DamageAlly(amount);
            return true;
        }

        EnemyHealthController enemyHealth = hit.GetComponent<EnemyHealthController>() ?? hit.GetComponentInParent<EnemyHealthController>();
        if (enemyHealth != null)
        {
            enemyHealth.DamageEnemy(amount);
            return true;
        }

        MeleeEnemyHealthController meleeHealth = hit.GetComponent<MeleeEnemyHealthController>() ?? hit.GetComponentInParent<MeleeEnemyHealthController>();
        if (meleeHealth != null)
        {
            meleeHealth.TakeDamage(amount);
            return true;
        }

        // Generic support for common damage patterns on collider object, rigidbody object, and parent chain.
        if (TryDamageComponentChain(hit.gameObject, amount)) return true;

        Rigidbody attachedRb = hit.attachedRigidbody;
        if (attachedRb != null && TryDamageComponentChain(attachedRb.gameObject, amount)) return true;

        Transform t = hit.transform.parent;
        while (t != null)
        {
            if (TryDamageComponentChain(t.gameObject, amount)) return true;
            t = t.parent;
        }

        return false;
    }

    private bool TryDamageComponentChain(GameObject go, int amount)
    {
        if (go == null) return false;

        MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;

            Type type = behaviour.GetType();

            if (InvokeDamageMethod(type, behaviour, "TakeDamage", amount)) return true;
            if (InvokeDamageMethod(type, behaviour, "ApplyDamage", amount)) return true;
            if (InvokeDamageMethod(type, behaviour, "Damage", amount)) return true;
            if (InvokeDamageMethod(type, behaviour, "ReceiveDamage", amount)) return true;
            if (InvokeDamageMethod(type, behaviour, "DamageAlly", amount)) return true;
            if (InvokeDamageMethod(type, behaviour, "DamageEnemy", amount)) return true;
        }

        return false;
    }

    private bool InvokeDamageMethod(Type type, object target, string methodName, int amount)
    {
        var intMethod = type.GetMethod(methodName, new[] { typeof(int) });
        if (intMethod != null)
        {
            intMethod.Invoke(target, new object[] { amount });
            return true;
        }

        var floatMethod = type.GetMethod(methodName, new[] { typeof(float) });
        if (floatMethod != null)
        {
            floatMethod.Invoke(target, new object[] { (float)amount });
            return true;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugRadius) return;

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
