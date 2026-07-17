using System.Collections;
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

        yield return backgroundManager.RevealDayFromBlackOverlay(revealDayDuration);
    }
}
