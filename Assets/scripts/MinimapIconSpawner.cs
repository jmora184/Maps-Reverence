using System.Collections.Generic;
using UnityEngine;


public class MinimapIconSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Where the icons should be created (recommended: RawImage/IconContainer).")]
    public RectTransform iconContainer;

    [Tooltip("Minimap camera (your FullMini camera).")]
    public Camera minimapCamera;

    [Tooltip("The rect used for positioning (recommended: the RawImage RectTransform OR IconContainer rect).")]
    public RectTransform mapRect;

    [Header("Prefab")]
    [Tooltip("Your AllyMiniIcon prefab (must have MinimapIconFollow on the ROOT).")]
    public GameObject allyMiniIconPrefab;

    [Header("Auto-find allies (optional)")]
    [Tooltip("If set, spawns icons for GameObjects with this tag on Start.")]
    public string allyTag = "Ally";

    // Keeps one icon per ally so you don't duplicate
    private readonly Dictionary<Transform, MinimapIconFollow> _spawned = new();

    void Start()
    {
        // Optional: auto-spawn for all allies in scene using a tag
        if (!string.IsNullOrEmpty(allyTag))
        {
            var allies = GameObject.FindGameObjectsWithTag(allyTag);
            for (int i = 0; i < allies.Length; i++)
                RegisterAlly(allies[i].transform);
        }
    }

    /// <summary>
    /// Call this when you spawn a new ally so they get a minimap icon.
    /// </summary>
    public MinimapIconFollow RegisterAlly(Transform ally)
    {
        if (!ally) return null;
        if (_spawned.ContainsKey(ally)) return _spawned[ally];

        if (!allyMiniIconPrefab || !iconContainer || !minimapCamera || !mapRect)
        {
            Debug.LogWarning("MinimapIconSpawner is missing references (prefab/container/camera/mapRect).");
            return null;
        }

        GameObject iconGO = Instantiate(allyMiniIconPrefab, iconContainer);
        var follow = iconGO.GetComponent<MinimapIconFollow>();

        if (!follow)
        {
            Debug.LogWarning("AllyMiniIcon prefab must have MinimapIconFollow on the ROOT object.");
            Destroy(iconGO);
            return null;
        }

        // Wire it up (THIS is the part that 'attaches to ally')
        follow.target = ally;
        follow.directionSource = ally;          // change if you want DirectionSprite instead
        follow.minimapCamera = minimapCamera;
        follow.mapRect = mapRect;

        _spawned.Add(ally, follow);
        return follow;
    }

    /// <summary>
    /// Optional cleanup if an ally dies.
    /// </summary>
    public void UnregisterAlly(Transform ally)
    {
        if (!ally) return;
        if (_spawned.TryGetValue(ally, out var follow) && follow)
            Destroy(follow.gameObject);

        _spawned.Remove(ally);
    }
}