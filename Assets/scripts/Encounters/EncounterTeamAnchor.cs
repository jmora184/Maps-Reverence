using UnityEngine;

/// <summary>
/// Computes a "team anchor" (centroid) from this transform's children.
///
/// IMPORTANT:
/// If this component is on a team ROOT that is also the parent of team members,
/// you must NOT move the root transform.position, or you'll drag the entire team and can cause drift.
///
/// Consumers should read AnchorWorldPosition for UI placement / leash logic.
/// </summary>
public class EncounterTeamAnchor : MonoBehaviour
{
    [Header("Team Meta")]
    [Tooltip("Used by systems that filter anchors by faction (e.g., EnemyTeamIconSystem).")]
    public EncounterDirectorPOC.Faction faction = EncounterDirectorPOC.Faction.Enemy;

    [Header("Anchor")]
    [Tooltip("If true, anchor position is recomputed each frame from children.")]
    public bool updateContinuously = true;

    [Tooltip("If true, anchor position is smoothed to reduce jitter.")]
    public bool smooth = true;

    [Tooltip("Smoothing speed (higher = snappier).")]
    public float smoothSpeed = 10f;

    [Tooltip("If true, drives this transform.position to the computed anchor position.\n\nLeave OFF when this object is the PARENT of the team members (typical EnemyTeam_* root), or it will drag the whole team.")]
    public bool driveTransformPosition = false;

    [Tooltip("If false, inactive children are ignored in the centroid.")]
    public bool includeInactiveChildren = false;

    /// <summary>Latest computed anchor position in WORLD space.</summary>
    public Vector3 AnchorWorldPosition { get; private set; }

    // --- Optional: planned destination storage for UI helpers (eg. direction arrows) ---
    // This does NOT drive movement. Your AI scripts can set/clear this purely for UI.
    private bool _hasMoveTarget;
    private Vector3 _moveTarget;

    /// <summary>True when an AI script has set a UI-only move target.</summary>
    public bool HasMoveTarget => _hasMoveTarget;

    /// <summary>UI-only move target in WORLD space (does not drive AI).</summary>
    public Vector3 MoveTarget => _moveTarget;

    // Back-compat aliases (mirrors Team.cs naming)
    public bool HasPlannedDestination => _hasMoveTarget;
    public Vector3 PlannedDestination => _moveTarget;

    /// <summary>
    /// Set a planned destination for UI purposes (direction arrow, etc.).
    /// Your AI scripts can call this whenever they pick a new destination.
    /// </summary>
    public void SetMoveTarget(Vector3 worldPos)
    {
        _hasMoveTarget = true;
        _moveTarget = worldPos;
    }

    /// <summary>Clear the UI-only planned destination.</summary>
    public void ClearMoveTarget()
    {
        _hasMoveTarget = false;
        _moveTarget = Vector3.zero;
    }

    public void SetPlannedDestination(Vector3 worldPos) => SetMoveTarget(worldPos);
    public void ClearPlannedDestination() => ClearMoveTarget();

    private void Awake()
    {
        AnchorWorldPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (!updateContinuously) return;

        Vector3 computed = ComputeCentroidOfChildren();

        if (!smooth)
        {
            AnchorWorldPosition = computed;
        }
        else
        {
            // Exponential smoothing (stable across framerates)
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, smoothSpeed) * Time.deltaTime);
            AnchorWorldPosition = Vector3.Lerp(AnchorWorldPosition, computed, t);
        }

        if (driveTransformPosition)
        {
            transform.position = AnchorWorldPosition;
        }
    }

    private Vector3 ComputeCentroidOfChildren()
    {
        int count = 0;
        Vector3 sum = Vector3.zero;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;

            if (!includeInactiveChildren && !child.gameObject.activeInHierarchy)
                continue;

            sum += child.position;
            count++;
        }

        // If there are no children, fall back to current anchor (don't jump to origin).
        if (count == 0)
            return AnchorWorldPosition;

        return sum / count;
    }
}
