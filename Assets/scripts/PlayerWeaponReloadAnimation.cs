using System.Collections;
using UnityEngine;

/// <summary>
/// Visual reload animation for the currently equipped weapon.
/// Fixes weapon-switch issues by cleaning up properly when the weapon/script is disabled mid-reload.
/// </summary>
public class PlayerWeaponReloadAnimation : MonoBehaviour
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

    [Tooltip("Magazine GameObject to hide/show (ex: AR_A_Mag). If left null, we will try to auto-find a child named 'AR_A_Mag' under the weapon visual.")]
    public GameObject magazineObject;

    [Header("Reload Audio (optional)")]
    public AudioClip reloadSFX;
    public AudioSource reloadAudioSource;
    [Range(0f, 1f)] public float reloadVolume = 1f;

    [Header("Motion (per-weapon values)")]
    public Vector3 reloadLocalOffset = new Vector3(0.05f, -0.08f, 0.22f);
    public Vector3 reloadLocalEuler = new Vector3(-18f, 6f, 0f);
    public float reloadMoveDuration = 0.10f;

    [Tooltip("How long to hold at the peak (seconds). This is where the mag swap looks best.")]
    public float peakHoldTime = 0.15f;

    [Header("Magazine Timing (normalized over the full animation time)")]
    [Range(0f, 1f)] public float hideMagAt = 0.35f;
    [Range(0f, 1f)] public float showMagAt = 0.75f;

    [Header("Fire Blocking (optional)")]
    [Tooltip("Extends Gun.fireCounter while reloading (only works if your firing respects Gun.fireCounter).")]
    public bool blockFiringDuringReload = true;

    [Tooltip("Blocks the Player2Controller shooting loop (recommended if your firing uses nextFireTime).")]
    public bool blockPlayerControllerShooting = true;

    [Tooltip("Extra time to add on top of animation when blocking (seconds).")]
    public float extraFiringBlockTime = 0.05f;

    private Transform _weaponVisual;
    private Vector3 _weaponStartPos;
    private Quaternion _weaponStartRot;
    private bool _weaponCached;

    private Coroutine _reloadCo;
    private bool _isReloading;

    private Player2Controller _blockedPlayerController;
    private bool _capturedPrevShootBlock;
    private bool _prevPlayerShootBlock;

    private void OnDisable()
    {
        CancelReloadAndReset();
    }

    private void OnDestroy()
    {
        CancelReloadAndReset();
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

        if (_reloadCo != null)
            StopCoroutine(_reloadCo);

        _reloadCo = StartCoroutine(ReloadRoutine());
    }

    /// <summary>
    /// Call this from weapon-switch code if you want an explicit cleanup before deactivating the weapon.
    /// </summary>
    public void CancelReload()
    {
        CancelReloadAndReset();
    }

    private void CancelReloadAndReset()
    {
        if (_reloadCo != null)
        {
            StopCoroutine(_reloadCo);
            _reloadCo = null;
        }

        if (_weaponVisual != null)
        {
            if (_weaponCached)
            {
                _weaponVisual.localPosition = _weaponStartPos;
                _weaponVisual.localRotation = _weaponStartRot;
            }
            else
            {
                _weaponVisual.localPosition = Vector3.zero;
                _weaponVisual.localRotation = Quaternion.identity;
            }
        }

        if (magazineObject != null)
            magazineObject.SetActive(true);

        RestorePlayerShootBlock();

        _isReloading = false;
    }

    void ResolveWeaponVisual()
    {
        if (weaponVisualRootOverride != null)
        {
            if (_weaponVisual != weaponVisualRootOverride)
            {
                _weaponVisual = weaponVisualRootOverride;
                CacheWeaponStart();
            }
            return;
        }

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
            _reloadCo = null;
            yield break;
        }

        if (!_weaponCached)
            CacheWeaponStart();

        PlayReloadSound();

        Vector3 localOffset = reloadLocalOffset;
        Vector3 localEuler = reloadLocalEuler;
        float moveDur = Mathf.Max(0.001f, reloadMoveDuration);
        float hold = Mathf.Max(0f, peakHoldTime);

        float totalTime = moveDur + hold + moveDur;

        if (blockFiringDuringReload)
        {
            float blockTime = totalTime + Mathf.Max(0f, extraFiringBlockTime);
            BlockFiring(blockTime);
        }

        if (blockPlayerControllerShooting)
        {
            _blockedPlayerController = FindPlayerController();
            if (_blockedPlayerController != null)
            {
                _prevPlayerShootBlock = _blockedPlayerController.blockShooting;
                _capturedPrevShootBlock = true;
                _blockedPlayerController.blockShooting = true;
            }
        }

        Vector3 fromPos = _weaponStartPos;
        Vector3 toPos = _weaponStartPos + localOffset;

        Quaternion fromRot = _weaponStartRot;
        Quaternion toRot = _weaponStartRot * Quaternion.Euler(localEuler);

        bool magHidden = false;
        bool magShownAgain = false;

        float elapsed = 0f;

        float t = 0f;
        while (t < 1f)
        {
            if (_weaponVisual == null) break;

            t += Time.deltaTime / moveDur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(fromPos, toPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(fromRot, toRot, s);

            elapsed += Time.deltaTime;
            HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);
            yield return null;
        }

        if (hold > 0f)
        {
            float holdT = 0f;
            while (holdT < hold)
            {
                if (_weaponVisual == null) break;

                holdT += Time.deltaTime;

                elapsed += Time.deltaTime;
                HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);
                yield return null;
            }
        }

        t = 0f;
        while (t < 1f)
        {
            if (_weaponVisual == null) break;

            t += Time.deltaTime / moveDur;
            float s = Smooth01(t);
            _weaponVisual.localPosition = Vector3.Lerp(toPos, fromPos, s);
            _weaponVisual.localRotation = Quaternion.Slerp(toRot, fromRot, s);

            elapsed += Time.deltaTime;
            HandleMagTiming(elapsed, totalTime, ref magHidden, ref magShownAgain);
            yield return null;
        }

        if (_weaponVisual != null)
        {
            _weaponVisual.localPosition = fromPos;
            _weaponVisual.localRotation = fromRot;
        }

        if (magazineObject != null)
            magazineObject.SetActive(true);

        RestorePlayerShootBlock();

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

    void PlayReloadSound()
    {
        if (reloadSFX == null) return;

        AudioSource src = ResolveReloadAudioSourceIfNeeded();
        if (src == null) return;

        src.PlayOneShot(reloadSFX, Mathf.Clamp01(reloadVolume));
    }

    AudioSource ResolveReloadAudioSourceIfNeeded()
    {
        if (reloadAudioSource != null) return reloadAudioSource;

        reloadAudioSource = GetComponent<AudioSource>();
        if (reloadAudioSource != null) return reloadAudioSource;

        reloadAudioSource = GetComponentInParent<AudioSource>();
        return reloadAudioSource;
    }

    void RestorePlayerShootBlock()
    {
        if (_capturedPrevShootBlock && _blockedPlayerController != null)
        {
            _blockedPlayerController.blockShooting = _prevPlayerShootBlock;
        }

        _blockedPlayerController = null;
        _capturedPrevShootBlock = false;
        _prevPlayerShootBlock = false;
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
