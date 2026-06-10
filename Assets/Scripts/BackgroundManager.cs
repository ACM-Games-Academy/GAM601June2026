using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// BackgroundManager
//
// Crossfades between a daytime and nighttime background sprite.
//
// SETUP IN UNITY:
// 1. Create two UI Image objects on your Canvas, stacked on top of each other,
//    both stretching to fill the full canvas. Name them:
//      - BackgroundBottom  (sits behind)
//      - BackgroundTop     (sits in front, this is the one that fades)
//    Make sure BackgroundBottom is above BackgroundTop in the Hierarchy
//    so that BackgroundTop renders on top.
//
// 2. Attach this script to an empty GameObject called BackgroundManager.
//
// 3. In the Inspector assign:
//      - Bottom Layer  → BackgroundBottom Image
//      - Top Layer     → BackgroundTop Image
//      - Day Sprite    → your daytime background sprite
//      - Night Sprite  → your nighttime background sprite
//      - Fade Duration → how long the crossfade takes in seconds (default 1.5)
//
// 4. To trigger a fade, call from a Dialogue System Sequencer node:
//      SendMessage(FadeToNight, BackgroundManager, WorldSpace)
//      SendMessage(FadeToDay,   BackgroundManager, WorldSpace)
// ─────────────────────────────────────────────────────────────────────────────

public class BackgroundManager : MonoBehaviour
{
    [Header("Background Image Layers")]
    // The bottom image layer — always shows the current background
    public Image bottomLayer;

    // The top image layer — fades in over the bottom to reveal the new background
    public Image topLayer;

    [Header("Sprites")]
    public Sprite daySprite;
    public Sprite nightSprite;

    [Header("Settings")]
    // How long the crossfade takes in seconds
    public float fadeDuration = 1.5f;

    // Tracks whether a fade is currently in progress
    // Prevents overlapping fades if triggered rapidly
    private bool isFading = false;

    // Tracks which background is currently showing
    private bool isDay = true;

    void Start()
    {
        // Initialise both layers to the daytime background
        // Bottom layer fully visible, top layer fully transparent
        bottomLayer.sprite = daySprite;
        topLayer.sprite = daySprite;

        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

        isDay = true;
    }

    // ── Public methods called by Dialogue System Sequencer ────────────────────

    // Call via: SendMessage(FadeToNight, BackgroundManager, WorldSpace)
    public void FadeToNight()
    {
        if (isFading) return;
        if (!isDay) return;   // already night, do nothing

        StartCoroutine(CrossFade(nightSprite));
        isDay = false;
    }

    // Call via: SendMessage(FadeToDay, BackgroundManager, WorldSpace)
    public void FadeToDay()
    {
        if (isFading) return;
        if (isDay) return;   // already day, do nothing

        StartCoroutine(CrossFade(daySprite));
        isDay = true;
    }

    // ── Crossfade coroutine ───────────────────────────────────────────────────

    IEnumerator CrossFade(Sprite targetSprite)
    {
        isFading = true;

        // Place the new sprite on the top layer, fully transparent
        topLayer.sprite = targetSprite;
        SetAlpha(topLayer, 0f);

        // Fade the top layer in over the bottom layer
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeDuration);
            SetAlpha(topLayer, alpha);
            yield return null;
        }

        // Crossfade complete:
        // Snap the bottom layer to the new sprite and hide the top layer again.
        // This keeps the bottom layer always showing the current background,
        // ready for the next crossfade.
        bottomLayer.sprite = targetSprite;
        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

        isFading = false;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    void SetAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}
