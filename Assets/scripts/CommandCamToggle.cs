using System.Collections;
using UnityEngine;

public class CommandCamToggle : MonoBehaviour
{
    public static CommandCamToggle Instance { get; private set; }

    [Header("References")]
    public Camera fpsCam;
    public Camera commandCam;
    public GameObject commandUIRoot;
    public GameObject commandLegendPanel;

    [Header("Optional: overlay to refresh when entering command mode")]
    public CommandOverlayUI overlayUI; // drag MiniUI (has CommandOverlayUI)

    [Header("Optional: Disable these while in Command Mode")]
    public MonoBehaviour[] disableInCommandMode; // drag Player2Controller, gun scripts, etc.

    [Header("Execute queued commands on exit")]
    public CommandExecutor executor;   // drag your CommandExecutor (or it will auto-find)

    [Header("Keys")]
    public bool useSingleToggleKey = true;
    public KeyCode toggleCommandKey = KeyCode.Tab;
    public KeyCode enterCommandKey = KeyCode.K;
    public KeyCode exitCommandKey = KeyCode.L;

    [Header("Settings")]
    public bool startInCommandMode = false;
    public bool lockCursorInFps = true;
    public bool clearQueueOnEnter = true;

    [Header("Legend")]
    public bool showLegendOnlyAtDefaultZoom = true;
    public int legendVisibleZoomIndex = 0;

    private bool isCommandMode;
    public bool IsCommandMode => isCommandMode;

    private CanvasGroup uiGroup;
    private Coroutine switchRoutine;
    private CommandCameraZoomPan zoomPan;

    [Header("Pause While In Command Mode")]
    public bool pauseGameInCommandMode = true;
    public float commandModeTimeScale = 0f;
    public float fpsTimeScale = 1f;

    private void Awake()
    {
        Instance = this;

        if (executor == null) executor = FindObjectOfType<CommandExecutor>();
        CacheZoomPan();

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

        RefreshLegendVisibility(startInCommandMode);

        SetCommandMode(startInCommandMode, force: true);
    }

    private void Update()
    {
        if (useSingleToggleKey)
        {
            if (Input.GetKeyDown(toggleCommandKey))
                SetCommandMode(!isCommandMode);
        }
        else
        {
            if (!isCommandMode && Input.GetKeyDown(enterCommandKey))
                SetCommandMode(true);

            if (isCommandMode && Input.GetKeyDown(exitCommandKey))
                SetCommandMode(false);
        }

        RefreshLegendVisibility(isCommandMode);
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

        RefreshLegendVisibility(on);

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

            // Snap to the configured command-map overview, if that script exists.
            if (commandCam != null)
            {
                CacheZoomPan();

                if (zoomPan != null && zoomPan.applyEntryViewOnCommandModeEnter)
                    zoomPan.ApplyCommandModeEntryView();
            }

            RefreshLegendVisibility(true);

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

            RefreshLegendVisibility(false);

            // Execute queued moves now (like old FullMini behavior)
            if (CommandQueue.Instance != null && executor != null)
            {
                CommandQueue.Instance.FlushMoves(executor);
            }
        }
    }

    private void CacheZoomPan()
    {
        if (commandCam != null)
            zoomPan = commandCam.GetComponent<CommandCameraZoomPan>();
        else
            zoomPan = null;
    }

    private void RefreshLegendVisibility(bool commandModeOn)
    {
        if (commandLegendPanel == null)
            return;

        bool shouldShow = commandModeOn;

        if (shouldShow && showLegendOnlyAtDefaultZoom)
        {
            CacheZoomPan();

            if (zoomPan != null)
                shouldShow = zoomPan.CurrentZoomIndex == legendVisibleZoomIndex;
        }

        if (commandLegendPanel.activeSelf != shouldShow)
            commandLegendPanel.SetActive(shouldShow);
    }
}
