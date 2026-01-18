using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

public class MoveDestinationMarkerSystem : MonoBehaviour
{
    public static MoveDestinationMarkerSystem Instance { get; private set; }

    [Header("Refs")]
    public Camera commandCam;
    public CommandStateMachine stateMachine;

    [Header("Prefabs")]
    [Tooltip("A world-space marker prefab (SpriteRenderer on a GameObject).")]
    public SpriteRenderer markerPrefab;

    [Tooltip("Optional parent for spawned markers (keeps Hierarchy clean). If empty, a MarkerHolder will be created automatically.")]
    public Transform markerParent;

    [Header("Raycast")]
    public LayerMask groundMask;
    public float raycastMaxDistance = 5000f;

    [Header("Behavior")]
    public bool onlyShowInCommandMode = true;

    [Tooltip("Hover marker follows mouse only while StateMachine is MoveTargeting.")]
    public bool showHoverWhileMoveTargeting = true;

    [Tooltip("If true, we clear pinned markers when exiting Command Mode. If false, markers are simply hidden and will reappear when re-entering Command Mode.")]
    public bool clearPinnedOnExitCommandMode = false;

    [Tooltip("If true, pinned markers hide once the unit arrives.")]
    public bool hidePinnedWhenArrived = false;

    [Header("Team pin lifetime")]
    [Tooltip("Team pins stay visible until a new team order replaces them OR the team arrives at the destination.")]
    public bool hideTeamPinnedWhenArrived = true;

    [Tooltip("If true, we destroy & forget the Team pin marker on arrival (recommended).")]
    public bool destroyTeamPinOnArrive = true;

    [Tooltip("Arrival threshold (only used if hidePinnedWhenArrived = true).")]
    public float arriveDistance = 0.5f;

    [Header("Team marker")]
    [Tooltip("Arrival threshold used for Team markers (world units).")]
    public float teamArriveDistance = 1.0f;

    [Header("Placement")]
    public float heightOffset = 0.03f;
    public Vector3 flatEuler = new Vector3(90f, 0f, 0f);

    [Header("Visual")]
    [Tooltip("Uniform scale applied to spawned markers (1 = prefab scale).")]
    public float markerScale = 1f;

    [Header("Labels")]
    [Tooltip("If true, pins will set a label (via DestinationMarkerLabel) to show who the pin belongs to.")]
    public bool showLabels = true;

    [Tooltip("If true, the hover marker will show a label too.")]
    public bool labelHoverMarker = false;

    [Tooltip("Text to use for the hover marker label (only if labelHoverMarker is true).")]
    public string hoverMarkerLabelText = "MOVE";

    [Tooltip("If true, when the selected unit(s) belong to a Team, we will prefer planting a single TEAM pin and label it with the Team name (instead of labeling as Ally 7/Ally 8).")]
    public bool preferTeamPinsForTeamedUnits = true;

    // ---- internals ----
    private SpriteRenderer hoverMarker;

    private class Pinned
    {
        public Transform unit;
        public NavMeshAgent agent;
        public SpriteRenderer marker;
        public Vector3 destination;
    }

    private readonly Dictionary<Transform, Pinned> pinnedByUnit = new();

    private class PinnedTeam
    {
        public Team team;
        public SpriteRenderer marker;
        public Vector3 destination;
    }

    private readonly Dictionary<int, PinnedTeam> pinnedByTeamId = new();
    private bool lastInCommandMode = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;

        if (commandCam == null && CommandCamToggle.Instance != null)
            commandCam = CommandCamToggle.Instance.commandCam;

        if (stateMachine == null)
            stateMachine = FindObjectOfType<CommandStateMachine>();

        EnsureMarkerParent();

        // Create a hover marker instance right away (so it appears instantly)
        if (markerPrefab != null)
        {
            hoverMarker = Instantiate(markerPrefab, markerParent);
            hoverMarker.name = "MouseHoverMarker";
            hoverMarker.gameObject.SetActive(false);
            hoverMarker.transform.rotation = Quaternion.Euler(flatEuler);
            ApplyMarkerScale(hoverMarker.transform);

            if (showLabels)
                ApplyLabel(hoverMarker, labelHoverMarker ? hoverMarkerLabelText : string.Empty);
        }
        else
        {
            Debug.LogWarning("MoveDestinationMarkerSystem: markerPrefab is not assigned.");
        }

        lastInCommandMode = IsInCommandMode();
    }

    private void EnsureMarkerParent()
    {
        if (markerParent != null) return;

        // Auto-create a holder to keep hierarchy clean.
        var holder = GameObject.Find("MarkerHolder");
        if (holder == null)
            holder = new GameObject("MarkerHolder");

        // IMPORTANT: Keep MarkerHolder at scene root so markers don't inherit movement
        // from GameManagers / units / cameras that might be parented under something moving.
        holder.transform.SetParent(null);

        markerParent = holder.transform;
    }

    private void ApplyMarkerScale(Transform t)
    {
        if (t == null) return;
        if (markerScale <= 0f) markerScale = 1f;
        t.localScale = Vector3.one * markerScale;
    }

    private void ApplyLabel(SpriteRenderer marker, string text)
    {
        if (!showLabels || marker == null) return;

        // Label component can be on the root or a child.
        var label = marker.GetComponent<DestinationMarkerLabel>();
        if (label == null)
            label = marker.GetComponentInChildren<DestinationMarkerLabel>(true);

        if (label != null)
            label.SetText(text);
    }

    private Team GetTeamOf(Transform unit)
    {
        if (unit == null) return null;
        if (TeamManager.Instance == null) return null;
        return TeamManager.Instance.GetTeamOf(unit);
    }

    private void Update()
    {
        bool inCommandMode = IsInCommandMode();

        // Gate by command mode
        if (onlyShowInCommandMode && !inCommandMode)
        {
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);

            // Hide pins while out of Command Mode, but DON'T destroy them.
            // This makes pins persist across enter/exit Command Mode (your desired behavior).
            HideAllPinned();

            // Optional: if you really want pins cleared on exit, enable this.
            if (clearPinnedOnExitCommandMode && lastInCommandMode)
                ClearAllPinned();

            lastInCommandMode = inCommandMode;
            return;
        }

        lastInCommandMode = inCommandMode;

        UpdateHoverMarker();
        UpdatePinnedMarkers();
        UpdatePinnedTeamMarkers();
    }

    // ---- Team method compatibility (avoids hard dependency on Team API) ----

    private static bool TryInvokeTeamMethod(Team team, string methodName, params object[] args)
    {
        if (team == null) return false;

        try
        {
            var t = team.GetType();
            var mi = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return false;
            mi.Invoke(team, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TryGetTeamString(Team team)
    {
        if (team == null) return "Team";

        try
        {
            var t = team.GetType();

            // Common patterns: team.Name / team.DisplayName
            var pName = t.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pName != null && pName.PropertyType == typeof(string))
            {
                var val = pName.GetValue(team) as string;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            var pDisplay = t.GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pDisplay != null && pDisplay.PropertyType == typeof(string))
            {
                var val = pDisplay.GetValue(team) as string;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            // Field fallback
            var fName = t.GetField("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fName != null && fName.FieldType == typeof(string))
            {
                var val = fName.GetValue(team) as string;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        catch { }

        return $"Team {team.Id}";
    }

    // Called by CommandStateMachine when a TEAM move is confirmed
    public void PlaceForTeam(Team team, Vector3 destination)
    {
        if (team == null || markerPrefab == null) return;
        EnsureMarkerParent();

        // Store on the Team too (optional; some projects may not have this API)
        TryInvokeTeamMethod(team, "SetMoveTarget", destination);

        if (!pinnedByTeamId.TryGetValue(team.Id, out var pinned) || pinned == null || pinned.marker == null)
        {
            var sr = Instantiate(markerPrefab, markerParent);
            sr.name = $"PinnedTeamMarker_{team.Id}";
            sr.transform.rotation = Quaternion.Euler(flatEuler);
            ApplyMarkerScale(sr.transform);

            pinned = new PinnedTeam
            {
                team = team,
                marker = sr,
                destination = destination
            };

            pinnedByTeamId[team.Id] = pinned;
        }
        else
        {
            pinned.team = team;
            pinned.destination = destination;
        }

        pinned.marker.transform.position = destination + Vector3.up * heightOffset;
        pinned.marker.gameObject.SetActive(true);

        ApplyLabel(pinned.marker, TryGetTeamString(team));
    }

    /// <summary>
    /// Clear (destroy) the pinned destination marker for a Team immediately.
    /// Use this when a Team commits a new non-move action (ex: Join) so the last move pin doesn't linger.
    /// </summary>
    public void ClearForTeam(Team team)
    {
        if (team == null) return;

        // Clear any remembered move target on the Team too (optional).
        TryInvokeTeamMethod(team, "ClearMoveTarget");

        if (pinnedByTeamId.TryGetValue(team.Id, out var pinned) && pinned != null)
        {
            if (pinned.marker != null)
                Destroy(pinned.marker.gameObject);

            pinnedByTeamId.Remove(team.Id);
        }
    }

    /// <summary>
    /// Clear pinned markers for specific units (non-team pins).
    /// </summary>
    public void ClearForUnits(GameObject[] units)
    {
        if (units == null || units.Length == 0) return;

        for (int i = 0; i < units.Length; i++)
        {
            var go = units[i];
            if (go == null) continue;

            var tr = go.transform;
            if (tr == null) continue;

            if (pinnedByUnit.TryGetValue(tr, out var pinned) && pinned != null)
            {
                if (pinned.marker != null)
                    Destroy(pinned.marker.gameObject);
                pinnedByUnit.Remove(tr);
            }
        }
    }


    private bool TryGetCommonTeam(GameObject[] units, out Team team)
    {
        team = null;
        if (units == null || units.Length == 0) return false;

        for (int i = 0; i < units.Length; i++)
        {
            var go = units[i];
            if (go == null) return false;

            var t = go.transform;
            if (t == null) return false;

            var thisTeam = GetTeamOf(t);
            if (thisTeam == null) return false;

            if (team == null)
            {
                team = thisTeam;
            }
            else
            {
                // Prefer reference equality, but also allow same Id.
                if (!ReferenceEquals(team, thisTeam) && team.Id != thisTeam.Id)
                    return false;
            }
        }

        return team != null;
    }

    // Called by CommandStateMachine when a move is confirmed
    public void PlaceFor(GameObject[] units, Vector3 destination)
    {
        if (units == null || units.Length == 0 || markerPrefab == null) return;

        EnsureMarkerParent();

        // If the user selected the TEAM star (or a team selection), the state machine may pass
        // one or more member GameObjects here. That can cause the pin label to alternate
        // between "Ally 7" and "Ally 8" depending on which member is first.
        // Prefer a single TEAM pin with the Team name in that case.
        if (preferTeamPinsForTeamedUnits && TryGetCommonTeam(units, out Team commonTeam) && commonTeam != null)
        {
            bool treatAsTeam = units.Length > 1;

            // If only one unit was passed but that unit belongs to a multi-member team, still treat as Team.
            try
            {
                if (!treatAsTeam && commonTeam.Members != null && commonTeam.Members.Count > 1)
                    treatAsTeam = true;
            }
            catch { }

            if (treatAsTeam)
            {
                PlaceForTeam(commonTeam, destination);

                // Clear any per-unit pins for these members to avoid clutter/confusion.
                ClearForUnits(units);
                return;
            }
        }

        foreach (var go in units)
        {
            if (go == null) continue;

            Transform t = go.transform;
            if (!pinnedByUnit.TryGetValue(t, out var pinned) || pinned.marker == null)
            {
                var sr = Instantiate(markerPrefab, markerParent);
                sr.name = $"PinnedMarker_{t.name}";
                sr.transform.rotation = Quaternion.Euler(flatEuler);
                ApplyMarkerScale(sr.transform);

                pinned = new Pinned
                {
                    unit = t,
                    agent = go.GetComponent<NavMeshAgent>(),
                    marker = sr,
                    destination = destination
                };

                pinnedByUnit[t] = pinned;
            }
            else
            {
                pinned.destination = destination;

                if (pinned.agent == null)
                    pinned.agent = go.GetComponent<NavMeshAgent>();
            }

            pinned.marker.transform.position = destination + Vector3.up * heightOffset;
            pinned.marker.gameObject.SetActive(true);

            ApplyLabel(pinned.marker, t != null ? t.name : "Unit");
        }
    }

    private void UpdateHoverMarker()
    {
        if (hoverMarker == null) return;

        if (!showHoverWhileMoveTargeting || stateMachine == null || commandCam == null)
        {
            hoverMarker.gameObject.SetActive(false);
            return;
        }

        bool inMoveTargeting = stateMachine.CurrentState == CommandStateMachine.State.MoveTargeting;
        if (!inMoveTargeting)
        {
            hoverMarker.gameObject.SetActive(false);
            return;
        }

        Ray r = commandCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, groundMask))
        {
            hoverMarker.gameObject.SetActive(true);
            hoverMarker.transform.position = hit.point + Vector3.up * heightOffset;

            if (showLabels)
                ApplyLabel(hoverMarker, labelHoverMarker ? hoverMarkerLabelText : string.Empty);
        }
        else
        {
            hoverMarker.gameObject.SetActive(false);
        }
    }

    private void UpdatePinnedMarkers()
    {
        List<Transform> dead = null;

        foreach (var kvp in pinnedByUnit)
        {
            var unit = kvp.Key;
            var pinned = kvp.Value;

            if (unit == null || pinned == null || pinned.marker == null)
            {
                dead ??= new List<Transform>();
                dead.Add(unit);
                continue;
            }

            pinned.marker.transform.position = pinned.destination + Vector3.up * heightOffset;

            // Keep label correct (in case names change at runtime)
            ApplyLabel(pinned.marker, unit.name);

            if (!hidePinnedWhenArrived)
            {
                pinned.marker.gameObject.SetActive(true);
                continue;
            }

            if (pinned.agent != null)
            {
                bool hasPath = pinned.agent.hasPath && !pinned.agent.pathPending;
                float remaining = hasPath ? pinned.agent.remainingDistance : Mathf.Infinity;
                bool arrived = remaining <= arriveDistance;

                pinned.marker.gameObject.SetActive(!arrived);
            }
            else
            {
                pinned.marker.gameObject.SetActive(true);
            }
        }

        if (dead != null)
        {
            foreach (var t in dead)
            {
                if (t != null && pinnedByUnit.TryGetValue(t, out var p) && p.marker != null)
                    Destroy(p.marker.gameObject);

                pinnedByUnit.Remove(t);
            }
        }
    }

    private void UpdatePinnedTeamMarkers()
    {
        if (pinnedByTeamId.Count == 0) return;

        List<int> dead = null;

        foreach (var kvp in pinnedByTeamId)
        {
            int teamId = kvp.Key;
            var pinned = kvp.Value;

            if (pinned == null || pinned.marker == null || pinned.team == null)
            {
                dead ??= new List<int>();
                dead.Add(teamId);
                continue;
            }

            pinned.marker.transform.position = pinned.destination + Vector3.up * heightOffset;

            // Keep label correct
            ApplyLabel(pinned.marker, TryGetTeamString(pinned.team));

            // Team pins have their own lifetime rule (separate from single-unit pins).
            if (!hideTeamPinnedWhenArrived)
            {
                pinned.marker.gameObject.SetActive(true);
                continue;
            }

            bool arrived = !IsTeamStillMovingToward(pinned.team, pinned.destination);

            if (!arrived)
            {
                pinned.marker.gameObject.SetActive(true);
                continue;
            }

            // Arrived: clear the Team's remembered move target and optionally remove the pin.
            TryInvokeTeamMethod(pinned.team, "ClearMoveTarget");

            if (destroyTeamPinOnArrive)
            {
                dead ??= new List<int>();
                dead.Add(teamId);
            }
            else
            {
                pinned.marker.gameObject.SetActive(false);
            }
        }

        if (dead != null)
        {
            for (int i = 0; i < dead.Count; i++)
            {
                int id = dead[i];
                if (pinnedByTeamId.TryGetValue(id, out var p) && p != null)
                {
                    if (p.marker != null) Destroy(p.marker.gameObject);
                    if (p != null) TryInvokeTeamMethod(p.team, "ClearMoveTarget");
                }
                pinnedByTeamId.Remove(id);
            }
        }
    }

    private bool IsTeamStillMovingToward(Team team, Vector3 destination)
    {
        if (team == null) return false;

        Transform anchor = team.Anchor != null ? team.Anchor : (team.Members != null && team.Members.Count > 0 ? team.Members[0] : null);
        if (anchor == null) return false;

        if (Vector3.Distance(anchor.position, destination) <= Mathf.Max(0.1f, teamArriveDistance))
            return false;

        var agent = anchor.GetComponent<NavMeshAgent>();
        if (agent == null) agent = anchor.GetComponentInChildren<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            if (agent.pathPending) return true;
            if (agent.hasPath && agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.05f) + 0.2f)
                return true;
        }

        if (team.Members != null)
        {
            for (int i = 0; i < team.Members.Count; i++)
            {
                var m = team.Members[i];
                if (m == null) continue;
                if (Vector3.Distance(m.position, destination) > Mathf.Max(0.1f, teamArriveDistance) + 0.25f)
                    return true;
            }
        }

        return false;
    }

    private void ClearAllPinned()
    {
        foreach (var kvp in pinnedByUnit)
        {
            if (kvp.Value?.marker != null)
                Destroy(kvp.Value.marker.gameObject);
        }
        pinnedByUnit.Clear();

        foreach (var kvp in pinnedByTeamId)
        {
            if (kvp.Value?.marker != null)
                Destroy(kvp.Value.marker.gameObject);
            if (kvp.Value != null) TryInvokeTeamMethod(kvp.Value.team, "ClearMoveTarget");
        }
        pinnedByTeamId.Clear();
    }

    private void HideAllPinned()
    {
        foreach (var kvp in pinnedByUnit)
        {
            if (kvp.Value?.marker != null)
                kvp.Value.marker.gameObject.SetActive(false);
        }

        foreach (var kvp in pinnedByTeamId)
        {
            if (kvp.Value?.marker != null)
                kvp.Value.marker.gameObject.SetActive(false);
        }
    }

    private bool IsInCommandMode()
    {
        // Prefer CommandCamToggle when available, but don't let it hide pins if cameras/states disagree.
        bool toggleSays = false;
        if (CommandCamToggle.Instance != null)
            toggleSays = CommandCamToggle.Instance.IsCommandMode;

        bool camSays = (commandCam != null && commandCam.enabled);

        bool stateSays = (stateMachine != null && stateMachine.CurrentState != CommandStateMachine.State.Inactive);

        return toggleSays || camSays || stateSays;
    }
}
