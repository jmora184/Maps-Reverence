using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to your existing Canvas -> Recruit GameObject that has a Text (Legacy).
/// This version is "explicit show/hide" (no auto-hide). If something calls Show(), it's visible until Hide() is called.
/// </summary>
public class RecruitPromptUI : MonoBehaviour
{
    private static RecruitPromptUI _instance;

    [Header("References")]
    [SerializeField] private Text promptText;

    [Header("Debug")]
    [SerializeField] private bool testShowOnStart = false;
    [SerializeField] private string testMessage = "Press J to recruit";

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        if (promptText == null)
            promptText = GetComponentInChildren<Text>(true);

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

        // Ensure hierarchy is active
        if (!_instance.gameObject.activeSelf) _instance.gameObject.SetActive(true);
        if (!_instance.promptText.gameObject.activeSelf) _instance.promptText.gameObject.SetActive(true);

        // Force visible
        _instance.promptText.enabled = true;
        var c = _instance.promptText.color;
        if (c.a < 0.05f) { c.a = 1f; _instance.promptText.color = c; }

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
        if (promptText == null) return;
        promptText.enabled = visible;
        promptText.gameObject.SetActive(visible);
    }
}
