using System.Collections;
using System.Collections.Generic;
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

    [System.Serializable]
    public class GodRayConfig
    {
        // Anchored to the top-center of the background rect (anchorY=1)
        // — this is a horizontal offset plus a slight vertical nudge
        // from the top edge, not a free-floating anchor like the torch
        // glows, since a god ray only makes sense hanging from the sky.
        public Vector2 anchoredPosition;
        public float width = 260f;
        public float height = 1000f;
        // A little tilt reads as raking sunlight rather than a perfectly
        // vertical, mechanical-looking shaft.
        public float rotationDegrees = 0f;
        public Color color = new Color(1f, 0.95f, 0.75f, 1f); // warm sunlight
        [Range(0f, 1f)] public float baseAlpha = 0.35f;
    }

    [Header("Day Ambience — God Rays")]
    // Empty by default — add one entry per shaft of light you want
    // (e.g. one per skylight opening visible in the day sprite) and
    // position/rotate it in the Inspector or by dragging in the Scene
    // view. Each one breathes independently, same reasoning as the
    // torch glows above.
    public bool enableGodRays = true;
    public List<GodRayConfig> godRays = new List<GodRayConfig>();
    public float godRaySwaySpeed = 0.4f;
    [Range(0f, 1f)] public float godRaySwayAmount = 0.2f;

    private GameObject dayAmbienceContainer;
    private List<Image> godRayImages;
    private List<float> godRaySwaySeeds;

    private static Sprite cachedGodRaySprite;
    private const int GodRayTextureWidth = 128;
    private const int GodRayTextureHeight = 256;

    private bool isFading = false;

    // The scene STARTS AT NIGHT — see Start() below.
    private bool isDay = false;

    void Start()
    {
        // Initialise both layers to the NIGHTTIME background,
        // matching how the game actually begins
        bottomLayer.sprite = nightSprite;
        topLayer.sprite = nightSprite;

        SetAlpha(bottomLayer, 1f);
        SetAlpha(topLayer, 0f);

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
    }

    // ── Opening day reveal (cutscene helpers) ───────────────────────────────
    //
    // Used by OpeningDayRevealSequence for the one-off opening transition,
    // where the screen fades to black BEHIND a portrait (which stays
    // visible, since topLayer already renders behind the portraits
    // canvas), rather than straight-crossfading to the day sprite.

    // Fades topLayer to solid opaque black, covering bottomLayer (and
    // anything else drawn behind the portraits) without touching
    // whichever portrait is currently on top of it.
    public IEnumerator FadeToBlackOverlay(float duration)
    {
        isFading = true;

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

        if (wordsearchPanel != null)
        {
            wordsearchPanel.SetActive(true);
        }

        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(true);
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

        if (dayAmbienceContainer != null && dayAmbienceContainer.activeSelf)
        {
            AnimateGodRaySway();
        }
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

    // ── Day ambience: god rays ───────────────────────────────────────────────

    // Lets you build (and re-sync) the god ray objects without pressing
    // Play — same reasoning and same find-or-create-by-name persistence
    // as "Build Night Ambience In Editor" above. Also switches the
    // background layers to the day sprite so the temple art is actually
    // visible in the Scene view while you position the rays.
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

        SeedExampleGodRaysIfEmpty();
        BuildDayAmbience();

        if (dayAmbienceContainer != null) dayAmbienceContainer.SetActive(true);
    }

    // First-time convenience only: if you haven't configured any god
    // rays yet, seeds three large, brightly-lit placeholders spread
    // across the top of the screen with a bit of varied tilt, so
    // there's something obvious to find and drag under an actual
    // skylight/window in the art, then dial down afterward.
    private void SeedExampleGodRaysIfEmpty()
    {
        if (godRays != null && godRays.Count > 0) return;

        Color obviousSunlight = new Color(1f, 0.95f, 0.7f, 1f);
        godRays = new List<GodRayConfig>
        {
            new GodRayConfig { anchoredPosition = new Vector2(-500f, 0f), width = 300f, height = 1000f, rotationDegrees = -12f, baseAlpha = 0.65f, color = obviousSunlight },
            new GodRayConfig { anchoredPosition = new Vector2(0f, 0f),    width = 300f, height = 1000f, rotationDegrees = 0f,   baseAlpha = 0.65f, color = obviousSunlight },
            new GodRayConfig { anchoredPosition = new Vector2(500f, 0f),  width = 300f, height = 1000f, rotationDegrees = 12f,  baseAlpha = 0.65f, color = obviousSunlight },
        };
    }

    // Finds (or creates) a container sitting just above the background
    // layers, then re-syncs its god ray children against the current
    // configuration — same persistence behavior as BuildNightAmbience:
    // existing children are matched by name and never have their
    // position/rotation reset, so anything dragged/rotated in the Scene
    // view stays put across rebuilds and Play/Stop cycles.
    private void BuildDayAmbience()
    {
        Transform backgroundParent = topLayer.transform.parent;
        Transform existingContainer = backgroundParent.Find("DayAmbience");

        if (existingContainer != null)
        {
            dayAmbienceContainer = existingContainer.gameObject;
        }
        else
        {
            dayAmbienceContainer = new GameObject("DayAmbience", typeof(RectTransform));
            RectTransform newContainerRect = dayAmbienceContainer.GetComponent<RectTransform>();
            newContainerRect.SetParent(backgroundParent, false);
            newContainerRect.anchorMin = Vector2.zero;
            newContainerRect.anchorMax = Vector2.one;
            newContainerRect.offsetMin = Vector2.zero;
            newContainerRect.offsetMax = Vector2.zero;

            newContainerRect.SetSiblingIndex(topLayer.transform.GetSiblingIndex() + 1);
        }

        RectTransform containerRect = dayAmbienceContainer.GetComponent<RectTransform>();

        if (enableGodRays)
        {
            BuildGodRays(containerRect);
        }
        else
        {
            RemoveExtraChildren(containerRect, "GodRay ", 0);
        }
    }

    private void BuildGodRays(RectTransform container)
    {
        godRayImages = new List<Image>();
        godRaySwaySeeds = new List<float>();

        for (int i = 0; i < godRays.Count; i++)
        {
            GodRayConfig config = godRays[i];

            GameObject rayObject = FindOrCreateAmbienceChild(container, "GodRay " + i, out bool wasCreated);
            RectTransform rayRect = rayObject.GetComponent<RectTransform>();

            if (wasCreated)
            {
                // Hangs from the top edge — pivot and anchor both sit at
                // top-center, so the ray extends downward from there.
                rayRect.anchorMin = new Vector2(0.5f, 1f);
                rayRect.anchorMax = new Vector2(0.5f, 1f);
                rayRect.pivot = new Vector2(0.5f, 1f);
                // Starting point only — drag/rotate it into place
                // afterward; later rebuilds won't reset it.
                rayRect.anchoredPosition = config.anchoredPosition;
                rayRect.localRotation = Quaternion.Euler(0f, 0f, config.rotationDegrees);
            }

            rayRect.sizeDelta = new Vector2(config.width, config.height);

            Image rayImage = rayObject.GetComponent<Image>();
            if (rayImage == null) rayImage = rayObject.AddComponent<Image>();
            rayImage.sprite = GetGodRaySprite();
            rayImage.raycastTarget = false;
            rayImage.color = new Color(config.color.r, config.color.g, config.color.b, config.baseAlpha);

            godRayImages.Add(rayImage);

            // Distinct random phase per ray so they breathe out of sync
            // with each other, same reasoning as the torch flicker.
            godRaySwaySeeds.Add(Random.Range(0f, 1000f));
        }

        RemoveExtraChildren(container, "GodRay ", godRays.Count);
    }

    private void AnimateGodRaySway()
    {
        if (godRayImages == null) return;

        for (int i = 0; i < godRayImages.Count; i++)
        {
            GodRayConfig config = godRays[i];
            float noise = Mathf.PerlinNoise(godRaySwaySeeds[i], Time.time * godRaySwaySpeed);
            float alpha = config.baseAlpha + (noise - 0.5f) * 2f * godRaySwayAmount;

            Image image = godRayImages[i];
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }
    }

    // A soft, tapered light-shaft gradient: narrow and fading in near
    // the top (the "source"), widening further down, with soft left/
    // right edges throughout and a gentle fade-out toward the bottom so
    // it dissolves rather than cutting off hard. Shared by every god ray
    // (color/size/alpha applied via the Image), generated once and
    // cached like this project's other procedural VFX.
    private Sprite GetGodRaySprite()
    {
        if (cachedGodRaySprite != null) return cachedGodRaySprite;

        Texture2D texture = new Texture2D(GodRayTextureWidth, GodRayTextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[GodRayTextureWidth * GodRayTextureHeight];
        float centerX = GodRayTextureWidth / 2f;

        for (int y = 0; y < GodRayTextureHeight; y++)
        {
            // 0 at the top (the light source), 1 at the bottom.
            float normalizedY = y / (float)GodRayTextureHeight;

            // The beam's half-width grows from narrow near the source to
            // nearly the full texture width by the bottom, like light
            // spreading out from a point.
            float halfWidthFraction = Mathf.Lerp(0.08f, 0.9f, normalizedY);

            // Fades in quickly right at the top, and dissolves gradually
            // over the bottom ~40% instead of ending in a hard edge.
            float verticalAlpha = 1f;
            if (normalizedY < 0.05f)
            {
                verticalAlpha = normalizedY / 0.05f;
            }
            else if (normalizedY > 0.6f)
            {
                verticalAlpha = 1f - Mathf.Clamp01((normalizedY - 0.6f) / 0.4f);
            }

            for (int x = 0; x < GodRayTextureWidth; x++)
            {
                float normalizedX = Mathf.Abs(x - centerX) / centerX; // 0 at center, 1 at edge
                float distanceRatio = halfWidthFraction > 0f ? normalizedX / halfWidthFraction : 1f;

                const float edgeSoftness = 0.35f;
                float horizontalAlpha = 1f - Mathf.SmoothStep(1f - edgeSoftness, 1f, distanceRatio);

                float alpha = Mathf.Clamp01(horizontalAlpha) * verticalAlpha;
                pixels[y * GodRayTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        // Pivot at top-center, matching how each ray's RectTransform is
        // anchored/pivoted in BuildGodRays.
        cachedGodRaySprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, GodRayTextureWidth, GodRayTextureHeight),
            new Vector2(0.5f, 1f));

        return cachedGodRaySprite;
    }
}
