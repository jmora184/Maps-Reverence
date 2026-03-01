using System;
using UnityEngine;

/// <summary>
/// AlienBossHealth
/// - Same pattern as AnimalHealth
/// - Fires Die trigger when health reaches 0
/// - Also fires events so AlienBossController can react and aggro when damaged
/// </summary>
[DisallowMultipleComponent]
public class AlienBossHealth : MonoBehaviour
{
    [Header("Health")]
    [Min(1)] public int maxHealth = 150;
    [SerializeField] private int currentHealth;

    [Header("Animator (optional)")]
    public Animator animator;
    [Tooltip("Animator trigger to fire when the boss dies.")]
    public string dieTrigger = "Die";

    [Tooltip("Optional animator trigger when the boss is hurt.")]
    public string hurtTrigger = "";

    [Header("Death Behaviour")]
    [Tooltip("Disable these behaviours on death (optional). Put AlienBossController + NavMeshAgent here if you want them to stop.")]
    public Behaviour[] disableOnDeath;

    [Tooltip("If true, destroy this GameObject after death.")]
    public bool destroyOnDeath = false;

    [Tooltip("Delay before destroying (lets death animation play).")]
    public float destroyDelay = 5.0f;

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    public event Action<AlienBossHealth, int, Transform> OnDamaged;
    public event Action<AlienBossHealth, Transform> OnDied;

    private bool _deathHandled = false;

    private void Awake()
    {
        if (currentHealth <= 0) currentHealth = maxHealth;
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        _deathHandled = false;
    }

    public void TakeDamage(int amount, Transform attacker)
    {
        if (IsDead) return;
        if (amount <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (animator && !string.IsNullOrWhiteSpace(hurtTrigger))
            animator.SetTrigger(hurtTrigger);

        OnDamaged?.Invoke(this, amount, attacker);

        if (currentHealth <= 0)
            Die(attacker);
    }

    // Compatibility overloads (for your various projectile scripts)
    public void TakeDamage(float amount) => TakeDamage(Mathf.CeilToInt(amount), null);
    public void TakeDamage(int amount) => TakeDamage(amount, null);
    public void TakeDamage(float amount, Transform attacker) => TakeDamage(Mathf.CeilToInt(amount), attacker);
    public void ApplyDamage(int amount) => TakeDamage(amount, null);
    public void ApplyDamage(float amount) => TakeDamage(amount, null);
    public void Damage(int amount) => TakeDamage(amount, null);
    public void Damage(float amount) => TakeDamage(amount, null);
    public void ReceiveDamage(int amount) => TakeDamage(amount, null);
    public void ReceiveDamage(float amount) => TakeDamage(amount, null);
    public void Hurt(int amount) => TakeDamage(amount, null);
    public void Hurt(float amount) => TakeDamage(amount, null);

    private void Die(Transform killer)
    {
        if (_deathHandled) return;
        _deathHandled = true;

        if (animator && !string.IsNullOrWhiteSpace(dieTrigger))
            animator.SetTrigger(dieTrigger);

        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
                if (b) b.enabled = false;
        }

        OnDied?.Invoke(this, killer);

        if (destroyOnDeath)
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
    }
}
