using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using Yarn.Unity;

// DialogueBoxClickToAdvance
//
// Attach to the dialogue panel background (the box the text sits in).
//
// Clicking anywhere on the box calls LineAdvancer's existing public
// RequestLineHurryUp() method, which completes the current line's
// typewriter effect immediately if it's still animating. If the line
// has already finished displaying, calling this again is harmless �
// there's nothing left to hurry, so it does nothing.
//
// This does NOT advance to the next line � that remains the separate
// job of your existing Continue button, untouched.
//
// No Yarn Spinner package files are modified. This only calls a
// method that's already public.
//
// SETUP:
// 1. The dialogue panel's background Image needs "Raycast Target" ticked.
// 2. Attach this script to that panel object.
// 3. Drag your LineAdvancer component into "Line Advancer".

public class DialogueBoxClickToAdvance : MonoBehaviour, IPointerClickHandler
{
    public LineAdvancer lineAdvancer;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (lineAdvancer == null)
        {
            Debug.LogWarning("DialogueBoxClickToAdvance: Line Advancer not assigned.");
            return;
        }

        // If LineAdvancer has been disabled (e.g. by WordsearchDialogueBridge
        // during an active #wordsearch puzzle), respect that lock and do
        // nothing � same as clicking the Continue button would currently do.
        if (!lineAdvancer.enabled) return;

        // A vermin critter is constantly darting around (see
        // CritterCatchEffect), so the click that presses it down and the
        // click that releases over it can land on two different frames �
        // if it darted out from under the cursor by release time, Unity's
        // UI event system can end up delivering this SAME click to
        // whatever's now underneath (the dialogue box), which otherwise
        // reads as clicking a critter also advancing dialogue. Deferring
        // to end of frame lets CritterCatchEffect's own click handler
        // (wherever it lands in this frame's dispatch order) record the
        // catch first, so the check below reliably sees it either way.
        StartCoroutine(HurryUpUnlessCritterCaughtThisFrame());
    }

    private IEnumerator HurryUpUnlessCritterCaughtThisFrame()
    {
        int clickFrame = Time.frameCount;
        yield return new WaitForEndOfFrame();

        if (CritterCatchEffect.LastCatchFrame == clickFrame) yield break;

        lineAdvancer.RequestLineHurryUp();
    }
}
