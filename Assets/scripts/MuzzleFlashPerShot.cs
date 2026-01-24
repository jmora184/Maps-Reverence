using System.Collections;
using UnityEngine;

/// <summary>
/// Ensures a muzzle-flash ParticleSystem visibly triggers on EVERY shot,
/// even at higher fire rates where ParticleSystem.Play() alone may not retrigger.
/// 
/// Usage:
/// 1) Add this component to the muzzle flash ParticleSystem GameObject (pistol or rifle).
/// 2) Drag a reference to this component in your gun/fire script.
/// 3) Call Trigger() immediately after each successful shot.
/// </summary>
public class MuzzleFlashPerShot : MonoBehaviour
{
    [Header("Particle System")]
    public ParticleSystem ps;

    [Header("Trigger Mode")]
    [Tooltip("Preferred: Emit() guarantees a burst each call even if the system is already playing.")]
    public bool useEmit = true;

    [Tooltip("How many particles to emit per shot when using Emit(). Usually 1 is enough.")]
    public int emitCount = 1;

    [Tooltip("If true, forces Stop/Clear/Play each shot (more aggressive; sometimes needed for certain setups).")]
    public bool forceRestartEachShot = false;

    [Header("Renderer Safety (optional)")]
    [Tooltip("Some muzzle flashes 'stick' invisible if the renderer is disabled/enabled during weapon switching. This toggles it briefly each shot.")]
    public bool toggleRendererEachShot = true;

    [Tooltip("How long to keep the renderer on after a shot (seconds).")]
    public float rendererOnTime = 0.05f;

    private ParticleSystemRenderer psRenderer;
    private Coroutine rendererRoutine;

    private void Awake()
    {
        if (ps == null) ps = GetComponent<ParticleSystem>();
        if (ps != null) psRenderer = ps.GetComponent<ParticleSystemRenderer>();
    }

    private void OnEnable()
    {
        // Reset so the very first shot after switching weapons always shows.
        if (ps != null)
        {
            ps.Clear(true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (psRenderer != null && toggleRendererEachShot)
            psRenderer.enabled = false;
    }

    public void Trigger()
    {
        if (ps == null) return;

        // Ensure renderer is on for the flash frame(s)
        if (psRenderer != null && toggleRendererEachShot)
        {
            psRenderer.enabled = true;
            if (rendererRoutine != null) StopCoroutine(rendererRoutine);
            rendererRoutine = StartCoroutine(DisableRendererSoon());
        }

        if (forceRestartEachShot)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
            return;
        }

        if (useEmit)
        {
            // Emit is the most reliable per-shot trigger.
            ps.Emit(Mathf.Max(1, emitCount));
        }
        else
        {
            // Fallback: Play. (May not retrigger if already playing, depending on setup.)
            if (!ps.isPlaying) ps.Play(true);
            else
            {
                // Gentle nudge: clear+play if already playing.
                ps.Clear(true);
                ps.Play(true);
            }
        }
    }

    private IEnumerator DisableRendererSoon()
    {
        yield return new WaitForSeconds(rendererOnTime);
        if (psRenderer != null) psRenderer.enabled = false;
        rendererRoutine = null;
    }
}
