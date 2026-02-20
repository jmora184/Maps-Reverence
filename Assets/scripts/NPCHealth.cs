using System;
using UnityEngine;

/// <summary>
/// NPCHealth
/// - Simple health + death events for NPCs.
/// - Compatible with your existing death flow via MnR.DeathController (if present).
/// - Notifies NPCController when damaged so the NPC can become hostile.
/// </summary>
[DisallowMultipleComponent]
public class NPCHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 20;
    public int currentHealth = 20;

    public event Action<float> OnHealth01Changed;
    public event Action OnDied;

    [Header("Animator Death")]
    public Animator animator;
    public string deathTrigger = "Die";
    public string deathBool = "";
    public bool forceAlwaysAnimateOnDeath = true;

    [Header("Death Behavior")]
    public bool lockAfterDeath = true;
    public float fallbackCleanupDelay = 6f;
    public bool fallbackDisableInsteadOfDestroy = true;

    [Header("Debug")]
    public bool debugLogs = false;

    public bool IsDead => _isDead;
    private bool _isDead;

    private NPCController _npc;

    void Awake()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        _npc = GetComponent<NPCController>();
        RaiseHealthChanged();
    }

    public float Health01() => maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);

    /// <summary>
    /// Convenience: old-style "DamageX" naming.
    /// </summary>
    public void DamageNPC(int damageAmount) => TakeDamage(damageAmount, null);

    /// <summary>
    /// Preferred entry point.
    /// </summary>
    public void TakeDamage(int damageAmount, Transform attacker)
    {
        if (lockAfterDeath && _isDead) return;
        if (damageAmount <= 0) return;

        currentHealth -= damageAmount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();

        if (_npc != null)
            _npc.OnTookDamage(attacker);

        if (currentHealth <= 0)
            Die();
    }

    public void Heal(int amount)
    {
        if (lockAfterDeath && _isDead) return;
        if (amount <= 0) return;

        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);
        RaiseHealthChanged();
    }

    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        currentHealth = 0;
        RaiseHealthChanged();

        if (debugLogs) Debug.Log($"[NPCHealth] Die() on {name}", this);

        OnDied?.Invoke();

        // Prefer MnR.DeathController if your project uses it
        var dc = GetComponent<MnR.DeathController>();
        if (dc != null)
        {
            if (dc.animator == null && animator != null)
                dc.animator = animator;

            if (forceAlwaysAnimateOnDeath && dc.animator != null)
                dc.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (debugLogs) Debug.Log("[NPCHealth] Using MnR.DeathController.Die()", this);
            dc.Die();
            if (lockAfterDeath) enabled = false;
            return;
        }

        TriggerAnimatorDeath();

        if (_npc != null)
            _npc.OnDied();

        if (fallbackCleanupDelay > 0f)
            Invoke(nameof(FallbackCleanup), fallbackCleanupDelay);

        if (lockAfterDeath) enabled = false;
    }

    private void TriggerAnimatorDeath()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            if (debugLogs) Debug.LogWarning("[NPCHealth] No Animator found for death.", this);
            return;
        }

        if (forceAlwaysAnimateOnDeath)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        bool did = false;

        if (!string.IsNullOrWhiteSpace(deathTrigger) && HasParam(animator, deathTrigger, AnimatorControllerParameterType.Trigger))
        {
            animator.ResetTrigger(deathTrigger);
            animator.SetTrigger(deathTrigger);
            did = true;
        }

        if (!string.IsNullOrWhiteSpace(deathBool) && HasParam(animator, deathBool, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(deathBool, true);
            did = true;
        }

        if (!did && debugLogs)
            Debug.LogWarning($"[NPCHealth] Animator found but no matching death param. Trigger='{deathTrigger}' Bool='{deathBool}'.", this);
    }

    private void FallbackCleanup()
    {
        if (fallbackDisableInsteadOfDestroy)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    private void RaiseHealthChanged()
    {
        OnHealth01Changed?.Invoke(Health01());
    }

    private static bool HasParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (a == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == name && ps[i].type == type) return true;
        return false;
    }
}
