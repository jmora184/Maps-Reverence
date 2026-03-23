using System;
using UnityEngine;

/// <summary>
/// Health component for animals.
/// - Tracks current/max health
/// - Raises OnDamaged / OnDied events
/// - Triggers animator on death (Die trigger by default)
/// - Optional: destroy GameObject after death
/// </summary>
[DisallowMultipleComponent]
public class AnimalHealth : MonoBehaviour
{
    [Header("Health")]
    [Min(1)] public int maxHealth = 30;
    [SerializeField] private int currentHealth;

    [Header("Animator (optional)")]
    public Animator animator;
    [Tooltip("Animator trigger to fire when the animal dies.")]
    public string dieTrigger = "Die";
    [Tooltip("Animator trigger to fire when the animal is hurt (optional).")]
    public string hurtTrigger = "";

    [Header("Death SFX")]
    [Tooltip("Sound played when the animal dies.")]
    public AudioClip dieSfx;
    [Tooltip("Optional dedicated audio source. If empty, the script will try to find one on this object.")]
    public AudioSource dieAudioSource;
    [Range(0f, 1f)] public float dieSfxVolume = 1f;

    [Tooltip("If true, creates a temporary detached audio object so the death sound can finish even if this animal gets disabled or destroyed.")]
    public bool useDetachedDeathAudio = true;

    [Tooltip("0 = 2D, 1 = fully 3D. Start with 0 for testing if you cannot hear it.")]
    [Range(0f, 1f)] public float deathSpatialBlend = 0f;

    [Tooltip("Minimum distance for 3D death audio.")]
    public float deathMinDistance = 2f;

    [Tooltip("Maximum distance for 3D death audio.")]
    public float deathMaxDistance = 25f;

    [Tooltip("Legacy fallback. If detached audio is off, uses PlayClipAtPoint so the death sound can still be heard even if this object is disabled or destroyed.")]
    public bool usePlayClipAtPointForDeath = true;

    [Header("Death Behaviour")]
    [Tooltip("Disable these behaviours on death (optional). Put AnimalController + NavMeshAgent here if you want them to stop.")]
    public Behaviour[] disableOnDeath;

    [Tooltip("If true, destroy this GameObject after death.")]
    public bool destroyOnDeath = false;

    [Tooltip("Delay before destroying (lets death animation play).")]
    public float destroyDelay = 3.0f;

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    /// <summary> attacker may be null (environment damage etc). </summary>
    public event Action<AnimalHealth, int, Transform> OnDamaged;
    public event Action<AnimalHealth, Transform> OnDied;

    private bool _deathHandled = false;

    private void Awake()
    {
        if (currentHealth <= 0) currentHealth = maxHealth;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!dieAudioSource) dieAudioSource = GetComponent<AudioSource>();
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        _deathHandled = false;
    }

    /// <summary>
    /// Standard damage entry point.
    /// </summary>
    public void TakeDamage(int amount, Transform attacker)
    {
        if (IsDead) return;
        if (amount <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (animator && !string.IsNullOrWhiteSpace(hurtTrigger))
            animator.SetTrigger(hurtTrigger);

        // Let AI react immediately
        OnDamaged?.Invoke(this, amount, attacker);

        if (currentHealth <= 0)
            Die(attacker);
    }

    // --- Compatibility overloads (so bullets/melee can damage animals using common method names) ---

    /// <summary>
    /// Common interface-style entry point used by many projectiles.
    /// </summary>
    public void TakeDamage(float amount)
    {
        TakeDamage(Mathf.CeilToInt(amount), null);
    }

    /// <summary>
    /// Some scripts call TakeDamage(int) without attacker context.
    /// </summary>
    public void TakeDamage(int amount)
    {
        TakeDamage(amount, null);
    }

    /// <summary>
    /// Some scripts send attacker context with float damage.
    /// </summary>
    public void TakeDamage(float amount, Transform attacker)
    {
        TakeDamage(Mathf.CeilToInt(amount), attacker);
    }

    /// <summary>
    /// Alias used by older scripts.
    /// </summary>
    public void ApplyDamage(int amount)
    {
        TakeDamage(amount, null);
    }

    public void ApplyDamage(float amount)
    {
        TakeDamage(amount, null);
    }

    public void Damage(int amount)
    {
        TakeDamage(amount, null);
    }

    public void Damage(float amount)
    {
        TakeDamage(amount, null);
    }

    public void ReceiveDamage(int amount)
    {
        TakeDamage(amount, null);
    }

    public void ReceiveDamage(float amount)
    {
        TakeDamage(amount, null);
    }

    public void Hurt(int amount)
    {
        TakeDamage(amount, null);
    }

    public void Hurt(float amount)
    {
        TakeDamage(amount, null);
    }

    public void PlayDieSfx()
    {
        if (!dieSfx) return;

        Vector3 pos = transform.position;

        if (useDetachedDeathAudio)
        {
            GameObject temp = new GameObject(name + "_DeathAudio");
            temp.transform.position = pos;

            var src = temp.AddComponent<AudioSource>();
            src.clip = dieSfx;
            src.volume = dieSfxVolume;
            src.spatialBlend = deathSpatialBlend;
            src.minDistance = Mathf.Max(0.01f, deathMinDistance);
            src.maxDistance = Mathf.Max(src.minDistance, deathMaxDistance);
            src.playOnAwake = false;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();

            Destroy(temp, dieSfx.length + 0.25f);
            return;
        }

        if (usePlayClipAtPointForDeath)
        {
            AudioSource.PlayClipAtPoint(dieSfx, pos, dieSfxVolume);
            return;
        }

        if (!dieAudioSource) dieAudioSource = GetComponent<AudioSource>();
        if (dieAudioSource)
        {
            dieAudioSource.spatialBlend = deathSpatialBlend;
            dieAudioSource.minDistance = Mathf.Max(0.01f, deathMinDistance);
            dieAudioSource.maxDistance = Mathf.Max(dieAudioSource.minDistance, deathMaxDistance);
            dieAudioSource.rolloffMode = AudioRolloffMode.Linear;
            dieAudioSource.PlayOneShot(dieSfx, dieSfxVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(dieSfx, pos, dieSfxVolume);
        }
    }

    private void Die(Transform killer)
    {
        if (_deathHandled) return;
        _deathHandled = true;

        // Trigger animation
        if (animator && !string.IsNullOrWhiteSpace(dieTrigger))
            animator.SetTrigger(dieTrigger);

        // Play death sound before disabling anything
        PlayDieSfx();

        // Disable behaviours (stops movement/AI)
        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
            {
                if (b) b.enabled = false;
            }
        }

        OnDied?.Invoke(this, killer);

        if (destroyOnDeath)
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
    }
}
