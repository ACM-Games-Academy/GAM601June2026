using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// PortraitSwitcher
//
// A Dialogue Presenter that controls one portrait image on screen.
// Run two instances side by side — one configured with NPC characters
// (left portrait), one configured with the player character (right
// portrait) — to get a two-portrait VN layout.
//
// DIMMING BEHAVIOUR:
// Rather than hiding the portrait when it's not this character's turn,
// it stays on screen at a dimmed opacity, and brightens back up when
// they're speaking again. This keeps both portraits visible throughout
// the conversation, which reads as more polished than portraits
// popping in and out.
//
// EXPRESSIONS:
// Add an #expression:name tag to the end of any line in your .yarn
// script to choose a specific expression sprite, e.g.:
//     Pharaoh: You have done well. #expression:happy
// A line with no tag uses whichever expression is marked "Is Default".
//
// SETUP:
// 1. Create an empty GameObject, attach this script.
// 2. Drag a UI Image into the Portrait Image slot.
// 3. Build the Characters list — for a single-portrait-per-side setup,
//    add just the one character this instance is responsible for.
// 4. Adjust Active Alpha / Inactive Alpha / Dim Fade Duration to taste.
// 5. Add this object to the Dialogue Runner's "Dialogue Presenters" list.

public class PortraitSwitcher : DialoguePresenterBase
{
    [Header("References")]
    public Image portraitImage;

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

    [Header("Characters and Expressions")]
    public List<CharacterPortraits> characters = new List<CharacterPortraits>();

    [Header("Dimming")]
    [Range(0f, 1f)] public float activeAlpha = 1f;
    [Range(0f, 1f)] public float inactiveAlpha = 0.4f;
    public float dimFadeDuration = 0.3f;

    private const string ExpressionTagPrefix = "expression:";

    // Tracks the currently running fade so a new one can cleanly
    // interrupt it without overlapping coroutines stacking up
    private Coroutine activeFadeCoroutine;

    // ── Required abstract members of DialoguePresenterBase ──────────────────

    public override YarnTask OnDialogueStartedAsync()
    {
        // Show this character's default expression, dimmed, as soon as
        // the conversation begins — so both portraits are visible from
        // the very first line, rather than popping in later.
        if (characters.Count > 0 && characters[0].expressions.Count > 0)
        {
            CharacterPortraits firstCharacter = characters[0];

            Expression defaultExpression = firstCharacter.expressions.Find(e => e.isDefault)
                ?? firstCharacter.expressions[0];

            portraitImage.sprite = defaultExpression.sprite;
            portraitImage.enabled = true;
            SetAlphaInstant(inactiveAlpha);
        }
        else
        {
            portraitImage.enabled = false;
        }

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

        CharacterPortraits character = characters.Find(c => c.characterName == speaker);

        if (character == null || character.expressions.Count == 0)
        {
            // This line isn't spoken by any character this switcher
            // manages — dim instead of hiding, keeping the last
            // expression shown on screen
            FadeTo(inactiveAlpha);
            return YarnTask.CompletedTask;
        }

        // It IS this switcher's character speaking — pick the
        // requested expression (or the default) and brighten
        string requestedExpression = GetRequestedExpression(line);

        Expression chosen = null;

        if (!string.IsNullOrEmpty(requestedExpression))
        {
            chosen = character.expressions.Find(e => e.expressionName == requestedExpression);
        }

        if (chosen == null)
        {
            chosen = character.expressions.Find(e => e.isDefault);
        }

        if (chosen == null)
        {
            chosen = character.expressions[0];
        }

        portraitImage.sprite = chosen.sprite;
        portraitImage.enabled = true;
        FadeTo(activeAlpha);

        return YarnTask.CompletedTask;
    }

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

    // ── Alpha fading ──────────────────────────────────────────────────────

    private void FadeTo(float targetAlpha)
    {
        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
        }

        activeFadeCoroutine = StartCoroutine(FadeAlphaCoroutine(targetAlpha));
    }

    private IEnumerator FadeAlphaCoroutine(float targetAlpha)
    {
        Color startColor = portraitImage.color;
        float startAlpha = startColor.a;
        float elapsed = 0f;

        while (elapsed < dimFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dimFadeDuration);
            SetAlphaInstant(Mathf.Lerp(startAlpha, targetAlpha, t));
            yield return null;
        }

        SetAlphaInstant(targetAlpha);
        activeFadeCoroutine = null;
    }

    private void SetAlphaInstant(float alpha)
    {
        Color c = portraitImage.color;
        c.a = alpha;
        portraitImage.color = c;
    }

    // This presenter never shows options — your Line/Options Presenter
    // handles that instead.
    public override YarnTask<DialogueOption> RunOptionsAsync(
        DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
    {
        return YarnTask<DialogueOption>.FromResult(null);
    }
}
