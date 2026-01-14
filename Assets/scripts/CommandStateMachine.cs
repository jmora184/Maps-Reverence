using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;

public class CommandStateMachine : MonoBehaviour
{
    public enum State
    {
        Inactive,
        AwaitSelection,
        UnitSelected,
        MoveTargeting,
        AddTargeting
    }

    [Header("References")]
    public Camera commandCam;
    public bool requireCommandCamEnabled = true;

    [Header("Raycast")]
    public LayerMask groundMask;          // ground2
    public LayerMask selectableMask;      // optional
    public float raycastMaxDistance = 5000f;

    [Header("Selection")]
    public bool allowMultiSelect = false;
    public bool allowEnemySelect = false;
    public bool allowBossSelect = true;

    [Header("Move behavior")]
    public bool queueMovesInCommandMode = true;


    [Tooltip("If true, after clicking a move destination we keep the current selection, but return to AwaitSelection (so panels do not pop back up).")]
    public bool keepSelectionAfterMove = true;
    [Header("UI")]
    public CommandModeButtonsUI buttonsUI;

    [Header("Join Route Hint")]
    public bool blockJoinLeaderWhileInRoute = true;
    [TextArea] public string joinInRouteMessage = "Ally in route to Team";
    public float joinInRouteDuration = 2.0f;

    [Header("Move Order Busy Hint")]
    [Tooltip("If true, allies that are currently traveling to a move destination will not be selectable in command mode until they are close enough.")]
    public bool blockMoveOrderedUnitsWhileBusy = true;
    [TextArea] public string moveOrderBusyMessage = "Ally is currently following orders";
    public float moveOrderBusyDuration = 2.0f;
    [Tooltip("Extra distance beyond NavMeshAgent.stoppingDistance that still counts as busy.")]
    public float moveOrderBusyStopBuffer = 0.6f;

    public State CurrentState { get; private set; } = State.Inactive;

    private readonly List<GameObject> selection = new();
    public IReadOnlyList<GameObject> CurrentSelection => selection;
    public GameObject PrimarySelected => selection.Count > 0 ? selection[0] : null;

    // Join state (used by Executor when AddTargeting is join)
    public bool JoinArmed { get; private set; }
    public GameObject JoinSource { get; private set; }

    public event Action<IReadOnlyList<GameObject>, Vector3> OnMoveRequested;
    // Fires whenever the player clicks a ground point in MoveTargeting (even if moves are queued)
    public event Action<IReadOnlyList<GameObject>, Vector3> OnMoveTargetChosen;
    public event Action<IReadOnlyList<GameObject>, GameObject> OnAddRequested;
    public event Action<IReadOnlyList<GameObject>> OnSplitRequested;
    public event Action<IReadOnlyList<GameObject>> OnSelectionChanged;

    private void Awake()
    {
        if (buttonsUI == null) buttonsUI = FindObjectOfType<CommandModeButtonsUI>();
    }

    private void OnEnable() => EnterCommandMode();
    private void OnDisable() => ExitCommandMode();

    /// <summary>
    /// ✅ RESTORED for CommandOverlayUI compatibility.
    /// Sets selection to exactly this list and refreshes state/UI.
    /// </summary>
    public void SetSelection(List<GameObject> newSelection)
    {
        selection.Clear();

        if (newSelection != null)
        {
            for (int i = 0; i < newSelection.Count; i++)
            {
                var u = newSelection[i];
                if (u == null) continue;
                selection.Add(u);
            }
        }

        // Update state based on selection size (don’t force out of targeting modes)
        if (CurrentState != State.MoveTargeting && CurrentState != State.AddTargeting)
            SetState(selection.Count > 0 ? State.UnitSelected : State.AwaitSelection);

        buttonsUI?.Refresh(CurrentState, selection.Count);
        OnSelectionChanged?.Invoke(selection);
    }

    private void Update()
    {
        if (!IsCommandModeActive())
        {
            if (CurrentState != State.Inactive)
                ExitCommandMode();
            return;
        }

        // If we require command cam, enforce it
        if (requireCommandCamEnabled && (commandCam == null || !commandCam.enabled))
            return;

        // Ignore clicks over UI (icons/buttons handle themselves)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (commandCam == null)
            return;

        Ray r = commandCam.ScreenPointToRay(Input.mousePosition);

        // Move targeting: click ground to set destination
        if (CurrentState == State.MoveTargeting)
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, groundMask))
                {
                    // Notify UI about the chosen destination (even if the move is queued)
                    OnMoveTargetChosen?.Invoke(selection, hit.point);

                    if (queueMovesInCommandMode && CommandQueue.Instance != null)
                        CommandQueue.Instance.EnqueueMove(selection, hit.point);
                    else
                        OnMoveRequested?.Invoke(selection, hit.point);

                    if (MoveDestinationMarkerSystem.Instance != null)
                        MoveDestinationMarkerSystem.Instance.PlaceFor(selection.ToArray(), hit.point);

                    if (!keepSelectionAfterMove)
                    {
                        ClearSelectionInternal();
                    }
                    // Always leave targeting mode after confirming the move.
                    // We return to AwaitSelection so other UIs don\'t immediately pop the panel back up.
                    SetState(State.AwaitSelection);
                    buttonsUI?.Refresh(CurrentState, selection.Count);
                }
            }
            return;
        }

        // Add targeting (join): click a unit as the target
        if (CurrentState == State.AddTargeting)
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (TryGetClickedUnit(r, out GameObject clicked))
                {
                    bool wasJoin = JoinArmed;

                    OnAddRequested?.Invoke(selection, clicked);

                    CancelJoin();

                    if (!wasJoin)
                    {
                        ClearSelectionInternal();
                        SetState(State.AwaitSelection);
                        buttonsUI?.Refresh(CurrentState, selection.Count);
                    }
                    else
                    {
                        // remain selected after join target chosen
                        SetState(selection.Count > 0 ? State.UnitSelected : State.AwaitSelection);
                        buttonsUI?.Refresh(CurrentState, selection.Count);
                    }
                }
            }
            return;
        }

        // Normal selection: click a unit, or click empty to clear
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetClickedUnit(r, out GameObject unitGO))
            {
                SelectUnit(unitGO, additive: false);
                return;
            }

            ClearSelectionInternal();
            CancelJoin();
            SetState(State.AwaitSelection);
            buttonsUI?.Refresh(CurrentState, selection.Count);
        }
    }

    private bool IsCommandModeActive()
    {
        if (CommandCamToggle.Instance != null)
            return CommandCamToggle.Instance.IsCommandMode;

        return true;
    }

    private bool TryGetClickedUnit(Ray r, out GameObject unitGO)
    {
        unitGO = null;

        if (!Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, selectableMask.value != 0 ? selectableMask : ~0))
            return false;

        var go = hit.collider.gameObject;

        if (go.CompareTag("Ally")) { unitGO = go; return true; }
        if (allowEnemySelect && go.CompareTag("Enemy")) { unitGO = go; return true; }
        if (allowBossSelect && go.CompareTag("Boss")) { unitGO = go; return true; }

        return false;
    }

    private void EnterCommandMode()
    {
        if (CurrentState == State.Inactive)
        {
            SetState(State.AwaitSelection);
            buttonsUI?.Refresh(CurrentState, selection.Count);
        }
    }

    private void ExitCommandMode()
    {
        CancelJoin();
        ClearSelectionInternal();
        SetState(State.Inactive);
        buttonsUI?.Refresh(CurrentState, selection.Count);
    }

    private bool IsUnitBusyMoving(GameObject unit)
    {
        if (unit == null) return false;
        if (!unit.CompareTag("Ally")) return false;

        var agent = unit.GetComponent<NavMeshAgent>();
        if (agent == null) agent = unit.GetComponentInChildren<NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled) return false;

        // If path is still being computed, treat as busy
        if (agent.pathPending) return true;

        // If no path and not moving, not busy
        if (!agent.hasPath && agent.velocity.sqrMagnitude < 0.01f) return false;

        float remain = agent.remainingDistance;

        // remainingDistance can be Infinity/NaN sometimes; fall back to velocity
        if (float.IsInfinity(remain) || float.IsNaN(remain))
            return agent.velocity.sqrMagnitude >= 0.01f;

        float threshold = Mathf.Max(agent.stoppingDistance, 0.05f) + moveOrderBusyStopBuffer;
        return remain > threshold;
    }

    // ---------- selection ----------
    public void SelectUnit(GameObject unit, bool additive = false)
    {
        if (unit == null) return;

        // ✅ Block selecting ally if it's in-route to join
        if (blockJoinLeaderWhileInRoute && unit.CompareTag("Ally"))
        {
            var marker = unit.GetComponent<JoinRouteMarker>();
            if (marker != null && marker.inRoute)
            {
                TryShowHint(joinInRouteMessage, joinInRouteDuration);
                return;
            }
        }

        // ✅ Block selecting ally if it's currently executing a MOVE order
        if (blockMoveOrderedUnitsWhileBusy && IsUnitBusyMoving(unit))
        {
            TryShowHint(moveOrderBusyMessage, moveOrderBusyDuration);
            return;
        }

        if (!allowEnemySelect && unit.CompareTag("Enemy")) return;
        if (!allowBossSelect && unit.CompareTag("Boss")) return;

        if (!allowMultiSelect || !additive)
            selection.Clear();

        if (!selection.Contains(unit))
            selection.Add(unit);

        SetState(State.UnitSelected);

        buttonsUI?.Refresh(CurrentState, selection.Count);
        OnSelectionChanged?.Invoke(selection);
    }

    // ✅ IMPORTANT: this is what UI icon clicks should call
    public void ClickUnitFromUI(GameObject clickedUnit)
    {
        if (clickedUnit == null) return;

        // ✅ Block selecting ally if it's in-route to join (ICON clicks too)
        if (blockJoinLeaderWhileInRoute && clickedUnit.CompareTag("Ally"))
        {
            var marker = clickedUnit.GetComponent<JoinRouteMarker>();
            if (marker != null && marker.inRoute)
            {
                TryShowHint(joinInRouteMessage, joinInRouteDuration);
                return;
            }
        }

        // ✅ Block selecting ally if it's currently executing a MOVE order (ICON clicks too)
        if (blockMoveOrderedUnitsWhileBusy && IsUnitBusyMoving(clickedUnit))
        {
            TryShowHint(moveOrderBusyMessage, moveOrderBusyDuration);
            return;
        }

        if (CurrentState == State.AddTargeting)
        {
            bool wasJoin = JoinArmed;

            OnAddRequested?.Invoke(selection, clickedUnit);

            CancelJoin();

            if (!wasJoin)
            {
                ClearSelectionInternal();
                SetState(State.AwaitSelection);
            }
            else
            {
                SetState(selection.Count > 0 ? State.UnitSelected : State.AwaitSelection);
            }

            buttonsUI?.Refresh(CurrentState, selection.Count);
            return;
        }

        SelectUnit(clickedUnit, additive: false);
    }

    // ---------- buttons ----------
    public void ArmMoveFromCurrentSelection()
    {
        if (selection.Count == 0)
        {
            TryShowHint("Select a Unit or Ally", 2f);
            return;
        }

        SetState(State.MoveTargeting);
        buttonsUI?.Refresh(CurrentState, selection.Count);
    }

    public void ArmJoinFromCurrentSelection()
    {
        if (selection.Count == 0)
        {
            TryShowHint("Select a Unit or Ally", 2f);
            return;
        }

        JoinArmed = true;
        JoinSource = PrimarySelected;

        SetState(State.AddTargeting);
        buttonsUI?.Refresh(CurrentState, selection.Count);

        Debug.Log($"[Join] Armed from: {(JoinSource != null ? JoinSource.name : "NULL")}");
    }

    public void CancelJoin()
    {
        JoinArmed = false;
        JoinSource = null;

        if (CurrentState == State.AddTargeting)
        {
            SetState(selection.Count > 0 ? State.UnitSelected : State.AwaitSelection);
            buttonsUI?.Refresh(CurrentState, selection.Count);
        }
    }

    public void ClearSelection()
    {
        CancelJoin();
        ClearSelectionInternal();
        SetState(State.AwaitSelection);
        buttonsUI?.Refresh(CurrentState, selection.Count);
    }

    private void ClearSelectionInternal()
    {
        selection.Clear();
        OnSelectionChanged?.Invoke(selection);
    }

    // ---------- Hint helper ----------
    private void TryShowHint(string message, float duration)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            Type t = FindType("HintToast") ?? FindType("HintSystem");
            if (t == null)
            {
                Debug.Log(message);
                return;
            }

            // static Show(string, float)
            var m2 = t.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(float) }, null);
            if (m2 != null) { m2.Invoke(null, new object[] { message, duration }); return; }

            // static Show(string)
            var m1 = t.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (m1 != null) { m1.Invoke(null, new object[] { message }); return; }

            Debug.Log(message);
        }
        catch
        {
            Debug.Log(message);
        }
    }

    private static Type FindType(string typeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var tt = assemblies[i].GetType(typeName);
            if (tt != null) return tt;
        }
        return null;
    }

    private void SetState(State s) => CurrentState = s;
}
