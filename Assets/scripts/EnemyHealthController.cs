using System;
using UnityEngine;

public class EnemyHealthController : MonoBehaviour
{
    public int maxHealth = 5;
    public int currentHealth = 5;

    public EnemyController theEC;

    // UI can subscribe to this
    public event Action<float> OnHealth01Changed;

    private void Awake()
    {
        if (maxHealth <= 0) maxHealth = 5;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();
    }

    public void DamageEnemy(int damageAmount)
    {
        currentHealth -= damageAmount;

        if (theEC != null)
            theEC.GetShot();

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        RaiseHealthChanged();

        if (currentHealth <= 0)
            Destroy(gameObject);
    }

    public float Health01()
    {
        return (maxHealth <= 0) ? 0f : (float)currentHealth / maxHealth;
    }

    private void RaiseHealthChanged()
    {
        OnHealth01Changed?.Invoke(Health01());
    }
}