using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton helper for showing/hiding hover tooltips.
/// Stores the current pointer position so HoverHintUI can follow the mouse.
///
/// Supports an optional per-call pixel offset (added on top of HoverHintUI.pixelOffset).
/// </summary>
public class HoverHintSystem : MonoBehaviour
{
    public static HoverHintSystem Instance { get; private set; }
    public static HoverHintSystem instance => Instance; // backwards compat

    [Header("UI")]
    public HoverHintUI hoverUI;

    // Track current owner so multiple icons don't fight.
    private Object currentOwner;

    // Pointer tracking
    public bool HasPointer { get; private set; }
    public Vector2 PointerScreenPos { get; private set; }

    // If Show() is called before Instance exists, buffer it here.
    private static readonly Queue<(Object owner, RectTransform anchor, string msg, Vector2 extraOffset)> pending = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (hoverUI == null)
            hoverUI = FindObjectOfType<HoverHintUI>(true);

        FlushPending();
    }

    public static void Show(Object owner, RectTransform anchor, string message)
    {
        Show(owner, anchor, message, Vector2.zero);
    }

    /// <summary>
    /// Show tooltip with an extra pixel offset (added on top of HoverHintUI.pixelOffset).
    /// Useful for per-icon nudges.
    /// </summary>
    public static void Show(Object owner, RectTransform anchor, string message, Vector2 extraPixelOffset)
    {
        if (anchor == null) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        if (Instance == null || Instance.hoverUI == null)
        {
            pending.Enqueue((owner, anchor, message, extraPixelOffset));
            return;
        }

        Instance.currentOwner = owner;
        Instance.hoverUI.Show(anchor, message, extraPixelOffset);
    }

    public static void Hide(Object owner)
    {
        if (Instance == null || Instance.hoverUI == null) return;

        // Only the current owner may hide (prevents flicker during fast transitions).
        if (owner != null && Instance.currentOwner != null && owner != Instance.currentOwner)
            return;

        Instance.currentOwner = null;
        Instance.hoverUI.Hide();
    }

    public static void HideImmediate()
    {
        if (Instance == null || Instance.hoverUI == null) return;
        Instance.currentOwner = null;
        Instance.hoverUI.HideImmediate();
    }

    /// <summary>Update the pointer screen position (usually from UIHoverHintTarget).</summary>
    public static void UpdatePointer(Object owner, Vector2 screenPos)
    {
        if (Instance == null) return;
        if (owner != null && Instance.currentOwner != null && owner != Instance.currentOwner) return;

        Instance.PointerScreenPos = screenPos;
        Instance.HasPointer = true;
    }

    private void FlushPending()
    {
        if (hoverUI == null) return;

        while (pending.Count > 0)
        {
            var p = pending.Dequeue();
            currentOwner = p.owner;
            hoverUI.Show(p.anchor, p.msg, p.extraOffset);
        }
    }
}
