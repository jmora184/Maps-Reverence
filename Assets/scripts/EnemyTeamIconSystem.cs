using System.Collections.Generic;
using UnityEngine;

using UnityEngine.UI;

/// <summary>
/// Read-only UI system that displays one icon per Enemy Team root (EncounterTeamAnchor).
///
/// Fixes:
/// - Allows dragging ANY prefab (GameObject) instead of requiring EnemyTeamIconUI on the root.
/// - Properly wires EnemyTeamDirectionArrowUI to a deep child "ArrowImage"/"Arrow" (so the STAR doesn't rotate).
/// - Optional scale / pixel size to avoid "tiny icon" spawns.
/// </summary>
[DefaultExecutionOrder(2000)]
public class EnemyTeamIconSystem : MonoBehaviour
{
    [Header("Arrow Tuning")]
    [Tooltip("Extra non-uniform scale applied to ArrowImage only (X and Y). (1,1) = unchanged.")]
    public Vector2 arrowScaleXY = Vector2.one;

    [Tooltip("Extra pixel offset applied to ArrowImage only (anchoredPosition). (0,0) = unchanged.")]
    public Vector2 arrowOffsetXY = Vector2.zero;


    public static EnemyTeamIconSystem Instance { get; private set; }

    [Header("Prefab + Parent")]
    [Tooltip("UI prefab for the enemy team icon (star). Can be ANY GameObject prefab.")]
    public GameObject iconPrefab;

    [Tooltip("Parent under the Canvas where icons will be instantiated.")]
    public RectTransform iconParent;

    [Header("Icon Size (optional)")]
    [Tooltip("Force local scale for spawned icons. Use this if icons appear tiny/huge.")]
    [Min(0.01f)]
    public float iconLocalScale = 1f;

    [Tooltip("Force icon size in pixels (width & height). Leave 0 to keep prefab size.")]
    [Min(0f)]
    public float iconSizePixels = 0f;

    [Header("Cameras / Projection")]
    [Tooltip("Camera used to project world points to screen points. For command mode icons, use your command camera.")]
    public Camera worldCamera;

    [Header("Placement")]
    [Tooltip("World-space offset added to the team's anchor position (e.g., raise above ground).")]
    public Vector3 worldOffset = new Vector3(0f, 1.8f, 0f);

    [Tooltip("Screen-space pixel offset after projection.")]
    public Vector2 screenOffsetPixels = Vector2.zero;

    [Header("Visibility")]
    [Tooltip("If true, only show icons when command mode is active (wire a behaviour that is enabled in command mode).")]
    public bool onlyShowInCommandMode = false;

    [Tooltip("If set, icons are only visible when this Behaviour is enabled (e.g., CommandCamera.enabled or a CommandMode script).")]
    public Behaviour commandModeEnabledIndicator;

    [Tooltip("If true, hides icons when offscreen.")]
    public bool hideWhenOffscreen = true;

    private readonly Dictionary<EncounterTeamAnchor, EnemyTeamIconUI> _icons = new();
    private readonly List<EncounterTeamAnchor> _removeBuffer = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (iconParent == null || iconPrefab == null) return;
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;

        bool allowVisible = !onlyShowInCommandMode || (commandModeEnabledIndicator != null && commandModeEnabledIndicator.enabled);

        // Find all enemy team anchors
        EncounterTeamAnchor[] anchors = FindObjectsOfType<EncounterTeamAnchor>();
        foreach (var a in anchors)
        {
            if (a == null) continue;
            if (a.faction != EncounterDirectorPOC.Faction.Enemy) continue;

            if (!_icons.ContainsKey(a))
                _icons[a] = CreateIconFor(a);
        }

        // Update existing icons
        _removeBuffer.Clear();
        foreach (var kvp in _icons)
        {
            var anchor = kvp.Key;
            var icon = kvp.Value;

            if (anchor == null || icon == null)
            {
                _removeBuffer.Add(anchor);
                continue;
            }

            if (!allowVisible)
            {
                icon.SetVisible(false);
                continue;
            }

            Vector3 worldPos = anchor.AnchorWorldPosition + worldOffset;
            Vector3 sp = worldCamera.WorldToScreenPoint(worldPos);

            bool isOnscreen = sp.z > 0f &&
                              sp.x >= 0f && sp.x <= Screen.width &&
                              sp.y >= 0f && sp.y <= Screen.height;

            if (hideWhenOffscreen && !isOnscreen)
            {
                icon.SetVisible(false);
                continue;
            }

            icon.SetVisible(true);
            icon.SetScreenPosition((Vector2)sp + screenOffsetPixels);

            // Update count (exclude a child named "Anchor" if present)
            int count = CountTeamMembers(anchor.transform);
            icon.SetCount(count);
        }

        // Cleanup removed
        for (int i = 0; i < _removeBuffer.Count; i++)
        {
            var a = _removeBuffer[i];
            if (a == null) continue;
            if (_icons.TryGetValue(a, out var icon) && icon != null)
                Destroy(icon.gameObject);
            _icons.Remove(a);
        }
    }

    private EnemyTeamIconUI CreateIconFor(EncounterTeamAnchor anchor)
    {
        GameObject go = Instantiate(iconPrefab, iconParent);
        go.name = $"EnemyTeamIcon_{anchor.name}";
        go.SetActive(true);

        // Normalize size/scale (fix "tiny icon" issues)
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            // Respect prefab scale: multiply existing scale instead of overwriting it
            var __baseScale = rt.localScale;
            var __mult = Mathf.Max(0.01f, iconLocalScale);
            rt.localScale = new Vector3(__baseScale.x * __mult, __baseScale.y * __mult, __baseScale.z * __mult);
            if (iconSizePixels > 0.01f)
            {
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, iconSizePixels);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, iconSizePixels);
            }
        }

        // Ensure EnemyTeamIconUI exists
        var icon = go.GetComponent<EnemyTeamIconUI>();
        if (icon == null) icon = go.AddComponent<EnemyTeamIconUI>();
        AutoWireIconUI(icon);

        // Optional: allow hover/click targeting
        var bridge = go.GetComponent<EnemyTeamIconTargetingBridge>();
        if (bridge == null) bridge = go.AddComponent<EnemyTeamIconTargetingBridge>();
        bridge.hoverHintMessage = "Enemy Team";
        bridge.Bind(anchor.transform);

        // --- Direction arrow (optional) ---
        // Prefer an arrow component already in the prefab (often wired in Inspector).
        var arrow = go.GetComponentInChildren<EnemyTeamDirectionArrowUI>(true);
        if (arrow == null) arrow = go.AddComponent<EnemyTeamDirectionArrowUI>();

        EnsureArrowWired(go.transform, arrow, arrowScaleXY, arrowOffsetXY);
        
            ApplyArrowTuningRuntime(arrow);EnsureStarWired(go.transform);
        arrow.spritePointsRight = true;
        arrow.Bind(anchor, worldCamera);

        return icon;
    }

    public static int CountTeamMembers(Transform teamRoot)
    {
        if (teamRoot == null) return 0;
        int count = 0;
        for (int i = 0; i < teamRoot.childCount; i++)
        {
            var c = teamRoot.GetChild(i);
            if (c == null) continue;
            if (c.name == "Anchor") continue;
            if (!c.gameObject.activeInHierarchy) continue;
            count++;
        }
        return count;
    }

    private static void AutoWireIconUI(EnemyTeamIconUI icon)
    {
        if (icon == null) return;

        if (icon.rect == null) icon.rect = icon.GetComponent<RectTransform>();
        if (icon.image == null) icon.image = icon.GetComponent<Image>();

        if (icon.countText == null)
        {
            // Try common names first
            Transform t = icon.transform.Find("CountText");
            if (t == null) t = icon.transform.Find("Count");
            if (t == null)
            {
                // Fallback: first Text found in children
                Text txt = icon.GetComponentInChildren<Text>(true);
                if (txt != null) icon.countText = txt;
            }
            else
            {
                icon.countText = t.GetComponent<Text>();
            }
        }
    }

    /// <summary>
    /// Make sure EnemyTeamDirectionArrowUI drives an ArrowImage child (not the star/root),
    /// even if ArrowImage is nested under OrbitalAnchor.
    /// </summary>
    private static void EnsureArrowWired(Transform iconRoot, EnemyTeamDirectionArrowUI arrow, Vector2 arrowScaleXY, Vector2 arrowOffsetXY)
    {
        if (iconRoot == null || arrow == null) return;

        // Prefer an OrbitalAnchor as the rotation center if it exists (keeps the arrow outside the star).
        Transform orbitalAnchorT = FindDeepChild(iconRoot, "OrbitalAnchor");
        var iconRootRT = iconRoot as RectTransform ?? iconRoot.GetComponent<RectTransform>();
        var orbitalRT = orbitalAnchorT as RectTransform ?? (orbitalAnchorT != null ? orbitalAnchorT.GetComponent<RectTransform>() : null);

        arrow.orbitCenter = orbitalRT != null ? orbitalRT : iconRootRT;

        // ALWAYS bind the arrow to a dedicated ArrowImage (so the STAR never rotates).
        Transform arrowT = FindDeepChild(iconRoot, "ArrowImage");
        if (arrowT == null) arrowT = FindDeepChild(iconRoot, "Arrow");
        if (arrowT == null) arrowT = FindDeepChild(iconRoot, "ArrowGraphic");

        Image arrowImg = null;

        if (arrowT == null)
        {
            // Create a safe default under OrbitalAnchor.
            Transform parent = orbitalAnchorT;
            if (parent == null)
            {
                var orbitalGO = new GameObject("OrbitalAnchor", typeof(RectTransform));
                parent = orbitalGO.transform;
                parent.SetParent(iconRoot, false);
                orbitalRT = parent.GetComponent<RectTransform>();
                orbitalRT.anchorMin = new Vector2(0.5f, 0.5f);
                orbitalRT.anchorMax = new Vector2(0.5f, 0.5f);
                orbitalRT.anchoredPosition = Vector2.zero;
                orbitalRT.sizeDelta = Vector2.zero;

                arrow.orbitCenter = orbitalRT;
            }

            var arrowGO = new GameObject("ArrowImage", typeof(RectTransform), typeof(Image));
            arrowGO.transform.SetParent(parent, false);
            arrowT = arrowGO.transform;

            // Important: arrow should not block clicks.
            arrowImg = arrowGO.GetComponent<Image>();
            if (arrowImg != null) arrowImg.raycastTarget = false;

            // Prevent the "white box" issue:
            // If we had to create an Image, Unity assigns a built-in white sprite.
            // Try to copy the real arrow sprite from any existing ArrowImage in the scene.
            if (arrowImg != null)
            {
                var refSprite = FindReferenceSprite("ArrowImage", iconRoot);
                if (refSprite != null) arrowImg.sprite = refSprite;
                else
                {
                    // No reference found: hide until user assigns a sprite in the prefab.
                    var c = arrowImg.color; c.a = 0f; arrowImg.color = c;
                }
            }
        }
        else
        {
            // Ensure it has an Image.
            arrowImg = arrowT.GetComponent<Image>();
            if (arrowImg == null) arrowImg = arrowT.gameObject.AddComponent<Image>();

            // If it's still the built-in white sprite, replace with a reference sprite.
            if (arrowImg != null && (arrowImg.sprite == null || IsBuiltinWhiteSprite(arrowImg.sprite)))
            {
                var refSprite = FindReferenceSprite("ArrowImage", iconRoot);
                if (refSprite != null) arrowImg.sprite = refSprite;
            }
        }

        // Make sure it's active.
        if (!arrowT.gameObject.activeSelf) arrowT.gameObject.SetActive(true);

        // Force the binding (overwrite any incorrect inspector wiring).
        arrow.arrowRect = arrowT as RectTransform ?? arrowT.GetComponent<RectTransform>();
        if (arrow.arrowRect != null)
            arrow.arrowGraphic = arrow.arrowRect.GetComponent<Graphic>();
    
        // Apply per-arrow tuning (scale + offset)
        if (arrow.arrowRect != null)
        {
            arrow.arrowRect.localScale = new Vector3(arrowScaleXY.x, arrowScaleXY.y, 1f);
            arrow.arrowRect.anchoredPosition += arrowOffsetXY;
        }
}


    private static void EnsureStarWired(Transform iconRoot)
    {
        if (iconRoot == null) return;

        Transform orbital = FindDeepChild(iconRoot, "OrbitalAnchor");
        Transform starT = FindDeepChild(iconRoot, "StarImage");
        if (starT == null) starT = FindDeepChild(iconRoot, "Star");

        if (starT == null)
        {
            // Create under OrbitalAnchor (or root if missing)
            Transform parent = orbital != null ? orbital : iconRoot;
            var starGO = new GameObject("StarImage", typeof(RectTransform), typeof(Image));
            starGO.transform.SetParent(parent, false);
            starT = starGO.transform;

            var img = starGO.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }

        if (!starT.gameObject.activeSelf) starT.gameObject.SetActive(true);

        var starImg = starT.GetComponent<Image>();
        if (starImg == null) starImg = starT.gameObject.AddComponent<Image>();

        // If no sprite (or builtin), try to copy from any existing StarImage in the scene.
        if (starImg.sprite == null || IsBuiltinWhiteSprite(starImg.sprite))
        {
            var refSprite = FindReferenceSprite("StarImage", iconRoot);
            if (refSprite != null) starImg.sprite = refSprite;
        }

        // Ensure the star renders on TOP of the arrow:
        // In Unity UI, later siblings render on top.
        if (starT.parent != null)
            starT.SetAsLastSibling();
    }
    private static bool IsBuiltinWhiteSprite(Sprite s)
    {
        if (s == null) return false;
        // Unity's built-in default for UI Image is often named "UISprite"
        return string.Equals(s.name, "UISprite", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Sprite FindReferenceSprite(string childName, Transform excludeRoot)
    {
        // Look for any Image named childName (or containing "Arrow") with a non-builtin sprite.
        var images = GameObject.FindObjectsOfType<Image>(true);
        foreach (var img in images)
        {
            if (img == null) continue;
            if (excludeRoot != null && img.transform.IsChildOf(excludeRoot)) continue;

            var n = img.gameObject.name;
            bool nameMatch = string.Equals(n, childName, System.StringComparison.OrdinalIgnoreCase) ||
                             (childName == "ArrowImage" && n.ToLowerInvariant().Contains("arrow"));

            if (!nameMatch) continue;

            if (img.sprite != null && !IsBuiltinWhiteSprite(img.sprite))
                return img.sprite;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == name) return c;
            var found = FindDeepChild(c, name);
            if (found != null) return found;
        }
        return null;
    }
    private void ApplyArrowTuningRuntime(EnemyTeamDirectionArrowUI arrow)
    {
        if (arrow == null || arrow.arrowRect == null) return;

        // Ensure we run after the arrow script has computed its orbit position.
        // This re-applies scale/position every frame so changes in the inspector take effect immediately.
        arrow.arrowRect.localScale = new Vector3(arrowScaleXY.x, arrowScaleXY.y, 1f);
        arrow.arrowRect.anchoredPosition += arrowOffsetXY;
    }

}
