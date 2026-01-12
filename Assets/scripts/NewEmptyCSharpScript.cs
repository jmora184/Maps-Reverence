using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the UI icon (RectTransform) that lives in your minimap UI.
/// It positions the icon based on the minimap camera's viewport projection,
/// and rotates an optional arrow child to match a direction source.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MinimapIconFollow : MonoBehaviour
{
    [Header("Links")]
    public Transform target;          // World ally to follow
    public Camera minimapCamera;      // Top-down minimap camera
    public RectTransform mapRect;     // The minimap UI panel/image rect (NOT the whole canvas)

    [Header("UI Parts")]
    public RectTransform arrowRect;   // Child UI arrow (optional)
    public Vector2 arrowOffset = new Vector2(0f, 18f);

    [Header("Direction")]
    public Transform directionSource;            // If null, uses target
    public bool useZRotationInsteadOfY = false;  // 2D top-down rotation on Z
    public float arrowSpriteAngleOffset = 0f;    // If arrow art points right etc.
    public bool invertArrow = true;              // Usually true for UI rotation

    [Header("Behavior")]
    public bool clampToMap = true;   // Keep icon on map even if target is offscreen
    public bool hideIfBehindCamera = true;

    RectTransform _iconRect;

    void Awake()
    {
        _iconRect = GetComponent<RectTransform>();
    }

    void Start()
    {
        // Make sure this icon is parented to the mapRect so localPosition math is correct.
        // NOTE: Best practice is to parent to a container under the minimap RawImage.
        // This still works if mapRect is that container or the RawImage rect itself.
        if (mapRect != null && transform.parent != mapRect)
            transform.SetParent(mapRect, worldPositionStays: false);
    }

    void LateUpdate()
    {
        if (!target || !minimapCamera || !mapRect) return;

        Vector3 vp = minimapCamera.WorldToViewportPoint(target.position);

        // If behind minimap camera, optionally hide
        if (hideIfBehindCamera && vp.z < 0f)
        {
            SetVisible(false);
            return;
        }

        // Viewport (0..1) -> local position inside mapRect, respecting pivot
        Rect r = mapRect.rect;

        float localX = (vp.x * r.width) - (r.width * mapRect.pivot.x);
        float localY = (vp.y * r.height) - (r.height * mapRect.pivot.y);

        if (clampToMap)
        {
            float minX = -r.width * mapRect.pivot.x;
            float maxX = r.width * (1f - mapRect.pivot.x);
            float minY = -r.height * mapRect.pivot.y;
            float maxY = r.height * (1f - mapRect.pivot.y);

            localX = Mathf.Clamp(localX, minX, maxX);
            localY = Mathf.Clamp(localY, minY, maxY);

            SetVisible(true); // always visible if clamping
        }
        else
        {
            bool onMap = (vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f);
            SetVisible(onMap);
            if (!onMap) return;
        }

        // Use localPosition because it's stable regardless of anchors
        _iconRect.localPosition = new Vector3(localX, localY, 0f);

        // Arrow offset + rotation
        if (arrowRect)
        {
            arrowRect.anchoredPosition = arrowOffset;

            Transform src = directionSource ? directionSource : target;
            float angle = useZRotationInsteadOfY ? src.eulerAngles.z : src.eulerAngles.y;

            float uiAngle = angle + arrowSpriteAngleOffset;
            if (invertArrow) uiAngle = -uiAngle;

            arrowRect.localEulerAngles = new Vector3(0f, 0f, uiAngle);
        }
    }

    void SetVisible(bool on)
    {
        var canvasRenderer = GetComponent<CanvasRenderer>();
        if (canvasRenderer) canvasRenderer.cull = !on;

        for (int i = 0; i < transform.childCount; i++)
        {
            var cr = transform.GetChild(i).GetComponent<CanvasRenderer>();
            if (cr) cr.cull = !on;
        }
    }
}
