using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple locomotion animator driver that prevents "ice skating" by also scaling Animator playback speed
/// to match the NavMeshAgent velocity.
///
/// Uses 2 bools: isWalking, isRunning (as you have).
/// Optional: Force walk mode for patrol.
/// </summary>
[DisallowMultipleComponent]
public class SimpleLocomotionAnimator : MonoBehaviour
{
    [Header("References")]
    public NavMeshAgent agent;
    public Animator animator;

    [Header("Animator Params")]
    public string walkBool = "isWalking";
    public string runBool = "isRunning";

    [Header("Thresholds")]
    [Tooltip("Velocity above this counts as moving.")]
    public float moveThreshold = 0.08f;

    [Tooltip("If not forcing walk, velocity above this becomes running.")]
    public float runThreshold = 1.9f;

    [Header("Mode")]
    [Tooltip("If true, we will never set isRunning true; movement becomes walking.")]
    public bool forceWalk = false;

    [Header("Anti-Slide (Animator Playback Speed)")]
    [Tooltip("When walking, the speed (m/s) that should correspond to animator.speed = 1.")]
    public float walkMetersPerSecondAtAnimSpeed1 = 1.8f;

    [Tooltip("When running, the speed (m/s) that should correspond to animator.speed = 1.")]
    public float runMetersPerSecondAtAnimSpeed1 = 3.5f;

    [Tooltip("Clamp animator.speed so it never goes crazy.")]
    public Vector2 animatorSpeedClamp = new Vector2(0.75f, 1.35f);

    [Tooltip("Smooth animation speed changes.")]
    public float animatorSpeedDamp = 0.10f;

    private float _animSpeedVel;

    private void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        // Reset starting state so we don't begin in Run due to other scripts setting params on Awake/Start.
        if (animator)
        {
            if (!string.IsNullOrEmpty(walkBool)) animator.SetBool(walkBool, false);
            if (!string.IsNullOrEmpty(runBool)) animator.SetBool(runBool, false);
            animator.speed = 1f;
        }

    }

    private void LateUpdate()
    {
        if (!agent || !animator) return;

        float v = agent.velocity.magnitude;
        bool moving = v > moveThreshold;

        bool walking = moving && (forceWalk || v < runThreshold);
        bool running = moving && (!forceWalk && v >= runThreshold);

        if (!string.IsNullOrEmpty(walkBool)) animator.SetBool(walkBool, walking);
        if (!string.IsNullOrEmpty(runBool)) animator.SetBool(runBool, running);

        // --- Anti-slide: scale playback speed to match how fast the agent is actually moving ---
        float targetAnimSpeed = 1f;

        if (moving)
        {
            if (walking)
            {
                float denom = Mathf.Max(0.01f, walkMetersPerSecondAtAnimSpeed1);
                targetAnimSpeed = v / denom;
            }
            else if (running)
            {
                float denom = Mathf.Max(0.01f, runMetersPerSecondAtAnimSpeed1);
                targetAnimSpeed = v / denom;
            }
        }

        targetAnimSpeed = Mathf.Clamp(targetAnimSpeed, animatorSpeedClamp.x, animatorSpeedClamp.y);

        // Smooth it to reduce jitter
        float next = Mathf.SmoothDamp(animator.speed, targetAnimSpeed, ref _animSpeedVel, animatorSpeedDamp);
        animator.speed = next;
    }

    /// <summary>Call this from other scripts (patrol) to force walk.</summary>
    public void SetForceWalk(bool enabled) => forceWalk = enabled;
}