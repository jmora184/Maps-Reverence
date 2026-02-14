// MinimapIconFollowWorld.cs
// Attach to a UI icon (RectTransform) under your minimap panel.
//
// Goal: keep UI icons "stuck" to world/map positions while your CommandCamera pans/zooms.
//
// Works in TWO modes:
// 1) Anchor Transform Mode (recommended):
//    - Create an empty GameObject in the world at the spot you want (e.g., MudZoneAnchor)
//    - Assign it to targetWorld.
// 2) Fixed World Position Mode (for purely manual setup):
//    - Turn on useFixedWorldPosition and set fixedWorldPosition (X,Z). Optional: sample terrain height.
//
// The script maps: World -> CommandCamera viewport -> minimapRect local coordinates,
// and clamps the icon inside the minimap panel.
//
// IMPORTANT (fix):
// Do NOT SetActive(false) to "hide" the icon when offscreen. If the GameObject is inactive,
// LateUpdate will stop running and the icon can never re-enable itself.
// Instead we hide via CanvasGroup alpha.

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class MinimapIconFollowWorld : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Orthographic camera that renders the command/minimap view (your CommandCamera).")]
    public Camera commandCamera;

    [Tooltip("RectTransform that defines the minimap area (usually your RawImage panel).")]
    public RectTransform minimapRect;

    [Header("World Source")]
    [Tooltip("Recommended: world anchor this icon should follow (empty GameObject at the zone).")]
    public Transform targetWorld;

    [Tooltip("Use a fixed world position instead of a target transform (for manual UI icons).")]
    public bool useFixedWorldPosition = false;

    [Tooltip("World position to use when useFixedWorldPosition is enabled.")]
    public Vector3 fixedWorldPosition;

    [Tooltip("Optional: terrain used to sample height when using fixedWorldPosition.")]
    public Terrain terrainForHeight;

    [Header("Offsets & Clamping")]
    [Tooltip("Offset applied in world space (X,Y,Z).")]
    public Vector3 worldOffset = Vector3.zero;

    [Tooltip("Offset applied in UI local space (pixels).")]
    public Vector2 uiOffset = Vector2.zero;

    [Tooltip("Padding from minimap edges (pixels) when clamping.")]
    public float clampPaddingPixels = 6f;

    [Tooltip("If true, hides icon when target is outside camera view (before clamping).")]
    public bool hideWhenOffscreen = false;

    [Tooltip("When hidden, also disables raycast blocking for this icon.")]
    public bool disableRaycastWhenHidden = true;

    [Header("Rotation (Optional)")]
    [Tooltip("Rotate icon to match the yaw of the target (world Y rotation).")]
    public bool matchTargetYaw = false;

    [Tooltip("Extra yaw offset (degrees).")]
    public float yawOffsetDegrees = 0f;

    [Header("Scale (Optional)")]
    [Tooltip("If enabled, scales icon based on command camera zoom (ortho size).")]
    public bool scaleWithZoom = false;

    [Tooltip("Ortho size where icon scale = 1.0 (use your common zoom, e.g., 700).")]
    public float referenceOrthoSize = 700f;

    [Tooltip("Min/max scale when scaleWithZoom is enabled.")]
    public Vector2 zoomScaleMinMax = new Vector2(0.7f, 1.3f);

    private RectTransform _rt;
    private Vector3 _baseLocalScale;

    // Used for hiding without disabling the GameObject
    private CanvasGroup _cg;
    private bool _isHidden;

    void Reset()
    {
        _rt = GetComponent<RectTransform>();
    }

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _baseLocalScale = transform.localScale;

        // Ensure we have a CanvasGroup for safe hide/show.
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        ApplyHidden(false);
    }

    void LateUpdate()
    {
        if (_rt == null) return;
        if (commandCamera == null || minimapRect == null) return;

        Vector3 worldPos = GetWorldPosition();
        // If we don't have a valid world source, do nothing
        if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.z)) return;

        // Convert world -> viewport (0..1)
        Vector3 vp = commandCamera.WorldToViewportPoint(worldPos);

        bool behind = vp.z < 0f;
        bool offscreen = behind || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;

        if (hideWhenOffscreen)
        {
            ApplyHidden(offscreen);
            if (offscreen) return;
        }
        else
        {
            ApplyHidden(false);
        }

        // Map viewport -> minimap local coordinates
        Rect r = minimapRect.rect;

        float xLocal = (vp.x - 0.5f) * r.width;
        float yLocal = (vp.y - 0.5f) * r.height;

        Vector2 local = new Vector2(xLocal, yLocal) + uiOffset;

        // Clamp to minimap rect
        float pad = Mathf.Max(0f, clampPaddingPixels);
        float minX = r.xMin + pad;
        float maxX = r.xMax - pad;
        float minY = r.yMin + pad;
        float maxY = r.yMax - pad;

        local.x = Mathf.Clamp(local.x, minX, maxX);
        local.y = Mathf.Clamp(local.y, minY, maxY);

        _rt.anchoredPosition = local;

        // Rotation
        if (matchTargetYaw)
        {
            float yaw = (useFixedWorldPosition ? 0f : (targetWorld != null ? targetWorld.eulerAngles.y : 0f)) + yawOffsetDegrees;
            // UI Z rotation is clockwise negative for world yaw
            _rt.localRotation = Quaternion.Euler(0f, 0f, -yaw);
        }

        // Scale with zoom
        if (scaleWithZoom)
        {
            float ortho = Mathf.Max(1f, commandCamera.orthographicSize);
            float refSize = Mathf.Max(1f, referenceOrthoSize);
            // As you zoom in (smaller ortho), icons can grow slightly (or shrink—tune via min/max)
            float t = refSize / ortho; // >1 when zoomed in
            float s = Mathf.Clamp(t, zoomScaleMinMax.x, zoomScaleMinMax.y);
            transform.localScale = _baseLocalScale * s;
        }
        else
        {
            transform.localScale = _baseLocalScale;
        }
    }

    private void ApplyHidden(bool hidden)
    {
        if (_cg == null) return;
        if (_isHidden == hidden) return;

        _isHidden = hidden;
        _cg.alpha = hidden ? 0f : 1f;

        if (disableRaycastWhenHidden)
        {
            _cg.blocksRaycasts = !hidden;
            _cg.interactable = !hidden;
        }
    }

    private Vector3 GetWorldPosition()
    {
        if (!useFixedWorldPosition)
        {
            if (targetWorld == null) return new Vector3(float.NaN, float.NaN, float.NaN);
            return targetWorld.position + worldOffset;
        }

        Vector3 pos = fixedWorldPosition + worldOffset;

        if (terrainForHeight != null)
        {
            float y = terrainForHeight.SampleHeight(pos) + terrainForHeight.transform.position.y;
            pos.y = y;
        }

        return pos;
    }
}
