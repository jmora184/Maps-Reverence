using System.Collections;
using UnityEngine;

public class CommandCamToggle : MonoBehaviour
{
    public static CommandCamToggle Instance { get; private set; }

    [Header("References")]
    public Camera fpsCam;
    public Camera commandCam;
    public GameObject commandUIRoot;

    [Header("Optional: overlay to refresh when entering command mode")]
    public CommandOverlayUI overlayUI; // drag MiniUI (has CommandOverlayUI)

    [Header("Optional: Disable these while in Command Mode")]
    public MonoBehaviour[] disableInCommandMode; // drag Player2Controller, gun scripts, etc.

    [Header("Execute queued commands on exit")]
    public CommandExecutor executor;   // drag your CommandExecutor (or it will auto-find)

    [Header("Keys")]
    public KeyCode enterCommandKey = KeyCode.K;
    public KeyCode exitCommandKey = KeyCode.L;

    [Header("Settings")]
    public bool startInCommandMode = false;
    public bool lockCursorInFps = true;
    public bool clearQueueOnEnter = true;

    private bool isCommandMode;
    public bool IsCommandMode => isCommandMode;

    private CanvasGroup uiGroup;
    private Coroutine switchRoutine;

    [Header("Pause While In Command Mode")]
    public bool pauseGameInCommandMode = true;
    public float commandModeTimeScale = 0f;
    public float fpsTimeScale = 1f;

    private void Awake()
    {
        Instance = this;

        if (executor == null) executor = FindObjectOfType<CommandExecutor>();

        // Keep the UI object active so its scripts always initialize;
        // hide/show using CanvasGroup when possible (prevents first-toggle weirdness)
        if (commandUIRoot != null)
        {
            uiGroup = commandUIRoot.GetComponent<CanvasGroup>();
            commandUIRoot.SetActive(true);

            if (uiGroup != null)
            {
                uiGroup.alpha = 0f;
                uiGroup.interactable = false;
                uiGroup.blocksRaycasts = false;
            }
        }

        SetCommandMode(startInCommandMode, force: true);
    }

    private void Update()
    {
        if (!isCommandMode && Input.GetKeyDown(enterCommandKey))
            SetCommandMode(true);

        if (isCommandMode && Input.GetKeyDown(exitCommandKey))
            SetCommandMode(false);
    }

    public void SetCommandMode(bool on, bool force = false)
    {
        if (!force && on == isCommandMode) return;
        isCommandMode = on;

        if (switchRoutine != null) StopCoroutine(switchRoutine);
        switchRoutine = StartCoroutine(SwitchModeRoutine(on));
    }

    private IEnumerator SwitchModeRoutine(bool on)
    {
        if (pauseGameInCommandMode)
            Time.timeScale = on ? commandModeTimeScale : fpsTimeScale;

        // UI visibility
        if (commandUIRoot != null)
        {
            if (uiGroup != null)
            {
                uiGroup.alpha = on ? 1f : 0f;
                uiGroup.interactable = on;
                uiGroup.blocksRaycasts = on;
            }
            else
            {
                commandUIRoot.SetActive(on);
            }
        }

        // Disable gameplay scripts while in command mode
        if (disableInCommandMode != null)
        {
            foreach (var mb in disableInCommandMode)
                if (mb != null) mb.enabled = !on;
        }

        // Cursor
        if (lockCursorInFps)
        {
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
        }

        if (on)
        {
            // Optional: clear old queued commands when entering command mode
            if (clearQueueOnEnter && CommandQueue.Instance != null)
                CommandQueue.Instance.ClearAll();

            // Enable command cam first (avoid "no cameras rendering" frame)
            if (commandCam != null) commandCam.enabled = true;
            yield return null;
            if (fpsCam != null) fpsCam.enabled = false;

            // Refresh overlay/icons
            if (overlayUI == null)
            {
                // Prefer the singleton if your CommandOverlayUI uses Instance
                if (CommandOverlayUI.Instance != null)
                    overlayUI = CommandOverlayUI.Instance;
                else
                    overlayUI = FindObjectOfType<CommandOverlayUI>();
            }

            if (overlayUI != null)
                overlayUI.BuildIcons();
        }
        else
        {
            // Back to FPS camera
            if (fpsCam != null) fpsCam.enabled = true;
            if (commandCam != null) commandCam.enabled = false;

            // Execute queued moves now (like old FullMini behavior)
            if (CommandQueue.Instance != null && executor != null)
            {
                CommandQueue.Instance.FlushMoves(executor);
            }
        }
    }
}
