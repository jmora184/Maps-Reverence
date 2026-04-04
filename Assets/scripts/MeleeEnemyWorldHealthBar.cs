// 2026-03-05
// MeleeEnemyWorldHealthBar.cs
//
// World-space overhead health bar for your melee enemy (SpriteRenderer-based),
// modeled after EnemyWorldHealthBar, but bound to MeleeEnemyHealthController.
//
// Drop this script on the HealthBar object (a child above the head).
//
// Recommended hierarchy:
//   MeleeEnemyRoot
//     - MeleeEnemy2Controller
//     - MeleeEnemyHealthController
//     HealthBar   <-- (empty GameObject positioned above head; add this script here)
//       Bar       <-- SpriteRenderer using your green bar sprite (pivot ideally left-center)
//       (Optional) Background <-- SpriteRenderer not scaled
//       (Optional) DirectionalBonus2x / 2x <-- SpriteRenderer for the temporary 2x icon
//
// Notes:
// - This script POLLS health (no events needed).
// - If your Bar sprite pivot is centered, enable keepLeftEdgeFixed to compensate.
// - Billboard makes the bar face the camera.
//
// Works with: MeleeEnemyHealthController (maxHealth/currentHealth, IsDead) from your project.

using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class MeleeEnemyWorldHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the green bar SpriteRenderer (your 'Bar' child).")]
    [SerializeField] private SpriteRenderer barSprite;

    [Tooltip("Optional: background sprite (not scaled).")]
    [SerializeField] private SpriteRenderer backgroundSprite;

    [Tooltip("Optional: sprite used to briefly show a 2x bonus damage icon.")]
    [SerializeField] private SpriteRenderer directionalBonus2xSprite;

    [Tooltip("If set, this transform will be billboarding instead of this GameObject's transform.")]
    [SerializeField] private Transform billboardRoot;

    [Header("Binding")]
    [Tooltip("If empty, auto-finds MeleeEnemyHealthController in parents/children of this object.")]
    [SerializeField] private MeleeEnemyHealthController health;

    [Header("Visual Behavior")]
    [Tooltip("If true, shrinks bar by localScale.x (simple).")]
    [SerializeField] private bool useLocalScale = true;

    [Tooltip("If not using localScale, uses SpriteRenderer.size.x (requires Draw Mode = Sliced or Tiled).")]
    [SerializeField] private bool useSpriteSize = false;

    [Tooltip("If true, keeps the LEFT edge fixed while shrinking (useful if pivot is centered).")]
    [SerializeField] private bool keepLeftEdgeFixed = true;

    [Tooltip("Hide when full health.")]
    [SerializeField] private bool hideWhenFull = false;

    [Tooltip("Hide when dead / health <= 0.")]
    [SerializeField] private bool hideWhenZero = true;

    [Header("Directional Bonus UI")]
    [Tooltip("How long the 2x icon stays visible.")]
    [Min(0f)] [SerializeField] private float directionalBonusShowTime = 0.6f;

    [Tooltip("If true, hides the 2x icon automatically on Awake/OnEnable.")]
    [SerializeField] private bool hideDirectionalBonusOnStart = true;

    [Header("Directional Bonus Flash")]
    [Tooltip("If true, briefly flashes the fill bar when directional bonus damage happens.")]
    [SerializeField] private bool flashBarOnDirectionalBonus = true;

    [Tooltip("If true, briefly flashes the background too. This is easier to see than only tinting a red fill bar.")]
    [SerializeField] private bool flashBackgroundOnDirectionalBonus = true;

    [Tooltip("Bright flash color for the fill bar.")]
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

    [Header("Billboard")]
    [Tooltip("If true, the health bar always faces the camera.")]
    [SerializeField] private bool faceCamera = true;

    [Tooltip("If true, uses Camera.main. Otherwise uses explicitCamera.")]
    [SerializeField] private bool useMainCamera = true;

    [SerializeField] private Camera explicitCamera;

    [Tooltip("If true, only billboard yaw (no tilt up/down).")]
    [SerializeField] private bool yawOnly = false;

    [Header("Update Rate")]
    [Tooltip("How often (seconds) to refresh the bar from health. 0 = every frame.")]
    [Min(0f)] [SerializeField] private float refreshInterval = 0.05f;

    // Cached base values
    private Vector3 _barBaseScale;
    private Vector3 _barBaseLocalPos;
    private float _barBaseSizeX;
    private Vector3 _rootBaseScale;
    private Color _barBaseColor = Color.white;
    private Color _backgroundBaseColor = Color.white;

    private float _nextRefreshTime;
    private Coroutine _directionalBonusRoutine;

    private void Reset()
    {
        billboardRoot = transform;
        TryAutoWire();
        CacheBaseVisualState();
        ResetDirectionalBonusVisualsImmediate();
    }

    private void Awake()
    {
        if (billboardRoot == null)
            billboardRoot = transform;

        TryAutoWire();
        CacheBaseVisualState();

        if (hideDirectionalBonusOnStart)
            ResetDirectionalBonusVisualsImmediate();
    }

    private void OnEnable()
    {
        _nextRefreshTime = 0f;

        if (hideDirectionalBonusOnStart)
            ResetDirectionalBonusVisualsImmediate();

        RedrawImmediate();
    }

    private void OnDisable()
    {
        if (_directionalBonusRoutine != null)
        {
            StopCoroutine(_directionalBonusRoutine);
            _directionalBonusRoutine = null;
        }

        if (hideDirectionalBonusOnStart)
            ResetDirectionalBonusVisualsImmediate();
    }

    private void LateUpdate()
    {
        if (health == null || barSprite == null)
            TryAutoWire();

        if (faceCamera)
            DoBillboard();

        if (health == null || barSprite == null)
            return;

        if (refreshInterval <= 0f || Time.time >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.time + refreshInterval;
            OnHealthChanged(Health01());
        }
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

        if (directionalBonus2xSprite != null)
        {
            directionalBonus2xSprite.gameObject.SetActive(true);
            directionalBonus2xSprite.enabled = true;
        }

        Color flashBarColor = directionalBonusBarFlashColor;
        flashBarColor.a = directionalBonusFlashAlpha;

        Color flashBackgroundColor = directionalBonusBackgroundFlashColor;
        flashBackgroundColor.a = directionalBonusFlashAlpha;

        if (flashBarOnDirectionalBonus && barSprite != null)
            barSprite.color = flashBarColor;

        if (flashBackgroundOnDirectionalBonus && backgroundSprite != null)
            backgroundSprite.color = flashBackgroundColor;

        if (pulseRootOnDirectionalBonus)
            transform.localScale = _rootBaseScale * Mathf.Max(1f, directionalBonusRootPulseScaleMultiplier);

        float flashDuration = Mathf.Max(0.01f, directionalBonusFlashSeconds);
        float pulseDuration = Mathf.Max(0.01f, directionalBonusRootPulseSeconds);
        float iconDuration = Mathf.Max(0.01f, directionalBonusShowTime);
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

            yield return null;
        }

        ResetDirectionalBonusVisualsImmediate();
        _directionalBonusRoutine = null;
    }

    private void ResetDirectionalBonusVisualsImmediate()
    {
        transform.localScale = _rootBaseScale == Vector3.zero ? transform.localScale : _rootBaseScale;

        if (barSprite != null)
            barSprite.color = _barBaseColor;

        if (backgroundSprite != null)
            backgroundSprite.color = _backgroundBaseColor;

        HideDirectionalBonusImmediate();
    }

    private void HideDirectionalBonusImmediate()
    {
        if (directionalBonus2xSprite != null)
            directionalBonus2xSprite.enabled = false;
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
            _backgroundBaseColor = backgroundSprite.color;
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
            if (t == null) t = transform.Find("Background");
            if (t != null) backgroundSprite = t.GetComponent<SpriteRenderer>();
        }

        if (directionalBonus2xSprite == null)
        {
            Transform bonus = transform.Find("DirectionalBonus2x");
            if (bonus == null) bonus = transform.Find("2x");
            if (bonus != null) directionalBonus2xSprite = bonus.GetComponent<SpriteRenderer>();
        }

        if (health == null)
        {
            health = GetComponentInParent<MeleeEnemyHealthController>();
            if (health == null) health = GetComponentInChildren<MeleeEnemyHealthController>(true);
        }
    }

    private void RedrawImmediate()
    {
        if (health != null)
            OnHealthChanged(Health01());
    }

    private float Health01()
    {
        if (health == null) return 1f;
        int max = Mathf.Max(1, health.maxHealth);
        int cur = Mathf.Clamp(health.currentHealth, 0, max);
        return (float)cur / max;
    }

    private void OnHealthChanged(float t)
    {
        t = Mathf.Clamp01(t);

        bool shouldShow = true;

        bool isDead = false;
        try { isDead = health != null && health.IsDead; } catch { }

        if (hideWhenZero && (t <= 0.0001f || isDead)) shouldShow = false;
        if (hideWhenFull && t >= 0.9999f) shouldShow = false;

        if (backgroundSprite != null) backgroundSprite.enabled = shouldShow;
        if (barSprite != null) barSprite.enabled = shouldShow;
        if (!shouldShow) HideDirectionalBonusImmediate();

        if (!shouldShow || barSprite == null) return;

        if (useSpriteSize && !useLocalScale)
        {
            Vector2 sz = barSprite.size;
            sz.x = Mathf.Max(0.0001f, _barBaseSizeX * t);
            barSprite.size = sz;

            if (keepLeftEdgeFixed)
            {
                float delta = (_barBaseSizeX - sz.x) * 0.5f;
                barSprite.transform.localPosition = _barBaseLocalPos + new Vector3(-delta, 0f, 0f);
            }
        }
        else
        {
            Vector3 s = _barBaseScale;
            s.x *= t;
            barSprite.transform.localScale = new Vector3(Mathf.Max(0.0001f, s.x), s.y, s.z);

            if (keepLeftEdgeFixed)
            {
                float lost = _barBaseScale.x - s.x;
                float shift = lost * 0.5f;
                barSprite.transform.localPosition = _barBaseLocalPos + new Vector3(-shift, 0f, 0f);
            }
        }
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
