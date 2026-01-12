using System.Collections.Generic;
using UnityEngine;

public class Team
{
    public int Id { get; private set; }

    // Members of the team
    public List<Transform> Members { get; private set; } = new();

    // ✅ NEW: where the star should appear (we anchor it to the "second ally" on join)
    public Transform Anchor { get; set; }

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

    // ✅ Required by your TeamIconUI.cs
    // You can change this string format any time.
    public string GetDisplayLabel()
    {
        int count = Members != null ? Members.Count : 0;
        return $"Team {Id} ({count})";
    }
}
