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
public class PlayerVitals : MonoBehaviour
{
    public static PlayerVitals Instance { get; private set; }

    [Header("Health")]
    [SerializeField] private int maxHealth = 500;
    [SerializeField] private int currentHealth = 500;

    [Header("Invincibility")]
    [Tooltip("Seconds of invincibility after taking damage. 0 = none.")]
    [SerializeField] private float invincibleSeconds = 1f;

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
        PushToUI();
    }

    private void Update()
    {
        if (invincTimer > 0f)
            invincTimer -= Time.deltaTime;
    }

    public bool Damage(int amount)
    {
        if (amount <= 0) return false;
        if (IsDead) return false;
        if (invincTimer > 0f) return false;

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            PushToUI();
            if (logChanges) Debug.Log($"[PlayerVitals] DIED", this);
            OnDied?.Invoke();
        }
        else
        {
            PushToUI();
        }

        invincTimer = Mathf.Max(0f, invincibleSeconds);
        if (logChanges) Debug.Log($"[PlayerVitals] Damage {amount} => {currentHealth}/{maxHealth}", this);
        return true;
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        if (IsDead) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        PushToUI();
        if (logChanges) Debug.Log($"[PlayerVitals] Heal {amount} => {currentHealth}/{maxHealth}", this);
    }

    public void SetHealth(int newCurrent, int newMax = -1)
    {
        if (newMax > 0) maxHealth = Mathf.Max(1, newMax);
        currentHealth = Mathf.Clamp(newCurrent, 0, maxHealth);
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
