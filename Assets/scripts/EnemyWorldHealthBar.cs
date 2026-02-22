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
    private bool _isBound;

    private void Reset()
    {
        billboardRoot = transform;
        TryAutoWire();
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

    private void LateUpdate()
    {
        if (!_isBound) BindIfNeeded();
        if (faceCamera) DoBillboard();
    }

    private void TryAutoWire()
    {
        if (barSprite == null)
        {
            var t = transform.Find("Bar");
            if (t != null) barSprite = t.GetComponent<SpriteRenderer>();
        }

        if (enemyHealth == null)
        {
            enemyHealth = GetComponentInParent<EnemyHealthController>();
            if (enemyHealth == null) enemyHealth = GetComponentInChildren<EnemyHealthController>(true);
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

        bool shouldShow = true;
        if (hideWhenZero && t <= 0.0001f) shouldShow = false;
        if (hideWhenFull && t >= 0.9999f) shouldShow = false;

        if (backgroundSprite != null) backgroundSprite.enabled = shouldShow;
        if (barSprite != null) barSprite.enabled = shouldShow;

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
