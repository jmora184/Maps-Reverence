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
//       (Optional) DirectionalBonus2x <-- SpriteRenderer for the temporary 2x icon
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

    private float _nextRefreshTime;
    private Coroutine _directionalBonusRoutine;

    private void Reset()
    {
        billboardRoot = transform;
        TryAutoWire();
        HideDirectionalBonusImmediate();
    }

    private void Awake()
    {
        if (billboardRoot == null) billboardRoot = transform;

        TryAutoWire();

        if (barSprite != null)
        {
            _barBaseScale = barSprite.transform.localScale;
            _barBaseLocalPos = barSprite.transform.localPosition;
            _barBaseSizeX = barSprite.size.x;
        }

        if (hideDirectionalBonusOnStart)
            HideDirectionalBonusImmediate();
    }

    private void OnEnable()
    {
        _nextRefreshTime = 0f;

        if (hideDirectionalBonusOnStart)
            HideDirectionalBonusImmediate();

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
            HideDirectionalBonusImmediate();
    }

    private void LateUpdate()
    {
        if (health == null || barSprite == null)
            TryAutoWire();

        if (faceCamera) DoBillboard();

        if (health == null || barSprite == null) return;

        if (refreshInterval <= 0f || Time.time >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.time + refreshInterval;
            OnHealthChanged(Health01());
        }
    }

    public void ShowDirectionalBonus2x()
    {
        if (directionalBonus2xSprite == null)
            TryAutoWire();

        if (directionalBonus2xSprite == null)
            return;

        directionalBonus2xSprite.enabled = true;

        if (_directionalBonusRoutine != null)
            StopCoroutine(_directionalBonusRoutine);

        if (directionalBonusShowTime <= 0f)
        {
            HideDirectionalBonusImmediate();
            return;
        }

        _directionalBonusRoutine = StartCoroutine(HideDirectionalBonusAfterDelay());
    }

    private IEnumerator HideDirectionalBonusAfterDelay()
    {
        yield return new WaitForSeconds(directionalBonusShowTime);
        HideDirectionalBonusImmediate();
        _directionalBonusRoutine = null;
    }

    private void HideDirectionalBonusImmediate()
    {
        if (directionalBonus2xSprite != null)
            directionalBonus2xSprite.enabled = false;
    }

    private void TryAutoWire()
    {
        if (barSprite == null)
        {
            var t = transform.Find("Bar");
            if (t != null) barSprite = t.GetComponent<SpriteRenderer>();
        }

        if (directionalBonus2xSprite == null)
        {
            var bonus = transform.Find("DirectionalBonus2x");
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

        // Prefer IsDead if available, but still respect actual health.
        bool isDead = false;
        try { isDead = health != null && health.IsDead; } catch { /* ignore */ }

        if (hideWhenZero && (t <= 0.0001f || isDead)) shouldShow = false;
        if (hideWhenFull && t >= 0.9999f) shouldShow = false;

        if (backgroundSprite != null) backgroundSprite.enabled = shouldShow;
        if (barSprite != null) barSprite.enabled = shouldShow;
        if (!shouldShow) HideDirectionalBonusImmediate();

        if (!shouldShow || barSprite == null) return;

        if (useSpriteSize && !useLocalScale)
        {
            var sz = barSprite.size;
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
            var s = _barBaseScale;
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
        var cam = GetCamera();
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
