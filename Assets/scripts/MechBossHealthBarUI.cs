using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Canvas boss-style health bar for the mech.
/// Put this on a UI object under your screen-space Canvas.
/// It binds to EnemyHealthController and updates from the existing OnHealth01Changed event.
/// 
/// Recommended setup:
/// - Root panel on your Canvas for the boss bar
/// - Fill Image set to Image Type = Filled, Fill Method = Horizontal
/// - Assign fillImage below
/// - Optional TMP labels for boss name and HP text
/// 
/// If you leave mechHealth empty, this script can auto-find the first mechController in the scene.
/// </summary>
[DisallowMultipleComponent]
public class MechBossHealthBarUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Optional root object to show/hide. If empty, this GameObject is used.")]
    [SerializeField] private GameObject visibilityRoot;

    [Tooltip("Preferred: UI Image using Filled/Horizontal.")]
    [SerializeField] private Image fillImage;

    [Tooltip("Optional alternate fill method if you prefer scaling a RectTransform instead of Image.fillAmount.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("Optional boss name label.")]
    [SerializeField] private TMP_Text bossNameText;

    [Tooltip("Optional HP readout label.")]
    [SerializeField] private TMP_Text hpText;

    [Header("Binding")]
    [Tooltip("Assign directly if your mech already exists in-scene.")]
    [SerializeField] private EnemyHealthController mechHealth;

    [Tooltip("Optional direct mech AI ref. If set, the script will grab EnemyHealthController from it.")]
    [SerializeField] private mechController mechAI;

    [Tooltip("If true, auto-find the first mechController in the scene when no target is assigned.")]
    [SerializeField] private bool autoFindMech = true;

    [Tooltip("How often to retry auto-find when no mech is bound.")]
    [SerializeField] private float autoFindInterval = 0.5f;

    [Header("Display")]
    [SerializeField] private string bossDisplayName = "MECH";
    [SerializeField] private bool hideWhenUnbound = true;
    [SerializeField] private bool hideWhenDead = true;
    [SerializeField] private bool useFillAmount = true;
    [SerializeField] private bool useScaleXFallback = false;
    [SerializeField] private bool showHpAsCurrentOverMax = true;

    private bool _isBound;
    private float _nextAutoFindTime;
    private Vector3 _fillBaseScale;
    private CanvasGroup _selfCanvasGroup;

    private void Reset()
    {
        if (visibilityRoot == null) visibilityRoot = gameObject;
        if (fillImage == null) fillImage = GetComponentInChildren<Image>(true);
        if (fillRect == null && fillImage != null) fillRect = fillImage.rectTransform;
        TryResolveTarget();
        RefreshStaticLabels();
    }

    private void Awake()
    {
        if (visibilityRoot == null) visibilityRoot = gameObject;

        if (fillRect == null && fillImage != null)
            fillRect = fillImage.rectTransform;

        if (fillRect != null)
            _fillBaseScale = fillRect.localScale;
        else
            _fillBaseScale = Vector3.one;

        if (visibilityRoot == gameObject)
            _selfCanvasGroup = GetComponent<CanvasGroup>();

        TryResolveTarget();
        RefreshStaticLabels();
    }

    private void OnEnable()
    {
        BindIfNeeded();
        RedrawImmediate();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (_isBound)
        {
            if (mechHealth == null)
            {
                Unbind();
                ApplyVisibility(false);
            }

            return;
        }

        if (!autoFindMech)
        {
            if (hideWhenUnbound) ApplyVisibility(false);
            return;
        }

        if (Time.time >= _nextAutoFindTime)
        {
            _nextAutoFindTime = Time.time + Mathf.Max(0.1f, autoFindInterval);
            TryResolveTarget();
            BindIfNeeded();
            RedrawImmediate();
        }
    }

    public void SetTarget(EnemyHealthController health)
    {
        if (mechHealth == health && _isBound) return;

        Unbind();
        mechHealth = health;
        mechAI = null;
        BindIfNeeded();
        RedrawImmediate();
    }

    public void SetTarget(mechController mech)
    {
        Unbind();
        mechAI = mech;
        mechHealth = ResolveHealthFromMech(mechAI);
        BindIfNeeded();
        RedrawImmediate();
    }

    public void ClearTarget()
    {
        Unbind();
        mechHealth = null;
        mechAI = null;
        ApplyVisibility(false);
    }

    private void TryResolveTarget()
    {
        if (mechHealth == null && mechAI != null)
            mechHealth = ResolveHealthFromMech(mechAI);

        if (mechHealth != null) return;

        if (!autoFindMech) return;

        if (mechAI == null)
            mechAI = FindObjectOfType<mechController>();

        if (mechAI != null)
            mechHealth = ResolveHealthFromMech(mechAI);
    }

    private EnemyHealthController ResolveHealthFromMech(mechController mech)
    {
        if (mech == null) return null;

        EnemyHealthController health = mech.GetComponent<EnemyHealthController>();
        if (health == null) health = mech.GetComponentInChildren<EnemyHealthController>(true);
        if (health == null) health = mech.GetComponentInParent<EnemyHealthController>();

        return health;
    }

    private void BindIfNeeded()
    {
        if (_isBound) return;

        if (mechHealth == null)
            TryResolveTarget();

        if (mechHealth == null)
        {
            if (hideWhenUnbound) ApplyVisibility(false);
            return;
        }

        mechHealth.OnHealth01Changed -= OnHealth01Changed;
        mechHealth.OnHealth01Changed += OnHealth01Changed;
        _isBound = true;
    }

    private void Unbind()
    {
        if (mechHealth != null)
            mechHealth.OnHealth01Changed -= OnHealth01Changed;

        _isBound = false;
    }

    private void RedrawImmediate()
    {
        RefreshStaticLabels();

        if (mechHealth == null)
        {
            if (hideWhenUnbound) ApplyVisibility(false);
            return;
        }

        OnHealth01Changed(mechHealth.Health01());
    }

    private void RefreshStaticLabels()
    {
        if (bossNameText != null)
            bossNameText.text = bossDisplayName;
    }

    private void OnHealth01Changed(float health01)
    {
        health01 = Mathf.Clamp01(health01);

        bool shouldShow = true;

        if (mechHealth == null)
            shouldShow = !hideWhenUnbound;

        if (hideWhenDead && mechHealth != null && mechHealth.IsDead)
            shouldShow = false;

        ApplyVisibility(shouldShow);

        if (!shouldShow)
            return;

        if (fillImage != null && useFillAmount)
            fillImage.fillAmount = health01;

        if (fillRect != null && useScaleXFallback)
        {
            Vector3 scaled = _fillBaseScale;
            scaled.x = Mathf.Max(0.0001f, _fillBaseScale.x * health01);
            fillRect.localScale = scaled;
        }

        if (hpText != null && mechHealth != null)
        {
            if (showHpAsCurrentOverMax)
                hpText.text = $"{Mathf.Max(0, mechHealth.currentHealth)} / {Mathf.Max(1, mechHealth.maxHealth)}";
            else
                hpText.text = $"{Mathf.RoundToInt(health01 * 100f)}%";
        }
    }

    private void ApplyVisibility(bool visible)
    {
        GameObject target = visibilityRoot != null ? visibilityRoot : gameObject;

        // Do not disable this script's own GameObject or auto-find/binding would stop running.
        if (target == gameObject)
        {
            if (_selfCanvasGroup == null)
                _selfCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _selfCanvasGroup.alpha = visible ? 1f : 0f;
            _selfCanvasGroup.interactable = visible;
            _selfCanvasGroup.blocksRaycasts = visible;
            return;
        }

        if (target.activeSelf != visible)
            target.SetActive(visible);
    }
}
