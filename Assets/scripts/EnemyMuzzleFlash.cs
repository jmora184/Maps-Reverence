using UnityEngine;

/// <summary>
/// Drop-in muzzle flash trigger for ENEMIES (same pattern as AllyMuzzleFlash).
///
/// Setup:
/// 1) Put this on the enemy gun object OR the enemy root (wherever you can call it when firing).
/// 2) Drag your existing "MuzzleFlash" GameObject (at the barrel tip) into muzzleFlashObject.
/// 3) In your enemy fire code, call: enemyMuzzleFlash.OnShotFired();
///
/// Works with:
/// - MuzzleFlashPerShot (preferred). If present, it will be used.
/// - Otherwise falls back to ParticleSystem.Emit() so it flashes every shot.
///
/// Animation Event option:
/// - Add an Animation Event and call AnimEvent_ShotFired()
/// </summary>
public class EnemyMuzzleFlash : MonoBehaviour
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

        // If added at runtime, make sure ps is assigned.
        if (_perShot != null)
        {
            if (_perShot.ps == null)
                _perShot.ps = muzzleFlashObject.GetComponent<ParticleSystem>();

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
        if (_perShot != null)
        {
            _perShot.Trigger();
            return;
        }

        if (_ps != null)
        {
            _ps.Emit(Mathf.Max(1, fallbackEmitCount));
        }
    }

    /// <summary>
    /// Animation Event friendly method name.
    /// </summary>
    public void AnimEvent_ShotFired()
    {
        OnShotFired();
    }
}
