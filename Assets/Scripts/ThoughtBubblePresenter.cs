using System.Collections;
using TMPro;
using UnityEngine;
using Yarn.Unity;

// ThoughtBubblePresenter
//
// Whenever a line spoken by thoughtCharacterName (Cat_Meritamun by
// default) contains text wrapped in square brackets — this project's
// existing "internal thought" convention, e.g.
//
//     Cat_Meritamun: \[Oh! Someone is coming!\] #expression:neutral
//
// in Prototype.yarn — this shows the Thought Bubble object with just
// that bracketed text, and hides the normal dialogue box background,
// its line text, and the name tag for as long as the bubble is up — so
// a thought reads as replacing the usual dialogue chrome for that beat
// rather than showing the same bracketed text twice at once. The
// Continue button/arrow stays visible and active throughout, since the
// player still needs it to advance past a thought line. The brackets
// are escaped in the .yarn source only so Yarn's compiler doesn't try
// to parse them as its own markup syntax (square brackets are Yarn's
// markup delimiter) — by the time the line reaches here, \[ and \]
// have already become plain literal [ and ] characters.
//
// Hides the bubble and restores the normal dialogue chrome automatically
// as soon as a line without bracketed text for her comes up, or when
// dialogue starts/completes — so neither ever lingers onto a line it
// doesn't belong to.
//
// Unlike this project's other reaction effects (PulseGlowEffect,
// AngerPulseEffect, etc.), the bubble is NOT built procedurally at
// runtime and does NOT follow whichever portrait slot is speaking —
// it's a plain, fixed object already sitting in the scene (see
// "ThoughtBubble" under PortraitsContainer in YarnViabilityTest.unity),
// specifically so it's visible in the Scene view and repositionable
// with the normal Rect Transform tool, the same as any other UI element
// — no need to enter Play Mode just to find and drag it.
//
// SETUP:
// 1. Attach to any GameObject in the dialogue scene (e.g. alongside
//    PortraitManager) and add it to the DialogueRunner's Dialogue
//    Presenters list.
// 2. Assign Bubble Object (the "ThoughtBubble" GameObject), Bubble Text
//    (its child TextMeshProUGUI), and the three dialogue-chrome objects
//    to hide while it's up: Dialogue Box Background, Dialogue Line Text,
//    and Name Tab.
// 3. Drag "ThoughtBubble" in the Scene view to wherever it should sit —
//    its position, size, and the bubble artwork/font/color are all just
//    normal Inspector values on that object now, not fields here.

public class ThoughtBubblePresenter : DialoguePresenterBase
{
    [Header("References")]
    // The bubble's root GameObject (Image + RectTransform) — its
    // position/size/sprite are set directly on it in the Scene/
    // Inspector, this script only ever shows/hides it and sets its text.
    public GameObject bubbleObject;
    public TextMeshProUGUI bubbleText;

    // Hidden for as long as the thought bubble is showing, and restored
    // the moment it hides — so a thought reads as fully replacing the
    // normal dialogue chrome for that beat, rather than the bracketed
    // text appearing twice at once. The Continue button/arrow is
    // deliberately NOT included here: the player still needs it to
    // advance past a thought line.
    public GameObject dialogueBoxBackground;
    public GameObject dialogueLineText;
    public GameObject nameTab;

    [Header("Who")]
    // Only lines spoken by this exact character name are checked for
    // bracketed thoughts — matches this project's convention of using
    // [...] exclusively for Cat_Meritamun's internal monologue.
    public string thoughtCharacterName = "Cat_Meritamun";

    [Header("Pop Animation")]
    public float popDuration = 0.25f;
    [Range(0.5f, 1f)] public float popStartScale = 0.6f;

    private Coroutine popCoroutine;

    // Whatever scale the bubble was authored at in the scene (e.g. if
    // it's been manually resized to something other than 1,1,1) — the
    // pop animation settles back to THIS, not a hardcoded Vector3.one,
    // so a manual resize survives being shown. Captured once up front
    // rather than re-read at the start of every pop, so interrupting a
    // pop mid-animation (StopCoroutine below) can never leave a
    // transient in-between value as the new "rest" scale.
    private Vector3 restScale = Vector3.one;

    void Awake()
    {
        if (bubbleObject != null) restScale = bubbleObject.transform.localScale;
    }

    public override YarnTask OnDialogueStartedAsync()
    {
        // Safety net: guarantee no bubble is left over from a previous
        // playtest or scene when a fresh conversation begins.
        HideBubble();
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        HideBubble();
        return YarnTask.CompletedTask;
    }

    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        if (line.CharacterName == thoughtCharacterName &&
            TryExtractThought(line.TextWithoutCharacterName.Text, out string thought))
        {
            ShowBubble(thought);
        }
        else
        {
            HideBubble();
        }

        return YarnTask.CompletedTask;
    }

    // Finds the first [bracketed] segment in a line's text. Only ever
    // one per line in this project's convention, so first-open/last-
    // close is sufficient — no need for full nested-bracket parsing.
    private bool TryExtractThought(string text, out string thought)
    {
        thought = null;
        if (string.IsNullOrEmpty(text)) return false;

        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return false;

        thought = text.Substring(start + 1, end - start - 1).Trim();
        return thought.Length > 0;
    }

    private void ShowBubble(string thoughtText)
    {
        if (bubbleObject == null || bubbleText == null) return;

        bubbleText.text = thoughtText;
        bubbleObject.SetActive(true);
        SetDialogueChromeVisible(false);

        if (popCoroutine != null) StopCoroutine(popCoroutine);
        popCoroutine = StartCoroutine(PopIn());
    }

    private void HideBubble()
    {
        if (popCoroutine != null)
        {
            StopCoroutine(popCoroutine);
            popCoroutine = null;
        }

        if (bubbleObject != null) bubbleObject.SetActive(false);
        SetDialogueChromeVisible(true);
    }

    private void SetDialogueChromeVisible(bool visible)
    {
        if (dialogueBoxBackground != null) dialogueBoxBackground.SetActive(visible);
        if (dialogueLineText != null) dialogueLineText.SetActive(visible);
        if (nameTab != null) nameTab.SetActive(visible);
    }

    // Quick scale-up "pop" so the bubble doesn't just instantly snap
    // into existence — quadratic ease-out, same easing shape as this
    // project's other pop animations (PortraitManager's expression pop,
    // SplashScreenController's portrait cycle pop).
    private IEnumerator PopIn()
    {
        Transform bubbleTransform = bubbleObject.transform;
        float elapsed = 0f;

        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);
            float eased = 1f - (1f - t) * (1f - t);
            bubbleTransform.localScale = Vector3.Lerp(restScale * popStartScale, restScale, eased);
            yield return null;
        }

        bubbleTransform.localScale = restScale;
        popCoroutine = null;
    }
}
