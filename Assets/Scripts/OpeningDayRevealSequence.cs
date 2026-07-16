using System.Collections;
using UnityEngine;
using Yarn.Unity;

// OpeningDayRevealSequence
//
// One-off cinematic for the very first night-to-day transition. Instead
// of the normal straight crossfade (<<fadetoday>>), the screen fades to
// black BEHIND Meritamun's portrait — she stays visible throughout,
// since BackgroundManager's fade layer already renders behind the
// portraits canvas — her portrait then crossfades to Cat_Meritamun in
// place, and finally the daytime background fades in around her.
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
//      - Background Manager → the scene's BackgroundManager component
//      - Portrait Manager   → the scene's PortraitManager component
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

    [Header("Reveal Settings")]
    public string portraitSlotName = "Right";
    public string revealCharacterName = "Cat_Meritamun";

    public float fadeToBlackDuration = 1f;
    public float portraitCrossfadeDuration = 1.2f;
    public float revealDayDuration = 1.5f;

    [YarnCommand("openingdayreveal")]
    public IEnumerator PlayOpeningDayReveal()
    {
        if (backgroundManager == null || portraitManager == null)
        {
            Debug.LogWarning("OpeningDayRevealSequence: Background Manager or Portrait Manager not assigned.");
            yield break;
        }

        yield return backgroundManager.FadeToBlackOverlay(fadeToBlackDuration);

        yield return portraitManager.CrossfadeCharacterInSlot(
            portraitSlotName, revealCharacterName, portraitCrossfadeDuration);

        yield return backgroundManager.RevealDayFromBlackOverlay(revealDayDuration);
    }
}
