using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates a row of grenade UI images based on the player's current grenade count.
/// Put your images in left-to-right order in the inspector:
/// Grenade1, Grenade2, Grenade3, Grenade4.
///
/// Example with 4 icons:
/// 4 grenades = all solid
/// 3 grenades = rightmost icon transparent
/// 2 grenades = two rightmost transparent
///
/// Attach this to your GrenadeManager object on the Canvas.
/// </summary>
public class GrenadeAmmoUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your grenade throw script. If left empty, auto-finds one in the scene.")]
    public PlayerThrowGrenadeOnG_Aim grenadeThrow;

    [Tooltip("Assign grenade images from LEFT to RIGHT.")]
    public Image[] grenadeImages;

    [Header("Alpha")]
    [Range(0f, 1f)] public float fullAlpha = 1f;
    [Range(0f, 1f)] public float emptyAlpha = 0.2f;

    [Header("Optional")]
    [Tooltip("If true, keeps checking for the throw script in case it spawns later.")]
    public bool keepTryingToFindThrowScript = false;

    private void Awake()
    {
        TryAutoFindThrowScript();
        RefreshNow();
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshNow();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (grenadeThrow == null && keepTryingToFindThrowScript)
        {
            TryAutoFindThrowScript();
            if (grenadeThrow != null)
            {
                Subscribe();
                RefreshNow();
            }
        }
    }

    private void Subscribe()
    {
        if (grenadeThrow != null)
            grenadeThrow.OnGrenadeCountChanged += HandleGrenadeCountChanged;
    }

    private void Unsubscribe()
    {
        if (grenadeThrow != null)
            grenadeThrow.OnGrenadeCountChanged -= HandleGrenadeCountChanged;
    }

    private void TryAutoFindThrowScript()
    {
        if (grenadeThrow == null)
            grenadeThrow = FindFirstObjectByType<PlayerThrowGrenadeOnG_Aim>();
    }

    private void HandleGrenadeCountChanged(int current, int max)
    {
        ApplyVisuals(current);
    }

    public void RefreshNow()
    {
        if (grenadeThrow != null)
            ApplyVisuals(grenadeThrow.currentGrenades);
        else
            ApplyVisuals(0);
    }

    private void ApplyVisuals(int currentGrenades)
    {
        if (grenadeImages == null || grenadeImages.Length == 0)
            return;

        int clamped = Mathf.Clamp(currentGrenades, 0, grenadeImages.Length);

        for (int i = 0; i < grenadeImages.Length; i++)
        {
            Image img = grenadeImages[i];
            if (img == null) continue;

            bool hasGrenadeForThisSlot = i < clamped;
            SetImageAlpha(img, hasGrenadeForThisSlot ? fullAlpha : emptyAlpha);
        }
    }

    private void SetImageAlpha(Image img, float alpha)
    {
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }
}
