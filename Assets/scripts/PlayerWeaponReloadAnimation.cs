using System.Collections;
using UnityEngine;

/// <summary>
/// Simple "reload animation" that:
/// 1) Plays a weapon swing using the SAME motion values as PlayerWeaponMeleePunch (optional)
/// 2) Temporarily hides the magazine GameObject (AR_A_Mag) and shows it again
/// 3) (Optional) blocks firing by extending Gun.fireCounter while the animation runs
///
/// This is ONLY the visual animation + mag toggle (no ammo logic yet).
/// </summary>
public class PlayerWeaponReloadAnimation : MonoBehaviour
{
    [Header("Input")]
    public KeyCode reloadKey = KeyCode.N;

    [Header("References")]
    [Tooltip("Optional: If assigned, we copy the exact motion values (offset/euler/duration) from this script.")]
    public PlayerWeaponMeleePunch meleePunchSource;

    [Tooltip("Optional: weapon parent/holder to animate instead of the gun root. If null, we auto-use the active Gun transform.")]
    public Transform weaponVisualRootOverride;

    [Tooltip("Magazine GameObject to hide/show (ex: AR_A_Mag). If left null, we will try to auto-find a child named 'AR_A_Mag' under the weapon visual.")]
    public GameObject magazineObject;

    [Header("Motion (used when Copy Motion From Melee is OFF)")]
    public bool copyMotionFromMelee = true;
    public Vector3 reloadLocalOffset = new Vector3(0.05f, -0.08f, 0.22f);
    public Vector3 reloadLocalEuler = new Vector3(-18f, 6f, 0f);
    public float reloadMoveDuration = 0.10f;

    [Tooltip("How long to hold at the peak (seconds). This is where the mag swap looks best.")]
    public float peakHoldTime = 0.15f;

    [Header("Magazine Timing (normalized over the full animation time)")]
    [Range(0f, 1f)] public float hideMagAt = 0.35f;
    [Range(0f, 1f)] public float showMagAt = 0.75f;

    [Header("Fire Blocking (optional)")]
    public bool blockFiringDuringReload = true;


    [Tooltip("Also blocks the Player2Controller shooting loop (your current firing code uses nextFireTime, not Gun.fireCounter).")]
    public bool blockPlayerControllerShooting = true;
    [Tooltip("Extra time to add on top of animation when blocking (seconds).")]
    public float extraFiringBlockTime = 0.05f;

    Transform _weaponVisual;
    Vector3 _weaponStartPos;
    Quaternion _weaponStartRot;
    bool _weaponCached;

    Coroutine _reloadCo;
    bool _isReloading;

    void Reset()
    {
        // Try to auto-wire a melee punch script on the same object if present.
        meleePunchSource = GetComponent<PlayerWeaponMeleePunch>();
    }

    void Awake()
    {
        if (meleePunchSource == null)
            meleePunchSource = GetComponent<PlayerWeaponMeleePunch>();
    }

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

        ResolveMagazineObject();

        // Start animation
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

        // Otherwise, if we're copying from melee, reuse the melee override if it exists.
        if (copyMotionFromMelee && meleePunchSource != null && meleePunchSource.weaponVisualRootOverride != null)
        {
            if (_weaponVisual != meleePunchSource.weaponVisualRootOverride)
            {
                _weaponVisual = meleePunchSource.weaponVisualRootOverride;
                CacheWeaponStart();
            }
            return;
        }

        // Otherwise animate the active gun root (same approach as melee script).
        Gun activeGun = FindActiveGun();
        if (activeGun != null && _weaponVisual != activeGun.transform)
        {
            _weaponVisual = activeGun.transform;
            CacheWeaponStart();
        }
    }

    void ResolveMagazineObject()
    {
        if (magazineObject != null) return;
        if (_weaponVisual == null) return;

        // Try to find AR_A_Mag automatically.
        Transform t = FindDeepChild(_weaponVisual, "AR_A_Mag");
        if (t != null) magazineObject = t.gameObject;
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

        // Pull motion values (either copy from melee or use local fields).
        Vector3 localOffset;
        Vector3 localEuler;
        float moveDur;

        if (copyMotionFromMelee && meleePunchSource != null)
        {
            localOffset = meleePunchSource.punchLocalOffset;
            localEuler = meleePunchSource.punchLocalEuler;
            moveDur = meleePunchSource.punchDuration;
        }
        else
        {
            localOffset = reloadLocalOffset;
            localEuler = reloadLocalEuler;
            moveDur = reloadMoveDuration;
        }

        moveDur = Mathf.Max(0.001f, moveDur);
        float hold = Mathf.Max(0f, peakHoldTime);

        // Total time for normalized timing.
        float totalTime = moveDur + hold + moveDur;

        // Fire blocking for the full animation.
        if (blockFiringDuringReload)
        {
            float blockTime = totalTime + Mathf.Max(0f, extraFiringBlockTime);
            BlockFiring(blockTime);
        }


        // Also block the actual Player2Controller shooting loop (your current firing path doesn't read Gun.fireCounter).
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
        // Setup positions/rotations.
        Vector3 fromPos = _weaponStartPos;
        Vector3 toPos = _weaponStartPos + localOffset;

        Quaternion fromRot = _weaponStartRot;
        Quaternion toRot = _weaponStartRot * Quaternion.Euler(localEuler);

        bool magHidden = false;
        bool magShownAgain = false;

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
            HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);

            yield return null;
        }

        // Hold at peak (for the "mag swap" moment)
        if (hold > 0f)
        {
            float holdT = 0f;
            while (holdT < hold)
            {
                holdT += Time.deltaTime;

                elapsed += Time.deltaTime;
                HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);

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
            HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);

            yield return null;
        }

        // Ensure we end cleanly.
        _weaponVisual.localPosition = fromPos;
        _weaponVisual.localRotation = fromRot;

        // Make sure mag ends visible.
        if (magazineObject != null)
            magazineObject.SetActive(true);

        // Restore previous shooting block state if we changed it.
        if (capturedPrevBlock)
        {
            var pc = FindPlayerController();
            if (pc != null) pc.blockShooting = prevPlayerShootBlock;
        }

        _isReloading = false;
        _reloadCo = null;
    }

    void HandleMagTiming(float elapsed, float totalTime, ref bool magHidden, ref bool magShownAgain)
    {
        if (magazineObject == null) return;

        float n = totalTime <= 0f ? 1f : Mathf.Clamp01(elapsed / totalTime);

        if (!magHidden && n >= hideMagAt)
        {
            magazineObject.SetActive(false);
            magHidden = true;
        }

        if (!magShownAgain && n >= showMagAt)
        {
            magazineObject.SetActive(true);
            magShownAgain = true;
        }
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
        // Prefer the singleton if your project uses it.
        if (Player2Controller.instance != null) return Player2Controller.instance;

        // Fallback: try find in parents.
        var pc = GetComponentInParent<Player2Controller>();
        if (pc != null) return pc;

        // Last resort: scene search.
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

        // Breadth-first search to avoid recursion depth issues.
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
