using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DIAGNOSTIC / OVERRIDE SCRIPT
/// Put this on the SAME GameObject as your crosshair UI Image.
/// It will FORCE the crosshair to a specific scale (and optional sizeDelta) every LateUpdate,
/// so you can prove whether something else is overriding your changes.
///
/// Use:
/// 1) Add to Crosshair Image
/// 2) Set forceScale = true and scale = 0.4 (for smaller)
/// 3) Press Play. If it still doesn't change, you're not on the right Image.
/// 4) If it DOES change, some other script/Animator/Layout is overwriting it.
/// </summary>
[DisallowMultipleComponent]
public class CrosshairForceSize : MonoBehaviour
{
    [Header("Target")]
    public Image targetImage;

    [Header("Force Scale")]
    public bool forceScale = true;
    [Range(0.05f, 5f)] public float scale = 0.4f;

    [Header("Optional: Force SizeDelta (useful if layouts fight scale)")]
    public bool forceSizeDelta = false;
    public Vector2 sizeDelta = new Vector2(24f, 24f);

    [Header("Logging")]
    public bool logOnceOnStart = true;

    private RectTransform _rt;
    private bool _logged;

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage != null)
            _rt = targetImage.rectTransform;
        else
            _rt = GetComponent<RectTransform>();
    }

    private void Start()
    {
        if (logOnceOnStart && !_logged)
        {
            _logged = true;
            Debug.Log($"[CrosshairForceSize] Target='{(targetImage ? targetImage.name : gameObject.name)}' scale={scale} forceScale={forceScale} forceSizeDelta={forceSizeDelta} sizeDelta={sizeDelta}", this);
        }
    }

    private void LateUpdate()
    {
        if (_rt == null) return;

        if (forceScale)
            _rt.localScale = Vector3.one * scale;

        if (forceSizeDelta)
            _rt.sizeDelta = sizeDelta;
    }
}
