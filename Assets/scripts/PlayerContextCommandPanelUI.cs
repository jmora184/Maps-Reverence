using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player personal commands panel (shown when clicking the Player icon in CommandOverlayUI).
///
/// Follow Me behavior (updated):
/// - Uses CURRENT COMMAND SELECTION to decide who follows.
/// - If no allies are selected, we show a hint and do nothing (so you can choose).
///
/// Notes:
/// - This is meant to be used while in command view: select allies or a team star, then click Player icon -> Follow Me.
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
    public Button lineFormationButton;
    public Button wedgeFormationButton;
    public Button holdPositionButton; // optional

    [Header("Follow Selection")]
    [Tooltip("If true, Follow Me requires allies selected in command mode. If false, falls back to ALL allies.")]
    public bool requireSelectionForFollow = true;

    [Tooltip("Hint shown when Follow Me is pressed but no allies are selected.")]
    public string followRequiresSelectionHint = "Select allies (or a Team Star) first, then press Follow Me.";

    [Tooltip("How long the hint stays visible (if HintToastUI exists).")]
    public float hintDuration = 2f;

    [Header("Theme (Player Buttons)")]
    public Color buttonNormal = new Color(0.20f, 0.35f, 0.85f, 1f);
    public Color buttonHighlight = new Color(0.28f, 0.45f, 0.95f, 1f);
    public Color buttonPressed = new Color(0.12f, 0.25f, 0.70f, 1f);
    public Color buttonTextColor = Color.white;

    [Header("Positioning")]
    public Vector2 screenOffset = new Vector2(0f, -30f); // under the icon

    private RectTransform anchor;
    private Transform playerTarget;
    private HintToastUI hintToastUI;
    private Coroutine hintHideRoutine;

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

        // Hook buttons
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
        if (hintHideRoutine != null) StopCoroutine(hintHideRoutine);
        hintHideRoutine = null;

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

    // ===== Button actions =====

    private void OnFollowMeClicked()
    {
        // Resolve player
        if (playerTarget == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTarget = p.transform;
        }

        if (playerTarget == null)
        {
            Debug.LogWarning("[Player Panel] Follow Me clicked, but Player not found (tag=Player).");
            HideImmediate();
            return;
        }

        // Gather selected allies
        List<Transform> selectedAllies = GatherSelectedAllies();

        if (requireSelectionForFollow && (selectedAllies == null || selectedAllies.Count == 0))
        {
            ShowHint(followRequiresSelectionHint, hintDuration);
            return; // keep panel open so you can select then click again
        }

        // Start squad follow formation behind player
        var sys = PlayerSquadFollowSystem.EnsureExists();
        sys.SetPlayer(playerTarget);

        if (selectedAllies != null && selectedAllies.Count > 0)
            sys.BeginFollow(selectedAllies);
        else
            sys.BeginFollow_AllAllies(); // optional fallback

        HideImmediate();
    }

    private List<Transform> GatherSelectedAllies()
    {
        var sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null || sm.CurrentSelection == null || sm.CurrentSelection.Count == 0)
            return new List<Transform>();

        var list = new List<Transform>(sm.CurrentSelection.Count);
        for (int i = 0; i < sm.CurrentSelection.Count; i++)
        {
            var go = sm.CurrentSelection[i];
            if (go == null) continue;
            if (!go.CompareTag("Ally")) continue;

            // Avoid accidentally adding the player if you ever tag them Ally (unlikely)
            if (playerTarget != null && go.transform == playerTarget) continue;

            list.Add(go.transform);
        }

        // Stable order so slot assignment doesn't shuffle each click
        list.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return list;
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

    // ===== Hint Toast =====

    private void AutoFindHintToastUI()
    {
        if (hintToastUI != null) return;

        // Finds disabled objects too.
        var all = Resources.FindObjectsOfTypeAll<HintToastUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;
            if (!h.gameObject.scene.IsValid()) continue; // scene objects only
            hintToastUI = h;
            return;
        }
    }

    private void ShowHint(string message, float durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AutoFindHintToastUI();

        if (hintToastUI == null)
        {
            Debug.Log($"[Hint] {message}");
            return;
        }

        hintToastUI.Show(message);

        if (hintHideRoutine != null) StopCoroutine(hintHideRoutine);
        hintHideRoutine = StartCoroutine(HideHintAfter(durationSeconds));
    }

    private IEnumerator HideHintAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, seconds));

        if (hintToastUI != null)
            hintToastUI.Hide();

        hintHideRoutine = null;
    }
}
