using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI controller for a single enemy-team icon.
/// Attach this to a prefab that lives under a Canvas.
/// 
/// Minimum:
/// - A RectTransform (on the prefab root)
/// - An Image (optional)
/// 
/// Optional:
/// - A Text label for member count (UnityEngine.UI.Text)
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class EnemyTeamIconUI : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform rect;
    public Image image;
    public Text countText;

    private Canvas _canvas;

    private void Awake()
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (image == null) image = GetComponent<Image>();
        _canvas = GetComponentInParent<Canvas>();
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    public void SetScreenPosition(Vector2 screenPos)
    {
        // Works best for Screen Space - Overlay canvas.
        // If your canvas is Screen Space - Camera or World Space, we can adjust later.
        if (rect == null) return;
        rect.position = screenPos;
    }

    public void SetCount(int count)
    {
        if (countText == null) return;
        countText.text = count.ToString();
    }
}