using System.Collections;
using UnityEngine;

// MusicManager
//
// Plays looping background music, crossfading between a day track and
// a night track as BackgroundManager transitions the scene, and
// surviving scene loads (via DontDestroyOnLoad) so music can keep
// playing uninterrupted from the splash screen into gameplay.
//
// Uses two AudioSources so switching tracks is a real crossfade — one
// fades out while the other fades in — the same two-layer technique
// BackgroundManager uses for its own day/night sprite crossfade,
// rather than a hard cut from one clip to the next.
//
// Unlike this project's other managers (which are wired via explicit
// per-scene Inspector references), this one has to be reachable from
// scripts living in a DIFFERENT scene than it was created in — that's
// what DontDestroyOnLoad is for, but it also means a normal Inspector
// drag-and-drop reference can't work across the scene boundary. A
// static Instance reference is the standard way to solve that in Unity.
//
// Called by:
//   - SplashScreenController.Start()   → PlayDayMusic()
//   - BackgroundManager.Start()        → PlayNightMusic()  (scene starts at night)
//   - BackgroundManager.FadeToDay()    → PlayDayMusic()
//   - BackgroundManager.FadeToNight()  → PlayNightMusic()
//   - BackgroundManager.RevealDayFromBlackOverlay() → PlayDayMusic()
//
// SETUP:
// 1. Attach to an empty GameObject named "MusicManager", placed in
//    whichever scene loads FIRST (the splash screen).
// 2. Assign Day Music Clip and Night Music Clip in the Inspector.
// 3. Don't place a second MusicManager in the gameplay scene — this
//    one already survives into it automatically. If a duplicate does
//    turn up, this script destroys the newcomer and keeps the
//    original (which is already mid-track) rather than restarting.

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music Tracks")]
    public AudioClip dayMusicClip;
    public AudioClip nightMusicClip;
    [Range(0f, 1f)] public float musicVolume = 0.5f;
    public float fadeDuration = 1.5f;

    private AudioSource sourceA;
    private AudioSource sourceB;
    private AudioSource activeSource;
    private AudioClip activeClip;
    private Coroutine fadeCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Reuse any AudioSource(s) already on this GameObject (e.g. one
        // left over from before this script needed two) before adding
        // new ones, so upgrading from the old single-track version
        // doesn't leave a stray unused component behind.
        AudioSource[] existingSources = GetComponents<AudioSource>();
        sourceA = existingSources.Length > 0 ? existingSources[0] : gameObject.AddComponent<AudioSource>();
        sourceB = existingSources.Length > 1 ? existingSources[1] : gameObject.AddComponent<AudioSource>();

        foreach (AudioSource source in new AudioSource[] { sourceA, sourceB })
        {
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // background music should never fall off with listener position
            source.volume = 0f;
        }

        activeSource = sourceA;
    }

    public void PlayDayMusic()
    {
        PlayTrack(dayMusicClip);
    }

    public void PlayNightMusic()
    {
        PlayTrack(nightMusicClip);
    }

    // Fades the currently playing track (if any) down to silence and
    // stops it, without starting a replacement.
    public void StopMusic()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        fadeCoroutine = StartCoroutine(FadeOutAndStop(activeSource));
        activeClip = null;
    }

    // Crossfades to 'clip': it fades in on the currently inactive
    // AudioSource while whatever's already playing fades out on the
    // other one, so the two tracks overlap briefly instead of cutting.
    // Does nothing if 'clip' is unassigned, or already the active track.
    private void PlayTrack(AudioClip clip)
    {
        if (clip == null) return;
        if (clip == activeClip) return;

        AudioSource newSource = (activeSource == sourceA) ? sourceB : sourceA;
        AudioSource oldSource = activeSource;

        newSource.clip = clip;
        newSource.volume = 0f;
        newSource.Play();

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        fadeCoroutine = StartCoroutine(CrossfadeCoroutine(oldSource, newSource));

        activeSource = newSource;
        activeClip = clip;
    }

    private IEnumerator CrossfadeCoroutine(AudioSource fadeOutSource, AudioSource fadeInSource)
    {
        float startOutVolume = fadeOutSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            fadeOutSource.volume = Mathf.Lerp(startOutVolume, 0f, t);
            fadeInSource.volume = Mathf.Lerp(0f, musicVolume, t);
            yield return null;
        }

        fadeOutSource.volume = 0f;
        fadeOutSource.Stop();
        fadeInSource.volume = musicVolume;

        fadeCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(AudioSource source)
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            source.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        source.volume = 0f;
        source.Stop();
        fadeCoroutine = null;
    }
}
