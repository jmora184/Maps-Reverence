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

    [Tooltip("Optional parent for spawned markers (keeps Hierarchy clean).")]
    public Transform markerParent;

    [Header("Raycast")]
    public LayerMask groundMask;
    public float raycastMaxDistance = 5000f;

    [Header("Behavior")]
    public bool onlyShowInCommandMode = true;

    [Tooltip("Hover marker follows mouse only while StateMachine is MoveTargeting.")]
    public bool showHoverWhileMoveTargeting = true;

    [Header("Hover Sprites")]
    [Tooltip("Sprite used for the hover marker during MoveTargeting (defaults to markerPrefab's sprite if left empty).")]
    public Sprite moveHoverSprite;

    [Tooltip("Sprite used when the mouse is hovering an enemy icon during MoveTargeting (optional).")]
    public Sprite attackHoverSprite;

    [Tooltip("If true, hovering an enemy icon switches the hover marker sprite to attackHoverSprite.")]
    public bool switchHoverSpriteWhenOverEnemy = true;


    [Tooltip("Pinned markers remain until you leave Command Mode (your request).")]
    public bool keepPinnedUntilExitCommandMode = true;

    [Tooltip("If true, pinned markers hide once the unit arrives. (OFF for your request)")]
    public bool hidePinnedWhenArrived = false;

    [Tooltip("Arrival threshold (only used if hidePinnedWhenArrived = true).")]
    public float arriveDistance = 0.5f;

    [Header("Placement")]
    public float heightOffset = 0.03f;
    public Vector3 flatEuler = new Vector3(90f, 0f, 0f);

    // ---- internals ----
    private SpriteRenderer hoverMarker;


    private bool hoverOverEnemyIcon;
    // per-unit pinned marker
    private class Pinned
    {
        public Transform unit;
        public NavMeshAgent agent;
        public SpriteRenderer marker;
        public Vector3 destination;
    }

    private readonly Dictionary<Transform, Pinned> pinnedByUnit = new();

    private void Awake()
    {
        Instance = this;

        if (commandCam == null && CommandCamToggle.Instance != null)
            commandCam = CommandCamToggle.Instance.commandCam;

        if (stateMachine == null)
            stateMachine = FindObjectOfType<CommandStateMachine>();

        // Create a hover marker instance right away (so it appears instantly)
        if (markerPrefab != null)
        {
            hoverMarker = Instantiate(markerPrefab, markerParent);
            hoverMarker.name = "MouseHoverMarker";
            hoverMarker.gameObject.SetActive(false);
            hoverMarker.transform.rotation = Quaternion.Euler(flatEuler);

            // Default moveHoverSprite to whatever the prefab had (so you only need to set attackHoverSprite).
            if (moveHoverSprite == null)
                moveHoverSprite = hoverMarker.sprite;

            ApplyHoverSprite();
        }
        else
        {
            Debug.LogWarning("MoveDestinationMarkerSystem: markerPrefab is not assigned.");
        }
    }

    private void Update()
    {
        // Gate by command mode
        if (onlyShowInCommandMode && !IsInCommandMode())
        {
            // Hide hover
            if (hoverMarker != null) hoverMarker.gameObject.SetActive(false);

            // Either hide or clear pinned markers when leaving command mode
            if (keepPinnedUntilExitCommandMode)
            {
                // Leaving command mode => clear pinned markers
                ClearAllPinned();
            }
            else
            {
                HideAllPinned();
            }

            return;
        }

        // Hover marker logic
        UpdateHoverMarker();

        // Pinned markers logic
        UpdatePinnedMarkers();
    }

    // Called by your CommandStateMachine when you click the ground
    public void PlaceFor(GameObject[] units, Vector3 destination)
    {
        if (units == null || units.Length == 0 || markerPrefab == null) return;

        foreach (var go in units)
        {
            if (go == null) continue;

            Transform t = go.transform;
            if (!pinnedByUnit.TryGetValue(t, out var pinned) || pinned.marker == null)
            {
                var sr = Instantiate(markerPrefab, markerParent);
                sr.name = $"PinnedMarker_{t.name}";
                sr.transform.rotation = Quaternion.Euler(flatEuler);

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

            // Show immediately at destination
            pinned.marker.transform.position = destination + Vector3.up * heightOffset;
            pinned.marker.gameObject.SetActive(true);
        }
    }



    /// <summary>
    /// Called by UI (enemy icons) to tell this system whether the mouse is currently hovering an enemy icon.
    /// While MoveTargeting, this will swap the hover marker sprite to the attack sprite (if assigned).
    /// </summary>
    public void SetHoverOverEnemyIcon(bool overEnemy)
    {
        hoverOverEnemyIcon = overEnemy;
        ApplyHoverSprite();
    }

    private void ApplyHoverSprite()
    {
        if (hoverMarker == null) return;
        if (!switchHoverSpriteWhenOverEnemy) return;

        // Prefer attack sprite when hovering an enemy icon.
        Sprite s = (hoverOverEnemyIcon && attackHoverSprite != null) ? attackHoverSprite : moveHoverSprite;
        if (s != null) hoverMarker.sprite = s;
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
            // Leaving move targeting => ensure we return to the move sprite next time.
            hoverOverEnemyIcon = false;
            ApplyHoverSprite();

            hoverMarker.gameObject.SetActive(false);
            return;
        }

        // While targeting, keep sprite in sync with UI hover.
        ApplyHoverSprite();

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
        // Clean up dead units / markers
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

            // Keep marker at the last ordered destination
            pinned.marker.transform.position = pinned.destination + Vector3.up * heightOffset;

            if (!hidePinnedWhenArrived)
            {
                pinned.marker.gameObject.SetActive(true);
                continue;
            }

            // Optional: hide when arrived
            if (pinned.agent != null)
            {
                bool hasPath = pinned.agent.hasPath && !pinned.agent.pathPending;
                float remaining = hasPath ? pinned.agent.remainingDistance : Mathf.Infinity;
                bool arrived = remaining <= arriveDistance;

                pinned.marker.gameObject.SetActive(!arrived);
            }
            else
            {
                // No agent => just keep visible
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
        if (CommandCamToggle.Instance != null)
            return CommandCamToggle.Instance.IsCommandMode;

        // fallback
        return commandCam != null && commandCam.enabled;
    }
}
