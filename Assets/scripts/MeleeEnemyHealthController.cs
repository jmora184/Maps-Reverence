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

    [Header("Death Audio (optional)")]
    [Tooltip("Optional death SFX played once when this enemy dies.")]
    public AudioClip deathSFX;

    [Tooltip("Optional audio source for the death SFX. If empty, we auto-find one. If none exists, we use PlayClipAtPoint.")]
    public AudioSource deathAudioSource;

    [Range(0f, 1f)] public float deathSFXVolume = 1f;

    [Header("Directional Bonus UI (optional)")]
    [Tooltip("Optional world health bar used to briefly show a 2x sprite when directional bonus damage is applied.")]
    [SerializeField] private MeleeEnemyWorldHealthBar directionalDamageUI;

    [Header("Directional Bonus Audio (optional)")]
    [Tooltip("Optional SFX played when directional bonus damage (2x flank/side hit) is applied.")]
    [SerializeField] private AudioClip directionalBonus2xSFX;

    [Tooltip("Optional AudioSource for the 2x bonus SFX. Leave empty to use PlayClipAtPoint at the enemy position.")]
    [SerializeField] private AudioSource directionalBonus2xAudioSource;

    [Range(0f, 2f)]
    [Tooltip("Volume used for the 2x bonus SFX.")]
    [SerializeField] private float directionalBonus2xVolume = 1f;

    [Header("Directional Damage (optional)")]
    [Tooltip("If true, hits from outside the front cone deal bonus damage and show the 2x icon.")]
    public bool enableDirectionalDamageBonus = true;

    [Tooltip("Damage multiplier applied when the hit comes from outside the front cone.")]
    public float sideOrBackDamageMultiplier = 2f;

    [Tooltip("Damage multiplier applied when the hit comes from INSIDE the front cone. Default 1 = normal damage. Set lower for front-armored melee enemies.")]
    public float frontDamageMultiplier = 1f;

    [Tooltip("Half-angle of the FRONT cone in degrees. Hits inside this cone use the front damage multiplier. Hits outside it get the side/back multiplier.")]
    [Range(0f, 180f)]
    public float frontDamageHalfAngle = 60f;

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
    private bool _deathSoundPlayed;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        ResolveDeathAudioSourceIfNeeded();

        if (maxHealth <= 0) maxHealth = 1;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        _dead = currentHealth <= 0;

        if (!meleeController)
            meleeController = AutoFindController();

        if (directionalDamageUI == null)
            directionalDamageUI = GetComponentInChildren<MeleeEnemyWorldHealthBar>(true);

        // If we start dead (e.g., prefab misconfigured), clean up consistently.
        if (_dead)
            HandleDeathSideEffects();
    }

    // --- IDamageable ---
    public void TakeDamage(float amount) => ApplyDamageInternal(Mathf.Max(1, Mathf.RoundToInt(amount)));

    // Convenience for older callers
    public void TakeDamage(int amount) => ApplyDamageInternal(amount);
    public void ApplyDamage(float amount) => ApplyDamageInternal(Mathf.Max(1, Mathf.RoundToInt(amount)));
    public void ApplyDamage(int amount) => ApplyDamageInternal(amount);
    public void ReceiveDamage(float amount) => ApplyDamageInternal(Mathf.Max(1, Mathf.RoundToInt(amount)));
    public void ReceiveDamage(int amount) => ApplyDamageInternal(amount);

    // Matches EnemyHealthController-style entry points so bullet scripts can pass hit direction.
    public void DamageEnemy(int damageAmount)
    {
        ApplyDamageInternal(damageAmount);
    }

    public void DamageEnemy(int damageAmount, Vector3 incomingDirectionWorld)
    {
        bool appliedDirectionalBonus;
        int finalDamage = ApplyDirectionalDamageMultiplier(damageAmount, incomingDirectionWorld, out appliedDirectionalBonus);

        if (appliedDirectionalBonus)
            ShowDirectionalDamageBonusUI();

        ApplyDamageInternal(finalDamage);
    }

    public bool IsDead => _dead;

    public bool ShouldUseFrontResistImpact(Vector3 incomingDirectionWorld, float maxFrontDamageMultiplierForImpact = 0.1f)
    {
        if (!enableDirectionalDamageBonus)
            return false;

        if (!IsFrontHit(incomingDirectionWorld))
            return false;

        float clampedThreshold = Mathf.Clamp01(maxFrontDamageMultiplierForImpact);
        float clampedFrontMultiplier = Mathf.Max(0f, frontDamageMultiplier);

        return clampedFrontMultiplier <= clampedThreshold;
    }

    public void ShowDirectionalDamageBonusUI()
    {
        if (directionalDamageUI == null)
            directionalDamageUI = GetComponentInChildren<MeleeEnemyWorldHealthBar>(true);

        if (directionalDamageUI != null)
            directionalDamageUI.ShowDirectionalBonus2x();

        PlayDirectionalBonus2xSound();
    }

    private void PlayDirectionalBonus2xSound()
    {
        if (directionalBonus2xSFX == null)
            return;

        float volume = Mathf.Clamp(directionalBonus2xVolume, 0f, 2f);

        if (directionalBonus2xAudioSource != null && directionalBonus2xAudioSource.enabled && directionalBonus2xAudioSource.gameObject.activeInHierarchy)
        {
            directionalBonus2xAudioSource.PlayOneShot(directionalBonus2xSFX, volume);
            return;
        }

        AudioSource.PlayClipAtPoint(directionalBonus2xSFX, transform.position, volume);
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (_dead) return;

        currentHealth = Mathf.Clamp(currentHealth + amount, 0, maxHealth);
    }

    private int ApplyDirectionalDamageMultiplier(int damageAmount, Vector3 incomingDirectionWorld, out bool appliedDirectionalBonus)
    {
        appliedDirectionalBonus = false;

        if (!enableDirectionalDamageBonus)
            return damageAmount;

        if (damageAmount <= 0)
            return damageAmount;

        bool isFrontHit = IsFrontHit(incomingDirectionWorld);

        if (isFrontHit)
        {
            float frontMultiplier = Mathf.Max(0f, frontDamageMultiplier);
            if (Mathf.Approximately(frontMultiplier, 1f))
                return damageAmount;

            float frontMultiplied = damageAmount * frontMultiplier;
            return Mathf.Max(0, Mathf.RoundToInt(frontMultiplied));
        }

        float multiplier = Mathf.Max(1f, sideOrBackDamageMultiplier);
        if (multiplier <= 1f)
            return damageAmount;

        appliedDirectionalBonus = true;
        float multiplied = damageAmount * multiplier;
        return Mathf.Max(1, Mathf.RoundToInt(multiplied));
    }

    private bool IsFrontHit(Vector3 incomingDirectionWorld)
    {
        Vector3 incoming = incomingDirectionWorld;
        incoming.y = 0f;
        if (incoming.sqrMagnitude <= 0.0001f)
            return false;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return false;

        incoming.Normalize();
        forward.Normalize();

        float frontDotThreshold = Mathf.Cos(Mathf.Clamp(frontDamageHalfAngle, 0f, 180f) * Mathf.Deg2Rad);
        float dot = Vector3.Dot(forward, incoming);
        return dot >= frontDotThreshold;
    }

    private void ApplyDamageInternal(int damageAmount)
    {
        if (damageAmount <= 0) return;
        if (lockAfterDeath && _dead) return;

        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);

        if (debugLogs)
            Debug.Log($"[MeleeEnemyHealthController] {name} took {damageAmount}. HP {currentHealth}/{maxHealth}");

        // Hit react
        if (animator && !_dead && !string.IsNullOrEmpty(hitReactTrigger))
            TrySetTriggerSafe(animator, hitReactTrigger);

        NotifyControllerDamaged(damageAmount);

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

        PlayDeathSound();

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

    private void PlayDeathSound()
    {
        if (_deathSoundPlayed) return;
        _deathSoundPlayed = true;

        if (deathSFX == null) return;

        AudioSource src = ResolveDeathAudioSourceIfNeeded();
        if (src != null && src.enabled && src.gameObject.activeInHierarchy)
        {
            src.PlayOneShot(deathSFX, Mathf.Clamp01(deathSFXVolume));
            return;
        }

        AudioListener listener = FindObjectOfType<AudioListener>();
        float volume = Mathf.Clamp01(deathSFXVolume);
        if (listener != null)
        {
            Vector3 pos = transform.position;
            pos.z = listener.transform.position.z;
            AudioSource.PlayClipAtPoint(deathSFX, pos, volume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(deathSFX, transform.position, volume);
        }
    }

    private AudioSource ResolveDeathAudioSourceIfNeeded()
    {
        if (deathAudioSource != null) return deathAudioSource;

        deathAudioSource = GetComponent<AudioSource>();
        if (deathAudioSource != null) return deathAudioSource;

        deathAudioSource = GetComponentInChildren<AudioSource>(true);
        if (deathAudioSource != null) return deathAudioSource;

        deathAudioSource = GetComponentInParent<AudioSource>();
        return deathAudioSource;
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
