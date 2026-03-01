// AntAttackDamageGlobal.cs
// "Brute-force" attack damage: when the ant attacks, the player takes damage
// regardless of distance/position.
//
// This is intentionally NOT realistic bite hit detection—it's a last-resort
// guaranteed damage hook so you can keep moving.
//
// How to use:
// 1) Add this script to your ant root (the object that owns the attack/Animator).
// 2) Assign playerTransform (optional). If left empty, it will find the player by tag.
// 3) Call DealAttackDamage() at the exact moment your ant attack should deal damage:
//    - Best: Animation Event on the attack clip -> DealAttackDamage
//    - Or: from your AI code when you trigger the attack
//
// Damage routing (same pattern as your other scripts):
// - Preferred: IDamageable.TakeDamage(float)
// - Fallback: reflection on TakeDamage/ApplyDamage/Damage/ReceiveDamage/Hurt (int or float)
//
// Safety:
// - Cooldown prevents damage from firing multiple times per attack spam.
// - Optional "requireAttackState" lets you gate damage to a specific Animator state name.

using System;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class AntAttackDamageGlobal : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Optional explicit reference. If null, we find by Player Tag.")]
    public Transform playerTransform;

    [Tooltip("Tag used to find the player if playerTransform is not assigned.")]
    public string playerTag = "Player";

    [Header("Damage")]
    public int damage = 5;

    [Tooltip("Minimum seconds between successful damage applications.")]
    public float damageCooldown = 0.75f;

    [Header("Optional Animator Gate")]
    [Tooltip("If enabled, damage only applies when the Animator is currently in the named state (layer 0).")]
    public bool requireAttackState = false;

    [Tooltip("Exact Animator state name (layer 0), e.g. 'Attack' or 'Bite'.")]
    public string requiredStateName = "Attack";

    [Header("Debug")]
    public bool logDamage = false;

    private float _nextDamageTime;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Call this from an Animation Event or AI code when the ant attack should deal damage.
    /// </summary>
    public void DealAttackDamage()
    {
        if (Time.time < _nextDamageTime)
            return;

        if (requireAttackState && !IsInRequiredAnimatorState())
            return;

        Transform target = GetPlayerTransform();
        if (target == null)
        {
            if (logDamage)
                Debug.LogWarning("[AntAttackDamageGlobal] No player found (missing ref and no object with Player tag).", this);
            return;
        }

        if (ApplyDamage(target))
        {
            _nextDamageTime = Time.time + Mathf.Max(0.05f, damageCooldown);

            if (logDamage)
                Debug.Log($"[AntAttackDamageGlobal] Dealt {damage} global damage to player.", this);
        }
        else
        {
            if (logDamage)
                Debug.LogWarning("[AntAttackDamageGlobal] Player found, but no IDamageable/TakeDamage method found on it or its parents.", this);
        }
    }

    private Transform GetPlayerTransform()
    {
        if (playerTransform != null) return playerTransform;

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) return go.transform;
        }

        return null;
    }

    private bool IsInRequiredAnimatorState()
    {
        var anim = GetComponentInChildren<Animator>();
        if (anim == null) return false;

        // Layer 0 state
        AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
        return st.IsName(requiredStateName);
    }

    private bool ApplyDamage(Transform player)
    {
        if (player == null) return false;

        // Preferred interface
        var dmg = player.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage);
            return true;
        }

        // Reflection fallback: scan behaviours on parents
        MonoBehaviour[] monos = player.GetComponentsInParent<MonoBehaviour>(true);
        if (monos == null || monos.Length == 0) return false;

        foreach (var mb in monos)
        {
            if (mb == null) continue;
            if (TryInvokeDamageMethod(mb, "TakeDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "ApplyDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "Damage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "ReceiveDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "Hurt", damage)) return true;
        }

        return false;
    }

    private bool TryInvokeDamageMethod(MonoBehaviour target, string methodName, int dmgAmount)
    {
        Type t = target.GetType();

        MethodInfo mInt = t.GetMethod(methodName, BF, null, new Type[] { typeof(int) }, null);
        if (mInt != null)
        {
            mInt.Invoke(target, new object[] { dmgAmount });
            return true;
        }

        MethodInfo mFloat = t.GetMethod(methodName, BF, null, new Type[] { typeof(float) }, null);
        if (mFloat != null)
        {
            mFloat.Invoke(target, new object[] { (float)dmgAmount });
            return true;
        }

        return false;
    }
}
