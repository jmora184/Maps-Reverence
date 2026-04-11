using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight clean-view / screenshot mode toggle.
///
/// Press the toggle key to hide things like:
/// - weapon / gun model
/// - reticle / crosshair
/// - health bar / HUD
///
/// Designed to be easy to disable or remove before final build.
/// Attach this to any always-active GameObject (player, game manager, etc.).
/// Then assign the objects you want hidden while screenshot mode is active.
///
/// This version also supports disabling specific scripts/components that may
/// try to force UI elements back on while screenshot mode is active.
/// </summary>
public class ScreenshotModeToggle : MonoBehaviour
{
    [Header("Toggle")]
    public bool featureEnabled = true;
    public KeyCode toggleKey = KeyCode.O;
    public bool startInScreenshotMode = false;

    [Header("Objects To Hide In Screenshot Mode")]
    [Tooltip("Assign your gun root, reticle, health bar, ammo UI, etc.")]
    public GameObject[] objectsToHide;

    [Header("Optional Components To Disable While Active")]
    [Tooltip("Useful for scripts like CrosshairRecoilUI that may re-enable visuals.")]
    public Behaviour[] behavioursToDisable;

    [Header("Optional Graphics To Force Off")]
    [Tooltip("Optional extra safety for Image/Text/RawImage graphics that may get re-enabled elsewhere.")]
    public Graphic[] graphicsToDisable;

    [Header("Optional")]
    [Tooltip("If true, also hide the mouse cursor while screenshot mode is active.")]
    public bool forceHideCursor = false;

    [Tooltip("If true, re-applies the hidden state every LateUpdate in case another script turns things back on.")]
    public bool enforceHiddenEveryFrame = true;

    private bool screenshotModeActive = false;
    private CursorLockMode originalCursorLockMode;
    private bool originalCursorVisible;

    private readonly Dictionary<Behaviour, bool> originalBehaviourStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Graphic, bool> originalGraphicStates = new Dictionary<Graphic, bool>();

    private void Awake()
    {
        originalCursorLockMode = Cursor.lockState;
        originalCursorVisible = Cursor.visible;
        CacheOriginalStates();
    }

    private void Start()
    {
        SetScreenshotMode(startInScreenshotMode);
    }

    private void Update()
    {
        if (!featureEnabled)
            return;

        if (Input.GetKeyDown(toggleKey))
        {
            SetScreenshotMode(!screenshotModeActive);
        }
    }

    private void LateUpdate()
    {
        if (!featureEnabled || !screenshotModeActive || !enforceHiddenEveryFrame)
            return;

        ApplyHiddenState();
    }

    public void SetScreenshotMode(bool active)
    {
        screenshotModeActive = active;

        if (active)
        {
            ApplyHiddenState();

            if (forceHideCursor)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
        else
        {
            RestoreVisibleState();

            if (forceHideCursor)
            {
                Cursor.visible = originalCursorVisible;
                Cursor.lockState = originalCursorLockMode;
            }
        }
    }

    public void ToggleScreenshotMode()
    {
        SetScreenshotMode(!screenshotModeActive);
    }

    public bool IsScreenshotModeActive()
    {
        return screenshotModeActive;
    }

    private void ApplyHiddenState()
    {
        if (objectsToHide != null)
        {
            for (int i = 0; i < objectsToHide.Length; i++)
            {
                if (objectsToHide[i] != null && objectsToHide[i].activeSelf)
                    objectsToHide[i].SetActive(false);
            }
        }

        if (behavioursToDisable != null)
        {
            for (int i = 0; i < behavioursToDisable.Length; i++)
            {
                Behaviour behaviour = behavioursToDisable[i];
                if (behaviour != null)
                    behaviour.enabled = false;
            }
        }

        if (graphicsToDisable != null)
        {
            for (int i = 0; i < graphicsToDisable.Length; i++)
            {
                Graphic graphic = graphicsToDisable[i];
                if (graphic != null)
                    graphic.enabled = false;
            }
        }
    }

    private void RestoreVisibleState()
    {
        if (objectsToHide != null)
        {
            for (int i = 0; i < objectsToHide.Length; i++)
            {
                if (objectsToHide[i] != null && !objectsToHide[i].activeSelf)
                    objectsToHide[i].SetActive(true);
            }
        }

        foreach (KeyValuePair<Behaviour, bool> kvp in originalBehaviourStates)
        {
            if (kvp.Key != null)
                kvp.Key.enabled = kvp.Value;
        }

        foreach (KeyValuePair<Graphic, bool> kvp in originalGraphicStates)
        {
            if (kvp.Key != null)
                kvp.Key.enabled = kvp.Value;
        }
    }

    private void CacheOriginalStates()
    {
        originalBehaviourStates.Clear();
        if (behavioursToDisable != null)
        {
            for (int i = 0; i < behavioursToDisable.Length; i++)
            {
                Behaviour behaviour = behavioursToDisable[i];
                if (behaviour != null && !originalBehaviourStates.ContainsKey(behaviour))
                    originalBehaviourStates.Add(behaviour, behaviour.enabled);
            }
        }

        originalGraphicStates.Clear();
        if (graphicsToDisable != null)
        {
            for (int i = 0; i < graphicsToDisable.Length; i++)
            {
                Graphic graphic = graphicsToDisable[i];
                if (graphic != null && !originalGraphicStates.ContainsKey(graphic))
                    originalGraphicStates.Add(graphic, graphic.enabled);
            }
        }
    }

    private void OnDisable()
    {
        // Safety: if the component gets disabled or removed during testing,
        // restore everything so the player is not stuck with hidden UI.
        SetScreenshotMode(false);
    }
}
