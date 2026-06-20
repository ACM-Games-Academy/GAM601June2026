using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// PortraitSwitcher
//
// A Dialogue Presenter that swaps a portrait image based on who is
// speaking AND what expression their line is tagged with.
//
// HOW EXPRESSIONS WORK:
// In your .yarn script, add an #expression:name tag to the end of any
// line. For example:
//
//     Pharaoh: You have done well. #expression:happy
//     Pharaoh: This cannot be... #expression:shocked
//     Pharaoh: Welcome, traveller.
//
// That last line has no #expression tag, so it falls back to whichever
// expression is marked "Is Default" for that character in the Inspector.
//
// EACH EXPRESSION IS A SEPARATE, INDEPENDENT SPRITE — there is no
// sprite sheet or atlas involved. You import each expression PNG into
// Unity individually (Texture Type: Sprite (2D and UI)) and drag each
// one into its own slot in the list below.
//
// SETUP:
// 1. Create an empty GameObject, name it "PortraitSwitcher".
// 2. Attach this script.
// 3. Drag your PortraitImage (a UI Image) into the portraitImage slot.
// 4. Build the Characters list:
//      - One entry per speaking character (name must match exactly
//        what appears before ':' in your .yarn lines)
//      - Inside each character, add one entry per expression sprite,
//        e.g. "neutral", "happy", "shocked", "angry" — each pointing
//        to its own separate sprite file
//      - Tick "Is Default" on exactly one expression per character —
//        this is shown whenever a line has no #expression tag
// 5. Click your Dialogue Runner / Dialogue System object.
// 6. In the "Dialogue Presenters" list, add this PortraitSwitcher
//    object alongside your existing Line Presenter.

public class PortraitSwitcher : DialoguePresenterBase
{
    [Header("References")]
    public Image portraitImage;

    [System.Serializable]
    public class Expression
    {
        public string expressionName; // e.g. "neutral", "happy", "shocked"
        public Sprite sprite;         // a single independent image file
        public bool isDefault;        // shown when a line has no #expression tag
    }

    [System.Serializable]
    public class CharacterPortraits
    {
        public string characterName;          // must match the speaker name in .yarn lines
        public List<Expression> expressions = new List<Expression>();
    }

    [Header("Characters and Expressions")]
    public List<CharacterPortraits> characters = new List<CharacterPortraits>();

    // The tag prefix to look for, e.g. a line tagged #expression:happy
    private const string ExpressionTagPrefix = "expression:";

    // ── Required abstract members of DialoguePresenterBase ──────────────────

    public override YarnTask OnDialogueStartedAsync()
    {
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        return YarnTask.CompletedTask;
    }

    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        string speaker = line.CharacterName;

        if (string.IsNullOrEmpty(speaker))
        {
            return YarnTask.CompletedTask;
        }

        // Find this character's entry
        CharacterPortraits character = characters.Find(c => c.characterName == speaker);

        if (character == null || character.expressions.Count == 0)
        {
            portraitImage.enabled = false;
            return YarnTask.CompletedTask;
        }

        // Look for an #expression:xyz tag on this line
        string requestedExpression = GetRequestedExpression(line);

        Expression chosen = null;

        if (!string.IsNullOrEmpty(requestedExpression))
        {
            chosen = character.expressions.Find(e => e.expressionName == requestedExpression);
        }

        // No tag, or the tagged expression wasn't found — fall back
        // to whichever expression is marked as default
        if (chosen == null)
        {
            chosen = character.expressions.Find(e => e.isDefault);
        }

        // Still nothing — just use the first expression in the list
        if (chosen == null)
        {
            chosen = character.expressions[0];
        }

        portraitImage.sprite = chosen.sprite;
        portraitImage.enabled = true;

        return YarnTask.CompletedTask;
    }

    // Scans the line's metadata tags for one starting with "expression:"
    // and returns the part after the colon, e.g. #expression:happy → "happy"
    private string GetRequestedExpression(LocalizedLine line)
    {
        if (line.Metadata == null) return null;

        foreach (string tag in line.Metadata)
        {
            if (tag.StartsWith(ExpressionTagPrefix))
            {
                return tag.Substring(ExpressionTagPrefix.Length);
            }
        }

        return null;
    }

    // This presenter never shows options — your Line/Options Presenter
    // handles that instead.
    public override YarnTask<DialogueOption> RunOptionsAsync(
        DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
    {
        return YarnTask<DialogueOption>.FromResult(null);
    }
}
