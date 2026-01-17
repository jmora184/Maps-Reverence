using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Simple hover tooltip for the SlowMove icon.
///
/// Attach this to your SlowMove UI icon (Button/Image/RectTransform).
/// When the mouse hovers the icon, it shows the configured message.
///
/// Default message:
///     Tough Terrain: Movement -10%
///
/// Uses HoverHintSystem + HoverHintUI (rich text supported).
/// </summary>
[DisallowMultipleComponent]
public class SlowMoveHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [Header("Hint")]
    [Tooltip("Additional pixel offset for the tooltip (added on top of HoverHintUI.pixelOffset).")]
    public Vector2 extraPixelOffset = Vector2.zero;

    [TextArea(2, 6)]
    public string message = "Tough Terrain: Movement -10%";

    private RectTransform _anchor;
    private bool _hovering;

    private void Awake()
    {
        _anchor = GetComponent<RectTransform>();
        if (_anchor == null)
            _anchor = GetComponentInChildren<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovering = true;
        Show(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!_hovering) return;
        Show(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        HoverHintSystem.Hide(this);
    }

    private void OnDisable()
    {
        _hovering = false;
        HoverHintSystem.Hide(this);
    }

    private void Show(PointerEventData e)
    {
        if (_anchor == null) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        // Keep the tooltip following the mouse.
        HoverHintSystem.UpdatePointer(this, e.position);
        HoverHintSystem.Show(this, _anchor, message, extraPixelOffset);
    }
}
