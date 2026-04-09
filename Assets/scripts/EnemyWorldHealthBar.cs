// 2026-02-21 AI-Tag
// EnemyWorldHealthBar.cs
//
// World-space overhead health bar for enemies (SpriteRenderer-based), matching the Ally version.
// Drives a green "Bar" SpriteRenderer by shrinking X based on EnemyHealthController.Health01().
//
// Setup:
// - Enemy root has EnemyHealthController
// - Create child empty: HealthBar (position above head)
// - Under HealthBar create child: Bar (SpriteRenderer) using your green bar sprite
// - (Optional) add background sprite as a sibling and assign it
//
// Best visual: set the Bar sprite pivot to Custom (X=0, Y=0.5) in Sprite Editor.
// If you can't, leave keepLeftEdgeFixed = true to compensate.
//
// Drop this script on the HealthBar object.

using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyWorldHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the green bar SpriteRenderer (your 'Bar' child).")]
    [SerializeField] private SpriteRenderer barSprite;

    [Tooltip("Optional: background sprite (not scaled).")]
    [SerializeField] private SpriteRenderer backgroundSprite;

    [Tooltip("If set, this transform will be billboarding instead of this GameObject's transform.")]
    [SerializeField] private Transform billboardRoot;

    [Header("Directional Bonus 2x")]
    [Tooltip("Optional sprite shown briefly when directional bonus damage (2x) is applied.")]
    [SerializeField] private SpriteRenderer directionalBonus2xSprite;

    [Tooltip("How long the 2x sprite stays visible after being triggered.")]
    [SerializeField] private float directionalBonus2xShowSeconds = 0.5f;

    [Header("Directional Bonus Flash")]
    [Tooltip("If true, briefly flashes the fill bar when directional bonus damage happens.")]
    [SerializeField] private bool flashBarOnDirectionalBonus = true;

    [Tooltip("If true, briefly flashes the background too. This is easier to see than only tinting a red fill bar.")]
    [SerializeField] private bool flashBackgroundOnDirectionalBonus = true;

    [Tooltip("If true, and the background flashes, it temporarily shrinks to the SAME current width as the live fill bar so it does not look like the enemy regained full health.")]
    [SerializeField] private bool matchBackgroundFlashToCurrentHealth = true;

    [Tooltip("Bright flash color for the fill bar. Using a bright yellow/white reads better than orange on top of red.")]
    [SerializeField] private Color directionalBonusBarFlashColor = new Color(1f, 0.95f, 0.45f, 1f);

    [Tooltip("Flash color for the background bar.")]
    [SerializeField] private Color directionalBonusBackgroundFlashColor = new Color(1f, 0.65f, 0.15f, 1f);

    [Tooltip("Alpha used during the flash.")]
    [Range(0f, 1f)]
    [SerializeField] private float directionalBonusFlashAlpha = 1f;

    [Tooltip("How long the bar/background flash lasts.")]
    [SerializeField] private float directionalBonusFlashSeconds = 0.2f;

    [Header("Directional Bonus Pulse")]
    [Tooltip("If true, the whole health bar pulses slightly when directional bonus damage happens.")]
    [SerializeField] private bool pulseRootOnDirectionalBonus = true;

    [Tooltip("How much to scale the health bar root during the pulse.")]
    [SerializeField] private float directionalBonusRootPulseScaleMultiplier = 1.12f;

    [Tooltip("How long the pulse lasts.")]
    [SerializeField] private float directionalBonusRootPulseSeconds = 0.18f;

    [Header("Binding")]
    [Tooltip("If empty, auto-finds EnemyHealthController in parents/children of this object.")]
    [SerializeField] private EnemyHealthController enemyHealth;

    [Header("Visual Behavior")]
    [Tooltip("If true, shrinks bar by localScale.x (simple).")]
    [SerializeField] private bool useLocalScale = true;

    [Tooltip("If not using localScale, uses SpriteRenderer.size.x (requires Draw Mode = Sliced or Tiled).")]
    [SerializeField] private bool useSpriteSize = false;

    [Tooltip("If true, keeps the LEFT edge fixed while shrinking (useful if pivot is centered).")]
    [SerializeField] private bool keepLeftEdgeFixed = true;

    [Tooltip("Hide when full health.")]
    [SerializeField] private bool hideWhenFull = false;

    [Tooltip("Hide when dead.")]
    [SerializeField] private bool hideWhenZero = true;

    [Header("Billboard")]
    [Tooltip("If true, the health bar always faces the camera.")]
    [SerializeField] private bool faceCamera = true;

    [Tooltip("If true, uses Camera.main. Otherwise uses explicitCamera.")]
    [SerializeField] private bool useMainCamera = true;

    [SerializeField] private Camera explicitCamera;

    [Tooltip("If true, only billboard yaw (no tilt up/down).")]
    [SerializeField] private bool yawOnly = false;

    // Cached base values
    private Vector3 _barBaseScale;
    private Vector3 _barBaseLocalPos;
    private float _barBaseSizeX;

    private Vector3 _backgroundBaseScale;
    private Vector3 _backgroundBaseLocalPos;
    private float _backgroundBaseSizeX;

    private Vector3 _directionalBonus2xBaseLocalScale = Vector3.one;
    private Transform _directionalBonus2xParent;
    private Vector3 _directionalBonus2xParentBaseLocalScale = Vector3.one;

    private Vector3 _rootBaseScale;
    private Color _barBaseColor = Color.white;
    private Color _backgroundBaseColor = Color.white;
    private bool _isBound;
    private Coroutine _directionalBonusRoutine;
    private float _currentHealth01 = 1f;
    private bool _directionalBackgroundFlashUsingHealthWidth = false;

    private void Reset()
    {
        billboardRoot = transform;
        TryAutoWire();
    }

    private void Awake()
    {
        if (billboardRoot == null)
            billboardRoot = transform;

        TryAutoWire();
        CacheBaseVisualState();
        ResetDirectionalBonusVisualsImmediate();
    }

    private void OnEnable()
    {
        ResetDirectionalBonusVisualsImmediate();
        BindIfNeeded();
        RedrawImmediate();
    }

    private void OnDisable()
    {
        if (_directionalBonusRoutine != null)
        {
            StopCoroutine(_directionalBonusRoutine);
            _directionalBonusRoutine = null;
        }

        ResetDirectionalBonusVisualsImmediate();
        Unbind();
    }

    private void LateUpdate()
    {
        if (!_isBound)
            BindIfNeeded();

        if (faceCamera)
            DoBillboard();

        MaintainDirectionalBonus2xVisualScale();
    }

    private void TryAutoWire()
    {
        if (barSprite == null)
        {
            Transform t = transform.Find("Bar");
            if (t != null) barSprite = t.GetComponent<SpriteRenderer>();
        }

        if (backgroundSprite == null)
        {
            Transform t = transform.Find("background");
            if (t != null) backgroundSprite = t.GetComponent<SpriteRenderer>();
        }

        if (enemyHealth == null)
        {
            enemyHealth = GetComponentInParent<EnemyHealthController>();
            if (enemyHealth == null) enemyHealth = GetComponentInChildren<EnemyHealthController>(true);
        }

        if (directionalBonus2xSprite == null)
        {
            Transform t = transform.Find("2x");
            if (t != null) directionalBonus2xSprite = t.GetComponent<SpriteRenderer>();
        }
    }

    private void CacheBaseVisualState()
    {
        _rootBaseScale = transform.localScale;

        if (barSprite != null)
        {
            _barBaseScale = barSprite.transform.localScale;
            _barBaseLocalPos = barSprite.transform.localPosition;
            _barBaseSizeX = barSprite.size.x;
            _barBaseColor = barSprite.color;
        }

        if (backgroundSprite != null)
        {
            _backgroundBaseScale = backgroundSprite.transform.localScale;
            _backgroundBaseLocalPos = backgroundSprite.transform.localPosition;
            _backgroundBaseSizeX = backgroundSprite.size.x;
            _backgroundBaseColor = backgroundSprite.color;
        }

        if (directionalBonus2xSprite != null)
        {
            _directionalBonus2xBaseLocalScale = directionalBonus2xSprite.transform.localScale;
            _directionalBonus2xParent = directionalBonus2xSprite.transform.parent;
            _directionalBonus2xParentBaseLocalScale = _directionalBonus2xParent != null
                ? _directionalBonus2xParent.localScale
                : Vector3.one;
        }
    }

    private void BindIfNeeded()
    {
        if (_isBound) return;

        if (enemyHealth == null) TryAutoWire();
        if (enemyHealth == null) return;

        enemyHealth.OnHealth01Changed -= OnHealth01Changed;
        enemyHealth.OnHealth01Changed += OnHealth01Changed;
        _isBound = true;
    }

    private void Unbind()
    {
        if (enemyHealth != null)
            enemyHealth.OnHealth01Changed -= OnHealth01Changed;

        _isBound = false;
    }

    private void RedrawImmediate()
    {
        if (enemyHealth != null)
            OnHealth01Changed(enemyHealth.Health01());
    }

    private void OnHealth01Changed(float t)
    {
        t = Mathf.Clamp01(t);
        _currentHealth01 = t;

        bool shouldShow = true;
        if (hideWhenZero && t <= 0.0001f) shouldShow = false;
        if (hideWhenFull && t >= 0.9999f) shouldShow = false;

        if (backgroundSprite != null) backgroundSprite.enabled = shouldShow;
        if (barSprite != null) barSprite.enabled = shouldShow;

        if (!shouldShow || barSprite == null) return;

        ApplyFillVisual(barSprite, _barBaseScale, _barBaseLocalPos, _barBaseSizeX, t);

        if (_directionalBackgroundFlashUsingHealthWidth && backgroundSprite != null)
            ApplyFillVisual(backgroundSprite, _backgroundBaseScale, _backgroundBaseLocalPos, _backgroundBaseSizeX, t);

        MaintainDirectionalBonus2xVisualScale();
    }

    private void ApplyFillVisual(SpriteRenderer targetSprite, Vector3 baseScale, Vector3 baseLocalPos, float baseSizeX, float t)
    {
        if (targetSprite == null)
            return;

        if (useSpriteSize && !useLocalScale)
        {
            Vector2 sz = targetSprite.size;
            sz.x = Mathf.Max(0.0001f, baseSizeX * t);
            targetSprite.size = sz;

            if (keepLeftEdgeFixed)
            {
                float delta = (baseSizeX - sz.x) * 0.5f;
                targetSprite.transform.localPosition = baseLocalPos + new Vector3(-delta, 0f, 0f);
            }
            else
            {
                targetSprite.transform.localPosition = baseLocalPos;
            }
        }
        else
        {
            Vector3 s = baseScale;
            s.x *= t;
            targetSprite.transform.localScale = new Vector3(Mathf.Max(0.0001f, s.x), s.y, s.z);

            if (keepLeftEdgeFixed)
            {
                float lost = baseScale.x - s.x;
                float shift = lost * 0.5f;
                targetSprite.transform.localPosition = baseLocalPos + new Vector3(-shift, 0f, 0f);
            }
            else
            {
                targetSprite.transform.localPosition = baseLocalPos;
            }
        }
    }

    private void ResetFillVisual(SpriteRenderer targetSprite, Vector3 baseScale, Vector3 baseLocalPos, float baseSizeX)
    {
        if (targetSprite == null)
            return;

        if (useSpriteSize && !useLocalScale)
        {
            Vector2 sz = targetSprite.size;
            sz.x = baseSizeX;
            targetSprite.size = sz;
        }
        else
        {
            targetSprite.transform.localScale = baseScale;
        }

        targetSprite.transform.localPosition = baseLocalPos;
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
        // Start from clean base every time so repeated side hits do not stack weirdly.
        ResetDirectionalBonusVisualsImmediate();

        if (directionalBonus2xSprite != null)
        {
            directionalBonus2xSprite.gameObject.SetActive(true);
            directionalBonus2xSprite.enabled = true;
            MaintainDirectionalBonus2xVisualScale();
        }

        Color flashBarColor = directionalBonusBarFlashColor;
        flashBarColor.a = directionalBonusFlashAlpha;

        Color flashBackgroundColor = directionalBonusBackgroundFlashColor;
        flashBackgroundColor.a = directionalBonusFlashAlpha;

        if (flashBarOnDirectionalBonus && barSprite != null)
            barSprite.color = flashBarColor;

        _directionalBackgroundFlashUsingHealthWidth =
            flashBackgroundOnDirectionalBonus &&
            backgroundSprite != null &&
            matchBackgroundFlashToCurrentHealth;

        if (_directionalBackgroundFlashUsingHealthWidth)
            ApplyFillVisual(backgroundSprite, _backgroundBaseScale, _backgroundBaseLocalPos, _backgroundBaseSizeX, _currentHealth01);

        if (flashBackgroundOnDirectionalBonus && backgroundSprite != null)
            backgroundSprite.color = flashBackgroundColor;

        if (pulseRootOnDirectionalBonus)
            transform.localScale = _rootBaseScale * Mathf.Max(1f, directionalBonusRootPulseScaleMultiplier);

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
                float pulseT = Mathf.Clamp01(elapsed / pulseDuration);
                float pulseLerp = 1f - pulseT;
                float pulseScale = Mathf.Lerp(1f, Mathf.Max(1f, directionalBonusRootPulseScaleMultiplier), pulseLerp);
                transform.localScale = _rootBaseScale * pulseScale;
            }

            if (flashBarOnDirectionalBonus && barSprite != null)
            {
                float flashT = Mathf.Clamp01(elapsed / flashDuration);
                barSprite.color = Color.Lerp(flashBarColor, _barBaseColor, flashT);
            }

            if (flashBackgroundOnDirectionalBonus && backgroundSprite != null)
            {
                float flashT = Mathf.Clamp01(elapsed / flashDuration);
                backgroundSprite.color = Color.Lerp(flashBackgroundColor, _backgroundBaseColor, flashT);
            }

            if (directionalBonus2xSprite != null)
                directionalBonus2xSprite.enabled = elapsed < iconDuration;

            MaintainDirectionalBonus2xVisualScale();

            yield return null;
        }

        ResetDirectionalBonusVisualsImmediate();
        _directionalBonusRoutine = null;
    }

    private void ResetDirectionalBonusVisualsImmediate()
    {
        transform.localScale = _rootBaseScale == Vector3.zero ? transform.localScale : _rootBaseScale;
        _directionalBackgroundFlashUsingHealthWidth = false;

        if (barSprite != null)
            barSprite.color = _barBaseColor;

        if (backgroundSprite != null)
        {
            backgroundSprite.color = _backgroundBaseColor;
            ResetFillVisual(backgroundSprite, _backgroundBaseScale, _backgroundBaseLocalPos, _backgroundBaseSizeX);
        }

        HideDirectionalBonus2xImmediate();
    }

    private void HideDirectionalBonus2xImmediate()
    {
        if (directionalBonus2xSprite == null)
            return;

        directionalBonus2xSprite.transform.localScale = _directionalBonus2xBaseLocalScale;
        directionalBonus2xSprite.enabled = false;
    }

    private void MaintainDirectionalBonus2xVisualScale()
    {
        if (directionalBonus2xSprite == null || !directionalBonus2xSprite.enabled)
            return;

        Transform iconTransform = directionalBonus2xSprite.transform;
        Transform parent = iconTransform.parent;

        if (parent == null || parent == transform)
        {
            iconTransform.localScale = _directionalBonus2xBaseLocalScale;
            return;
        }

        if (_directionalBonus2xParent != parent)
        {
            _directionalBonus2xParent = parent;
            _directionalBonus2xParentBaseLocalScale = parent.localScale;
            _directionalBonus2xBaseLocalScale = iconTransform.localScale;
        }

        Vector3 parentBaseScale = _directionalBonus2xParentBaseLocalScale;
        Vector3 parentCurrentScale = parent.localScale;

        iconTransform.localScale = new Vector3(
            _directionalBonus2xBaseLocalScale.x * SafeDivide(parentBaseScale.x, parentCurrentScale.x),
            _directionalBonus2xBaseLocalScale.y * SafeDivide(parentBaseScale.y, parentCurrentScale.y),
            _directionalBonus2xBaseLocalScale.z * SafeDivide(parentBaseScale.z, parentCurrentScale.z));
    }

    private float SafeDivide(float value, float divisor)
    {
        if (Mathf.Abs(divisor) < 0.0001f)
            return value;

        return value / divisor;
    }

    private void DoBillboard()
    {
        Camera cam = GetCamera();
        if (cam == null) return;

        Transform root = billboardRoot != null ? billboardRoot : transform;

        Vector3 toCam = root.position - cam.transform.position;
        if (toCam.sqrMagnitude < 0.0001f) return;

        if (yawOnly)
        {
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) return;
        }

        root.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    private Camera GetCamera()
    {
        if (!useMainCamera && explicitCamera != null) return explicitCamera;
        if (useMainCamera) return Camera.main;
        return explicitCamera;
    }
}
