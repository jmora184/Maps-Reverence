using System.Collections;
using UnityEngine;

/// <summary>
/// Melee "punch / butt-stroke" using the CURRENT equipped weapon's transform for the swing,
/// and ENEMY TAG filtering (no Enemy layer required).
///
/// Key fix for your project:
/// - Your enemies use EnemyHealthController.DamageEnemy(int damageAmount)
///   (NOT TakeDamage). This script will call DamageEnemy directly when that component exists.
///
/// Notes:
/// - Your GUN does NOT need a collider.
/// - ENEMIES DO need colliders to be hit by the SphereCast.
/// </summary>
public class PlayerWeaponMeleePunch : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Typically the player camera transform. Melee traces forward from here.")]
    public Transform aimOrigin;

    [Tooltip("Optional: weapon parent/holder to animate instead of the gun root. If null, we auto-use the active Gun transform.")]
    public Transform weaponVisualRootOverride;

    [Header("Target Filtering")]
    [Tooltip("Enemies must have this Tag (or a parent with this Tag).")]
    public string enemyTag = "Enemy";

    [Tooltip("If true, only objects with enemyTag can be damaged.")]
    public bool requireEnemyTag = true;

    [Header("Melee")]
    [Tooltip("Melee damage. If EnemyHealthController is used (maxHealth ~5), set this to 1–2 for balanced hits.")]
    public float damage = 1f;

    public float range = 2.2f;
    public float radius = 0.32f;
    public float cooldown = 0.45f;

    [Header("Input")]
    public KeyCode meleeKey = KeyCode.V;

    [Header("Physics Filtering")]
    [Tooltip("Physics mask to search. You can leave as Everything. For best results exclude Ground/Water/Player layers if you have them.")]
    public LayerMask hitMask = ~0;

    [Tooltip("Ignore trigger colliders (recommended).")]
    public bool ignoreTriggers = true;

    [Header("Impact")]
    public float pushForce = 6f;

    [Header("Debug")]
    public bool debugDraw = false;
    public bool debugLog = false;

    [Header("Punch Animation")]
    public Vector3 punchLocalOffset = new Vector3(0.05f, -0.08f, 0.22f);
    public Vector3 punchLocalEuler = new Vector3(-18f, 6f, 0f);
    public float punchDuration = 0.10f;

    [Header("Fire Blocking")]
    public bool blockFiringBriefly = true;
    public float firingBlockTime = 0.12f;

    float _nextTimeAllowed;

    Transform _weaponVisual;
    Vector3 _weaponStartPos;
    Quaternion _weaponStartRot;
    bool _weaponCached;

    void Awake()
    {
        if (aimOrigin == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) aimOrigin = cam.transform;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(meleeKey))
            TryMelee();
    }

    void TryMelee()
    {
        if (Time.time < _nextTimeAllowed) return;
        _nextTimeAllowed = Time.time + cooldown;

        ResolveWeaponVisual();

        if (_weaponVisual != null)
        {
            StopAllCoroutines();
            StartCoroutine(PunchAnim());
        }

        if (blockFiringBriefly)
            BlockFiring();

        DoHitCheck();
    }

    void ResolveWeaponVisual()
    {
        if (weaponVisualRootOverride != null)
        {
            if (_weaponVisual != weaponVisualRootOverride)
            {
                _weaponVisual = weaponVisualRootOverride;
                CacheWeaponStart();
            }
            return;
        }

        Gun activeGun = FindActiveGun();
        if (activeGun != null && _weaponVisual != activeGun.transform)
        {
            _weaponVisual = activeGun.transform;
            CacheWeaponStart();
        }
    }

    void CacheWeaponStart()
    {
        if (_weaponVisual == null) return;
        _weaponStartPos = _weaponVisual.localPosition;
        _weaponStartRot = _weaponVisual.localRotation;
        _weaponCached = true;
    }

    IEnumerator PunchAnim()
    {
        if (_weaponVisual == null) yield break;
        if (!_weaponCached) CacheWeaponStart();

        Vector3 fromPos = _weaponStartPos;
        Vector3 toPos = _weaponStartPos + punchLocalOffset;

        Quaternion fromRot = _weaponStartRot;
        Quaternion toRot = _weaponStartRot * Quaternion.Euler(punchLocalEuler);

        float dur = Mathf.Max(0.001f, punchDuration);

        // Forward
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(fromPos, toPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(fromRot, toRot, s);
            yield return null;
        }

        // Return
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(toPos, fromPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(toRot, fromRot, s);
            yield return null;
        }

        _weaponVisual.localPosition = fromPos;
        _weaponVisual.localRotation = fromRot;
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void DoHitCheck()
    {
        if (aimOrigin == null) return;

        Vector3 origin = aimOrigin.position;
        Vector3 dir = aimOrigin.forward;

        if (debugDraw)
            Debug.DrawRay(origin, dir * range, Color.yellow, 0.25f);

        QueryTriggerInteraction qti = ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;

        RaycastHit[] hits = Physics.SphereCastAll(origin, radius, dir, range, hitMask, qti);
        if (hits == null || hits.Length == 0)
        {
            if (debugLog) Debug.Log("[Melee] No hits detected. Enemies must have colliders.");
            return;
        }

        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;

            if (requireEnemyTag && !IsEnemyTagged(c)) continue;

            float d = hits[i].distance;
            if (d < bestDist)
            {
                bestDist = d;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            if (debugLog) Debug.Log($"[Melee] Hits found, but none matched tag='{enemyTag}'. Tag the enemy root (or any parent of the collider).");
            return;
        }

        RaycastHit hit = hits[bestIndex];

        if (debugLog)
            Debug.Log($"[Melee] Hit '{hit.collider.name}' dist={hit.distance:0.00}");

        // Push (optional)
        Rigidbody rb = hit.rigidbody;
        if (rb != null)
            rb.AddForce(dir * pushForce, ForceMode.Impulse);

        // 1) Preferred: call your enemy health script directly (this is what your enemies actually use)
        EnemyHealthController eh = hit.collider.GetComponentInParent<EnemyHealthController>();
        if (eh != null)
        {
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            eh.DamageEnemy(dmgInt);
            if (debugLog) Debug.Log($"[Melee] DamageEnemy({dmgInt}) applied to {eh.name}");
            return;
        }

        // 2) Interface-based (if you ever add it later)
        IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage);
            if (debugLog) Debug.Log($"[Melee] IDamageable.TakeDamage({damage}) applied to {hit.collider.name}");
            return;
        }

        // 3) Fallback: SendMessage for projects that use TakeDamage(float)
        hit.collider.SendMessageUpwards("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (debugLog) Debug.Log($"[Melee] Sent message TakeDamage({damage}) to {hit.collider.name} (may do nothing if no such method exists)");
    }

    bool IsEnemyTagged(Collider c)
    {
        if (c.CompareTag(enemyTag)) return true;

        Transform t = c.transform;
        int safety = 0;
        while (t != null && safety++ < 16)
        {
            if (t.CompareTag(enemyTag)) return true;
            t = t.parent;
        }
        return false;
    }

    void BlockFiring()
    {
        Gun g = FindActiveGun();
        if (g == null) return;
        g.fireCounter = Mathf.Max(g.fireCounter, firingBlockTime);
    }

    Gun FindActiveGun()
    {
        Gun[] guns = GetComponentsInChildren<Gun>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            if (guns[i] != null && guns[i].gameObject.activeInHierarchy)
                return guns[i];
        }
        return null;
    }
}

/// <summary>
/// Optional interface you can implement on enemies/allies/destructibles.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount);
}
