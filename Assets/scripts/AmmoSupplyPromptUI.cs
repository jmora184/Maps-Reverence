using UnityEngine;
using TMPro;

/// <summary>
/// Dedicated prompt UI for ammo supply interactions.
///
/// Attach this to your existing Canvas -> TakeAmmo GameObject.
/// Supports a hierarchy like:
/// TakeAmmo
///   └── Panel
///       └── Text (TMP)
///
/// Explicit show/hide only. If something calls Show(), it stays visible until Hide() is called.
/// Mirrors the behavior of RecruitPromptUI, but is kept separate so ammo prompts do not fight the recruit prompt.
/// </summary>
public class AmmoSupplyPromptUI : MonoBehaviour
{
    private static AmmoSupplyPromptUI _instance;

    [Header("References")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;

    [Header("Debug")]
    [SerializeField] private bool testShowOnStart = false;
    [SerializeField] private string testMessage = "Press T to take ammo";

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        if (promptText == null)
            promptText = GetComponentInChildren<TMP_Text>(true);

        if (promptRoot == null)
        {
            if (promptText != null && promptText.transform.parent != null)
                promptRoot = promptText.transform.parent.gameObject;
            else
                promptRoot = gameObject;
        }

        SetVisible(false);
    }

    private void Start()
    {
        if (testShowOnStart)
            Show(testMessage);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static void Show(string message)
    {
        if (_instance == null) return;
        if (_instance.promptText == null) return;
        if (string.IsNullOrEmpty(message)) return;

        if (CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode)
        {
            _instance.SetVisible(false);
            return;
        }

        if (!_instance.gameObject.activeSelf) _instance.gameObject.SetActive(true);
        if (_instance.promptRoot != null && !_instance.promptRoot.activeSelf) _instance.promptRoot.SetActive(true);
        if (!_instance.promptText.gameObject.activeSelf) _instance.promptText.gameObject.SetActive(true);

        _instance.promptText.enabled = true;
        var c = _instance.promptText.color;
        if (c.a < 0.05f)
        {
            c.a = 1f;
            _instance.promptText.color = c;
        }

        _instance.promptText.text = message;
        _instance.SetVisible(true);
    }

    public static void Hide()
    {
        if (_instance == null) return;
        _instance.SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (visible && CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode)
            visible = false;

        if (promptRoot != null)
            promptRoot.SetActive(visible);

        if (promptText == null) return;

        promptText.enabled = visible;
        promptText.gameObject.SetActive(visible);
    }
}
