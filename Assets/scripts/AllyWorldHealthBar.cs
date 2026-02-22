// 2026-02-21 AI-Tag
// AllyWorldHealthBar.cs
//
// Drop this on the Ally's "HealthBar" root (the parent of your green bar sprite).
// It will find AllyHealth on the ally and drive the bar width based on Health01().
//
// Works with SpriteRenderer shrinking (via localScale.x) and supports optional billboarding.
//
// Setup (recommended):
// - Ally (root) has AllyHealth component somewhere (root or child)
// - Create child: HealthBar (empty) positioned above head
// - Inside HealthBar, create child: Bar (SpriteRenderer) assigned below
// - IMPORTANT: Set the Bar sprite's pivot to LEFT in Sprite Editor (best), so it shrinks from left.
//   If you can't, this script can also "shrink from left" by shifting position if you enable keepLeftEdgeFixed.
//
// Jeremiah / Maps and Reverence

using UnityEngine;

[DisallowMultipleComponent]
public class AllyWorldHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the green bar SpriteRenderer (your 'Bar' child).")]
    [SerializeField] private SpriteRenderer barSprite;

    [Tooltip("Optional: If you have a separate background sprite, keep it here (not scaled).")]
    [SerializeField] private SpriteRenderer backgroundSprite;

    [Tooltip("If set, this transform will be billboarding instead of this GameObject's transform.")]
    [SerializeField] private Transform billboardRoot;

    [Header("Binding")]
    [Tooltip("If empty, auto-finds AllyHealth in parents/children of this object.")]
    [SerializeField] private AllyHealth allyHealth;

    [Tooltip("If true, also tries to locate AllyHealth by searching the tagged Ally root.")]
    [SerializeField] private bool searchTaggedAllyRoot = true;

    [Header("Visual Behavior")]
    [Tooltip("Shrink method: using localScale.x is simplest, but can 'squish' borders if your sprite has them.")]
    [SerializeField] private bool useLocalScale = true;

    [Tooltip("If not using localScale, uses SpriteRenderer.size.x (requires Draw Mode = Sliced or Tiled).")]
    [SerializeField] private bool useSpriteSize = false;

    [Tooltip("If true, keeps the LEFT edge of the bar fixed while shrinking (even if pivot is centered).")]
    [SerializeField] private bool keepLeftEdgeFixed = true;

    [Tooltip("Health bar hides when health is full (1.0).")]
    [SerializeField] private bool hideWhenFull = false;

    [Tooltip("Health bar hides when health is zero (0.0).")]
    [SerializeField] private bool hideWhenZero = true;

    [Header("Billboard")]
    [Tooltip("If true, the health bar always faces the camera.")]
    [SerializeField] private bool faceCamera = true;

    [Tooltip("If true, uses Camera.main. Otherwise uses explicitCamera.")]
    [SerializeField] private bool useMainCamera = true;

    [SerializeField] private Camera explicitCamera;

    [Tooltip("Lock rotation axes (useful if you only want yaw billboarding).")]
    [SerializeField] private bool yawOnly = false;

    [Header("Distance Culling (optional)")]
    [Tooltip("If > 0, hides the bar when farther than this distance from camera.")]
    [SerializeField] private float maxVisibleDistance = 0f;

    // cached original values
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

        if (maxVisibleDistance > 0f)
        {
            var cam = GetCamera();
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, transform.position);
                bool show = d <= maxVisibleDistance;
                if (gameObject.activeSelf != show) gameObject.SetActive(show); // note: disables script too
                return;
            }
        }
    }

    private void TryAutoWire()
    {
        if (barSprite == null)
        {
            // try the most obvious child name
            var t = transform.Find("Bar");
            if (t != null) barSprite = t.GetComponent<SpriteRenderer>();
        }

        if (allyHealth == null)
        {
            allyHealth = GetComponentInParent<AllyHealth>();
            if (allyHealth == null) allyHealth = GetComponentInChildren<AllyHealth>(true);
        }

        if (searchTaggedAllyRoot && allyHealth == null)
        {
            var allyRoot = FindTaggedAllyRoot(transform);
            if (allyRoot != null)
            {
                allyHealth = allyRoot.GetComponentInChildren<AllyHealth>(true);
                if (allyHealth == null) allyHealth = allyRoot.GetComponentInParent<AllyHealth>();
            }
        }
    }

    private void BindIfNeeded()
    {
        if (_isBound) return;

        if (allyHealth == null) TryAutoWire();
        if (allyHealth == null) return;

        allyHealth.OnHealth01Changed -= OnHealth01Changed;
        allyHealth.OnHealth01Changed += OnHealth01Changed;
        _isBound = true;
    }

    private void Unbind()
    {
        if (allyHealth != null)
            allyHealth.OnHealth01Changed -= OnHealth01Changed;

        _isBound = false;
    }

    private void RedrawImmediate()
    {
        if (allyHealth != null)
            OnHealth01Changed(allyHealth.Health01());
    }

    private void OnHealth01Changed(float t)
    {
        t = Mathf.Clamp01(t);

        // Hide logic (optional)
        bool shouldShow = true;
        if (hideWhenZero && t <= 0.0001f) shouldShow = false;
        if (hideWhenFull && t >= 0.9999f) shouldShow = false;

        if (backgroundSprite != null) backgroundSprite.enabled = shouldShow;
        if (barSprite != null) barSprite.enabled = shouldShow;

        if (!shouldShow || barSprite == null) return;

        // Apply fill
        if (useSpriteSize && !useLocalScale)
        {
            // Requires barSprite.drawMode = Sliced or Tiled for size to have an effect.
            var sz = barSprite.size;
            sz.x = Mathf.Max(0.0001f, _barBaseSizeX * t);
            barSprite.size = sz;

            if (keepLeftEdgeFixed)
            {
                // Maintain left edge by shifting local position right/left half of lost size
                float delta = (_barBaseSizeX - sz.x) * 0.5f;
                barSprite.transform.localPosition = _barBaseLocalPos + new Vector3(-delta, 0f, 0f);
            }
        }
        else
        {
            // Default: scale x
            var s = _barBaseScale;
            s.x *= t;
            barSprite.transform.localScale = new Vector3(Mathf.Max(0.0001f, s.x), s.y, s.z);

            if (keepLeftEdgeFixed)
            {
                // If pivot is centered, compensate position so left edge stays in place.
                // If your sprite pivot is already left, you can turn this off.
                float lost = _barBaseScale.x - (s.x);
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

    private static Transform FindTaggedAllyRoot(Transform t)
    {
        Transform cur = t;
        while (cur != null)
        {
            if (cur.CompareTag("Ally"))
                return cur;
            cur = cur.parent;
        }
        return null;
    }
}
