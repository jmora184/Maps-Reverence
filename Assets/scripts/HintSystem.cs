using System.Collections.Generic;
using UnityEngine;

public class HintSystem : MonoBehaviour
{
    public static HintSystem Instance { get; private set; }
    public static HintSystem instance => Instance; // backwards compat

    [Header("UI")]
    public HintToastUI toast;

    [Header("Defaults")]
    public float defaultHoldSeconds = 1.6f;

    // If Show() is called before Instance exists, buffer it here.
    private static readonly Queue<(string msg, float hold)> pending = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (toast == null)
            toast = FindObjectOfType<HintToastUI>(true);

        // Flush anything that fired before Awake finished.
        FlushPending();
    }

    // ====== BACKWARDS COMPAT: STATIC SHOW ======
    public static void Show(string message)
    {
        Show(message, Instance != null ? Instance.defaultHoldSeconds : 1.6f);
    }

    public static void Show(string message, float holdSeconds)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // If system isn't ready yet, buffer it.
        if (Instance == null || Instance.toast == null)
        {
            pending.Enqueue((message, holdSeconds));
            return;
        }

        // IMPORTANT: immediate interrupt
        Instance.toast.InterruptAndShow(message, holdSeconds);
    }

    public static void Hide()
    {
        if (Instance == null || Instance.toast == null) return;
        Instance.toast.HideImmediate();
    }

    private void FlushPending()
    {
        if (toast == null) return;

        while (pending.Count > 0)
        {
            var p = pending.Dequeue();
            toast.InterruptAndShow(p.msg, p.hold);
        }
    }
}
