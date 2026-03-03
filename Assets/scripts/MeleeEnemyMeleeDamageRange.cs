using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// MeleeEnemyMeleeDamageRange
/// Melee damage WITHOUT hitbox colliders:
/// - When the Animator ENTERS an attack animation state, deal damage ONCE if target is within range.
/// - Target can be pulled from your melee controller (via reflection) or found by tag in range.
///
/// Works with AnimalDamageReceiver (animals implement IDamageable now).
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemyMeleeDamageRange : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your melee enemy controller script (optional but recommended). Used to resolve/set target.")]
    public MonoBehaviour meleeController;

    [Tooltip("Animator (auto-finds if null).")]
    public Animator animator;

    [Header("Attack State Detection")]
    [Tooltip("Exact Animator state names on layer 0 that represent attacks (case-sensitive).")]
    public string[] attackStateNames = new string[] { "attack", "swing", "punch", "slash" };

    public int layerIndex = 0;

    [Header("Range Gate")]
    public float attackRange = 2.2f;
    public Transform rangeOrigin;

    [Header("Damage")]
    public int damage = 10;
    public float damageCooldown = 0.75f;

    [Header("Targeting")]
    [Tooltip("Tags that this melee enemy is allowed to damage.")]
    public string[] allowedTargetTags = new string[] { "Player", "Ally", "Animal", "Enemy" };

    [Tooltip("If true and controller target is null/out of range, find closest allowed target within range.")]
    public bool fallbackFindTargetInRange = true;

    [Tooltip("If true, when fallback finds a target it will try to set it on the controller (combatTarget/target).")]
    public bool fallbackAssignTargetToController = true;

    [Header("Debug")]
    public bool logDamage = false;

    private bool _wasInAttack;
    private float _nextDamageTime;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!rangeOrigin) rangeOrigin = transform;
    }

    private void Update()
    {
        if (!animator) return;

        bool inAttack = IsInAnyState(animator, layerIndex, attackStateNames);

        if (inAttack && !_wasInAttack)
        {
            TryDamage();
        }

        _wasInAttack = inAttack;
    }

    private void TryDamage()
    {
        if (Time.time < _nextDamageTime) return;

        Transform target = ResolveTarget();

        if (!target && fallbackFindTargetInRange)
        {
            target = FindClosestAllowedTargetInRange();
            if (target && fallbackAssignTargetToController)
                TrySetControllerTarget(target);
        }

        if (!target) return;

        float dist = Vector3.Distance(rangeOrigin.position, target.position);
        if (dist > attackRange) return;

        bool did = TryApplyDamage(target.gameObject, damage);
        if (did)
        {
            _nextDamageTime = Time.time + Mathf.Max(0.01f, damageCooldown);

            if (logDamage)
                Debug.Log($"[MeleeEnemyMeleeDamageRange] {name} hit {target.name} for {damage} (dist {dist:0.00})");
        }
        else if (logDamage)
        {
            Debug.LogWarning($"[MeleeEnemyMeleeDamageRange] Could not find damage receiver on {target.name}.");
        }
    }

    private Transform ResolveTarget()
    {
        if (!meleeController) return null;

        // common names in your project style
        string[] names = { "combatTarget", "target", "currentTarget", "enemyTarget", "attackTarget", "chaseTarget", "targetTransform" };

        foreach (var n in names)
        {
            var f = meleeController.GetType().GetField(n, BF);
            if (f != null)
            {
                object v = f.GetValue(meleeController);
                if (v is Transform ft) return ft;
                if (v is GameObject fgo) return fgo ? fgo.transform : null;
                if (v is Component fc) return fc ? fc.transform : null;
            }

            var p = meleeController.GetType().GetProperty(n, BF);
            if (p != null)
            {
                object v = p.GetValue(meleeController, null);
                if (v is Transform pt) return pt;
                if (v is GameObject pgo) return pgo ? pgo.transform : null;
                if (v is Component pc) return pc ? pc.transform : null;
            }
        }

        return null;
    }

    private void TrySetControllerTarget(Transform t)
    {
        if (!meleeController || !t) return;

        // Try methods first
        Type ct = meleeController.GetType();

        MethodInfo m =
            ct.GetMethod("SetCombatTarget", BF, null, new[] { typeof(Transform) }, null) ??
            ct.GetMethod("SetTarget", BF, null, new[] { typeof(Transform) }, null);
        if (m != null) { m.Invoke(meleeController, new object[] { t }); return; }

        m =
            ct.GetMethod("SetCombatTarget", BF, null, new[] { typeof(GameObject) }, null) ??
            ct.GetMethod("SetTarget", BF, null, new[] { typeof(GameObject) }, null);
        if (m != null) { m.Invoke(meleeController, new object[] { t.gameObject }); return; }

        // Otherwise, try common fields
        string[] fields = { "combatTarget", "target", "currentTarget" };
        foreach (var fn in fields)
        {
            var f = ct.GetField(fn, BF);
            if (f == null) continue;

            if (f.FieldType == typeof(Transform)) { f.SetValue(meleeController, t); return; }
            if (f.FieldType == typeof(GameObject)) { f.SetValue(meleeController, t.gameObject); return; }
        }
    }

    private Transform FindClosestAllowedTargetInRange()
    {
        Transform best = null;
        float bestDist = float.MaxValue;

        Vector3 origin = rangeOrigin ? rangeOrigin.position : transform.position;

        for (int i = 0; i < allowedTargetTags.Length; i++)
        {
            string tag = allowedTargetTags[i];
            if (string.IsNullOrEmpty(tag)) continue;

            GameObject[] gos;
            try { gos = GameObject.FindGameObjectsWithTag(tag); }
            catch { continue; } // tag might not exist in project

            for (int g = 0; g < gos.Length; g++)
            {
                var go = gos[g];
                if (!go || go == gameObject) continue;

                float d = Vector3.Distance(origin, go.transform.position);
                if (d <= attackRange && d < bestDist)
                {
                    bestDist = d;
                    best = go.transform;
                }
            }
        }

        return best;
    }

    private bool TryApplyDamage(GameObject victim, int amount)
    {
        if (!victim) return false;

        // 1) Interface
        var dmg = victim.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(amount);
            return true;
        }

        // 2) Reflection methods on parent chain
        MonoBehaviour[] mbs = victim.GetComponentsInParent<MonoBehaviour>(true);
        foreach (var mb in mbs)
        {
            if (!mb) continue;
            if (TryInvokeDamageMethod(mb, amount))
                return true;
        }

        // 3) SendMessage fallback (best effort)
        try
        {
            victim.SendMessage("TakeDamage", (float)amount, SendMessageOptions.DontRequireReceiver);
            victim.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
            return true;
        }
        catch { /* ignore */ }

        return false;
    }

    private static bool TryInvokeDamageMethod(MonoBehaviour mb, int amount)
    {
        Type t = mb.GetType();
        string[] methodNames = { "TakeDamage", "ApplyDamage", "ReceiveDamage", "Damage" };

        foreach (string name in methodNames)
        {
            MethodInfo mi = t.GetMethod(name, BF, null, new[] { typeof(int) }, null);
            if (mi != null) { mi.Invoke(mb, new object[] { amount }); return true; }

            mi = t.GetMethod(name, BF, null, new[] { typeof(float) }, null);
            if (mi != null) { mi.Invoke(mb, new object[] { (float)amount }); return true; }
        }

        return false;
    }

    private static bool IsInAnyState(Animator a, int layer, string[] stateNames)
    {
        if (!a) return false;

        AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(layer);
        for (int i = 0; i < stateNames.Length; i++)
        {
            var n = stateNames[i];
            if (!string.IsNullOrEmpty(n) && s.IsName(n))
                return true;
        }
        return false;
    }
}
