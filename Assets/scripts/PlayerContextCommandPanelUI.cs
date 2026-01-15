using UnityEngine;
using UnityEngine.UI;

public class PlayerContextCommandPanelUI : MonoBehaviour
{
    public static PlayerContextCommandPanelUI Instance { get; private set; }

    [Header("UI Root")]
    public RectTransform root; // panel RectTransform
    public CanvasGroup canvasGroup;

    [Header("Buttons")]
    public Button followMeButton;
    public Button holdFireButton;
    public Button holdPositionButton; // optional

    [Header("Theme (Player Buttons)")]
    public Color buttonNormal = new Color(0.20f, 0.35f, 0.85f, 1f);
    public Color buttonHighlight = new Color(0.28f, 0.45f, 0.95f, 1f);
    public Color buttonPressed = new Color(0.12f, 0.25f, 0.70f, 1f);
    public Color buttonTextColor = Color.white;

    [Header("Positioning")]
    public Vector2 screenOffset = new Vector2(0f, -30f); // under the icon

    private RectTransform anchor;
    private Transform playerTarget;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (root == null) root = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        ApplyTheme();
        HideImmediate();

        // Hook buttons (you can replace these bodies later with your real logic)
        if (followMeButton != null) followMeButton.onClick.AddListener(OnFollowMeClicked);
        if (holdFireButton != null) holdFireButton.onClick.AddListener(OnHoldFireClicked);
        if (holdPositionButton != null) holdPositionButton.onClick.AddListener(OnHoldPositionClicked);
    }

    private void LateUpdate()
    {
        if (!IsVisible()) return;
        if (anchor == null) return;

        // Keep panel pinned under player icon
        root.position = anchor.position + (Vector3)screenOffset;
    }

    public void ShowUnder(RectTransform iconRect, Transform player)
    {
        anchor = iconRect;
        playerTarget = player;

        ApplyTheme();

        if (!root.gameObject.activeSelf)
            root.gameObject.SetActive(true);

        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    public void HideImmediate()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);

        anchor = null;
        playerTarget = null;
    }

    public bool IsVisible()
    {
        return root != null && root.gameObject.activeSelf && (canvasGroup == null || canvasGroup.alpha > 0.01f);
    }

    private void ApplyTheme()
    {
        ApplyButtonTheme(followMeButton);
        ApplyButtonTheme(holdFireButton);
        ApplyButtonTheme(holdPositionButton);
    }

    private void ApplyButtonTheme(Button b)
    {
        if (b == null) return;

        // ColorBlock controls hover/pressed transitions
        var cb = b.colors;
        cb.normalColor = buttonNormal;
        cb.highlightedColor = buttonHighlight;
        cb.pressedColor = buttonPressed;
        cb.selectedColor = buttonHighlight;
        cb.disabledColor = new Color(buttonNormal.r, buttonNormal.g, buttonNormal.b, 0.35f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        b.colors = cb;

        // Ensure the background image exists and is tinted
        var img = b.GetComponent<Image>();
        if (img != null) img.color = buttonNormal;

        // Tint text
        var txt = b.GetComponentInChildren<Text>(true);
        if (txt != null) txt.color = buttonTextColor;
    }

    // ===== Button actions (stub for now) =====

    private void OnFollowMeClicked()
    {
        // TODO: Hook to your player command system
        Debug.Log("[Player Panel] Follow Me clicked");
        HideImmediate();
    }

    private void OnHoldFireClicked()
    {
        Debug.Log("[Player Panel] Hold Fire clicked");
        HideImmediate();
    }

    private void OnHoldPositionClicked()
    {
        Debug.Log("[Player Panel] Hold Position clicked");
        HideImmediate();
    }
}
