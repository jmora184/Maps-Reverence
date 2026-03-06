// 2026-03-05
// GrenadeThrowArmPose.cs
//
// Simple, tweakable, code-driven "left arm throws a grenade" pose/animation.
// - Rotates LeftUpperArm and LeftForearm over time using AnimationCurves
// - Trigger with a key (default: G) or call TriggerThrow() from your weapon/ability code
// - Non-destructive: stores the starting local rotations and blends back to them
//
// HOW TO USE
// 1) Add this component to your Player (or Soldier_marine root).
// 2) Assign the bone transforms:
//    - leftUpperArm: Bip001 L UpperArm
//    - leftForearm:  Bip001 L Forearm
//    Optionally leftHand: Bip001 L Hand (if you want slight hand roll)
// 3) Press G in Play Mode to test, then tweak curves/angles in Inspector.
//
// NOTES
// - This is NOT an Animator clip. It directly drives bone transforms each frame.
// - If you use Humanoid Animator with IK, it may override these rotations.
//   If so, either run this in LateUpdate (default) OR temporarily disable IK / animation layer.
// - If your model’s arm axes differ, adjust the "Axis" dropdowns or angles.

using System.Collections;
using UnityEngine;

public class GrenadeThrowArmPose : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("Bone References")]
    [SerializeField] private Transform leftUpperArm;  // Bip001 L UpperArm
    [SerializeField] private Transform leftForearm;   // Bip001 L Forearm
    [SerializeField] private Transform leftHand;      // optional

    [Header("Test Trigger")]
    [SerializeField] private bool enableTestKey = true;
    [SerializeField] private KeyCode testKey = KeyCode.G;

    [Header("Timing")]
    [Tooltip("Total duration of the throw motion (seconds).")]
    [Min(0.05f)] public float duration = 0.55f;

    [Tooltip("How long to blend back to the original pose after the throw.")]
    [Min(0.0f)] public float returnDuration = 0.20f;

    [Tooltip("If true, arm returns to the starting pose automatically.")]
    public bool autoReturn = true;

    [Tooltip("If true, motion uses unscaled time (ignores timescale).")]
    public bool useUnscaledTime = false;

    [Header("Upper Arm Rotation")]
    public Axis upperAxis1 = Axis.X;
    public float upperAxis1Degrees = -55f;

    public Axis upperAxis2 = Axis.Z;
    public float upperAxis2Degrees = 35f;

    [Tooltip("Curve: 0..1 time -> 0..1 intensity for upper arm.")]
    public AnimationCurve upperCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Forearm Rotation")]
    public Axis foreAxis1 = Axis.X;
    public float foreAxis1Degrees = -35f;

    public Axis foreAxis2 = Axis.Y;
    public float foreAxis2Degrees = -20f;

    [Tooltip("Curve: 0..1 time -> 0..1 intensity for forearm.")]
    public AnimationCurve foreCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Optional Hand Rotation")]
    public bool driveHand = false;
    public Axis handAxis = Axis.Z;
    public float handDegrees = 15f;
    public AnimationCurve handCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Blending")]
    [Tooltip("If true, applies in LateUpdate (helps override Animator).")]
    public bool applyInLateUpdate = true;

    [Tooltip("If true, you can stack/retrigger while playing (will restart).")]
    public bool restartIfTriggered = true;

    [Header("Debug")]
    public bool debugLog = false;

    // Cached start pose
    private Quaternion _upperStart;
    private Quaternion _foreStart;
    private Quaternion _handStart;

    private bool _hasCached;
    private bool _isPlaying;

    // Current frame targets (computed by coroutine, applied in LateUpdate or Update)
    private Quaternion _upperTarget;
    private Quaternion _foreTarget;
    private Quaternion _handTarget;

    private Coroutine _co;

    private void Awake()
    {
        CacheStartPose();
        ResetTargetsToStart();
    }

    private void CacheStartPose()
    {
        if (leftUpperArm != null) _upperStart = leftUpperArm.localRotation;
        if (leftForearm != null) _foreStart = leftForearm.localRotation;
        if (leftHand != null) _handStart = leftHand.localRotation;
        _hasCached = true;
    }

    private void ResetTargetsToStart()
    {
        _upperTarget = _upperStart;
        _foreTarget = _foreStart;
        _handTarget = _handStart;
    }

    private void Update()
    {
        if (enableTestKey && Input.GetKeyDown(testKey))
            TriggerThrow();

        if (!applyInLateUpdate)
            ApplyTargets();
    }

    private void LateUpdate()
    {
        if (applyInLateUpdate)
            ApplyTargets();
    }

    private void ApplyTargets()
    {
        if (!_hasCached) CacheStartPose();

        if (leftUpperArm != null) leftUpperArm.localRotation = _upperTarget;
        if (leftForearm != null) leftForearm.localRotation = _foreTarget;
        if (driveHand && leftHand != null) leftHand.localRotation = _handTarget;
        else if (leftHand != null && !_isPlaying) leftHand.localRotation = _handStart;
    }

    /// Call this from your grenade logic.
    public void TriggerThrow()
    {
        if (leftUpperArm == null || leftForearm == null)
        {
            Debug.LogWarning("[GrenadeThrowArmPose] Missing bone references (leftUpperArm/leftForearm).", this);
            return;
        }

        if (!_hasCached) CacheStartPose();

        if (_isPlaying)
        {
            if (!restartIfTriggered) return;

            if (_co != null) StopCoroutine(_co);
            _isPlaying = false;
        }

        _co = StartCoroutine(CoThrow());
    }

    private IEnumerator CoThrow()
    {
        _isPlaying = true;

        // Ensure start pose is current (in case Animator changed it since Awake)
        _upperStart = leftUpperArm.localRotation;
        _foreStart = leftForearm.localRotation;
        if (leftHand != null) _handStart = leftHand.localRotation;

        float t = 0f;
        float inv = 1f / Mathf.Max(0.0001f, duration);

        if (debugLog) Debug.Log("[GrenadeThrowArmPose] Throw started.", this);

        // 1) Blend from start -> throw pose
        while (t < duration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;

            float u = Mathf.Clamp01(t * inv);

            float upperAlpha = Mathf.Clamp01(upperCurve != null ? upperCurve.Evaluate(u) : u);
            float foreAlpha  = Mathf.Clamp01(foreCurve != null  ? foreCurve.Evaluate(u)  : u);
            float handAlpha  = Mathf.Clamp01(handCurve != null  ? handCurve.Evaluate(u)  : u);

            Quaternion upperPose = ComposeOffsetRotation(_upperStart, upperAxis1, upperAxis1Degrees, upperAxis2, upperAxis2Degrees, upperAlpha);
            Quaternion forePose  = ComposeOffsetRotation(_foreStart,  foreAxis1,  foreAxis1Degrees,  foreAxis2,  foreAxis2Degrees,  foreAlpha);

            _upperTarget = upperPose;
            _foreTarget = forePose;

            if (driveHand && leftHand != null)
                _handTarget = ComposeSingleOffsetRotation(_handStart, handAxis, handDegrees, handAlpha);

            yield return null;
        }

        // Hold at peak for a tiny moment (feels like "release")
        float hold = 0.05f;
        float ht = 0f;
        while (ht < hold)
        {
            ht += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        if (!autoReturn || returnDuration <= 0f)
        {
            _isPlaying = false;
            if (debugLog) Debug.Log("[GrenadeThrowArmPose] Throw finished (no return).", this);
            yield break;
        }

        // 2) Blend back to start pose
        float r = 0f;
        float rinv = 1f / Mathf.Max(0.0001f, returnDuration);

        Quaternion upperFrom = _upperTarget;
        Quaternion foreFrom = _foreTarget;
        Quaternion handFrom = _handTarget;

        while (r < returnDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            r += dt;

            float u = Mathf.Clamp01(r * rinv);
            float a = Smooth01(u);

            _upperTarget = Quaternion.Slerp(upperFrom, _upperStart, a);
            _foreTarget  = Quaternion.Slerp(foreFrom,  _foreStart,  a);

            if (driveHand && leftHand != null)
                _handTarget = Quaternion.Slerp(handFrom, _handStart, a);

            yield return null;
        }

        ResetTargetsToStart();

        _isPlaying = false;
        if (debugLog) Debug.Log("[GrenadeThrowArmPose] Throw finished (returned).", this);
    }

    private static float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x); // smoothstep
    }

    private static Quaternion ComposeOffsetRotation(
        Quaternion start,
        Axis a1, float deg1,
        Axis a2, float deg2,
        float alpha)
    {
        // Apply two local-axis rotations as offsets from start pose.
        Quaternion r1 = Quaternion.AngleAxis(deg1 * alpha, AxisVector(a1));
        Quaternion r2 = Quaternion.AngleAxis(deg2 * alpha, AxisVector(a2));
        return start * r1 * r2;
    }

    private static Quaternion ComposeSingleOffsetRotation(
        Quaternion start,
        Axis a, float deg,
        float alpha)
    {
        Quaternion r = Quaternion.AngleAxis(deg * alpha, AxisVector(a));
        return start * r;
    }

    private static Vector3 AxisVector(Axis axis)
    {
        switch (axis)
        {
            case Axis.X: return Vector3.right;
            case Axis.Y: return Vector3.up;
            default:     return Vector3.forward;
        }
    }
}
