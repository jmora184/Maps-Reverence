using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Simple pause menu controller for MNR.
/// Put this on an ALWAYS-ACTIVE object (for example: your Canvas root or a dedicated UI manager object).
/// Do NOT put it on the PausePanel itself if that panel starts disabled.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Pause UI")]
    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject tipsPanel;
    [SerializeField] private GameObject controlsPanel;

    [Header("Optional: Hide Other UI While Paused")]
    [SerializeField] private GameObject[] hideWhilePaused;

    [Header("Optional: Disable Player/Camera Scripts While Paused")]
    [SerializeField] private Behaviour[] disableWhilePaused;

    [Header("Scene")]
    [SerializeField] private string startScreenSceneName = "StartScreen";

    [Header("Input")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    [Header("Audio")]
    [SerializeField] private bool pauseAudio = true;

    [Header("Optional Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button tipsButton;
    [SerializeField] private Button controlsButton;

    [Tooltip("Legacy/shared back button. Optional. Still wired to ShowMain().")]
    [SerializeField] private Button backButton;

    [Tooltip("Optional back button inside the Tips panel.")]
    [SerializeField] private Button tipsBackButton;

    [Tooltip("Optional back button inside the Controls panel.")]
    [SerializeField] private Button controlsBackButton;

    [SerializeField] private Button quitButton;
    [SerializeField] private Button respawnButton;

    [Header("Optional Respawn / Unstuck")]
    [SerializeField] private Transform playerToRespawn;
    [SerializeField] private Vector3 respawnOffset = new Vector3(10f, 10f, 10f);
    [SerializeField] private bool resumeAfterRespawn = true;

    private bool isPaused;
    private float previousTimeScale = 1f;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        EnsureValidSetup();
        WireButtons();
        ForceClosedImmediate();
    }

    private void Update()
    {
        if (Input.GetKeyDown(pauseKey))
            TogglePause();
    }

    private void OnDisable()
    {
        if (isPaused)
            ResumeGame();
    }

    private void EnsureValidSetup()
    {
        if (pauseRoot == null)
            Debug.LogWarning("PauseMenu: pauseRoot is not assigned.", this);
    }

    private void WireButtons()
    {
        WireButton(resumeButton, ResumeGame);
        WireButton(saveButton, SaveGame);
        WireButton(tipsButton, ShowTips);
        WireButton(controlsButton, ShowControls);
        WireButton(backButton, ShowMain);
        WireButton(tipsBackButton, ShowMain);
        WireButton(controlsBackButton, ShowMain);
        WireButton(quitButton, QuitToStartScreen);
        WireButton(respawnButton, RespawnPlayerByOffset);
    }

    private void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    public void TogglePause()
    {
        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        if (isPaused)
            return;

        isPaused = true;
        previousTimeScale = Time.timeScale;
        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;

        if (pauseRoot != null)
            pauseRoot.SetActive(true);

        ShowMain();
        SetGameplayObjectsEnabled(false);
        SetHiddenUiVisible(false);

        Time.timeScale = 0f;

        if (pauseAudio)
            AudioListener.pause = true;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void ResumeGame()
    {
        if (!isPaused)
            return;

        isPaused = false;
        Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;

        if (pauseAudio)
            AudioListener.pause = false;

        SetGameplayObjectsEnabled(true);
        SetHiddenUiVisible(true);

        if (pauseRoot != null)
            pauseRoot.SetActive(false);

        Cursor.visible = previousCursorVisible;
        Cursor.lockState = previousCursorLockMode;
    }

    public void SaveGame()
    {
        bool success = MNRSaveSystem.SaveCurrentGame();
        Debug.Log(success ? "[PauseMenu] Game saved." : "[PauseMenu] Save failed.", this);
    }

    public void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (tipsPanel != null) tipsPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(false);
    }

    public void ShowTips()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (tipsPanel != null) tipsPanel.SetActive(true);
        if (controlsPanel != null) controlsPanel.SetActive(false);
    }

    public void ShowControls()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (tipsPanel != null) tipsPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(true);
    }

    public void RespawnPlayerByOffset()
    {
        if (playerToRespawn == null)
        {
            Debug.LogWarning("PauseMenu: playerToRespawn is not assigned.", this);
            return;
        }

        Vector3 targetPosition = playerToRespawn.position + respawnOffset;

        CharacterController characterController = playerToRespawn.GetComponent<CharacterController>();
        Rigidbody rigidbodyComponent = playerToRespawn.GetComponent<Rigidbody>();

        bool reEnableCharacterController = false;
        if (characterController != null && characterController.enabled)
        {
            characterController.enabled = false;
            reEnableCharacterController = true;
        }

        playerToRespawn.position = targetPosition;

        if (rigidbodyComponent != null)
        {
            rigidbodyComponent.linearVelocity = Vector3.zero;
            rigidbodyComponent.angularVelocity = Vector3.zero;
        }

        if (reEnableCharacterController)
            characterController.enabled = true;

        if (resumeAfterRespawn)
            ResumeGame();
    }

    public void QuitToStartScreen()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(startScreenSceneName);
    }

    private void ForceClosedImmediate()
    {
        isPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (pauseRoot != null)
            pauseRoot.SetActive(false);

        if (mainPanel != null) mainPanel.SetActive(true);
        if (tipsPanel != null) tipsPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(false);
    }

    private void SetGameplayObjectsEnabled(bool enabled)
    {
        if (disableWhilePaused == null)
            return;

        for (int i = 0; i < disableWhilePaused.Length; i++)
        {
            if (disableWhilePaused[i] != null)
                disableWhilePaused[i].enabled = enabled;
        }
    }

    private void SetHiddenUiVisible(bool visible)
    {
        if (hideWhilePaused == null)
            return;

        for (int i = 0; i < hideWhilePaused.Length; i++)
        {
            if (hideWhilePaused[i] != null)
                hideWhilePaused[i].SetActive(visible);
        }
    }
}
