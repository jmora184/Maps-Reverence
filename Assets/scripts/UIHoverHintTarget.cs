using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to any UI icon (RectTransform). When hovered, shows a tooltip via HoverHintSystem.
/// Tooltip follows the mouse cursor.
/// </summary>
[DisallowMultipleComponent]
public class UIHoverHintTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [TextArea]
    public string message = "Hint";

    [Tooltip("If set, will use this RectTransform as the anchor. Otherwise uses this object's RectTransform.")]
    public RectTransform anchorOverride;

    [Tooltip("Optional: only show hints when this camera is enabled (ex: command view).")]
    public Camera onlyWhenCameraEnabled;

    [Tooltip("If true, hide hint when this object gets disabled.")]
    public bool hideOnDisable = true;

    private RectTransform _selfRT;

    private void Awake()
    {
        _selfRT = GetComponent<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (onlyWhenCameraEnabled != null && !onlyWhenCameraEnabled.enabled)
            return;

        var anchor = anchorOverride != null ? anchorOverride : _selfRT;
        if (anchor == null) return;

        HoverHintSystem.Show(this, anchor, message);
        HoverHintSystem.UpdatePointer(this, eventData.position);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        HoverHintSystem.UpdatePointer(this, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HoverHintSystem.Hide(this);
    }

    private void OnDisable()
    {
        if (!hideOnDisable) return;
        HoverHintSystem.Hide(this);
    }
}
