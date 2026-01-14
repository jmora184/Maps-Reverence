using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a vertical-stack context command panel (Join/Move/Cancel)
/// near the currently selected unit in command mode.
/// 
/// UPDATED:
/// - If a TEAM is selected (selectionCount > 1) and a Team Star UI exists,
///   the panel will anchor under the TEAM STAR (instead of the ally).
///   This uses reflection to locate the TeamIconUI's underlying Team (Id match).
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

    // After a move destination is chosen, keep the panel hidden until the player re-selects
    private bool suppressAfterMoveTargetChosen;
    private bool waitingForMoveConfirm;
    private GameObject suppressMovePrimarySelectionRef;

    private int moveSuppressSetFrame = -1;

    // Track state transitions so we can detect "move confirm" (leaving MoveTargeting after pressing Move)
    private CommandStateMachine.State lastState = CommandStateMachine.State.Inactive;

    // Reflection cache for TeamIconUI -> Team field
    private static FieldInfo cachedTeamIconUITeamField;

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
            sm.OnAddRequested += HandleAddRequested;
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
        if (suppressAfterJoinTargetChosen) { Hide(); return; }
        if (suppressAfterMoveTargetChosen) { Hide(); return; }

        bool shouldShow =
            state == CommandStateMachine.State.UnitSelected &&
            selectionCount > 0 &&
            sm != null &&
            sm.PrimarySelected != null;

        if (!shouldShow) { Hide(); return; }

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

            suppressAfterMoveTargetChosen = true;
            suppressMovePrimarySelectionRef = sm.PrimarySelected;

            moveSuppressSetFrame = Time.frameCount;
            Hide();

            lastState = sm.CurrentState;
            return;
        }

        if (suppressAfterJoinTargetChosen) { Hide(); lastState = sm.CurrentState; return; }
        if (suppressAfterMoveTargetChosen) { Hide(); lastState = sm.CurrentState; return; }

        bool shouldShow =
            sm.CurrentState == CommandStateMachine.State.UnitSelected &&
            sm.PrimarySelected != null;

        if (!shouldShow) { Hide(); lastState = sm.CurrentState; return; }

        followTarget = sm.PrimarySelected.transform;
        Show();
        Reposition();

        lastState = sm.CurrentState;
    }

    private void HandleAddRequested(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (sm == null) return;
        if (clickedUnit == null) return;

        // JOIN confirmation moment
        if (sm.JoinArmed && sm.JoinSource != null && clickedUnit != sm.JoinSource)
        {
            suppressAfterJoinTargetChosen = true;
            suppressPrimarySelectionRef = sm.PrimarySelected;
            suppressSelectionCountRef = selection != null ? selection.Count : 0;
            Hide();
        }
    }

    private void HandleSelectionChanged(IReadOnlyList<GameObject> selection)
    {
        if (sm == null) return;

        // JOIN suppression clears when selection changes away
        if (suppressAfterJoinTargetChosen)
        {
            int currentCount = selection != null ? selection.Count : 0;
            if (sm.PrimarySelected != suppressPrimarySelectionRef || currentCount != suppressSelectionCountRef)
            {
                suppressAfterJoinTargetChosen = false;
                suppressPrimarySelectionRef = null;
                suppressSelectionCountRef = 0;
            }
        }

        // MOVE suppression clears only when the player re-selects something (and not in the same frame)
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
                TryShowHint(joinHintMessage, joinHintDuration);
            });
        }

        if (moveButton != null)
        {
            moveButton.onClick.RemoveAllListeners();
            moveButton.onClick.AddListener(() =>
            {
                if (sm == null || sm.PrimarySelected == null) return;

                sm.ArmMoveFromCurrentSelection();
                Hide();

                // We will suppress the panel once the destination is confirmed (when we leave MoveTargeting)
                waitingForMoveConfirm = true;

                TryShowHint(moveHintMessage, moveHintDuration);
            });
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() =>
            {
                if (sm != null)
                    sm.ClearSelection();

                if (CommandCamToggle.Instance != null)
                    CommandCamToggle.Instance.SetCommandMode(false);

                suppressAfterJoinTargetChosen = false;
                suppressPrimarySelectionRef = null;
                suppressSelectionCountRef = 0;

                suppressAfterMoveTargetChosen = false;
                suppressMovePrimarySelectionRef = null;
                moveSuppressSetFrame = -1;
                waitingForMoveConfirm = false;

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
        if (panel == null) return;

        EnsureCommandCam();
        if (commandCam == null) return;

        // Determine UI camera for conversion (overlay = null)
        Camera uiCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCam = canvas.worldCamera;
            if (uiCam == null) uiCam = commandCam;
        }

        RectTransform parentRect = panel.parent as RectTransform;
        if (parentRect == null) parentRect = canvasRoot;
        if (parentRect == null) return;

        // ✅ NEW: If a TEAM is selected and we can find its star UI, anchor the panel under the star.
        if (sm != null && sm.PrimarySelected != null)
        {
            int selCount = sm.CurrentSelection != null ? sm.CurrentSelection.Count : 0;
            if (selCount > 1 && TeamManager.Instance != null)
            {
                Team team = TeamManager.Instance.GetTeamOf(sm.PrimarySelected.transform);
                if (team != null)
                {
                    RectTransform star = FindTeamStarRect(team.Id);
                    if (star != null && star.gameObject.activeInHierarchy)
                    {
                        float s = (canvas != null) ? canvas.scaleFactor : 1f;

                        // Convert star UI world position -> screen -> panel-parent local
                        Vector2 starScreen = RectTransformUtility.WorldToScreenPoint(uiCam, star.position);
                        Vector2 desiredScreen = starScreen + (screenOffset * s);

                        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, desiredScreen, uiCam, out Vector2 local))
                        {
                            panel.anchoredPosition = ClampToRect(parentRect, panel, local, screenPadding);
                            panel.SetAsLastSibling();
                            return;
                        }
                    }
                }
            }
        }

        // Fallback: follow selected unit in world
        if (followTarget == null) return;

        Vector3 world = followTarget.position + Vector3.up * worldHeight;
        Vector3 screen = commandCam.WorldToScreenPoint(world);
        if (screen.z <= 0f) { Hide(); return; }

        float scale = (canvas != null) ? canvas.scaleFactor : 1f;
        Vector2 screenPos = (Vector2)screen + (screenOffset * scale);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, uiCam, out Vector2 localPoint))
        {
            panel.anchoredPosition = ClampToRect(parentRect, panel, localPoint, screenPadding);
            panel.SetAsLastSibling();
        }
    }

    private RectTransform FindTeamStarRect(int teamId)
    {
        // Find all TeamIconUI in the scene (including inactive)
        TeamIconUI[] all = Resources.FindObjectsOfTypeAll<TeamIconUI>();
        for (int i = 0; i < all.Length; i++)
        {
            TeamIconUI ui = all[i];
            if (ui == null) continue;
            if (!ui.gameObject.scene.IsValid()) continue; // scene objects only

            Team t = TryGetTeamFromTeamIconUI(ui);
            if (t != null && t.Id == teamId)
                return ui.transform as RectTransform;
        }
        return null;
    }

    private Team TryGetTeamFromTeamIconUI(TeamIconUI teamIconUI)
    {
        if (teamIconUI == null) return null;

        // Cache a likely field once
        if (cachedTeamIconUITeamField == null)
        {
            Type tp = teamIconUI.GetType();
            cachedTeamIconUITeamField =
                tp.GetField("_team", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                tp.GetField("team", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                tp.GetField("Team", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        if (cachedTeamIconUITeamField == null) return null;

        try
        {
            return cachedTeamIconUITeamField.GetValue(teamIconUI) as Team;
        }
        catch
        {
            return null;
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
                BindingFlags.Public | BindingFlags.Static,
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
                BindingFlags.Public | BindingFlags.Static,
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
