using System;
using UnityEngine;

/// <summary>
/// Simple health component for enemies.
/// IMPORTANT: Do NOT Destroy immediately on 0 HP.
/// Instead: trigger death animation (via MnR.DeathController / Enemy2Controller / Animator trigger),
/// then cleanup after a delay (disable or destroy).
/// </summary>
public class EnemyHealthController : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 5;
    public int currentHealth = 5;

    [Header("Optional hit reaction")]
    public EnemyController theEC;

    [Header("Death")]
    [Tooltip("If true, this script will stop accepting damage once dead.")]
    public bool lockAfterDeath = true;

    [Tooltip("If no MnR.DeathController is found, use this fallback cleanup delay.")]
    public float fallbackCleanupDelay = 6f;

    [Tooltip("If true, fallback cleanup disables the GameObject (pool-friendly). If false, it destroys it.")]
    public bool fallbackDisableInsteadOfDestroy = true;

    // UI can subscribe to this
    public event Action<float> OnHealth01Changed;

    public bool IsDead => _isDead;

    private bool _isDead;

    private void Awake()
    {
        if (maxHealth <= 0) maxHealth = 5;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();
    }

    public void DamageEnemy(int damageAmount)
    {
        if (lockAfterDeath && _isDead) return;

        currentHealth -= damageAmount;

        if (theEC != null)
            theEC.GetShot();

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();

        if (currentHealth <= 0)
            Die();
    }

    /// <summary>
    /// Public death entry point so other systems can kill the enemy cleanly.
    /// </summary>
    public void Die()
    {
        if (_isDead) return;
        _isDead = true;

        currentHealth = 0;
        RaiseHealthChanged();

        // 1) Preferred: MnR.DeathController (plays anim, disables AI/nav, cleans up)
        var deathController = GetComponent<MnR.DeathController>();
        if (deathController != null)
        {
            deathController.Die();
            return;
        }

        // 2) Next: Enemy2Controller (if you wired death there)
        var enemy2 = GetComponent<Enemy2Controller>();
        if (enemy2 != null)
        {
            enemy2.Die();
        }

        // 3) Fallback: trigger Animator parameter "Die" if it exists
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null && HasAnimatorParameter(anim, "Die", AnimatorControllerParameterType.Trigger))
        {
            anim.SetTrigger("Die");
        }

        // Disable common colliders so corpse isn't interactable (fallback behavior)
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && !cols[i].isTrigger)
                cols[i].enabled = false;
        }

        // Stop this health script from doing more work
        if (lockAfterDeath) enabled = false;

        // Fallback cleanup so bodies don't live forever (DeathController handles this on its own)
        if (fallbackCleanupDelay > 0f)
            Invoke(nameof(FallbackCleanup), fallbackCleanupDelay);
    }

    public float Health01()
    {
        return (maxHealth <= 0) ? 0f : (float)currentHealth / maxHealth;
    }

    private void RaiseHealthChanged()
    {
        OnHealth01Changed?.Invoke(Health01());
    }

    private void FallbackCleanup()
    {
        if (fallbackDisableInsteadOfDestroy)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    private static bool HasAnimatorParameter(Animator animator, string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrWhiteSpace(paramName)) return false;
        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == paramName && ps[i].type == type) return true;
        }
        return false;
    }
}
