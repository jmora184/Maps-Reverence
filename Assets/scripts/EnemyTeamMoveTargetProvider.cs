using UnityEngine;

/// <summary>
/// Optional helper component to store a UI-only move target for an enemy team.
/// This does NOT drive movement; it's purely for UI (direction arrows, etc).
///
/// Your AI scripts can call:
///   provider.SetMoveTarget(worldPos);
///   provider.ClearMoveTarget();
/// </summary>
public class EnemyTeamMoveTargetProvider : MonoBehaviour
{
    private bool _hasMoveTarget;
    private Vector3 _moveTarget;

    /// <summary>True if a UI-only move target has been set.</summary>
    public bool HasMoveTarget => _hasMoveTarget;

    /// <summary>UI-only move target in WORLD space.</summary>
    public Vector3 MoveTarget => _moveTarget;

    /// <summary>Set a UI-only move target in WORLD space.</summary>
    public void SetMoveTarget(Vector3 worldPos)
    {
        _hasMoveTarget = true;
        _moveTarget = worldPos;
    }

    /// <summary>Clear the UI-only move target.</summary>
    public void ClearMoveTarget()
    {
        _hasMoveTarget = false;
        _moveTarget = default;
    }
}
