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
    [SerializeField] private float invincibleSeconds = 0f;// set to 0 to allow simultaneous hits to stack

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

    [Header("Directional Damage UI (Optional)")]
    [Tooltip("Optional directional damage indicator for left/right/behind bullet hits.")]
    [SerializeField] private DamageDirectionIndicator damageIndicator;

    [Header("Health Bar Damage Flash (Optional)")]
    [Tooltip("Optional red Image placed over the player health bar. Script flashes its alpha when damage is taken.")]
    [SerializeField] private Image healthBarDamageFlashImage;

    [Tooltip("Peak alpha applied to the red health-bar flash image when the player takes damage.")]
    [Range(0f, 1f)]
    [SerializeField] private float healthBarFlashPeakAlpha = 0.7f;

    [Tooltip("How quickly the health-bar flash image fades back to transparent.")]
    [SerializeField] private float healthBarFlashFadeSpeed = 4.5f;

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

        if (damageIndicator == null)
            damageIndicator = FindFirstObjectByType<DamageDirectionIndicator>();

        SetHealthBarFlashAlpha(0f);
        PushToUI();
    }

    private void Update()
    {
        if (invincTimer > 0f)
            invincTimer -= Time.deltaTime;

        FadeHealthBarFlash();

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
        return DamageInternal(amount, false, default);
    }

    public bool Damage(int amount, Vector3 sourceWorldPosition)
    {
        return DamageInternal(amount, true, sourceWorldPosition);
    }

    public void TakeDamageFromSource(int amount, Vector3 sourceWorldPosition)
    {
        DamageInternal(amount, true, sourceWorldPosition);
    }

    public void TakeDamageFromSource(float amount, Vector3 sourceWorldPosition)
    {
        DamageInternal(Mathf.RoundToInt(amount), true, sourceWorldPosition);
    }

    private bool DamageInternal(int amount, bool hasSource, Vector3 sourceWorldPosition)
    {
        if (amount <= 0) return false;
        if (IsDead) return false;
        if (invincTimer > 0f) return false;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        rechargeHealthFloat = currentHealth;

        if (hasSource && damageIndicator != null)
            damageIndicator.FlashFromWorldPosition(sourceWorldPosition);

        TriggerHealthBarFlash();

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

        if (logChanges)
        {
            if (hasSource)
                Debug.Log($"[PlayerVitals] Damage {amount} from {sourceWorldPosition} => {currentHealth}/{maxHealth}", this);
            else
                Debug.Log($"[PlayerVitals] Damage {amount} => {currentHealth}/{maxHealth}", this);
        }

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

    private void TriggerHealthBarFlash()
    {
        if (healthBarDamageFlashImage == null)
            return;

        SetHealthBarFlashAlpha(Mathf.Clamp01(healthBarFlashPeakAlpha));
    }

    private void FadeHealthBarFlash()
    {
        if (healthBarDamageFlashImage == null)
            return;

        Color c = healthBarDamageFlashImage.color;
        if (c.a <= 0f)
            return;

        c.a = Mathf.MoveTowards(c.a, 0f, Mathf.Max(0f, healthBarFlashFadeSpeed) * Time.deltaTime);
        healthBarDamageFlashImage.color = c;
    }

    private void SetHealthBarFlashAlpha(float alpha)
    {
        if (healthBarDamageFlashImage == null)
            return;

        Color c = healthBarDamageFlashImage.color;
        c.a = Mathf.Clamp01(alpha);
        healthBarDamageFlashImage.color = c;
    }

#if UNITY_EDITOR
    [ContextMenu("TEST - Damage 25")]
    private void TestDamage25() => Damage(25);

    [ContextMenu("TEST - Heal 25")]
    private void TestHeal25() => Heal(25);
#endif
}
