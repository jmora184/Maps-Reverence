using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NPCController
///
/// Rules:
/// - NOT recruitable (exclude NPC layer from command-mode raycasts/selection).
/// - Has Animator params (supports common AllyRun controller params).
/// - Has dialog hooks.
/// - Has health + can die (NPCHealth).
/// - If attacked by Player or Enemy, becomes hostile and fights back.
/// - Can optionally defend against nearby enemies (e.g., within 30m).
/// - Starts with weapon hidden; draws weapon on aggro (optional).
/// </summary>
[DisallowMultipleComponent]
public class NPCController : MonoBehaviour
{
    public enum NPCState { Idle, Dialog, Hostile, Dead }

    [Header("State")]
    public NPCState state = NPCState.Idle;

    [Header("References")]
    public Animator animator;
    public NavMeshAgent agent;
    public Transform firePoint;              // optional (only if NPC should shoot back)
    public GameObject bulletPrefab;          // optional

    [Header("Combat")]
    public float shootRange = 14f;
    public float fireRate = 3.5f;
    public float faceTargetTurnSpeed = 12f;

    [Tooltip("If true, NPC will only fight back after being attacked (unless Defend Against Enemies is enabled).")]
    public bool onlyAggroWhenAttacked = true;

    [Header("Proactive Enemy Defense")]
    [Tooltip("If true, the NPC will draw their weapon and fight nearby enemies even if not attacked.")]
    public bool defendAgainstEnemies = true;

    [Tooltip("How far (in meters) the NPC scans for enemies to defend against.")]
    public float defendEnemyScanRange = 30f;

    [Tooltip("Layer mask used to find enemies. Put your enemies on an 'Enemy' layer and set this accordingly.")]
    public LayerMask enemyLayerMask = 0;

    [Tooltip("Optional tag fallback if enemyLayerMask is 0 (no layers set).")]
    public string enemyTagFallback = "Enemy";

    [Tooltip("How often (seconds) to scan for enemies.")]
    public float enemyScanInterval = 0.5f;

    [Header("Weapon Draw")]
    [Tooltip("Optional: drag your w_usp45 object here. If empty, we will try find a child named 'w_usp45'.")]
    public GameObject weaponObject;
    public string weaponObjectName = "w_usp45";
    public bool startWithWeaponHidden = true;
    public bool showWeaponWhenHostile = true;

    [Tooltip("Animator trigger to play the draw animation (optional).")]
    public string animTriggerDrawWeapon = "DrawWeapon";
    [Tooltip("Animator bool to indicate weapon is out (optional).")]
    public string animBoolWeaponOut = "weaponOut";

    [Header("Animator Params (AllyRun-friendly)")]
    [Tooltip("Common on AllyRun controller: 'Speed' (float).")]
    public string animFloatSpeed = "Speed";
    [Tooltip("Common on AllyRun controller: 'isWalking' (bool). Optional.")]
    public string animBoolWalking = "isWalking";
    [Tooltip("Common on AllyRun controller: 'isRunning' (bool). Optional.")]
    public string animBoolRunning = "isRunning";
    [Tooltip("Optional: if your controller has a hostile bool.")]
    public string animBoolHostile = "isHostile";
    [Tooltip("Optional: talking bool for dialog.")]
    public string animBoolTalking = "isTalking";
    [Tooltip("Optional: hit trigger.")]
    public string animTriggerHit = "Hit";
    [Tooltip("Common on some controllers: 'Die' trigger.")]
    public string animTriggerDie = "Die";
    [Tooltip("Optional: shoot trigger.")]
    public string animTriggerShoot = "Shoot";

    [Header("Animator Driving")]
    [Tooltip("If true, we will auto-resolve parameter name casing (e.g., 'Speed' vs 'speed') if the provided names don't exist.")]
    public bool autoResolveAnimatorParams = true;

    [Tooltip("Smooth the speed parameter changes to reduce jitter.")]
    public float speedDampTime = 0.10f;

    [Tooltip("If true, force Animator culling mode to AlwaysAnimate (prevents animations stopping when offscreen).")]
    public bool forceAlwaysAnimate = true;

    [Tooltip("Recommended with NavMeshAgent-driven movement: keep Apply Root Motion OFF to avoid sliding issues.")]
    public bool forceDisableRootMotion = true;

    [Header("Run/Walk thresholds")]
    [Tooltip("If you use isWalking/isRunning, speed below this is idle.")]
    public float walkThreshold = 0.15f;
    [Tooltip("If you use isWalking/isRunning, speed above this is running.")]
    public float runThreshold = 2.0f;

    // Resolved parameter names (internal)
    private string _pSpeed;
    private string _pWalking;
    private string _pRunning;
    private string _pHostile;
    private string _pTalking;
    private string _pHit;
    private string _pDie;
    private string _pShoot;

    private NPCHealth _health;
    private float _nextEnemyScanTime;
    private Transform _target;
    private float _nextFireTime;
    private bool _weaponOut;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<NPCHealth>();

        if (animator != null)
        {
            if (forceAlwaysAnimate) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (forceDisableRootMotion) animator.applyRootMotion = false;
        }

        ResolveWeaponObject();
        ResolveAnimatorParams();

        if (startWithWeaponHidden && weaponObject != null)
            weaponObject.SetActive(false);
    }

    private void Update()
    {
        if (state == NPCState.Dead) return;

        UpdateAnimatorLocomotion();

        if (state == NPCState.Dialog)
        {
            if (agent != null) agent.isStopped = true;
            return;
        }

        if (state == NPCState.Hostile)
        {
            HostileTick();
        }
        else
        {
            if (defendAgainstEnemies)
                ScanForThreats();
        }
    }

    // Called by NPCHealth when the NPC is damaged.
    public void OnTookDamage(Transform attacker)
    {
        if (state == NPCState.Dead) return;
        BecomeHostile(attacker);

        if (animator != null && !string.IsNullOrEmpty(_pHit))
            animator.SetTrigger(_pHit);
    }

    /// <summary>Become hostile (no specific target).</summary>
    public void BecomeHostile()
    {
        BecomeHostile(null);
    }

    /// <summary>Become hostile toward a specific attacker/target.</summary>
    public void BecomeHostile(Transform attacker)
    {
        if (state == NPCState.Dead) return;

        state = NPCState.Hostile;
        if (attacker != null) _target = attacker;

        if (animator != null && !string.IsNullOrEmpty(_pHostile))
            animator.SetBool(_pHostile, true);

        if (showWeaponWhenHostile)
            DrawWeaponNow();
    }

    public void BeginDialogue()
    {
        if (state == NPCState.Dead) return;

        state = NPCState.Dialog;

        if (animator != null && !string.IsNullOrEmpty(_pTalking))
            animator.SetBool(_pTalking, true);

        if (agent != null)
            agent.isStopped = true;
    }

    public void EndDialogue()
    {
        if (state == NPCState.Dead) return;

        state = NPCState.Idle;

        if (animator != null && !string.IsNullOrEmpty(_pTalking))
            animator.SetBool(_pTalking, false);

        if (agent != null)
            agent.isStopped = false;
    }

    public void OnDied()
    {
        if (state == NPCState.Dead) return;

        state = NPCState.Dead;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        if (animator != null && !string.IsNullOrEmpty(_pDie))
            animator.SetTrigger(_pDie);
    }

    
    
    private bool IsTargetValid(Transform t)
    {
        if (t == null) return false;

        // Unity will return 'null' for destroyed objects, but if the object is disabled we still consider it invalid for combat.
        if (!t.gameObject.activeInHierarchy) return false;

        // If the target has an NPCHealth and is dead, stop targeting it.
        var npcHealth = t.GetComponent<NPCHealth>();
        if (npcHealth != null && npcHealth.IsDead) return false;

        // Best-effort: if the target has some other health component with an IsDead/isDead/dead flag, respect it.
        // This avoids hard references to your Enemy/Ally health scripts.
        var behaviours = t.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;

            var type = b.GetType();
            // Fast skip if it's obviously unrelated
            var name = type.Name;
            if (name.IndexOf("Health", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            // Property: IsDead / Dead
            var pIsDead = type.GetProperty("IsDead");
            if (pIsDead != null && pIsDead.PropertyType == typeof(bool))
            {
                try { if ((bool)pIsDead.GetValue(b, null)) return false; } catch { }
            }
            var pDead = type.GetProperty("Dead");
            if (pDead != null && pDead.PropertyType == typeof(bool))
            {
                try { if ((bool)pDead.GetValue(b, null)) return false; } catch { }
            }

            // Field: isDead / dead
            var fIsDead = type.GetField("isDead");
            if (fIsDead != null && fIsDead.FieldType == typeof(bool))
            {
                try { if ((bool)fIsDead.GetValue(b)) return false; } catch { }
            }
            var fDead = type.GetField("dead");
            if (fDead != null && fDead.FieldType == typeof(bool))
            {
                try { if ((bool)fDead.GetValue(b)) return false; } catch { }
            }
        }

        return true;
    }

private void ClearTarget()
    {
        _target = null;
        if (agent != null) agent.isStopped = true;
    }

private void HostileTick()
    {
        if (!IsTargetValid(_target))
        {
            // Target died / got disabled / destroyed.
            ClearTarget();

            // If proactive defense is enabled, immediately try to pick a new nearby enemy.
            if (defendAgainstEnemies)
            {
                ScanForThreats();
            }

            // If still no target, just hold hostile stance without shooting.
            return;
        }

float dist = Vector3.Distance(transform.position, _target.position);

        // Move toward target until in range
        if (agent != null && agent.enabled)
        {
            if (dist > shootRange)
            {
                agent.isStopped = false;
                agent.SetDestination(_target.position);
            }
            else
            {
                agent.isStopped = true;
            }
        }

        // Face target
        Vector3 to = _target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, faceTargetTurnSpeed * Time.deltaTime);
        }

        // Fire
        if (bulletPrefab != null && firePoint != null && dist <= shootRange && Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + (1f / Mathf.Max(0.1f, fireRate));
            ShootOnce();
        }
    }

    private void ShootOnce()
    {
        if (bulletPrefab == null || firePoint == null) return;

        GameObject b = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

        // If your project has BulletController, set owner so NPC can aggro correctly elsewhere.
        // (This line requires BulletController to exist in your project.)
        var bc = b.GetComponent<BulletController>();
        if (bc != null)
        {
            bc.owner = transform;
        }

        if (animator != null && !string.IsNullOrEmpty(_pShoot))
            animator.SetTrigger(_pShoot);
    }

    private void ScanForThreats()
    {
        if (!defendAgainstEnemies || state == NPCState.Dead || state == NPCState.Dialog) return;

        // If you truly only want aggro-on-hit, disable defendAgainstEnemies in Inspector.
        // Otherwise, we defend proactively.
        if (Time.time < _nextEnemyScanTime) return;
        _nextEnemyScanTime = Time.time + Mathf.Max(0.05f, enemyScanInterval);

        Transform best = FindClosestEnemyWithin(defendEnemyScanRange);
        if (best != null)
        {
            _target = best;
            BecomeHostile(best);
        }
    }

    private Transform FindClosestEnemyWithin(float range)
    {
        float bestDist = float.MaxValue;
        Transform best = null;

        // Preferred: layer mask
        if (enemyLayerMask.value != 0)
        {
            Collider[] cols = Physics.OverlapSphere(transform.position, range, enemyLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                Transform root = cols[i].transform.root != null ? cols[i].transform.root : cols[i].transform;
                float d = (root.position - transform.position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = root;
                }
            }
            return best;
        }

        // Fallback: tag
        if (!string.IsNullOrEmpty(enemyTagFallback))
        {
            try
            {
                GameObject[] objs = GameObject.FindGameObjectsWithTag(enemyTagFallback);
                float r2 = range * range;
                for (int i = 0; i < objs.Length; i++)
                {
                    var go = objs[i];
                    if (go == null) continue;
                    float d = (go.transform.position - transform.position).sqrMagnitude;
                    if (d <= r2 && d < bestDist)
                    {
                        bestDist = d;
                        best = go.transform;
                    }
                }
            }
            catch { /* tag missing */ }
        }

        return best;
    }

    private void UpdateAnimatorLocomotion()
    {
        if (animator == null) return;

        float spd = 0f;
        if (agent != null && agent.enabled)
            spd = agent.velocity.magnitude;

        if (!string.IsNullOrEmpty(_pSpeed))
            animator.SetFloat(_pSpeed, spd, Mathf.Max(0f, speedDampTime), Time.deltaTime);

        // Optional walking/running bools (matches AllyRun controller screenshot)
        if (!string.IsNullOrEmpty(_pWalking))
            animator.SetBool(_pWalking, spd >= walkThreshold);

        if (!string.IsNullOrEmpty(_pRunning))
            animator.SetBool(_pRunning, spd >= runThreshold);
    }

    private void ResolveWeaponObject()
    {
        if (weaponObject != null) return;

        Transform t = transform.Find(weaponObjectName);
        if (t == null)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == weaponObjectName)
                {
                    t = all[i];
                    break;
                }
            }
        }

        if (t != null)
            weaponObject = t.gameObject;
    }

    private void DrawWeaponNow()
    {
        if (_weaponOut) return;
        _weaponOut = true;

        if (weaponObject != null)
            weaponObject.SetActive(true);

        if (animator != null)
        {
            // Optional draw anim
            if (HasParam(animator, animTriggerDrawWeapon, AnimatorControllerParameterType.Trigger))
                animator.SetTrigger(animTriggerDrawWeapon);

            if (HasParam(animator, animBoolWeaponOut, AnimatorControllerParameterType.Bool))
                animator.SetBool(animBoolWeaponOut, true);
        }
    }

    private void ResolveAnimatorParams()
    {
        _pSpeed   = ResolveParam(animFloatSpeed, AnimatorControllerParameterType.Float);
        _pWalking = ResolveParam(animBoolWalking, AnimatorControllerParameterType.Bool);
        _pRunning = ResolveParam(animBoolRunning, AnimatorControllerParameterType.Bool);
        _pHostile = ResolveParam(animBoolHostile, AnimatorControllerParameterType.Bool);
        _pTalking = ResolveParam(animBoolTalking, AnimatorControllerParameterType.Bool);
        _pHit     = ResolveParam(animTriggerHit, AnimatorControllerParameterType.Trigger);
        _pDie     = ResolveParam(animTriggerDie, AnimatorControllerParameterType.Trigger);
        _pShoot   = ResolveParam(animTriggerShoot, AnimatorControllerParameterType.Trigger);
    }

    private string ResolveParam(string name, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(name)) return null;

        // exact match
        if (HasParam(animator, name, type)) return name;

        if (!autoResolveAnimatorParams) return null;

        // common casing fixes
        string lower = name.ToLowerInvariant();
        string upperFirst = char.ToUpperInvariant(name[0]) + (name.Length > 1 ? name.Substring(1) : "");
        string lowerFirst = char.ToLowerInvariant(name[0]) + (name.Length > 1 ? name.Substring(1) : "");

        if (HasParam(animator, lower, type)) return lower;
        if (HasParam(animator, upperFirst, type)) return upperFirst;
        if (HasParam(animator, lowerFirst, type)) return lowerFirst;

        // scan any parameter with same letters ignoring case
        foreach (var p in animator.parameters)
        {
            if (p.type != type) continue;
            if (string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase))
                return p.name;
        }

        return null;
    }

    private static bool HasParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (a == null || string.IsNullOrEmpty(name)) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == type && ps[i].name == name)
                return true;
        return false;
    }
}
