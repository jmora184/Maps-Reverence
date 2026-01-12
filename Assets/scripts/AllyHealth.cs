using System;
using UnityEngine;

public class AllyHealth : MonoBehaviour
{
    public int maxHealth = 5;
    public int currentHealth = 5;

    public event Action<float> OnHealth01Changed;

    void Awake()
    {
        if (currentHealth <= 0) currentHealth = maxHealth;
        OnHealth01Changed?.Invoke(Health01()); // initial
    }

    public float Health01()
    {
        if (maxHealth <= 0) return 0f;
        return Mathf.Clamp01((float)currentHealth / maxHealth);
    }

    public void DamageAlly(int damageAmount)
    {
        currentHealth -= damageAmount;
        if (currentHealth < 0) currentHealth = 0;

        // 🔥 THIS is what updates the UI
        OnHealth01Changed?.Invoke(Health01());

        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }
}
