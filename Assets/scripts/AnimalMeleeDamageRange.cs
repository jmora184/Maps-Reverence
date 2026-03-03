using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AnimalMeleeDamageRange
/// Range-gated melee damage WITHOUT relying on hitbox colliders.
/// 
/// Pattern (same as your AlienBossMeleeDamageRange):
/// - Detects when the Animator ENTERS an attack animation state (by exact state name).
/// - On entering attack state, damages the animal's current target IF within attackRange.
/// 
/// Target resolution:
/// - Prefer explicit "animal" reference (your AnimalController).
/// - If provided, we reflect for a current target Transform/GameObject on that controller.
///   Common names it will try:
///     currentTarget, target, combatTarget, enemyTarget, chaseTarget, attackTarget, targetTransform
/// - If nothing is found, you can set targetOverride at runtime via SetTarget(..).
///
/// Damage application:
/// - Tries IDamageable.TakeDamage(float)
/// - Then tries methods: TakeDamage(int/float), ApplyDamage(int/float), ReceiveDamage(int/float), Damage(int/float)
/// - As a last resort, SendMessage("TakeDamage", ..)
///
/// Optional "aggro back":
/// - After damage, tries to call victim methods to set their target to THIS animal:
///   SetCombatTarget(GameObject/Transform), SetTarget(GameObject/Transform), GetShot(GameObject), OnProvoked(GameObject)
/// </summary>
[DisallowMultipleComponent]
public class AnimalMeleeDamageRange : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your AnimalController (optional but recommended).")]
    public MonoBehaviour animal;

    [Tooltip("Animator on the animal (auto-finds if null).")]
    public Animator animator;

    [Header("Attack State Detection")]
    [Tooltip("Exact Animator state names on Layer 0 that represent attacks (case-sensitive).")]
    public string[] attackStateNames = new string[] { "attack", "bite", "pound", "swing" };

    [Tooltip("Animator layer index to watch (0 is default).")]
    public int layerIndex = 0;

    [Header("Range Gate")]
    [Tooltip("Target must be within this distance (from rangeOrigin) to take damage.")]
    public float attackRange = 2.2f;

    [Tooltip("Where range is measured from. If null, uses this transform.")]
    public Transform rangeOrigin;

    [Header("Damage")]
    [Tooltip("Damage applied per attack entry.")]
    public int damage = 10;

    [Tooltip("Minimum seconds between damage applications (prevents double-hits when transitions are weird).")]
    public float damageCooldown = 0.75f;

    [Header("Behavior")]
    [Tooltip("If true, after damaging, try to make the victim AI aggro back onto this animal.")]
    public bool provokeVictimToAggroBack = true;

    [Header("Debug")]
    public bool logDamage = false;

    // Optional external override (if you want to drive targets manually)
    [NonSerialized] public Transform targetOverride;

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

        bool inAttack = IsInAttackState(animator, layerIndex, attackStateNames);

        // ENTER attack -> apply damage once (cooldown guarded)
        if (inAttack && !_wasInAttack)
        {
            TryDamageCurrentTarget();
        }

        _wasInAttack = inAttack;
    }

    public void SetTarget(Transform t) => targetOverride = t;

    private void TryDamageCurrentTarget()
    {
        if (Time.time < _nextDamageTime) return;

        Transform t = ResolveTargetTransform();
        if (!t) return;

        float dist = Vector3.Distance(rangeOrigin.position, t.position);
        if (dist > attackRange) return;

        // Try apply damage to the victim object or parent (if target is a child bone, etc.)
        GameObject victimGO = t.gameObject;

        bool didDamage = TryApplyDamage(victimGO, damage);

        if (!didDamage)
        {
            // common: damage receiver is on parent/root
            var parent = t.GetComponentInParent<Transform>();
            if (parent && parent != t)
                didDamage = TryApplyDamage(parent.gameObject, damage);
        }

        if (didDamage)
        {
            _nextDamageTime = Time.time + Mathf.Max(0.01f, damageCooldown);

            if (logDamage)
                Debug.Log($"[AnimalMeleeDamageRange] {name} dealt {damage} to {victimGO.name} (dist {dist:0.00})");

            if (provokeVictimToAggroBack)
                TryProvokeVictim(victimGO);
        }
        else
        {
            if (logDamage)
                Debug.LogWarning($"[AnimalMeleeDamageRange] Could not find a damage receiver on {victimGO.name} or its parents.");
        }
    }

    private Transform ResolveTargetTransform()
    {
        if (targetOverride) return targetOverride;

        if (!animal) return null;

        // Try common fields/properties on your controller
        string[] names =
        {
            "currentTarget","target","combatTarget","enemyTarget","chaseTarget","attackTarget","targetTransform"
        };

        foreach (var n in names)
        {
            // Field?
            var f = animal.GetType().GetField(n, BF);
            if (f != null)
            {
                object v = f.GetValue(animal);
                if (v is Transform ft) return ft;
                if (v is GameObject fgo) return fgo ? fgo.transform : null;
                if (v is Component fc) return fc ? fc.transform : null;
            }

            // Property?
            var p = animal.GetType().GetProperty(n, BF);
            if (p != null)
            {
                object v = p.GetValue(animal, null);
                if (v is Transform pt) return pt;
                if (v is GameObject pgo) return pgo ? pgo.transform : null;
                if (v is Component pc) return pc ? pc.transform : null;
            }
        }

        return null;
    }

    private static bool IsInAttackState(Animator a, int layer, string[] stateNames)
    {
        if (a == null) return false;

        AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(layer);

        for (int i = 0; i < stateNames.Length; i++)
        {
            var n = stateNames[i];
            if (!string.IsNullOrEmpty(n) && s.IsName(n))
                return true;
        }

        return false;
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

        // 3) SendMessage fallback
        try
        {
            victim.SendMessage("TakeDamage", (float)amount, SendMessageOptions.DontRequireReceiver);
            victim.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
            return true; // can't know if received; assume yes
        }
        catch { /* ignore */ }

        return false;
    }

    private static bool TryInvokeDamageMethod(MonoBehaviour mb, int amount)
    {
        Type t = mb.GetType();

        // Try a few common method names/signatures.
        string[] methodNames = { "TakeDamage", "ApplyDamage", "ReceiveDamage", "Damage" };

        foreach (string name in methodNames)
        {
            // int
            MethodInfo mi = t.GetMethod(name, BF, null, new[] { typeof(int) }, null);
            if (mi != null) { mi.Invoke(mb, new object[] { amount }); return true; }

            // float
            mi = t.GetMethod(name, BF, null, new[] { typeof(float) }, null);
            if (mi != null) { mi.Invoke(mb, new object[] { (float)amount }); return true; }
        }

        // Sometimes damage methods are on a "health" field
        string[] healthFieldNames = { "health", "Health", "hp", "HP" };
        foreach (var hf in healthFieldNames)
        {
            FieldInfo f = t.GetField(hf, BF);
            if (f == null) continue;

            object hObj = f.GetValue(mb);
            if (hObj == null) continue;

            Type ht = hObj.GetType();
            MethodInfo mi = ht.GetMethod("TakeDamage", BF, null, new[] { typeof(int) }, null)
                          ?? ht.GetMethod("TakeDamage", BF, null, new[] { typeof(float) }, null)
                          ?? ht.GetMethod("ApplyDamage", BF, null, new[] { typeof(int) }, null)
                          ?? ht.GetMethod("ApplyDamage", BF, null, new[] { typeof(float) }, null);

            if (mi != null)
            {
                var param = mi.GetParameters()[0].ParameterType == typeof(int) ? (object)amount : (object)(float)amount;
                mi.Invoke(hObj, new[] { param });
                return true;
            }
        }

        return false;
    }

    private void TryProvokeVictim(GameObject victim)
    {
        if (!victim) return;

        // Call on victim parents too (controllers often live on root)
        MonoBehaviour[] mbs = victim.GetComponentsInParent<MonoBehaviour>(true);
        foreach (var mb in mbs)
        {
            if (!mb) continue;

            Type t = mb.GetType();

            // SetCombatTarget(GameObject/Transform)
            if (TryInvokeTargetSetter(t, mb, "SetCombatTarget")) return;
            if (TryInvokeTargetSetter(t, mb, "SetTarget")) return;

            // GetShot(GameObject) - your older pattern
            MethodInfo gs = t.GetMethod("GetShot", BF, null, new[] { typeof(GameObject) }, null);
            if (gs != null) { gs.Invoke(mb, new object[] { gameObject }); return; }

            // OnProvoked(GameObject)
            MethodInfo op = t.GetMethod("OnProvoked", BF, null, new[] { typeof(GameObject) }, null);
            if (op != null) { op.Invoke(mb, new object[] { gameObject }); return; }
        }
    }

    private bool TryInvokeTargetSetter(Type t, MonoBehaviour mb, string methodName)
    {
        MethodInfo mGO = t.GetMethod(methodName, BF, null, new[] { typeof(GameObject) }, null);
        if (mGO != null) { mGO.Invoke(mb, new object[] { gameObject }); return true; }

        MethodInfo mT = t.GetMethod(methodName, BF, null, new[] { typeof(Transform) }, null);
        if (mT != null) { mT.Invoke(mb, new object[] { transform }); return true; }

        return false;
    }
}

