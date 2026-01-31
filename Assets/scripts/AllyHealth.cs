using System;
using UnityEngine;

/// <summary>
/// AllyHealth (global namespace - compatibility with your existing scripts)
///
/// Fixes:
/// - AllyHealthIcon.cs and Enemy2Controller.cs can find AllyHealth again (no namespace).
/// - Still uses MnR.DeathController when present (fully qualified).
///
/// Provides:
/// - Health01()
/// - event Action<float> OnHealth01Changed
/// - event Action OnDied
/// - DamageAlly(int)
/// </summary>
[DisallowMultipleComponent]
public class AllyHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 20;
    public int currentHealth = 20;

    /// <summary>UI expects this event (0..1).</summary>
    public event Action<float> OnHealth01Changed;

    /// <summary>AI can subscribe to drop this ally as a target.</summary>
    public event Action OnDied;

    [Header("Animator Death")]
    [Tooltip("Assign the SAME Animator that uses your Ally Animator Controller (with 'Die' parameter). If empty, auto-finds in children.")]
    public Animator animator;

    [Tooltip("Animator Trigger name to fire on death (recommended).")]
    public string deathTrigger = "Die";

    [Tooltip("Optional Animator Bool name to set true on death (leave empty if you use Trigger).")]
    public string deathBool = "";

    [Tooltip("Keeps death anim playing even if the ally is off-screen.")]
    public bool forceAlwaysAnimateOnDeath = true;

    [Header("Death Behavior")]
    public bool lockAfterDeath = true;

    [Tooltip("If no DeathController exists, cleanup after this delay.")]
    public float fallbackCleanupDelay = 6f;

    [Tooltip("Fallback cleanup disables object instead of destroying it.")]
    public bool fallbackDisableInsteadOfDestroy = true;

    [Header("Debug")]
    public bool debugLogs = false;

    public bool IsDead => _isDead;
    bool _isDead;

    void Awake()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        RaiseHealthChanged();
    }

    /// <summary>Used by AllyHealthIcon.cs and other UI.</summary>
    public float Health01()
    {
        return maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
    }

    public void DamageAlly(int damageAmount)
    {
        if (lockAfterDeath && _isDead) return;

        currentHealth -= damageAmount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        RaiseHealthChanged();

        if (currentHealth <= 0)
            Die();
    }

    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        currentHealth = 0;
        RaiseHealthChanged();

        if (debugLogs) Debug.Log($"[AllyHealth] Die() on {name}", this);

        // Notify AI immediately so enemies drop this target
        OnDied?.Invoke();

        // Prefer MnR.DeathController if present
        var dc = GetComponent<MnR.DeathController>();
        if (dc != null)
        {
            if (dc.animator == null && animator != null)
                dc.animator = animator;

            if (forceAlwaysAnimateOnDeath && dc.animator != null)
                dc.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (debugLogs) Debug.Log("[AllyHealth] Using MnR.DeathController.Die()", this);
            dc.Die();
            return;
        }

        // Fallback: trigger animator directly
        TriggerAnimatorDeath();

        if (fallbackCleanupDelay > 0f)
            Invoke(nameof(FallbackCleanup), fallbackCleanupDelay);

        if (lockAfterDeath) enabled = false;
    }

    void TriggerAnimatorDeath()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            if (debugLogs) Debug.LogWarning("[AllyHealth] No Animator found for death.", this);
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
            if (debugLogs) Debug.Log($"[AllyHealth] SetTrigger('{deathTrigger}')", this);
        }

        if (!string.IsNullOrWhiteSpace(deathBool) && HasParam(animator, deathBool, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(deathBool, true);
            did = true;
            if (debugLogs) Debug.Log($"[AllyHealth] SetBool('{deathBool}', true)", this);
        }

        if (!did && debugLogs)
        {
            Debug.LogWarning($"[AllyHealth] Animator found but no matching death parameter. Trigger='{deathTrigger}' Bool='{deathBool}'.", this);
        }
    }

    void FallbackCleanup()
    {
        if (fallbackDisableInsteadOfDestroy)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    void RaiseHealthChanged()
    {
        OnHealth01Changed?.Invoke(Health01());
    }

    static bool HasParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (a == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == name && ps[i].type == type) return true;
        return false;
    }
}
