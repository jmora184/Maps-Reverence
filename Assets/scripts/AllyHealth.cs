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

    [Header("Death Audio")]
    [Tooltip("Optional clip played when this ally dies.")]
    public AudioClip deathSFX;

    [Tooltip("Loudness of the death clip.")]
    [Range(0f, 2f)] public float deathVolume = 1f;

    [Tooltip("0 = 2D, 1 = fully 3D. Start with 0 for testing.")]
    [Range(0f, 1f)] public float deathSpatialBlend = 0f;

    [Tooltip("Minimum distance for 3D death audio.")]
    public float deathMinDistance = 2f;

    [Tooltip("Maximum distance for 3D death audio.")]
    public float deathMaxDistance = 25f;

    [Tooltip("If true, creates a temporary detached audio object so the clip can finish even if this ally is destroyed.")]
    public bool useDetachedDeathAudio = true;

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

        PlayDeathSound();

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


    void PlayDeathSound()
    {
        if (deathSFX == null) return;

        Vector3 pos = transform.position;

        if (useDetachedDeathAudio)
        {
            GameObject temp = new GameObject(name + "_DeathAudio");
            temp.transform.position = pos;

            var src = temp.AddComponent<AudioSource>();
            src.clip = deathSFX;
            src.volume = deathVolume;
            src.spatialBlend = deathSpatialBlend;
            src.minDistance = Mathf.Max(0.01f, deathMinDistance);
            src.maxDistance = Mathf.Max(src.minDistance, deathMaxDistance);
            src.playOnAwake = false;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();

            if (debugLogs) Debug.Log($"[AllyHealth] Playing detached death audio: {deathSFX.name}", temp);

            Destroy(temp, deathSFX.length + 0.25f);
            return;
        }

        var existing = GetComponent<AudioSource>();
        if (existing == null)
            existing = gameObject.AddComponent<AudioSource>();

        existing.spatialBlend = deathSpatialBlend;
        existing.minDistance = Mathf.Max(0.01f, deathMinDistance);
        existing.maxDistance = Mathf.Max(existing.minDistance, deathMaxDistance);
        existing.rolloffMode = AudioRolloffMode.Linear;
        existing.PlayOneShot(deathSFX, deathVolume);

        if (debugLogs) Debug.Log($"[AllyHealth] Playing attached death audio: {deathSFX.name}", this);
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
