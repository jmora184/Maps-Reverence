using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HintToastUI : MonoBehaviour
{
    [Header("UI")]
    public RectTransform root;
    public CanvasGroup canvasGroup;
    public Text messageText;

    [Header("Animation")]
    public float fadeIn = 0.15f;
    public float fadeOut = 0.15f;
    public float slidePixels = 18f;

    [Header("Behavior")]
    [Tooltip("Prevents the same message from re-triggering immediately (stops the tiny 'flash').")]
    public float debounceSeconds = 0.25f;

    private Vector2 baseAnchoredPos;
    private Coroutine routine;

    private string lastMessage = "";
    private float lastShowRealtime = -999f;

    private void Awake()
    {
        if (root == null || root.GetComponent<Canvas>() != null)
            root = transform as RectTransform;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        baseAnchoredPos = root.anchoredPosition;

        HideInstant();
    }

    public void Show(string message)
    {
        // Treat empty as hide
        if (string.IsNullOrEmpty(message))
        {
            Hide();
            return;
        }

        // ✅ Debounce: if the same message is being re-fired quickly, ignore it
        float now = Time.unscaledTime;
        if (message == lastMessage && (now - lastShowRealtime) <= debounceSeconds)
            return;

        lastMessage = message;
        lastShowRealtime = now;

        // If disabled, enable before coroutines
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (!enabled)
            enabled = true;

        if (messageText != null)
            messageText.text = message;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(FadeInRoutine());
    }

    public void Hide()
    {
        if (!gameObject.activeSelf)
            return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(FadeOutRoutine());
    }

    public void HideInstant()
    {
        if (routine != null)
            StopCoroutine(routine);

        routine = null;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (root != null)
            root.anchoredPosition = baseAnchoredPos + new Vector2(slidePixels, 0f);
    }

    private IEnumerator FadeInRoutine()
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeIn);

        Vector2 startPos = baseAnchoredPos + new Vector2(slidePixels, 0f);
        Vector2 endPos = baseAnchoredPos;

        if (root != null) root.anchoredPosition = startPos;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, t);

            if (root != null) root.anchoredPosition = Vector2.Lerp(startPos, endPos, k);
            if (canvasGroup != null) canvasGroup.alpha = k;

            yield return null;
        }

        if (root != null) root.anchoredPosition = endPos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        routine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeOut);

        Vector2 startPos = baseAnchoredPos;
        Vector2 endPos = baseAnchoredPos + new Vector2(slidePixels, 0f);

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float k = Mathf.SmoothStep(0f, 1f, t);

            if (root != null) root.anchoredPosition = Vector2.Lerp(startPos, endPos, k);
            if (canvasGroup != null) canvasGroup.alpha = 1f - k;

            yield return null;
        }

        // End state
        if (root != null) root.anchoredPosition = endPos;
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        routine = null;
    }
}
