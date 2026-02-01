using System.Collections;
using UnityEngine;

/// <summary>
/// Simple muzzle flash that does NOT require the muzzle flash object to be a child of FirePoint.
/// On each shot it:
/// 1) Moves the muzzle flash object to match a given "firePoint" transform (position + rotation)
/// 2) Enables it briefly
/// 3) Disables it again
///
/// Usage:
/// - Put this script on your Enemy/Ally (or gun).
/// - Assign:
///   firePoint: the barrel tip transform (your existing Fire Point)
///   muzzleFlashObject: your existing muzzle flash GameObject (can live anywhere in the hierarchy)
/// - Ensure the muzzle flash particle system is set to Play On Awake (so it plays when enabled).
/// - Call Flash() when the shot is fired (right after bullet spawn / raycast).
/// </summary>
public class SimpleMuzzleFlashToggleFollow : MonoBehaviour
{
    [Header("Required")]
    [Tooltip("Where the muzzle flash should appear (your barrel tip / Fire Point transform).")]
    public Transform firePoint;

    [Tooltip("Your muzzle flash GameObject. It can be anywhere in the hierarchy (NOT a child of firePoint).")]
    public GameObject muzzleFlashObject;

    [Header("Timing")]
    [Range(0.005f, 0.2f)]
    [Tooltip("How long the muzzle flash stays enabled (seconds). Typical 0.03 - 0.08")]
    public float flashDuration = 0.05f;

    [Header("Options")]
    [Tooltip("Force muzzle flash off on Awake (recommended).")]
    public bool forceOffOnAwake = true;

    [Tooltip("If true, sets the muzzle flash rotation to match the firePoint rotation.")]
    public bool matchRotation = true;

    [Tooltip("Optional extra offset from firePoint in local space (e.g., push slightly forward).")]
    public Vector3 localOffset = Vector3.zero;

    private Coroutine _routine;

    private void Awake()
    {
        if (forceOffOnAwake && muzzleFlashObject != null)
            muzzleFlashObject.SetActive(false);
    }

    /// <summary>
    /// Call this when a shot is actually fired.
    /// </summary>
    public void Flash()
    {
        if (firePoint == null || muzzleFlashObject == null)
            return;

        // Snap muzzle flash to barrel
        muzzleFlashObject.transform.position = firePoint.TransformPoint(localOffset);

        if (matchRotation)
            muzzleFlashObject.transform.rotation = firePoint.rotation;

        // Enable briefly
        muzzleFlashObject.SetActive(true);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(DisableAfterDelay());
    }

    /// <summary>
    /// Animation Event friendly call.
    /// </summary>
    public void AnimEvent_Flash()
    {
        Flash();
    }

    private IEnumerator DisableAfterDelay()
    {
        yield return new WaitForSeconds(flashDuration);

        if (muzzleFlashObject != null)
            muzzleFlashObject.SetActive(false);

        _routine = null;
    }
}
