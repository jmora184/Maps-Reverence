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

    [Header("Inactive Ally Hint")]
    [Tooltip("If true, inactive allies (AllyActivationGate.IsActive == false) cannot be selected/commanded in command mode.")]
    public bool blockInactiveAllies = true;
    [TextArea] public string inactiveAllyMessage = "Ally is inactive (approach and press J)";
    public float inactiveAllyDuration = 2.0f;


    public State CurrentState { get; private set; } = State.Inactive;

    private readonly List<GameObject> selection = new();
    public IReadOnlyList<GameObject> CurrentSelection => selection;
    public GameObject PrimarySelected => selection.Count > 0 ? selection[0] : null;

    // Join state (used by Executor when AddTargeting is join)
    public bool JoinArmed { get; private set; }
    public GameObject JoinSource { get; private set; }

    public event Action<IReadOnlyList<GameObject>, Vector3> OnMoveRequested;
    public event Action<IReadOnlyList<GameObject>, GameObject> OnFollowRequested;
    // Fires whenever the player clicks a ground point in MoveTargeting (even if moves are queued)
    public event Action<IReadOnlyList<GameObject>, Vector3> OnMoveTargetChosen;
    public event Action<IReadOnlyList<GameObject>, GameObject> OnAddRequested;
    public event Action<IReadOnlyList<GameObject>> OnSplitRequested;
    public event Action<IReadOnlyList<GameObject>> OnSelectionChanged;

    // ---- Team pin helpers ----
    // We place Team destination pins as either:
    //  1) a dedicated Team pin (PlaceForTeam / ClearForTeam)
    //  2) OR (older behavior) a per-member pin (PlaceFor / ClearForUnits).
    //
    // To be safe, whenever a Team commits a new non-move action (Join, etc.),
    // we clear BOTH the Team pin and any per-member pins that might exist.

    private void ClearAllPinsForTeam(Team team)
    {
        if (team == null) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;

        // Clear dedicated Team pin (if used)
        MoveDestinationMarkerSystem.Instance.ClearForTeam(team);

        // Also clear any per-member pins (if selection-based PlaceFor was used)
        if (team.Members == null || team.Members.Count == 0) return;

        var gos = new List<GameObject>(team.Members.Count);
        for (int i = 0; i < team.Members.Count; i++)
        {
            var m = team.Members[i];
            if (m == null) continue;
            gos.Add(m.gameObject);
        }

        if (gos.Count > 0)
            MoveDestinationMarkerSystem.Instance.ClearForUnits(gos.ToArray());
    }

    private void ClearTeamPinsForSelection(IReadOnlyList<GameObject> units)
    {
        if (units == null || units.Count == 0) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;
        if (TeamManager.Instance == null) return;

        // Clear each affected team's pins (dedupe by team Id)
        HashSet<int> cleared = null;
        for (int i = 0; i < units.Count; i++)
        {
            var go = units[i];
            if (go == null) continue;

            var team = TeamManager.Instance.GetTeamOf(go.transform);
            if (team == null) continue;

            cleared ??= new HashSet<int>();
            if (!cleared.Add(team.Id)) continue;

            ClearAllPinsForTeam(team);
        }
    }

    private void ClearTeamPinsForTransforms(params Transform[] units)
    {
        if (units == null || units.Length == 0) return;
        if (MoveDestinationMarkerSystem.Instance == null) return;
        if (TeamManager.Instance == null) return;

        HashSet<int> cleared = null;
        for (int i = 0; i < units.Length; i++)
        {
            var tr = units[i];
            if (tr == null) continue;

            var team = TeamManager.Instance.GetTeamOf(tr);
            if (team == null) continue;

            cleared ??= new HashSet<int>();
            if (!cleared.Add(team.Id)) continue;

            ClearAllPinsForTeam(team);
        }
    }

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

    /// <summary>
    /// Used by UI code (CommandOverlayUI) to apply a selection while respecting the same blocking rules as SelectUnit().
    /// Key behavior: multi-unit (team) selections are allowed even if members are "busy moving" or "join in-route",
    /// so you can immediately redirect a newly-created team without needing to exit/re-enter command mode.
    /// </summary>
    public bool TrySetSelectionFromUI(List<GameObject> newSelection)
    {
        return TrySetSelectionFromUI(newSelection, allowBusyIfMulti: true);
    }

    /// <summary>
    /// Apply selection coming from UI. If allowBusyIfMulti is true, team selections (count>1) bypass busy/join blockers.
    /// </summary>
    public bool TrySetSelectionFromUI(List<GameObject> newSelection, bool allowBusyIfMulti)
    {
        if (newSelection == null || newSelection.Count == 0) return false;

        bool isMulti = newSelection.Count > 1;

        // Validate each unit against the same selection blockers used elsewhere.
        for (int i = 0; i < newSelection.Count; i++)
        {
            var unit = newSelection[i];
            if (unit == null) continue;

            // For team selections, allow immediate redirect even if the team is still forming / moving.
            if (allowBusyIfMulti && isMulti)
                continue;


            // Block selecting inactive allies (must be activated in-world first)
            if (blockInactiveAllies && IsInactiveAlly(unit))
            {
                TryShowHint(inactiveAllyMessage, inactiveAllyDuration);
                return false;
            }

            // Block selecting ally if it's in-route to join
            if (blockJoinLeaderWhileInRoute && unit.CompareTag("Ally"))
            {
                var marker = unit.GetComponent<JoinRouteMarker>();
                if (marker != null && marker.inRoute)
                {
                    TryShowHint(joinInRouteMessage, joinInRouteDuration);
                    return false;
                }
            }

            // Block selecting ally if it's currently executing a MOVE order
            if (blockMoveOrderedUnitsWhileBusy && IsUnitBusyMoving(unit))
            {
                TryShowHint(moveOrderBusyMessage, moveOrderBusyDuration);
                return false;
            }
        }

        SetSelection(newSelection);
        return true;
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
                    SubmitMoveTarget(hit.point);
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

                    // ✅ If we are executing a JOIN (team action), clear any existing team pin(s)
                    // so the destination marker doesn't linger after a different action is committed.
                    if (wasJoin)
                    {
                        // JoinSource = the unit that armed Join (often the team anchor)
                        ClearTeamPinsForTransforms(JoinSource != null ? JoinSource.transform : null,
                                                  clicked != null ? clicked.transform : null);
                    }

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


    /// <summary>
    /// Resolve a clicked collider to a unit root GameObject by walking up the hierarchy.
    /// This prevents "child collider not tagged" issues when using store-bought rigs/prefabs.
    /// </summary>
    private GameObject ResolveUnitRootFromCollider(Collider col)
    {
        if (col == null) return null;

        // Prefer components if present (more robust than tags alone)
        var ally = col.GetComponentInParent<AllyController>();
        if (ally != null) return ally.gameObject;

        var enemy = col.GetComponentInParent<Enemy2Controller>();
        if (enemy != null) return enemy.gameObject;

        // Fallback: climb by tag
        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag("Ally")) return t.gameObject;
            if (allowEnemySelect && t.CompareTag("Enemy")) return t.gameObject;
            if (allowBossSelect && t.CompareTag("Boss")) return t.gameObject;
            t = t.parent;
        }

        return null;
    }


    private bool TryGetClickedUnit(Ray r, out GameObject unitGO)
    {
        unitGO = null;

        if (!Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, selectableMask.value != 0 ? selectableMask : ~0))
            return false;

        // IMPORTANT: Many prefabs have colliders on child bones/meshes with different tags.
        // Resolve to the actual unit root (AllyController/Enemy2Controller parent or tagged parent).
        var resolved = ResolveUnitRootFromCollider(hit.collider);
        if (resolved == null) return false;

        // Final tag gate (unit root should carry the proper tag).
        if (resolved.CompareTag("Ally")) { unitGO = resolved; return true; }
        if (allowEnemySelect && resolved.CompareTag("Enemy")) { unitGO = resolved; return true; }
        if (allowBossSelect && resolved.CompareTag("Boss")) { unitGO = resolved; return true; }

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


        // ✅ Patrolling units should remain selectable even though they are moving.
        var patrol = unit.GetComponent<AllyPatrolPingPong>();
        if (patrol == null) patrol = unit.GetComponentInChildren<AllyPatrolPingPong>();
        if (patrol != null && patrol.patrolEnabledOnStart)
            return false;

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

    private bool IsInactiveAlly(GameObject unit)
    {
        if (unit == null) return false;
        if (!unit.CompareTag("Ally")) return false;
        var gate = unit.GetComponent<AllyActivationGate>();
        return (gate != null && !gate.IsActive);
    }


    // ---------- selection ----------
    public void SelectUnit(GameObject unit, bool additive = false)
    {
        if (unit == null) return;

        // ✅ Block selecting inactive allies (must be activated in-world first)
        if (blockInactiveAllies && IsInactiveAlly(unit))
        {
            TryShowHint(inactiveAllyMessage, inactiveAllyDuration);
            return;
        }


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

        // ✅ Block selecting inactive allies from UI (icon clicks)
        if (blockInactiveAllies && IsInactiveAlly(clickedUnit))
        {
            TryShowHint(inactiveAllyMessage, inactiveAllyDuration);
            return;
        }


        bool joinTargeting = (CurrentState == State.AddTargeting && JoinArmed);

        // ✅ Block selecting ally if it's in-route to join (ICON clicks too)
        if (!joinTargeting && blockJoinLeaderWhileInRoute && clickedUnit.CompareTag("Ally"))
        {
            var marker = clickedUnit.GetComponent<JoinRouteMarker>();
            if (marker != null && marker.inRoute)
            {
                TryShowHint(joinInRouteMessage, joinInRouteDuration);
                return;
            }
        }

        // ✅ Block selecting ally if it's currently executing a MOVE order (ICON clicks too)
        if (!joinTargeting && blockMoveOrderedUnitsWhileBusy && IsUnitBusyMoving(clickedUnit))
        {
            TryShowHint(moveOrderBusyMessage, moveOrderBusyDuration);
            return;
        }

        // ✅ While choosing a MOVE destination, ignore unit icon clicks so selection doesn't change mid-order.
        // (Enemy icon clicks during MoveTargeting are handled via SubmitFollowTarget instead.)
        if (CurrentState == State.MoveTargeting)
            return;

        if (CurrentState == State.AddTargeting)
        {
            bool wasJoin = JoinArmed;

            // ✅ If we are executing a JOIN (team action), clear any existing team pin(s)
            if (wasJoin)
            {
                ClearTeamPinsForTransforms(JoinSource != null ? JoinSource.transform : null,
                                          clickedUnit != null ? clickedUnit.transform : null);
            }

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

    /// <summary>
    /// Confirm a MOVE target at the given world point. This is the same path used when clicking the ground
    /// in MoveTargeting, but can also be called by UI (ex: clicking an enemy icon).
    /// </summary>
    public void SubmitMoveTarget(Vector3 worldPoint)
    {
        if (CurrentState != State.MoveTargeting) return;
        if (selection.Count == 0) return;

        // If we are issuing a NON-attack command (move), clear any committed attack indicators
        // for these units so counts don't linger on enemies.
        if (AttackTargetIndicatorSystem.Instance != null)
            AttackTargetIndicatorSystem.Instance.UnregisterAttackers(selection);

        // Notify UI about the chosen destination (even if the move is queued)
        OnMoveTargetChosen?.Invoke(selection, worldPoint);

        if (queueMovesInCommandMode && CommandQueue.Instance != null)
            CommandQueue.Instance.EnqueueMove(selection, worldPoint);
        else
            OnMoveRequested?.Invoke(selection, worldPoint);

        if (MoveDestinationMarkerSystem.Instance != null)
        {
            // If the current selection represents an entire Team, store a Team move target (Option B)
            // so the Team Star can show a single direction arrow.
            Team selectedTeam = null;
            if (TeamManager.Instance != null && selection.Count > 0 && selection[0] != null)
                selectedTeam = TeamManager.Instance.GetTeamOf(selection[0].transform);

            if (selectedTeam != null && SelectionMatchesEntireTeam(selectedTeam, selection))
                MoveDestinationMarkerSystem.Instance.PlaceForTeam(selectedTeam, worldPoint);

            // Keep existing per-unit pins (older behavior)
            MoveDestinationMarkerSystem.Instance.PlaceFor(selection.ToArray(), worldPoint);
        }
if (!keepSelectionAfterMove)
            ClearSelectionInternal();

        // Always leave targeting mode after confirming the move.
        // We return to AwaitSelection so other UIs don't immediately pop the panel back up.
        SetState(State.AwaitSelection);
        buttonsUI?.Refresh(CurrentState, selection.Count);
    }


    /// <summary>
    /// Confirm a MOVE target as a FOLLOW order (dynamic). Used when clicking an enemy icon while in MoveTargeting.
    /// The executor will keep updating destinations to the target's current position until the target is destroyed
    /// or a new order is issued.
    /// </summary>
    public void SubmitFollowTarget(GameObject targetUnit)
    {
        if (CurrentState != State.MoveTargeting) return;
        if (selection.Count == 0) return;
        if (targetUnit == null) return;

        // Place a marker at the target's current projected ground position (visual feedback).
        Vector3 target = targetUnit.transform.position;
        Ray downRay = new Ray(target + Vector3.up * 50f, Vector3.down);
        if (Physics.Raycast(downRay, out RaycastHit hit, 200f, groundMask))
            target = hit.point;

        OnMoveTargetChosen?.Invoke(selection, target);

        // Follow is not queued (for now). It is an active order that tracks a moving target.
        OnFollowRequested?.Invoke(selection, targetUnit);

        if (MoveDestinationMarkerSystem.Instance != null)
            MoveDestinationMarkerSystem.Instance.PlaceFor(selection.ToArray(), target);

        if (!keepSelectionAfterMove)
            ClearSelectionInternal();

        // Leave targeting mode after confirming.
        SetState(State.AwaitSelection);
        buttonsUI?.Refresh(CurrentState, selection.Count);
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


    private static bool SelectionMatchesEntireTeam(Team team, IReadOnlyList<GameObject> selection)
    {
        if (team == null || team.Members == null) return false;
        if (selection == null) return false;

        // Fast lookup
        var set = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < selection.Count; i++)
        {
            var go = selection[i];
            if (go == null) continue;
            set.Add(go.transform);
        }

        int count = 0;
        for (int i = 0; i < team.Members.Count; i++)
        {
            var m = team.Members[i];
            if (m == null) continue;
            if (set.Contains(m)) count++;
        }

        // Only treat as "team move" when the whole team is selected
        return count > 0 && count == team.Members.Count;
    }

}