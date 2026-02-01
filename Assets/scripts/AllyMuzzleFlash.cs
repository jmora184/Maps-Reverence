using UnityEngine;

/// <summary>
/// Drop-in muzzle flash trigger for allies (works like the common "Player2Controller muzzleflash" pattern).
///
/// Goal: You already have a child GameObject called "MuzzleFlash" on the ally weapon.
/// This script gives you ONE method to call when a shot is fired, and it will reliably flash every time.
///
/// How to use (fast):
/// 1) Put this component on your Ally weapon/gun object OR on the Ally root.
/// 2) Drag your existing MuzzleFlash GameObject into "muzzleFlashObject".
///    - The muzzle flash object should have a ParticleSystem (typical).
/// 3) In your ally fire/shoot code, call: muzzleFlash.OnShotFired();
///
/// No-code option:
/// - Add an Animation Event to the fire animation and call "AnimEvent_ShotFired".
///
/// Notes:
/// - If your muzzle flash object already has MuzzleFlashPerShot, this will use it.
/// - If not, it can auto-add one at runtime (optional).
/// </summary>
public class AllyMuzzleFlash : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag your existing MuzzleFlash GameObject here (the one at the barrel tip).")]
    public GameObject muzzleFlashObject;

    [Tooltip("If left empty, we'll try to find a child named 'MuzzleFlash' under this object.")]
    public string autoFindChildName = "MuzzleFlash";

    [Header("Behavior")]
    [Tooltip("If the muzzle flash object doesn't have MuzzleFlashPerShot, auto-add it at runtime.")]
    public bool autoAddPerShotComponent = true;

    [Tooltip("If no MuzzleFlashPerShot is used, fallback to ParticleSystem.Emit() for reliability.")]
    public int fallbackEmitCount = 1;

    private MuzzleFlashPerShot _perShot;
    private ParticleSystem _ps;

    private void Awake()
    {
        Resolve();
    }

    private void OnEnable()
    {
        // Re-resolve in case this is pooled / re-enabled.
        Resolve();
    }

    private void Resolve()
    {
        if (muzzleFlashObject == null && !string.IsNullOrEmpty(autoFindChildName))
        {
            Transform t = transform.Find(autoFindChildName);
            if (t != null) muzzleFlashObject = t.gameObject;
        }

        if (muzzleFlashObject == null)
        {
            _perShot = null;
            _ps = null;
            return;
        }

        _perShot = muzzleFlashObject.GetComponent<MuzzleFlashPerShot>();
        if (_perShot == null && autoAddPerShotComponent)
        {
            _perShot = muzzleFlashObject.AddComponent<MuzzleFlashPerShot>();
        }

        // If MuzzleFlashPerShot exists, it will auto-grab its ParticleSystem in Awake,
        // but if we add it at runtime, Awake already ran; make sure it has a ps.
        if (_perShot != null)
        {
            if (_perShot.ps == null)
                _perShot.ps = muzzleFlashObject.GetComponent<ParticleSystem>();

            // Sensible defaults for reliability.
            _perShot.useEmit = true;
            _perShot.emitCount = Mathf.Max(1, fallbackEmitCount);
        }

        _ps = muzzleFlashObject.GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// Call this immediately AFTER a successful shot (right after spawning the bullet / doing the raycast).
    /// </summary>
    public void OnShotFired()
    {
        // Prefer your existing, reliable component if present.
        if (_perShot != null)
        {
            _perShot.Trigger();
            return;
        }

        // Fallback: emit directly.
        if (_ps != null)
        {
            _ps.Emit(Mathf.Max(1, fallbackEmitCount));
        }
    }

    /// <summary>
    /// Animation Event friendly method name.
    /// In the animation event, pick: AllyMuzzleFlash -> AnimEvent_ShotFired
    /// </summary>
    public void AnimEvent_ShotFired()
    {
        OnShotFired();
    }
}
