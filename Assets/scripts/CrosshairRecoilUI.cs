using UnityEngine;

public class CrosshairRecoilUI : MonoBehaviour
{
    public static CrosshairRecoilUI Instance { get; private set; }

    [Header("Kick")]
    public Vector2 kickPixels = new Vector2(0f, 6f);     // up a little
    public float kickRandomX = 2.5f;                    // small sideways jitter
    public float kickRandomY = 1.5f;

    [Header("Return")]
    public float returnSpeed = 18f;                      // higher = faster return
    public float snappiness = 35f;

    [Header("Bloom (optional)")]
    public bool useScaleBloom = true;
    public float bloomPerShot = 0.06f;                   // scale add per shot
    public float bloomMax = 0.35f;                       // cap
    public float bloomReturnSpeed = 10f;

    [Header("Visibility")]
    [Tooltip("If assigned, crosshair will auto-hide when this camera zooms (FOV decreases).")]
    public Camera fpsCamera;

    [Tooltip("If true, crosshair hides while zoomed (FOV < baseFov - zoomFovDelta).")]
    public bool autoHideWhenZoomed = true;

    [Tooltip("How much FOV must drop from base before we consider it 'zoomed'.")]
    public float zoomFovDelta = 1.0f;

    [Tooltip("If true, crosshair also hides while in Command Mode (K/L).")]
    public bool hideInCommandMode = true;

    private CanvasGroup canvasGroup;
    private bool isVisible = true;

    private RectTransform rt;
    private Vector2 basePos;
    private Vector2 offset;
    private Vector2 offsetVel;

    private float bloom;
    private float bloomVel;

    // Base crosshair scale (weapon-specific). Final scale = baseScale * (1 + bloom).
    private float baseScale = 1f;

    // For zoom detection
    private float baseFov = -1f;

    private void Awake()
    {
        Instance = this;

        rt = transform as RectTransform;
        basePos = rt.anchoredPosition;
        baseScale = rt.localScale.x;

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (fpsCamera == null)
            fpsCamera = Camera.main;

        BindBaseFov();
        SetVisible(true, force: true);
    }

    /// <summary>
    /// Call this if you swap cameras / change default FOV and want the zoom detector to re-learn the base FOV.
    /// </summary>
    public void BindBaseFov()
    {
        if (fpsCamera != null)
            baseFov = fpsCamera.fieldOfView;
    }

    public void Kick(float intensity = 1f)
    {
        // kick a bit (pixels) + some randomness
        float rx = Random.Range(-kickRandomX, kickRandomX) * intensity;
        float ry = Random.Range(-kickRandomY, kickRandomY) * intensity;

        offset += (kickPixels * intensity) + new Vector2(rx, ry);

        if (useScaleBloom)
        {
            bloom = Mathf.Min(bloom + bloomPerShot * intensity, bloomMax);
        }
    }

    private void Update()
    {
        // ---------- Visibility (ADS / Zoom) ----------
        bool desiredVisible = true;

        if (hideInCommandMode && CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode)
            desiredVisible = false;

        if (desiredVisible && autoHideWhenZoomed && fpsCamera != null)
        {
            if (baseFov < 0f) baseFov = fpsCamera.fieldOfView;
            // Learn the highest (unzoomed) FOV we see so weapon swaps / runtime FOV changes are handled gracefully.
            baseFov = Mathf.Max(baseFov, fpsCamera.fieldOfView);

            // If your ADS zoom is done by lowering camera FOV, this will detect it.
            bool zoomed = fpsCamera.fieldOfView < (baseFov - Mathf.Max(0.01f, zoomFovDelta));
            if (zoomed) desiredVisible = false;
        }

        if (desiredVisible != isVisible)
            SetVisible(desiredVisible);

        // ---------- Recoil / Bloom ----------
        // return position offset toward 0
        offset = Vector2.SmoothDamp(offset, Vector2.zero, ref offsetVel, 1f / Mathf.Max(0.01f, returnSpeed));
        rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, basePos + offset, Time.deltaTime * snappiness);

        // return bloom toward 0
        if (useScaleBloom)
        {
            bloom = Mathf.SmoothDamp(bloom, 0f, ref bloomVel, 1f / Mathf.Max(0.01f, bloomReturnSpeed));
            float s = baseScale * (1f + bloom);
            rt.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>
    /// Shows/hides the crosshair without disabling the GameObject (so references remain valid).
    /// </summary>
    public void SetVisible(bool visible, bool force = false)
    {
        if (!force && isVisible == visible) return;

        isVisible = visible;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        else
        {
            // Fallback
            gameObject.SetActive(visible);
        }
    }

    // Optional: call this if you ever move the crosshair base position at runtime
    public void RebindBase()
    {
        if (rt == null) rt = transform as RectTransform;
        basePos = rt.anchoredPosition;
        baseScale = rt.localScale.x;
    }

    /// <summary>
    /// Sets the base crosshair scale (e.g., smaller for pistols). Bloom is applied on top of this.
    /// </summary>
    public void SetBaseScale(float scale)
    {
        if (rt == null) rt = transform as RectTransform;

        baseScale = Mathf.Max(0.01f, scale);

        // Apply immediately using current bloom so switching feels instant.
        float s = useScaleBloom ? baseScale * (1f + bloom) : baseScale;
        rt.localScale = new Vector3(s, s, 1f);
    }
}