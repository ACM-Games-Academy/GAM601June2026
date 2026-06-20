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

        yield return StartCoroutine(CrossFade(nightSprite));
    }

    [YarnCommand("fadetoday")]
    public IEnumerator FadeToDay()
    {
        if (isFading) yield break;
        if (isDay) yield break;   // already day, nothing to do

        isDay = true;
        yield return StartCoroutine(CrossFade(daySprite));

        // Reveal the wordsearch now that daytime has fully arrived
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
