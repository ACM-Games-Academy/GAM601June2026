using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// SplashScreenController
//
// Builds the game's splash/title screen — Canvas, background, character
// art, title text and Play button. Every element is created the first
// time it's needed, then found and REUSED after that (by name, as a
// direct child), rather than being torn down and rebuilt from scratch —
// so once an element exists, its RectTransform position/size is yours
// to drag around in the Scene view and this script will never reset it
// again. Only its content (sprite/text/color/font) gets refreshed.
//
// To get elements into the Scene view for hand-positioning (e.g. the
// title), use the ⋮ menu on this component in the Inspector and choose
// "Build In Editor" — this runs the same build the game uses at
// Start(), but in Edit mode, so the resulting GameObjects get saved
// with the scene. Drag TitleText (or anything else) wherever you like
// afterwards; re-running "Build In Editor" or pressing Play won't move
// it again.
//
// SETUP IN UNITY:
// 1. Create a new empty Scene (e.g. "SplashScreen") and add it to
//    Build Settings ABOVE your gameplay scene, so it loads first.
// 2. Also make sure your gameplay scene (e.g. "YarnViabilityTest") is
//    in Build Settings, since the Play button loads it by name.
// 3. In the new scene, attach this script to an empty GameObject.
// 4. In the Inspector assign:
//      - Background Sprite       → Assets/Sprites/Background/TempleDayBackground.png
//      - Left Portrait Cycle     → one sprite per other character (Bes, Sekhmet, Harwa, ...)
//      - Right Portrait Cycle    → every Meritamun/Cat_Meritamun expression sprite
//      - Establishing Shot Sprite → Assets/Sprites/Background/Establishing_Scene_Image_2.png
//        (optional — leave unassigned to skip straight to the gameplay
//        scene like before, no establishing shot)
//    Both portrait lists cycle forever with a pop transition between
//    entries — see "Portrait Cycle Timing" for pacing.
// 5. Adjust Gameplay Scene Name if your main scene isn't named
//    "YarnViabilityTest".
// 6. Optional: right-click this component's header (⋮ menu) and choose
//    "Build In Editor" to generate everything as real, visible,
//    draggable GameObjects ahead of time.

public class SplashScreenController : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite backgroundSprite;

    // Cycles through every listed sprite in order, looping forever, with
    // the same "ease pop" transition PortraitManager uses in-game for
    // expression swaps (shrink slightly, swap the sprite at the bottom
    // of the dip, ease back up) — rather than a hard cut. Meant to hold
    // every expression of Meritamun and Cat_Meritamun; add/reorder
    // freely in the Inspector.
    [Header("Right Portrait Cycle — Meritamun / Cat_Meritamun")]
    public List<Sprite> rightPortraitSprites = new List<Sprite>();

    // Same idea, meant for one portrait per OTHER character (not
    // Meritamun/Cat_Meritamun) so the splash screen shows some variety
    // of who's in the story.
    [Header("Left Portrait Cycle — Other Characters")]
    public List<Sprite> leftPortraitSprites = new List<Sprite>();

    [Header("Portrait Cycle Timing")]
    public float portraitCycleInterval = 4f;
    [Range(0.5f, 1f)] public float portraitPopScale = 0.92f;
    public float portraitPopDuration = 0.3f;

    private Dictionary<GameObject, Coroutine> portraitCycleCoroutines = new Dictionary<GameObject, Coroutine>();

    private Image establishingShotOverlayImage;
    private Image splashBlackOverlay;
    private CanvasGroup establishingShotDialogueBoxGroup;
    private TextMeshProUGUI establishingShotDialogueText;
    private bool isTransitioningToGameplay = false;

    [Header("Font")]
    // Applied to both the title and the Play button label. Leave blank
    // to fall back to TextMeshPro's default project font.
    public TMP_FontAsset gameFont;

    [Header("Title")]
    public string gameTitle = "BAST";
    public int titleFontSize = 180;
    public Color titleColor = new Color(0.95f, 0.85f, 0.55f); // warm gold, matches the game's palette

    [Header("Play Button")]
    public string playButtonLabel = "Play";
    public Color playButtonColor = new Color(0.72f, 0.53f, 0.15f);
    public string gameplaySceneName = "YarnViabilityTest";

    // Shown full-screen after Play is clicked: the splash screen fades
    // to black first, then the establishing shot fades in FROM that
    // black (not a crossfade over the splash content), holds, then
    // fades back to black before the gameplay scene actually loads — a
    // brief establishing shot bridging the two, rather than an abrupt
    // cut straight from the splash screen into gameplay.
    [Header("Establishing Shot")]
    public Sprite establishingShotSprite;
    // How long the splash screen itself (art/title/portraits/button)
    // takes to fade to solid black, before the establishing shot starts
    // fading in from that black.
    public float splashFadeToBlackDuration = 1f;
    public float establishingShotFadeInDuration = 1.2f;
    public float establishingShotHoldDuration = 12f;
    public float establishingShotFadeToBlackDuration = 0.8f;
    // Played once the splash screen has fully faded to black — the
    // splash screen's own day music (see Start() below) is faded out
    // completely first (via MusicManager.StopMusic()), then this track
    // fades in on its own rather than crossfading against it.
    public AudioClip establishingShotMusicClip;

    // A single static line shown in a small dialogue box over the
    // establishing shot — not driven by Yarn Spinner (there's no
    // interactivity or branching here, just one line appearing and
    // disappearing on a timer), so this is just plain text. The box
    // reuses the same background sprite as the main gameplay dialogue
    // box (Assets/Sprites/UI/Panel Background (1).png) for visual
    // consistency between the two — assign that same sprite here.
    public Sprite dialogueBoxBackgroundSprite;
    [TextArea]
    public string establishingShotLine = "The world of the living -- everyone I love and care about, gone forever...";
    // Fades in AFTER the image itself has finished fading in (i.e. at
    // the start of the hold), rather than at the same time — so the
    // image gets a beat to register on its own first.
    public float establishingShotTextFadeInDuration = 0.6f;

    // Shared PREFAB with BackgroundManager's daytime god rays — assign
    // the SAME DayAtmosphereEffect prefab asset to both, and editing the
    // prefab (positions, rotations, adding more effects to it later)
    // applies everywhere that instantiates it. The splash screen uses a
    // daytime background too, so it gets the same treatment.
    [Header("Sky Ambience — God Rays")]
    public GameObject dayAtmosphereEffectPrefab;

    void Start()
    {
        EnsureEventSystem();
        BuildSplashScreen();

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayDayMusic();
        }
    }

    // Lets you generate the splash screen's GameObjects in the Editor,
    // without pressing Play, so you can select and drag them (e.g.
    // TitleText) directly in the Scene view. Available from the ⋮ menu
    // on this component in the Inspector.
    [ContextMenu("Build In Editor")]
    private void BuildInEditor()
    {
        EnsureEventSystem();
        BuildSplashScreen();
    }

    // ── Setup ────────────────────────────────────────────────────────────

    // A fresh scene has no EventSystem of its own — without one, the
    // Play button can't receive clicks at all.
    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildSplashScreen()
    {
        RectTransform canvasRect = FindOrCreateCanvas();

        CreateBackground(canvasRect);
        BuildDayAtmosphere(canvasRect);

        // Same GameObject names as the old single-sprite versions, so an
        // already-saved scene reuses (rather than orphans) the existing
        // objects — only the content behind them changed.
        CreateCyclingCharacterImage(canvasRect, leftPortraitSprites, "MeritamunWorried",
            anchor: new Vector2(0f, 0f), size: new Vector2(450f, 700f), offset: new Vector2(60f, 0f));
        CreateCyclingCharacterImage(canvasRect, rightPortraitSprites, "CatMeritamunPawRaised",
            anchor: new Vector2(1f, 0f), size: new Vector2(450f, 700f), offset: new Vector2(-60f, 0f));
        CreateTitle(canvasRect);
        CreatePlayButton(canvasRect);

        // Built before the establishing shot overlay so it renders
        // directly BEHIND it — solid black, faded in first to hide the
        // splash content, so the establishing shot image (built next)
        // can then fade in from true black instead of crossfading over
        // the splash screen.
        CreateSplashBlackOverlay(canvasRect);

        // Built last so it's the last sibling — renders on top of
        // everything above (title, portraits, Play button) once faded
        // in, the same way any of this file's other elements rely on
        // build order for layering.
        CreateEstablishingShotOverlay(canvasRect);

        // Built after the overlay image, so it renders on top of it —
        // otherwise the fully-opaque establishing shot image would
        // cover the dialogue box once both are visible.
        CreateEstablishingShotDialogueBox(canvasRect);
    }

    // ── Find-or-create helpers ───────────────────────────────────────────

    // Finds a direct child by name, or creates a fresh one if it doesn't
    // exist yet. wasCreated tells the caller whether to apply default
    // layout values (only ever done once, on first creation) or leave
    // an existing RectTransform exactly as a human left it.
    private GameObject FindOrCreateChild(Transform parent, string name, out bool wasCreated)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            wasCreated = false;
            return existing.gameObject;
        }

        GameObject newObject = new GameObject(name, typeof(RectTransform));
        newObject.transform.SetParent(parent, false);
        wasCreated = true;
        return newObject;
    }

    private RectTransform FindOrCreateCanvas()
    {
        Transform existing = transform.Find("SplashCanvas");
        if (existing != null)
        {
            return existing as RectTransform;
        }

        GameObject canvasObject = new GameObject("SplashCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        return canvasObject.GetComponent<RectTransform>();
    }

    // ── Elements ─────────────────────────────────────────────────────────

    private void CreateBackground(RectTransform parent)
    {
        if (backgroundSprite == null) return;

        GameObject bgObject = FindOrCreateChild(parent, "Background", out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = bgObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        Image image = bgObject.GetComponent<Image>();
        if (image == null) image = bgObject.AddComponent<Image>();
        image.sprite = backgroundSprite;
        image.preserveAspect = false; // fill the screen edge to edge
        image.raycastTarget = false;
    }

    // Full-screen solid black, no sprite — sits directly behind
    // establishingShotOverlayImage. Built once, kept fully transparent
    // until PlayEstablishingShotThenLoadGameplay fades it in ahead of
    // everything else, hiding the splash content in solid black before
    // the establishing shot image itself starts fading in on top of it.
    private void CreateSplashBlackOverlay(RectTransform parent)
    {
        GameObject overlayObject = FindOrCreateChild(parent, "SplashBlackOverlay", out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        overlayObject.transform.SetAsLastSibling();

        splashBlackOverlay = overlayObject.GetComponent<Image>();
        if (splashBlackOverlay == null) splashBlackOverlay = overlayObject.AddComponent<Image>();
        splashBlackOverlay.raycastTarget = false;
        splashBlackOverlay.color = new Color(0f, 0f, 0f, 0f);
    }

    // Full-screen overlay for the establishing shot — built once, kept
    // fully transparent (and non-blocking) until PlayEstablishingShotThenLoadGameplay
    // actually animates it. raycastTarget stays off permanently; the
    // click-guard in OnPlayButtonClicked is what stops a second click
    // from starting a second transition, rather than this overlay
    // physically blocking input.
    private void CreateEstablishingShotOverlay(RectTransform parent)
    {
        if (establishingShotSprite == null) return;

        GameObject overlayObject = FindOrCreateChild(parent, "EstablishingShotOverlay", out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        overlayObject.transform.SetAsLastSibling();

        establishingShotOverlayImage = overlayObject.GetComponent<Image>();
        if (establishingShotOverlayImage == null) establishingShotOverlayImage = overlayObject.AddComponent<Image>();
        establishingShotOverlayImage.sprite = establishingShotSprite;
        establishingShotOverlayImage.preserveAspect = false; // fill the screen edge to edge
        establishingShotOverlayImage.raycastTarget = false;

        Color startColor = Color.white;
        startColor.a = 0f;
        establishingShotOverlayImage.color = startColor;
    }

    // A small dialogue box over the establishing shot, showing a single
    // static line — same background sprite (and 9-sliced border) as the
    // main gameplay dialogue box, at a matching bottom-anchored position,
    // for visual consistency between the two. Built once, kept fully
    // transparent via a CanvasGroup until PlayEstablishingShotThenLoadGameplay
    // fades it in/out.
    private void CreateEstablishingShotDialogueBox(RectTransform parent)
    {
        GameObject boxObject = FindOrCreateChild(parent, "EstablishingShotDialogueBox", out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = boxObject.GetComponent<RectTransform>();
            // Bottom-anchored, full width minus side margins — the same
            // convention the gameplay dialogue box's own panel uses.
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 43f);
            rect.sizeDelta = new Vector2(-400f, 220f);
        }

        boxObject.transform.SetAsLastSibling();

        Image panelImage = boxObject.GetComponent<Image>();
        if (panelImage == null) panelImage = boxObject.AddComponent<Image>();
        panelImage.sprite = dialogueBoxBackgroundSprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.raycastTarget = false;

        CanvasGroup group = boxObject.GetComponent<CanvasGroup>();
        if (group == null) group = boxObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        establishingShotDialogueBoxGroup = group;

        GameObject textObject = FindOrCreateChild(boxObject.transform, "Text", out bool textWasCreated);

        if (textWasCreated)
        {
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(50f, 30f);
            textRect.offsetMax = new Vector2(-50f, -30f);
        }

        establishingShotDialogueText = textObject.GetComponent<TextMeshProUGUI>();
        if (establishingShotDialogueText == null) establishingShotDialogueText = textObject.AddComponent<TextMeshProUGUI>();
        establishingShotDialogueText.text = establishingShotLine;
        establishingShotDialogueText.fontSize = 40;
        establishingShotDialogueText.alignment = TextAlignmentOptions.Center;
        establishingShotDialogueText.color = Color.black;
        establishingShotDialogueText.raycastTarget = false;
        if (gameFont != null) establishingShotDialogueText.font = gameFont;
    }


    // Anchors, sizes and positions a character portrait at a screen
    // corner the first time it's created, then starts (or restarts) it
    // cycling through 'sprites' forever with a pop transition between
    // each one. Skips gracefully if the list is empty.
    private void CreateCyclingCharacterImage(RectTransform parent, List<Sprite> sprites, string name, Vector2 anchor, Vector2 size, Vector2 offset)
    {
        if (sprites == null || sprites.Count == 0) return;

        GameObject imageObject = FindOrCreateChild(parent, name, out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        Image image = imageObject.GetComponent<Image>();
        if (image == null) image = imageObject.AddComponent<Image>();
        if (image.sprite == null) image.sprite = sprites[0];
        image.preserveAspect = true;
        image.raycastTarget = false;

        // Coroutines only actually run in Play mode — harmless to call
        // this here in Edit mode too (via "Build In Editor"), it just
        // sits inert until Play starts.
        if (portraitCycleCoroutines.TryGetValue(imageObject, out Coroutine running) && running != null)
        {
            StopCoroutine(running);
        }
        portraitCycleCoroutines[imageObject] = StartCoroutine(PortraitCycleRoutine(imageObject, sprites));
    }

    // Repeats forever: wait portraitCycleInterval, then pop-swap to the
    // next sprite in the list (wrapping back to the start after the
    // last), looping through the whole list indefinitely.
    private IEnumerator PortraitCycleRoutine(GameObject imageObject, List<Sprite> sprites)
    {
        Image image = imageObject.GetComponent<Image>();
        RectTransform rect = imageObject.GetComponent<RectTransform>();

        // Start from wherever the currently-shown sprite already is in
        // the list (e.g. after a rebuild) rather than always restarting
        // from the first entry.
        int index = Mathf.Max(0, sprites.IndexOf(image.sprite));

        while (true)
        {
            yield return new WaitForSeconds(portraitCycleInterval);

            index = (index + 1) % sprites.Count;
            yield return PopSwapSprite(image, rect, sprites[index]);
        }
    }

    // Same shrink/swap/grow "ease pop" recipe as PortraitManager's
    // in-game expression swaps — shrinks to portraitPopScale, swaps the
    // sprite at the bottom of the dip (when it's smallest and the cut
    // is least noticeable), then eases back to full size.
    private IEnumerator PopSwapSprite(Image image, RectTransform rect, Sprite newSprite)
    {
        float halfDuration = portraitPopDuration / 2f;
        bool swapped = false;
        float elapsed = 0f;

        while (elapsed < portraitPopDuration)
        {
            elapsed += Time.deltaTime;

            if (!swapped && elapsed >= halfDuration)
            {
                image.sprite = newSprite;
                swapped = true;
            }

            float scale;
            if (elapsed < halfDuration)
            {
                float t = Mathf.Clamp01(elapsed / halfDuration);
                scale = Mathf.Lerp(1f, portraitPopScale, t);
            }
            else
            {
                float t = Mathf.Clamp01((elapsed - halfDuration) / halfDuration);
                scale = Mathf.Lerp(portraitPopScale, 1f, t);
            }

            rect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        if (!swapped) image.sprite = newSprite;
        rect.localScale = Vector3.one;
    }

    private void CreateTitle(RectTransform parent)
    {
        GameObject titleObject = FindOrCreateChild(parent, "TitleText", out bool wasCreated);

        if (wasCreated)
        {
            RectTransform rect = titleObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(1400f, 300f);
            rect.anchoredPosition = new Vector2(0f, -120f);
        }

        TextMeshProUGUI text = titleObject.GetComponent<TextMeshProUGUI>();
        if (text == null) text = titleObject.AddComponent<TextMeshProUGUI>();

        text.text = gameTitle;
        text.fontSize = titleFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = titleColor;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        if (gameFont != null) text.font = gameFont;
    }

    private void CreatePlayButton(RectTransform parent)
    {
        GameObject buttonObject = FindOrCreateChild(parent, "PlayButton", out bool wasCreated);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();

        if (wasCreated)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(320f, 100f);
            rect.anchoredPosition = new Vector2(0f, 90f);
        }

        Image buttonImage = buttonObject.GetComponent<Image>();
        if (buttonImage == null) buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = playButtonColor;

        Button button = buttonObject.GetComponent<Button>();
        if (button == null) button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.RemoveListener(OnPlayButtonClicked); // avoid double-binding if rebuilt
        button.onClick.AddListener(OnPlayButtonClicked);

        GameObject labelObject = FindOrCreateChild(rect, "Label", out bool labelWasCreated);

        if (labelWasCreated)
        {
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            labelRect.anchoredPosition = Vector2.zero;
        }

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        if (label == null) label = labelObject.AddComponent<TextMeshProUGUI>();

        label.text = playButtonLabel;
        label.fontSize = 44;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        if (gameFont != null) label.font = gameFont;
    }

    private void OnPlayButtonClicked()
    {
        // Guards against a second click starting a second overlapping
        // transition (and a second eventual LoadScene call) while the
        // establishing shot is already playing.
        if (isTransitioningToGameplay) return;
        isTransitioningToGameplay = true;

        if (establishingShotOverlayImage == null)
        {
            // No establishing shot configured (or its sprite was never
            // assigned) — fall back to the original instant cut rather
            // than silently doing nothing.
            SceneManager.LoadScene(gameplaySceneName);
            return;
        }

        StartCoroutine(PlayEstablishingShotThenLoadGameplay());
    }

    // Fades the splash screen to black, fades the establishing shot in
    // from that black, holds on it, fades it back to black, then loads
    // the gameplay scene. Fading to black (both at the start AND before
    // the scene load) rather than crossfading directly between elements
    // matters here: the gameplay scene starts already fully lit (night
    // background at full alpha, no fade-in of its own beyond its own
    // matching black-cover fade), so cutting from a black screen into it
    // reads as a clean transition, while a direct crossfade would pop.
    private IEnumerator PlayEstablishingShotThenLoadGameplay()
    {
        // Kicked off immediately and left to load in the background for
        // the whole rest of this coroutine. A plain synchronous
        // LoadScene() call blocks the main thread while it instantiates
        // every GameObject in the gameplay scene, which is exactly what
        // produced a visible skip/hitch right as the screen went black —
        // nothing could render for however long that instantiation
        // took. Loading async and holding activation back with
        // allowSceneActivation lets all of that heavy lifting happen
        // underneath the still-visible establishing shot instead.
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(gameplaySceneName);
        asyncLoad.allowSceneActivation = false;

        // Splash music starts fading to silence now, in parallel with
        // the visual fade below — StopMusic() runs over MusicManager's
        // own fadeDuration, which the wait further down accounts for
        // separately from the visual timing here.
        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.StopMusic();
        }

        // Splash content fades to solid black first — splashBlackOverlay
        // sits directly behind establishingShotOverlayImage, so once
        // it's fully opaque the establishing shot image can fade in
        // from true black next, rather than crossfading over the splash
        // screen's own art/title/button.
        yield return StartCoroutine(FadeImageAlpha(splashBlackOverlay, 0f, 1f, splashFadeToBlackDuration));

        if (MusicManager.Instance != null)
        {
            // The splash track needs to be COMPLETELY silent before the
            // establishing shot's music starts (rather than the two
            // overlapping in a crossfade, which is what calling PlayClip
            // while the old track is still audible would produce) — if
            // MusicManager's own fadeDuration is longer than the visual
            // fade just finished, wait out whatever's left of it.
            float remainingMusicFade = MusicManager.Instance.fadeDuration - splashFadeToBlackDuration;
            if (remainingMusicFade > 0f) yield return new WaitForSeconds(remainingMusicFade);

            MusicManager.Instance.PlayClip(establishingShotMusicClip);
        }

        yield return StartCoroutine(FadeImageAlpha(establishingShotOverlayImage, 0f, 1f, establishingShotFadeInDuration));

        if (establishingShotDialogueBoxGroup != null)
        {
            StartCoroutine(FadeCanvasGroupAlpha(establishingShotDialogueBoxGroup, 0f, 1f, establishingShotTextFadeInDuration));
        }

        yield return new WaitForSeconds(establishingShotHoldDuration);

        if (establishingShotDialogueBoxGroup != null)
        {
            StartCoroutine(FadeCanvasGroupAlpha(establishingShotDialogueBoxGroup, establishingShotDialogueBoxGroup.alpha, 0f, establishingShotFadeToBlackDuration));
        }

        // Fades the establishing shot image back out, revealing the
        // still-opaque splashBlackOverlay sitting behind it — solid
        // black either way, so no separate tint-to-black step is needed.
        yield return StartCoroutine(FadeImageAlpha(establishingShotOverlayImage, 1f, 0f, establishingShotFadeToBlackDuration));

        // Screen now reads fully black via splashBlackOverlay — but that
        // Image belongs to THIS scene and is about to be destroyed. Hand
        // the "stay black" duty off to a persistent overlay that
        // survives the load, so there's no gap where the outgoing
        // scene's black cover is gone but the incoming scene's own
        // black cover hasn't taken effect yet. Snapping it opaque here
        // is invisible — the screen is already black at this point.
        SceneTransitionOverlay.GetOrCreate().SetOpaque();

        // The load itself should be long done by now (it only needed a
        // couple of frames), but wait for it just in case before handing
        // off — activation itself is then effectively instant since
        // there's no more work left to do underneath it.
        while (asyncLoad.progress < 0.9f) yield return null;
        asyncLoad.allowSceneActivation = true;
    }

    private IEnumerator FadeCanvasGroupAlpha(CanvasGroup group, float fromAlpha, float toAlpha, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        group.alpha = toAlpha;
    }

    private IEnumerator FadeImageAlpha(Image image, float fromAlpha, float toAlpha, float duration)
    {
        Color c = image.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / duration));
            image.color = c;
            yield return null;
        }

        c.a = toAlpha;
        image.color = c;
    }

    // ── Sky ambience: god rays (via DayAtmosphereEffect prefab) ──────────

    // Finds (or instantiates) the DayAtmosphereEffect prefab as a child
    // of the splash canvas. This component no longer builds or animates
    // any rays itself — the prefab instance manages its own entirely
    // (see DayAtmosphereEffect.cs), so editing the prefab asset (or the
    // GodRaySettings it references) updates every scene that uses it,
    // including BackgroundManager's gameplay day background.
    private void BuildDayAtmosphere(RectTransform parent)
    {
        if (dayAtmosphereEffectPrefab == null) return;

        Transform existing = parent.Find("DayAtmosphereEffect");
        GameObject instance;

        if (existing != null)
        {
            instance = existing.gameObject;
        }
        else
        {
            instance = Instantiate(dayAtmosphereEffectPrefab, parent);
            instance.name = "DayAtmosphereEffect"; // strip the "(Clone)" suffix so Find() above matches on future calls
        }

        // Enforced every time, not just on creation — an instance found
        // already sitting in the scene (e.g. built before the title/
        // portraits/button existed, or before this ordering was in
        // place) can otherwise be left wherever it happened to land,
        // which is what let the god rays render on top of the title
        // instead of behind it. Sitting right after Background keeps it
        // above the art but below every other UI element.
        Transform background = parent.Find("Background");
        int targetIndex = background != null ? background.GetSiblingIndex() + 1 : 0;
        instance.transform.SetSiblingIndex(targetIndex);
    }
}
