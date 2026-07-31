using System.Collections;
using TMPro;
using UnityEngine;
using Yarn.Unity;

// OpeningDayRevealSequence
//
// One-off cinematic for the very first night-to-day transition. Instead
// of the normal straight crossfade (<<fadetoday>>), the screen fades to
// black BEHIND Meritamun's portrait — she stays visible throughout,
// since BackgroundManager's fade layer already renders behind the
// portraits canvas — a burst of magical sparkle particles plays in
// front of her, her portrait crossfades to Cat_Meritamun in place, and
// finally the daytime background fades in around her.
//
// Registered as a single Yarn command:
//
//   <<openingdayreveal OpeningDayReveal>>
//
// The second word must match the exact name of the GameObject this
// script is attached to in the Hierarchy, same convention as
// BackgroundManager's <<fadetoday BackgroundManager>>.
//
// Because this is a coroutine, Yarn Spinner automatically pauses the
// dialogue until the whole sequence finishes.
//
// SETUP IN UNITY:
// 1. Attach this script to an empty GameObject named "OpeningDayReveal".
// 2. In the Inspector assign:
//      - Background Manager  → the scene's BackgroundManager component
//      - Portrait Manager    → the scene's PortraitManager component
//      - Sound Effect Manager → the scene's SoundEffectManager component
//        (optional — the crossfade chime is skipped if left unassigned)
// 3. In your .yarn script, in place of <<hideallportraits>> +
//    <<fadetoday BackgroundManager>>, keep Meritamun's portrait showing
//    and call:
//
//        <<hideportrait Left>>
//        <<hideportrait CenterLeft>>
//        <<openingdayreveal OpeningDayReveal>>

public class OpeningDayRevealSequence : MonoBehaviour
{
    [Header("References")]
    public BackgroundManager backgroundManager;
    public PortraitManager portraitManager;
    public SoundEffectManager soundEffectManager;

    [Header("Reveal Settings")]
    public string portraitSlotName = "Right";
    public string revealCharacterName = "Cat_Meritamun";
    public string crossfadeSoundEffectName = "MagicalTinkle";

    public float fadeToBlackDuration = 1f;
    public float portraitCrossfadeDuration = 1.2f;
    public float revealDayDuration = 1.5f;

    [Header("Transformation Particles")]
    // How far sparkles scatter from center, in UI units — deliberately
    // larger than the portrait itself so the burst reads as bigger than
    // the character.
    public float particleScatterRadius = 400f;
    public float particleSparkleSize = 45f;

    [Header("Location Title Card")]
    // Shown once the daytime background has fully revealed around
    // Cat_Meritamun's portrait — styled to match SplashScreenController's
    // own title text (same font/size/position), just in black rather
    // than the splash screen's gold, and with no bold weight since a
    // location card reads more like a caption than a game logo.
    // Everything else (wordsearch grid, day ambience, critters) stays
    // hidden until this has fully faded back out — see
    // BackgroundManager.ShowDayGameplayElements.
    public string locationTitleText = "Thebes";
    public TMP_FontAsset titleFont;
    public float titleFontSize = 180f;
    public Vector2 titleSizeDelta = new Vector2(1400f, 300f);
    public Vector2 titleAnchoredPosition = new Vector2(0f, -120f);
    public Color titleColor = Color.black;
    public float titleFadeInDuration = 1f;
    public float titleHoldDuration = 2f;
    public float titleFadeOutDuration = 1.5f;

    [YarnCommand("openingdayreveal")]
    public IEnumerator PlayOpeningDayReveal()
    {
        if (backgroundManager == null || portraitManager == null)
        {
            Debug.LogWarning("OpeningDayRevealSequence: Background Manager or Portrait Manager not assigned.");
            yield break;
        }

        yield return backgroundManager.FadeToBlackOverlay(fadeToBlackDuration);

        // A little magic sparkle right as Meritamun transforms
        if (soundEffectManager != null)
        {
            soundEffectManager.PlaySoundEffect(crossfadeSoundEffectName);
        }

        // Sparkle particles in front of the portrait, running alongside
        // (not blocking) the crossfade itself
        portraitManager.PlayEffectInSlot<MagicalTransformationParticles>(
            portraitSlotName, particles =>
            {
                particles.scatterRadius = particleScatterRadius;
                particles.sparkleSize = particleSparkleSize;
            }, inFront: true);

        yield return portraitManager.CrossfadeCharacterInSlot(
            portraitSlotName, revealCharacterName, portraitCrossfadeDuration);

        // Cat_Meritamun's portrait stays put throughout all of this —
        // RevealDayFromBlackOverlay only ever touches the background
        // layers, never the portraits canvas sitting in front of them.
        yield return backgroundManager.RevealDayFromBlackOverlay(revealDayDuration);

        yield return StartCoroutine(PlayLocationTitleCard());

        backgroundManager.ShowDayGameplayElements();
    }

    // Builds (fresh each time — this cutscene only ever plays once per
    // playthrough, so there's no need for the find-or-create/caching
    // convention used by repeatable UI elsewhere) a centered title card
    // on top of the now-fully-revealed daytime background, fades it in,
    // holds, fades it back out, then destroys itself.
    private IEnumerator PlayLocationTitleCard()
    {
        if (string.IsNullOrEmpty(locationTitleText)) yield break;
        if (backgroundManager == null || backgroundManager.topLayer == null) yield break;

        Transform backgroundParent = backgroundManager.topLayer.transform.parent;

        GameObject textObject = new GameObject("LocationTitle", typeof(RectTransform));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(backgroundParent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = titleSizeDelta;
        rect.anchoredPosition = titleAnchoredPosition;

        // Last sibling so nothing already in the background layer stack
        // (torches, god rays, critters) can render on top of it.
        rect.SetAsLastSibling();

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = locationTitleText;
        text.fontSize = titleFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        if (titleFont != null) text.font = titleFont;

        Color color = titleColor;
        float targetAlpha = titleColor.a;
        color.a = 0f;
        text.color = color;

        float elapsed = 0f;
        while (elapsed < titleFadeInDuration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Lerp(0f, targetAlpha, Mathf.Clamp01(elapsed / titleFadeInDuration));
            text.color = color;
            yield return null;
        }
        color.a = targetAlpha;
        text.color = color;

        yield return new WaitForSeconds(titleHoldDuration);

        elapsed = 0f;
        while (elapsed < titleFadeOutDuration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Lerp(targetAlpha, 0f, Mathf.Clamp01(elapsed / titleFadeOutDuration));
            text.color = color;
            yield return null;
        }

        Destroy(textObject);
    }
}
