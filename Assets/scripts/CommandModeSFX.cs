using UnityEngine;

/// <summary>
/// Small centralized SFX router for command mode UI + order confirmations.
/// Attach this to a persistent active object (HUD / command UI root is fine)
/// and assign the clips in the Inspector.
/// </summary>
public class CommandModeSFX : MonoBehaviour
{
    public static CommandModeSFX Instance { get; private set; }

    [Header("References")]
    public AudioSource audioSource;

    [Header("Clips")]
    public AudioClip clickClip;
    public AudioClip moveOrderClip;
    public AudioClip attackOrderClip;

    [Header("Volume")]
    [Range(0f, 1f)] public float clickVolume = 1f;
    [Range(0f, 1f)] public float moveOrderVolume = 1f;
    [Range(0f, 1f)] public float attackOrderVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void PlayClickGlobal()
    {
        if (TryGetInstance(out var sfx))
            sfx.PlayOneShot(sfx.clickClip, sfx.clickVolume);
    }

    public static void PlayMoveOrderGlobal()
    {
        if (TryGetInstance(out var sfx))
            sfx.PlayOneShot(sfx.moveOrderClip, sfx.moveOrderVolume);
    }

    public static void PlayAttackOrderGlobal()
    {
        if (TryGetInstance(out var sfx))
            sfx.PlayOneShot(sfx.attackOrderClip, sfx.attackOrderVolume);
    }

    private static bool TryGetInstance(out CommandModeSFX sfx)
    {
        sfx = Instance;
        if (sfx == null)
            sfx = FindObjectOfType<CommandModeSFX>();

        return sfx != null;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (audioSource == null) return;

        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
}
