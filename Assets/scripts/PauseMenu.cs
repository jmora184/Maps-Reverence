using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Audio;

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

    [Tooltip("Optional AudioMixer used for music/SFX mute buttons.")]
    [SerializeField] private AudioMixer gameAudioMixer;

    [Tooltip("Exposed AudioMixer parameter name for gameplay music volume.")]
    [SerializeField] private string musicVolumeParameter = "MusicVolume";

    [Tooltip("Exposed AudioMixer parameter name for sound effects volume.")]
    [SerializeField] private string sfxVolumeParameter = "SFXVolume";

    [Tooltip("dB value used when music is not muted.")]
    [SerializeField] private float unmutedMusicDb = 0f;

    [Tooltip("dB value used when music is muted.")]
    [SerializeField] private float mutedMusicDb = -80f;

    [Tooltip("dB value used when SFX are not muted.")]
    [SerializeField] private float unmutedSfxDb = 0f;

    [Tooltip("dB value used when SFX are muted.")]
    [SerializeField] private float mutedSfxDb = -80f;

    [Header("Optional Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button tipsButton;
    [SerializeField] private Button controlsButton;

    [Tooltip("Optional button to mute gameplay music.")]
    [SerializeField] private Button muteMusicButton;

    [Tooltip("Optional button to unmute gameplay music.")]
    [SerializeField] private Button unmuteMusicButton;

    [Tooltip("Optional button to mute all sound effects.")]
    [SerializeField] private Button muteSfxButton;

    [Tooltip("Optional button to unmute all sound effects.")]
    [SerializeField] private Button unmuteSfxButton;

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

    private const string MusicMutedPrefKey = "PauseMenu_MusicMuted";
    private const string SfxMutedPrefKey = "PauseMenu_SfxMuted";

    private bool isPaused;
    private float previousTimeScale = 1f;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        EnsureValidSetup();
        WireButtons();
        ApplySavedAudioSettings();
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

        WireButton(muteMusicButton, MuteMusic);
        WireButton(unmuteMusicButton, UnmuteMusic);
        WireButton(muteSfxButton, MuteSfx);
        WireButton(unmuteSfxButton, UnmuteSfx);

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

    public void MuteMusic()
    {
        SetMusicMuted(true);
    }

    public void UnmuteMusic()
    {
        SetMusicMuted(false);
    }

    public void MuteSfx()
    {
        SetSfxMuted(true);
    }

    public void UnmuteSfx()
    {
        SetSfxMuted(false);
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

    private void ApplySavedAudioSettings()
    {
        bool musicMuted = PlayerPrefs.GetInt(MusicMutedPrefKey, 0) == 1;
        bool sfxMuted = PlayerPrefs.GetInt(SfxMutedPrefKey, 0) == 1;

        SetMusicMutedInternal(musicMuted, false);
        SetSfxMutedInternal(sfxMuted, false);
    }

    private void SetMusicMuted(bool muted)
    {
        SetMusicMutedInternal(muted, true);
    }

    private void SetSfxMuted(bool muted)
    {
        SetSfxMutedInternal(muted, true);
    }

    private void SetMusicMutedInternal(bool muted, bool savePref)
    {
        if (!TrySetMixerVolume(musicVolumeParameter, muted ? mutedMusicDb : unmutedMusicDb))
            return;

        if (savePref)
        {
            PlayerPrefs.SetInt(MusicMutedPrefKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void SetSfxMutedInternal(bool muted, bool savePref)
    {
        if (!TrySetMixerVolume(sfxVolumeParameter, muted ? mutedSfxDb : unmutedSfxDb))
            return;

        if (savePref)
        {
            PlayerPrefs.SetInt(SfxMutedPrefKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private bool TrySetMixerVolume(string parameterName, float value)
    {
        if (gameAudioMixer == null)
        {
            Debug.LogWarning("PauseMenu: gameAudioMixer is not assigned. Audio mute buttons will do nothing until it is assigned.", this);
            return false;
        }

        bool success = gameAudioMixer.SetFloat(parameterName, value);
        if (!success)
        {
            Debug.LogWarning("PauseMenu: AudioMixer parameter '" + parameterName + "' was not found or is not exposed.", this);
            return false;
        }

        return true;
    }
}
