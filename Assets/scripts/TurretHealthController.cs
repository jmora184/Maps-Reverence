using System;
using UnityEngine;

/// <summary>
/// Simple health controller for a stationary turret.
/// Compatible with the "minimal" TurretController (no MarkDestroyed required).
/// - TakeDamage / ApplyDamage
/// - Spawns explosion VFX on death
/// - Disables TurretController + colliders on death
/// - Destroys turret GameObject after a delay
/// - Exposes OnHealth01Changed / Health01() so a world health bar can bind to it
/// </summary>
[DisallowMultipleComponent]
public class TurretHealthController : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 120f;
    [SerializeField] private float currentHealth;

    [Header("Death")]
    [Tooltip("Explosion VFX prefab to spawn on death (optional).")]
    public GameObject explosionPrefab;

    [Tooltip("Offset for where the explosion spawns (use Y to move it up).")]
    public Vector3 explosionOffset = new Vector3(0f, 1.2f, 0f);

    [Tooltip("Seconds to wait before destroying the turret after death (lets VFX play).")]
    public float destroyDelay = 2.5f;

    [Tooltip("Disable all colliders on death to prevent further hits / interactions.")]
    public bool disableCollidersOnDeath = true;

    [Tooltip("Disable TurretController on death.")]
    public bool disableTurretControllerOnDeath = true;

    [Tooltip("If true, destroys the entire GameObject on death.")]
    public bool destroyGameObjectOnDeath = true;

    // UI can subscribe to this exactly like your enemy health setup.
    public event Action<float> OnHealth01Changed;

    private bool _isDead;

    private void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth <= 0f ? maxHealth : currentHealth, 0f, maxHealth);
        RaiseHealthChanged();
    }

    /// <summary>
    /// Generic damage entry point used by many of your scripts.
    /// </summary>
    public void TakeDamage(float amount)
    {
        ApplyDamage(amount, null);
    }

    /// <summary>
    /// Damage entry point with attacker info (optional).
    /// </summary>
    public void ApplyDamage(float amount, Transform attacker)
    {
        if (_isDead) return;
        if (amount <= 0f) return;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        RaiseHealthChanged();

        if (currentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;
        currentHealth = 0f;
        RaiseHealthChanged();

        // Stop turret behavior
        if (disableTurretControllerOnDeath)
        {
            var turret = GetComponent<TurretController>();
            if (turret != null) turret.enabled = false;
        }

        // Disable colliders so nothing keeps "hitting" it
        if (disableCollidersOnDeath)
        {
            foreach (var col in GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        // Spawn explosion VFX
        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position + explosionOffset, Quaternion.identity);

        // Destroy
        if (destroyGameObjectOnDeath)
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
    }

    public float Health01()
    {
        return maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    }

    private void RaiseHealthChanged()
    {
        OnHealth01Changed?.Invoke(Health01());
    }

    // Optional helpers
    public bool IsDead => _isDead;
    public float CurrentHealth => currentHealth;
}
