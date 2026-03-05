using UnityEngine;

/// <summary>
/// Forces the Enemy Team "star/skull" UI image to a manual scale, even if other scripts
/// keep resetting parent scale to (1,1,1).
///
/// Attach this to the spawned EnemyTeamIcon root (the one that has OrbitalAnchor/StarImage).
/// Assign the StarImage RectTransform in the inspector (or leave blank to auto-find).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public class EnemyTeamIconManualScale : MonoBehaviour
{
    [Header("Assign the StarImage RectTransform here (OrbitalAnchor/StarImage)")]
    public RectTransform starImage;

    [Header("Manual Scale")]
    [Min(0.01f)]
    public float scale = 1f;

    [Header("Optional: also scale the ArrowImage")]
    public bool alsoScaleArrowImage = false;
    public RectTransform arrowImage;

    [Header("Advanced")]
    [Tooltip("If another script overwrites localScale, enable this to drive sizeDelta instead.")]
    public bool useSizeDelta = false;

    [Tooltip("Base sizeDelta used when 'Use SizeDelta' is enabled. If left at 0,0 it will be captured from StarImage at runtime.")]
    public Vector2 baseStarSizeDelta = Vector2.zero;

    [Tooltip("Base sizeDelta for ArrowImage when 'Use SizeDelta' is enabled. If left at 0,0 it will be captured from ArrowImage at runtime.")]
    public Vector2 baseArrowSizeDelta = Vector2.zero;

    private bool _capturedBase;

    void Reset()
    {
        AutoFindRefs();
    }

    void Awake()
    {
        if (!starImage || (alsoScaleArrowImage && !arrowImage))
            AutoFindRefs();
    }

    void AutoFindRefs()
    {
        if (!starImage)
        {
            var t = transform.Find("OrbitalAnchor/StarImage");
            if (t) starImage = t.GetComponent<RectTransform>();
        }

        if (alsoScaleArrowImage && !arrowImage)
        {
            var t = transform.Find("OrbitalAnchor/ArrowImage");
            if (t) arrowImage = t.GetComponent<RectTransform>();
        }
    }

    // LateUpdate so we run AFTER other icon scripts (which often run Update).
    void LateUpdate()
    {
        if (!starImage) return;

        if (useSizeDelta)
        {
            if (!_capturedBase)
            {
                if (baseStarSizeDelta == Vector2.zero)
                    baseStarSizeDelta = starImage.sizeDelta;

                if (alsoScaleArrowImage && arrowImage && baseArrowSizeDelta == Vector2.zero)
                    baseArrowSizeDelta = arrowImage.sizeDelta;

                _capturedBase = true;
            }

            starImage.sizeDelta = baseStarSizeDelta * scale;

            if (alsoScaleArrowImage && arrowImage)
                arrowImage.sizeDelta = baseArrowSizeDelta * scale;
        }
        else
        {
            starImage.localScale = new Vector3(scale, scale, 1f);

            if (alsoScaleArrowImage && arrowImage)
                arrowImage.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
