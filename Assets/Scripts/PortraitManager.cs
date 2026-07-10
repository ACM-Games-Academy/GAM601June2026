using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// PortraitManager
//
// Manages any number of on-screen portrait "slots", explicitly shown
// and hidden from .yarn commands, and (new) slides a NameTab UI panel
// to sit above whichever slot is currently speaking.
//
// YARN COMMANDS:
//
//   <<showportrait Left Pharaoh>>      Puts Pharaoh into the "Left" slot.
//   <<hideportrait Left>>              Removes whoever is in "Left".
//   <<hideallportraits>>               Clears every slot at once.
//
// NAME TAB SLIDING:
//
// Whenever the current line's speaker is found to occupy a slot, the
// NameTab RectTransform smoothly slides its horizontal position to
// that slot's configured X position. The tab's vertical position is
// left untouched — that's still handled by NameTab's own anchor
// pinned to the dialogue panel's top edge, exactly as before.
//
// SETUP FOR NAME TAB SLIDING (in addition to existing slot/character setup):
// 1. On NameTab's Rect Transform, make sure it is NOT horizontally
//    stretched — Anchor Min X and Anchor Max X should be equal (e.g.
//    both 0, pinned to the left) so its Width stays fixed and its
//    horizontal position is driven by Anchored Position X.
// 2. Drag NameTab's RectTransform into the "Name Tab Rect" field below.
// 3. Fill in the "Slot Tab Positions" list — one entry per slot,
//    with the Anchored X value you want the tab to slide to for that
//    slot. The easiest way to find these numbers: manually drag
//    NameTab in the Scene view to sit above each portrait in turn,
//    and read off its "Pos X" value from the Rect Transform each time
//    — that's the number to type into that slot's entry.
// 4. Adjust "Tab Move Duration" to taste (0.25s is a natural default).

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

    [Header("Name Tab Sliding")]
    // The NameTab panel's RectTransform. Its vertical position should
    // already be pinned to the dialogue panel's top edge via its own
    // anchor setup — this script only ever touches its X position.
    public RectTransform nameTabRect;

    [System.Serializable]
    public class SlotTabPosition
    {
        public string slotName;  // must match a slot name in 'slots' above
        public float anchoredX; // the Pos X to slide NameTab to for this slot
    }

    public List<SlotTabPosition> slotTabPositions = new List<SlotTabPosition>();
    public float tabMoveDuration = 0.25f;

    private Coroutine tabMoveCoroutine;

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

                // This slot's character is the one speaking this line —
                // slide the NameTab to sit above this slot's position
                MoveNameTabToSlot(slot.slotName);
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

    // ── Name tab sliding ─────────────────────────────────────────────────

    private void MoveNameTabToSlot(string slotName)
    {
        if (nameTabRect == null) return;

        SlotTabPosition targetPos = slotTabPositions.Find(p => p.slotName == slotName);
        if (targetPos == null)
        {
            // No configured position for this slot — leave the tab where it is
            return;
        }

        if (tabMoveCoroutine != null)
        {
            StopCoroutine(tabMoveCoroutine);
        }

        tabMoveCoroutine = StartCoroutine(MoveTabCoroutine(targetPos.anchoredX));
    }

    private IEnumerator MoveTabCoroutine(float targetX)
    {
        Vector2 startPos = nameTabRect.anchoredPosition;
        // Only X changes — Y stays exactly as it already is, since that's
        // still being driven by NameTab's own anchor pinned to the panel's
        // top edge, independent of this script.
        Vector2 targetPos = new Vector2(targetX, startPos.y);

        float elapsed = 0f;

        while (elapsed < tabMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / tabMoveDuration);
            nameTabRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        nameTabRect.anchoredPosition = targetPos;
        tabMoveCoroutine = null;
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
