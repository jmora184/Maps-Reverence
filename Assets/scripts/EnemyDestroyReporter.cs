using UnityEngine;

public class EnemyDestroyReporter : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Logs when this object reports a counted enemy removal.")]
    public bool debugLogs = true;

    private static bool applicationIsQuitting;
    private bool armed;
    private bool counted;

    private void Awake()
    {
        counted = false;
        armed = false;
    }

    private void Start()
    {
        // Don't allow startup disable events to count.
        armed = true;
    }

    private void OnApplicationQuit()
    {
        applicationIsQuitting = true;
    }

    private void OnDestroy()
    {
        TryReport("OnDestroy");
    }

    private void OnDisable()
    {
        // Fallback for enemies that are deactivated instead of destroyed.
        TryReport("OnDisable");
    }

    private void TryReport(string source)
    {
        if (!Application.isPlaying) return;
        if (applicationIsQuitting) return;
        if (!armed) return;
        if (counted) return;
        if (!CompareTag("Enemy")) return;
        if (EnemyDestroyTracker.Instance == null) return;
        if (!EnemyDestroyTracker.Instance.isActiveAndEnabled) return;

        counted = true;
        EnemyDestroyTracker.Instance.RegisterEnemyDestroyed(gameObject);

        if (debugLogs)
            Debug.Log($"[EnemyDestroyReporter] Counted '{name}' via {source}.", this);
    }
}
