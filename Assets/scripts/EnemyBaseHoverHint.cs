using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Hover hint for an enemy base icon.
/// Attach this to the enemy base UI icon (Image/Button/RectTransform).
/// When hovered, it shows: "Enemy Base"
/// Uses the existing HoverHintSystem + HoverHintUI.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBaseHoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [Header("Hint")]
    [Tooltip("Additional pixel offset for the tooltip (added on top of HoverHintUI.pixelOffset).")]
    public Vector2 extraPixelOffset = Vector2.zero;

    [TextArea(1, 3)]
    public string message = "Enemy Base";

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

        HoverHintSystem.UpdatePointer(this, e.position);
        HoverHintSystem.Show(this, _anchor, message, extraPixelOffset);
    }
}
