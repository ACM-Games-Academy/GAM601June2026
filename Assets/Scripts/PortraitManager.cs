using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// PortraitManager
//
// Replaces the old PortraitSwitcher / PlayerPortraitSwitcher pair with
// ONE script that manages any number of on-screen portrait "slots".
//
// Instead of hard-coding "left = NPCs, right = player", you decide at
// runtime — from your .yarn file — exactly which character appears in
// which slot, and when. This supports scenes with 2, 3, 4 or more
// characters simply by adding more slots.
//
// YARN COMMANDS:
//
//   <<showportrait Left Pharaoh>>
//   Puts Pharaoh into the slot named "Left", dimmed, ready to speak.
//
//   <<hideportrait Left>>
//   Removes whoever is currently in the "Left" slot.
//
//   <<hideallportraits>>
//   Clears every slot at once. Use this at the start or end of a
//   scene to guarantee nothing carries over from the previous one.
//
// Whichever character is currently SPEAKING (matching the name before
// the colon in a dialogue line) automatically brightens. Everyone else
// assigned to a slot dims, but stays visible until explicitly hidden.
//
// SETUP:
// 1. Create one UI Image per portrait position you want on screen
//    (e.g. "PortraitLeft", "PortraitRight", "PortraitCenter").
// 2. Create one empty GameObject called "PortraitManager" and attach
//    this script.
// 3. In the Inspector, fill in the Slots list — one entry per Image,
//    each given a short Slot Name (e.g. "Left", "Right", "Center").
// 4. Fill in the Characters list — every character in your game, with
//    their expression sprites, in ONE shared list (no more splitting
//    NPCs and player into separate scripts).
// 5. Add this PortraitManager object to the Dialogue Runner's
//    "Dialogue Presenters" list. Remove the old PortraitSwitcher and
//    PlayerPortraitSwitcher entries if they're still there.

public class PortraitManager : DialoguePresenterBase
{
    [Header("References")]
    public DialogueRunner dialogueRunner;

    [System.Serializable]
    public class SlotConfig
    {
        public string slotName;      // e.g. "Left", "Right", "Center"
        public Image portraitImage; // the UI Image for this position
    }

    [System.Serializable]
    public class Expression
    {
        public string expressionName;
        public Sprite sprite;
        public bool isDefault;
    }

    [System.Serializable]
    public class CharacterPortraits
    {
        public string characterName;
        public List<Expression> expressions = new List<Expression>();
    }

    [Header("Screen Positions")]
    public List<SlotConfig> slots = new List<SlotConfig>();

    [Header("All Characters (shared across every slot)")]
    public List<CharacterPortraits> characters = new List<CharacterPortraits>();

    [Header("Dimming")]
    [Range(0f, 1f)] public float activeAlpha = 1f;
    [Range(0f, 1f)] public float inactiveAlpha = 0.4f;
    public float dimFadeDuration = 0.3f;

    private const string ExpressionTagPrefix = "expression:";

    // Which character (if any) currently occupies each slot
    private Dictionary<string, string> slotAssignments = new Dictionary<string, string>();

    // Tracks the running fade coroutine per slot, so a new fade can
    // cleanly cancel an old one instead of them fighting each other
    private Dictionary<string, Coroutine> activeFades = new Dictionary<string, Coroutine>();

    void Awake()
    {
        dialogueRunner.AddCommandHandler<string, string>("showportrait", ShowPortrait);
        dialogueRunner.AddCommandHandler<string>("hideportrait", HidePortrait);
        dialogueRunner.AddCommandHandler("hideallportraits", HideAllPortraits);
    }

    // ── Yarn commands ────────────────────────────────────────────────────

    private void ShowPortrait(string slotName, string characterName)
    {
        SlotConfig slot = slots.Find(s => s.slotName == slotName);
        if (slot == null)
        {
            Debug.LogWarning("PortraitManager: No slot named '" + slotName + "'.");
            return;
        }

        CharacterPortraits character = characters.Find(c => c.characterName == characterName);
        if (character == null || character.expressions.Count == 0)
        {
            Debug.LogWarning("PortraitManager: No character named '" + characterName + "' with expressions set up.");
            return;
        }

        slotAssignments[slotName] = characterName;

        Expression defaultExpression = character.expressions.Find(e => e.isDefault)
            ?? character.expressions[0];

        slot.portraitImage.sprite = defaultExpression.sprite;
        slot.portraitImage.enabled = true;
        SetAlphaInstant(slot.portraitImage, inactiveAlpha);
    }

    private void HidePortrait(string slotName)
    {
        SlotConfig slot = slots.Find(s => s.slotName == slotName);
        if (slot == null)
        {
            Debug.LogWarning("PortraitManager: No slot named '" + slotName + "'.");
            return;
        }

        slotAssignments.Remove(slotName);

        if (activeFades.ContainsKey(slotName) && activeFades[slotName] != null)
        {
            StopCoroutine(activeFades[slotName]);
            activeFades.Remove(slotName);
        }

        slot.portraitImage.enabled = false;
    }

    private void HideAllPortraits()
    {
        foreach (SlotConfig slot in slots)
        {
            HidePortrait(slot.slotName);
        }
    }

    // ── DialoguePresenterBase ─────────────────────────────────────────────

    public override YarnTask OnDialogueStartedAsync()
    {
        // Safety net: guarantee no portraits are left over from a
        // previous playtest or scene when a fresh conversation begins
        HideAllPortraits();
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        HideAllPortraits();
        return YarnTask.CompletedTask;
    }

    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        string speaker = line.CharacterName;

        foreach (SlotConfig slot in slots)
        {
            if (!slotAssignments.TryGetValue(slot.slotName, out string assignedCharacter))
            {
                // Nothing assigned to this slot right now — leave it hidden
                continue;
            }

            bool isSpeaking = !string.IsNullOrEmpty(speaker) && assignedCharacter == speaker;

            if (isSpeaking)
            {
                CharacterPortraits character = characters.Find(c => c.characterName == assignedCharacter);
                if (character != null)
                {
                    string requested = GetRequestedExpression(line);
                    Expression chosen = null;

                    if (!string.IsNullOrEmpty(requested))
                        chosen = character.expressions.Find(e => e.expressionName == requested);

                    if (chosen == null)
                        chosen = character.expressions.Find(e => e.isDefault);

                    if (chosen == null && character.expressions.Count > 0)
                        chosen = character.expressions[0];

                    if (chosen != null)
                        slot.portraitImage.sprite = chosen.sprite;
                }

                FadeTo(slot, activeAlpha);
            }
            else
            {
                FadeTo(slot, inactiveAlpha);
            }
        }

        return YarnTask.CompletedTask;
    }

    private string GetRequestedExpression(LocalizedLine line)
    {
        if (line.Metadata == null) return null;

        foreach (string tag in line.Metadata)
        {
            if (tag.StartsWith(ExpressionTagPrefix))
                return tag.Substring(ExpressionTagPrefix.Length);
        }

        return null;
    }

    // ── Fading ────────────────────────────────────────────────────────────

    private void FadeTo(SlotConfig slot, float targetAlpha)
    {
        if (activeFades.ContainsKey(slot.slotName) && activeFades[slot.slotName] != null)
        {
            StopCoroutine(activeFades[slot.slotName]);
        }

        activeFades[slot.slotName] = StartCoroutine(FadeAlphaCoroutine(slot, targetAlpha));
    }

    private IEnumerator FadeAlphaCoroutine(SlotConfig slot, float targetAlpha)
    {
        float startAlpha = slot.portraitImage.color.a;
        float elapsed = 0f;

        while (elapsed < dimFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dimFadeDuration);
            SetAlphaInstant(slot.portraitImage, Mathf.Lerp(startAlpha, targetAlpha, t));
            yield return null;
        }

        SetAlphaInstant(slot.portraitImage, targetAlpha);
        activeFades[slot.slotName] = null;
    }

    private void SetAlphaInstant(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    public override YarnTask<DialogueOption> RunOptionsAsync(
        DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
    {
        return YarnTask<DialogueOption>.FromResult(null);
    }
}
