using UnityEngine;
using UnityEngine.EventSystems;

public class MoveCursorMarker : MonoBehaviour
{
    [Header("Refs")]
    public CommandStateMachine stateMachine;
    public Camera commandCam;                  // your command/map camera
    public LayerMask groundMask;               // same ground layer you use in CommandStateMachine
    public float raycastMaxDistance = 5000f;

    [Header("Visibility")]
    public bool onlyShowInCommandMode = true;
    public bool hideWhenOverUI = true;
    public bool hideWhenNoGroundHit = true;

    [Header("Placement")]
    public float heightOffset = 0.03f;         // lift slightly above ground to avoid z-fighting

    [Header("Rotation (flat marker)")]
    public bool keepFlat = true;
    public Vector3 flatEuler = new Vector3(90f, 0f, 0f);

    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;

        // Auto-wiring
        if (stateMachine == null) stateMachine = FindObjectOfType<CommandStateMachine>();
        if (commandCam == null && CommandCamToggle.Instance != null) commandCam = CommandCamToggle.Instance.commandCam;

        // If you forgot to set groundMask, try grabbing it from the state machine
        if (stateMachine != null && groundMask.value == 0)
            groundMask = stateMachine.groundMask;
    }

    void LateUpdate()
    {
        if (sr == null || stateMachine == null)
            return;

        // Only in command mode (optional)
        if (onlyShowInCommandMode && CommandCamToggle.Instance != null && !CommandCamToggle.Instance.IsCommandMode)
        {
            sr.enabled = false;
            return;
        }

        // Only while choosing a move destination
        if (stateMachine.CurrentState != CommandStateMachine.State.MoveTargeting)
        {
            sr.enabled = false;
            return;
        }

        // Hide if pointer is over UI (optional)
        if (hideWhenOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            sr.enabled = false;
            return;
        }

        if (commandCam == null)
            commandCam = Camera.main;

        Ray r = commandCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(r, out RaycastHit hit, raycastMaxDistance, groundMask))
        {
            sr.enabled = true;

            Vector3 p = hit.point;
            p.y += heightOffset;
            transform.position = p;

            if (keepFlat)
                transform.rotation = Quaternion.Euler(flatEuler);
        }
        else
        {
            if (hideWhenNoGroundHit)
                sr.enabled = false;
        }
    }
}
