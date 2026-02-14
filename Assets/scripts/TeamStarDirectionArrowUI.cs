using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orbiting direction arrow for the Ally Team Star UI (Option B).
/// Expects Option B storage on Team (SetMoveTarget/ClearMoveTarget OR SetPlannedDestination/ClearPlannedDestination).
///
/// - Orbits clockwise around the star (position-only orbit).
/// - Points toward the team's stored move target.
/// - Slows/pauses orbit near arrival.
/// - Hides when no move target or not meaningful movement.
/// </summary>
public class TeamStarDirectionArrowUI : MonoBehaviour
{
    [Header("Wiring (optional - auto found if left empty)")]
    public RectTransform orbitAnchor;
    public RectTransform arrowRect;
    public Image arrowImage;

    [Header("Orbit")]
    public float orbitRadiusPixels = 34f;
    public float orbitSpeedDegPerSec = 240f; // clockwise

    [Header("Visibility")]
    public float arriveHideDistanceMeters = 3f;
    public float slowOrbitWithinMeters = 7f;
    public float minScreenDeltaPixels = 2f;

    [Header("Rotation")]
    [Tooltip("If your sprite's 'forward' isn't pointing right, set an offset here. Right-facing sprite = 0.")]
    public float spriteAngleOffsetDegrees = 0f;

    private float _orbitAngleDeg;

    // Bound refs (preferred)
    private Team _team;
    private Camera _commandCam;

    // Fallback refs (if Bind isn't called)
    private TeamIconUI _teamIcon;
    private CommandOverlayUI _overlay;

    /// <summary>
    /// Called by CommandOverlayUI when the Team Star icon is spawned/bound.
    /// </summary>
    public void Bind(Team team, Camera uiCam)
    {
        _team = team;
        _commandCam = uiCam;

        // avoid 1-frame prefab flash
        SetArrowVisible(false);
    }

    private void Awake()
    {
        AutoWire();

        _teamIcon = GetComponent<TeamIconUI>();
        if (_teamIcon == null) _teamIcon = GetComponentInParent<TeamIconUI>();

        // Start hidden to avoid the 1-frame flash on spawn
        SetArrowVisible(false);
    }

    private void AutoWire()
    {
        if (orbitAnchor == null)
        {
            var t = transform.Find("OrbitAnchor");
            if (t != null) orbitAnchor = t.GetComponent<RectTransform>();
        }

        if (arrowRect == null && orbitAnchor != null)
        {
            var t = orbitAnchor.Find("ArrowImage");
            if (t != null) arrowRect = t.GetComponent<RectTransform>();
        }

        if (arrowImage == null && arrowRect != null)
            arrowImage = arrowRect.GetComponent<Image>();
    }

    private void LateUpdate()
    {
        // Lazy resolve camera if not bound
        if (_commandCam == null)
        {
            if (_overlay == null) _overlay = FindFirstObjectByType<CommandOverlayUI>();
            if (_overlay != null) _commandCam = _overlay.commandCam;
        }

        // Resolve team if not bound
        if (_team == null)
        {
            if (_teamIcon == null) _teamIcon = GetComponentInParent<TeamIconUI>();
            if (_teamIcon != null) _team = _teamIcon.Team;
        }

        if (_team == null || _commandCam == null)
        {
            SetArrowVisible(false);
            return;
        }

        // Support both naming conventions on Team
        bool hasTarget = _team.HasMoveTarget || _team.HasPlannedDestination;
        if (!hasTarget)
        {
            SetArrowVisible(false);
            return;
        }

        Vector3 centroid = _team.GetCentroid();
        Vector3 dest = _team.HasMoveTarget ? _team.MoveTarget : _team.PlannedDestination;

        // Distance in world for arrival / orbit slowdown.
        float worldDist = Vector3.Distance(new Vector3(centroid.x, 0f, centroid.z), new Vector3(dest.x, 0f, dest.z));

        // Project to screen space to match the minimap/command camera orientation automatically.
        Vector3 cS = _commandCam.WorldToScreenPoint(centroid);
        Vector3 dS = _commandCam.WorldToScreenPoint(dest);

        // If behind camera, hide (rare in top-down but safe)
        if (cS.z < 0.01f || dS.z < 0.01f)
        {
            SetArrowVisible(false);
            return;
        }

        Vector2 delta = new Vector2(dS.x - cS.x, dS.y - cS.y);

        // If basically not moving (or same point), hide to reduce clutter.
        if (worldDist <= arriveHideDistanceMeters || delta.magnitude <= minScreenDeltaPixels)
        {
            // Pause orbit near arrival (your preference)
            UpdateOrbit(0f);
            SetArrowVisible(false);
            return;
        }

        SetArrowVisible(true);

        // Rotate arrow to face destination
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + spriteAngleOffsetDegrees;
        if (arrowRect != null)
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);

        // Orbit speed scale: slow down as we approach arrival range
        float speedScale = 1f;
        if (worldDist < slowOrbitWithinMeters)
        {
            speedScale = Mathf.InverseLerp(arriveHideDistanceMeters, slowOrbitWithinMeters, worldDist);
            speedScale = Mathf.Clamp01(speedScale);
        }

        UpdateOrbit(orbitSpeedDegPerSec * speedScale);
    }

    private void UpdateOrbit(float speedDegPerSec)
    {
        if (orbitAnchor == null || arrowRect == null) return;

        _orbitAngleDeg += speedDegPerSec * Time.deltaTime;

        float rad = _orbitAngleDeg * Mathf.Deg2Rad;
        float x = Mathf.Cos(rad) * orbitRadiusPixels;
        float y = Mathf.Sin(rad) * orbitRadiusPixels;

        arrowRect.anchoredPosition = new Vector2(x, y);
    }

    private void SetArrowVisible(bool visible)
    {
        if (arrowRect == null) return;

        if (arrowRect.gameObject.activeSelf != visible)
            arrowRect.gameObject.SetActive(visible);
    }
}
