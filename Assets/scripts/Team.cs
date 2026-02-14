using System.Collections.Generic;
using UnityEngine;

public class Team
{
    public int Id { get; private set; }

    // Members of the team
    public List<Transform> Members { get; private set; } = new();

    // Where the star should appear (anchored to the "second ally" on join)
    public Transform Anchor { get; set; }

    // Preferred spacing radius (meters) around Anchor for members (non-anchor members get slotted around the leader)
    public float FormationRadius { get; set; } = 2.4f;

    public Team(int id, Transform a, Transform b)
    {
        Id = id;

        if (a != null) Members.Add(a);
        if (b != null && b != a) Members.Add(b);

        // Default anchor = second ally if provided, otherwise first
        Anchor = b != null ? b : a;
    }

    public bool Contains(Transform t)
    {
        if (t == null) return false;
        return Members.Contains(t);
    }

    public void Add(Transform t)
    {
        if (t == null) return;
        if (!Members.Contains(t))
            Members.Add(t);
    }

    public string GetDisplayLabel()
    {
        int count = Members != null ? Members.Count : 0;
        return $"Team {Id} ({count})";
    }


    // --- Option B: Team planned destination (a.k.a move target) ---
    // MoveDestinationMarkerSystem already tries to call SetMoveTarget/ClearMoveTarget via reflection.
    // Some scripts may call SetPlannedDestination/ClearPlannedDestination.
    private bool _hasMoveTarget;
    private Vector3 _moveTarget;

    public bool HasMoveTarget => _hasMoveTarget;
    public Vector3 MoveTarget => _moveTarget;

    // Back-compat aliases
    public bool HasPlannedDestination => _hasMoveTarget;
    public Vector3 PlannedDestination => _moveTarget;

    public void SetMoveTarget(Vector3 worldPos)
    {
        _hasMoveTarget = true;
        _moveTarget = worldPos;
    }

    public void ClearMoveTarget()
    {
        _hasMoveTarget = false;
        _moveTarget = Vector3.zero;
    }

    public void SetPlannedDestination(Vector3 worldPos) => SetMoveTarget(worldPos);
    public void ClearPlannedDestination() => ClearMoveTarget();

    /// <summary>Average (centroid) world position of all non-null members.</summary>
    public Vector3 GetCentroid()
    {
        if (Members == null || Members.Count == 0)
            return (Anchor != null) ? Anchor.position : Vector3.zero;

        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < Members.Count; i++)
        {
            var t = Members[i];
            if (t == null) continue;
            sum += t.position;
            n++;
        }

        if (n == 0)
            return (Anchor != null) ? Anchor.position : Vector3.zero;

        return sum / n;
    }
}
