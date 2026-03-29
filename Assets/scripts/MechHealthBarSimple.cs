using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Very simple screen-space mech boss health bar.
/// Put this on your MechHealth UI object.
/// 
/// IMPORTANT:
/// - Assign ONLY the red fill Image to redFillImage.
/// - Do NOT assign the parent panel/background/title object.
/// - Best setup: set redFillImage Image Type = Filled, Fill Method = Horizontal.
/// </summary>
[DisallowMultipleComponent]
public class MechHealthBarSimple : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MechHealthController mechHealth;
    [SerializeField] private GameObject visibilityRoot;
    [SerializeField] private Image redFillImage;
    [SerializeField] private TMP_Text bossNameText;
    [SerializeField] private TMP_Text hpText;

    [Header("Display")]
    [SerializeField] private string bossName = "MECH";
    [SerializeField] private bool hideWhenDead = true;
    [SerializeField] private bool hideWhenUnbound = true;
    [SerializeField] private bool useFillAmount = true;
    [SerializeField] private bool useWidthFallback = false;

    private RectTransform _fillRect;
    private float _baseWidth = -1f;
    private CanvasGroup _canvasGroup;

    private void Reset()
    {
        if (visibilityRoot == null) visibilityRoot = gameObject;
        ApplyStaticText();
    }

    private void Awake()
    {
        if (visibilityRoot == null) visibilityRoot = gameObject;

        if (redFillImage != null)
            _fillRect = redFillImage.rectTransform;

        if (_fillRect != null)
            _baseWidth = _fillRect.sizeDelta.x;

        if (visibilityRoot == gameObject)
            _canvasGroup = GetComponent<CanvasGroup>();

        ApplyStaticText();
        Redraw();
    }

    private void OnEnable()
    {
        ApplyStaticText();
        Redraw();
    }

    private void Update()
    {
        Redraw();
    }

    private void ApplyStaticText()
    {
        if (bossNameText != null)
            bossNameText.text = bossName;
    }

    private void Redraw()
    {
        bool hasHealth = mechHealth != null;
        bool dead = hasHealth && mechHealth.IsDead;

        bool visible = hasHealth;
        if (!hasHealth && hideWhenUnbound) visible = false;
        if (dead && hideWhenDead) visible = false;

        ApplyVisibility(visible);
        if (!visible) return;

        float health01 = 0f;
        if (mechHealth.maxHealth > 0)
            health01 = Mathf.Clamp01((float)mechHealth.currentHealth / mechHealth.maxHealth);

        if (redFillImage != null)
        {
            if (useFillAmount)
            {
                redFillImage.fillAmount = health01;
            }
            else if (useWidthFallback && _fillRect != null)
            {
                Vector2 size = _fillRect.sizeDelta;
                if (_baseWidth < 0f) _baseWidth = size.x;
                size.x = Mathf.Max(0f, _baseWidth * health01);
                _fillRect.sizeDelta = size;
            }
        }

        if (hpText != null)
            hpText.text = $"{Mathf.Max(0, mechHealth.currentHealth)} / {Mathf.Max(1, mechHealth.maxHealth)}";
    }

    private void ApplyVisibility(bool visible)
    {
        GameObject target = visibilityRoot != null ? visibilityRoot : gameObject;

        if (target == gameObject)
        {
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
            return;
        }

        if (target.activeSelf != visible)
            target.SetActive(visible);
    }
}
