using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple player health component + UI bar driver.
/// New name to avoid conflicts with your older PlayerHealthController.
///
/// Attach to the Player.
/// Drag your GREEN bar RectTransform into "Bar Fill" (pivot X should be 0).
/// </summary>
public class PlayerVitals : MonoBehaviour, IDamageable
{
    public static PlayerVitals Instance { get; private set; }

    [Header("Health")]
    [SerializeField] private int maxHealth = 500;
    [SerializeField] private int currentHealth = 500;

    [Header("Invincibility")]
    [Tooltip("Seconds of invincibility after taking damage. 0 = none.")]
    [SerializeField] private float invincibleSeconds = 1f;

    [Header("Auto Recharge")]
    [Tooltip("If enabled, the player begins recharging health after going this many seconds without taking damage.")]
    [SerializeField] private bool rechargeEnabled = true;

    [Tooltip("Seconds without taking damage before health starts recharging.")]
    [SerializeField] private float rechargeDelay = 5f;

    [Tooltip("How fast health recharges per second once the delay has passed.")]
    [SerializeField] private float rechargeRatePerSecond = 75f;

    [Header("UI")]
    [Tooltip("GREEN bar 'fill' RectTransform (the part that shrinks). Pivot X must be 0.")]
    [SerializeField] private RectTransform barFill;

    [Tooltip("Optional alternative: Image set to Filled (Horizontal).")]
    [SerializeField] private Image fillImage;

    [Header("Debug")]
    [SerializeField] private bool logChanges = false;

    public event Action<float> OnHealth01Changed; // 0..1
    public event Action<int, int> OnHealthChanged; // current, max
    public event Action OnDied;

    private float invincTimer;
    private float timeSinceLastDamage = Mathf.Infinity;
    private bool rechargeQueued;
    private float rechargeHealthFloat;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        rechargeHealthFloat = currentHealth;
        rechargeQueued = rechargeEnabled && currentHealth < maxHealth;
        PushToUI();
    }

    private void Update()
    {
        if (invincTimer > 0f)
            invincTimer -= Time.deltaTime;

        if (!rechargeEnabled || IsDead || currentHealth >= maxHealth || !rechargeQueued)
            return;

        timeSinceLastDamage += Time.deltaTime;
        if (timeSinceLastDamage < rechargeDelay)
            return;

        rechargeHealthFloat = Mathf.MoveTowards(
            rechargeHealthFloat,
            maxHealth,
            Mathf.Max(0f, rechargeRatePerSecond) * Time.deltaTime);

        int newHealth = Mathf.Clamp(Mathf.RoundToInt(rechargeHealthFloat), 0, maxHealth);
        if (newHealth != currentHealth)
        {
            currentHealth = newHealth;
            PushToUI();

            if (logChanges) Debug.Log($"[PlayerVitals] Auto recharge => {currentHealth}/{maxHealth}", this);
        }

        if (currentHealth >= maxHealth)
        {
            currentHealth = maxHealth;
            rechargeHealthFloat = maxHealth;
            rechargeQueued = false;
            PushToUI();
        }
    }

    public bool Damage(int amount)
    {
        if (amount <= 0) return false;
        if (IsDead) return false;
        if (invincTimer > 0f) return false;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        rechargeHealthFloat = currentHealth;

        if (currentHealth <= 0)
        {
            rechargeQueued = false;
            PushToUI();
            if (logChanges) Debug.Log($"[PlayerVitals] DIED", this);
            OnDied?.Invoke();
        }
        else
        {
            rechargeQueued = rechargeEnabled;
            timeSinceLastDamage = 0f;
            PushToUI();
        }

        invincTimer = Mathf.Max(0f, invincibleSeconds);
        if (logChanges) Debug.Log($"[PlayerVitals] Damage {amount} => {currentHealth}/{maxHealth}", this);
        return true;
    }

    // IDamageable implementation (used by melee, animal bites, bullets, etc.)
    public void TakeDamage(int amount)
    {
        Damage(amount);
    }

    // IDamageable required overload in your project
    public void TakeDamage(float amount)
    {
        // Convert to int damage (round to nearest)
        Damage(Mathf.RoundToInt(amount));
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (IsDead) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        rechargeHealthFloat = currentHealth;
        rechargeQueued = rechargeEnabled && currentHealth < maxHealth;
        PushToUI();
        if (logChanges) Debug.Log($"[PlayerVitals] Heal {amount} => {currentHealth}/{maxHealth}", this);
    }

    public void SetHealth(int newCurrent, int newMax = -1)
    {
        if (newMax > 0) maxHealth = Mathf.Max(1, newMax);
        currentHealth = Mathf.Clamp(newCurrent, 0, maxHealth);
        rechargeHealthFloat = currentHealth;
        rechargeQueued = rechargeEnabled && currentHealth < maxHealth;
        PushToUI();
    }

    public float Health01()
    {
        if (maxHealth <= 0) return 0f;
        return Mathf.Clamp01((float)currentHealth / maxHealth);
    }

    private void PushToUI()
    {
        float t = Health01();

        if (barFill != null)
        {
            var s = barFill.localScale;
            s.x = t;
            barFill.localScale = s;
        }

        if (fillImage != null)
        {
            fillImage.fillAmount = t;
        }

        OnHealth01Changed?.Invoke(t);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

#if UNITY_EDITOR
    [ContextMenu("TEST - Damage 25")]
    private void TestDamage25() => Damage(25);

    [ContextMenu("TEST - Heal 25")]
    private void TestHeal25() => Heal(25);
#endif
}
