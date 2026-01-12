using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(SpriteRenderer))]
public class DirectionArrowFollower : MonoBehaviour
{
    [Header("Refs (bound by DirectionArrowSystem)")]
    public Transform unit;          // Unit transform this arrow belongs to
    public NavMeshAgent agent;      // Unit agent this arrow belongs to

    [Header("Camera / Ground")]
    public Camera commandCam;
    public LayerMask groundMask = ~0;
    public float raycastMaxDistance = 5000f;

    [Header("Command Mode Gate")]
    public bool onlyShowInCommandMode = true;

    [Header("Show planned destination while still in command mode")]
    [Tooltip("If true, and CommandQueue has a planned destination for this unit, we point to that plan (even if paused).")]
    public bool showPlannedDestinationFromQueue = true;

    [Header("Paused-path support (useful for Enemies)")]
    [Tooltip("If timeScale==0 and the agent has a path, still show the arrow even if remainingDistance isn't updating.")]
    public bool treatHasPathAsMovingWhenPaused = true;

    [Header("Preview coexistence (optional)")]
    [Tooltip("If true, hides this follower arrow for the selected unit while MoveTargeting so the Preview arrow can own it.")]
    public bool hideForSelectedWhileMoveTargeting = true;

    [Tooltip("Auto-found if null.")]
    public CommandStateMachine sm;

    [Header("Visibility")]
    public bool hideWhenNotMoving = true;
    public float showWhenRemainingDistanceAbove = 0.35f;

    [Header("Placement")]
    public float forwardOffset = 1.2f;
    public float heightOffset = 0.05f;

    [Header("Avoid UI/icons (screen space offset)")]
    public bool useScreenOffset = true;
    public Vector2 screenOffsetPixels = new Vector2(100f, 100f);

    [Header("Rotation")]
    public Vector3 fixedEuler = new Vector3(90f, 0f, 0f);
    public bool faceDestination = true;
    public float yawOffsetDegrees = 0f;

    [Header("Sorting (optional)")]
    public string sortingLayerName = "";
    public int orderInLayer = 5000;

    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;

        // Sorting (optional)
        if (sr != null)
        {
            if (!string.IsNullOrEmpty(sortingLayerName))
                sr.sortingLayerName = sortingLayerName;

            sr.sortingOrder = orderInLayer;
        }

        // Auto cam (optional)
        if (commandCam == null && CommandCamToggle.Instance != null)
            commandCam = CommandCamToggle.Instance.commandCam;

        if (sm == null)
            sm = FindObjectOfType<CommandStateMachine>();
    }

    /// <summary>
    /// Called by DirectionArrowSystem after spawning this arrow for a specific unit.
    /// NOTE: Keep unit/agent EMPTY on the prefab. Bind fills them per spawned instance.
    /// </summary>
    public void Bind(Transform unitTransform, NavMeshAgent navAgent)
    {
        unit = unitTransform;
        agent = navAgent;

        if (commandCam == null && CommandCamToggle.Instance != null)
            commandCam = CommandCamToggle.Instance.commandCam;

        if (sm == null)
            sm = FindObjectOfType<CommandStateMachine>();

        // Let LateUpdate decide final visibility
        if (sr != null) sr.enabled = true;
    }

    private void LateUpdate()
    {
        if (sr == null) return;

        // Keep sorting up-to-date if you tweak in inspector
        if (!string.IsNullOrEmpty(sortingLayerName))
            sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = orderInLayer;

        // Command mode gate
        if (onlyShowInCommandMode && CommandCamToggle.Instance != null && !CommandCamToggle.Instance.IsCommandMode)
        {
            sr.enabled = false;
            return;
        }

        if (unit == null || agent == null)
        {
            sr.enabled = false;
            return;
        }

        // Optional: if we’re in MoveTargeting, let Preview arrow own the selected unit
        if (hideForSelectedWhileMoveTargeting && sm != null &&
            sm.CurrentState == CommandStateMachine.State.MoveTargeting)
        {
            var primary = sm.PrimarySelected;
            if (primary != null && primary.transform == unit)
            {
                sr.enabled = false;
                return;
            }
        }

        // Choose destination:
        // - While in command mode, if CommandQueue has a planned destination for this unit, use it.
        // - Otherwise fallback to agent.destination
        Vector3 dest = agent.destination;
        bool hasPlanned = false;

        if (showPlannedDestinationFromQueue &&
            CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode &&
            CommandQueue.Instance != null)
        {
            if (CommandQueue.Instance.TryGetPlannedDestination(unit.gameObject, out Vector3 planned))
            {
                dest = planned;
                hasPlanned = true;
            }
        }

        // Determine "moving" from agent path
        bool hasPath = agent.hasPath && !agent.pathPending;
        float remaining = (hasPath && !float.IsInfinity(agent.remainingDistance)) ? agent.remainingDistance : 0f;

        bool moving = hasPath && remaining > showWhenRemainingDistanceAbove;

        // Key: if paused, we may still want arrows for units that *have a path*
        bool pausedPath = treatHasPathAsMovingWhenPaused && Time.timeScale == 0f && hasPath;

        bool shouldShow = moving || hasPlanned || pausedPath;

        if (hideWhenNotMoving)
            sr.enabled = shouldShow;
        else
            sr.enabled = true;

        if (!sr.enabled) return;

        // Direction
        Vector3 from = unit.position;
        Vector3 dir = dest - from;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            sr.enabled = false;
            return;
        }

        dir.Normalize();

        // Base world position: in front of unit in direction of destination
        Vector3 worldPos = from + dir * forwardOffset;
        worldPos.y = from.y + heightOffset;

        // Optional: apply screen offset then raycast back onto ground
        if (useScreenOffset && commandCam != null)
        {
            Vector3 screen = commandCam.WorldToScreenPoint(worldPos);
            screen.x += screenOffsetPixels.x;
            screen.y += screenOffsetPixels.y;

            Ray r = commandCam.ScreenPointToRay(screen);
            if (Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, groundMask))
            {
                worldPos = hit.point;
                worldPos.y = from.y + heightOffset;
            }
        }

        transform.position = worldPos;

        // Rotation
        if (faceDestination)
        {
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(fixedEuler.x, yaw + fixedEuler.y + yawOffsetDegrees, fixedEuler.z);
        }
        else
        {
            transform.rotation = Quaternion.Euler(fixedEuler);
        }
    }
}
