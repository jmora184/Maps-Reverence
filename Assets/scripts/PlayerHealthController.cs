using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player health + optional UI wiring (same idea as AllyHealthIcon):
/// - Tracks max/current health
/// - Optional invincibility window after taking damage
/// - Raises OnHealth01Changed (0..1) whenever health changes
/// - Can directly drive a UI bar (RectTransform scale X) or a UI Image (fillAmount)
///
/// Attach this to your PLAYER root (the object that should "be" the player).
/// Then either:
///   A) Assign Bar Fill (RectTransform) in the inspector (pivot X must be 0)
///   B) Or assign Fill Image (Image) and set Image Type = Filled (Horizontal)
/// </summary>
public class PlayerHealthController : MonoBehaviour
{
    public static PlayerHealthController Instance { get; private set; }

    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;

    [Header("Invincibility")]
    [Tooltip("Seconds of invincibility after taking damage. 0 = no invincibility.")]
    [SerializeField] private float invincibleLength = 0.25f;

    [Header("UI (optional)")]
    [Tooltip("Drag the green bar 'fill' RectTransform here (pivot X must be 0). This will scale X from 0..1.")]
    [SerializeField] private RectTransform barFill;

    [Tooltip("Alternative UI: drag an Image here and set Image Type = Filled (Horizontal). This will set fillAmount 0..1.")]
    [SerializeField] private Image fillImage;

    [Header("Debug")]
    [SerializeField] private bool logDamage = false;

    public event Action<float> OnHealth01Changed;
    public event Action<int, int> OnHealthChanged; // current, max
    public event Action OnDied;

    private float invincCounter;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        // Singleton (simple)
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        // Ensure valid values
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        PushHealthToUIAndEvents();
    }

    private void Update()
    {
        if (invincCounter > 0f)
            invincCounter -= Time.deltaTime;
    }

    /// <summary>Damage the player. Returns true if damage was applied.</summary>
    public bool DamagePlayer(int damageAmount)
    {
        if (damageAmount <= 0) return false;
        if (IsDead) return false;
        if (invincCounter > 0f) return false;

        currentHealth -= damageAmount;
        if (logDamage) Debug.Log($"[PlayerHealth] Damage {damageAmount} -> {currentHealth}/{maxHealth}", this);

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            PushHealthToUIAndEvents();
            Die();
        }
        else
        {
            PushHealthToUIAndEvents();
        }

        invincCounter = Mathf.Max(0f, invincibleLength);
        return true;
    }

    public void HealPlayer(int healAmount)
    {
        if (healAmount <= 0) return;
        if (IsDead) return;

        currentHealth += healAmount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;

        PushHealthToUIAndEvents();
    }

    public void SetMaxHealth(int newMax, bool alsoHealToFull = true)
    {
        maxHealth = Mathf.Max(1, newMax);

        if (alsoHealToFull)
            currentHealth = maxHealth;
        else
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        PushHealthToUIAndEvents();
    }

    public float Health01()
    {
        if (maxHealth <= 0) return 0f;
        return Mathf.Clamp01((float)currentHealth / maxHealth);
    }

    public bool IsInvincible()
    {
        return invincCounter > 0f;
    }

    private void Die()
    {
        if (logDamage) Debug.Log("[PlayerHealth] Player died.", this);
        OnDied?.Invoke();

        // NOTE: Don't auto-disable the player here unless you want that behavior.
        // Your existing death/respawn system can subscribe to OnDied.
    }

    private void PushHealthToUIAndEvents()
    {
        float t = Health01();

        // UI option A: scale X
        if (barFill != null)
        {
            var s = barFill.localScale;
            s.x = t;
            barFill.localScale = s;
        }

        // UI option B: fill amount
        if (fillImage != null)
        {
            // Works best when Image Type = Filled (Horizontal)
            fillImage.fillAmount = t;
        }

        OnHealth01Changed?.Invoke(t);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

#if UNITY_EDITOR
    // Handy button in inspector to test
    [ContextMenu("TEST - Damage 10")]
    private void _TestDamage10() => DamagePlayer(10);

    [ContextMenu("TEST - Heal 10")]
    private void _TestHeal10() => HealPlayer(10);
#endif
}
