using UnityEngine;

/// <summary>
/// Lives on the Ally Icon prefab (UI). Handles:
/// - Health bar fill scaling (barFill pivot X must be 0)
/// - Icon bob animation (unscaled time)
/// - TeamTag (small star) visibility: only shows when the bound ally is part of a team
/// - Gun visual switching so each ally icon can show a different weapon image
/// </summary>
public class AllyHealthIcon : MonoBehaviour
{
    public enum GunVisualType
    {
        None,
        Rifle,
        Shotgun,
        Sniper
    }

    [Header("Health Bar")]
    [SerializeField] private RectTransform barFill;   // drag HealthBar_Green here (pivot X must be 0)

    [Header("Team Tag (small star)")]
    [SerializeField] private GameObject teamTagRoot;  // drag the TeamTag child here (or auto-find by name)
    [SerializeField] private bool forceTeamTagVisible = false;
    [Tooltip("How often (seconds) to re-check team membership. 0 = every LateUpdate.")]
    [SerializeField] private float teamCheckInterval = 0.25f;

    [Header("Ally Icon Bob")]
    [SerializeField] private RectTransform bobTarget; // drag the CHILD you want to bob (ex: IconVisual)
    [SerializeField] private float bobAmount = 6f;    // pixels up/down
    [SerializeField] private float bobSpeed = 2f;     // speed

    [Header("Gun Visuals")]
    [Tooltip("Pick which gun image this UI icon should show. You can set this manually per ally icon in the Inspector.")]
    [SerializeField] private GunVisualType gunVisual = GunVisualType.Rifle;
    [SerializeField] private GameObject rifleRoot;    // child named Rifle
    [SerializeField] private GameObject shotgunRoot;  // child named Shotgun
    [SerializeField] private GameObject sniperRoot;   // child named Sniper

    private Vector3 bobBaseLocalPos;
    private float bobSeed;

    private AllyHealth health;
    private Transform boundAllyRoot;

    private float nextTeamCheckTimeUnscaled;
    private bool lastTeamVisible;

    private void Awake()
    {
        AutoAssignOptionalRefs();

        if (bobTarget != null)
        {
            bobBaseLocalPos = bobTarget.localPosition;
            bobSeed = Random.Range(0f, 1000f); // make each icon slightly different
        }

        // Default: hidden unless forced or teamed
        SetTeamTagVisible(forceTeamTagVisible);
        ApplyGunVisual();
    }

    private void OnEnable()
    {
        if (bobTarget != null)
            bobBaseLocalPos = bobTarget.localPosition;

        // Re-evaluate immediately when enabled
        nextTeamCheckTimeUnscaled = 0f;
        ApplyGunVisual();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoAssignOptionalRefs();
        ApplyGunVisual();
    }
#endif

    private void LateUpdate()
    {
        // Bob
        if (bobTarget != null)
        {
            float y = Mathf.Sin((Time.unscaledTime + bobSeed) * bobSpeed) * bobAmount;
            bobTarget.localPosition = bobBaseLocalPos + new Vector3(0f, y, 0f);
        }

        // Team tag toggle
        if (teamTagRoot != null)
        {
            if (teamCheckInterval <= 0f || Time.unscaledTime >= nextTeamCheckTimeUnscaled)
            {
                nextTeamCheckTimeUnscaled = Time.unscaledTime + Mathf.Max(0.01f, teamCheckInterval);

                bool shouldShow = forceTeamTagVisible || IsBoundAllyInATeam();
                if (shouldShow != lastTeamVisible)
                    SetTeamTagVisible(shouldShow);
            }
        }
    }

    public void Bind(Transform ally)
    {
        if (ally == null) return;

        boundAllyRoot = FindAllyRootTransform(ally);

        // Health binding
        health = ally.GetComponentInParent<AllyHealth>();
        if (health == null) health = ally.GetComponentInChildren<AllyHealth>();
        if (health == null)
        {
            Debug.LogWarning("AllyHealthIcon: Bound ally has no AllyHealth: " + ally.name);
        }
        else
        {
            // initial draw
            SetHealth01(health.Health01());

            // subscribe (avoid double subscribe)
            health.OnHealth01Changed -= SetHealth01;
            health.OnHealth01Changed += SetHealth01;
        }

        // Re-check team tag now that we know the bound ally
        nextTeamCheckTimeUnscaled = 0f;
    }

    public void SetGunVisual(GunVisualType newGunVisual)
    {
        gunVisual = newGunVisual;
        ApplyGunVisual();
    }

    public GunVisualType GetGunVisual()
    {
        return gunVisual;
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

    private void SetTeamTagVisible(bool on)
    {
        lastTeamVisible = on;
        if (teamTagRoot != null)
            teamTagRoot.SetActive(on);
    }

    private void ApplyGunVisual()
    {
        SetActiveSafe(rifleRoot, gunVisual == GunVisualType.Rifle);
        SetActiveSafe(shotgunRoot, gunVisual == GunVisualType.Shotgun);
        SetActiveSafe(sniperRoot, gunVisual == GunVisualType.Sniper);
    }

    private void AutoAssignOptionalRefs()
    {
        if (teamTagRoot == null)
        {
            Transform t = FindDeepChild(transform, "TeamTag");
            if (t != null) teamTagRoot = t.gameObject;
        }

        if (rifleRoot == null)
        {
            Transform t = FindDeepChild(transform, "Rifle");
            if (t != null) rifleRoot = t.gameObject;
        }

        if (shotgunRoot == null)
        {
            Transform t = FindDeepChild(transform, "Shotgun");
            if (t != null) shotgunRoot = t.gameObject;
        }

        if (sniperRoot == null)
        {
            Transform t = FindDeepChild(transform, "Sniper");
            if (t != null) sniperRoot = t.gameObject;
        }
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private bool IsBoundAllyInATeam()
    {
        if (boundAllyRoot == null) return false;

        var tm = TeamManager.Instance;
        if (tm == null) return false;

        // Try the most likely root first
        if (tm.GetTeamOf(boundAllyRoot) != null) return true;

        // Fallbacks: sometimes teams store a parent or child transform
        if (boundAllyRoot.parent != null && tm.GetTeamOf(boundAllyRoot.parent) != null) return true;

        // Shallow child scan (cheap)
        for (int i = 0; i < boundAllyRoot.childCount; i++)
        {
            var c = boundAllyRoot.GetChild(i);
            if (c != null && tm.GetTeamOf(c) != null) return true;
        }

        return false;
    }

    private static Transform FindAllyRootTransform(Transform t)
    {
        if (t == null) return null;

        // Walk up until we find the GameObject tagged "Ally"
        Transform cur = t;
        while (cur != null)
        {
            if (cur.CompareTag("Ally"))
                return cur;

            cur = cur.parent;
        }

        // Otherwise: return the original
        return t;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;

            Transform found = FindDeepChild(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
