using UnityEngine;

/// <summary>
/// Attach this to the SAME GameObject that has your HP script for the melee enemy.
/// This guarantees other systems (Animal bite, bullets, etc.) can always damage the melee enemy,
/// even if they hit a child collider/hitbox.
///
/// It implements IDamageable and forwards:
/// - HP reduction -> your health script (best-effort via SendMessage/known method names)
/// - hit reaction -> MeleeEnemy2Controller.TakeDamage(...)
/// - aggro -> MeleeEnemy2Controller.SetCombatTarget(attacker)
///
/// NOTE: This does not change your HP logic; it just provides a consistent receiver.
/// </summary>
[DisallowMultipleComponent]
public class MeleeEnemyDamageReceiver : MonoBehaviour, IDamageable
{
    [Tooltip("Optional: melee controller (auto-found).")]
    public MeleeEnemy2Controller controller;

    [Tooltip("Optional: reference to a health/HP component on this object (auto-found if left empty).")]
    public MonoBehaviour healthComponent;

    [Tooltip("Method name used on your health component to apply damage. Common: TakeDamage / ApplyDamage / Damage / Hurt")]
    public string[] healthMethodNames = new string[] { "TakeDamage", "ApplyDamage", "Damage", "Hurt", "ReceiveDamage" };

    private void Awake()
    {
        if (!controller) controller = GetComponent<MeleeEnemy2Controller>();

        if (healthComponent == null)
        {
            // Try to pick a likely health component automatically.
            var monos = GetComponents<MonoBehaviour>();
            foreach (var m in monos)
            {
                if (!m) continue;
                string n = m.GetType().Name.ToLowerInvariant();
                if (n.Contains("health") || n.Contains("vitals") || n.Contains("hp"))
                {
                    healthComponent = m;
                    break;
                }
            }
        }
    }

    public void TakeDamage(float damage)
    {
        // Hit reaction on melee controller
        if (controller) controller.TakeDamage(damage);

        // Apply HP damage on the health script (best-effort)
        TryApplyHpDamage(damage);
    }

    // Optional convenience overload for scripts that pass int
    public void TakeDamage(int damage)
    {
        TakeDamage((float)damage);
    }

    // Optional: if you want to pass attacker from your bullets/animals:
    public void TakeDamage(float damage, Transform attacker)
    {
        if (controller) controller.TakeDamage(damage, attacker);
        TryApplyHpDamage(damage);
    }

    private void TryApplyHpDamage(float dmg)
    {
        if (healthComponent == null)
        {
            // Try SendMessage on self as last resort (won't throw)
            gameObject.SendMessage("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
            gameObject.SendMessage("ApplyDamage", dmg, SendMessageOptions.DontRequireReceiver);
            gameObject.SendMessage("Damage", dmg, SendMessageOptions.DontRequireReceiver);
            return;
        }

        var t = healthComponent.GetType();

        foreach (var methodName in healthMethodNames)
        {
            if (string.IsNullOrEmpty(methodName)) continue;

            var mFloat = t.GetMethod(methodName, new System.Type[] { typeof(float) });
            if (mFloat != null)
            {
                mFloat.Invoke(healthComponent, new object[] { dmg });
                return;
            }

            var mInt = t.GetMethod(methodName, new System.Type[] { typeof(int) });
            if (mInt != null)
            {
                mInt.Invoke(healthComponent, new object[] { Mathf.RoundToInt(dmg) });
                return;
            }
        }

        // Fallback
        gameObject.SendMessage("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
    }
}
