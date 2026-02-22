using System.Collections;
using UnityEngine;

/// <summary>
/// Pistol reload animation (separate from PlayerWeaponReloadAnimation).
///
/// Behaves the same as your rifle reload animation:
/// - Animates weapon local position/rotation to a "reload pose" then back.
/// - Optionally blocks firing while running.
/// - Instead of disabling/enabling the magazine object, it SLIDES the magazine along LOCAL X:
///     insertedX (-0.0057) -> removedX (-0.04) -> insertedX
///
/// Attach to your Pistol (or the same place you attached the rifle version).
/// Assign:
///   - Weapon Visual Root Override (optional)
///   - Magazine Transform (the pistol magazine transform)
/// Then tune the motion/ timings as needed.
/// </summary>
public class PlayerPistolReloadAnimation : MonoBehaviour
{
    // Called by WeaponAmmo via UnityEvent (OnReloadRequested)
    public void RequestReload()
    {
        TryReload();
    }

    [Header("Input")]
    public KeyCode reloadKey = KeyCode.N;

    [Header("References")]
    [Tooltip("Optional: weapon parent/holder to animate instead of the gun root. If null, we auto-use the active Gun transform.")]
    public Transform weaponVisualRootOverride;

    [Tooltip("Magazine Transform to slide in/out (pistol mag). If null, we try to auto-find a child named 'Magazine' under the weapon visual.")]
    public Transform magazineTransform;

    [Header("Motion (per-weapon values)")]
    public Vector3 reloadLocalOffset = new Vector3(0.05f, -0.08f, 0.22f);
    public Vector3 reloadLocalEuler = new Vector3(-18f, 6f, 0f);
    public float reloadMoveDuration = 0.10f;

    [Tooltip("How long to hold at the peak (seconds). This is where the mag slide looks best.")]
    public float peakHoldTime = 0.15f;

    [Header("Magazine Slide (local X)")]
    [Tooltip("Local X when the magazine is inserted (your normal value).")]
    public float insertedLocalX = -0.0057f;

    [Tooltip("Local X when the magazine is pulled out.")]
    public float removedLocalX = -0.04f;

    [Tooltip("If true, we capture the magazine's starting local X on first resolve and use that as Inserted X.")]
    public bool autoCaptureInsertedX = true;

    [Tooltip("Seconds to smooth the magazine slide when switching positions (0 = instant).")]
    public float magSlideDuration = 0.05f;

    [Header("Magazine Timing (normalized over the full animation time)")]
    [Range(0f, 1f)] public float pullMagAt = 0.35f;   // was "hideMagAt"
    [Range(0f, 1f)] public float insertMagAt = 0.75f; // was "showMagAt"

    [Header("Fire Blocking (optional)")]
    [Tooltip("Extends Gun.fireCounter while reloading (only works if your firing respects Gun.fireCounter).")]
    public bool blockFiringDuringReload = true;

    [Tooltip("Blocks the Player2Controller shooting loop (recommended if your firing uses blockShooting).")]
    public bool blockPlayerControllerShooting = true;

    [Tooltip("Extra time to add on top of animation when blocking (seconds).")]
    public float extraFiringBlockTime = 0.05f;

    private Transform _weaponVisual;
    private Vector3 _weaponStartPos;
    private Quaternion _weaponStartRot;
    private bool _weaponCached;

    private Vector3 _magStartLocalPos;
    private bool _magCached;

    private Coroutine _reloadCo;
    private bool _isReloading;

    private Coroutine _magSlideCo;

    void Update()
    {
        if (Input.GetKeyDown(reloadKey))
            TryReload();
    }

    public void TryReload()
    {
        if (_isReloading) return;

        ResolveWeaponVisual();
        if (_weaponVisual == null) return;

        ResolveMagazineTransform();

        if (_reloadCo != null) StopCoroutine(_reloadCo);
        _reloadCo = StartCoroutine(ReloadRoutine());
    }

    void ResolveWeaponVisual()
    {
        // Prefer explicit override on THIS script.
        if (weaponVisualRootOverride != null)
        {
            if (_weaponVisual != weaponVisualRootOverride)
            {
                _weaponVisual = weaponVisualRootOverride;
                CacheWeaponStart();
            }
            return;
        }

        // Otherwise animate the active gun root.
        Gun activeGun = FindActiveGun();
        if (activeGun != null && _weaponVisual != activeGun.transform)
        {
            _weaponVisual = activeGun.transform;
            CacheWeaponStart();
        }
    }

    void ResolveMagazineTransform()
    {
        if (magazineTransform == null && _weaponVisual != null)
        {
            // Best-effort auto-find (you can rename this to match your pistol mag object if needed).
            Transform t = FindDeepChild(_weaponVisual, "Magazine");
            if (t == null) t = FindDeepChild(_weaponVisual, "Mag");
            if (t == null) t = FindDeepChild(_weaponVisual, "AR_A_Mag"); // fallback if you keep same name
            if (t != null) magazineTransform = t;
        }

        if (magazineTransform != null && !_magCached)
        {
            _magStartLocalPos = magazineTransform.localPosition;
            _magCached = true;

            if (autoCaptureInsertedX)
                insertedLocalX = _magStartLocalPos.x;
        }
    }

    void CacheWeaponStart()
    {
        if (_weaponVisual == null) return;
        _weaponStartPos = _weaponVisual.localPosition;
        _weaponStartRot = _weaponVisual.localRotation;
        _weaponCached = true;
    }

    IEnumerator ReloadRoutine()
    {
        _isReloading = true;

        if (_weaponVisual == null)
        {
            _isReloading = false;
            yield break;
        }

        if (!_weaponCached) CacheWeaponStart();

        Vector3 localOffset = reloadLocalOffset;
        Vector3 localEuler = reloadLocalEuler;
        float moveDur = Mathf.Max(0.001f, reloadMoveDuration);
        float hold = Mathf.Max(0f, peakHoldTime);

        float totalTime = moveDur + hold + moveDur;

        // Fire blocking for the full animation.
        if (blockFiringDuringReload)
        {
            float blockTime = totalTime + Mathf.Max(0f, extraFiringBlockTime);
            BlockFiring(blockTime);
        }

        // Also block the Player2Controller shooting loop.
        bool prevPlayerShootBlock = false;
        bool capturedPrevBlock = false;
        if (blockPlayerControllerShooting)
        {
            var pc = FindPlayerController();
            if (pc != null)
            {
                prevPlayerShootBlock = pc.blockShooting;
                capturedPrevBlock = true;
                pc.blockShooting = true;
            }
        }

        Vector3 fromPos = _weaponStartPos;
        Vector3 toPos = _weaponStartPos + localOffset;

        Quaternion fromRot = _weaponStartRot;
        Quaternion toRot = _weaponStartRot * Quaternion.Euler(localEuler);

        bool magPulled = false;
        bool magInsertedAgain = false;

        float elapsed = 0f;

        // Forward
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / moveDur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(fromPos, toPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(fromRot, toRot, s);

            elapsed += Time.deltaTime;
            HandleMagTiming(elapsed, totalTime, ref magPulled, ref magInsertedAgain);
            yield return null;
        }

        // Hold at peak
        if (hold > 0f)
        {
            float holdT = 0f;
            while (holdT < hold)
            {
                holdT += Time.deltaTime;

                elapsed += Time.deltaTime;
                HandleMagTiming(elapsed, totalTime, ref magPulled, ref magInsertedAgain);
                yield return null;
            }
        }

        // Return
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / moveDur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(toPos, fromPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(toRot, fromRot, s);

            elapsed += Time.deltaTime;
            HandleMagTiming(elapsed, totalTime, ref magPulled, ref magInsertedAgain);
            yield return null;
        }

        // Ensure we end cleanly.
        _weaponVisual.localPosition = fromPos;
        _weaponVisual.localRotation = fromRot;

        // Ensure mag ends "inserted"
        SetMagazineLocalX(insertedLocalX, instant: true);

        // Restore previous shooting block state if we changed it.
        if (capturedPrevBlock)
        {
            var pc = FindPlayerController();
            if (pc != null) pc.blockShooting = prevPlayerShootBlock;
        }

        _isReloading = false;
        _reloadCo = null;
    }

    void HandleMagTiming(float elapsed, float totalTime, ref bool magPulled, ref bool magInsertedAgain)
    {
        if (magazineTransform == null) return;

        float n = totalTime <= 0f ? 1f : Mathf.Clamp01(elapsed / totalTime);

        if (!magPulled && n >= pullMagAt)
        {
            SetMagazineLocalX(removedLocalX, instant: magSlideDuration <= 0f);
            magPulled = true;
        }

        if (!magInsertedAgain && n >= insertMagAt)
        {
            SetMagazineLocalX(insertedLocalX, instant: magSlideDuration <= 0f);
            magInsertedAgain = true;
        }
    }

    void SetMagazineLocalX(float targetX, bool instant)
    {
        if (magazineTransform == null) return;

        if (_magSlideCo != null)
        {
            StopCoroutine(_magSlideCo);
            _magSlideCo = null;
        }

        if (instant)
        {
            Vector3 p = magazineTransform.localPosition;
            p.x = targetX;
            magazineTransform.localPosition = p;
            return;
        }

        _magSlideCo = StartCoroutine(SlideMagXRoutine(targetX, magSlideDuration));
    }

    IEnumerator SlideMagXRoutine(float targetX, float dur)
    {
        if (magazineTransform == null) yield break;

        dur = Mathf.Max(0.001f, dur);
        Vector3 start = magazineTransform.localPosition;
        Vector3 end = start; end.x = targetX;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float s = Smooth01(t);
            magazineTransform.localPosition = Vector3.Lerp(start, end, s);
            yield return null;
        }

        magazineTransform.localPosition = end;
        _magSlideCo = null;
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void BlockFiring(float blockTime)
    {
        Gun g = FindActiveGun();
        if (g == null) return;
        g.fireCounter = Mathf.Max(g.fireCounter, blockTime);
    }

    Player2Controller FindPlayerController()
    {
        if (Player2Controller.instance != null) return Player2Controller.instance;

        var pc = GetComponentInParent<Player2Controller>();
        if (pc != null) return pc;

        return FindObjectOfType<Player2Controller>();
    }

    Gun FindActiveGun()
    {
        Gun[] guns = GetComponentsInChildren<Gun>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            if (guns[i] != null && guns[i].gameObject.activeInHierarchy)
                return guns[i];
        }
        return null;
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;

        var q = new System.Collections.Generic.Queue<Transform>();
        q.Enqueue(parent);

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (t.name == name) return t;

            for (int i = 0; i < t.childCount; i++)
                q.Enqueue(t.GetChild(i));
        }

        return null;
    }
}
