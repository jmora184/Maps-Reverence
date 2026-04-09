using UnityEngine;

/// <summary>
/// Simple persistent background music manager for MNR.
/// Attach this to a GameObject named MusicManager and assign a gameplay clip.
/// Keeps music alive across scene loads and prevents duplicates.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music")]
    [SerializeField] private AudioClip gameplayMusic;

    [Header("Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.2f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private bool destroyDuplicates = true;

    private AudioSource audioSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (destroyDuplicates)
            {
                Destroy(gameObject);
            }
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
            DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        ConfigureAudioSource();
    }

    private void Start()
    {
        if (playOnStart)
            PlayGameplayMusic();
    }

    private void OnValidate()
    {
        musicVolume = Mathf.Clamp01(musicVolume);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
            audioSource.volume = musicVolume;
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // 2D audio
        audioSource.volume = musicVolume;
    }

    public void PlayGameplayMusic()
    {
        if (audioSource == null || gameplayMusic == null)
            return;

        if (audioSource.clip == gameplayMusic && audioSource.isPlaying)
            return;

        audioSource.clip = gameplayMusic;
        audioSource.volume = musicVolume;
        audioSource.Play();
    }

    public void PlayTrack(AudioClip newClip, bool loop = true)
    {
        if (audioSource == null || newClip == null)
            return;

        if (audioSource.clip == newClip && audioSource.isPlaying)
            return;

        audioSource.clip = newClip;
        audioSource.loop = loop;
        audioSource.volume = musicVolume;
        audioSource.Play();
    }

    public void StopMusic()
    {
        if (audioSource != null)
            audioSource.Stop();
    }

    public void PauseMusic()
    {
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Pause();
    }

    public void ResumeMusic()
    {
        if (audioSource != null)
            audioSource.UnPause();
    }

    public void SetVolume(float newVolume)
    {
        musicVolume = Mathf.Clamp01(newVolume);

        if (audioSource != null)
            audioSource.volume = musicVolume;
    }

    public float GetVolume()
    {
        return musicVolume;
    }

    public bool IsPlaying()
    {
        return audioSource != null && audioSource.isPlaying;
    }
}
