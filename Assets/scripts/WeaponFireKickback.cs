using System.Collections;
using UnityEngine;

/// <summary>
/// "Reload-controller style" recoil without an Animator:
/// - Add this to Pistol/Revolver prefab (or a child).
/// - It will animate a target transform's local position/rotation on each shot.
/// - Rifle can simply not have this component, keeping rifle recoil unchanged.
/// </summary>
public class WeaponFireKickback : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("If null, uses this transform.")]
    public Transform target;

    [Header("Kick Settings (Local Space)")]
    [Tooltip("Local position offset applied at peak kick. Example: (0, 0, -0.06)")]
    public Vector3 kickLocalPos = new Vector3(0f, 0f, -0.08f);

    [Tooltip("Local rotation offset (Euler) applied at peak kick. Example: (-10, 2, 0)")]
    public Vector3 kickLocalEuler = new Vector3(-10f, 2f, 0f);

    [Header("Timing")]
    [Tooltip("Seconds to reach peak kick.")]
    public float kickInTime = 0.05f;

    [Tooltip("Seconds to return to rest.")]
    public float returnTime = 0.10f;

    [Tooltip("Minimum time between kicks to prevent spam on automatic weapons.")]
    public float cooldown = 0.10f;

    [Header("Behavior")]
    [Tooltip("If true, uses rest pose from Awake. If false, uses current pose as rest each Play().")]
    public bool lockRestPose = true;

    private Vector3 _restPos;
    private Quaternion _restRot;
    private float _nextAllowed;
    private Coroutine _routine;

    private void Awake()
    {
        if (target == null) target = transform;
        _restPos = target.localPosition;
        _restRot = target.localRotation;
    }

    public void Play()
    {
        if (target == null) target = transform;

        if (Time.time < _nextAllowed) return;
        _nextAllowed = Time.time + Mathf.Max(0f, cooldown);

        if (!lockRestPose)
        {
            _restPos = target.localPosition;
            _restRot = target.localRotation;
        }

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(KickRoutine());
    }

    private IEnumerator KickRoutine()
    {
        Vector3 peakPos = _restPos + kickLocalPos;
        Quaternion peakRot = _restRot * Quaternion.Euler(kickLocalEuler);

        // Kick in
        float t = 0f;
        float inTime = Mathf.Max(0.001f, kickInTime);
        while (t < 1f)
        {
            t += Time.deltaTime / inTime;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            target.localPosition = Vector3.Lerp(_restPos, peakPos, s);
            target.localRotation = Quaternion.Slerp(_restRot, peakRot, s);
            yield return null;
        }

        // Return
        t = 0f;
        float outTime = Mathf.Max(0.001f, returnTime);
        while (t < 1f)
        {
            t += Time.deltaTime / outTime;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            target.localPosition = Vector3.Lerp(peakPos, _restPos, s);
            target.localRotation = Quaternion.Slerp(peakRot, _restRot, s);
            yield return null;
        }

        target.localPosition = _restPos;
        target.localRotation = _restRot;
        _routine = null;
    }

    [ContextMenu("Strong Pistol Defaults")]
    private void StrongPistolDefaults()
    {
        kickLocalPos = new Vector3(0f, 0f, -0.10f);
        kickLocalEuler = new Vector3(-14f, 3f, 0f);
        kickInTime = 0.045f;
        returnTime = 0.11f;
        cooldown = 0.12f;
        lockRestPose = true;
    }

    [ContextMenu("Mild Defaults")]
    private void MildDefaults()
    {
        kickLocalPos = new Vector3(0f, 0f, -0.05f);
        kickLocalEuler = new Vector3(-6f, 1f, 0f);
        kickInTime = 0.05f;
        returnTime = 0.10f;
        cooldown = 0.10f;
        lockRestPose = true;
    }
}
