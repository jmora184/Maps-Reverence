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
}
