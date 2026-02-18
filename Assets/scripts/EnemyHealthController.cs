using System;
using UnityEngine;

/// <summary>
/// Simple health component for enemies.
/// IMPORTANT: Do NOT Destroy immediately on 0 HP.
/// Instead: trigger death animation (via MnR.DeathController / Enemy2Controller / Animator trigger),
/// then cleanup after a delay (disable or destroy).
///
/// UPDATE (return-fire fix):
/// - On taking damage, this now NOTIFIES Enemy2Controller (preferred) so enemies can aggro/return-fire,
///   even if older "EnemyController" references are not assigned.
/// </summary>
public class EnemyHealthController : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 5;
    public int currentHealth = 5;

    [Header("Optional hit reaction (preferred)")]
    [Tooltip("If assigned, will be notified when this enemy takes damage so it can aggro/return-fire.")]
    public Enemy2Controller enemy2Controller;

    [Header("Legacy hit reaction (older setups)")]
    [Tooltip("Legacy controller type used in older versions of the project.")]
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

        // Auto-wire controllers if not set in inspector (safe + helps prevent "standing there" bugs)
        if (enemy2Controller == null)
            enemy2Controller = GetComponent<Enemy2Controller>();

        if (theEC == null)
            theEC = GetComponent<EnemyController>();

        RaiseHealthChanged();
    }

    /// <summary>
    /// Apply damage to the enemy.
    /// NOTE: This method does NOT take an attacker. Enemy2Controller.GetShot() should still aggro/return-fire
    /// using its internal fallback (typically Player) when attacker is unknown.
    /// </summary>
    public void DamageEnemy(int damageAmount)
    {
        if (lockAfterDeath && _isDead) return;

        currentHealth -= damageAmount;

        NotifyHitReaction();

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();

        if (currentHealth <= 0)
            Die();
    }

    private void NotifyHitReaction()
    {
        // Prefer Enemy2Controller (your current AI controller)
        if (enemy2Controller == null)
            enemy2Controller = GetComponent<Enemy2Controller>();

        if (enemy2Controller != null)
        {
            enemy2Controller.GetShot();
            return;
        }

        // Fallback to legacy controller if present
        if (theEC == null)
            theEC = GetComponent<EnemyController>();

        if (theEC != null)
        {
            theEC.GetShot();
        }
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

        // 2) Next: Enemy2Controller (if you've wired death there)
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
        if (animator == null) return false;
        var parms = animator.parameters;
        for (int i = 0; i < parms.Length; i++)
        {
            if (parms[i].name == paramName && parms[i].type == type)
                return true;
        }
        return false;
    }
}
