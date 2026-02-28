using System.Reflection;
using UnityEngine;

/// <summary>
/// Damage helper that fits your project:
/// - Your project already defines IDamageable with: void TakeDamage(float amount)
/// - Many other scripts also respond to SendMessageUpwards("TakeDamage", float)
///
/// This utility avoids depending on any specific interface at compile time (no duplicate IDamageable issues)
/// and will successfully hit targets that implement:
///   - TakeDamage(float)
///   - TakeDamage(int)
///   - TakeDamage(float, Transform)
///   - TakeDamage(int, Transform)
/// or that listen via SendMessage/SendMessageUpwards.
/// </summary>
public static class AnimalDamageUtility
{
    public static bool TryApplyDamage(GameObject target, float damage, Transform attacker)
    {
        if (!target) return false;
        if (damage <= 0f) return false;

        var root = target.transform.root ? target.transform.root.gameObject : target;

        // Prefer direct method calls via reflection on components (fast enough, avoids interface mismatch).
        if (TryInvokeTakeDamage(root, damage, attacker, useAttackerParam: true, useFloat: true)) return true;
        if (TryInvokeTakeDamage(root, damage, attacker, useAttackerParam: false, useFloat: true)) return true;

        // Try int overloads too (some of your health scripts use int damage).
        int dmgInt = Mathf.RoundToInt(damage);
        if (dmgInt > 0)
        {
            if (TryInvokeTakeDamage(root, dmgInt, attacker, useAttackerParam: true)) return true;
            if (TryInvokeTakeDamage(root, dmgInt, attacker, useAttackerParam: false)) return true;
        }

        // Fallback: SendMessageUpwards is what your BulletController / Melee already uses.
        // This hits a method anywhere up the hierarchy.
        root.SendMessageUpwards("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        return true; // we can't know if someone received it, but this matches your project's style.
    }

    private static bool TryInvokeTakeDamage(GameObject go, float damage, Transform attacker, bool useAttackerParam, bool useFloat)
    {
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (!b) continue;

            var t = b.GetType();
            MethodInfo mi;

            if (useAttackerParam)
            {
                mi = t.GetMethod(
                    "TakeDamage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(float), typeof(Transform) },
                    modifiers: null
                );
                if (mi != null)
                {
                    mi.Invoke(b, new object[] { damage, attacker });
                    return true;
                }
            }
            else
            {
                mi = t.GetMethod(
                    "TakeDamage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(float) },
                    modifiers: null
                );
                if (mi != null)
                {
                    mi.Invoke(b, new object[] { damage });
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryInvokeTakeDamage(GameObject go, int damage, Transform attacker, bool useAttackerParam)
    {
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (!b) continue;

            var t = b.GetType();
            MethodInfo mi;

            if (useAttackerParam)
            {
                mi = t.GetMethod(
                    "TakeDamage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(int), typeof(Transform) },
                    modifiers: null
                );
                if (mi != null)
                {
                    mi.Invoke(b, new object[] { damage, attacker });
                    return true;
                }
            }
            else
            {
                mi = t.GetMethod(
                    "TakeDamage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(int) },
                    modifiers: null
                );
                if (mi != null)
                {
                    mi.Invoke(b, new object[] { damage });
                    return true;
                }
            }
        }

        return false;
    }
}
