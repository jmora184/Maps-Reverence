using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Standalone title screen controller for Maps & Reverence.
///
/// Features:
/// - Loads the configured gameplay scene.
/// - Can clear the saved checkpoint so Start Game begins fresh.
/// - Can Continue from the latest MNR save file.
/// - Opens / closes a tips overlay.
/// - Opens / closes a controls overlay.
/// - Shows a loading panel before scene loading begins.
/// - Waits a couple frames so the loading UI actually renders before the heavy scene starts loading.
/// - Supports a spinner, loading text, and optional progress fill image.
/// </summary>
public class StartScreenMenu : MonoBehaviour
{
    [Header("Scene Loading")]
    [SerializeField] private string gameplaySceneName = "secondScene";
    [SerializeField] private bool clearCheckpointOnStartGame = true;
    [SerializeField] private bool closeTipsWhenStartGame = true;
    [SerializeField] private float minimumLoadingScreenSeconds = 0.35f;
    [SerializeField] private int framesToShowLoadingUIBeforeLoad = 2;
    [SerializeField] private Button continueButton;

    [Header("Tips Overlay")]
    [SerializeField] private GameObject tipsPanel;
    [SerializeField] private bool startWithTipsClosed = true;
    [SerializeField] private bool allowEscapeToCloseTips = true;

    [Header("Controls Overlay")]
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private Button controlsButton;
    [SerializeField] private Button backButton;

    [Header("Optional Tips Image Setup")]
    [SerializeField] private Image tipsImageTarget;
    [SerializeField] private Sprite tipsSprite;
    [SerializeField] private bool preserveImageAspect = true;

    [Header("Optional Menu Objects To Hide While Overlay Is Open")]
    [SerializeField] private GameObject[] hideWhenTipsOpen;

    [Header("Optional Loading UI")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject[] hideWhenLoading;
    [SerializeField] private RectTransform loadingSpinner;
    [SerializeField] private float loadingSpinnerDegreesPerSecond = -180f;
    [SerializeField] private Image loadingProgressFill;
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private Text legacyLoadingText;
    [SerializeField] private string loadingTextPrefix = "Loading";
    [SerializeField] private bool showPercentInLoadingText = false;
    [SerializeField] private bool animateLoadingDots = true;

    [Header("Cursor")]
    [SerializeField] private bool unlockCursorOnMenu = true;
    [SerializeField] private bool showCursorOnMenu = true;

    private bool isLoading;
    private float dotsTimer;
    private int dotsCount;

    private void Awake()
    {
        if (unlockCursorOnMenu)
            Cursor.lockState = CursorLockMode.None;

        Cursor.visible = showCursorOnMenu;

        WireButtons();
        ApplyTipsSprite();
        ResetLoadingUI();
        UpdateContinueButtonState();

        if (tipsPanel != null && startWithTipsClosed)
            tipsPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        RefreshOverlayHiddenObjects();
    }

    private void OnEnable()
    {
        UpdateContinueButtonState();
        RefreshOverlayHiddenObjects();
    }

    private void Update()
    {
        if (allowEscapeToCloseTips && !isLoading && AnyOverlayOpen() && Input.GetKeyDown(KeyCode.Escape))
            CloseAllOverlays();

        if (!isLoading)
            return;

        if (loadingSpinner != null)
            loadingSpinner.Rotate(0f, 0f, loadingSpinnerDegreesPerSecond * Time.unscaledDeltaTime);

        if (animateLoadingDots)
        {
            dotsTimer += Time.unscaledDeltaTime;
            if (dotsTimer >= 0.35f)
            {
                dotsTimer = 0f;
                dotsCount = (dotsCount + 1) % 4;
                UpdateLoadingText(-1f);
            }
        }
    }

    private void WireButtons()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(ContinueGame);
            continueButton.onClick.AddListener(ContinueGame);
        }

        if (controlsButton != null)
        {
            controlsButton.onClick.RemoveListener(OpenControls);
            controlsButton.onClick.AddListener(OpenControls);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(CloseAllOverlays);
            backButton.onClick.AddListener(CloseAllOverlays);
        }
    }

    public void StartGame()
    {
        if (isLoading)
            return;

        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("[StartScreenMenu] Gameplay scene name is empty.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogError("[StartScreenMenu] Scene '" + gameplaySceneName + "' is not in Build Profiles / Build Settings or the name is wrong.");
            return;
        }

        if (closeTipsWhenStartGame && AnyOverlayOpen())
            CloseAllOverlays();

        StartCoroutine(LoadGameAsync(gameplaySceneName, clearCheckpointOnStartGame, false));
    }

    public void ContinueGame()
    {
        if (isLoading)
            return;

        if (!MNRSaveSystem.HasSave())
        {
            UpdateContinueButtonState();
            Debug.LogWarning("[StartScreenMenu] Continue clicked but no save file exists.");
            return;
        }

        string targetSceneName = MNRSaveSystem.GetSavedSceneNameOrFallback(gameplaySceneName);
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogError("[StartScreenMenu] Continue could not resolve a saved scene name.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError("[StartScreenMenu] Saved scene '" + targetSceneName + "' is not in Build Profiles / Build Settings or the name is wrong.");
            return;
        }

        if (closeTipsWhenStartGame && AnyOverlayOpen())
            CloseAllOverlays();

        StartCoroutine(LoadGameAsync(targetSceneName, false, true));
    }

    public void OpenTips()
    {
        if (isLoading)
            return;

        ApplyTipsSprite();

        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        if (tipsPanel != null)
            tipsPanel.SetActive(true);

        RefreshOverlayHiddenObjects();
    }

    public void CloseTips()
    {
        if (tipsPanel != null)
            tipsPanel.SetActive(false);

        RefreshOverlayHiddenObjects();
    }

    public void ToggleTips()
    {
        if (tipsPanel == null || isLoading)
            return;

        if (tipsPanel.activeSelf)
            CloseTips();
        else
            OpenTips();
    }

    public void OpenControls()
    {
        if (isLoading)
            return;

        if (tipsPanel != null)
            tipsPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(true);

        RefreshOverlayHiddenObjects();
    }

    public void CloseControls()
    {
        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        RefreshOverlayHiddenObjects();
    }

    public void ToggleControls()
    {
        if (controlsPanel == null || isLoading)
            return;

        if (controlsPanel.activeSelf)
            CloseControls();
        else
            OpenControls();
    }

    public void CloseAllOverlays()
    {
        if (tipsPanel != null)
            tipsPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        RefreshOverlayHiddenObjects();
    }

    public void QuitGame()
    {
        PlayerPrefs.Save();
        Application.Quit();
        Debug.Log("Quitting Game");
    }

    private IEnumerator LoadGameAsync(string targetSceneName, bool clearCheckpoint, bool requestContinueLoad)
    {
        isLoading = true;
        dotsTimer = 0f;
        dotsCount = 0;

        PrepareSceneLoadPrefs(targetSceneName, clearCheckpoint, requestContinueLoad);
        ShowLoadingUI(true);
        UpdateLoadingVisuals(0f);

        Canvas.ForceUpdateCanvases();

        int frameCount = Mathf.Max(1, framesToShowLoadingUIBeforeLoad);
        for (int i = 0; i < frameCount; i++)
            yield return null;

        yield return new WaitForEndOfFrame();

        float loadingScreenStartTime = Time.unscaledTime;
        AsyncOperation operation = SceneManager.LoadSceneAsync(targetSceneName);

        if (operation == null)
        {
            Debug.LogError("[StartScreenMenu] Failed to start async load for scene '" + targetSceneName + "'.");
            ShowLoadingUI(false);
            isLoading = false;
            yield break;
        }

        operation.allowSceneActivation = false;

        while (operation.progress < 0.9f)
        {
            float normalizedProgress = Mathf.Clamp01(operation.progress / 0.9f);
            UpdateLoadingVisuals(normalizedProgress);
            yield return null;
        }

        UpdateLoadingVisuals(1f);

        while (Time.unscaledTime - loadingScreenStartTime < minimumLoadingScreenSeconds)
        {
            UpdateLoadingVisuals(1f);
            yield return null;
        }

        operation.allowSceneActivation = true;

        while (!operation.isDone)
            yield return null;
    }

    private void PrepareSceneLoadPrefs(string targetSceneName, bool clearCheckpoint, bool requestContinueLoad)
    {
        PlayerPrefs.SetString("CurrentLevel", targetSceneName);

        if (clearCheckpoint)
            PlayerPrefs.SetString(targetSceneName + "_cp", "");

        PlayerPrefs.Save();

        if (requestContinueLoad)
            MNRSaveSystem.RequestContinueLoad();
    }

    private void ApplyTipsSprite()
    {
        if (tipsImageTarget == null || tipsSprite == null)
            return;

        tipsImageTarget.sprite = tipsSprite;
        tipsImageTarget.preserveAspect = preserveImageAspect;
        tipsImageTarget.enabled = true;
    }

    private void ResetLoadingUI()
    {
        ShowLoadingUI(false);
        UpdateLoadingVisuals(0f);
    }

    private void UpdateContinueButtonState()
    {
        if (continueButton != null)
            continueButton.interactable = MNRSaveSystem.HasSave();
    }

    private void ShowLoadingUI(bool show)
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(show);

        SetGameObjectsActive(hideWhenLoading, !show);
    }

    private void UpdateLoadingVisuals(float normalizedProgress)
    {
        if (loadingProgressFill != null)
            loadingProgressFill.fillAmount = Mathf.Clamp01(normalizedProgress);

        UpdateLoadingText(normalizedProgress);
    }

    private void UpdateLoadingText(float normalizedProgress)
    {
        string dots = string.Empty;

        if (animateLoadingDots)
        {
            for (int i = 0; i < dotsCount; i++)
                dots += ".";
        }

        string finalText = loadingTextPrefix + dots;

        if (showPercentInLoadingText && normalizedProgress >= 0f)
        {
            int percent = Mathf.RoundToInt(Mathf.Clamp01(normalizedProgress) * 100f);
            finalText += " " + percent + "%";
        }

        if (loadingText != null)
            loadingText.text = finalText;

        if (legacyLoadingText != null)
            legacyLoadingText.text = finalText;
    }

    private bool AnyOverlayOpen()
    {
        return (tipsPanel != null && tipsPanel.activeSelf)
            || (controlsPanel != null && controlsPanel.activeSelf);
    }

    private void RefreshOverlayHiddenObjects()
    {
        SetGameObjectsActive(hideWhenTipsOpen, !AnyOverlayOpen());
    }

    private void SetGameObjectsActive(GameObject[] objects, bool activeState)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(activeState);
        }
    }
}
