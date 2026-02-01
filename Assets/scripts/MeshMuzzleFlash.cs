using System.Collections;
using UnityEngine;

/// <summary>
/// Mesh-based muzzle flash (sphere/cubes/etc). No ParticleSystem required.
/// Designed to avoid editing large controller scripts:
/// - Put this on the SAME GameObject that has the Animator (enemy/ally root).
/// - Add an Animation Event on the fire frame that calls Flash().
///
/// The muzzle flash object can be anywhere (does NOT need to be a child of FirePoint).
/// On Flash():
/// - Snap muzzle flash to firePoint (optional)
/// - SetActive(true) briefly
/// - SetActive(false)
/// </summary>
public class MeshMuzzleFlash : MonoBehaviour
{
    [Header("Required")]
    public Transform firePoint;
    public GameObject muzzleFlashObject;

    [Header("Timing")]
    [Range(0.005f, 0.2f)]
    public float flashDuration = 0.05f;

    [Header("Options")]
    public bool forceOffOnAwake = true;
    public bool snapToFirePoint = true;
    public bool matchRotation = true;
    public Vector3 localOffset = Vector3.zero;

    [Header("Debug")]
    public bool logWhenFlashing = false;

    private Coroutine _routine;

    private void Awake()
    {
        if (forceOffOnAwake && muzzleFlashObject != null)
            muzzleFlashObject.SetActive(false);
    }

    /// <summary>
    /// Call from an Animation Event, or from code when a shot fires.
    /// </summary>
    public void Flash()
    {
        if (muzzleFlashObject == null)
            return;

        if (snapToFirePoint && firePoint != null)
        {
            muzzleFlashObject.transform.position = firePoint.TransformPoint(localOffset);
            if (matchRotation)
                muzzleFlashObject.transform.rotation = firePoint.rotation;
        }

        muzzleFlashObject.SetActive(true);

        if (logWhenFlashing)
            Debug.Log($"[MeshMuzzleFlash] Flash() on {name}", this);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(DisableAfterDelay());
    }

    private IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(flashDuration);

        if (muzzleFlashObject != null)
            muzzleFlashObject.SetActive(false);

        _routine = null;
    }

    [ContextMenu("TEST Flash")]
    private void TestFlash()
    {
        Flash();
    }
}
