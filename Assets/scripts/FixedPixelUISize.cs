using UnityEngine;

/// <summary>
/// Keeps a UI object (typically an icon visual) a consistent on-screen pixel size by counter-scaling
/// against the root Canvas' scaleFactor.
/// 
/// IMPORTANT:
/// - This component is marked ExecuteAlways so it can optionally preview in Edit Mode,
///   but by default it will NOT modify your prefab/scene scale in Edit Mode (to prevent compounding).
/// - Use the context menu "Capture Current As Baseline" after you set the icon's desired base scale.
/// </summary>
[ExecuteAlways]
public class FixedPixelUISize : MonoBehaviour
{
    [Tooltip("Optional: assign if you want to force a specific canvas. If empty, it will auto-find the parent canvas.")]
    public Canvas rootCanvas;

    [Tooltip("1 = keep same visual size as your 1080 reference. 0.8 = 20% smaller, 1.2 = 20% bigger.")]
    [Range(0.1f, 3f)]
    public float sizeMultiplier = 1f;

    [Tooltip("If true, uses the captured baseline local scale as the starting point.")]
    public bool preserveOriginalScale = true;

    [Tooltip("If enabled, applies the counter-scale while NOT playing so you can preview it in the editor.\n" +
             "Leave OFF to prevent the object from getting smaller/larger every time Unity re-enables scripts.")]
    public bool previewInEditMode = false;

    [SerializeField, HideInInspector]
    private Vector3 _baselineLocalScale = Vector3.one;

    [SerializeField, HideInInspector]
    private bool _hasBaseline = false;

    private float _lastCanvasScaleFactor = -1f;

    private void Reset()
    {
        // Called when component is first added or reset in inspector.
        CaptureBaseline();
    }

    private void OnEnable()
    {
        if (!_hasBaseline)
            CaptureBaseline();

        // In edit mode, keep the transform at baseline unless preview is explicitly enabled.
        if (!Application.isPlaying && !previewInEditMode)
        {
            RestoreBaselineScale();
            return;
        }

        ApplyIfNeeded(force: true);
    }

    private void OnValidate()
    {
        // Do NOT recapture baseline automatically here; that caused compounding.
        // Only re-apply scaling for live preview (if enabled) or during play mode.
        if (!Application.isPlaying && !previewInEditMode)
        {
            RestoreBaselineScale();
            return;
        }

        ApplyIfNeeded(force: true);
    }

    private void Update()
    {
        if (!Application.isPlaying && !previewInEditMode)
            return;

        ApplyIfNeeded(force: false);
    }

    [ContextMenu("Capture Current As Baseline")]
    public void CaptureBaseline()
    {
        _baselineLocalScale = transform.localScale;
        _hasBaseline = true;
        _lastCanvasScaleFactor = -1f; // force refresh next Apply
    }

    private void RestoreBaselineScale()
    {
        if (preserveOriginalScale && _hasBaseline)
            transform.localScale = _baselineLocalScale;

        _lastCanvasScaleFactor = -1f;
    }

    private void ApplyIfNeeded(bool force)
    {
        if (!rootCanvas) rootCanvas = GetComponentInParent<Canvas>();
        if (!rootCanvas) return;

        float s = rootCanvas.scaleFactor;
        if (s <= 0.0001f) return;

        if (!force && Mathf.Approximately(s, _lastCanvasScaleFactor))
            return;

        _lastCanvasScaleFactor = s;

        Vector3 baseline = preserveOriginalScale && _hasBaseline ? _baselineLocalScale : Vector3.one;

        // Cancel the parent canvas scaling so this object stays a consistent on-screen size.
        transform.localScale = baseline * (sizeMultiplier / s);
    }
}
