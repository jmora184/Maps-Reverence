using System;
using System.Reflection;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class DirectionArrowPreview : MonoBehaviour
{
    [Header("Refs")]
    public Camera commandCam;
    public CommandStateMachine stateMachine;

    [Tooltip("Optional explicit ref. If empty we auto-find a MonoBehaviour named 'CommandExecutor'.")]
    public MonoBehaviour executor;

    [Header("Raycast (same settings as your state machine)")]
    public LayerMask groundMask;
    public float raycastMaxDistance = 5000f;

    [Header("Show Rules")]
    public bool onlyInCommandMode = true;

    [Tooltip("Classic behavior: show only when state == MoveTargeting (mouse-hover preview).")]
    public bool onlyWhenMoveTargeting = true;

    [Tooltip("NEW: also show while join leader is walking to the join target.")]
    public bool showDuringJoinRoute = true;

    [Tooltip("If true, we only show join-route arrow when the currently selected unit is the join leader.")]
    public bool onlyShowJoinRouteWhenLeaderSelected = false;

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

    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;

        if (commandCam == null && CommandCamToggle.Instance != null)
            commandCam = CommandCamToggle.Instance.commandCam;

        if (stateMachine == null)
            stateMachine = FindObjectOfType<CommandStateMachine>();

        if (executor == null)
            executor = FindExecutorByName("CommandExecutor");
    }

    private void LateUpdate()
    {
        if (sr == null)
            return;

        // Gate: command mode
        if (onlyInCommandMode && CommandCamToggle.Instance != null && !CommandCamToggle.Instance.IsCommandMode)
        {
            sr.enabled = false;
            return;
        }

        if (commandCam == null) commandCam = Camera.main;
        if (commandCam == null)
        {
            sr.enabled = false;
            return;
        }

        // ---------- JOIN ROUTE ARROW (NEW) ----------
        if (showDuringJoinRoute && TryGetJoinRoute(out Transform leader, out Transform target))
        {
            if (leader != null && target != null)
            {
                if (onlyShowJoinRouteWhenLeaderSelected && stateMachine != null)
                {
                    var selected = stateMachine.PrimarySelected;
                    if (selected == null || selected.transform != leader)
                    {
                        // Not selected, don’t show in this mode
                        sr.enabled = false;
                        return;
                    }
                }

                ShowArrowFromTo(leader.position, target.position);
                return;
            }
        }

        // ---------- MOVE TARGETING PREVIEW (existing behavior) ----------
        if (stateMachine == null)
        {
            sr.enabled = false;
            return;
        }

        if (onlyWhenMoveTargeting && stateMachine.CurrentState != CommandStateMachine.State.MoveTargeting)
        {
            sr.enabled = false;
            return;
        }

        var selectedUnit = stateMachine.PrimarySelected;
        if (selectedUnit == null)
        {
            sr.enabled = false;
            return;
        }

        // Raycast mouse to ground to get hovered destination
        Ray r = commandCam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, groundMask))
        {
            sr.enabled = false;
            return;
        }

        ShowArrowFromTo(selectedUnit.transform.position, hit.point);
    }

    private void ShowArrowFromTo(Vector3 from, Vector3 dest)
    {
        Vector3 dir = dest - from;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            sr.enabled = false;
            return;
        }

        dir.Normalize();

        // Base placement: in front of unit toward destination
        Vector3 pos = from + dir * forwardOffset;
        pos.y = from.y + heightOffset;

        // Optional: screen offset to avoid overlapping UI icons
        if (useScreenOffset && commandCam != null)
        {
            Vector3 screen = commandCam.WorldToScreenPoint(pos);
            screen.x += screenOffsetPixels.x;
            screen.y += screenOffsetPixels.y;

            Ray rr = commandCam.ScreenPointToRay(screen);
            if (Physics.Raycast(rr, out RaycastHit hit2, raycastMaxDistance, groundMask))
            {
                pos = hit2.point;
                pos.y = from.y + heightOffset;
            }
        }

        transform.position = pos;

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

        sr.enabled = true;
    }

    // ---------------------------
    // Join-route detection (compile-safe)
    // Supports BOTH property name styles:
    // - IsJoinMoveInProgress + JoinMoveLeader + JoinMoveTarget
    // - IsJoinMoveInProgress + JoinPreviewLeader (+ JoinMoveTarget/JoinPreviewTarget)
    // ---------------------------
    private bool TryGetJoinRoute(out Transform leader, out Transform target)
    {
        leader = null;
        target = null;

        if (executor == null)
            executor = FindExecutorByName("CommandExecutor");

        if (executor == null)
            return false;

        bool inProgress = TryReadBool(executor, "IsJoinMoveInProgress");
        if (!inProgress) return false;

        leader =
            TryReadTransform(executor, "JoinMoveLeader") ??
            TryReadTransform(executor, "JoinPreviewLeader");

        target =
            TryReadTransform(executor, "JoinMoveTarget") ??
            TryReadTransform(executor, "JoinPreviewTarget") ??
            TryReadTransform(executor, "JoinTarget");

        // If we can’t find a target, we can’t draw a meaningful arrow
        return leader != null && target != null;
    }

    private MonoBehaviour FindExecutorByName(string typeName)
    {
        var all = FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null) continue;
            if (mb.GetType().Name == typeName)
                return mb;
        }
        return null;
    }

    private bool TryReadBool(object obj, string name)
    {
        if (obj == null) return false;
        try
        {
            var t = obj.GetType();

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(bool))
                return (bool)p.GetValue(obj);

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool))
                return (bool)f.GetValue(obj);
        }
        catch { }
        return false;
    }

    private Transform TryReadTransform(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var t = obj.GetType();

            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && typeof(Transform).IsAssignableFrom(p.PropertyType))
                return p.GetValue(obj) as Transform;

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && typeof(Transform).IsAssignableFrom(f.FieldType))
                return f.GetValue(obj) as Transform;
        }
        catch { }
        return null;
    }
}
