using UnityEngine;

public class BulletController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 175f;
    public float lifeTime = 25f;

    [Tooltip("Optional; if left empty we'll auto-grab the Rigidbody on this object.")]
    public Rigidbody theRB;

    [Header("Impact")]
    public GameObject impactEffect;

    [Tooltip("Push the impact slightly out from the surface so it doesn't clip inside.")]
    public float impactSurfaceOffset = 0.05f;

    [Header("Damage")]
    public int Damage = 2;
    public bool damageEnemy, damagePlayer;

    // Position at the start of the last physics step.
    // We raycast from this position to the current position to find the *real* surface hit.
    private Vector3 _prevPhysicsPos;

    private void Awake()
    {
        if (theRB == null)
            theRB = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        _prevPhysicsPos = transform.position;

        if (theRB != null)
        {
            // Best for fast trigger bullets.
            theRB.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            theRB.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void FixedUpdate()
    {
        // Cache where we were BEFORE the physics step runs.
        _prevPhysicsPos = transform.position;
    }

    private void Update()
    {
        // Keep your original feel: set velocity every frame.
        if (theRB != null)
            theRB.linearVelocity = transform.forward * moveSpeed;

        lifeTime -= Time.deltaTime;
        if (lifeTime <= 0f)
            Destroy(gameObject);
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
            if (PlayerHealthController.instance != null)
                PlayerHealthController.instance.DamagePlayer(Damage);
        }

        SpawnImpact(other);

        Destroy(gameObject);
    }

    private void SpawnImpact(Collider other)
    {
        if (impactEffect == null) return;

        Vector3 start = _prevPhysicsPos;
        Vector3 end = transform.position;

        Vector3 delta = end - start;
        float dist = delta.magnitude;

        // Reasonable fallbacks
        Vector3 hitPoint = other.ClosestPoint(end);
        Vector3 hitNormal = -transform.forward;

        // Raycast along the traveled segment to get a trustworthy surface point.
        // We ONLY accept hits on the collider we actually triggered (or its root), so we won't pick the ground.
        if (dist > 0.0001f)
        {
            Ray ray = new Ray(start, delta / dist);
            RaycastHit[] hits = Physics.RaycastAll(ray, dist + 0.25f, ~0, QueryTriggerInteraction.Collide);

            float best = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.collider == null) continue;

                if (h.collider == other || h.collider.transform.root == other.transform.root)
                {
                    if (h.distance < best)
                    {
                        best = h.distance;
                        hitPoint = h.point;
                        hitNormal = h.normal;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                // Fallback: closest point based on approach side
                hitPoint = other.ClosestPoint(start);
                Vector3 n = (start - hitPoint);
                if (n.sqrMagnitude > 0.0001f) hitNormal = n.normalized;
            }
            else
            {
                // Sometimes normals can be zero/invalid for triggers; compute from approach direction if needed.
                if (hitNormal.sqrMagnitude < 0.0001f)
                {
                    Vector3 n = (start - hitPoint);
                    if (n.sqrMagnitude > 0.0001f) hitNormal = n.normalized;
                    else hitNormal = -transform.forward;
                }
            }
        }
        else
        {
            // Minimal movement fallback
            Vector3 n = (start - hitPoint);
            if (n.sqrMagnitude > 0.0001f) hitNormal = n.normalized;
        }

        // Ensure the normal faces the shooter/approach side so the offset always pushes
        // the impact effect toward the camera/shooter (prevents "behind the enemy" flips).
        Vector3 towardShooter = (start - hitPoint);
        if (towardShooter.sqrMagnitude > 0.0001f)
        {
            towardShooter.Normalize();

            // If the normal points away from the shooter, flip it.
            if (Vector3.Dot(hitNormal, towardShooter) < 0f)
                hitNormal = -hitNormal;
        }

        float offset = Mathf.Max(0.001f, impactSurfaceOffset);
        Vector3 spawnPos = hitPoint + hitNormal * offset;
        Quaternion rot = Quaternion.LookRotation(hitNormal);

        Instantiate(impactEffect, spawnPos, rot);
    }
}
