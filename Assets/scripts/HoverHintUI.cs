using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tooltip-style UI that fades in/out and positions next to the mouse while hovering.
/// Auto-sizes its panel based on the text content (with min/max bounds).
/// Fix: keeps the Text RectTransform centered inside the panel so it never drifts outside.
/// 
/// Supports an optional per-call extra pixel offset (added on top of pixelOffset).
/// </summary>
public class HoverHintUI : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Panel RectTransform (the tooltip root). Usually THIS RectTransform.")]
    public RectTransform root;

    [Tooltip("CanvasGroup on the root panel.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Legacy Text on the tooltip.")]
    public Text messageText;

    [Header("Animation")]
    public float fadeIn = 0.10f;
    public float fadeOut = 0.10f;

    [Header("Position")]
    [Tooltip("If true, tooltip follows the mouse cursor.")]
    public bool followMouse = true;

    [Tooltip("Base offset in pixels from the mouse (or anchor) position.")]
    public Vector2 pixelOffset = new Vector2(18f, -18f);

    [Tooltip("Clamp tooltip inside the parent rect.")]
    public bool clampToParent = true;

    [Tooltip("Padding used when clamping to parent.")]
    public Vector2 clampPadding = new Vector2(10f, 10f);

    [Header("Auto Size")]
    [Tooltip("If true, panel resizes to fit the text with padding and bounds.")]
    public bool autoSizeToText = true;

    [Tooltip("Total padding added to text (x = left+right, y = top+bottom).")]
    public Vector2 panelPadding = new Vector2(24f, 16f);

    public float minWidth = 140f;
    public float maxWidth = 340f;
    public float minHeight = 44f;
    public float maxHeight = 220f;

    [Tooltip("Max width used for wrapping text before sizing the panel.")]
    public float maxTextWidth = 300f;

    private RectTransform _anchor;
    private Coroutine _routine;
    private int _token;
    private bool _visible;
    // Per-call offset (set by HoverHintSystem.Show(..., extraOffset))
    private Vector2 _extraPixelOffset = Vector2.zero;

    private Canvas _canvas;

    // (no duplicate field)

    private void Awake()
    {
        if (root == null) root = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (messageText == null) messageText = GetComponentInChildren<Text>(true);

        // We rely on rich text tags (e.g., <color=...>) for conditional hints.
        if (messageText != null) messageText.supportRichText = true;

        _canvas = GetComponentInParent<Canvas>();

        // Tooltip should never block clicks/hover on other UI.
        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;

        HideImmediate();
    }

    public bool IsVisible() => _visible;

    /// <summary>
    /// Show tooltip. Anchor is used to auto-hide if the hovered icon disappears.
    /// </summary>
    public void Show(RectTransform anchor, string message)
    {
        Show(anchor, message, Vector2.zero);
    }

    /// <summary>
    /// Show tooltip with an extra pixel offset (added on top of pixelOffset).
    /// </summary>
    public void Show(RectTransform anchor, string message, Vector2 extraPixelOffset)
    {
        if (anchor == null) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        _anchor = anchor;
        _extraPixelOffset = extraPixelOffset;

        if (messageText != null)
        {
            messageText.text = message;
            UpdateSizeToContent();
        }

        _token++;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        // Start hidden then fade in (prevents flash)
        ApplyHiddenState();

        _routine = StartCoroutine(FadeTo(_token, 1f, fadeIn));
    }

    public void Hide()
    {
        _token++;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        if (!_visible)
        {
            ApplyHiddenState();
            return;
        }

        _routine = StartCoroutine(FadeOutThenHide(_token));
    }

    public void HideImmediate()
    {
        _token++;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        ApplyHiddenState();
    }

    private IEnumerator FadeOutThenHide(int myToken)
    {
        yield return FadeTo(myToken, 0f, fadeOut);
        if (myToken != _token) yield break;
        ApplyHiddenState();
        _routine = null;
    }

    private IEnumerator FadeTo(int myToken, float targetAlpha, float duration)
    {
        if (root != null && !root.gameObject.activeSelf) root.gameObject.SetActive(true);

        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 0f;
        float t = 0f;

        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            yield break;
        }

        while (t < duration)
        {
            if (myToken != _token) yield break;

            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);

            ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, u));
            yield return null;
        }

        ApplyAlpha(targetAlpha);
    }

    private void ApplyAlpha(float a)
    {
        if (canvasGroup != null) canvasGroup.alpha = a;
        _visible = a > 0.001f;
    }

    private void ApplyHiddenState()
    {
        if (root != null)
        {
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }

        ApplyAlpha(0f);
        _anchor = null;
        _extraPixelOffset = Vector2.zero;
    }

    private void LateUpdate()
    {
        if (!_visible) return;
        if (root == null) return;

        // If anchor is disabled/destroyed, hide (prevents stuck tooltip).
        if (_anchor != null && !_anchor.gameObject.activeInHierarchy)
        {
            HideImmediate();
            return;
        }

        var parent = root.parent as RectTransform;
        if (parent == null) return;

        Vector2 pos;
        Vector2 totalOffset = pixelOffset + _extraPixelOffset;

        if (followMouse && HoverHintSystem.Instance != null && HoverHintSystem.Instance.HasPointer)
        {
            pos = ScreenToParentLocal(parent, HoverHintSystem.Instance.PointerScreenPos) + totalOffset;
        }
        else if (_anchor != null)
        {
            // Fallback to anchor center
            Vector3 anchorWorld = _anchor.TransformPoint(_anchor.rect.center);
            Vector3 local3 = parent.InverseTransformPoint(anchorWorld);
            pos = new Vector2(local3.x, local3.y) + totalOffset;
        }
        else
        {
            return;
        }

        if (clampToParent)
            pos = ClampTo(parent, pos);

        root.anchoredPosition = pos;

        // Render on top
        root.SetAsLastSibling();
    }

    private void UpdateSizeToContent()
    {
        if (!autoSizeToText) return;
        if (root == null || messageText == null) return;

        RectTransform textRT = messageText.rectTransform;
        if (textRT == null) return;

        // Ensure wrapping behaves predictably
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        messageText.verticalOverflow = VerticalWrapMode.Overflow;

        // Force Text to be centered inside the panel (prevents drifting outside)
        textRT.anchorMin = new Vector2(0.5f, 0.5f);
        textRT.anchorMax = new Vector2(0.5f, 0.5f);
        textRT.pivot = new Vector2(0.5f, 0.5f);
        textRT.anchoredPosition = Vector2.zero;
        textRT.localScale = Vector3.one;

        // Text wraps at this width (also respects max panel width minus padding)
        float wrapWidth = Mathf.Min(maxTextWidth, Mathf.Max(10f, maxWidth - panelPadding.x));
        textRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, wrapWidth);

        // Force layout update so preferredWidth/Height are correct this frame
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(textRT);

        float textW = Mathf.Min(messageText.preferredWidth, wrapWidth);
        float textH = messageText.preferredHeight;

        float panelW = Mathf.Clamp(textW + panelPadding.x, minWidth, maxWidth);
        float panelH = Mathf.Clamp(textH + panelPadding.y, minHeight, maxHeight);

        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelW);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelH);

        // Now that we know the panel size, clamp the Text box to live INSIDE it.
        float innerW = Mathf.Max(10f, panelW - panelPadding.x);
        float innerH = Mathf.Max(10f, panelH - panelPadding.y);

        textRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerW);
        textRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, innerH);
        textRT.anchoredPosition = Vector2.zero;
    }

    private Vector2 ScreenToParentLocal(RectTransform parent, Vector2 screen)
    {
        Camera cam = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = _canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out Vector2 local);
        return local;
    }

    private Vector2 ClampTo(RectTransform parent, Vector2 anchoredPos)
    {
        if (root == null) return anchoredPos;

        Vector2 parentMin = parent.rect.min + clampPadding;
        Vector2 parentMax = parent.rect.max - clampPadding;

        Vector2 size = root.rect.size;

        float halfW = size.x * 0.5f;
        float halfH = size.y * 0.5f;

        float x = Mathf.Clamp(anchoredPos.x, parentMin.x + halfW, parentMax.x - halfW);
        float y = Mathf.Clamp(anchoredPos.y, parentMin.y + halfH, parentMax.y - halfH);

        return new Vector2(x, y);
    }
}
