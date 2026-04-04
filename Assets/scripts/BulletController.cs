using UnityEngine;
using System.Collections;

/// <summary>
/// BulletController (works for BOTH your old cube bullets and Unity-Store “laser” bullets)
///
/// Key upgrades for laser prefabs:
/// - Auto-adds a small trigger collider + kinematic Rigidbody if the prefab has none (common for visual-only lasers).
/// - Optional SphereCast/Raycast hit detection each FixedUpdate (recommended for fast projectiles like lasers).
///
/// Damage routing (same as before):
/// - ENEMY damage: EnemyHealthController in parents -> DamageEnemy(int)
/// - ALLY damage: AllyHealth in parents -> DamageAlly(int)
/// - Fallback: tag checks + SendMessage (optional)
///
/// Notes:
/// - Targets MUST have colliders (enemy/ally/player) to be hittable.
/// - If your store laser prefab has its own movement script (e.g., ShotBehavior), DISABLE/REMOVE it and let this script move it.
/// </summary>
[DisallowMultipleComponent]
public class BulletController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 40f;
    public float lifeTime = 12f;

    [Tooltip("Optional; if left empty we'll auto-grab the Rigidbody on this object.")]
    public Rigidbody theRB;

    [Header("Laser / Fast Projectile Hit Detection")]
    [Tooltip("Recommended ON for lasers / very fast bullets. Uses a cast from current->next position each FixedUpdate.")]
    public bool useCastsForHits = true;

    [Tooltip("Radius for SphereCast. Set to 0 to use a Raycast instead.")]
    public float castRadius = 0.03f;

    [Tooltip("Which layers can be hit by this projectile.")]
    public LayerMask hitMask = ~0;

    [Tooltip("Whether the cast should consider trigger colliders.")]
    public QueryTriggerInteraction castTriggers = QueryTriggerInteraction.Ignore;

    [Header("Auto Physics Setup (for store laser prefabs)")]
    [Tooltip("If the prefab has no Collider, we add a small trigger CapsuleCollider so OnTriggerEnter can fire.")]
    public bool autoAddTriggerColliderIfMissing = true;

    [Tooltip("If the prefab has no Rigidbody, we add a kinematic Rigidbody so trigger messages can fire reliably.")]
    public bool autoAddKinematicRigidbodyIfMissing = true;

    [Tooltip("Radius for the auto-added CapsuleCollider.")]
    public float autoColliderRadius = 0.03f;

    [Tooltip("Length/height for the auto-added CapsuleCollider (aligned forward/Z).")]
    public float autoColliderLength = 0.25f;

    [Header("Impact")]
    public GameObject impactEffect;
    [Tooltip("Impact effect used when hitting ENEMIES (eg: GreenBloodSpray). Leave empty to use Impact Effect (legacy).")]
    public GameObject impactEffectEnemy;

    [Tooltip("Impact effect used when hitting the WORLD (walls, floor, props). Leave empty to use Impact Effect.")]
    public GameObject impactEffectWorld;

    [Tooltip("If true, spawn an impact effect when hitting non-damageable world geometry.")]
    public bool spawnWorldImpacts = true;

    

    [Header("Turret Impact Override (optional)")]
    [Tooltip("If true, this bullet will use the turret impact effects below (when assigned). Set this from your turret when spawning bullets.")]
    public bool firedByTurret = false;

    [Tooltip("Turret-specific impact effect for targets (characters/robots). If empty, falls back to the normal impact selection.")]
    public GameObject turretImpactEffectTarget;

    [Tooltip("Turret-specific impact effect for the world (walls/floor/props). If empty, falls back to Impact Effect World / Impact Effect.")]
    public GameObject turretImpactEffectWorld;
[Tooltip("Impact effect used when hitting DRONES (eg: MetalSparks or ElectricBurst).")]
    public GameObject impactEffectDrone;

    [Tooltip("Optional impact effect for Turrets (TurretHealthController). Useful for metal sparks when shooting turrets.")]
    public GameObject impactEffectTurret;

    [Tooltip("Impact effect used when the PLAYER shoots an ALLY/PLAYER (eg: RedBloodSpray).")]
    public GameObject impactEffectAlly;

    [Tooltip("Optional impact effect for NPCs.")]
    public GameObject impactEffectNPC;

    [Tooltip("Optional impact effect for Animals (tag \"animal\" / AnimalHealth).")]
    public GameObject impactEffectAnimal;


    [Tooltip("Optional impact effect for Bosses (tag \"Boss\" / AlienBossHealth).")]
    public GameObject impactEffectBoss;
    public float impactSurfaceOffset = 0.03f;
    public bool alignToCollisionNormal = true;

    [Header("Damage")]
    [Tooltip("Who fired this projectile. Used so enemies can aggro/return-fire at the correct attacker (ally vs player).")]
    public Transform owner;

    [Tooltip("Base damage before headshot multiplier. (Legacy field name also exists: Damage)")]
    public float baseDamage = 2f;

    [Tooltip("Multiply damage when hitting a head collider.")]
    public float headshotMultiplier = 2f;

    [Tooltip("If true, bullets can damage enemies.")]
    public bool damageEnemy = true;

    [Tooltip("If true, bullets can damage allies/players.")]
    public bool damageAlly = true;

    [Tooltip("If true, bullets can damage NPCs (non-recruitable civilians).")]
    public bool damageNPC = true;

    [Tooltip("If true, bullets can damage animals (AnimalHealth).")]
    public bool damageAnimals = true;



    [Tooltip("If true, bullets can damage bosses (AlienBossHealth).")]
    public bool damageBoss = true;
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
    public string npcTag = "NPC";
    public string playerTag = "Player";
    public string animalTag = "animal";

    public string bossTag = "Boss";
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

        EnsureLaserPrefabHasPhysics();
    }

    private void OnEnable()
    {
        _spawnTime = Time.time;

        // Ensure legacy Damage stays synced when prefab overrides are present
        if (Mathf.Abs(Damage - baseDamage) > 0.0001f)
            baseDamage = Damage;

        // Back-compat: if Impact Effect Enemy not set, fall back to legacy Impact Effect
        if (impactEffectEnemy == null)
            impactEffectEnemy = impactEffect;
        if (impactEffectDrone == null)
            impactEffectDrone = impactEffectEnemy;

        if (theRB != null)
        {
            theRB.useGravity = false;

            // If it's a dynamic rigidbody, prefer continuous dynamic
            if (!theRB.isKinematic)
                theRB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            else
                theRB.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            theRB.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    
    /// <summary>
    /// Call this right after Instantiate() when spawning bullets from a turret.
    /// This lets turrets use their own impact VFX without changing the rest of your bullet setup.
    /// </summary>
    public void ConfigureTurretImpacts(GameObject turretTargetFx, GameObject turretWorldFx)
    {
        firedByTurret = true;
        turretImpactEffectTarget = turretTargetFx;
        turretImpactEffectWorld = turretWorldFx;
    }

private void FixedUpdate()
    {
        // Lifetime
        if (Time.time - _spawnTime >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        // Predict next position (for casts)
        Vector3 from = (theRB != null) ? theRB.position : transform.position;
        Vector3 to = from + transform.forward * moveSpeed * Time.fixedDeltaTime;

        // Cast-based hit detection (best for lasers)
        if (useCastsForHits && TryGetCastHit(from, to, out RaycastHit hit))
        {
            // Move to contact point (helps impact FX placement)
            transform.position = hit.point;

            HandleHit(hit.collider, hit.point, hit.normal);
            Destroy(gameObject);
            return;
        }

        // Movement
        if (theRB != null && !theRB.isKinematic)
        {
            // Dynamic RB movement
            theRB.linearVelocity = transform.forward * moveSpeed;
        }
        else
        {
            // Manual movement (covers kinematic RBs / no RB)
            transform.position = to;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Prevent self-hits
        if (other.transform.IsChildOf(transform)) return;

        HandleHit(other, transform.position, -transform.forward);
        Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;

        Collider hit = collision.collider;
        if (hit != null && hit.transform.IsChildOf(transform)) return;

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

    private void EnsureLaserPrefabHasPhysics()
    {
        // Many store laser prefabs are visuals only (mesh/line) and have no collider/rigidbody.
        // To make trigger hits work, at least one side must have a Rigidbody; we add one if missing.

        if (autoAddTriggerColliderIfMissing)
        {
            Collider anyCol = GetComponent<Collider>();
            if (anyCol == null)
            {
                CapsuleCollider cc = gameObject.AddComponent<CapsuleCollider>();
                cc.direction = 2; // Z axis
                cc.radius = Mathf.Max(0.0001f, autoColliderRadius);
                cc.height = Mathf.Max(autoColliderLength, cc.radius * 2f);
                cc.isTrigger = true;
            }
        }

        if (theRB == null && autoAddKinematicRigidbodyIfMissing)
        {
            theRB = gameObject.AddComponent<Rigidbody>();
            theRB.useGravity = false;
            theRB.isKinematic = true; // we move via transform by default
            theRB.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            theRB.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private bool TryGetCastHit(Vector3 from, Vector3 to, out RaycastHit bestHit)
    {
        bestHit = default;

        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return false;

        Vector3 dir = delta / dist;

        if (castRadius > 0f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(from, castRadius, dir, dist, hitMask, castTriggers);
            return ChooseBestNonSelfHit(hits, out bestHit);
        }
        else
        {
            RaycastHit[] hits = Physics.RaycastAll(from, dir, dist, hitMask, castTriggers);
            return ChooseBestNonSelfHit(hits, out bestHit);
        }
    }

    private bool ChooseBestNonSelfHit(RaycastHit[] hits, out RaycastHit bestHit)
    {
        bestHit = default;
        bool found = false;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null) continue;

            // Ignore this projectile's own colliders/children
            if (h.collider.transform.IsChildOf(transform)) continue;

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                bestHit = h;
                found = true;
            }
        }

        return found;
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
        MeleeEnemyHealthController meleeEnemyHealth = hit.GetComponentInParent<MeleeEnemyHealthController>();
        MechHealthController mechHealth = hit.GetComponentInParent<MechHealthController>();
        AllyHealth allyHealth = hit.GetComponentInParent<AllyHealth>();
        PlayerVitals playerVitals = hit.GetComponentInParent<PlayerVitals>();

        NPCHealth npcHealth = hit.GetComponentInParent<NPCHealth>();

        AnimalHealth animalHealth = hit.GetComponentInParent<AnimalHealth>();
        AlienBossHealth bossHealth = hit.GetComponentInParent<AlienBossHealth>();
        AlienBossController bossController = hit.GetComponentInParent<AlienBossController>();


        DroneEnemyController droneEnemy = hit.GetComponentInParent<DroneEnemyController>();
        bool isEnemy = enemyHealth != null || meleeEnemyHealth != null || mechHealth != null || droneEnemy != null || HasTagInParents(hit, enemyTag);
        bool isAlly = allyHealth != null || playerVitals != null || HasTagInParents(hit, allyTag) || HasTagInParents(hit, playerTag);
        bool isNPC = npcHealth != null || HasTagInParents(hit, npcTag);
        bool isAnimal = animalHealth != null || HasTagInParents(hit, animalTag);
        bool isBoss = bossHealth != null || HasTagInParents(hit, bossTag);
        var turretHealth = hit.GetComponentInParent<TurretHealthController>();
        bool isTurret = turretHealth != null;
        bool isDrone = droneEnemy != null;

        // Let enemies know WHO hit them so they aggro the attacker (prevents them from snapping to the player when an ally shoots).
        if (isEnemy)
            NotifyEnemyAggroFromHit(hit);

        if (isEnemy && damageEnemy)
        {
            if (enemyHealth != null)
            {
                Vector3 incomingDirectionWorld;
                if (owner != null)
                    incomingDirectionWorld = owner.position - enemyHealth.transform.position;
                else
                    incomingDirectionWorld = -transform.forward;

                enemyHealth.DamageEnemy(dmgInt, incomingDirectionWorld);
            }
            else if (meleeEnemyHealth != null)
            {
                Vector3 incomingDirectionWorld;
                if (owner != null)
                    incomingDirectionWorld = owner.position - meleeEnemyHealth.transform.position;
                else
                    incomingDirectionWorld = -transform.forward;

                meleeEnemyHealth.DamageEnemy(dmgInt, incomingDirectionWorld);
            }
            else if (mechHealth != null)
            {
                Vector3 incomingDirectionWorld;
                if (owner != null)
                    incomingDirectionWorld = owner.position - mechHealth.transform.position;
                else
                    incomingDirectionWorld = -transform.forward;

                mechHealth.DamageEnemy(dmgInt, incomingDirectionWorld);
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
            // Player first (new health system)
            if (playerVitals != null)
            {
                Vector3 damageSourcePos = owner != null
                    ? owner.position
                    : (transform.position - (transform.forward * 2f));

                playerVitals.Damage(dmgInt, damageSourcePos);
            }
            else if (allyHealth != null)
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

        else if (isNPC && damageNPC)
        {
            if (npcHealth != null)
            {
                npcHealth.TakeDamage(dmgInt, owner);
            }
            else
            {
                // fallback for custom NPC health scripts
                hit.SendMessageUpwards("DamageNPC", dmgInt, SendMessageOptions.DontRequireReceiver);
                hit.SendMessageUpwards("TakeDamage", dmgFloat, SendMessageOptions.DontRequireReceiver);
            }
        }


        else if (isBoss && damageBoss)
        {
            // Bosses (AlienBoss system)
            if (bossController != null && owner != null)
            {
                // Ensure boss switches target to attacker (provoked)
                bossController.GetShot(owner);
            }

            if (bossHealth != null)
            {
                // AlienBossHealth accepts attacker transform for aggro logic
                bossHealth.TakeDamage(dmgInt, owner);
            }
            else
            {
                // Fallback if the boss uses a different damage method
                hit.SendMessageUpwards("TakeDamage", dmgInt, SendMessageOptions.DontRequireReceiver);
                hit.SendMessageUpwards("TakeDamage", dmgFloat, SendMessageOptions.DontRequireReceiver);
            }
        }

        else if (isAnimal && damageAnimals)
        {
            // Animals (your new wildlife system)
            if (animalHealth != null)
            {
                // AnimalHealth uses int damage and accepts attacker transform (owner).
                animalHealth.TakeDamage(dmgInt, owner); // IMPORTANT: owner should be the shooter transform for aggro to work.
            }
            else
            {
                // Fallback if the animal uses a different damage method
                hit.SendMessageUpwards("TakeDamage", dmgInt, SendMessageOptions.DontRequireReceiver);
                hit.SendMessageUpwards("TakeDamage", dmgFloat, SendMessageOptions.DontRequireReceiver);
            }
        }

        // FX: pick impact prefab
        // Special rule you asked for:
        // - Player bullet uses GreenBloodSpray for enemies
        // - BUT when the PLAYER shoots an ally/player, use RedBloodSpray instead
        GameObject fx = impactEffectEnemy != null ? impactEffectEnemy : impactEffect;

        // NPC override (optional)
        if (isNPC && impactEffectNPC != null)
            fx = impactEffectNPC;

        // Animal override (optional)
        if (isAnimal && impactEffectAnimal != null)
            fx = impactEffectAnimal;

        // Boss override (optional)
        if (isBoss && impactEffectBoss != null)
            fx = impactEffectBoss;


        // Turret override (optional)
        if (isTurret && impactEffectTurret != null)
            fx = impactEffectTurret;

        // Drone override (optional)
        if (isDrone && impactEffectDrone != null)
            fx = impactEffectDrone;

        // Ally override ONLY when the shooter is the player
        if (isAlly && impactEffectAlly != null && IsOwnerPlayer())
            fx = impactEffectAlly;

        bool hitCharacter = (isEnemy || isAlly || isNPC || isAnimal || isBoss || isDrone || isTurret);

        // Turret override (optional): let turret bullets use their own impact VFX.
        if (firedByTurret && hitCharacter && turretImpactEffectTarget != null)
            fx = turretImpactEffectTarget;

        // If it wasn't a character, allow a "world" impact (sparks/dust) if enabled.
        if (!hitCharacter && spawnWorldImpacts)
        {
            if (firedByTurret && turretImpactEffectWorld != null)
                fx = turretImpactEffectWorld;
            else
                fx = impactEffectWorld != null ? impactEffectWorld : impactEffect;
        }

        if (fx != null && (hitCharacter || spawnWorldImpacts))
        {
            Vector3 p = hit.ClosestPoint(point);
            SpawnImpact(fx, p, normal);
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

        Transform t = hit.transform;
        while (t != null)
        {
            // SAFE: CompareTag throws if the requested tag name is not defined in the Tag Manager.
            // We compare the existing tag string instead, and do it case-insensitively so "Animal" and "animal" both work.
            if (string.Equals(t.tag, tag, System.StringComparison.OrdinalIgnoreCase))
                return true;

            t = t.parent;
        }

        return false;
    }

    // ---------------------------
    // Helpers added for compile-fix
    // ---------------------------

    private bool IsOwnerPlayer()
    {
        if (owner == null) return false;
        // Safe, case-insensitive tag compare
        return string.Equals(owner.tag, playerTag, System.StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyEnemyAggroFromHit(Collider hit)
    {
        if (hit == null || owner == null) return;

        // Best-effort: notify common enemy scripts to aggro onto the attacker.
        hit.SendMessageUpwards("GetShot", owner, SendMessageOptions.DontRequireReceiver);
        hit.SendMessageUpwards("SetCombatTarget", owner, SendMessageOptions.DontRequireReceiver);
        hit.SendMessageUpwards("SetTarget", owner, SendMessageOptions.DontRequireReceiver);
    }

    private void SpawnImpact(GameObject fxPrefab, Vector3 point, Vector3 normal)
    {
        if (fxPrefab == null) return;

        Vector3 spawnPos = point + (normal * impactSurfaceOffset);

        Quaternion rot = Quaternion.identity;
        if (alignToCollisionNormal && normal.sqrMagnitude > 0.0001f)
            rot = Quaternion.LookRotation(normal);

        GameObject fx = Instantiate(fxPrefab, spawnPos, rot);
        // Safety: auto-destroy common impact effects
        Destroy(fx, 6f);
    }

}