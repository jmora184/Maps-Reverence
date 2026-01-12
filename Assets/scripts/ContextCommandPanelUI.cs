using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Shows a vertical-stack context command panel (Join/Move/Cancel)
/// near the currently selected unit in command mode.
/// </summary>
public class ContextCommandPanelUI : MonoBehaviour
{
    [Header("Refs")]
    public CommandStateMachine sm;
    public RectTransform panel;
    public RectTransform canvasRoot;
    public Canvas canvas;
    public Camera commandCam;

    [Header("Buttons")]
    public Button joinButton;
    public Button moveButton;
    public Button cancelButton;

    [Header("Placement")]
    public float worldHeight = 2.0f;
    public Vector2 screenOffset = new Vector2(80f, 40f);
    public Vector2 screenPadding = new Vector2(12f, 12f);

    [Header("Hints")]
    [TextArea] public string moveHintMessage = "Pick a new location to move";
    public float moveHintDuration = 2.5f;

    [TextArea] public string joinHintMessage = "Select a Unit or Ally to join";
    public float joinHintDuration = 2.5f;

    private Transform followTarget;

    // After join is confirmed (2nd ally selected), suppress showing this panel
    // until the player changes selection to another object.
    private bool suppressAfterJoinTargetChosen;
    private GameObject suppressPrimarySelectionRef;

    private void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();

        if (canvasRoot == null && canvas != null)
            canvasRoot = canvas.transform as RectTransform;

        EnsureCommandCam();

        WireButtons();
        Hide();
    }

    private void OnEnable()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm != null)
        {
            // Detect the moment a join target (2nd ally) is chosen:
            sm.OnAddRequested += HandleAddRequested;

            // Detect when user clicks something else (selection changes):
            sm.OnSelectionChanged += HandleSelectionChanged;
        }
    }

    private void OnDisable()
    {
        if (sm != null)
        {
            sm.OnAddRequested -= HandleAddRequested;
            sm.OnSelectionChanged -= HandleSelectionChanged;
        }
    }

    /// <summary>
    /// CommandStateMachine expects the UI to have Refresh(state, selectionCount).
    /// </summary>
    public void Refresh(CommandStateMachine.State state, int selectionCount)
    {
        // If we are suppressing after a join target was chosen, keep hidden.
        if (suppressAfterJoinTargetChosen)
        {
            Hide();
            return;
        }

        bool shouldShow =
            state == CommandStateMachine.State.UnitSelected &&
            selectionCount > 0 &&
            sm != null &&
            sm.PrimarySelected != null;

        if (!shouldShow)
        {
            Hide();
            return;
        }

        followTarget = sm.PrimarySelected.transform;
        Show();
        Reposition();
    }

    private void LateUpdate()
    {
        if (sm == null || panel == null) return;

        // Suppress after 2nd ally chosen
        if (suppressAfterJoinTargetChosen)
        {
            Hide();
            return;
        }

        bool shouldShow =
            sm.CurrentState == CommandStateMachine.State.UnitSelected &&
            sm.PrimarySelected != null;

        if (!shouldShow)
        {
            Hide();
            return;
        }

        followTarget = sm.PrimarySelected.transform;
        Show();
        Reposition();
    }

    private void HandleAddRequested(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (sm == null) return;
        if (clickedUnit == null) return;

        // Detect JOIN confirmation moment:
        // JoinArmed is true, JoinSource exists, and clickedUnit is NOT the join source.
        if (sm.JoinArmed && sm.JoinSource != null && clickedUnit != sm.JoinSource)
        {
            suppressAfterJoinTargetChosen = true;

            // Track what selection we’re “waiting to change from”
            suppressPrimarySelectionRef = sm.PrimarySelected;

            // Hide immediately
            Hide();
        }
    }

    private void HandleSelectionChanged(IReadOnlyList<GameObject> selection)
    {
        if (!suppressAfterJoinTargetChosen) return;
        if (sm == null) return;

        // Once the player clicks another object, the primary selected changes.
        if (sm.PrimarySelected != suppressPrimarySelectionRef)
        {
            suppressAfterJoinTargetChosen = false;
            suppressPrimarySelectionRef = null;
            // LateUpdate/Refresh will show naturally when appropriate.
        }
    }

    private void WireButtons()
    {
        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() =>
            {
                if (sm == null || sm.PrimarySelected == null) return;

                sm.ArmJoinFromCurrentSelection();
                Hide();

                // show join hint
                TryShowHint(joinHintMessage, joinHintDuration);
            });
        }

        if (moveButton != null)
        {
            moveButton.onClick.RemoveAllListeners();
            moveButton.onClick.AddListener(() =>
            {
                // Only if a unit is selected
                if (sm == null || sm.PrimarySelected == null) return;

                // ✅ UPDATED to match your current CommandStateMachine
                sm.ArmMoveFromCurrentSelection();
                Hide();

                // Move hint
                TryShowHint(moveHintMessage, moveHintDuration);
            });
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() =>
            {
                // ✅ Your CommandStateMachine no longer exposes ExitCommandMode publicly.
                // So we let CommandCamToggle handle leaving command mode,
                // and we clear selection for safety.

                if (sm != null)
                    sm.ClearSelection();

                if (CommandCamToggle.Instance != null)
                    CommandCamToggle.Instance.SetCommandMode(false);

                // Cancel should also clear suppression
                suppressAfterJoinTargetChosen = false;
                suppressPrimarySelectionRef = null;

                Hide();
            });
        }
    }

    private void Show()
    {
        if (panel != null && !panel.gameObject.activeSelf)
            panel.gameObject.SetActive(true);
    }

    public void Hide()
    {
        followTarget = null;
        if (panel != null && panel.gameObject.activeSelf)
            panel.gameObject.SetActive(false);
    }

    private void Reposition()
    {
        if (followTarget == null) return;

        EnsureCommandCam();
        if (commandCam == null) return;

        Vector3 world = followTarget.position + Vector3.up * worldHeight;
        Vector3 screen = commandCam.WorldToScreenPoint(world);
        if (screen.z <= 0f) { Hide(); return; }

        Camera uiCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCam = canvas.worldCamera;
            if (uiCam == null) uiCam = commandCam;
        }

        float s = (canvas != null) ? canvas.scaleFactor : 1f;
        Vector2 screenPos = (Vector2)screen + (screenOffset * s);

        RectTransform parentRect = panel.parent as RectTransform;
        if (parentRect == null) parentRect = canvasRoot;
        if (parentRect == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, uiCam, out Vector2 local))
        {
            panel.anchoredPosition = ClampToRect(parentRect, panel, local, screenPadding);
            panel.SetAsLastSibling();
        }
    }

    private void EnsureCommandCam()
    {
        if (commandCam != null) return;

        if (CommandCamToggle.Instance != null && CommandCamToggle.Instance.commandCam != null)
        {
            commandCam = CommandCamToggle.Instance.commandCam;
            return;
        }

        commandCam = Camera.main;
    }

    private static Vector2 ClampToRect(RectTransform root, RectTransform rt, Vector2 desired, Vector2 padding)
    {
        Rect r = root.rect;
        Vector2 size = rt.rect.size;
        Vector2 pivot = rt.pivot;

        float minX = r.xMin + padding.x + size.x * pivot.x;
        float maxX = r.xMax - padding.x - size.x * (1f - pivot.x);
        float minY = r.yMin + padding.y + size.y * pivot.y;
        float maxY = r.yMax - padding.y - size.y * (1f - pivot.y);

        desired.x = Mathf.Clamp(desired.x, minX, maxX);
        desired.y = Mathf.Clamp(desired.y, minY, maxY);
        return desired;
    }

    // Compile-safe HintSystem call (Show(string,float) preferred, fallback Show(string))
    private void TryShowHint(string message, float duration)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            Type hintType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && hintType == null; i++)
                hintType = assemblies[i].GetType("HintSystem");

            if (hintType == null)
            {
                Debug.Log(message);
                return;
            }

            var show2 = hintType.GetMethod(
                "Show",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(float) },
                null);

            if (show2 != null)
            {
                show2.Invoke(null, new object[] { message, duration });
                return;
            }

            var show1 = hintType.GetMethod(
                "Show",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (show1 != null)
            {
                show1.Invoke(null, new object[] { message });
                return;
            }

            Debug.Log(message);
        }
        catch
        {
            Debug.Log(message);
        }
    }
}
