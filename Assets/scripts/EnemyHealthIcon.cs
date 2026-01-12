using UnityEngine;

public class EnemyIconHealthUI : MonoBehaviour
{
    [SerializeField] private RectTransform barFill;   // drag HealthBar_Red here (pivot X must be 0)

    [Header("Enemy Icon Bob")]
    [SerializeField] private RectTransform bobTarget; // drag the CHILD you want to bob (ex: IconVisual)
    [SerializeField] private float bobAmount = 6f;    // pixels up/down
    [SerializeField] private float bobSpeed = 2f;     // speed

    private Vector3 bobBaseLocalPos;                  // CHANGED (Vector3)
    private float bobSeed;

    private EnemyHealthController health;

    void Awake()
    {
        if (bobTarget != null)
        {
            bobBaseLocalPos = bobTarget.localPosition; // CHANGED
            bobSeed = Random.Range(0f, 1000f);
        }
    }

    void OnEnable()
    {
        if (bobTarget != null)
            bobBaseLocalPos = bobTarget.localPosition; // CHANGED
    }

    void LateUpdate()
    {
        if (bobTarget == null) return;

        float y = Mathf.Sin((Time.unscaledTime + bobSeed) * bobSpeed) * bobAmount;

        // CHANGED: bob using localPosition so it doesn't drift diagonally
        bobTarget.localPosition = bobBaseLocalPos + new Vector3(0f, y, 0f);
    }

    public void Bind(Transform enemy)
    {
        if (enemy == null) return;

        health = enemy.GetComponentInParent<EnemyHealthController>();
        if (health == null) health = enemy.GetComponentInChildren<EnemyHealthController>();
        if (health == null)
        {
            Debug.LogWarning("EnemyIconHealthUI: Enemy has no EnemyHealthController: " + enemy.name);
            return;
        }

        // initial draw
        SetHealth01(health.Health01());

        // subscribe (avoid double subscribe)
        health.OnHealth01Changed -= SetHealth01;
        health.OnHealth01Changed += SetHealth01;
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnHealth01Changed -= SetHealth01;
    }

    private void SetHealth01(float t)
    {
        if (barFill == null) return;

        t = Mathf.Clamp01(t);
        var s = barFill.localScale;
        s.x = t;
        barFill.localScale = s;
    }
}
