using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI-only direction arrow for an enemy team icon.
/// - Orbits an arrow around the icon toward the team's intended direction.
/// - Intended direction comes from:
///    1) EnemyTeamMoveTargetProvider on the team root (recommended), OR
///    2) Reflection-based read of EncounterTeamAnchor's optional MoveTarget/PlannedDestination members, OR
///    3) Velocity fallback (anchor position delta).
///
/// This does NOT drive enemy movement.
/// </summary>
[DisallowMultipleComponent]
public class EnemyTeamDirectionArrowUI : MonoBehaviour
{
    [Header("UI Refs")]
    [Tooltip("RectTransform that the arrow will orbit around. Usually the icon root RectTransform.")]
    public RectTransform orbitCenter;

    [Tooltip("The RectTransform of the arrow image (child).")]
    public RectTransform arrowRect;

    [Tooltip("Optional: image component for enable/disable.")]
    public Graphic arrowGraphic;

    [Header("Behavior")]
    [Tooltip("Distance from icon center (pixels).")]
    public float orbitRadiusPixels = 26f;

    [Tooltip("If true, arrow rotates to point outward (tangent not used).")]
    public bool rotateToDirection = true;

    [Tooltip("If true, hides arrow when there is no direction (no target + not moving).")]
    public bool hideWhenNoDirection = true;

    [Tooltip("If true, uses movement delta as a fallback direction when no explicit target exists.")]
    public bool useVelocityFallback = true;

    [Tooltip("Minimum movement speed (world units/sec) to show velocity fallback arrow.")]
    public float minWorldSpeedForArrow = 0.05f;

    [Tooltip("If your arrow sprite points RIGHT in its default orientation, leave this ON. If it points UP, turn this OFF.")]
    public bool spritePointsRight = true;

    [Header("World / Binding")]
    [Tooltip("Camera used to convert world direction to screen direction. Usually the command camera.")]
    public Camera worldCamera;

    [Tooltip("EncounterTeamAnchor on the enemy team root (optional but recommended).")]
    public EncounterTeamAnchor teamAnchor;

    [Tooltip("EnemyTeamMoveTargetProvider on the enemy team root (optional).")]
    public EnemyTeamMoveTargetProvider moveTargetProvider;

    private Vector3 _prevAnchorPos;
    private bool _hasPrev;

    // Cached reflection info for optional EncounterTeamAnchor members (safe even if absent)
    private PropertyInfo _piHasMoveTarget;
    private PropertyInfo _piMoveTarget;
    private PropertyInfo _piHasPlannedDestination;
    private PropertyInfo _piPlannedDestination;
    private bool _reflectionCached;

    private void Reset()
    {
        TryAutoWire();
    }

    private void Awake()
    {
        TryAutoWire();
    }

    public void Bind(EncounterTeamAnchor anchor, Camera cam)
    {
        teamAnchor = anchor;
        worldCamera = cam;
        if (teamAnchor != null && moveTargetProvider == null)
            moveTargetProvider = teamAnchor.GetComponent<EnemyTeamMoveTargetProvider>();

        _hasPrev = false;
        CacheReflection();
    }

    public void TryAutoWire()
    {
        if (orbitCenter == null)
            orbitCenter = GetComponent<RectTransform>();

        if (arrowRect == null)
        {
            // Try common child names
            Transform t = transform.Find("ArrowImage");
            if (t == null) t = transform.Find("Arrow");
            if (t == null)
            {
                // fallback: first child with a Graphic
                var g = GetComponentInChildren<Graphic>(true);
                if (g != null) t = g.transform;
            }
            if (t != null) arrowRect = t as RectTransform;
        }

        if (arrowGraphic == null && arrowRect != null)
            arrowGraphic = arrowRect.GetComponent<Graphic>();

        if (worldCamera == null)
        {
            // If on a Screen Space - Camera canvas, prefer that camera
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                worldCamera = canvas.worldCamera;

            if (worldCamera == null)
                worldCamera = Camera.main;
        }

        CacheReflection();
    }

    private void CacheReflection()
    {
        if (_reflectionCached || teamAnchor == null) return;

        Type t = teamAnchor.GetType();
        _piHasMoveTarget = t.GetProperty("HasMoveTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _piMoveTarget = t.GetProperty("MoveTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _piHasPlannedDestination = t.GetProperty("HasPlannedDestination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _piPlannedDestination = t.GetProperty("PlannedDestination", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _reflectionCached = true;
    }

    private void LateUpdate()
    {
        if (orbitCenter == null || arrowRect == null)
            return;

        if (teamAnchor == null)
        {
            // Try auto bind from a bridge or parent context, if any
            // (Icon systems should call Bind().)
            return;
        }

        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;

        Vector3 anchorPos = teamAnchor.AnchorWorldPosition;

        bool hasDirection = TryGetExplicitTargetDirection(anchorPos, out Vector3 dirWorld);

        if (!hasDirection && useVelocityFallback)
            hasDirection = TryGetVelocityDirection(anchorPos, out dirWorld);

        if (!hasDirection)
        {
            if (hideWhenNoDirection) SetArrowVisible(false);
            return;
        }

        SetArrowVisible(true);

        // Convert world direction to screen direction using camera projection
        Vector3 a = worldCamera.WorldToScreenPoint(anchorPos);
        Vector3 b = worldCamera.WorldToScreenPoint(anchorPos + dirWorld.normalized);

        Vector2 dirScreen = (Vector2)(b - a);
        if (dirScreen.sqrMagnitude < 0.0001f)
        {
            if (hideWhenNoDirection) SetArrowVisible(false);
            return;
        }

        dirScreen.Normalize();

        // Orbit placement in UI pixels
        arrowRect.anchoredPosition = dirScreen * orbitRadiusPixels;

        if (rotateToDirection)
        {
            float ang = Mathf.Atan2(dirScreen.y, dirScreen.x) * Mathf.Rad2Deg;

            // If sprite points RIGHT, angle is correct. If sprite points UP, offset by -90.
            float rotZ = spritePointsRight ? ang : (ang - 90f);
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        }
    }

    private bool TryGetExplicitTargetDirection(Vector3 anchorPos, out Vector3 dirWorld)
    {
        dirWorld = default;

        // Preferred: provider
        if (moveTargetProvider != null && moveTargetProvider.HasMoveTarget)
        {
            Vector3 t = moveTargetProvider.MoveTarget;
            dirWorld = t - anchorPos;
            return dirWorld.sqrMagnitude > 0.0001f;
        }

        // Fallback: reflect optional members on EncounterTeamAnchor
        CacheReflection();
        if (teamAnchor != null)
        {
            try
            {
                bool has = false;
                Vector3 target = default;

                if (_piHasMoveTarget != null && _piMoveTarget != null)
                {
                    has = (bool)_piHasMoveTarget.GetValue(teamAnchor);
                    if (has) target = (Vector3)_piMoveTarget.GetValue(teamAnchor);
                }
                else if (_piHasPlannedDestination != null && _piPlannedDestination != null)
                {
                    has = (bool)_piHasPlannedDestination.GetValue(teamAnchor);
                    if (has) target = (Vector3)_piPlannedDestination.GetValue(teamAnchor);
                }

                if (has)
                {
                    dirWorld = target - anchorPos;
                    return dirWorld.sqrMagnitude > 0.0001f;
                }
            }
            catch { /* ignore */ }
        }

        return false;
    }

    private bool TryGetVelocityDirection(Vector3 anchorPos, out Vector3 dirWorld)
    {
        dirWorld = default;

        if (!_hasPrev)
        {
            _prevAnchorPos = anchorPos;
            _hasPrev = true;
            return false;
        }

        Vector3 delta = anchorPos - _prevAnchorPos;
        _prevAnchorPos = anchorPos;

        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float speed = delta.magnitude / dt;

        if (speed < minWorldSpeedForArrow)
            return false;

        dirWorld = delta;
        return dirWorld.sqrMagnitude > 0.0001f;
    }

    private void SetArrowVisible(bool visible)
    {
        if (arrowGraphic != null)
        {
            if (arrowGraphic.enabled != visible)
                arrowGraphic.enabled = visible;
        }
        else
        {
            if (arrowRect.gameObject.activeSelf != visible)
                arrowRect.gameObject.SetActive(visible);
        }
    }
}
