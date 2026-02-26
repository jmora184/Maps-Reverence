using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on your Crosshair UI root (the same GameObject that holds your crosshair Image).
/// Assign the Image you want to swap (usually the main crosshair image).
/// Weapons can call SetCrosshair() to change the sprite at runtime.
/// </summary>
public class CrosshairImageManager : MonoBehaviour
{
    public static CrosshairImageManager Instance { get; private set; }

    [Header("Target UI")]
    [Tooltip("The UI Image that displays the crosshair sprite.")]
    public Image crosshairImage;

    [Tooltip("Optional: if your crosshair uses a separate bloom/glow image, assign it here.")]
    public Image bloomImage;

    [Header("Optional Defaults")]
    public Sprite defaultCrosshair;
    [Range(0.1f, 4f)] public float defaultScale = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (crosshairImage == null)
            crosshairImage = GetComponentInChildren<Image>(true);

        if (defaultCrosshair == null && crosshairImage != null)
            defaultCrosshair = crosshairImage.sprite;
    }

    public void SetCrosshair(Sprite sprite, float scale = 1f, float bloomAlpha = -1f)
    {
        if (crosshairImage != null && sprite != null)
            crosshairImage.sprite = sprite;

        if (crosshairImage != null)
            crosshairImage.rectTransform.localScale = Vector3.one * Mathf.Clamp(scale, 0.05f, 10f);

        if (bloomImage != null && bloomAlpha >= 0f)
        {
            var c = bloomImage.color;
            c.a = Mathf.Clamp01(bloomAlpha);
            bloomImage.color = c;
        }
    }

    public void ResetToDefault()
    {
        SetCrosshair(defaultCrosshair, defaultScale);
    }
}
