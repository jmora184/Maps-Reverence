using UnityEngine;

/// <summary>
/// BulletController (collision-based)
/// - Spawns impact FX ONLY when hitting an Enemy (tag "Enemy" on collider or any parent).
/// - Applies headshot multiplier when hitting a head collider (tag or name "HeadShot").
/// 
/// Backward compatibility:
/// - Older scripts (ex: AllyController) set bullet.Damage. We keep that API via a property that maps to baseDamage.
/// </summary>
[DisallowMultipleComponent]
public class BulletController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 175f;
    public float lifeTime = 2.5f;

    [Tooltip("Optional; if left empty we'll auto-grab the Rigidbody on this object.")]
    public Rigidbody theRB;

    [Header("Impact")]
    [Tooltip("Spawned ONLY when we hit an Enemy.")]
    public GameObject impactEffect;

    [Tooltip("Push the impact slightly out from the surface so it doesn't clip inside.")]
    public float impactSurfaceOffset = 0.03f;

    [Tooltip("If true, align the impact effect along the collision normal. If false, use -bulletForward (spray direction).")]
    public bool alignToCollisionNormal = true;

    [Header("Damage")]
    [Tooltip("Base damage for a body shot.")]
    public int baseDamage = 2;

    /// <summary>
    /// Backward compatible API: other scripts may set BulletController.Damage.
    /// This maps to baseDamage.
    /// </summary>
    public int Damage
    {
        get => baseDamage;
        set => baseDamage = value;
    }

    [Tooltip("Multiply damage when hitting a head collider.")]
    public float headshotMultiplier = 2f;

    [Tooltip("If true, bullets damage enemies.")]
    public bool damageEnemy = true;

    [Header("Headshot Detection")]
    [Tooltip("If the hit collider has this tag (or any parent does), it counts as headshot. Recommended to create tag 'HeadShot'.")]
    public string headshotTag = "HeadShot";

    [Tooltip("Fallback: if tag isn't used, collider GameObject name equals this, it counts as headshot.")]
    public string headshotName = "HeadShot";

    private float _spawnTime;

    private void Awake()
    {
        if (theRB == null)
            theRB = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        _spawnTime = Time.time;

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
        // Drive forward
        if (theRB != null)
            theRB.linearVelocity = transform.forward * moveSpeed;
        else
            transform.position += transform.forward * moveSpeed * Time.fixedDeltaTime;

        // Lifetime
        if (Time.time - _spawnTime >= lifeTime)
            Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;

        Collider hit = collision.collider;

        // Compute a stable hit point / normal
        Vector3 point = transform.position;
        Vector3 normal = -transform.forward;

        if (collision.contactCount > 0)
        {
            ContactPoint cp = collision.GetContact(0);
            point = cp.point;
            normal = cp.normal;
        }

        // Only handle enemy hits (damage + impact) — no blood on floor.
        if (HitIsEnemy(hit))
        {
            if (damageEnemy)
            {
                int dmg = ComputeDamage(hit);
                var eh = hit.GetComponentInParent<EnemyHealthController>();
                if (eh != null) eh.DamageEnemy(dmg);
            }

            SpawnImpact(point, normal);
        }

        Destroy(gameObject);
    }

    // Optional fallback if something is still configured as trigger
    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        if (HitIsEnemy(other))
        {
            if (damageEnemy)
            {
                int dmg = ComputeDamage(other);
                var eh = other.GetComponentInParent<EnemyHealthController>();
                if (eh != null) eh.DamageEnemy(dmg);
            }

            Vector3 point = other.ClosestPoint(transform.position);
            Vector3 normal = -transform.forward;

            SpawnImpact(point, normal);
        }

        Destroy(gameObject);
    }

    private int ComputeDamage(Collider hit)
    {
        float dmg = baseDamage;

        if (IsHeadshot(hit))
            dmg *= headshotMultiplier;

        return Mathf.Max(1, Mathf.RoundToInt(dmg));
    }

    private bool HitIsEnemy(Collider hit)
    {
        if (hit == null) return false;

        if (hit.CompareTag("Enemy")) return true;

        Transform t = hit.transform;
        while (t != null)
        {
            if (t.CompareTag("Enemy")) return true;
            t = t.parent;
        }

        return false;
    }

    private bool IsHeadshot(Collider hit)
    {
        if (hit == null) return false;

        // Prefer tag (cleanest)
        if (!string.IsNullOrEmpty(headshotTag))
        {
            if (hit.CompareTag(headshotTag)) return true;

            Transform t = hit.transform;
            while (t != null)
            {
                if (t.CompareTag(headshotTag)) return true;
                t = t.parent;
            }
        }

        // Fallback: name match (useful if you don't want to create a tag)
        if (!string.IsNullOrEmpty(headshotName))
        {
            if (hit.name == headshotName) return true;
        }

        return false;
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactEffect == null) return;

        Vector3 n = normal.sqrMagnitude > 0.0001f ? normal.normalized : -transform.forward;
        float offset = Mathf.Max(0.001f, impactSurfaceOffset);

        Vector3 spawnPos = point + n * offset;

        Vector3 forward = alignToCollisionNormal ? n : (-transform.forward);
        if (forward.sqrMagnitude < 0.0001f) forward = n;

        Quaternion rot = Quaternion.LookRotation(forward);

        Instantiate(impactEffect, spawnPos, rot);
    }
}
