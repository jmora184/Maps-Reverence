using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

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

    [Header("Directional Bonus 2x")]
    [Tooltip("Optional UI object shown briefly when directional bonus damage (2x) is applied.")]
    [SerializeField] private GameObject directionalBonus2xObject;

    [Tooltip("How long the 2x UI stays visible after being triggered.")]
    [SerializeField] private float directionalBonus2xShowSeconds = 0.5f;

    [Header("Directional Bonus Flash")]
    [Tooltip("Optional background image to flash too. If empty, the script tries to find one automatically.")]
    [SerializeField] private Image backgroundFlashImage;

    [Tooltip("If true, briefly flashes the red fill when directional bonus damage happens.")]
    [SerializeField] private bool flashFillOnDirectionalBonus = true;

    [Tooltip("If true, briefly flashes the background too.")]
    [SerializeField] private bool flashBackgroundOnDirectionalBonus = true;

    [Tooltip("Bright flash color for the fill image.")]
    [SerializeField] private Color directionalBonusFillFlashColor = new Color(1f, 0.95f, 0.45f, 1f);

    [Tooltip("Flash color for the background image.")]
    [SerializeField] private Color directionalBonusBackgroundFlashColor = new Color(1f, 0.65f, 0.15f, 1f);

    [Tooltip("Alpha used during the flash.")]
    [Range(0f, 1f)]
    [SerializeField] private float directionalBonusFlashAlpha = 1f;

    [Tooltip("How long the flash lasts.")]
    [SerializeField] private float directionalBonusFlashSeconds = 0.2f;

    [Header("Directional Bonus Pulse")]
    [Tooltip("If true, pulses the whole mech health UI slightly when directional bonus damage happens.")]
    [SerializeField] private bool pulseRootOnDirectionalBonus = true;

    [Tooltip("How much to scale the mech health UI during the pulse.")]
    [SerializeField] private float directionalBonusRootPulseScaleMultiplier = 1.08f;

    [Tooltip("How long the pulse lasts.")]
    [SerializeField] private float directionalBonusRootPulseSeconds = 0.18f;

    [Header("Display")]
    [SerializeField] private string bossName = "MECH";
    [SerializeField] private bool hideWhenDead = true;
    [SerializeField] private bool hideWhenUnbound = true;
    [SerializeField] private bool useFillAmount = true;
    [SerializeField] private bool useWidthFallback = false;

    private RectTransform _fillRect;
    private float _baseWidth = -1f;
    private CanvasGroup _canvasGroup;
    private Coroutine _directionalBonusRoutine;
    private Color _fillBaseColor = Color.white;
    private Color _backgroundBaseColor = Color.white;
    private Vector3 _rootBaseScale = Vector3.one;

    private void Reset()
    {
        if (visibilityRoot == null) visibilityRoot = gameObject;

        TryAutoWire();
        ApplyStaticText();
        CacheBaseVisualState();
        ResetDirectionalBonusVisualsImmediate();
    }

    private void Awake()
    {
        if (visibilityRoot == null)
            visibilityRoot = gameObject;

        TryAutoWire();

        if (redFillImage != null)
            _fillRect = redFillImage.rectTransform;

        if (_fillRect != null)
            _baseWidth = _fillRect.sizeDelta.x;

        if (visibilityRoot == gameObject)
            _canvasGroup = GetComponent<CanvasGroup>();

        CacheBaseVisualState();
        ResetDirectionalBonusVisualsImmediate();
        ApplyStaticText();
        Redraw();
    }

    private void OnEnable()
    {
        ResetDirectionalBonusVisualsImmediate();
        ApplyStaticText();
        Redraw();
    }

    private void OnDisable()
    {
        if (_directionalBonusRoutine != null)
        {
            StopCoroutine(_directionalBonusRoutine);
            _directionalBonusRoutine = null;
        }

        ResetDirectionalBonusVisualsImmediate();
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

    public void ShowDirectionalBonus2x()
    {
        TryAutoWire();

        if (_directionalBonusRoutine != null)
            StopCoroutine(_directionalBonusRoutine);

        _directionalBonusRoutine = StartCoroutine(PlayDirectionalBonusVisuals());
    }

    private IEnumerator PlayDirectionalBonusVisuals()
    {
        ResetDirectionalBonusVisualsImmediate();

        if (directionalBonus2xObject != null)
            directionalBonus2xObject.SetActive(true);

        Color flashFillColor = directionalBonusFillFlashColor;
        flashFillColor.a = directionalBonusFlashAlpha;

        Color flashBackgroundColor = directionalBonusBackgroundFlashColor;
        flashBackgroundColor.a = directionalBonusFlashAlpha;

        if (flashFillOnDirectionalBonus && redFillImage != null)
            redFillImage.color = flashFillColor;

        if (flashBackgroundOnDirectionalBonus && backgroundFlashImage != null)
            backgroundFlashImage.color = flashBackgroundColor;

        if (pulseRootOnDirectionalBonus)
        {
            Transform pulseRoot = GetPulseRootTransform();
            if (pulseRoot != null)
                pulseRoot.localScale = _rootBaseScale * Mathf.Max(1f, directionalBonusRootPulseScaleMultiplier);
        }

        float flashDuration = Mathf.Max(0.01f, directionalBonusFlashSeconds);
        float pulseDuration = Mathf.Max(0.01f, directionalBonusRootPulseSeconds);
        float iconDuration = Mathf.Max(0.01f, directionalBonus2xShowSeconds);
        float totalDuration = Mathf.Max(iconDuration, Mathf.Max(flashDuration, pulseDuration));

        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            if (pulseRootOnDirectionalBonus)
            {
                Transform pulseRoot = GetPulseRootTransform();
                if (pulseRoot != null)
                {
                    float pulseT = Mathf.Clamp01(elapsed / pulseDuration);
                    float pulseLerp = 1f - pulseT;
                    float pulseScale = Mathf.Lerp(1f, Mathf.Max(1f, directionalBonusRootPulseScaleMultiplier), pulseLerp);
                    pulseRoot.localScale = _rootBaseScale * pulseScale;
                }
            }

            if (flashFillOnDirectionalBonus && redFillImage != null)
            {
                float flashT = Mathf.Clamp01(elapsed / flashDuration);
                redFillImage.color = Color.Lerp(flashFillColor, _fillBaseColor, flashT);
            }

            if (flashBackgroundOnDirectionalBonus && backgroundFlashImage != null)
            {
                float flashT = Mathf.Clamp01(elapsed / flashDuration);
                backgroundFlashImage.color = Color.Lerp(flashBackgroundColor, _backgroundBaseColor, flashT);
            }

            if (directionalBonus2xObject != null)
                directionalBonus2xObject.SetActive(elapsed < iconDuration);

            yield return null;
        }

        ResetDirectionalBonusVisualsImmediate();
        _directionalBonusRoutine = null;
    }

    private void ResetDirectionalBonusVisualsImmediate()
    {
        if (redFillImage != null)
            redFillImage.color = _fillBaseColor;

        if (backgroundFlashImage != null)
            backgroundFlashImage.color = _backgroundBaseColor;

        Transform pulseRoot = GetPulseRootTransform();
        if (pulseRoot != null)
            pulseRoot.localScale = _rootBaseScale == Vector3.zero ? pulseRoot.localScale : _rootBaseScale;

        HideDirectionalBonus2xImmediate();
    }

    private void HideDirectionalBonus2xImmediate()
    {
        if (directionalBonus2xObject != null)
            directionalBonus2xObject.SetActive(false);
    }

    private void TryAutoWire()
    {
        if (directionalBonus2xObject == null)
        {
            Transform t = transform.Find("doubleDamage");
            if (t == null) t = transform.Find("2x");
            if (t != null) directionalBonus2xObject = t.gameObject;
        }

        if (backgroundFlashImage == null)
        {
            string[] candidates = { "Background", "background", "BG", "bg" };
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform t = transform.Find(candidates[i]);
                if (t != null)
                {
                    backgroundFlashImage = t.GetComponent<Image>();
                    if (backgroundFlashImage != null)
                        break;
                }
            }
        }
    }

    private void CacheBaseVisualState()
    {
        if (redFillImage != null)
            _fillBaseColor = redFillImage.color;

        if (backgroundFlashImage != null)
            _backgroundBaseColor = backgroundFlashImage.color;

        Transform pulseRoot = GetPulseRootTransform();
        if (pulseRoot != null)
            _rootBaseScale = pulseRoot.localScale;
    }

    private Transform GetPulseRootTransform()
    {
        if (visibilityRoot != null)
            return visibilityRoot.transform;

        return transform;
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
