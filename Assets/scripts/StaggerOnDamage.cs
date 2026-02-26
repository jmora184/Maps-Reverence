using System.Collections;
using UnityEngine;

/// <summary>
/// Accumulates incoming damage in a short window and triggers a hit-react animation
/// when the threshold is reached (e.g., two 5-damage hits quickly => 10).
///
/// IMPORTANT: Includes a cooldown so automatic rifles don't keep re-triggering stagger
/// every few frames once the enemy is already reacting.
/// </summary>
public class StaggerOnDamage : MonoBehaviour
{
    [Header("Stagger Threshold")]
    [Tooltip("Total damage required within the time window to trigger take_damage.")]
    public int staggerThreshold = 10;

    [Tooltip("Seconds in which damage must accumulate to count toward the threshold.")]
    public float windowSeconds = 0.35f;

    [Tooltip("If true, a single hit that is >= threshold triggers immediately (e.g., revolver 10).")]
    public bool allowSingleHitToTrigger = true;

    [Header("Stagger Cooldown / Reset")]
    [Tooltip("Minimum seconds between stagger triggers. Prevents automatic weapons from chain-staggering.")]
    public float staggerCooldownSeconds = 0.75f;

    [Tooltip("If true, ignore incoming damage for staggering while we're in hit-react lock (IsHitReacting).")]
    public bool ignoreWhileHitReacting = true;

    [Header("Animator")]
    public Animator animator;

    [Tooltip("Animator Trigger name for the hit animation.")]
    public string takeDamageTrigger = "take_damage";

    [Tooltip("Animator Bool name used by some graphs for hit reacting.")]
    public string hitReactBool = "HitReact";

    [Tooltip("Optional: gate other transitions (like shoot) while hit reacting.")]
    public string isHitReactingBool = "IsHitReacting";

    [Tooltip("How long to keep IsHitReacting true (seconds). If 0, uses windowSeconds.")]
    public float hitReactLockSeconds = 0.0f;

    private int _bucket;
    private float _bucketExpiresAt;
    private float _nextAllowedStaggerAt;
    private Coroutine _lockRoutine;

    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// Call this whenever the enemy takes damage (pass the same amount you subtract from health).
    /// </summary>
    public void NotifyDamage(int amount)
    {
        if (amount <= 0) return;

        float now = Time.time;

        // If we're currently in hit react, optionally do not accumulate more stagger.
        if (ignoreWhileHitReacting && animator != null &&
            !string.IsNullOrWhiteSpace(isHitReactingBool) &&
            HasParameter(animator, isHitReactingBool, AnimatorControllerParameterType.Bool) &&
            animator.GetBool(isHitReactingBool))
        {
            // Don't let old damage carry over forever.
            _bucket = 0;
            _bucketExpiresAt = now + windowSeconds;
            return;
        }

        // Enforce a cooldown between stagger triggers.
        if (now < _nextAllowedStaggerAt)
        {
            // Prevent "instant retrigger" as soon as cooldown ends.
            _bucket = 0;
            _bucketExpiresAt = now + windowSeconds;
            return;
        }

        // If bucket expired, reset.
        if (now > _bucketExpiresAt)
            _bucket = 0;

        _bucketExpiresAt = now + windowSeconds;

        // Optional immediate trigger on big hits.
        if (allowSingleHitToTrigger && amount >= staggerThreshold)
        {
            TriggerHitReact(now);
            _bucket = 0;
            return;
        }

        _bucket += amount;

        if (_bucket >= staggerThreshold)
        {
            _bucket = 0;
            TriggerHitReact(now);
        }
    }

    private void TriggerHitReact(float now)
    {
        if (animator == null) return;

        _nextAllowedStaggerAt = now + Mathf.Max(0.0f, staggerCooldownSeconds);

        // Preferred: trigger param.
        if (HasParameter(animator, takeDamageTrigger, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(takeDamageTrigger);
        }
        else if (HasParameter(animator, hitReactBool, AnimatorControllerParameterType.Bool))
        {
            // Fallback: bool param.
            animator.SetBool(hitReactBool, true);
        }

        // Optional lock bool to gate shooting transitions.
        if (!string.IsNullOrWhiteSpace(isHitReactingBool) &&
            HasParameter(animator, isHitReactingBool, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(isHitReactingBool, true);
            if (_lockRoutine != null) StopCoroutine(_lockRoutine);
            float t = hitReactLockSeconds > 0 ? hitReactLockSeconds : windowSeconds;
            _lockRoutine = StartCoroutine(ReleaseHitReactLock(t));
        }
        else
        {
            // If we're using HitReact bool, auto reset it after a short time so it doesn't get stuck.
            if (HasParameter(animator, hitReactBool, AnimatorControllerParameterType.Bool))
            {
                if (_lockRoutine != null) StopCoroutine(_lockRoutine);
                float t = hitReactLockSeconds > 0 ? hitReactLockSeconds : windowSeconds;
                _lockRoutine = StartCoroutine(ReleaseHitReactBool(t));
            }
        }
    }

    private IEnumerator ReleaseHitReactLock(float t)
    {
        yield return new WaitForSeconds(t);
        if (animator != null &&
            HasParameter(animator, isHitReactingBool, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(isHitReactingBool, false);
        }
        _lockRoutine = null;
    }

    private IEnumerator ReleaseHitReactBool(float t)
    {
        yield return new WaitForSeconds(t);
        if (animator != null &&
            HasParameter(animator, hitReactBool, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(hitReactBool, false);
        }
        _lockRoutine = null;
    }

    private static bool HasParameter(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (a == null || string.IsNullOrEmpty(name)) return false;
        foreach (var p in a.parameters)
            if (p.name == name && p.type == type)
                return true;
        return false;
    }
}
