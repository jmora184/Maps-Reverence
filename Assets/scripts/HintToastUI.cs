using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HintToastUI : MonoBehaviour
{
    [Header("UI")]
    public RectTransform root;        // panel rect
    public CanvasGroup canvasGroup;   // CanvasGroup on the panel
    public Text messageText;          // legacy Text

    [Header("Timing")]
    public float fadeIn = 0.15f;
    public float hold = 1.6f;
    public float fadeOut = 0.15f;

    [Header("Motion")]
    public float slidePixels = 18f;

    [Header("Behavior")]
    [Tooltip("Keep the root GameObject active; only fades alpha. Safer with many canvases.")]
    public bool keepRootActive = true;

    private Vector2 basePos;
    private Coroutine routine;
    private int token;

    private void Awake()
    {
        if (root == null) root = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (messageText == null) messageText = GetComponentInChildren<Text>(true);

        if (root != null) basePos = root.anchoredPosition;

        // Start hidden (but optionally keep active)
        ApplyHiddenState();
    }

    // --- BACKWARDS COMPAT ---
    // If your old code calls Hide()
    public void Hide() => HideImmediate();

    // If your old code calls Show(string)
    public void Show(string message) => Show(message, hold);

    // If your old code calls Show(string, float)
    public void Show(string message, float holdSeconds)
    {
        // IMPORTANT: interrupt immediately.
        InterruptAndShow(message, holdSeconds);
    }

    public void InterruptAndShow(string message, float holdSeconds)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        if (messageText != null)
            messageText.text = message;

        token++;

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        // Hard reset to prevent flicker / “flash again”
        ApplyHiddenState();

        routine = StartCoroutine(Play(token, holdSeconds));
    }

    public void HideImmediate()
    {
        token++;

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        ApplyHiddenState();
    }

    private IEnumerator Play(int myToken, float holdSeconds)
    {
        // Ensure visible and positioned
        if (root != null)
        {
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.anchoredPosition = basePos - new Vector2(0f, slidePixels);
        }
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        // Fade/slide in
        yield return FadeSlide(myToken, 0f, 1f, -slidePixels, 0f, fadeIn);
        if (myToken != token) yield break;

        // Hold
        float t = 0f;
        while (t < holdSeconds)
        {
            if (myToken != token) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Fade/slide out
        yield return FadeSlide(myToken, 1f, 0f, 0f, slidePixels, fadeOut);
        if (myToken != token) yield break;

        ApplyHiddenState();
        routine = null;
    }

    private IEnumerator FadeSlide(int myToken, float a0, float a1, float y0, float y1, float duration)
    {
        if (duration <= 0f)
        {
            Apply(a1, y1);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            if (myToken != token) yield break;

            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);

            Apply(Mathf.Lerp(a0, a1, u), Mathf.Lerp(y0, y1, u));
            yield return null;
        }

        Apply(a1, y1);
    }

    private void Apply(float alpha, float yOffset)
    {
        if (canvasGroup != null) canvasGroup.alpha = alpha;
        if (root != null) root.anchoredPosition = basePos + new Vector2(0f, yOffset);
    }

    private void ApplyHiddenState()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        if (root != null)
        {
            root.anchoredPosition = basePos - new Vector2(0f, slidePixels);

            if (!keepRootActive)
                root.gameObject.SetActive(false);
            else if (!root.gameObject.activeSelf)
                root.gameObject.SetActive(true);
        }
    }
}
