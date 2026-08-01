using System.Collections;
using UnityEngine;
using Yarn.Unity;

// ClosingNightRevealSequence
//
// One-off cinematic mirroring OpeningDayRevealSequence, but in reverse:
// instead of the normal straight crossfade (<<fadetonight>>), the screen
// fades to black BEHIND Cat_Meritamun's portrait — she stays visible
// throughout, since BackgroundManager's fade layer already renders
// behind the portraits canvas — a burst of magical sparkle particles
// plays in front of her, her portrait crossfades to Meritamun (a
// specific expression, e.g. "worried") in place, and finally the
// nighttime background fades in around her.
//
// Registered as a single Yarn command:
//
//   <<closingnightreveal ClosingNightReveal>>
//
// The second word must match the exact name of the GameObject this
// script is attached to in the Hierarchy, same convention as
// BackgroundManager's <<fadetonight BackgroundManager>>.
//
// Because this is a coroutine, Yarn Spinner automatically pauses the
// dialogue until the whole sequence finishes.
//
// SETUP IN UNITY:
// 1. Attach this script to an empty GameObject named "ClosingNightReveal".
// 2. In the Inspector assign:
//      - Background Manager  → the scene's BackgroundManager component
//      - Portrait Manager    → the scene's PortraitManager component
//      - Sound Effect Manager → the scene's SoundEffectManager component
//        (optional — the crossfade chime is skipped if left unassigned)
// 3. In your .yarn script, in place of <<hideallportraits>> +
//    <<fadetonight BackgroundManager>>, keep Cat_Meritamun's portrait
//    showing and call:
//
//        <<hideportrait Left>>
//        <<closingnightreveal ClosingNightReveal>>

public class ClosingNightRevealSequence : MonoBehaviour
{
    [Header("References")]
    public BackgroundManager backgroundManager;
    public PortraitManager portraitManager;
    public SoundEffectManager soundEffectManager;
    // Cat_Meritamun's last line before this cutscene is often a
    // bracketed thought, which pops the speech bubble — but
    // <<closingnightreveal>> is a COMMAND, not a new dialogue line, so
    // ThoughtBubblePresenter never gets its usual "a new line started"
    // signal to dismiss it. Without this reference the bubble would just
    // sit there frozen through the whole fade/crossfade/reveal below.
    public ThoughtBubblePresenter thoughtBubblePresenter;

    [Header("Reveal Settings")]
    public string portraitSlotName = "Right";
    public string revealCharacterName = "Meritamun";
    public string revealExpressionName = "worried";
    public string crossfadeSoundEffectName = "MagicalTinkle";

    public float fadeToBlackDuration = 1f;
    public float portraitCrossfadeDuration = 1.2f;
    public float revealNightDuration = 1.5f;

    [Header("Transformation Particles")]
    // How far sparkles scatter from center, in UI units — deliberately
    // larger than the portrait itself so the burst reads as bigger than
    // the character.
    public float particleScatterRadius = 400f;
    public float particleSparkleSize = 45f;

    [YarnCommand("closingnightreveal")]
    public IEnumerator PlayClosingNightReveal()
    {
        if (backgroundManager == null || portraitManager == null)
        {
            Debug.LogWarning("ClosingNightRevealSequence: Background Manager or Portrait Manager not assigned.");
            yield break;
        }

        // Fades out in step with the background below (same duration),
        // rather than being left to sit frozen at full opacity — see the
        // field comment above. Does nothing if no bubble is currently up.
        if (thoughtBubblePresenter != null)
        {
            thoughtBubblePresenter.FadeOutOverTime(fadeToBlackDuration);
        }

        // Same reasoning — a <<sadreaction>> triggered earlier in this
        // beat has no "new line" left to stop it on once this cutscene
        // (a COMMAND, not a line) takes over.
        portraitManager.StopAllSadReactions();

        yield return backgroundManager.FadeToBlackOverlay(fadeToBlackDuration);

        // A little magic sparkle right as Cat_Meritamun transforms back
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
            portraitSlotName, revealCharacterName, portraitCrossfadeDuration, revealExpressionName);

        // Meritamun's portrait stays put throughout all of this —
        // RevealNightFromBlackOverlay only ever touches the background
        // layers, never the portraits canvas sitting in front of them.
        yield return backgroundManager.RevealNightFromBlackOverlay(revealNightDuration);
    }
}
