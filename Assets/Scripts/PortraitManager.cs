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
//   <<angerreaction Pharaoh>>          Shakes Pharaoh's portrait and
//                                      flashes a red spiky pulse behind
//                                      it — a scripted "anger" beat.
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
    public SoundEffectManager soundEffectManager;

    [System.Serializable]
    public class SlotConfig
    {
        public string slotName;      // e.g. "Left", "Right", "Center"
        public Image portraitImage; // the UI Image for this position

        // Optional empty RectTransform sitting behind the portrait,
        // meant for visual effects (glows, etc.) to be parented into.
        // If left unassigned, effects fall back to the portrait's own
        // parent so nothing new needs to be wired up to keep working.
        public RectTransform effectAnchor;
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

    [Header("Portrait Slide-In")]
    // When a portrait is shown, it starts offset this far to the right
    // (in anchored-position UI units) and slides into its authored
    // resting position instead of just popping in.
    public float slideOffsetX = 60f;
    public float slideInDuration = 0.3f;

    [Header("Expression Swap Pop")]
    // SetExpression() swaps the portrait's sprite instantly — there's no
    // way to crossfade a plain Image without a second overlay Image, so
    // instead this masks the hard cut with a quick scale dip: the
    // portrait shrinks slightly, the sprite swaps at the bottom of the
    // dip (while it's smallest and the cut is least noticeable), then it
    // eases back to full size. Applies to every SetExpression() call —
    // hover, puzzled-hint flash, standing pose, meow, all of it.
    [Range(0.5f, 1f)] public float expressionPopScale = 0.92f;
    public float expressionPopDuration = 0.12f;
    public AnimationCurve expressionPopEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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

    // Shapes how NameTab eases across its main slide, evaluated over
    // normalized time (0-1 in, 0-1 out). Defaults to a standard ease-in-
    // out — drag the curve's middle tangents in the Inspector for a
    // stronger, more visible accelerate/decelerate feel.
    public AnimationCurve tabMoveEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // After the main slide finishes, NameTab overshoots past its target
    // by this many UI units (in the direction of travel) and settles
    // back, instead of stopping dead — a quick spring-like arrival.
    public float tabBounceDistance = 10f;
    public float tabBounceDuration = 0.15f;

    // Shapes the overshoot-and-settle bounce (0 = at target, 1 = fully
    // overshot), evaluated over normalized time. Defaults to a simple
    // rise-then-fall bump — adjust in the Inspector for a snappier or
    // softer bounce.
    public AnimationCurve tabBounceCurve = CreateDefaultBounceCurve();

    private static AnimationCurve CreateDefaultBounceCurve()
    {
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));

        for (int i = 0; i < curve.length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }

        return curve;
    }

    private Coroutine tabMoveCoroutine;

    private const string ExpressionTagPrefix = "expression:";

    // Which character (if any) currently occupies each slot
    private Dictionary<string, string> slotAssignments = new Dictionary<string, string>();

    // Tracks the running fade coroutine per slot, so a new fade can
    // cleanly cancel an old one instead of them fighting each other
    private Dictionary<string, Coroutine> activeFades = new Dictionary<string, Coroutine>();

    // Tracks the running slide-in coroutine per slot, same reasoning as
    // activeFades above
    private Dictionary<string, Coroutine> activeSlides = new Dictionary<string, Coroutine>();

    // Tracks the running expression-swap pop coroutine per slot, same
    // reasoning as activeFades above
    private Dictionary<string, Coroutine> activeExpressionPops = new Dictionary<string, Coroutine>();

    // Each slot's authored resting position, captured once at startup so
    // repeated slide-ins always animate from/to the correct place even
    // if a portrait is re-shown mid-animation.
    private Dictionary<string, Vector2> slotRestingPositions = new Dictionary<string, Vector2>();

    [Header("Anger Reaction")]
    // <<angerreaction CharacterName>> combines these three: a positional
    // shake on the portrait itself, a red spiky pulse behind it, and a
    // sound effect.
    public float angerShakeDuration = 0.5f;
    public float angerShakeIntensity = 18f; // peak horizontal displacement, in UI units
    public int angerShakeCount = 6;        // how many left-right cycles the shake completes

    public Vector2 angerPulseAnchorPoint = new Vector2(0.5f, 0.5f);
    public Vector2 angerPulseAnchoredOffset = Vector2.zero;
    public float angerPulseCircleDiameter = 700f; // deliberately large — testing whether it was being fully covered by the portrait art at the old, smaller default

    public string angerSoundEffectName = "AngerStinger";

    // Tracks the running shake coroutine per slot, so a new shake can
    // cleanly cancel an old one instead of them fighting each other
    private Dictionary<string, Coroutine> activeShakes = new Dictionary<string, Coroutine>();

    void Awake()
    {
        dialogueRunner.AddCommandHandler<string, string>("showportrait", ShowPortrait);
        dialogueRunner.AddCommandHandler<string>("hideportrait", HidePortrait);
        dialogueRunner.AddCommandHandler("hideallportraits", HideAllPortraits);
        dialogueRunner.AddCommandHandler<string>("angerreaction", TriggerAngerReaction);

        foreach (SlotConfig slot in slots)
        {
            if (slot.portraitImage != null)
                slotRestingPositions[slot.slotName] = slot.portraitImage.rectTransform.anchoredPosition;
        }
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

        // Already showing this character in this slot — treat as a
        // no-op rather than re-sliding them in. This is what lets a
        // scripted cutscene (e.g. CrossfadeCharacterInSlot) hand control
        // back to a later <<showportrait>> call for the same character
        // without a redundant pop-in.
        if (slotAssignments.TryGetValue(slotName, out string alreadyAssigned) && alreadyAssigned == characterName)
        {
            return;
        }

        slotAssignments[slotName] = characterName;

        Expression defaultExpression = character.expressions.Find(e => e.isDefault)
            ?? character.expressions[0];

        slot.portraitImage.sprite = defaultExpression.sprite;
        slot.portraitImage.enabled = true;
        SetAlphaInstant(slot.portraitImage, inactiveAlpha);

        SlideIn(slot);
        PlayAppearancePop(slot);
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

        if (activeSlides.ContainsKey(slotName) && activeSlides[slotName] != null)
        {
            StopCoroutine(activeSlides[slotName]);
            activeSlides.Remove(slotName);

            // Snap back to the resting position so the portrait doesn't
            // reappear mid-slide next time it's shown
            if (slotRestingPositions.ContainsKey(slotName))
                slot.portraitImage.rectTransform.anchoredPosition = slotRestingPositions[slotName];
        }

        if (activeShakes.ContainsKey(slotName) && activeShakes[slotName] != null)
        {
            StopCoroutine(activeShakes[slotName]);
            activeShakes.Remove(slotName);

            // Snap back to the resting position so a future occupant of
            // this slot doesn't inherit a mid-shake offset
            if (slotRestingPositions.ContainsKey(slotName))
                slot.portraitImage.rectTransform.anchoredPosition = slotRestingPositions[slotName];
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

    // ── Anger reaction ──────────────────────────────────────────────────

    // Combines a portrait shake, a red spiky pulse behind it, and a
    // sound effect — a scripted "anger" beat, callable directly from
    // dialogue. Does nothing (silently) if the character isn't
    // currently in any slot.
    private void TriggerAngerReaction(string characterName)
    {
        // TEMPORARY diagnostics for tracking down why the pulse isn't
        // appearing — safe to remove once resolved.
        Debug.Log("[AngerDebug] TriggerAngerReaction('" + characterName + "') called.");

        bool foundInSlot = false;
        foreach (SlotConfig s in slots)
        {
            if (slotAssignments.TryGetValue(s.slotName, out string a) && a == characterName)
            {
                foundInSlot = true;
                Debug.Log("[AngerDebug] '" + characterName + "' is currently in slot '" + s.slotName + "'.");
            }
        }
        if (!foundInSlot)
        {
            Debug.LogWarning("[AngerDebug] '" + characterName + "' is NOT assigned to any slot right now — " +
                              "PlayEffectOnCharacter will silently do nothing. Check the <<showportrait>> call " +
                              "actually ran for this exact character name (case-sensitive) before <<angerreaction>>.");
        }

        ShakePortrait(characterName, angerShakeDuration, angerShakeIntensity, angerShakeCount);

        PlayEffectOnCharacter<AngerPulseEffect>(characterName, pulse =>
        {
            Debug.Log("[AngerDebug] configureEffect running on new AngerPulseEffect instance.");
            pulse.anchorPoint = angerPulseAnchorPoint;
            pulse.anchoredOffset = angerPulseAnchoredOffset;
            pulse.circleDiameter = angerPulseCircleDiameter;
        });

        if (soundEffectManager != null)
        {
            soundEffectManager.PlaySoundEffect(angerSoundEffectName);
        }
        else
        {
            Debug.LogWarning("[AngerDebug] soundEffectManager is not assigned on PortraitManager.");
        }
    }

    // Shakes a character's portrait side to side around its resting
    // position, with amplitude decaying to 0 over 'duration', then
    // settles it back exactly on its resting position. Public — usable
    // on its own (e.g. from gameplay code), not just via
    // <<angerreaction>>. Does nothing if the character isn't currently
    // in any slot.
    public void ShakePortrait(string characterName, float duration, float intensity, int shakeCount)
    {
        foreach (SlotConfig slot in slots)
        {
            if (!slotAssignments.TryGetValue(slot.slotName, out string assigned)) continue;
            if (assigned != characterName) continue;

            if (activeShakes.ContainsKey(slot.slotName) && activeShakes[slot.slotName] != null)
            {
                StopCoroutine(activeShakes[slot.slotName]);
            }

            // A shake takes priority over an in-progress slide-in (e.g.
            // <<angerreaction>> fired the same instant as <<showportrait>>)
            // — cancel it so the two don't fight over anchoredPosition
            // every frame, which would make both look glitchy.
            if (activeSlides.ContainsKey(slot.slotName) && activeSlides[slot.slotName] != null)
            {
                StopCoroutine(activeSlides[slot.slotName]);
                activeSlides.Remove(slot.slotName);
            }

            activeShakes[slot.slotName] = StartCoroutine(ShakeCoroutine(slot, duration, intensity, shakeCount));
            return;
        }
    }

    private IEnumerator ShakeCoroutine(SlotConfig slot, float duration, float intensity, int shakeCount)
    {
        RectTransform rect = slot.portraitImage.rectTransform;

        Vector2 restingPosition = slotRestingPositions.TryGetValue(slot.slotName, out Vector2 resting)
            ? resting
            : rect.anchoredPosition;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Amplitude decays to 0 over the shake's lifetime, like the
            // trembling settling down rather than stopping dead
            float dampenedIntensity = intensity * (1f - t);

            float horizontal = Mathf.Sin(t * shakeCount * Mathf.PI * 2f) * dampenedIntensity;
            // A little Perlin-noise vertical jitter on top of the clean
            // horizontal swing, for a more "trembling with rage" texture
            // than a smooth side-to-side wobble alone
            float vertical = (Mathf.PerlinNoise(elapsed * 25f, 0f) - 0.5f) * 2f * dampenedIntensity * 0.4f;

            rect.anchoredPosition = restingPosition + new Vector2(horizontal, vertical);

            yield return null;
        }

        rect.anchoredPosition = restingPosition;
    }

    // Crossfades whichever character is showing in a slot to a
    // different character's default expression, in place, instead of
    // an instant swap — used for one-off cinematic reveals (e.g. the
    // opening day sequence's Meritamun -> Cat_Meritamun transformation).
    // A temporary Image fades in on top of the slot's portrait WHILE the
    // portrait itself fades out in the same loop — unlike a background
    // (a fully opaque rectangle, where fading a new layer in alone looks
    // identical to a true crossfade), portrait art has transparent
    // regions and mismatched silhouettes, so the old portrait has to be
    // faded out explicitly or it stays visible through the gaps.
    public IEnumerator CrossfadeCharacterInSlot(string slotName, string newCharacterName, float duration)
    {
        SlotConfig slot = slots.Find(s => s.slotName == slotName);
        if (slot == null)
        {
            Debug.LogWarning("PortraitManager: No slot named '" + slotName + "'.");
            yield break;
        }

        CharacterPortraits newCharacter = characters.Find(c => c.characterName == newCharacterName);
        if (newCharacter == null || newCharacter.expressions.Count == 0)
        {
            Debug.LogWarning("PortraitManager: No character named '" + newCharacterName + "' with expressions set up.");
            yield break;
        }

        Expression defaultExpression = newCharacter.expressions.Find(e => e.isDefault)
            ?? newCharacter.expressions[0];

        RectTransform portraitRect = slot.portraitImage.rectTransform;

        GameObject crossfadeObject = new GameObject("PortraitCrossfade", typeof(RectTransform));
        RectTransform crossfadeRect = crossfadeObject.GetComponent<RectTransform>();

        crossfadeRect.SetParent(portraitRect.parent, false);
        crossfadeRect.anchorMin = portraitRect.anchorMin;
        crossfadeRect.anchorMax = portraitRect.anchorMax;
        crossfadeRect.pivot = portraitRect.pivot;
        crossfadeRect.anchoredPosition = portraitRect.anchoredPosition;
        crossfadeRect.sizeDelta = portraitRect.sizeDelta;
        crossfadeRect.localScale = portraitRect.localScale;
        crossfadeRect.SetSiblingIndex(portraitRect.GetSiblingIndex() + 1); // render just in front of the portrait

        Image crossfadeImage = crossfadeObject.AddComponent<Image>();
        crossfadeImage.sprite = defaultExpression.sprite;
        crossfadeImage.raycastTarget = false;
        SetAlphaInstant(crossfadeImage, 0f);

        float startAlpha = slot.portraitImage.color.a;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Old portrait fades out while the new one fades in, in
            // lockstep, so neither one's solidity outpaces the other's
            SetAlphaInstant(slot.portraitImage, Mathf.Lerp(startAlpha, 0f, t));
            SetAlphaInstant(crossfadeImage, Mathf.Lerp(0f, activeAlpha, t));

            yield return null;
        }

        // The slot now truly shows the new character at full opacity,
        // exactly as if <<showportrait>> had been called for them
        slotAssignments[slotName] = newCharacterName;
        slot.portraitImage.sprite = defaultExpression.sprite;
        slot.portraitImage.enabled = true;
        SetAlphaInstant(slot.portraitImage, activeAlpha);
        PlayAppearancePop(slot);

        Destroy(crossfadeObject);
    }

    // Find which slot (if any) a character currently occupies, and swap
    // their displayed expression — used by hover effects etc. Rather
    // than cutting the sprite instantly, this eases into a quick scale
    // pop (see ExpressionPopCoroutine) so the swap reads as a soft
    // transition instead of a hard flicker.
    public void SetExpression(string characterName, string expressionName)
    {
        foreach (SlotConfig slot in slots)
        {
            if (!slotAssignments.TryGetValue(slot.slotName, out string assigned)) continue;
            if (assigned != characterName) continue;

            CharacterPortraits character = characters.Find(c => c.characterName == characterName);
            if (character == null) return;

            Expression chosen = character.expressions.Find(e => e.expressionName == expressionName);
            if (chosen == null)
                chosen = character.expressions.Find(e => e.isDefault);

            if (chosen != null)
            {
                if (activeExpressionPops.TryGetValue(slot.slotName, out Coroutine running) && running != null)
                {
                    StopCoroutine(running);
                }

                activeExpressionPops[slot.slotName] = StartCoroutine(ExpressionPopCoroutine(slot, chosen.sprite));
            }

            return;
        }
    }

    // Starts (or restarts) a scale pop on this slot with no sprite swap
    // — used for any other "character appearance" moment (becoming
    // fully solid from dimmed/transparent, sliding in for the first
    // time, finishing a transformation crossfade) so the same soft
    // treatment applies everywhere a portrait becomes visible, not just
    // on expression swaps.
    private void PlayAppearancePop(SlotConfig slot)
    {
        if (activeExpressionPops.TryGetValue(slot.slotName, out Coroutine running) && running != null)
        {
            StopCoroutine(running);
        }

        activeExpressionPops[slot.slotName] = StartCoroutine(ExpressionPopCoroutine(slot, null));
    }

    // Shrinks the portrait to expressionPopScale, swaps in newSprite (if
    // given — pass null for an appearance pop with no sprite change) at
    // the bottom of the dip, when it's smallest and any cut is least
    // noticeable, then eases back to full size. A cancelled/interrupted
    // pop always resets scale back to 1 first, so rapid successive pops
    // (e.g. quickly re-hovering) never compound into a smaller-and-
    // smaller portrait.
    private IEnumerator ExpressionPopCoroutine(SlotConfig slot, Sprite newSprite)
    {
        RectTransform rect = slot.portraitImage.rectTransform;
        rect.localScale = Vector3.one;

        if (expressionPopDuration <= 0f)
        {
            if (newSprite != null) slot.portraitImage.sprite = newSprite;
            activeExpressionPops[slot.slotName] = null;
            yield break;
        }

        float halfDuration = expressionPopDuration / 2f;
        bool swapped = false;
        float elapsed = 0f;

        while (elapsed < expressionPopDuration)
        {
            elapsed += Time.deltaTime;

            if (newSprite != null && !swapped && elapsed >= halfDuration)
            {
                slot.portraitImage.sprite = newSprite;
                swapped = true;
            }

            float scale;
            if (elapsed < halfDuration)
            {
                float t = expressionPopEaseCurve.Evaluate(Mathf.Clamp01(elapsed / halfDuration));
                scale = Mathf.Lerp(1f, expressionPopScale, t);
            }
            else
            {
                float t = expressionPopEaseCurve.Evaluate(Mathf.Clamp01((elapsed - halfDuration) / halfDuration));
                scale = Mathf.Lerp(expressionPopScale, 1f, t);
            }

            rect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        if (newSprite != null && !swapped)
        {
            slot.portraitImage.sprite = newSprite;
        }

        rect.localScale = Vector3.one;
        activeExpressionPops[slot.slotName] = null;
    }

    // Force a character's portrait to full (or dimmed) brightness,
    // independent of who is currently speaking.
    public void SetBrightness(string characterName, bool bright)
    {
        foreach (SlotConfig slot in slots)
        {
            if (!slotAssignments.TryGetValue(slot.slotName, out string assigned)) continue;
            if (assigned != characterName) continue;

            FadeTo(slot, bright ? activeAlpha : inactiveAlpha);
            return;
        }
    }

    // Find which slot (if any) a character currently occupies, and spawn
    // a TEffect there (e.g. PulseGlowEffect for correct answers,
    // WrongAnswerWaveEffect for incorrect ones). Does nothing if the
    // character isn't currently assigned to any slot — that's an
    // expected case (e.g. they've left the scene), not an error.
    public void PlayEffectOnCharacter<TEffect>(string characterName, System.Action<TEffect> configureEffect = null, bool inFront = false)
        where TEffect : MonoBehaviour
    {
        foreach (SlotConfig slot in slots)
        {
            if (!slotAssignments.TryGetValue(slot.slotName, out string assigned)) continue;
            if (assigned != characterName) continue;

            SpawnEffectInSlot(slot, configureEffect, inFront);
            return;
        }
    }

    // Same idea, but for callers that already know exactly which slot
    // they mean (e.g. a cinematic sequence) rather than needing to
    // resolve one from a character name — useful mid-transformation,
    // when "who's in this slot" is about to change.
    public void PlayEffectInSlot<TEffect>(string slotName, System.Action<TEffect> configureEffect = null, bool inFront = false)
        where TEffect : MonoBehaviour
    {
        SlotConfig slot = slots.Find(s => s.slotName == slotName);
        if (slot == null) return;

        SpawnEffectInSlot(slot, configureEffect, inFront);
    }

    private void SpawnEffectInSlot<TEffect>(SlotConfig slot, System.Action<TEffect> configureEffect, bool inFront)
        where TEffect : MonoBehaviour
    {
        Transform effectParent;

        if (inFront)
        {
            // Parenting directly onto the portrait itself renders in
            // front of it — the mirror image of the "behind" case
            // below: uGUI always draws a child after (i.e. on top of)
            // its own parent.
            effectParent = slot.portraitImage.transform;
        }
        else
        {
            // Prefer the slot's dedicated effect anchor so the effect
            // sits behind the portrait. If effectAnchor hasn't been
            // wired up, build a throwaway anchor that mirrors the
            // portrait's own rect exactly and sits directly behind it
            // in the hierarchy — see CreateBehindPortraitAnchor for why
            // the effect can't just be parented onto the portrait Image
            // itself in this case.
            effectParent = slot.effectAnchor != null
                ? (Transform)slot.effectAnchor
                : CreateBehindPortraitAnchor(slot).transform;
        }

        GameObject effectObject = new GameObject(typeof(TEffect).Name, typeof(RectTransform));
        effectObject.transform.SetParent(effectParent, false);

        TEffect effect = effectObject.AddComponent<TEffect>();

        // Let the caller tweak settings before Start() runs
        configureEffect?.Invoke(effect);
    }

    // Builds a throwaway RectTransform that exactly mirrors a portrait
    // Image's own rect (same anchors, pivot, position and size) and
    // inserts it as that portrait's immediate sibling, directly before
    // it in the hierarchy. uGUI always draws a parent's own Graphic
    // before recursing into its children — sibling index can't override
    // that — so an effect parented directly onto the portrait Image
    // would always render in front of it, no matter its sibling index.
    // Parenting the effect into this same-sized sibling instead, placed
    // earlier in the draw order, is what actually gets it to render
    // behind the portrait while still lining up with it visually.
    private GameObject CreateBehindPortraitAnchor(SlotConfig slot)
    {
        RectTransform portraitRect = slot.portraitImage.rectTransform;

        GameObject anchorObject = new GameObject("GlowAnchor (behind portrait)", typeof(RectTransform));
        RectTransform anchorRect = anchorObject.GetComponent<RectTransform>();

        anchorRect.SetParent(portraitRect.parent, false);
        anchorRect.anchorMin = portraitRect.anchorMin;
        anchorRect.anchorMax = portraitRect.anchorMax;
        anchorRect.pivot = portraitRect.pivot;

        // Use the portrait's AUTHORED resting position rather than its
        // current anchoredPosition. If an effect fires immediately
        // after <<showportrait>> (e.g. <<angerreaction>> right after
        // it), the portrait may still be parked at its slide-in's
        // offset starting position — mirroring that transient position
        // would leave this anchor behind at the wrong spot once the
        // portrait finishes sliding into place.
        anchorRect.anchoredPosition = slotRestingPositions.TryGetValue(slot.slotName, out Vector2 resting)
            ? resting
            : portraitRect.anchoredPosition;

        anchorRect.sizeDelta = portraitRect.sizeDelta;
        anchorRect.localScale = portraitRect.localScale;

        // Insert directly before the portrait so this anchor's whole
        // subtree draws ahead of (i.e. behind) the portrait Image.
        anchorRect.SetSiblingIndex(portraitRect.GetSiblingIndex());

        // This anchor only exists to host whatever effect gets parented
        // into it — once that effect has destroyed itself, the anchor
        // has nothing left to do. Rather than PortraitManager needing to
        // know each effect type's own timing, the anchor just watches
        // its own child count and cleans itself up once it's empty.
        anchorObject.AddComponent<DestroyWhenEmpty>();

        return anchorObject;
    }

    // Destroys its own GameObject once it has no children left. Used to
    // clean up throwaway effect anchors without PortraitManager needing
    // to know how long any particular effect runs for.
    private class DestroyWhenEmpty : MonoBehaviour
    {
        void Update()
        {
            if (transform.childCount == 0)
            {
                Destroy(gameObject);
            }
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
        // A new line starting is the reliable signal that the player
        // has advanced past whatever line triggered a sustained sound
        // (e.g. <<playsustainedsound CatPurr>>) — cut it off here
        // rather than letting it run to completion regardless of
        // dialogue pacing.
        if (soundEffectManager != null)
        {
            soundEffectManager.StopSustainedSound();
        }

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

                // Push this slot's portrait to the front of the draw order
                // so it renders on top of every other portrait sharing the
                // same parent, regardless of their transparency. Without
                // this, a dimmed portrait sitting later in the Hierarchy
                // can still visually overlap a brighter one underneath it.
                slot.portraitImage.transform.SetAsLastSibling();

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

        // Which way NameTab is travelling, so the bounce below overshoots
        // further in that same direction rather than snapping backwards.
        float travelDirection = Mathf.Sign(targetPos.x - startPos.x);

        // ── Main slide: eases from a standing start, accelerating away
        // from startPos and decelerating into targetPos, instead of
        // moving at a constant stiff speed.
        float elapsed = 0f;

        while (elapsed < tabMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / tabMoveDuration);
            float eased = tabMoveEaseCurve.Evaluate(t);
            nameTabRect.anchoredPosition = Vector2.LerpUnclamped(startPos, targetPos, eased);
            yield return null;
        }

        // ── Bounce: overshoot slightly past targetPos in the direction
        // of travel, then ease back — a quick spring-like arrival rather
        // than a hard stop.
        Vector2 overshootPos = targetPos + new Vector2(travelDirection * tabBounceDistance, 0f);
        elapsed = 0f;

        while (elapsed < tabBounceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / tabBounceDuration);
            float bounceT = tabBounceCurve.Evaluate(t);
            nameTabRect.anchoredPosition = Vector2.LerpUnclamped(targetPos, overshootPos, bounceT);
            yield return null;
        }

        nameTabRect.anchoredPosition = targetPos;
        tabMoveCoroutine = null;
    }

    // ── Slide-in ─────────────────────────────────────────────────────────

    private void SlideIn(SlotConfig slot)
    {
        if (!slotRestingPositions.ContainsKey(slot.slotName)) return;

        if (activeSlides.ContainsKey(slot.slotName) && activeSlides[slot.slotName] != null)
        {
            StopCoroutine(activeSlides[slot.slotName]);
        }

        activeSlides[slot.slotName] = StartCoroutine(SlideInCoroutine(slot));
    }

    private IEnumerator SlideInCoroutine(SlotConfig slot)
    {
        RectTransform rect = slot.portraitImage.rectTransform;
        Vector2 restingPosition = slotRestingPositions[slot.slotName];
        Vector2 startPosition = restingPosition + new Vector2(slideOffsetX, 0f);

        rect.anchoredPosition = startPosition;

        float elapsed = 0f;

        while (elapsed < slideInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideInDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out: quick start, gentle settle
            rect.anchoredPosition = Vector2.Lerp(startPosition, restingPosition, eased);
            yield return null;
        }

        rect.anchoredPosition = restingPosition;
        activeSlides[slot.slotName] = null;
    }

    // ── Fading ────────────────────────────────────────────────────────────

    private void FadeTo(SlotConfig slot, float targetAlpha)
    {
        // Genuinely becoming more visible (dimmed/transparent -> solid)
        // — e.g. this slot's character just became the active speaker,
        // or SetBrightness(true) was called — gets the same soft pop as
        // an expression swap. Fading the other way (going dim) doesn't;
        // only "appearing" warrants it.
        if (targetAlpha > slot.portraitImage.color.a)
        {
            PlayAppearancePop(slot);
        }

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
