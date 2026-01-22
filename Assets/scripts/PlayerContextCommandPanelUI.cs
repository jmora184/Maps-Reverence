using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Player personal commands panel (shown when clicking the Player icon).
///
/// Follow Me (pick flow):
/// 1) Click Player icon
/// 2) Click Follow Me
/// 3) Click an Ally icon (or Team Star) to choose who follows
///
/// Formation buttons (Line/Wedge/Cancel) affect the current follower formation.
/// </summary>
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

    [Header("Formation Buttons (Followers)")]
    public Button lineFormationButton;   // sets followers to LineFront
    public Button wedgeFormationButton;  // sets followers to WedgeFront
    [Tooltip("Clears formation and returns followers to normal Arc-Behind follow.")]
    public Button cancelFormationButton; // sets followers to ArcBehind

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

        if (followMeButton != null) followMeButton.onClick.AddListener(OnFollowMeClicked);
        if (holdFireButton != null) holdFireButton.onClick.AddListener(OnHoldFireClicked);
        if (holdPositionButton != null) holdPositionButton.onClick.AddListener(OnHoldPositionClicked);

        if (lineFormationButton != null) lineFormationButton.onClick.AddListener(OnLineFormationClicked);
        if (wedgeFormationButton != null) wedgeFormationButton.onClick.AddListener(OnWedgeFormationClicked);
        if (cancelFormationButton != null) cancelFormationButton.onClick.AddListener(OnCancelFormationClicked);
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

        UpdateFormationButtonsVisibility();
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

        ApplyButtonTheme(lineFormationButton);
        ApplyButtonTheme(wedgeFormationButton);
        ApplyButtonTheme(cancelFormationButton);
    }

    private void ApplyButtonTheme(Button b)
    {
        if (b == null) return;

        var cb = b.colors;
        cb.normalColor = buttonNormal;
        cb.highlightedColor = buttonHighlight;
        cb.pressedColor = buttonPressed;
        cb.selectedColor = buttonHighlight;
        cb.disabledColor = new Color(buttonNormal.r, buttonNormal.g, buttonNormal.b, 0.35f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        b.colors = cb;

        var img = b.GetComponent<Image>();
        if (img != null) img.color = buttonNormal;

        // Legacy Text support
        var legacyText = b.GetComponentInChildren<Text>(true);
        if (legacyText != null) legacyText.color = buttonTextColor;

        // TMP support
        var tmpText = b.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null) tmpText.color = buttonTextColor;
    }


    private void UpdateFormationButtonsVisibility()
    {
        // Formation buttons only make sense when we have at least 2 followers.
        var sys = PlayerSquadFollowSystem.Instance;
        int followerCount = (sys != null) ? sys.FollowerCount : 0;

        bool showLineWedge = followerCount >= 2;

        if (lineFormationButton != null)
            lineFormationButton.gameObject.SetActive(showLineWedge);

        if (wedgeFormationButton != null)
            wedgeFormationButton.gameObject.SetActive(showLineWedge);

        // Only show "Cancel Formation" when:
        // - we have enough followers to use formations, AND
        // - we're currently in a non-default formation.
        bool showCancel = showLineWedge && sys != null && sys.formation != PlayerSquadFollowSystem.FollowFormation.ArcBehind;

        if (cancelFormationButton != null)
            cancelFormationButton.gameObject.SetActive(showCancel);
    }


    // ===== Button actions =====

    private void OnFollowMeClicked()
    {
        // Resolve player
        ResolvePlayerIfNeeded();
        if (playerTarget == null)
        {
            Debug.LogWarning("[Player Panel] Follow Me clicked, but Player not found (tag=Player).");
            HideImmediate();
            return;
        }

        // Arm pick mode: next ally/team click chooses who follows.
        var sys = PlayerSquadFollowSystem.EnsureExists();
        sys.SetPlayer(playerTarget);
        sys.ArmPickFollowers();

        HideImmediate();
    }

    private void OnLineFormationClicked()
    {
        ResolvePlayerIfNeeded();

        var sys = PlayerSquadFollowSystem.EnsureExists();
        if (playerTarget != null) sys.SetPlayer(playerTarget);

        sys.SetFormationLineFront();
        HideImmediate();
    }

    private void OnWedgeFormationClicked()
    {
        ResolvePlayerIfNeeded();

        var sys = PlayerSquadFollowSystem.EnsureExists();
        if (playerTarget != null) sys.SetPlayer(playerTarget);

        sys.SetFormationWedgeFront();
        HideImmediate();
    }

    private void OnCancelFormationClicked()
    {
        // Return to the default "normal" follow behavior (Arc-Behind)
        // and cancel any active pick session.
        var sys = PlayerSquadFollowSystem.EnsureExists();
        if (playerTarget != null) sys.SetPlayer(playerTarget);

        sys.SetFormationArcBehind();
        sys.DisarmPickFollowers();

        HideImmediate();
    }

    private void OnHoldFireClicked()
    {
        Debug.Log("[Player Panel] Hold Fire clicked (stub).");
        HideImmediate();
    }

    private void OnHoldPositionClicked()
    {
        Debug.Log("[Player Panel] Hold Position clicked (stub).");
        HideImmediate();
    }

    private void ResolvePlayerIfNeeded()
    {
        if (playerTarget != null) return;

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTarget = p.transform;
    }
}
