using UnityEngine;

/// <summary>
/// BulletController (collision/trigger based)
///
/// Robust damage routing for MnR + BACKWARD COMPATIBILITY:
/// - Some of your scripts (e.g., AllyController) set bullet.Damage.
///   This file includes a public float Damage field + property wrapper so old code still compiles.
///
/// Damage routing:
/// - ENEMY damage: if EnemyHealthController exists in parents -> DamageEnemy(int)
/// - ALLY damage: if AllyHealth exists in parents -> DamageAlly(int)
/// - Fallback: optional tag + SendMessage for custom setups
///
/// Notes:
/// - Targets (enemy/ally) MUST have colliders to receive hits.
/// </summary>
[DisallowMultipleComponent]
public class BulletController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 40f;
    public float lifeTime = 12f;

    [Tooltip("Optional; if left empty we'll auto-grab the Rigidbody on this object.")]
    public Rigidbody theRB;

    [Header("Impact")]
    public GameObject impactEffect;
    public float impactSurfaceOffset = 0.03f;
    public bool alignToCollisionNormal = true;

    [Header("Damage")]
    [Tooltip("Base damage before headshot multiplier. (Legacy field name also exists: Damage)")]
    public float baseDamage = 2f;

    [Tooltip("Multiply damage when hitting a head collider.")]
    public float headshotMultiplier = 2f;

    [Tooltip("If true, bullets can damage enemies.")]
    public bool damageEnemy = true;

    [Tooltip("If true, bullets can damage allies/players.")]
    public bool damageAlly = true;

    [Header("Legacy Compatibility")]
    [Tooltip("Legacy field used by older scripts (e.g., AllyController). Keep this in sync with baseDamage.")]
    public float Damage = 2f;

    /// <summary>
    /// Legacy property access (some code might use bullet.Damage as property in other versions).
    /// </summary>
    public float DamageAmount
    {
        get => baseDamage;
        set
        {
            baseDamage = value;
            Damage = value;
        }
    }

    [Header("Optional Tag Filtering (fallback)")]
    public string enemyTag = "Enemy";
    public string allyTag = "Ally";
    public string playerTag = "Player";

    [Header("Fallback Ally Damage Message")]
    [Tooltip("Only used if AllyHealth isn't found. Example: TakeDamage, ApplyDamage, DamagePlayer.")]
    public string allyDamageMessageName = "DamageAlly";

    [Header("Headshot Detection")]
    public string headshotTag = "HeadShot";
    public string headshotName = "HeadShot";

    float _spawnTime;

    private void Awake()
    {
        if (theRB == null)
            theRB = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        _spawnTime = Time.time;

        // Ensure legacy Damage stays synced when prefab overrides are present
        if (Mathf.Abs(Damage - baseDamage) > 0.0001f)
            baseDamage = Damage;

        if (theRB != null)
        {
            theRB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            theRB.interpolation = RigidbodyInterpolation.Interpolate;
            theRB.isKinematic = false;
            theRB.useGravity = false;
        }
    }

    private void FixedUpdate()
    {
        if (theRB != null)
        {
            // Unity 6: linearVelocity exists; velocity works too.
            theRB.linearVelocity = transform.forward * moveSpeed;
        }
        else
        {
            transform.position += transform.forward * moveSpeed * Time.fixedDeltaTime;
        }

        if (Time.time - _spawnTime >= lifeTime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        HandleHit(other, transform.position, -transform.forward);
        Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;

        Collider hit = collision.collider;

        Vector3 point = transform.position;
        Vector3 normal = -transform.forward;

        if (collision.contactCount > 0)
        {
            ContactPoint cp = collision.GetContact(0);
            point = cp.point;
            normal = cp.normal;
        }

        HandleHit(hit, point, normal);
        Destroy(gameObject);
    }

    private void HandleHit(Collider hit, Vector3 point, Vector3 normal)
    {
        if (hit == null) return;

        // Keep legacy Damage synced (in case some spawner sets Damage at runtime)
        if (Mathf.Abs(Damage - baseDamage) > 0.0001f)
            baseDamage = Damage;

        // Compute damage (float + int)
        float dmgFloat = ComputeDamageFloat(hit);
        int dmgInt = Mathf.Max(1, Mathf.RoundToInt(dmgFloat));

        // Prefer component-based detection (most reliable)
        EnemyHealthController enemyHealth = hit.GetComponentInParent<EnemyHealthController>();
        AllyHealth allyHealth = hit.GetComponentInParent<AllyHealth>();

        bool isEnemy = enemyHealth != null || HasTagInParents(hit, enemyTag);
        bool isAlly = allyHealth != null || HasTagInParents(hit, allyTag) || HasTagInParents(hit, playerTag);

        if (isEnemy && damageEnemy)
        {
            if (enemyHealth != null)
            {
                enemyHealth.DamageEnemy(dmgInt);
            }
            else
            {
                // fallback if you have some other enemy script
                hit.SendMessageUpwards("DamageEnemy", dmgInt, SendMessageOptions.DontRequireReceiver);
                hit.SendMessageUpwards("TakeDamage", dmgFloat, SendMessageOptions.DontRequireReceiver);
            }
        }
        else if (isAlly && damageAlly)
        {
            if (allyHealth != null)
            {
                allyHealth.DamageAlly(dmgInt);
            }
            else if (!string.IsNullOrEmpty(allyDamageMessageName))
            {
                // fallback for custom ally/player health scripts
                hit.SendMessageUpwards(allyDamageMessageName, dmgInt, SendMessageOptions.DontRequireReceiver);
                hit.SendMessageUpwards("TakeDamage", dmgFloat, SendMessageOptions.DontRequireReceiver);
            }
        }

        // FX (optional): spawn only when hitting a character
        if (impactEffect != null && (isEnemy || isAlly))
        {
            Vector3 p = hit.ClosestPoint(transform.position);
            SpawnImpact(p, normal);
        }
    }

    private float ComputeDamageFloat(Collider hit)
    {
        float dmg = Mathf.Max(0.01f, baseDamage);
        if (IsHeadshot(hit))
            dmg *= Mathf.Max(1f, headshotMultiplier);
        return dmg;
    }

    private bool IsHeadshot(Collider hit)
    {
        if (hit == null) return false;

        if (!string.IsNullOrEmpty(headshotTag))
        {
            if (hit.CompareTag(headshotTag)) return true;

            Transform t = hit.transform;
            int safety = 0;
            while (t != null && safety++ < 16)
            {
                if (t.CompareTag(headshotTag)) return true;
                t = t.parent;
            }
        }

        if (!string.IsNullOrEmpty(headshotName))
            return hit.gameObject.name == headshotName;

        return false;
    }

    private bool HasTagInParents(Collider hit, string tag)
    {
        if (hit == null) return false;
        if (string.IsNullOrEmpty(tag)) return false;

        if (hit.CompareTag(tag)) return true;

        Transform t = hit.transform;
        int safety = 0;
        while (t != null && safety++ < 16)
        {
            if (t.CompareTag(tag)) return true;
            t = t.parent;
        }
        return false;
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : -transform.forward;
        float offset = Mathf.Max(0.001f, impactSurfaceOffset);
        Vector3 spawnPos = point + n * offset;

        Vector3 forward = alignToCollisionNormal ? n : (-transform.forward);
        if (forward.sqrMagnitude < 0.0001f) forward = n;

        Quaternion rot = Quaternion.LookRotation(forward);
        Instantiate(impactEffect, spawnPos, rot);
    }
}
