using System.Collections.Generic;
using UnityEngine;

public class CommandQueue : MonoBehaviour
{
    public static CommandQueue Instance { get; private set; }

    private readonly List<QueuedMove> moves = new();

    // ✅ NEW: “preview plan” per unit (so arrows persist in command mode)
    private readonly Dictionary<GameObject, Vector3> plannedDestByUnit = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private class QueuedMove
    {
        public List<GameObject> selection;
        public Vector3 destination;
    }

    public void EnqueueMove(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        if (selection == null || selection.Count == 0) return;

        var copy = new List<GameObject>(selection.Count);
        for (int i = 0; i < selection.Count; i++)
        {
            var u = selection[i];
            if (u == null) continue;
            copy.Add(u);

            // ✅ store planned destination per unit
            plannedDestByUnit[u] = destination;
        }

        moves.Add(new QueuedMove
        {
            selection = copy,
            destination = destination
        });
    }

    // ✅ NEW: arrows can query “planned destination”
    public bool TryGetPlannedDestination(GameObject unit, out Vector3 dest)
    {
        if (unit != null && plannedDestByUnit.TryGetValue(unit, out dest))
            return true;

        dest = default;
        return false;
    }

    public void ClearAll()
    {
        moves.Clear();
        plannedDestByUnit.Clear();
    }

    // Called when exiting command mode
    public void FlushMoves(CommandExecutor executor)
    {
        if (executor == null) return;

        for (int i = 0; i < moves.Count; i++)
        {
            var m = moves[i];
            if (m == null || m.selection == null || m.selection.Count == 0) continue;
            executor.ExecuteMoveOrder(m.selection, m.destination);
        }

        // ✅ once we switch to FPS and execute, clear planned preview
        moves.Clear();
        plannedDestByUnit.Clear();
    }
}
