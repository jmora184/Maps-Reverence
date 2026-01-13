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

    private int suppressSelectionCountRef;

    // ✅ NEW: after a move destination is chosen, keep the panel hidden until the player re-selects (clicks a unit/star again).
    private bool suppressAfterMoveTargetChosen;
    private bool waitingForMoveConfirm;
    private GameObject suppressMovePrimarySelectionRef;


    private int moveSuppressSetFrame = -1;// Track state transitions so we can detect "move confirm" (leaving MoveTargeting after pressing Move)
    private CommandStateMachine.State lastState = CommandStateMachine.State.Inactive;

    private void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();

        if (canvasRoot == null && canvas != null)
            canvasRoot = canvas.transform as RectTransform;

        EnsureCommandCam();

        WireButtons();
        Hide();

        if (sm != null) lastState = sm.CurrentState;
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



        // If we are suppressing after a move destination was chosen, keep hidden.
        if (suppressAfterMoveTargetChosen)
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

        // Detect leaving MoveTargeting after the player pressed Move (meaning destination was chosen)
        if (waitingForMoveConfirm && lastState == CommandStateMachine.State.MoveTargeting && sm.CurrentState != CommandStateMachine.State.MoveTargeting)
        {
            waitingForMoveConfirm = false;

            // Keep hidden even if selection stays (e.g., team remains selected)
            suppressAfterMoveTargetChosen = true;
            suppressMovePrimarySelectionRef = sm.PrimarySelected;


            moveSuppressSetFrame = Time.frameCount; Hide();
            lastState = sm.CurrentState;
            return;
        }
        // Suppress after 2nd ally chosen
        if (suppressAfterJoinTargetChosen)
        {
            Hide();
            return;
        }



        // Suppress after move destination chosen
        if (suppressAfterMoveTargetChosen)
        {
            Hide();
            lastState = sm.CurrentState;
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

        // Track for transition detection
        lastState = sm.CurrentState;
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

            suppressSelectionCountRef = selection != null ? selection.Count : 0;

            // Hide immediately
            Hide();
        }
    }

    private void HandleSelectionChanged(IReadOnlyList<GameObject> selection)
    {
        if (sm == null) return;

        // JOIN suppression clears when the selection changes away from the join-confirm moment.
        // Primary may stay the same (leader), so also clear when selection COUNT changes (eg: clicking team star selects team).
        if (suppressAfterJoinTargetChosen)
        {
            int currentCount = selection != null ? selection.Count : 0;

            if (sm.PrimarySelected != suppressPrimarySelectionRef || currentCount != suppressSelectionCountRef)
            {
                suppressAfterJoinTargetChosen = false;
                suppressPrimarySelectionRef = null;
                suppressSelectionCountRef = 0;
                suppressSelectionCountRef = 0;

                suppressSelectionCountRef = 0;
                // LateUpdate/Refresh will show naturally when appropriate.
            }
        }

        // MOVE suppression clears only when the player re-selects something (unit icon or team star).
        // We also ignore any selection callbacks that happen in the same frame we set suppression.
        if (suppressAfterMoveTargetChosen)
        {
            if (Time.frameCount > moveSuppressSetFrame)
            {
                suppressAfterMoveTargetChosen = false;
                suppressMovePrimarySelectionRef = null;
                moveSuppressSetFrame = -1;
            }
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


                // We will suppress the panel once the destination is confirmed (when we leave MoveTargeting)
                waitingForMoveConfirm = true;
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
                suppressSelectionCountRef = 0;

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
