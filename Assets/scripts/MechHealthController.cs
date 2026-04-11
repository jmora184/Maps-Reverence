using System;
using UnityEngine;

/// <summary>
/// Separate health component for the mech.
/// Keeps your original EnemyHealthController untouched.
/// Works like the enemy health controller, but notifies mechController instead.
/// </summary>
public class MechHealthController : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 5;
    public int currentHealth = 5;

    [Header("Optional hit reaction (preferred)")]
    [Tooltip("If assigned, will be notified when this mech takes damage so it can aggro/return-fire.")]
    public mechController mechAI;

    [Header("Stagger / hit-react threshold (optional)")]
    [Tooltip("If assigned, accumulates damage within a short window and triggers take_damage only when the threshold is reached.")]
    public StaggerOnDamage staggerOnDamage;

    [Header("Directional Damage (optional)")]
    [Tooltip("If true, hits from outside the front cone deal bonus damage and show the 2x icon.")]
    public bool enableDirectionalDamageBonus = true;

    [Tooltip("Damage multiplier applied when the hit comes from outside the front cone.")]
    public float sideOrBackDamageMultiplier = 2f;

    [Tooltip("Half-angle of the FRONT cone in degrees. Hits inside this cone deal normal damage. Hits outside it get the side/back multiplier.")]
    [Range(0f, 180f)]
    public float frontDamageHalfAngle = 60f;

    [Header("Directional Bonus UI (optional)")]
    [Tooltip("Optional mech health bar used to briefly show a 2x icon when directional bonus damage is applied.")]
    [SerializeField] private MechHealthBarSimple directionalDamageUI;

    [Header("Directional Bonus Audio")]
    [Tooltip("Optional clip played when directional 2x bonus damage is applied to this mech.")]
    public AudioClip directionalBonus2xSFX;

    [Tooltip("Loudness of the directional bonus clip.")]
    [Range(0f, 2f)] public float directionalBonus2xVolume = 1f;

    [Tooltip("0 = 2D, 1 = fully 3D. Start with 0 for testing.")]
    [Range(0f, 1f)] public float directionalBonus2xSpatialBlend = 0f;

    [Tooltip("Minimum distance for 3D directional bonus audio.")]
    public float directionalBonus2xMinDistance = 2f;

    [Tooltip("Maximum distance for 3D directional bonus audio.")]
    public float directionalBonus2xMaxDistance = 25f;

    [Tooltip("If true, creates a temporary detached audio object so the clip can finish even if this mech dies from the 2x hit.")]
    public bool useDetachedDirectionalBonusAudio = true;

    [Header("Debug Directional Bonus Audio")]
    public bool debugDirectionalBonusAudio = false;

    [Header("Death")]
    [Tooltip("If true, this script will stop accepting damage once dead.")]
    public bool lockAfterDeath = true;

    [Tooltip("Animator Bool parameter to set true when this mech dies.")]
    public string animatorIsDeadBool = "isDead";

    [Tooltip("Optional: clear common locomotion params when dying to avoid popping back to idle/run.")]
    public bool clearLocomotionParamsOnDeath = true;

    [Tooltip("Log when death flags/params are applied.")]
    public bool debugDeathLogging = false;

    [Tooltip("If no MnR.DeathController is found, use this fallback cleanup delay.")]
    public float fallbackCleanupDelay = 6f;

    [Tooltip("If true, fallback cleanup disables the GameObject (pool-friendly). If false, it destroys it.")]
    public bool fallbackDisableInsteadOfDestroy = true;

    [Header("Death Audio")]
    [Tooltip("Optional clip played when this mech dies.")]
    public AudioClip deathSFX;

    [Tooltip("Loudness of the death clip.")]
    [Range(0f, 2f)] public float deathVolume = 1f;

    [Tooltip("0 = 2D, 1 = fully 3D. Start with 0 for testing.")]
    [Range(0f, 1f)] public float deathSpatialBlend = 0f;

    [Tooltip("Minimum distance for 3D death audio.")]
    public float deathMinDistance = 2f;

    [Tooltip("Maximum distance for 3D death audio.")]
    public float deathMaxDistance = 25f;

    [Tooltip("If true, creates a temporary detached audio object so the clip can finish even if this mech is destroyed.")]
    public bool useDetachedDeathAudio = true;

    [Header("Debug Audio")]
    public bool debugDeathAudio = false;

    public event Action<float> OnHealth01Changed;
    public static event Action<MechHealthController> OnAnyMechDied;

    public bool IsDead => _isDead;
    private bool _isDead;

    private void Awake()
    {
        if (maxHealth <= 0) maxHealth = 5;
        if (currentHealth <= 0) currentHealth = maxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        if (mechAI == null)
            mechAI = GetComponent<mechController>();

        if (staggerOnDamage == null)
            staggerOnDamage = GetComponent<StaggerOnDamage>();

        if (directionalDamageUI == null)
            directionalDamageUI = GetComponentInChildren<MechHealthBarSimple>(true);

        RaiseHealthChanged();
    }

    public void TakeDamage(float damage)
    {
        int dmgInt = Mathf.CeilToInt(damage);
        DamageEnemy(dmgInt);
    }

    public void TakeDamage(int damage)
    {
        DamageEnemy(damage);
    }

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
        {
            ShowDirectionalDamageBonusUI();
            PlayDirectionalBonusSound();
        }

        ApplyDamageInternal(finalDamage);
    }

    public void ShowDirectionalDamageBonusUI()
    {
        if (directionalDamageUI == null)
            directionalDamageUI = GetComponentInChildren<MechHealthBarSimple>(true);

        if (directionalDamageUI != null)
            directionalDamageUI.ShowDirectionalBonus2x();
    }

    private void PlayDirectionalBonusSound()
    {
        if (directionalBonus2xSFX == null) return;

        Vector3 pos = transform.position;

        if (useDetachedDirectionalBonusAudio)
        {
            GameObject temp = new GameObject(name + "_DirectionalBonusAudio");
            temp.transform.position = pos;

            var src = temp.AddComponent<AudioSource>();
            src.clip = directionalBonus2xSFX;
            src.volume = directionalBonus2xVolume;
            src.spatialBlend = directionalBonus2xSpatialBlend;
            src.minDistance = Mathf.Max(0.01f, directionalBonus2xMinDistance);
            src.maxDistance = Mathf.Max(src.minDistance, directionalBonus2xMaxDistance);
            src.playOnAwake = false;
            src.loop = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();

            if (debugDirectionalBonusAudio) Debug.Log($"[MechHealthController] Playing detached directional bonus audio: {directionalBonus2xSFX.name}", temp);

            Destroy(temp, directionalBonus2xSFX.length + 0.25f);
            return;
        }

        var existing = GetComponent<AudioSource>();
        if (existing == null)
            existing = gameObject.AddComponent<AudioSource>();

        existing.spatialBlend = directionalBonus2xSpatialBlend;
        existing.minDistance = Mathf.Max(0.01f, directionalBonus2xMinDistance);
        existing.maxDistance = Mathf.Max(existing.minDistance, directionalBonus2xMaxDistance);
        existing.rolloffMode = AudioRolloffMode.Linear;
        existing.PlayOneShot(directionalBonus2xSFX, directionalBonus2xVolume);

        if (debugDirectionalBonusAudio) Debug.Log($"[MechHealthController] Playing attached directional bonus audio: {directionalBonus2xSFX.name}", this);
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
        bool isFrontHit = dot >= frontDotThreshold;

        if (isFrontHit)
            return damageAmount;

        float multiplier = Mathf.Max(1f, sideOrBackDamageMultiplier);
        if (multiplier <= 1f)
            return damageAmount;

        appliedDirectionalBonus = true;
        float multiplied = damageAmount * multiplier;
        return Mathf.Max(1, Mathf.RoundToInt(multiplied));
    }

    private void ApplyDamageInternal(int damageAmount)
    {
        if (lockAfterDeath && _isDead) return;

        currentHealth -= damageAmount;

        if (staggerOnDamage == null)
            staggerOnDamage = GetComponent<StaggerOnDamage>();
        if (staggerOnDamage != null)
            staggerOnDamage.NotifyDamage(damageAmount);

        NotifyHitReaction();

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();

        if (currentHealth <= 0)
            Die();
    }

    private void NotifyHitReaction()
    {
        if (mechAI == null)
            mechAI = GetComponent<mechController>();

        if (mechAI != null)
            mechAI.GetShot();
    }

    public void Die()
    {
        if (_isDead) return;
        _isDead = true;
        ApplyAnimatorDeathState();

        currentHealth = 0;
        RaiseHealthChanged();

        PlayDeathSound();
        OnAnyMechDied?.Invoke(this);

        var deathController = GetComponent<MnR.DeathController>();
        if (deathController != null)
        {
            deathController.Die();
            return;
        }

        var mech = GetComponent<mechController>();
        if (mech != null)
            mech.Die();

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

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && !cols[i].isTrigger)
                cols[i].enabled = false;
        }

        if (lockAfterDeath) enabled = false;

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

            if (debugDeathAudio) Debug.Log($"[MechHealthController] Playing detached death audio: {deathSFX.name}", temp);

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

        if (debugDeathAudio) Debug.Log($"[MechHealthController] Playing attached death audio: {deathSFX.name}", this);
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

    private void ApplyAnimatorDeathState()
    {
        Animator[] anims = GetComponentsInChildren<Animator>(true);
        if (anims == null || anims.Length == 0)
        {
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
