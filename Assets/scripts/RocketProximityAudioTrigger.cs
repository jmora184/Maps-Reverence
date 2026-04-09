using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RocketProximityAudioTrigger : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource rocketAudioSource;
    [SerializeField] private AudioClip rocketLoopClip;

    [Header("Target")]
    [SerializeField] private string playerTag = "Player";

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        if (rocketAudioSource == null)
            rocketAudioSource = GetComponent<AudioSource>();

        if (rocketAudioSource == null)
            rocketAudioSource = gameObject.AddComponent<AudioSource>();

        if (rocketLoopClip != null)
            rocketAudioSource.clip = rocketLoopClip;

        rocketAudioSource.loop = true;
        rocketAudioSource.playOnAwake = false;
        rocketAudioSource.spatialBlend = 1f;

        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag))
            return;

        if (rocketAudioSource == null || rocketAudioSource.clip == null)
            return;

        if (!rocketAudioSource.isPlaying)
            rocketAudioSource.Play();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag))
            return;

        if (rocketAudioSource != null && rocketAudioSource.isPlaying)
            rocketAudioSource.Stop();
    }
}
