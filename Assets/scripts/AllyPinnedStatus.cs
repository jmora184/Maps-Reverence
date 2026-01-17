using System;
using UnityEngine;

/// <summary>
/// Simple "pinned" flag for an ally (and implicitly its team).
/// v1: driven by AllyController.chasing (combat engagement).
/// </summary>
[DisallowMultipleComponent]
public class AllyPinnedStatus : MonoBehaviour
{
    [SerializeField] private bool isPinned;

    /// <summary>True when this unit is pinned (engaged) and should not be commandable.</summary>
    public bool IsPinned => isPinned;

    /// <summary>Fires when pinned state changes.</summary>
    public event Action<bool> OnPinnedChanged;

    public void SetPinned(bool pinned)
    {
        if (isPinned == pinned) return;
        isPinned = pinned;
        OnPinnedChanged?.Invoke(isPinned);
    }
}
