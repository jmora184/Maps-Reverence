using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HintSystem : MonoBehaviour
{
    public static HintSystem Instance { get; private set; }

    [Header("UI (optional - will auto-find if empty)")]
    public HintToastUI toastUI;

    [Header("Behavior")]
    public float defaultDuration = 3.0f;
    public float minGapBetweenToasts = 0.1f;

    private readonly Queue<(string msg, float duration)> queue = new();
    private Coroutine runner;

    // Auto-create a HintSystem if none exists (so reflection callers always work)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // If one already exists (even disabled), use it
        var existing = FindObjectOfType<HintSystem>(includeInactive: true);
        if (existing != null) return;

        // Create a new one
        var go = new GameObject("HintSystem");
        go.AddComponent<HintSystem>();
        // Optional:
        // DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        AutoFindToastUI();
    }

    private void OnEnable()
    {
        // If scene loads/changes and toast appears later, we can pick it up.
        if (toastUI == null) AutoFindToastUI();
    }

    // ---------- Public API ----------

    public static void Show(string message, float duration = -1f)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // If instance got destroyed / not yet initialized, try to recover
        if (Instance == null)
        {
            var existing = FindObjectOfType<HintSystem>(includeInactive: true);
            if (existing != null) Instance = existing;
        }

        if (Instance == null)
        {
            Debug.LogWarning("[HintSystem] No HintSystem available (bootstrap failed).");
            return;
        }

        Instance.Enqueue(message, duration);
    }

    public static void Clear()
    {
        if (Instance == null) return;

        Instance.queue.Clear();
        if (Instance.toastUI != null)
            Instance.toastUI.HideInstant();
    }

    // ---------- Internals ----------

    private void Enqueue(string message, float duration)
    {
        float d = (duration <= 0f) ? defaultDuration : duration;
        queue.Enqueue((message, d));

        if (runner == null)
            runner = StartCoroutine(RunQueue());
    }

    private IEnumerator RunQueue()
    {
        while (queue.Count > 0)
        {
            if (toastUI == null)
                AutoFindToastUI();

            if (toastUI == null)
            {
                Debug.LogWarning("[HintSystem] toastUI not found in scene (need a HintToastUI somewhere).");
                queue.Clear();
                break;
            }

            var item = queue.Dequeue();

            toastUI.Show(item.msg);

            float t = 0f;
            while (t < item.duration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            toastUI.Hide();

            if (minGapBetweenToasts > 0f)
            {
                float g = 0f;
                while (g < minGapBetweenToasts)
                {
                    g += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        runner = null;
    }

    private void AutoFindToastUI()
    {
        if (toastUI != null) return;

        // Finds disabled objects too.
        var all = Resources.FindObjectsOfTypeAll<HintToastUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;

            // Only use scene objects (not prefabs/assets)
            if (!h.gameObject.scene.IsValid()) continue;

            toastUI = h;
            return;
        }
    }
}
