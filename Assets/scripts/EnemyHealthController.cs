using System;
using System.Collections;
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

    [Header("Stagger / hit-react threshold (optional)")]
    [Tooltip("If assigned, accumulates damage within a short window and triggers take_damage only when the threshold is reached (e.g., two 5-dmg hits -> 10).")]
    public StaggerOnDamage staggerOnDamage;

    [Header("Legacy hit reaction (older setups)")]
    [Tooltip("Legacy controller type used in older versions of the project.")]
    public EnemyController theEC;

    [Header("Death")]
    [Tooltip("If true, this script will stop accepting damage once dead.")]
    public bool lockAfterDeath = true;

    [Header("Directional Bonus UI (optional)")]
    [Tooltip("Optional world health bar used to briefly show a 2x sprite when directional bonus damage is applied.")]
    [SerializeField] private EnemyWorldHealthBar directionalDamageUI;

    [Header("Directional Damage (optional)")]
    [Tooltip("If true, shots from the side or back can deal bonus damage, while front shots stay normal damage.")]
    public bool enableDirectionalDamageBonus = true;

    [Tooltip("Damage multiplier applied when the shot comes from outside the front cone.")]
    public float sideOrBackDamageMultiplier = 2f;

    [Tooltip("Damage multiplier applied when the shot comes from INSIDE the front cone. Default 1 = normal damage. Set lower for front-armored enemies.")]
    public float frontDamageMultiplier = 1f;

    [Tooltip("Half-angle of the FRONT cone in degrees. Shots inside this cone use the front damage multiplier. Shots outside it get the side/back multiplier.")]
    [Range(0f, 180f)]
    public float frontDamageHalfAngle = 60f;

[Tooltip("Animator Bool parameter to set true when this enemy dies. Helps prevent hit-reactions/locomotion from overriding death.")]
public string animatorIsDeadBool = "isDead";

[Tooltip("Optional: clear common locomotion params when dying to avoid popping back to idle/run.")]
public bool clearLocomotionParamsOnDeath = true;

    [Tooltip("Log when death flags/params are applied (helps debug isDead not being set).")]
    public bool debugDeathLogging = false;


    [Tooltip("If no MnR.DeathController is found, use this fallback cleanup delay.")]
    public float fallbackCleanupDelay = 6f;

    [Tooltip("If true, fallback cleanup disables the GameObject (pool-friendly). If false, it destroys it.")]
    public bool fallbackDisableInsteadOfDestroy = true;


    [Header("Death Audio")]
    [Tooltip("Optional clip played when this enemy dies.")]
    public AudioClip deathSFX;

    [Tooltip("Loudness of the death clip.")]
    [Range(0f, 2f)] public float deathVolume = 1f;

    [Tooltip("0 = 2D, 1 = fully 3D. Start with 0 for testing.")]
    [Range(0f, 1f)] public float deathSpatialBlend = 0f;

    [Tooltip("Minimum distance for 3D death audio.")]
    public float deathMinDistance = 2f;

    [Tooltip("Maximum distance for 3D death audio.")]
    public float deathMaxDistance = 25f;

    [Tooltip("If true, creates a temporary detached audio object so the clip can finish even if this enemy is destroyed.")]
    public bool useDetachedDeathAudio = true;

    [Header("Debug Audio")]
    public bool debugDeathAudio = false;

    // UI can subscribe to this
    public event Action<float> OnHealth01Changed;

    // Global death hook for reinforcement / encounter systems.
    public static event Action<EnemyHealthController> OnAnyEnemyDied;

    public bool IsDead => _isDead;

    private bool _isDead;

    private void Awake()
    {
        if (maxHealth <= 0) maxHealth = 5;
        if (currentHealth <= 0) currentHealth = maxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        // Auto-wire controllers if not set in inspector (safe + helps prevent "standing there" bugs)
        if (enemy2Controller == null)
            enemy2Controller = GetComponent<Enemy2Controller>();

        if (theEC == null)
            theEC = GetComponent<EnemyController>();

        
        if (staggerOnDamage == null)
            staggerOnDamage = GetComponent<StaggerOnDamage>();

        if (directionalDamageUI == null)
            directionalDamageUI = GetComponentInChildren<EnemyWorldHealthBar>(true);

        RaiseHealthChanged();
    }

    /// <summary>
    /// Apply damage to the enemy.
    /// NOTE: This method does NOT take an attacker. Enemy2Controller.GetShot() should still aggro/return-fire
    /// using its internal fallback (typically Player) when attacker is unknown.
    /// </summary>
    

// --- Common damage entry points (so different bullet / melee scripts can talk to this health) ---

/// <summary>
/// Common Unity pattern: some projectiles call TakeDamage(float).
/// This forwards into DamageEnemy(int).
/// </summary>
public void TakeDamage(float damage)
{
    // Round up so small damages still matter (e.g., 0.5 -> 1).
    int dmgInt = Mathf.CeilToInt(damage);
    DamageEnemy(dmgInt);
}

/// <summary>
/// Some scripts call TakeDamage(int).
/// </summary>
public void TakeDamage(int damage)
{
    DamageEnemy(damage);
}

/// <summary>
/// Alias for older scripts that call ApplyDamage.
/// </summary>
public void ApplyDamage(int damage)
{
    DamageEnemy(damage);
}

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

private int ApplyDirectionalDamageMultiplier(int damageAmount, Vector3 incomingDirectionWorld, out bool appliedDirectionalBonus)
{
    appliedDirectionalBonus = false;

    if (!enableDirectionalDamageBonus)
        return damageAmount;

    if (damageAmount <= 0)
        return damageAmount;

    Vector3 incoming = incomingDirectionWorld;
    incoming.y = 0f;
    if (incoming.sqrMagnitude <= 0.0001f)
        return damageAmount;

    Vector3 forward = transform.forward;
    forward.y = 0f;
    if (forward.sqrMagnitude <= 0.0001f)
        return damageAmount;

    incoming.Normalize();
    forward.Normalize();

    float frontDotThreshold = Mathf.Cos(Mathf.Clamp(frontDamageHalfAngle, 0f, 180f) * Mathf.Deg2Rad);
    float dot = Vector3.Dot(forward, incoming);
    bool isFrontShot = dot >= frontDotThreshold;

    if (isFrontShot)
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

private void ShowDirectionalDamageBonusUI()
{
    if (directionalDamageUI == null)
        directionalDamageUI = GetComponentInChildren<EnemyWorldHealthBar>(true);

    if (directionalDamageUI != null)
        directionalDamageUI.ShowDirectionalBonus2x();
}

private void ApplyDamageInternal(int damageAmount)
    {
        if (lockAfterDeath && _isDead) return;

        currentHealth -= damageAmount;

        // Accumulate damage for stagger/hit-react (e.g., two quick 5-dmg hits => 10 triggers take_damage once)
        if (staggerOnDamage == null) staggerOnDamage = GetComponent<StaggerOnDamage>();
        if (staggerOnDamage != null) staggerOnDamage.NotifyDamage(damageAmount);

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
        ApplyAnimatorDeathState();

        currentHealth = 0;
        RaiseHealthChanged();

        PlayDeathSound();

        OnAnyEnemyDied?.Invoke(this);

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

        // 3) Fallback: set Animator bool "isDead" and trigger Animator parameter "Die" if it exists
var anim = GetComponentInChildren<Animator>(true);
if (anim != null)
{
    if (!string.IsNullOrEmpty(animatorIsDeadBool) &&
        HasAnimatorParameter(anim, animatorIsDeadBool, AnimatorControllerParameterType.Bool))
    {
        anim.SetBool(animatorIsDeadBool, true);
    }

    if (clearLocomotionParamsOnDeath)
    {
        // These are common in your controllers; safe to call even if they don't exist.
        if (HasAnimatorParameter(anim, "Speed", AnimatorControllerParameterType.Float)) anim.SetFloat("Speed", 0f);
        if (HasAnimatorParameter(anim, "isRunning", AnimatorControllerParameterType.Bool)) anim.SetBool("isRunning", false);
        if (HasAnimatorParameter(anim, "isWalking", AnimatorControllerParameterType.Bool)) anim.SetBool("isWalking", false);
        if (HasAnimatorParameter(anim, "isMoving", AnimatorControllerParameterType.Bool)) anim.SetBool("isMoving", false);
        if (HasAnimatorParameter(anim, "fireShot", AnimatorControllerParameterType.Bool)) anim.SetBool("fireShot", false);
    }

    if (HasAnimatorParameter(anim, "Die", AnimatorControllerParameterType.Trigger))
    {
        anim.ResetTrigger("Die");
        anim.SetTrigger("Die");
    }
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

    private void PlayDeathSound()
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

            if (debugDeathAudio) Debug.Log($"[EnemyHealthController] Playing detached death audio: {deathSFX.name}", temp);

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

        if (debugDeathAudio) Debug.Log($"[EnemyHealthController] Playing attached death audio: {deathSFX.name}", this);
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

// --- Animator Death Helpers ---

private void ApplyAnimatorDeathState()
{
    // Apply to ALL animators in this hierarchy (covers cases where the animator is not on the expected child).
    Animator[] anims = GetComponentsInChildren<Animator>(true);
    if (anims == null || anims.Length == 0)
    {
        // As an extra fallback, try parent chain (in case the health is on a child under the animated root)
        var parentAnim = GetComponentInParent<Animator>();
        if (parentAnim != null) anims = new[] { parentAnim };
    }

    if (anims == null || anims.Length == 0)
    {
        if (debugDeathLogging) Debug.LogWarning($"{name}: No Animator found to set '{animatorIsDeadBool}'.", this);
        return;
    }

    foreach (var anim in anims)
    {
        if (anim == null) continue;

        if (!string.IsNullOrEmpty(animatorIsDeadBool) &&
            HasAnimatorParameter(anim, animatorIsDeadBool, AnimatorControllerParameterType.Bool))
        {
            anim.SetBool(animatorIsDeadBool, true);
            if (debugDeathLogging) Debug.Log($"{name}: Set Animator bool '{animatorIsDeadBool}' = true on {anim.gameObject.name}", this);
        }
        else
        {
            if (debugDeathLogging) Debug.LogWarning($"{name}: Animator on {anim.gameObject.name} has no bool '{animatorIsDeadBool}'.", anim);
        }

        if (clearLocomotionParamsOnDeath)
        {
            if (HasAnimatorParameter(anim, "Speed", AnimatorControllerParameterType.Float)) anim.SetFloat("Speed", 0f);
            if (HasAnimatorParameter(anim, "isRunning", AnimatorControllerParameterType.Bool)) anim.SetBool("isRunning", false);
            if (HasAnimatorParameter(anim, "isWalking", AnimatorControllerParameterType.Bool)) anim.SetBool("isWalking", false);
            if (HasAnimatorParameter(anim, "isMoving", AnimatorControllerParameterType.Bool)) anim.SetBool("isMoving", false);
            if (HasAnimatorParameter(anim, "fireShot", AnimatorControllerParameterType.Bool)) anim.SetBool("fireShot", false);
        }
    }
}

}
