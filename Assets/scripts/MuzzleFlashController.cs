using System.Collections;
using UnityEngine;

namespace MNR.VFX
{
    /// <summary>
    /// Drop this on the gun (or a child GameObject at the barrel) and assign:
    /// - One or more ParticleSystems (your muzzle flash, smoke puff, sparks, etc.)
    /// - Optional Light for a brief flash
    /// Then call Play() whenever a shot is fired.
    /// </summary>
    public class MuzzleFlashController : MonoBehaviour
    {
        [Header("Particle Systems")]
        [Tooltip("Particle systems to play on each shot (muzzle flash, smoke, sparks, etc.).")]
        public ParticleSystem[] particleSystems;

        [Tooltip("If true, randomizes this transform's local Z rotation each shot for variety.")]
        public bool randomizeRotation = true;

        [Tooltip("Rotation range in degrees if randomizeRotation is enabled.")]
        public Vector2 rotationRange = new Vector2(0f, 360f);

        [Header("Optional Light Flash")]
        public Light flashLight;
        [Tooltip("Enables the flashLight briefly on each shot.")]
        public bool useLightFlash = true;

        [Tooltip("How long the light stays on (seconds). Typical: 0.02 - 0.06")]
        [Range(0.005f, 0.2f)]
        public float lightOnTime = 0.035f;

        [Tooltip("Multiply the original light intensity when flashing. 1 = unchanged.")]
        [Range(0f, 10f)]
        public float lightIntensityMultiplier = 1.0f;

        [Header("Optional Audio")]
        [Tooltip("Optional: play this AudioSource on each shot (e.g., a 'snap' or 'crack'). Leave null to ignore.")]
        public AudioSource shotAudio;

        [Tooltip("Random pitch range for shotAudio.")]
        public Vector2 audioPitchRange = new Vector2(0.97f, 1.03f);

        private float _baseLightIntensity;
        private Coroutine _lightRoutine;

        private void Awake()
        {
            if (flashLight != null)
            {
                _baseLightIntensity = flashLight.intensity;
                flashLight.enabled = false;
            }

            // If none assigned, try to auto-grab ParticleSystems under this object.
            if (particleSystems == null || particleSystems.Length == 0)
            {
                particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            }
        }

        /// <summary>
        /// Call this whenever a shot is fired.
        /// </summary>
        public void Play()
        {
            if (randomizeRotation)
            {
                float z = Random.Range(rotationRange.x, rotationRange.y);
                var e = transform.localEulerAngles;
                e.z = z;
                transform.localEulerAngles = e;
            }

            // Play particle systems
            if (particleSystems != null)
            {
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    var ps = particleSystems[i];
                    if (ps == null) continue;

                    // Restart cleanly in case it is still playing
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
            }

            // Light flash
            if (useLightFlash && flashLight != null)
            {
                if (_lightRoutine != null) StopCoroutine(_lightRoutine);
                _lightRoutine = StartCoroutine(LightFlashRoutine());
            }

            // Audio
            if (shotAudio != null)
            {
                shotAudio.pitch = Random.Range(audioPitchRange.x, audioPitchRange.y);
                shotAudio.Play();
            }
        }

        private IEnumerator LightFlashRoutine()
        {
            flashLight.intensity = _baseLightIntensity * lightIntensityMultiplier;
            flashLight.enabled = true;

            // A tiny wait creates the "pop"
            yield return new WaitForSeconds(lightOnTime);

            if (flashLight != null)
                flashLight.enabled = false;

            _lightRoutine = null;
        }

        /// <summary>
        /// Convenience helper: lets you trigger via Unity Animation Event (e.g., on a fire animation).
        /// </summary>
        public void PlayFromAnimationEvent()
        {
            Play();
        }
    }
}
