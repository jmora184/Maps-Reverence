using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays the number of currently active, living allies in the scene.
/// Counts:
/// - Allies already active in the world
/// - Spawned allies
/// - Recruited allies after they become active
/// Ignores:
/// - Inactive GameObjects
/// - Dead allies
/// - Allies with AllyActivationGate that are not yet activated/recruited
/// </summary>
[DisallowMultipleComponent]
public class AllyActiveCountUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text countText;
    [SerializeField] private string prefix = "x ";
    [SerializeField] private string suffix = "";

    [Header("Scan Settings")]
    [SerializeField] private float rescanInterval = 0.5f;
    [SerializeField] private bool requireAllyHealth = true;
    [SerializeField] private bool requireTag = false;
    [SerializeField] private string allyTag = "Ally";

    private readonly List<AllyHealth> trackedAllies = new List<AllyHealth>();
    private readonly HashSet<AllyHealth> trackedLookup = new HashSet<AllyHealth>();
    private float nextRescanTime;

    private void OnEnable()
    {
        FullRefresh();
    }

    private void OnDisable()
    {
        UnsubscribeAll();
    }

    private void Update()
    {
        if (Time.time >= nextRescanTime)
            FullRefresh();
    }

    private void FullRefresh()
    {
        nextRescanTime = Time.time + Mathf.Max(0.1f, rescanInterval);

        CleanupDestroyedReferences();
        DiscoverAllies();
        UpdateCountText();
    }

    private void DiscoverAllies()
    {
        AllyHealth[] allAllies = FindObjectsOfType<AllyHealth>(true);

        for (int i = 0; i < allAllies.Length; i++)
        {
            AllyHealth ally = allAllies[i];
            if (ally == null) continue;

            if (requireAllyHealth && ally.GetComponent<AllyHealth>() == null)
                continue;

            if (requireTag && !ally.CompareTag(allyTag))
                continue;

            if (!trackedLookup.Contains(ally))
            {
                trackedLookup.Add(ally);
                trackedAllies.Add(ally);
                ally.OnDied += HandleAllyDied;
            }
        }
    }

    private void HandleAllyDied()
    {
        UpdateCountText();
    }

    private void CleanupDestroyedReferences()
    {
        for (int i = trackedAllies.Count - 1; i >= 0; i--)
        {
            AllyHealth ally = trackedAllies[i];
            if (ally != null) continue;

            trackedAllies.RemoveAt(i);
        }

        trackedLookup.Clear();

        for (int i = 0; i < trackedAllies.Count; i++)
        {
            if (trackedAllies[i] != null)
                trackedLookup.Add(trackedAllies[i]);
        }
    }

    private int GetActiveLivingAllyCount()
    {
        int count = 0;

        for (int i = 0; i < trackedAllies.Count; i++)
        {
            AllyHealth ally = trackedAllies[i];

            if (!ShouldCountAlly(ally))
                continue;

            count++;
        }

        return count;
    }

    private bool ShouldCountAlly(AllyHealth ally)
    {
        if (ally == null)
            return false;

        if (!ally.gameObject.activeInHierarchy)
            return false;

        if (ally.IsDead)
            return false;

        AllyActivationGate activationGate = ally.GetComponent<AllyActivationGate>();
        if (activationGate != null && !activationGate.IsActive)
            return false;

        return true;
    }

    private void UpdateCountText()
    {
        if (countText == null)
            return;

        int count = GetActiveLivingAllyCount();
        countText.text = prefix + count + suffix;
    }

    private void UnsubscribeAll()
    {
        for (int i = 0; i < trackedAllies.Count; i++)
        {
            AllyHealth ally = trackedAllies[i];
            if (ally != null)
                ally.OnDied -= HandleAllyDied;
        }

        trackedAllies.Clear();
        trackedLookup.Clear();
    }
}
