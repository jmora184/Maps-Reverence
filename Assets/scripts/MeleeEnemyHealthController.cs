using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Standalone health for your melee enemy.
/// Drop this on the melee enemy ROOT (same object as the MeleeEnemy2Controller).
///
/// What this fixes:
/// - Prevents the melee enemy from "coming back to life" after playing a death animation by:
///   1) Notifying the controller (calls Die()/OnDeath/SetDead(true) via reflection)
///   2) Optionally destroying the entire GameObject after a delay
///
/// Notes:
/// - Implements IDamageable so bullets / melee can call TakeDamage(...).
/// - Also exposes common method names (ApplyDamage / ReceiveDamage) for older callers.
/// - Animator params are optional. If the trigger/bool names don’t exist, this script won’t crash.
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemyHealthController : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public int maxHealth = 40;
    public int currentHealth = 40;

    [Header("References")]
    public Animator animator;

    [Tooltip("Optional: your melee enemy controller (any type). If empty, we auto-find one on this GameObject.")]
    public MonoBehaviour meleeController;

    [Header("Animator Params (optional)")]
    public string hitReactTrigger = "take_damage";

    // IMPORTANT: your MeleeEnemy2Controller default expects "Die" (capital D).
    public string dieTrigger = "Die";
    public string isDeadBool = "isDead";

    [Header("Death Cleanup")]
    [Tooltip("If true, the entire enemy GameObject will be destroyed after death.")]
    public bool destroyOnDeath = true;

    [Tooltip("Delay before destroy so the death animation can play. Set to 0 to destroy instantly.")]
    [Min(0f)] public float destroyDelay = 5f;

    [Tooltip("If true, further damage is ignored once dead.")]
    public bool lockAfterDeath = true;

    [Tooltip("If true and isDeadBool is not empty, sets animator bool isDeadBool = true on death.")]
    public bool setIsDeadBoolOnDeath = true;

    [Tooltip("Disable any Behaviour components (scripts) on this GameObject after death. Controller is notified first.")]
    public bool disableBehavioursOnDeath = false;

    [Header("Fallback (if you prefer disable instead of destroy)")]
    [Tooltip("Optional: disable the GameObject after death (useful if you are pooling enemies).")]
    public bool fallbackDisableOnDeath = false;
    public float fallbackDisableDelay = 6f;

    [Header("Debug")]
    public bool debugLogs = false;

    private bool _dead;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();

        if (maxHealth <= 0) maxHealth = 1;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        _dead = currentHealth <= 0;

        if (!meleeController)
            meleeController = AutoFindController();

        // If we start dead (e.g., prefab misconfigured), clean up consistently.
        if (_dead)
            HandleDeathSideEffects();
    }

    // --- IDamageable ---
    public void TakeDamage(float amount) => Apply(amount);

    // Convenience for older callers
    public void TakeDamage(int amount) => Apply(amount);
    public void ApplyDamage(float amount) => Apply(amount);
    public void ApplyDamage(int amount) => Apply(amount);
    public void ReceiveDamage(float amount) => Apply(amount);
    public void ReceiveDamage(int amount) => Apply(amount);

    public bool IsDead => _dead;

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (_dead) return;

        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);
    }

    private void Apply(float amount)
    {
        if (amount <= 0f) return;
        if (lockAfterDeath && _dead) return;

        int dmg = Mathf.Max(1, Mathf.RoundToInt(amount));
        currentHealth = Mathf.Clamp(currentHealth - dmg, 0, maxHealth);

        if (debugLogs)
            Debug.Log($"[MeleeEnemyHealthController] {name} took {dmg}. HP {currentHealth}/{maxHealth}");

        // Hit react
        if (animator && !_dead && !string.IsNullOrEmpty(hitReactTrigger))
            TrySetTriggerSafe(animator, hitReactTrigger);

        NotifyControllerDamaged(dmg);

        if (currentHealth <= 0 && !_dead)
        {
            _dead = true;
            HandleDeathSideEffects();
        }
    }

    private void HandleDeathSideEffects()
    {
        // Animator death params
        if (animator)
        {
            if (setIsDeadBoolOnDeath && !string.IsNullOrEmpty(isDeadBool))
                TrySetBoolSafe(animator, isDeadBool, true);

            if (!string.IsNullOrEmpty(dieTrigger))
                TrySetTriggerSafe(animator, dieTrigger);
        }

        // Tell the controller to actually "die" (stop AI / movement)
        NotifyControllerDeath();

        // Optionally disable other behaviours (after controller notified)
        if (disableBehavioursOnDeath)
        {
            foreach (var b in GetComponents<Behaviour>())
            {
                if (!b) continue;
                if (b == this) continue;
                b.enabled = false;
            }
        }

        // Cleanup: destroy or disable
        if (destroyOnDeath)
        {
            if (destroyDelay <= 0f) Destroy(gameObject);
            else Destroy(gameObject, destroyDelay);
        }
        else if (fallbackDisableOnDeath && fallbackDisableDelay > 0f)
        {
            Invoke(nameof(DisableSelf), fallbackDisableDelay);
        }
    }

    private void DisableSelf()
    {
        gameObject.SetActive(false);
    }

    private static void TrySetTriggerSafe(Animator a, string trigger)
    {
        if (!a) return;
        try { a.ResetTrigger(trigger); a.SetTrigger(trigger); } catch { /* ignore */ }
    }

    private static void TrySetBoolSafe(Animator a, string param, bool value)
    {
        if (!a) return;
        try { a.SetBool(param, value); } catch { /* ignore */ }
    }

    private void NotifyControllerDamaged(int dmg)
    {
        if (!meleeController) return;

        Type t = meleeController.GetType();

        // OnDamaged(int)
        MethodInfo m = t.GetMethod("OnDamaged", BF, null, new[] { typeof(int) }, null);
        if (m != null) { m.Invoke(meleeController, new object[] { dmg }); return; }

        // OnDamaged(float)
        m = t.GetMethod("OnDamaged", BF, null, new[] { typeof(float) }, null);
        if (m != null) { m.Invoke(meleeController, new object[] { (float)dmg }); return; }
    }

    private void NotifyControllerDeath()
    {
        if (!meleeController) return;

        Type t = meleeController.GetType();

        // Prefer Die()/OnDeath()
        MethodInfo m =
            t.GetMethod("Die", BF, null, Type.EmptyTypes, null) ??
            t.GetMethod("OnDeath", BF, null, Type.EmptyTypes, null);

        if (m != null)
        {
            m.Invoke(meleeController, null);
            return;
        }

        // SetDead(bool)
        m = t.GetMethod("SetDead", BF, null, new[] { typeof(bool) }, null);
        if (m != null)
            m.Invoke(meleeController, new object[] { true });
    }

    private MonoBehaviour AutoFindController()
    {
        // Common case: your controller is on the same root object.
        foreach (var mb in GetComponents<MonoBehaviour>())
        {
            if (!mb) continue;
            if (mb == this) continue;

            Type t = mb.GetType();

            // If it has a Die() or SetDead(bool), it's a good candidate.
            if (t.GetMethod("Die", BF, null, Type.EmptyTypes, null) != null) return mb;
            if (t.GetMethod("SetDead", BF, null, new[] { typeof(bool) }, null) != null) return mb;
            if (t.GetMethod("OnDeath", BF, null, Type.EmptyTypes, null) != null) return mb;
        }

        return null;
    }
}
