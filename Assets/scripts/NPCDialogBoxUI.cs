using UnityEngine;
using UnityEngine.UI;

public class NPCDialogBoxUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text dialogText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Behavior")]
    [SerializeField] private bool startHidden = true;
    [SerializeField] private float fadeSpeed = 12f;

    private float _targetAlpha;

    private void Awake()
    {
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (!canvasGroup) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (!dialogText) dialogText = GetComponentInChildren<Text>(true);

        _targetAlpha = startHidden ? 0f : 1f;
        canvasGroup.alpha = _targetAlpha;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Update()
    {
        if (!canvasGroup) return;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, _targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
    }

    public void Show(string message)
    {
        if (dialogText) dialogText.text = message;
        _targetAlpha = 1f;
    }

    public void Hide()
    {
        _targetAlpha = 0f;
    }

    public bool IsVisible => _targetAlpha > 0.5f;
}
