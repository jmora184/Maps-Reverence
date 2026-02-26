using UnityEngine;

/// <summary>
/// Local-space version: aims a hand weapon socket toward the camera aim point without twisting the wrist in world space.
/// Attach to the hand "weaponSocket" (child of the right hand bone).
///
/// Inspector:
/// - playerCamera: Main Camera
/// - hand: the right-hand bone transform (e.g., Bip001 R Hand)
/// - muzzle: optional barrel tip on the hand-gun (if null, socket position is used)
///
/// Notes:
/// - Uses the hand's local space and hand.up as the up vector reference to reduce corkscrew twisting.
/// - If your gun model's forward axis isn't +Z, use rotationOffsetEuler (common: (0,90,0) or (0,-90,0)).
/// </summary>
public class HandWeaponAimer_Local : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform hand;
    public Transform muzzle;
    public LayerMask aimMask = ~0;

    [Header("Tuning")]
    public float maxDistance = 200f;
    public float rotateSpeed = 25f;
    public Vector3 rotationOffsetEuler;

    private void LateUpdate()
    {
        if (!playerCamera || !hand) return;

        // 1) Compute aim target from camera center
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        Vector3 targetPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, aimMask, QueryTriggerInteraction.Ignore))
            targetPoint = hit.point;
        else
            targetPoint = ray.origin + ray.direction * maxDistance;

        // 2) Build a desired rotation in WORLD space, but using the hand's up to reduce twist
        Transform pivot = muzzle ? muzzle : transform;
        Vector3 dirWorld = targetPoint - pivot.position;
        if (dirWorld.sqrMagnitude < 0.0001f) return;

        Quaternion desiredWorld = Quaternion.LookRotation(dirWorld.normalized, hand.up);
        desiredWorld *= Quaternion.Euler(rotationOffsetEuler);

        // 3) Convert desired WORLD rotation into LOCAL rotation relative to the hand bone
        Quaternion desiredLocal = Quaternion.Inverse(hand.rotation) * desiredWorld;

        // 4) Smoothly apply LOCAL rotation to the socket
        transform.localRotation = Quaternion.Slerp(transform.localRotation, desiredLocal, rotateSpeed * Time.deltaTime);
    }
}
