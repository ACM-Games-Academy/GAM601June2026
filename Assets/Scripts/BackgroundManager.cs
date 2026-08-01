using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// ─────────────────────────────────────────────────────────────────────────────
// BackgroundManager — Yarn Spinner version
//
// Crossfades between a nighttime and daytime background sprite.
// The scene STARTS AT NIGHT — this matters for the guard clauses below,
// which prevent redundant fades (e.g. calling <<fadetonight>> when it's
// already night does nothing).
//
// Both fade methods are registered directly as Yarn commands via the
// [YarnCommand] attribute:
//
//     <<fadetonight BackgroundManager>>
//     <<fadetoday BackgroundManager>>
//
// The second word must match the exact name of the GameObject this
// script is attached to in the Hierarchy.
//
// Because these are coroutines, Yarn Spinner automatically PAUSES the
// dialogue until the fade finishes.
//
// SETUP IN UNITY:
// 1. Two UI Image objects on your Canvas, stacked on top of each other,
//    both stretching to fill the full canvas:
//      - BackgroundBottom  (sits behind)
//      - BackgroundTop     (sits in front, this is the one that fades)
//    BackgroundBottom must sit ABOVE BackgroundTop in the Hierarchy
//    list so BackgroundTop renders on top. Both must sit above your
//    dialogue UI in render order too (i.e. higher in the Hierarchy list).
//
// 2. Attach this script to a GameObject named "BackgroundManager".
//
// 3. In the Inspector assign:
//      - Bottom Layer  → BackgroundBottom Image
//      - Top Layer     → BackgroundTop Image
//      - Day Sprite    → your daytime background sprite
//      - Night Sprite  → your nighttime background sprite
//      - Fade Duration → how long the crossfade takes in seconds
//
// 4. In your .yarn script:
//
//        Narrator: Your journey continues...
//        <<fadetoday BackgroundManager>>
//        ===
// ─────────────────────────────────────────────────────────────────────────────

public class BackgroundManager : MonoBehaviour
{
    [Header("Background Image Layers")]
    public Image bottomLayer;
    public Image topLayer;

    [Header("Sprites")]
    public Sprite daySprite;
    public Sprite nightSprite;

    [Header("Settings")]
    public float fadeDuration = 1.5f;

    // How long the very first frame of the scene takes to fade in from
    // solid black — softens the cut from the splash screen's own
    // fade-to-black (see SplashScreenController) into gameplay, rather
    // than the scene just popping in fully lit immediately.
    public float openingFadeInDuration = 1.5f;

    [Header("Opening Intertitle")]
    // Plays once, entirely on top of the still-solid-black screen,
    // BEFORE the background reveal (FadeInFromBlackAtStart) below ever
    // starts — text fades in, holds, fades out, and only THEN does the
    // night background itself begin fading into view. Since Yarn
    // Spinner's DialogueRunner auto-starts independently of this
    // sequence, whatever <<wait>> sits at the top of the starting node
    // needs to cover at least this whole sequence's total duration
    // (intertitle fade in + hold + fade out + openingFadeInDuration
    // above), or the first portrait/line can appear before the
    // intertitle has actually finished — there's no code-level link
    // between the two, just this comment as a reminder to keep them in
    // sync if either changes.
    public string intertitleText = "The Land of the Dead";
    public TMP_FontAsset intertitleFont;
    public float intertitleFontSize = 72f;
    public Color intertitleColor = new Color(0.95f, 0.85f, 0.55f); // warm gold, matches the game's title-screen palette
    public float intertitleFadeInDuration = 1.5f;
    public float intertitleHoldDuration = 2.5f;
    // Deliberately the slowest of the three — "fades out slowly" was
    // asked for specifically, so this shouldn't just mirror
    // intertitleFadeInDuration by default.
    public float intertitleFadeOutDuration = 2.5f;

    private TextMeshProUGUI intertitleTextComponent;

    [Header("Wordsearch Visibility")]
    // The top-level wordsearch UI object (your GridPanel, or a parent
    // panel wrapping it). Shown when day arrives, hidden at night.
    public GameObject wordsearchPanel;

    // Optional but recommended: also reference GridManager so we can
    // force input off the moment night begins, as a safety net in
    // case a puzzle was left mid-solve.
    public GridManager gridManager;

    [System.Serializable]
    public class TorchGlowConfig
    {
        // Where this torch sits in the night sprite, in anchoredPosition
        // UI units relative to the background rect's center — there's no
        // way to know this from code, so drag each entry in Play mode
        // (or eyeball it against the art in the Scene view) until the
        // glow lines up with an actual torch/brazier in the painting.
        public Vector2 anchoredPosition;
        public float diameter = 220f;
        public Color color = new Color(1f, 0.65f, 0.25f, 1f); // warm torch orange
        [Range(0f, 1f)] public float baseAlpha = 0.35f;
    }

    [Header("Night Ambience — Torch Glow")]
    // Empty by default — add one entry per torch/brazier visible in the
    // night sprite and position it in the Inspector. Each one breathes
    // independently (randomized phase per torch) rather than in unison,
    // which is what actually reads as "alive" rather than mechanical.
    public List<TorchGlowConfig> torchGlows = new List<TorchGlowConfig>();
    public float torchFlickerSpeed = 1.3f;
    [Range(0f, 1f)] public float torchFlickerAmount = 0.15f;

    [Header("Night Ambience — Drifting Clouds")]
    public bool enableDriftingClouds = true;
    public int cloudCount = 2;
    // Deliberately large/visible defaults — easier to find them and dial
    // down (diameter, alpha) than to start invisible and guess upward.
    public float cloudDiameter = 900f;
    [Range(0f, 1f)] public float cloudAlpha = 0.35f;
    public Color cloudColor = new Color(0.75f, 0.8f, 0.9f, 1f); // pale, moonlit
    public float cloudDriftSpeed = 6f; // UI units per second
    // Clouds spawn/wrap with a random Y within +/- this, around
    // cloudBandCenterY — keep them up in the sky portion of the art.
    public float cloudBandCenterY = 300f;
    public float cloudBandHeight = 120f;

    private GameObject nightAmbienceContainer;
    private List<Image> torchGlowImages;
    private List<float> torchFlickerSeeds;
    private List<RectTransform> cloudRects;
    private float cloudWrapHalfWidth;

    private static Sprite cachedSoftGlowSprite;
    private const int GlowTextureSize = 128;

    [Header("Day Ambience — God Rays")]
    // Shared PREFAB (not just shared data) — assign the SAME
    // DayAtmosphereEffect prefab asset here and on SplashScreenController.
    // Editing the prefab (positions, rotations, adding more effects to it
    // later) updates every scene that instantiates it. The prefab itself
    // references a GodRaySettings asset for its tunable values.
    public GameObject dayAtmosphereEffectPrefab;

    private GameObject dayAmbienceContainer;

    [Header("Day Ambience — Critters")]
    // CritterCatchEffect's own GameObject (not a prefab — it builds its
    // own display layer itself) — toggled active/inactive at the exact
    // same points as dayAmbienceContainer above, since critters have no
    // business skittering across a night sky.
    public GameObject dayCrittersEffect;

    // Faded out (rather than left to pop or freeze) in FadeToNight if
    // a critter-catch exclamation or Yarn thought bubble is still up
    // when night falls — see ThoughtBubblePresenter.FadeOutOverTime.
    public ThoughtBubblePresenter thoughtBubblePresenter;

    // Used to cut off any still-playing <<sadreaction>> droplet cascade
    // at the same points thoughtBubblePresenter above gets faded out —
    // see PortraitManager.StopAllSadReactions.
    public PortraitManager portraitManager;

    private bool isFading = false;

    // The scene STARTS AT NIGHT — see Start() below.
    private bool isDay = false;

    // Awake() runs strictly before Start() (and before the scene's
    // first frame is ever presented) — covering both layers in solid
    // black here, rather than in Start(), closes a real gap where
    // whatever these Images happened to be serialized as in the scene
    // file (e.g. bottomLayer still showing the fully-lit day background
    // from whenever it was last edited/previewed in the Editor) could
    // otherwise flash through for a frame before any script corrects it.
    void Awake()
    {
        if (bottomLayer != null) bottomLayer.color = Color.black;
        if (topLayer != null) topLayer.color = Color.black;
    }

    void Start()
    {
        // Initialise both layers to the NIGHTTIME background,
        // matching how the game actually begins. bottomLayer's tint is
        // reset to white here — Awake() above deliberately left it
        // black, and SetAlpha only ever touches alpha, never RGB, so
        // without this reset it would stay black-tinted (i.e.
        // invisible) forever.
        bottomLayer.color = Color.white;
        bottomLayer.sprite = nightSprite;
        topLayer.sprite = nightSprite;

        SetAlpha(bottomLayer, 1f);

        // Cover everything in solid black immediately, then fade it away
        // below — without this, the very first frame shows the fully-lit
        // night scene instantly, popping in rather than easing in.
        // (Awake() above already set this; restated here so this still
        // reads correctly and stays correct on its own even if Awake()
        // above is ever changed.)
        topLayer.color = new Color(0f, 0f, 0f, 1f);

        isDay = false;

        // The game starts at night — the wordsearch shouldn't be
        // visible or interactive until the first daytime sequence
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(false);
        }

        if (gridManager != null)
        {
            gridManager.inputEnabled = false;
        }

        // The scene starts at night — crossfade from whatever's playing
        // (e.g. the day theme still fading in from the splash screen)
        // into the night track, rather than just cutting to silence.
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayNightMusic();
        }

        BuildNightAmbience();
        nightAmbienceContainer.SetActive(true); // the game starts at night

        BuildDayAmbience();
        dayAmbienceContainer.SetActive(false); // the game starts at night

        if (dayCrittersEffect != null) dayCrittersEffect.SetActive(false); // the game starts at night

        // Released EARLY — concurrently with the intertitle below, not
        // after it. This persistent overlay (see SceneTransitionOverlay)
        // sits on a Canvas with a much higher sorting order than
        // anything in this scene, specifically so it can cover the
        // scene-load boundary — but that also means it would silently
        // hide the intertitle text behind itself for as long as it
        // stayed opaque. topLayer below is still fully opaque black at
        // this point (its own fade hasn't started yet), so the screen
        // stays solid black regardless once this overlay clears out of
        // the way. If the scene was entered directly (e.g. testing it
        // standalone in the Editor) this just creates an already-
        // transparent overlay and fades it from 0 to 0, a harmless no-op.
        SceneTransitionOverlay.GetOrCreate().FadeOut(openingFadeInDuration);

        StartCoroutine(PlayIntertitleThenRevealScene());
    }

    // Plays the opening intertitle text on top of the still-solid-black
    // screen, then hands off to the background reveal that used to
    // start immediately in Start().
    private IEnumerator PlayIntertitleThenRevealScene()
    {
        yield return StartCoroutine(PlayIntertitle());

        StartCoroutine(FadeInFromBlackAtStart());
    }

    // Builds (once) a centered, full-screen-anchored text object sitting
    // above topLayer — same "find or create" convention as the rest of
    // this project's runtime-built UI — then fades it in, holds, and
    // fades it back out, entirely while the background underneath is
    // still solid black. Skipped entirely if intertitleText is blank.
    private IEnumerator PlayIntertitle()
    {
        if (string.IsNullOrEmpty(intertitleText)) yield break;

        BuildIntertitle();

        yield return StartCoroutine(FadeIntertitleAlpha(0f, 1f, intertitleFadeInDuration));
        yield return new WaitForSeconds(intertitleHoldDuration);
        yield return StartCoroutine(FadeIntertitleAlpha(1f, 0f, intertitleFadeOutDuration));
    }

    private void BuildIntertitle()
    {
        if (intertitleTextComponent != null) return;

        Transform backgroundParent = topLayer.transform.parent;

        GameObject textObject = new GameObject("Intertitle", typeof(RectTransform));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(backgroundParent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Last sibling so it renders above topLayer (still opaque black
        // at this point) and above the night ambience/god rays layers
        // built just above — nothing should be able to cover this text.
        rect.SetAsLastSibling();

        intertitleTextComponent = textObject.AddComponent<TextMeshProUGUI>();
        intertitleTextComponent.text = intertitleText;
        intertitleTextComponent.fontSize = intertitleFontSize;
        intertitleTextComponent.alignment = TextAlignmentOptions.Center;
        intertitleTextComponent.color = new Color(intertitleColor.r, intertitleColor.g, intertitleColor.b, 0f);
        intertitleTextComponent.raycastTarget = false;
        if (intertitleFont != null) intertitleTextComponent.font = intertitleFont;
    }

    private IEnumerator FadeIntertitleAlpha(float fromAlpha, float toAlpha, float duration)
    {
        Color c = intertitleTextComponent.color;
        c.a = fromAlpha;
        intertitleTextComponent.color = c;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / duration));
            intertitleTextComponent.color = c;
            yield return null;
        }

        c.a = toAlpha;
        intertitleTextComponent.color = c;
    }

    // Fades the solid black cover set at the top of Start() away, so the
    // scene eases in rather than popping in fully lit on its first frame.
    private IEnumerator FadeInFromBlackAtStart()
    {
        isFading = true; // same guard FadeToNight/FadeToDay use, so a command firing this early can't fight over topLayer

        // Scene activation just did a burst of synchronous work (every
        // object's Awake/Start, including one-time procedural texture
        // generation like the torch/cloud glow sprite and the critters'
        // mouse/snake art — see GetSoftGlowSprite/GetMouseSprite/
        // GetSnakeSprite) all in a single frame. That frame's
        // Time.deltaTime can spike well above normal, and since the
        // screen is still solid black at this point that's harmless —
        // EXCEPT that if the fade timer below started accumulating from
        // that same spiked frame, its very first step would already
        // leap forward by however long the hitch took, which reads as
        // a visible skip right as the fade begins. Waiting one frame
        // here lets that spike land on a frame that does nothing, so
        // the timer below only ever starts counting from a normal-sized
        // delta.
        yield return null;

        float elapsed = 0f;
        while (elapsed < openingFadeInDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / openingFadeInDuration));
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        // Reset topLayer to its normal resting state (transparent white)
        // now that it's invisible, so the first real <<fadetoday>>/
        // <<fadetonight>> — which only ever touch alpha, not RGB —
        // behaves exactly as it always has.
        topLayer.color = new Color(1f, 1f, 1f, 0f);
        isFading = false;
    }

    // ── Yarn-callable commands ─────────────────────────────────────────────

    [YarnCommand("fadetonight")]
    public IEnumerator FadeToNight()
    {
        if (isFading) yield break;
        if (!isDay) yield break;   // already night, nothing to do

        isDay = false;

        // Hide the wordsearch immediately — night gameplay shouldn't
        // show it at all, even while the background is mid-fade
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(false);
        }

        // Safety net: force input off in case a puzzle was left
        // unsolved when night arrived
        if (gridManager != null)
        {
            gridManager.inputEnabled = false;
        }

        // God rays don't belong in a night sky — hide immediately, same
        // as the wordsearch panel above.
        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(false);

        // Same for the critters — mice and snakes shouldn't keep
        // skittering across a night sky.
        if (dayCrittersEffect != null) dayCrittersEffect.SetActive(false);

        // A critter-catch exclamation (or a Yarn-driven bracketed
        // thought) can still be up when night falls — without this it
        // either sat frozen at full opacity while the background darkened
        // around it, or got yanked away instantly, either way reading as
        // out of sync with everything else's gradual crossfade. Fading
        // it out over the SAME fadeDuration keeps it in step.
        if (thoughtBubblePresenter != null)
        {
            thoughtBubblePresenter.FadeOutOverTime(fadeDuration);
        }

        // Same reasoning as the thought bubble above — a <<sadreaction>>
        // triggered earlier in this same beat has no "new line" left to
        // stop it on if night falls before one arrives.
        if (portraitManager != null)
        {
            portraitManager.StopAllSadReactions();
        }

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayNightMusic();
        }

        yield return StartCoroutine(CrossFade(nightSprite));

        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(true);
    }

    [YarnCommand("fadetoday")]
    public IEnumerator FadeToDay()
    {
        if (isFading) yield break;
        if (isDay) yield break;   // already day, nothing to do

        isDay = true;

        // Torches and a moonlit sky don't belong in daylight — hide
        // immediately, same as the wordsearch panel does the reverse in
        // FadeToNight above.
        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(false);

        // Same reasoning as FadeToNight's equivalent call — stop any
        // <<sadreaction>> cascade still running from the night scene
        // that's ending, rather than letting it linger into daytime.
        if (portraitManager != null)
        {
            portraitManager.StopAllSadReactions();
        }

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayDayMusic();
        }

        yield return StartCoroutine(CrossFade(daySprite));

        // Reveal the wordsearch now that daytime has fully arrived
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(true);
        }

        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(true);
        if (dayCrittersEffect != null) dayCrittersEffect.SetActive(true);
    }

    // ── Opening day reveal (cutscene helpers) ───────────────────────────────
    //
    // Used by OpeningDayRevealSequence (night→day) and
    // ClosingNightRevealSequence (day→night) for their one-off
    // transformation cutscenes, where the screen fades to black BEHIND
    // a portrait (which stays visible, since topLayer already renders
    // behind the portraits canvas), rather than straight-crossfading to
    // the destination sprite.

    // Fades topLayer to solid opaque black, covering bottomLayer (and
    // anything else drawn behind the portraits) without touching
    // whichever portrait is currently on top of it.
    public IEnumerator FadeToBlackOverlay(float duration)
    {
        isFading = true;

        // Every ambience/gameplay layer is inserted as a later sibling
        // than topLayer, so each renders in FRONT of the background —
        // including in front of topLayer once it's faded to opaque
        // black. Without this, whichever set belongs to the CURRENT
        // time of day (torches/clouds at night; wordsearch, god rays,
        // critters by day) would stay visibly floating on top of the
        // "black" screen for the whole transformation crossfade that
        // happens behind it, instead of actually being hidden. Hiding
        // all of them unconditionally is safe regardless of which
        // direction this cutscene is running: whichever set doesn't
        // belong to the CURRENT time of day is already inactive, so
        // hiding it again is a harmless no-op.
        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(false);
        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(false);
        if (dayCrittersEffect != null) dayCrittersEffect.SetActive(false);
        if (wordsearchPanel != null) wordsearchPanel.SetActive(false);

        topLayer.color = new Color(0f, 0f, 0f, 0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / duration);
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        topLayer.color = new Color(0f, 0f, 0f, 1f);
    }

    // While fully hidden behind the black overlay, swaps bottomLayer
    // straight to daySprite (no visible pop, since topLayer is fully
    // opaque at this point), then fades the black overlay back out to
    // reveal it. Mirrors FadeToDay's bookkeeping (isDay, wordsearchPanel).
    public IEnumerator RevealDayFromBlackOverlay(float duration)
    {
        // Turn off the night torches/clouds while the screen is still
        // fully black (from the preceding FadeToBlackOverlay), so there's
        // no visible pop — without this, they never get told day has
        // arrived and just keep animating on top of the day art forever.
        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(false);

        bottomLayer.sprite = daySprite;
        SetAlpha(bottomLayer, 1f);

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayDayMusic();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration));
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        // Reset topLayer to its normal resting state (transparent white)
        // now that it's invisible, so later ordinary day/night
        // crossfades — which only ever touch alpha, not RGB — behave
        // exactly as they did before this cutscene ran.
        topLayer.color = new Color(1f, 1f, 1f, 0f);

        isDay = true;
        isFading = false;
    }

    // Mirrors RevealDayFromBlackOverlay above, for ClosingNightRevealSequence's
    // day→night transformation cutscene: swaps bottomLayer straight to
    // nightSprite while fully hidden behind the black overlay, then
    // fades the black overlay back out to reveal it. Unlike the day
    // reveal, nothing here needs a separate "reveal gameplay elements"
    // step — night has no wordsearch/day-ambience/critters equivalent,
    // so nightAmbienceContainer is switched on directly at the end.
    public IEnumerator RevealNightFromBlackOverlay(float duration)
    {
        bottomLayer.sprite = nightSprite;
        SetAlpha(bottomLayer, 1f);

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayNightMusic();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration));
            topLayer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        // Reset topLayer to its normal resting state (transparent white)
        // now that it's invisible, so later ordinary day/night
        // crossfades — which only ever touch alpha, not RGB — behave
        // exactly as they did before this cutscene ran.
        topLayer.color = new Color(1f, 1f, 1f, 0f);

        isDay = false;
        isFading = false;

        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(true);
    }

    // Split out from RevealDayFromBlackOverlay above so
    // OpeningDayRevealSequence can insert its "Thebes" location title
    // card in the gap between the background finishing its reveal and
    // the wordsearch grid/day ambience/critters actually appearing,
    // rather than having them all pop in together the instant the
    // background is visible.
    public void ShowDayGameplayElements()
    {
        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(true);
        }

        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(true);
        if (dayCrittersEffect != null) dayCrittersEffect.SetActive(true);
    }

    // ── Crossfade coroutine ───────────────────────────────────────────────

    private IEnumerator CrossFade(Sprite targetSprite)
    {
        isFading = true;

        topLayer.sprite = targetSprite;
        SetAlpha(topLayer, 0f);

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeDuration);
            SetAlpha(topLayer, alpha);
            yield return null;
        }

        bottomLayer.sprite = targetSprite;
        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

        isFading = false;
    }

    private void SetAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    // ── Night ambience: torch glow + drifting clouds ────────────────────────

    void Update()
    {
        if (nightAmbienceContainer != null && nightAmbienceContainer.activeSelf)
        {
            AnimateTorchFlicker();
            AnimateCloudDrift();
        }

        // God rays are entirely self-driven by the DayAtmosphereEffect
        // prefab instance (see BuildDayAmbience) — nothing to do here.
    }

    // Lets you build (and re-sync) the night ambience objects without
    // pressing Play, so you can select and drag them — especially the
    // torch glows — directly in the Scene view against the actual night
    // art, instead of guessing coordinates blind or fiddling in Play
    // mode where nothing sticks once you stop. Available from the ⋮ menu
    // on this component in the Inspector.
    //
    // Also switches the background layers to the night sprite so the art
    // itself is actually visible in the Scene view while you work — by
    // default the Scene tab just shows whatever sprite was last saved,
    // which has nothing to do with Start()'s runtime logic.
    [ContextMenu("Build Night Ambience In Editor")]
    private void BuildNightAmbienceInEditor()
    {
        if (bottomLayer != null)
        {
            bottomLayer.sprite = nightSprite;
            SetAlpha(bottomLayer, 1f);
        }
        if (topLayer != null)
        {
            topLayer.sprite = nightSprite;
            SetAlpha(topLayer, 0f);
        }

        SeedExampleTorchesIfEmpty();
        BuildNightAmbience();

        if (nightAmbienceContainer != null) nightAmbienceContainer.SetActive(true);
    }

    // First-time convenience only: if you haven't configured any torches
    // yet, seeds three large, brightly-lit placeholders spread across the
    // screen so there's something obvious to find and drag into place —
    // much easier to drag a glaring circle onto the right spot and dial
    // it down afterward than to guess coordinates for something invisible.
    private void SeedExampleTorchesIfEmpty()
    {
        if (torchGlows != null && torchGlows.Count > 0) return;

        Color obviousOrange = new Color(1f, 0.55f, 0.1f, 1f);
        torchGlows = new List<TorchGlowConfig>
        {
            new TorchGlowConfig { anchoredPosition = new Vector2(-600f, 0f), diameter = 400f, baseAlpha = 0.9f, color = obviousOrange },
            new TorchGlowConfig { anchoredPosition = new Vector2(0f, 0f),    diameter = 400f, baseAlpha = 0.9f, color = obviousOrange },
            new TorchGlowConfig { anchoredPosition = new Vector2(600f, 0f),  diameter = 400f, baseAlpha = 0.9f, color = obviousOrange },
        };
    }

    // Finds (or creates, on first run) a container sitting just above the
    // background layers — in front of both, but still behind the
    // gameplay UI, since it's inserted as their immediate sibling —
    // then re-syncs its torch/cloud children against the current
    // configuration. Safe to call repeatedly (from Start(), or from the
    // Editor context menu above): existing children are matched by name
    // and never have their position/size reset, so anything you've
    // already dragged into place in the Scene view stays put.
    private void BuildNightAmbience()
    {
        Transform backgroundParent = topLayer.transform.parent;
        Transform existingContainer = backgroundParent.Find("NightAmbience");

        if (existingContainer != null)
        {
            nightAmbienceContainer = existingContainer.gameObject;
        }
        else
        {
            nightAmbienceContainer = new GameObject("NightAmbience", typeof(RectTransform));
            RectTransform newContainerRect = nightAmbienceContainer.GetComponent<RectTransform>();
            newContainerRect.SetParent(backgroundParent, false);
            newContainerRect.anchorMin = Vector2.zero;
            newContainerRect.anchorMax = Vector2.one;
            newContainerRect.offsetMin = Vector2.zero;
            newContainerRect.offsetMax = Vector2.zero;

            // Sit directly above both background layers (renders in
            // front of them) but stay behind everything else already in
            // the hierarchy — grid, portraits, dialogue UI, etc.
            newContainerRect.SetSiblingIndex(topLayer.transform.GetSiblingIndex() + 1);
        }

        RectTransform containerRect = nightAmbienceContainer.GetComponent<RectTransform>();
        cloudWrapHalfWidth = (containerRect.rect.width * 0.5f) + cloudDiameter;

        BuildTorchGlows(containerRect);

        if (enableDriftingClouds)
        {
            BuildDriftingClouds(containerRect);
        }
        else
        {
            RemoveExtraChildren(containerRect, "Cloud ", 0);
        }
    }

    // Finds a direct child by name, or creates a fresh one if it doesn't
    // exist yet — same pattern as SplashScreenController's editor-build
    // system. wasCreated tells the caller whether to apply starting
    // position/size (only ever done once) or leave an existing
    // RectTransform exactly as it was left, whether that's the default
    // or something you've since dragged elsewhere.
    private GameObject FindOrCreateAmbienceChild(Transform parent, string name, out bool wasCreated)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            wasCreated = false;
            return existing.gameObject;
        }

        GameObject created = new GameObject(name, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        wasCreated = true;
        return created;
    }

    // Removes any leftover numbered children (TorchGlow N / Cloud N)
    // whose index is no longer within range — e.g. you deleted an entry
    // from torchGlows, or turned drifting clouds off, since the last
    // build.
    private void RemoveExtraChildren(Transform container, string namePrefix, int keepCount)
    {
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            if (!child.name.StartsWith(namePrefix)) continue;

            string suffix = child.name.Substring(namePrefix.Length);
            if (int.TryParse(suffix, out int index) && index >= keepCount)
            {
                DestroyGameObjectSafely(child.gameObject);
            }
        }
    }

    private void DestroyGameObjectSafely(GameObject obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void BuildTorchGlows(RectTransform container)
    {
        torchGlowImages = new List<Image>();
        torchFlickerSeeds = new List<float>();

        for (int i = 0; i < torchGlows.Count; i++)
        {
            TorchGlowConfig config = torchGlows[i];

            GameObject glowObject = FindOrCreateAmbienceChild(container, "TorchGlow " + i, out bool wasCreated);
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();

            if (wasCreated)
            {
                glowRect.anchorMin = new Vector2(0.5f, 0.5f);
                glowRect.anchorMax = new Vector2(0.5f, 0.5f);
                glowRect.pivot = new Vector2(0.5f, 0.5f);
                // Starting point only — drag it into place afterward;
                // later rebuilds won't reset a position you've dragged.
                glowRect.anchoredPosition = config.anchoredPosition;
            }

            glowRect.sizeDelta = new Vector2(config.diameter, config.diameter);

            Image glowImage = glowObject.GetComponent<Image>();
            if (glowImage == null) glowImage = glowObject.AddComponent<Image>();
            glowImage.sprite = GetSoftGlowSprite();
            glowImage.raycastTarget = false;
            glowImage.color = new Color(config.color.r, config.color.g, config.color.b, config.baseAlpha);

            torchGlowImages.Add(glowImage);

            // Distinct random phase per torch so they flicker out of
            // sync with each other — synchronized flicker reads as
            // mechanical, not alive.
            torchFlickerSeeds.Add(Random.Range(0f, 1000f));
        }

        RemoveExtraChildren(container, "TorchGlow ", torchGlows.Count);
    }

    private void AnimateTorchFlicker()
    {
        if (torchGlowImages == null) return;

        for (int i = 0; i < torchGlowImages.Count; i++)
        {
            TorchGlowConfig config = torchGlows[i];
            float noise = Mathf.PerlinNoise(torchFlickerSeeds[i], Time.time * torchFlickerSpeed);
            float alpha = config.baseAlpha + (noise - 0.5f) * 2f * torchFlickerAmount;

            Image image = torchGlowImages[i];
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }
    }

    private void BuildDriftingClouds(RectTransform container)
    {
        cloudRects = new List<RectTransform>();

        for (int i = 0; i < cloudCount; i++)
        {
            GameObject cloudObject = FindOrCreateAmbienceChild(container, "Cloud " + i, out bool wasCreated);
            RectTransform cloudRect = cloudObject.GetComponent<RectTransform>();

            if (wasCreated)
            {
                cloudRect.anchorMin = new Vector2(0.5f, 0.5f);
                cloudRect.anchorMax = new Vector2(0.5f, 0.5f);
                cloudRect.pivot = new Vector2(0.5f, 0.5f);

                // Spread the clouds out along the drift path to start,
                // rather than all beginning stacked at the same point.
                float startX = Mathf.Lerp(-cloudWrapHalfWidth, cloudWrapHalfWidth, i / (float)Mathf.Max(cloudCount, 1));
                cloudRect.anchoredPosition = new Vector2(startX, RandomCloudY());
            }

            // A cloud shouldn't read as a perfect circle — stretch it
            // horizontally.
            cloudRect.sizeDelta = new Vector2(cloudDiameter, cloudDiameter * 0.55f);

            Image cloudImage = cloudObject.GetComponent<Image>();
            if (cloudImage == null) cloudImage = cloudObject.AddComponent<Image>();
            cloudImage.sprite = GetSoftGlowSprite();
            cloudImage.raycastTarget = false;
            cloudImage.color = new Color(cloudColor.r, cloudColor.g, cloudColor.b, cloudAlpha);

            cloudRects.Add(cloudRect);
        }

        RemoveExtraChildren(container, "Cloud ", cloudCount);
    }

    private void AnimateCloudDrift()
    {
        if (cloudRects == null) return;

        foreach (RectTransform cloudRect in cloudRects)
        {
            Vector2 pos = cloudRect.anchoredPosition;
            pos.x += cloudDriftSpeed * Time.deltaTime;

            if (pos.x > cloudWrapHalfWidth)
            {
                pos.x = -cloudWrapHalfWidth;
                pos.y = RandomCloudY();
            }

            cloudRect.anchoredPosition = pos;
        }
    }

    private float RandomCloudY()
    {
        return cloudBandCenterY + Random.Range(-cloudBandHeight, cloudBandHeight);
    }

    // A soft radial gradient, fully opaque at the center fading smoothly
    // to transparent at the edge — shared by both torch glows and clouds
    // (their color/size/alpha are all applied via the Image, not baked
    // into the texture), generated once and cached like this project's
    // other procedural VFX (see PulseGlowEffect, WrongAnswerWaveEffect).
    private Sprite GetSoftGlowSprite()
    {
        if (cachedSoftGlowSprite != null) return cachedSoftGlowSprite;

        Texture2D texture = new Texture2D(GlowTextureSize, GlowTextureSize, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(GlowTextureSize / 2f, GlowTextureSize / 2f);
        float maxDistance = GlowTextureSize / 2f;

        Color32[] pixels = new Color32[GlowTextureSize * GlowTextureSize];

        for (int y = 0; y < GlowTextureSize; y++)
        {
            for (int x = 0; x < GlowTextureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalizedDistance);
                pixels[y * GlowTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedSoftGlowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, GlowTextureSize, GlowTextureSize),
            new Vector2(0.5f, 0.5f));

        return cachedSoftGlowSprite;
    }

    // ── Day ambience: god rays (via DayAtmosphereEffect prefab) ──────────────

    // Lets you instantiate the day atmosphere prefab without pressing
    // Play, and switches the background layers to the day sprite so the
    // temple art is actually visible in the Scene view while you work.
    // Positioning/tuning the rays themselves now happens by editing the
    // DayAtmosphereEffect prefab directly (or the GodRaySettings asset it
    // references) — not here.
    [ContextMenu("Build Day Ambience In Editor")]
    private void BuildDayAmbienceInEditor()
    {
        if (bottomLayer != null)
        {
            bottomLayer.sprite = daySprite;
            SetAlpha(bottomLayer, 1f);
        }
        if (topLayer != null)
        {
            topLayer.sprite = daySprite;
            SetAlpha(topLayer, 0f);
        }

        BuildDayAmbience();

        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(true);
    }

    // Finds (or instantiates) the DayAtmosphereEffect prefab as a child
    // sitting just above the background layers. Unlike the old inline
    // system, this component no longer builds or animates anything
    // itself — the prefab instance manages its own rays entirely (see
    // DayAtmosphereEffect.cs), so editing the prefab asset (or the
    // GodRaySettings it references) updates every scene that uses it.
    private void BuildDayAmbience()
    {
        if (dayAtmosphereEffectPrefab == null)
        {
            Debug.LogWarning("BackgroundManager: Day Atmosphere Effect Prefab isn't assigned — nothing to build.");
            return;
        }

        Transform backgroundParent = topLayer.transform.parent;
        Transform existing = backgroundParent.Find("DayAtmosphereEffect");
        GameObject instance;

        if (existing != null)
        {
            instance = existing.gameObject;
        }
        else
        {
            instance = Instantiate(dayAtmosphereEffectPrefab, backgroundParent);
            instance.name = "DayAtmosphereEffect"; // strip the "(Clone)" suffix so Find() above matches on future calls
        }

        // Enforced every time, not just on creation — an instance found
        // already sitting in the scene could otherwise be left wherever
        // it happened to land (e.g. after the grid/portraits/dialogue UI
        // in sibling order), rendering on top of things it should sit
        // behind. Sitting right after topLayer keeps it above the
        // background art but below every other UI element.
        RectTransform instanceRect = instance.transform as RectTransform;
        if (instanceRect != null)
        {
            instanceRect.SetSiblingIndex(topLayer.transform.GetSiblingIndex() + 1);
        }

        dayAmbienceContainer = instance;
    }
}
