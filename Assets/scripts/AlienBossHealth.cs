using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

    [Header("Death Audio")]
    public AudioClip dieSfx;
    public AudioSource dieAudioSource;
    [Range(0f, 1f)] public float dieSfxVolume = 1f;
    public bool useDetachedDeathAudio = true;
    [Range(0f, 1f)] public float deathSpatialBlend = 0f;
    public float deathMinDistance = 10f;
    public float deathMaxDistance = 40f;

    [Header("Follower Reset / Restore On Death")]
    [Tooltip("If true, any active player bodyguards are temporarily removed from the follower system when this boss dies.")]
    public bool resetPlayerFollowersOnDeath = true;

    [Tooltip("If true, cancel Follow Me pick mode when the boss dies.")]
    public bool disarmFollowerPickModeOnDeath = true;

    [Tooltip("If true, the same bodyguards that were following right before boss death are automatically added back after a short delay.")]
    public bool autoRestorePlayerFollowersOnDeath = true;

    [Tooltip("Delay before auto-restoring the saved bodyguards after boss death.")]
    public float followerRestoreDelay = 0.35f;

    [Tooltip("Passes through to PlayerSquadFollowSystem.StopFollow(stopAgents). Usually leave false so allies are not hard-stopped.")]
    public bool stopFollowerAgentsOnDeath = false;

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    public event Action<AlienBossHealth, int, Transform> OnDamaged;
    public event Action<AlienBossHealth, Transform> OnDied;

    private bool _deathHandled = false;

    private void Awake()
    {
        if (currentHealth <= 0) currentHealth = maxHealth;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!dieAudioSource) dieAudioSource = GetComponent<AudioSource>();
        if (!dieAudioSource) dieAudioSource = GetComponentInChildren<AudioSource>();
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

        List<Transform> savedFollowers = CaptureCurrentPlayerFollowers();

        PlayDieSfx();

        if (animator && !string.IsNullOrWhiteSpace(dieTrigger))
            animator.SetTrigger(dieTrigger);

        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
                if (b) b.enabled = false;
        }

        HandleFollowerResetAndRestore(savedFollowers);

        OnDied?.Invoke(this, killer);

        if (destroyOnDeath)
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
    }

    private List<Transform> CaptureCurrentPlayerFollowers()
    {
        var system = PlayerSquadFollowSystem.Instance;
        if (!resetPlayerFollowersOnDeath || system == null || system.FollowerCount <= 0)
            return null;

        GameObject[] allies = GameObject.FindGameObjectsWithTag("Ally");
        if (allies == null || allies.Length == 0)
            return null;

        List<Transform> snapshot = new List<Transform>();

        for (int i = 0; i < allies.Length; i++)
        {
            GameObject go = allies[i];
            if (go == null) continue;

            Transform ally = go.transform;
            if (system.IsFollowing(ally))
                snapshot.Add(ally);
        }

        if (snapshot.Count == 0)
            return null;

        snapshot.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return snapshot;
    }

    private void HandleFollowerResetAndRestore(List<Transform> savedFollowers)
    {
        var system = PlayerSquadFollowSystem.Instance;
        if (system == null) return;

        if (resetPlayerFollowersOnDeath)
            system.StopFollow(stopFollowerAgentsOnDeath);

        if (disarmFollowerPickModeOnDeath)
            system.DisarmPickFollowers();

        if (autoRestorePlayerFollowersOnDeath && savedFollowers != null && savedFollowers.Count > 0)
            system.StartCoroutine(RestoreFollowersAfterDelay(system, savedFollowers));
    }

    private IEnumerator RestoreFollowersAfterDelay(PlayerSquadFollowSystem system, List<Transform> savedFollowers)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, followerRestoreDelay));

        if (system == null || savedFollowers == null || savedFollowers.Count == 0)
            yield break;

        for (int i = 0; i < savedFollowers.Count; i++)
        {
            Transform ally = savedFollowers[i];
            if (!IsFollowerEligibleForRestore(ally))
                continue;

            system.TryAddFollowerDirect(ally, false);
        }
    }

    private bool IsFollowerEligibleForRestore(Transform ally)
    {
        if (ally == null)
            return false;

        if (!ally.gameObject.activeInHierarchy)
            return false;

        var deathController = ally.GetComponentInParent<MnR.DeathController>();
        if (deathController != null && deathController.IsDead)
            return false;

        var allyHealth = ally.GetComponentInParent<AllyHealth>();
        if (allyHealth != null)
        {
            if (ReadCommonDeadFlag(allyHealth))
                return false;

            if (ReadCommonHealthValue(allyHealth, out float healthValue) && healthValue <= 0f)
                return false;
        }

        return true;
    }

    private bool ReadCommonDeadFlag(Component component)
    {
        if (component == null)
            return false;

        var type = component.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] names = { "IsDead", "isDead", "Dead", "dead" };

        for (int i = 0; i < names.Length; i++)
        {
            var prop = type.GetProperty(names[i], flags);
            if (prop != null && prop.PropertyType == typeof(bool))
                return (bool)prop.GetValue(component, null);

            var field = type.GetField(names[i], flags);
            if (field != null && field.FieldType == typeof(bool))
                return (bool)field.GetValue(component);
        }

        return false;
    }

    private bool ReadCommonHealthValue(Component component, out float value)
    {
        value = 0f;

        if (component == null)
            return false;

        var type = component.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] names = { "CurrentHealth", "currentHealth", "Health", "health" };

        for (int i = 0; i < names.Length; i++)
        {
            var prop = type.GetProperty(names[i], flags);
            if (prop != null)
            {
                if (TryConvertToFloat(prop.GetValue(component, null), out value))
                    return true;
            }

            var field = type.GetField(names[i], flags);
            if (field != null)
            {
                if (TryConvertToFloat(field.GetValue(component), out value))
                    return true;
            }
        }

        return false;
    }

    private bool TryConvertToFloat(object raw, out float value)
    {
        value = 0f;
        if (raw == null)
            return false;

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case float f:
                value = f;
                return true;
            case double d:
                value = (float)d;
                return true;
            case long l:
                value = l;
                return true;
            case short s:
                value = s;
                return true;
            case byte b:
                value = b;
                return true;
            default:
                return false;
        }
    }

    public void PlayDieSfx()
    {
        if (!dieSfx) return;

        if (useDetachedDeathAudio)
        {
            GameObject temp = new GameObject(name + "_DeathAudio");
            temp.transform.position = transform.position;

            AudioSource src = temp.AddComponent<AudioSource>();
            src.clip = dieSfx;
            src.volume = Mathf.Clamp01(dieSfxVolume);
            src.spatialBlend = Mathf.Clamp01(deathSpatialBlend);
            src.minDistance = Mathf.Max(0.01f, deathMinDistance);
            src.maxDistance = Mathf.Max(src.minDistance, deathMaxDistance);
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
            src.Play();

            Destroy(temp, Mathf.Max(0.1f, dieSfx.length + 0.25f));
            return;
        }

        if (dieAudioSource)
        {
            dieAudioSource.PlayOneShot(dieSfx, Mathf.Clamp01(dieSfxVolume));
            return;
        }

        AudioSource.PlayClipAtPoint(dieSfx, transform.position, Mathf.Clamp01(dieSfxVolume));
    }
}
