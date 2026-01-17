using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HoverCursorIconUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("A full-screen RectTransform to convert screen mouse position into local UI space. Usually your Canvas root.")]
    public RectTransform canvasRoot;

    [Tooltip("Optional. If not set, auto-found in parents.")]
    public Canvas canvas;

    [Tooltip("Optional. If not set, auto-found on this GameObject.")]
    public Image image;

    [Header("Sprites")]
    public Sprite moveSprite;
    public Sprite attackSprite;

    [Header("Position")]
    public Vector2 pixelOffset = new Vector2(24f, 24f);

    private bool visible;
    private bool overEnemy;

    private void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (image == null) image = GetComponent<Image>();

        // This UI element should never block clicks/hover on other UI.
        if (image != null)
        {
            image.raycastTarget = false;
            // Ensure it can't be invisible due to alpha.
            var c = image.color;
            c.a = 1f;
            image.color = c;
        }

        SetVisible(false);
        SetOverEnemy(false);
    }

    public void SetVisible(bool v)
    {
        visible = v;
        if (image != null) image.enabled = v;
    }

    public void SetOverEnemy(bool v)
    {
        overEnemy = v;
        if (image != null)
        {
            var s = overEnemy ? attackSprite : moveSprite;
            if (s != null) image.sprite = s;
        }
    }

    private Camera GetUICamera()
    {
        if (canvas == null) return null;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera;
    }

    private void LateUpdate()
    {
        if (!visible) return;
        if (canvasRoot == null || image == null) return;

        // Keep it enabled and opaque in case something changes it at runtime.
        image.enabled = true;
        var col = image.color;
        if (col.a != 1f) { col.a = 1f; image.color = col; }

        var uiCam = GetUICamera();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, Input.mousePosition, uiCam, out var local))
        {
            ((RectTransform)transform).anchoredPosition = local + pixelOffset;
        }
    }
}
