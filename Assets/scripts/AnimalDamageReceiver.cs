using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AnimalDamageReceiver
/// Attach to the Animal ROOT (same object as AnimalController / HP logic).
///
/// Goal: make the Animal reliably TAKE damage from enemies WITHOUT collider hitboxes.
/// - Implements your existing IDamageable interface so any attacker can call TakeDamage(...)
/// - Also exposes common method names (ApplyDamage/ReceiveDamage) so older scripts can call them.
///
/// Damage forwarding:
/// - Preferred: forward to your existing health/animal controller via method call:
///   TakeDamage / ApplyDamage / ReceiveDamage / Damage (int or float)
/// - Fallback: subtract from common health fields/properties:
///   currentHealth / health / hp (int or float)
///
/// IMPORTANT:
/// This only fixes the "receiver" side.
/// Your enemy/bullet/melee scripts must call IDamageable.TakeDamage(...) (or one of the common method names)
/// on the target. If an enemy script ONLY damages Player/Ally via tag checks, that script still needs updating.
/// </summary>
[DisallowMultipleComponent]
public class AnimalDamageReceiver : MonoBehaviour, IDamageable
{
    [Header("Optional explicit health component")]
    [Tooltip("If set, damage will be forwarded to this component first.")]
    public MonoBehaviour healthComponent;

    [Header("Search Options")]
    public bool searchInParents = true;
    public bool searchInChildren = false;

    [Header("Debug")]
    public bool logDamage = false;

    // Cache to reduce reflection overhead during combat
    private MonoBehaviour _cachedTarget;
    private MethodInfo _cachedMethod;
    private bool _cacheFailed;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // ---- IDamageable ----
    public void TakeDamage(float amount) => Apply(amount);

    // Common alt entry points so older scripts can still hurt the animal.
    public void TakeDamage(int amount) => Apply(amount);
    public void ApplyDamage(float amount) => Apply(amount);
    public void ApplyDamage(int amount) => Apply(amount);
    public void ReceiveDamage(float amount) => Apply(amount);
    public void ReceiveDamage(int amount) => Apply(amount);
    public void Damage(float amount) => Apply(amount);
    public void Damage(int amount) => Apply(amount);

    private void Apply(float amount)
    {
        if (amount <= 0f) return;

        if (logDamage)
            Debug.Log($"[AnimalDamageReceiver] {name} took {amount} damage");

        // 1) Explicit reference
        if (healthComponent && TryForwardToComponent(healthComponent, amount))
            return;

        // 2) Cached
        if (!_cacheFailed && _cachedTarget != null && _cachedMethod != null)
        {
            try
            {
                InvokeCached(amount);
                return;
            }
            catch
            {
                // Cache invalid; fall through to re-find
                _cachedTarget = null;
                _cachedMethod = null;
            }
        }

        // 3) Find best health component on this object / parents / children
        MonoBehaviour target = FindBestHealthComponent();
        if (target == null)
        {
            _cacheFailed = true;
            if (logDamage)
                Debug.LogWarning($"[AnimalDamageReceiver] No suitable health component found on {name}.");
            return;
        }

        if (TryForwardToComponent(target, amount))
            return;

        // 4) Last-resort: direct health field/property mutation
        if (TrySubtractHealthField(target, amount))
            return;

        if (logDamage)
            Debug.LogWarning($"[AnimalDamageReceiver] Found {target.GetType().Name} but couldn't apply damage (no matching method/field).");
    }

    private void InvokeCached(float amount)
    {
        if (_cachedTarget == null || _cachedMethod == null) return;

        var pType = _cachedMethod.GetParameters()[0].ParameterType;
        object arg = (pType == typeof(int)) ? (object)Mathf.RoundToInt(amount) : (object)amount;
        _cachedMethod.Invoke(_cachedTarget, new[] { arg });
    }

    private MonoBehaviour FindBestHealthComponent()
    {
        // Prefer something that looks like a health/controller script
        // We keep it generic because your project has multiple variants.
        MonoBehaviour[] pool;

        if (searchInChildren)
            pool = GetComponentsInChildren<MonoBehaviour>(true);
        else if (searchInParents)
            pool = GetComponentsInParent<MonoBehaviour>(true);
        else
            pool = GetComponents<MonoBehaviour>();

        // First pass: obvious health-ish names
        foreach (var mb in pool)
        {
            if (!mb) continue;
            string n = mb.GetType().Name;
            if (n.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Vitals", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return mb;
            }
        }

        // Second pass: anything with a compatible damage method
        foreach (var mb in pool)
        {
            if (!mb) continue;
            if (FindDamageMethod(mb.GetType()) != null)
                return mb;
        }

        return null;
    }

    private bool TryForwardToComponent(MonoBehaviour target, float amount)
    {
        if (!target) return false;

        // If target itself implements IDamageable, use that directly
        var dmg = target as IDamageable;
        if (dmg != null)
        {
            dmg.TakeDamage(amount);
            Cache(target, target.GetType().GetMethod(nameof(IDamageable.TakeDamage), BF, null, new[] { typeof(float) }, null));
            return true;
        }

        // Reflection method dispatch
        MethodInfo mi = FindDamageMethod(target.GetType());
        if (mi == null) return false;

        try
        {
            var pType = mi.GetParameters()[0].ParameterType;
            object arg = (pType == typeof(int)) ? (object)Mathf.RoundToInt(amount) : (object)amount;
            mi.Invoke(target, new[] { arg });
            Cache(target, mi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo FindDamageMethod(Type t)
    {
        string[] methodNames = { "TakeDamage", "ApplyDamage", "ReceiveDamage", "Damage" };

        foreach (string m in methodNames)
        {
            // float
            var mi = t.GetMethod(m, BF, null, new[] { typeof(float) }, null);
            if (mi != null) return mi;

            // int
            mi = t.GetMethod(m, BF, null, new[] { typeof(int) }, null);
            if (mi != null) return mi;
        }

        return null;
    }

    private bool TrySubtractHealthField(MonoBehaviour target, float amount)
    {
        if (!target) return false;

        string[] names = { "currentHealth", "health", "hp", "CurrentHealth", "Health", "HP" };

        // Fields
        foreach (var n in names)
        {
            var f = target.GetType().GetField(n, BF);
            if (f == null) continue;

            if (f.FieldType == typeof(int))
            {
                int v = (int)f.GetValue(target);
                v = Mathf.Max(0, v - Mathf.RoundToInt(amount));
                f.SetValue(target, v);
                return true;
            }

            if (f.FieldType == typeof(float))
            {
                float v = (float)f.GetValue(target);
                v = Mathf.Max(0f, v - amount);
                f.SetValue(target, v);
                return true;
            }
        }

        // Properties
        foreach (var n in names)
        {
            var p = target.GetType().GetProperty(n, BF);
            if (p == null || !p.CanRead || !p.CanWrite) continue;

            if (p.PropertyType == typeof(int))
            {
                int v = (int)p.GetValue(target);
                v = Mathf.Max(0, v - Mathf.RoundToInt(amount));
                p.SetValue(target, v);
                return true;
            }

            if (p.PropertyType == typeof(float))
            {
                float v = (float)p.GetValue(target);
                v = Mathf.Max(0f, v - amount);
                p.SetValue(target, v);
                return true;
            }
        }

        return false;
    }

    private void Cache(MonoBehaviour target, MethodInfo mi)
    {
        if (!target || mi == null) return;
        _cachedTarget = target;
        _cachedMethod = mi;
        _cacheFailed = false;
    }
}
