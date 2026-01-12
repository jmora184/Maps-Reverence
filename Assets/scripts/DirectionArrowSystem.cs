using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class DirectionArrowSystem : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Prefab must contain a SpriteRenderer + DirectionArrowFollower component.")]
    public DirectionArrowFollower arrowPrefab;

    [Tooltip("Optional parent for spawned arrows (keeps hierarchy clean).")]
    public Transform arrowParent;

    [Header("Targets")]
    public string allyTag = "Ally";

    [Header("Command Mode Gate")]
    public bool onlyShowInCommandMode = true;

    [Header("Rescan")]
    [Tooltip("How often we rescan for allies (handles spawn/despawn).")]
    public float rescanInterval = 0.5f;

    // unit -> spawned arrow
    private readonly Dictionary<Transform, DirectionArrowFollower> arrowsByUnit = new();
    private float nextRescanTime;

    private void Awake()
    {
        if (arrowParent == null) arrowParent = transform;
    }

    private void OnEnable()
    {
        nextRescanTime = 0f;
    }

    private void Update()
    {
        if (arrowPrefab == null) return;

        if (onlyShowInCommandMode && !IsInCommandMode())
        {
            SetAllArrowsActive(false);
            return;
        }

        // rescan periodically so each ally gets its own arrow
        if (Time.unscaledTime >= nextRescanTime)
        {
            nextRescanTime = Time.unscaledTime + rescanInterval;
            RescanAndBind();
        }

        // keep them active if we’re allowed to show
        SetAllArrowsActive(true);
    }

    private bool IsInCommandMode()
    {
        if (CommandCamToggle.Instance != null) return CommandCamToggle.Instance.IsCommandMode;
        return true; // fallback: if no toggle, allow
    }

    private void RescanAndBind()
    {
        // Build a "still alive" set
        var aliveUnits = new HashSet<Transform>();

        var allies = GameObject.FindGameObjectsWithTag(allyTag);
        foreach (var ally in allies)
        {
            if (ally == null) continue;

            Transform unit = ally.transform;
            aliveUnits.Add(unit);

            if (arrowsByUnit.ContainsKey(unit))
                continue;

            // Find agent (on ally or its children)
            var agent = ally.GetComponent<NavMeshAgent>();
            if (agent == null) agent = ally.GetComponentInChildren<NavMeshAgent>();

            // Spawn arrow for this unit
            var arrow = Instantiate(arrowPrefab, arrowParent);
            arrow.name = $"Arrow_{ally.name}";

            // ✅ THIS IS THE IMPORTANT LINE:
            // This is where the system binds each spawned arrow to a DIFFERENT ally.
            arrow.Bind(unit, agent);

            arrowsByUnit[unit] = arrow;
        }

        // Cleanup arrows whose units no longer exist
        var dead = new List<Transform>();
        foreach (var kv in arrowsByUnit)
        {
            var unit = kv.Key;
            var arrow = kv.Value;

            if (unit == null || !aliveUnits.Contains(unit) || arrow == null)
                dead.Add(unit);
        }

        foreach (var unit in dead)
        {
            if (unit != null && arrowsByUnit.TryGetValue(unit, out var arrow) && arrow != null)
                Destroy(arrow.gameObject);

            arrowsByUnit.Remove(unit);
        }
    }

    private void SetAllArrowsActive(bool on)
    {
        foreach (var kv in arrowsByUnit)
        {
            if (kv.Value != null)
                kv.Value.gameObject.SetActive(on);
        }
    }
}
