using System.Collections;
using UnityEngine;

// MusicManager
//
// Plays looping background music that survives scene loads (via
// DontDestroyOnLoad), so the same track can keep playing uninterrupted
// from the splash screen into the daytime portions of the gameplay
// scene, rather than restarting from silence each time a scene loads.
//
// Unlike this project's other managers (which are wired via explicit
// per-scene Inspector references), this one has to be reachable from
// scripts living in a DIFFERENT scene than it was created in — that's
// what DontDestroyOnLoad is for, but it also means a normal Inspector
// drag-and-drop reference can't work across the scene boundary. A
// static Instance reference is the standard way to solve that in Unity.
//
// Called by:
//   - SplashScreenController.Start()   → PlayMusic()
//   - BackgroundManager.Start()        → StopMusic()  (scene starts at night)
//   - BackgroundManager.FadeToDay()    → PlayMusic()
//   - BackgroundManager.FadeToNight()  → StopMusic()
//   - BackgroundManager.RevealDayFromBlackOverlay() → PlayMusic()
//
// SETUP:
// 1. Attach to an empty GameObject named "MusicManager", placed in
//    whichever scene loads FIRST (the splash screen).
// 2. Assign Music Clip in the Inspector.
// 3. Don't place a second MusicManager in the gameplay scene — this
//    one already survives into it automatically. If a duplicate does
//    turn up, this script destroys the newcomer and keeps the
//    original (which is already mid-track) rather than restarting.

[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music")]
    public AudioClip musicClip;
    [Range(0f, 1f)] public float musicVolume = 0.5f;
    public float fadeDuration = 1.5f;

    private AudioSource audioSource;
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

        audioSource = GetComponent<AudioSource>();
        audioSource.clip = musicClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // background music should never fall off with listener position
        audioSource.volume = 0f;
    }

    // Starts the music if it isn't already playing, and fades it up to
    // musicVolume. Safe to call repeatedly (e.g. redundant <<fadetoday>>
    // calls) — does nothing extra if already at full volume.
    public void PlayMusic()
    {
        if (musicClip == null) return;

        if (!audioSource.isPlaying)
        {
            audioSource.volume = 0f;
            audioSource.Play();
        }

        FadeTo(musicVolume, stopWhenDone: false);
    }

    // Fades the music down to silence and stops it.
    public void StopMusic()
    {
        if (!audioSource.isPlaying) return;
        FadeTo(0f, stopWhenDone: true);
    }

    private void FadeTo(float targetVolume, bool stopWhenDone)
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        fadeCoroutine = StartCoroutine(FadeCoroutine(targetVolume, stopWhenDone));
    }

    private IEnumerator FadeCoroutine(float targetVolume, bool stopWhenDone)
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            audioSource.volume = Mathf.Lerp(startVolume, targetVolume, t);
            yield return null;
        }

        audioSource.volume = targetVolume;

        if (stopWhenDone)
        {
            audioSource.Stop();
        }

        fadeCoroutine = null;
    }
}
