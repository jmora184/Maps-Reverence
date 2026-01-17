
using System.Collections.Generic;
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

    [Tooltip("Pinned markers remain until you leave Command Mode.")]
    public bool keepPinnedUntilExitCommandMode = true;

    [Tooltip("If true, pinned markers hide once the unit arrives.")]
    public bool hidePinnedWhenArrived = false;

    [Tooltip("Arrival threshold (only used if hidePinnedWhenArrived = true).")]
    public float arriveDistance = 0.5f;

    [Header("Placement")]
    public float heightOffset = 0.03f;
    public Vector3 flatEuler = new Vector3(90f, 0f, 0f);

    [Header("Visual")]
    [Tooltip("Uniform scale applied to spawned markers (1 = prefab scale).")]
    public float markerScale = 1f;

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
        {
            holder = new GameObject("MarkerHolder");
            holder.transform.SetParent(transform, false);
        }

        markerParent = holder.transform;
    }

    private void ApplyMarkerScale(Transform t)
    {
        if (t == null) return;
        if (markerScale <= 0f) markerScale = 1f;
        t.localScale = Vector3.one * markerScale;
    }

    private void Update()
    {
        bool inCommandMode = IsInCommandMode();

        // Gate by command mode
        if (onlyShowInCommandMode && !inCommandMode)
        {
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);

            // Clear pinned only ONCE when transitioning from command mode -> not command mode.
            if (keepPinnedUntilExitCommandMode)
            {
                if (lastInCommandMode)
                    ClearAllPinned();
            }
            else
            {
                HideAllPinned();
            }

            lastInCommandMode = inCommandMode;
            return;
        }

        lastInCommandMode = inCommandMode;

        UpdateHoverMarker();
        UpdatePinnedMarkers();
    }

    // Called by CommandStateMachine when a move is confirmed
    public void PlaceFor(GameObject[] units, Vector3 destination)
    {
        if (units == null || units.Length == 0 || markerPrefab == null) return;

        EnsureMarkerParent();

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

    private void ClearAllPinned()
    {
        foreach (var kvp in pinnedByUnit)
        {
            if (kvp.Value?.marker != null)
                Destroy(kvp.Value.marker.gameObject);
        }
        pinnedByUnit.Clear();
    }

    private void HideAllPinned()
    {
        foreach (var kvp in pinnedByUnit)
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
