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

    [Header("Explosion Audio (Optional)")]
    [Tooltip("Simple explosion clip option. If assigned, this script will spawn a temporary AudioSource at the explosion point.")]
    public AudioClip explosionSFX;

    [Range(0f, 2f)]
    public float explosionVolume = 1f;

    [Tooltip("If both are assigned, prefer the simple clip over the audio source prefab.")]
    public bool preferExplosionClipOverPrefab = true;

    [Header("Damage Falloff")]
    public bool useDamageFalloff = true;
    public float minimumDamageMultiplier = 0.25f;

    [Header("Damage By Target")]
    [Tooltip("Multiplier applied only when explosion damages PlayerVitals. 0.25 = player takes 75% less damage.")]
    public float playerDamageMultiplier = 0.25f;

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

        PlayExplosionSound(center);

        Collider[] hits = Physics.OverlapSphere(center, radius, hitMask, QueryTriggerInteraction.Collide);
        HashSet<int> processedTargets = new HashSet<int>();

        foreach (Collider hit in hits)
        {
            if (hit == null) continue;

            UnityEngine.Object damageKey = ResolveDamageTargetKey(hit);
            if (damageKey == gameObject) continue;

            int keyId = damageKey != null ? damageKey.GetInstanceID() : hit.GetInstanceID();
            if (!processedTargets.Add(keyId)) continue;

            float distance = Vector3.Distance(center, ClosestPointOrTransform(hit, center));
            float appliedDamage = CalculateDamage(distance);
            int damageInt = Mathf.Max(1, Mathf.RoundToInt(appliedDamage));
            int adjustedDamage = AdjustDamageForTarget(hit, damageInt);

            if (debugLogs)
            {
                string targetName = damageKey != null ? damageKey.name : hit.name;
                Debug.Log($"[GrenadeProjectile] Hit collider '{hit.name}', targetKey '{targetName}', distance {distance:F2}, damage {damageInt}, adjustedDamage {adjustedDamage}", hit);
            }

            bool damaged = TryApplyDamage(hit, adjustedDamage);

            if (debugLogs && !damaged)
            {
                string targetName = damageKey != null ? damageKey.name : hit.name;
                Debug.LogWarning($"[GrenadeProjectile] No supported damage receiver found from collider '{hit.name}' for target '{targetName}'", hit);
            }

            Rigidbody hitRb = hit.attachedRigidbody;
            if (hitRb != null && !hitRb.isKinematic)
            {
                hitRb.AddExplosionForce(explosionForce, center, radius, upwardsModifier, ForceMode.Impulse);
            }
        }

        Destroy(gameObject);
    }

    private void PlayExplosionSound(Vector3 center)
    {
        if (preferExplosionClipOverPrefab && explosionSFX != null)
        {
            SpawnOneShotAudio(center, explosionSFX, explosionVolume);
            return;
        }

        if (explosionAudioSourcePrefab != null)
        {
            AudioSource spawnedAudio = Instantiate(explosionAudioSourcePrefab, center, Quaternion.identity);
            if (spawnedAudio != null)
            {
                if (spawnedAudio.clip != null && !spawnedAudio.isPlaying)
                    spawnedAudio.Play();

                Destroy(spawnedAudio.gameObject, spawnedAudio.clip != null ? spawnedAudio.clip.length + 0.25f : 2f);
            }
            return;
        }

        if (explosionSFX != null)
        {
            SpawnOneShotAudio(center, explosionSFX, explosionVolume);
        }
    }

    private void SpawnOneShotAudio(Vector3 position, AudioClip clip, float volume)
    {
        if (clip == null) return;

        GameObject audioGO = new GameObject("GrenadeExplosionAudio");
        audioGO.transform.position = position;

        AudioSource source = audioGO.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.playOnAwake = false;
        source.loop = false;

        source.Play();
        Destroy(audioGO, clip.length + 0.25f);
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

    private int AdjustDamageForTarget(Collider hit, int amount)
    {
        if (amount <= 0) amount = 1;

        PlayerVitals playerVitals = hit.GetComponent<PlayerVitals>() ?? hit.GetComponentInParent<PlayerVitals>();
        if (playerVitals != null)
        {
            float scaled = amount * Mathf.Clamp01(playerDamageMultiplier);
            return Mathf.Max(1, Mathf.RoundToInt(scaled));
        }

        return amount;
    }

    private UnityEngine.Object ResolveDamageTargetKey(Collider hit)
    {
        if (hit == null) return null;

        // Prefer concrete health components so each enemy is damaged once even if it has multiple colliders.
        PlayerVitals playerVitals = hit.GetComponent<PlayerVitals>() ?? hit.GetComponentInParent<PlayerVitals>();
        if (playerVitals != null) return playerVitals;

        AllyHealth allyHealth = hit.GetComponent<AllyHealth>() ?? hit.GetComponentInParent<AllyHealth>();
        if (allyHealth != null) return allyHealth;

        EnemyHealthController enemyHealth = hit.GetComponent<EnemyHealthController>() ?? hit.GetComponentInParent<EnemyHealthController>();
        if (enemyHealth != null) return enemyHealth;

        MeleeEnemyHealthController meleeHealth = hit.GetComponent<MeleeEnemyHealthController>() ?? hit.GetComponentInParent<MeleeEnemyHealthController>();
        if (meleeHealth != null) return meleeHealth;

        MonoBehaviour damageBehaviour = FindDamageBehaviour(hit.gameObject);
        if (damageBehaviour != null) return damageBehaviour;

        if (hit.attachedRigidbody != null)
        {
            damageBehaviour = FindDamageBehaviour(hit.attachedRigidbody.gameObject);
            if (damageBehaviour != null) return damageBehaviour;
        }

        Transform t = hit.transform.parent;
        while (t != null)
        {
            damageBehaviour = FindDamageBehaviour(t.gameObject);
            if (damageBehaviour != null) return damageBehaviour;
            t = t.parent;
        }

        if (hit.attachedRigidbody != null) return hit.attachedRigidbody;
        return hit.gameObject;
    }

    private MonoBehaviour FindDamageBehaviour(GameObject go)
    {
        if (go == null) return null;

        MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;

            Type type = behaviour.GetType();
            if (HasDamageMethod(type, "TakeDamage")) return behaviour;
            if (HasDamageMethod(type, "ApplyDamage")) return behaviour;
            if (HasDamageMethod(type, "Damage")) return behaviour;
            if (HasDamageMethod(type, "ReceiveDamage")) return behaviour;
            if (HasDamageMethod(type, "DamageAlly")) return behaviour;
            if (HasDamageMethod(type, "DamageEnemy")) return behaviour;
        }

        return null;
    }

    private bool HasDamageMethod(Type type, string methodName)
    {
        return type.GetMethod(methodName, new[] { typeof(int) }) != null ||
               type.GetMethod(methodName, new[] { typeof(float) }) != null;
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
