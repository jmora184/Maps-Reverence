using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI badge that sits next to an enemy icon and shows it is being targeted.
/// Stacking is represented by a count.
/// </summary>
public class AttackTargetIndicatorBadgeUI : MonoBehaviour
{
    [Header("UI")]
    public Image iconImage;
    public TMP_Text countText;
    public CanvasGroup canvasGroup;

    [Header("Visual")]
    [Tooltip("Alpha used when this badge is only a preview (hover).")]
    public float previewAlpha = 0.55f;

    [Tooltip("Alpha used when this badge is committed (attack issued).")]
    public float committedAlpha = 1f;

    private bool isPreview;

    private void Reset()
    {
        iconImage = GetComponentInChildren<Image>(true);
        countText = GetComponentInChildren<TMP_Text>(true);
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void SetPreview(bool preview)
    {
        isPreview = preview;
        ApplyAlpha();
    }

    public void SetCount(int count)
    {
        if (countText != null)
        {
            if (count <= 1)
                countText.text = string.Empty;
            else
                countText.text = count.ToString();
        }

        gameObject.SetActive(count > 0);
        ApplyAlpha();
    }

    private void ApplyAlpha()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = isPreview ? previewAlpha : committedAlpha;
    }
}
