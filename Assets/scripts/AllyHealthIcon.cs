using UnityEngine;

public class AllyHealthIcon : MonoBehaviour
{
    [SerializeField] private RectTransform barFill;   // drag HealthBar_Green here (pivot X must be 0)

    [Header("Ally Icon Bob")]
    [SerializeField] private RectTransform bobTarget; // drag the CHILD you want to bob (ex: IconVisual)
    [SerializeField] private float bobAmount = 6f;    // pixels up/down
    [SerializeField] private float bobSpeed = 2f;     // speed

    private Vector3 bobBaseLocalPos;
    private float bobSeed;

    private AllyHealth health;

    void Awake()
    {
        if (bobTarget != null)
        {
            bobBaseLocalPos = bobTarget.localPosition;
            bobSeed = Random.Range(0f, 1000f); // make each icon slightly different
        }
    }

    void OnEnable()
    {
        if (bobTarget != null)
            bobBaseLocalPos = bobTarget.localPosition;
    }

    void LateUpdate()
    {
        if (bobTarget == null) return;

        float y = Mathf.Sin((Time.unscaledTime + bobSeed) * bobSpeed) * bobAmount;
        bobTarget.localPosition = bobBaseLocalPos + new Vector3(0f, y, 0f);
    }

    public void Bind(Transform ally)
    {
        if (ally == null) return;

        health = ally.GetComponentInParent<AllyHealth>();
        if (health == null) health = ally.GetComponentInChildren<AllyHealth>();
        if (health == null)
        {
            Debug.LogWarning("AllyIconHealthUI: Ally has no AllyHealthController: " + ally.name);
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