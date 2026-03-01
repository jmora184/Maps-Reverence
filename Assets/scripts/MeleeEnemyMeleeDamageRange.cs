using System;
using UnityEngine;

/// <summary>
/// FIXED VERSION
/// 
/// This melee damage script has NO hard dependency on any specific controller type.
/// It will compile even if you don't have MeleeEnemyController.cs in the project.
/// 
/// How to use:
/// 1) Put this on your melee enemy (same object as MeleeEnemy2Controller is ideal).
/// 2) In the Inspector, set attackStateNames to match your Animator's attack state name(s) exactly.
/// 3) Add an Animation Event on the HIT frame that calls TryApplyMeleeDamage(), OR
///    call TryApplyMeleeDamage() from your controller right after triggering attack.
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemyMeleeDamageRang : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator that plays the melee attack animation. If null, will auto-find in children.")]
    public Animator animator;

    [Tooltip("Optional: the transform to treat as the attacker root (for distance and aggro). If null, uses this transform.")]
    public Transform rootAttacker;

    [Tooltip("Optional: assign your controller component here (ex: MeleeEnemy2Controller). If null, script will try to find one automatically.")]
    public Component controllerComponent;

    [Header("Damage")]
    [Min(0.1f)] public float attackRange = 2.4f;
    [Min(0.01f)] public float damage = 15f;

    [Tooltip("Prevents repeated hits from the same animation state / rapid transitions.")]
    [Min(0.01f)] public float damageCooldown = 0.6f;

    [Header("Attack Animation State Names (case-sensitive)")]
    [Tooltip("Damage is only applied while the Animator is currently in one of these state names (layer 0). Add your real state name here.")]
    public string[] attackStateNames = new string[] { "attack", "Attack", "MeleeAttack", "Bite", "Swing", "pound" };

    [Header("Target Tags")]
    [Tooltip("Tags this melee enemy is allowed to damage.")]
    public string[] damageableTags = new string[] { "Player", "Ally", "Enemy", "Animal" };

    [Tooltip("When we hit something, try to make it aggro back onto this attacker (best-effort).")]
    public bool causeVictimToAggroBack = true;

    private float nextDamageTime;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (rootAttacker == null) rootAttacker = transform;

        if (controllerComponent == null)
        {
            // Best-effort: pick a controller-like component that has SetCombatTarget/GetShot methods.
            foreach (var c in GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t.GetMethod("SetCombatTarget", new[] { typeof(Transform) }) != null ||
                    t.GetMethod("GetShot", new[] { typeof(Transform) }) != null)
                {
                    controllerComponent = c;
                    break;
                }
            }
        }
    }

    private bool IsInAttackState()
    {
        if (animator == null) return false;

        var state = animator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < attackStateNames.Length; i++)
        {
            var n = attackStateNames[i];
            if (string.IsNullOrEmpty(n)) continue;
            if (state.IsName(n)) return true;
        }
        return false;
    }

    /// <summary>
    /// Call from an Animation Event near the HIT frame, or from your controller.
    /// </summary>
    public void TryApplyMeleeDamage()
    {
        if (Time.time < nextDamageTime) return;
        if (!IsInAttackState()) return;

        // 1) Prefer the controller's current target if available
        Transform target = TryGetControllerTarget();

        if (target != null)
        {
            TryDamageTarget(target);
            return;
        }

        // 2) Fallback: overlap sphere search
        var hits = Physics.OverlapSphere(rootAttacker.position, attackRange);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            var go = col.gameObject;
            if (go == null) continue;

            if (!IsAllowedTag(go.tag)) continue;

            // Don't hit self
            if (go.transform == rootAttacker || go.transform.IsChildOf(rootAttacker)) continue;

            TryDamageTarget(go.transform);
            return;
        }
    }

    private Transform TryGetControllerTarget()
    {
        if (controllerComponent == null) return null;

        var t = controllerComponent.GetType();

        // Common method name:
        // - GetCurrentTarget() => Transform
        var m = t.GetMethod("GetCurrentTarget", Type.EmptyTypes);
        if (m != null && m.ReturnType == typeof(Transform))
        {
            try { return (Transform)m.Invoke(controllerComponent, null); }
            catch { }
        }

        return null;
    }

    private void TryDamageTarget(Transform target)
    {
        if (target == null) return;

        float dist = Vector3.Distance(rootAttacker.position, target.position);
        if (dist > attackRange) return;

        bool didDamage = TryDamageByInterfaceOrMessage(target.gameObject, damage);

        if (didDamage)
        {
            nextDamageTime = Time.time + damageCooldown;

            if (causeVictimToAggroBack)
                TryCauseAggroBack(target);
        }
    }

    private bool TryDamageByInterfaceOrMessage(GameObject victim, float amount)
    {
        if (victim == null) return false;

        // 1) IDamageable (preferred)
        var damageable = victim.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(amount);
            return true;
        }

        // 2) Best-effort message calls (no hard dependency)
        victim.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
        victim.SendMessage("ApplyDamage", amount, SendMessageOptions.DontRequireReceiver);

        // We can't know for sure if it landed, but at least it won't error.
        return true;
    }

    private void TryCauseAggroBack(Transform victim)
    {
        if (victim == null) return;

        var victimBehaviours = victim.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < victimBehaviours.Length; i++)
        {
            var b = victimBehaviours[i];
            if (b == null) continue;

            var t = b.GetType();

            var m1 = t.GetMethod("GetShot", new[] { typeof(Transform) });
            if (m1 != null)
            {
                try { m1.Invoke(b, new object[] { rootAttacker }); } catch { }
                return;
            }

            var m2 = t.GetMethod("SetCombatTarget", new[] { typeof(Transform) });
            if (m2 != null)
            {
                try { m2.Invoke(b, new object[] { rootAttacker }); } catch { }
                return;
            }
        }
    }

    private bool IsAllowedTag(string tag)
    {
        for (int i = 0; i < damageableTags.Length; i++)
        {
            if (damageableTags[i] == tag) return true;
        }
        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (rootAttacker == null) rootAttacker = transform;
        Gizmos.DrawWireSphere(rootAttacker.position, attackRange);
    }
#endif
}
