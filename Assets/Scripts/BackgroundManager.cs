using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// ─────────────────────────────────────────────────────────────────────────────
// BackgroundManager — Yarn Spinner version
//
// Crossfades between a nighttime and daytime background sprite.
// The scene STARTS AT NIGHT — this matters for the guard clauses below,
// which prevent redundant fades (e.g. calling <<fadetonight>> when it's
// already night does nothing).
//
// Both fade methods are registered directly as Yarn commands via the
// [YarnCommand] attribute:
//
//     <<fadetonight BackgroundManager>>
//     <<fadetoday BackgroundManager>>
//
// The second word must match the exact name of the GameObject this
// script is attached to in the Hierarchy.
//
// Because these are coroutines, Yarn Spinner automatically PAUSES the
// dialogue until the fade finishes.
//
// SETUP IN UNITY:
// 1. Two UI Image objects on your Canvas, stacked on top of each other,
//    both stretching to fill the full canvas:
//      - BackgroundBottom  (sits behind)
//      - BackgroundTop     (sits in front, this is the one that fades)
//    BackgroundBottom must sit ABOVE BackgroundTop in the Hierarchy
//    list so BackgroundTop renders on top. Both must sit above your
//    dialogue UI in render order too (i.e. higher in the Hierarchy list).
//
// 2. Attach this script to a GameObject named "BackgroundManager".
//
// 3. In the Inspector assign:
//      - Bottom Layer  → BackgroundBottom Image
//      - Top Layer     → BackgroundTop Image
//      - Day Sprite    → your daytime background sprite
//      - Night Sprite  → your nighttime background sprite
//      - Fade Duration → how long the crossfade takes in seconds
//
// 4. In your .yarn script:
//
//        Narrator: Your journey continues...
//        <<fadetoday BackgroundManager>>
//        ===
// ─────────────────────────────────────────────────────────────────────────────

public class BackgroundManager : MonoBehaviour
{
    [Header("Background Image Layers")]
    public Image bottomLayer;
    public Image topLayer;

    [Header("Sprites")]
    public Sprite daySprite;
    public Sprite nightSprite;

    [Header("Settings")]
    public float fadeDuration = 1.5f;

    [Header("Wordsearch Visibility")]
    // The top-level wordsearch UI object (your GridPanel, or a parent
    // panel wrapping it). Shown when day arrives, hidden at night.
    public GameObject wordsearchPanel;

    // Optional but recommended: also reference GridManager so we can
    // force input off the moment night begins, as a safety net in
    // case a puzzle was left mid-solve.
    public GridManager gridManager;

    private bool isFading = false;

    // The scene STARTS AT NIGHT — see Start() below.
    private bool isDay = false;

    void Start()
    {
        // Initialise both layers to the NIGHTTIME background,
        // matching how the game actually begins
        bottomLayer.sprite = nightSprite;
        topLayer.sprite = nightSprite;

        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

        isDay = false;

        // The game starts at night — the wordsearch shouldn't be
        // visible or interactive until the first daytime sequence
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(false);
        }

        if (gridManager != null)
        {
            gridManager.inputEnabled = false;
        }

        // Music may still be playing (faded in) from the splash screen —
        // the scene starts at night, so fade it back out rather than
        // leaving it running.
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.StopMusic();
        }
    }

    // ── Yarn-callable commands ─────────────────────────────────────────────

    [YarnCommand("fadetonight")]
    public IEnumerator FadeToNight()
    {
        if (isFading) yield break;
        if (!isDay) yield break;   // already night, nothing to do

        isDay = false;

        // Hide the wordsearch immediately — night gameplay shouldn't
        // show it at all, even while the background is mid-fade
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(false);
        }

        // Safety net: force input off in case a puzzle was left
        // unsolved when night arrived
        if (gridManager != null)
        {
            gridManager.inputEnabled = false;
        }

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.StopMusic();
        }

        yield return StartCoroutine(CrossFade(nightSprite));
    }

    [YarnCommand("fadetoday")]
    public IEnumerator FadeToDay()
    {
        if (isFading) yield break;
        if (isDay) yield break;   // already day, nothing to do

        isDay = true;

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        yield return StartCoroutine(CrossFade(daySprite));

        // Reveal the wordsearch now that daytime has fully arrived
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(true);
        }
    }

    // ── Opening day reveal (cutscene helpers) ───────────────────────────────
    //
    // Used by OpeningDayRevealSequence for the one-off opening transition,
    // where the screen fades to black BEHIND a portrait (which stays
    // visible, since topLayer already renders behind the portraits
    // canvas), rather than straight-crossfading to the day sprite.

    // Fades topLayer to solid opaque black, covering bottomLayer (and
    // anything else drawn behind the portraits) without touching
    // whichever portrait is currently on top of it.
    public IEnumerator FadeToBlackOverlay(float duration)
    {
        isFading = true;

        topLayer.color = new Color(0f, 0f, 0f, 0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / duration);
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        topLayer.color = new Color(0f, 0f, 0f, 1f);
    }

    // While fully hidden behind the black overlay, swaps bottomLayer
    // straight to daySprite (no visible pop, since topLayer is fully
    // opaque at this point), then fades the black overlay back out to
    // reveal it. Mirrors FadeToDay's bookkeeping (isDay, wordsearchPanel).
    public IEnumerator RevealDayFromBlackOverlay(float duration)
    {
        bottomLayer.sprite = daySprite;
        SetAlpha(bottomLayer, 1f);

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMusic();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration));
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        // Reset topLayer to its normal resting state (transparent white)
        // now that it's invisible, so later ordinary day/night
        // crossfades — which only ever touch alpha, not RGB — behave
        // exactly as they did before this cutscene ran.
        topLayer.color = new Color(1f, 1f, 1f, 0f);

        isDay = true;
        isFading = false;

        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(true);
        }
    }

    // ── Crossfade coroutine ───────────────────────────────────────────────

    private IEnumerator CrossFade(Sprite targetSprite)
    {
        isFading = true;

        topLayer.sprite = targetSprite;
        SetAlpha(topLayer, 0f);

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeDuration);
            SetAlpha(topLayer, alpha);
            yield return null;
        }

        bottomLayer.sprite = targetSprite;
        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

        isFading = false;
    }

    private void SetAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}
