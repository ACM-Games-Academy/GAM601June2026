using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// DayAtmosphereEffect
//
// Self-contained "daytime background atmosphere" effect — god rays,
// water sparkles, and leaping goldfish so far, with more effects meant
// to join it here over time. Lives as a PREFAB
// (Assets/Prefab/DayAtmosphereEffect.prefab) rather than being built
// from scratch by each scene's controller script, so editing the prefab
// (position the GodRay/Sparkle/Fish children, tweak this component)
// updates every scene that uses it — currently both the gameplay day
// background (via BackgroundManager) and the splash screen (via
// SplashScreenController).
//
// Both of those scripts just Instantiate() this prefab as a child of
// their own background layer and otherwise leave it alone — this
// component builds and animates its own children independently.
//
// SETUP:
// 1. This prefab already has its GodRay/Sparkle/Fish children built in,
//    using whatever GodRaySettings asset and Water Sparkles/Goldfish
//    lists are assigned/configured below at the time it was last built.
//    To rebuild after changing those, either enter Prefab Mode and use
//    the ⋮ menu's "Build In Editor", or just press Play — Awake() does
//    the same build automatically.
// 2. Position the children directly in Prefab Mode if you want to move
//    them (fish at their RESTING position, between leaps) — like the
//    original per-scene system, rebuilding only resets a child's
//    position/rotation the first time it's created, never after.

[RequireComponent(typeof(RectTransform))]
public class DayAtmosphereEffect : MonoBehaviour
{
    // ── God rays ─────────────────────────────────────────────────────────

    public GodRaySettings godRaySettings;

    private List<Image> godRayImages;
    private List<float> godRaySwaySeeds;
    private List<Coroutine> godRayCycleCoroutines;

    private static Sprite cachedGodRaySprite;
    private const int GodRayTextureWidth = 128;
    private const int GodRayTextureHeight = 256;

    // ── Water sparkles ───────────────────────────────────────────────────

    [System.Serializable]
    public class WaterSparkleConfig
    {
        // Anchored to bottom-center (anchorY=0) — a horizontal offset
        // plus a small vertical nudge to land on the pool of water at
        // the bottom of the screen.
        public Vector2 anchoredPosition;
        public float size = 28f;
        public Color color = new Color(1f, 1f, 0.95f, 1f); // bright, near-white glint
        [Range(0f, 1f)] public float baseAlpha = 0.85f;
    }

    [Header("Water Sparkles")]
    public bool enableWaterSparkles = true;
    // Empty by default — add one entry per glint you want scattered
    // across the pool and position it in the Inspector or by dragging
    // in the Scene view. Each one twinkles independently, same
    // "never synchronized" reasoning as the god rays' breathing.
    public List<WaterSparkleConfig> waterSparkles = new List<WaterSparkleConfig>();
    public float sparkleTwinkleSpeed = 3f;
    [Range(0f, 1f)] public float sparkleTwinkleAmount = 0.15f;

    [Header("Water Sparkle Appear/Disappear Cycle")]
    // Same on/off cycle shape as the god rays (fade in, hold, fade out,
    // vanish, repeat) but much snappier by default — a sparkle is meant
    // to read as a quick glinting twinkle, not a slow-breathing shaft of
    // light. Starts are staggered the same way, spread evenly across one
    // full cycle length based on each sparkle's position in the list.
    public float sparkleVisibleDuration = 0.8f;
    public float sparkleHiddenDuration = 1.6f;
    public float sparkleFadeTransitionDuration = 0.25f;

    private List<Image> sparkleImages;
    private List<float> sparkleTwinkleSeeds;
    private List<Coroutine> sparkleCycleCoroutines;

    private static Sprite cachedSparkleSprite;
    private const int SparkleTextureSize = 64;

    // ── Goldfish ─────────────────────────────────────────────────────────

    [System.Serializable]
    public class FishConfig
    {
        // Anchored to bottom-center (anchorY=0), same convention as the
        // water sparkles — this is where the fish breaks the surface
        // and dives back in, not its position mid-leap.
        public Vector2 anchoredPosition;
        public float size = 60f;
        public Color color = new Color(1f, 0.6f, 0.15f, 1f); // goldfish orange
        public float leapHeight = 110f;
        // Horizontal drift over the course of one leap — a straight-up
        // hop reads as mechanical, a bit of travel reads as a real jump.
        public float leapDistance = 70f;
        // Which way it faces/travels — flip per fish for variety rather
        // than having every one leap the same direction.
        public bool leapRight = true;
    }

    [Header("Goldfish")]
    public bool enableGoldfish = true;
    // Empty by default — add one entry per leap point you want on the
    // pool and position it in the Inspector or by dragging in the Scene
    // view (drag it at its RESTING position; it only exists at that
    // point between leaps).
    public List<FishConfig> goldfish = new List<FishConfig>();
    public float maxLeapTiltDegrees = 25f;

    [Header("Goldfish Leap Cycle")]
    // Unlike the god rays/sparkles, a fish's "visible phase" IS the leap
    // itself — there's no separate hold; it rises, arcs, and dives back
    // in over leapDuration, then waits leapHiddenDuration completely
    // submerged before leaping again. Staggered the same way as the
    // other two effects.
    public float leapDuration = 0.9f;
    public float leapHiddenDuration = 8f;

    private List<Image> fishImages;
    private List<RectTransform> fishRects;
    private List<Coroutine> fishCycleCoroutines;

    private static Sprite cachedFishSprite;
    private const int FishTextureWidth = 96;
    private const int FishTextureHeight = 48;

    void Awake()
    {
        BuildGodRays();
        BuildWaterSparkles();
        BuildGoldfish();
    }

    // Awake() only ever fires once per instance, but this prefab gets
    // SetActive(false)/(true) toggled repeatedly by BackgroundManager
    // (day/night switching) — and Unity kills coroutines outright when a
    // GameObject is deactivated, without auto-resuming them on
    // reactivation. Starting/stopping the cycles from OnEnable/OnDisable
    // instead of Awake is what makes them survive that toggling; this is
    // exactly the bug that made the effects work on the splash screen
    // (which never deactivates this prefab) but go static after the
    // first day/night switch in the gameplay scene.
    void OnEnable()
    {
        StartGodRayCycles();
        StartSparkleCycles();
        StartFishCycles();
    }

    void OnDisable()
    {
        StopCycles(ref godRayCycleCoroutines);
        StopCycles(ref sparkleCycleCoroutines);
        StopCycles(ref fishCycleCoroutines);
    }

    // Lets you (re)build without pressing Play — e.g. right after
    // editing the assigned GodRaySettings asset, Water Sparkles list, or
    // Goldfish list, or while positioning children in Prefab Mode.
    // Available from the ⋮ menu on this component.
    [ContextMenu("Build In Editor")]
    private void BuildInEditor()
    {
        BuildGodRays();
        BuildWaterSparkles();
        BuildGoldfish();

        // If invoked while already playing, OnEnable already ran once
        // and won't fire again just because we rebuilt — restart the
        // cycles explicitly so a manual rebuild mid-Play doesn't leave
        // everything static until the next activation.
        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartGodRayCycles();
            StartSparkleCycles();
            StartFishCycles();
        }
    }

    // ── Building: god rays ───────────────────────────────────────────────

    private void BuildGodRays()
    {
        // Stop any previously running cycle coroutines before rebuilding
        // — each one closes over a list index into godRayImages/
        // godRaySettings.godRays, which would otherwise go stale (or out
        // of range) against the fresh lists built below.
        StopCycles(ref godRayCycleCoroutines);

        if (godRaySettings == null)
        {
            Debug.LogWarning("DayAtmosphereEffect: God Ray Settings isn't assigned — nothing to build. " +
                              "Create/assign a GodRaySettings asset (Assets > Create > BAST > God Ray Settings).");
            return;
        }

        if (!godRaySettings.enableGodRays)
        {
            RemoveExtraChildren("GodRay ", 0);
            godRayImages = null;
            return;
        }

        SeedExampleGodRaysIfEmpty();

        RectTransform container = (RectTransform)transform;
        godRayImages = new List<Image>();
        godRaySwaySeeds = new List<float>();

        for (int i = 0; i < godRaySettings.godRays.Count; i++)
        {
            GodRaySettings.GodRayConfig config = godRaySettings.godRays[i];

            GameObject rayObject = FindOrCreateChild(container, "GodRay " + i, out bool wasCreated);
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
            // Starts at its resting baseAlpha (rather than fully hidden)
            // so it's actually visible while positioning in the Editor —
            // coroutines don't tick outside Play mode, so a hidden start
            // would otherwise never fade in until you press Play.
            rayImage.color = new Color(config.color.r, config.color.g, config.color.b, config.baseAlpha);

            godRayImages.Add(rayImage);

            // Distinct random phase per ray so they breathe out of sync
            // with each other during their visible phase.
            godRaySwaySeeds.Add(Random.Range(0f, 1000f));
        }

        RemoveExtraChildren("GodRay ", godRaySettings.godRays.Count);
    }

    // Starts (or restarts) the god ray cycle coroutines against whatever
    // godRayImages currently holds. Separated from BuildGodRays so
    // OnEnable can call this alone — rebuilding the children isn't
    // needed on every reactivation, just restarting their animation.
    private void StartGodRayCycles()
    {
        if (godRaySettings == null || !godRaySettings.enableGodRays) return;
        if (godRayImages == null) return;

        StopCycles(ref godRayCycleCoroutines);

        // Coroutines only actually run in Play mode — harmless to call
        // here in Edit mode too, they just sit inert until Play starts.
        godRayCycleCoroutines = new List<Coroutine>();
        for (int i = 0; i < godRaySettings.godRays.Count; i++)
        {
            godRayCycleCoroutines.Add(StartCoroutine(GodRayCycleRoutine(i)));
        }
    }

    // First-time convenience only: if the assigned GodRaySettings asset
    // has no rays configured yet, seeds three large, brightly-lit
    // placeholders spread across the top of the screen with a bit of
    // varied tilt, so there's something obvious to find and drag under
    // an actual skylight/window in the art, then dial down afterward.
    private void SeedExampleGodRaysIfEmpty()
    {
        if (godRaySettings.godRays != null && godRaySettings.godRays.Count > 0) return;

        Color obviousSunlight = new Color(1f, 0.95f, 0.7f, 1f);
        godRaySettings.godRays = new List<GodRaySettings.GodRayConfig>
        {
            new GodRaySettings.GodRayConfig { anchoredPosition = new Vector2(-500f, 0f), width = 300f, height = 1000f, rotationDegrees = -12f, baseAlpha = 0.65f, color = obviousSunlight },
            new GodRaySettings.GodRayConfig { anchoredPosition = new Vector2(0f, 0f),    width = 300f, height = 1000f, rotationDegrees = 0f,   baseAlpha = 0.65f, color = obviousSunlight },
            new GodRaySettings.GodRayConfig { anchoredPosition = new Vector2(500f, 0f),  width = 300f, height = 1000f, rotationDegrees = 12f,  baseAlpha = 0.65f, color = obviousSunlight },
        };
    }

    // ── Building: water sparkles ─────────────────────────────────────────

    private void BuildWaterSparkles()
    {
        // Same reasoning as BuildGodRays — stop old coroutines before
        // the lists they index into get rebuilt.
        StopCycles(ref sparkleCycleCoroutines);

        if (!enableWaterSparkles)
        {
            RemoveExtraChildren("Sparkle ", 0);
            sparkleImages = null;
            return;
        }

        SeedExampleWaterSparklesIfEmpty();

        RectTransform container = (RectTransform)transform;
        sparkleImages = new List<Image>();
        sparkleTwinkleSeeds = new List<float>();

        for (int i = 0; i < waterSparkles.Count; i++)
        {
            WaterSparkleConfig config = waterSparkles[i];

            GameObject sparkleObject = FindOrCreateChild(container, "Sparkle " + i, out bool wasCreated);
            RectTransform sparkleRect = sparkleObject.GetComponent<RectTransform>();

            if (wasCreated)
            {
                // Sits on the pool of water at the bottom of the screen
                // — anchored to bottom-center so a small Y offset lands
                // right on the water's surface.
                sparkleRect.anchorMin = new Vector2(0.5f, 0f);
                sparkleRect.anchorMax = new Vector2(0.5f, 0f);
                sparkleRect.pivot = new Vector2(0.5f, 0.5f);
                // Starting point only — drag it into place afterward;
                // later rebuilds won't reset it.
                sparkleRect.anchoredPosition = config.anchoredPosition;
            }

            sparkleRect.sizeDelta = new Vector2(config.size, config.size);

            Image sparkleImage = sparkleObject.GetComponent<Image>();
            if (sparkleImage == null) sparkleImage = sparkleObject.AddComponent<Image>();
            sparkleImage.sprite = GetSparkleSprite();
            sparkleImage.raycastTarget = false;
            // Starts at its resting baseAlpha (rather than fully hidden)
            // so it's actually visible while positioning in the Editor —
            // see the matching comment in BuildGodRays for why.
            sparkleImage.color = new Color(config.color.r, config.color.g, config.color.b, config.baseAlpha);

            sparkleImages.Add(sparkleImage);

            // Distinct random phase per sparkle so they twinkle out of
            // sync with each other during their visible phase.
            sparkleTwinkleSeeds.Add(Random.Range(0f, 1000f));
        }

        RemoveExtraChildren("Sparkle ", waterSparkles.Count);
    }

    // Starts (or restarts) the sparkle cycle coroutines against whatever
    // sparkleImages currently holds — same reasoning as
    // StartGodRayCycles.
    private void StartSparkleCycles()
    {
        if (!enableWaterSparkles) return;
        if (sparkleImages == null) return;

        StopCycles(ref sparkleCycleCoroutines);

        sparkleCycleCoroutines = new List<Coroutine>();
        for (int i = 0; i < waterSparkles.Count; i++)
        {
            sparkleCycleCoroutines.Add(StartCoroutine(SparkleCycleRoutine(i)));
        }
    }

    // First-time convenience only: if you haven't configured any water
    // sparkles yet, seeds six large, brightly-lit placeholders spread
    // across the bottom of the screen, so there's something obvious to
    // find and drag onto the actual pool of water in the art, then dial
    // down afterward.
    private void SeedExampleWaterSparklesIfEmpty()
    {
        if (waterSparkles != null && waterSparkles.Count > 0) return;

        Color obviousWhite = new Color(1f, 1f, 0.95f, 1f);
        waterSparkles = new List<WaterSparkleConfig>
        {
            new WaterSparkleConfig { anchoredPosition = new Vector2(-400f, 60f), size = 32f, baseAlpha = 0.95f, color = obviousWhite },
            new WaterSparkleConfig { anchoredPosition = new Vector2(-240f, 90f), size = 32f, baseAlpha = 0.95f, color = obviousWhite },
            new WaterSparkleConfig { anchoredPosition = new Vector2(-80f, 50f),  size = 32f, baseAlpha = 0.95f, color = obviousWhite },
            new WaterSparkleConfig { anchoredPosition = new Vector2(80f, 80f),   size = 32f, baseAlpha = 0.95f, color = obviousWhite },
            new WaterSparkleConfig { anchoredPosition = new Vector2(240f, 55f),  size = 32f, baseAlpha = 0.95f, color = obviousWhite },
            new WaterSparkleConfig { anchoredPosition = new Vector2(400f, 85f),  size = 32f, baseAlpha = 0.95f, color = obviousWhite },
        };
    }

    // ── Shared helpers ───────────────────────────────────────────────────

    // Finds a direct child by name, or creates a fresh one if it doesn't
    // exist yet. wasCreated tells the caller whether to apply starting
    // position/size (only ever done once) or leave an existing
    // RectTransform exactly as it was left.
    private GameObject FindOrCreateChild(Transform parent, string name, out bool wasCreated)
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

    // Removes any leftover "<namePrefix>N" children whose index is no
    // longer within range — e.g. you deleted an entry from the list, or
    // disabled the effect entirely.
    private void RemoveExtraChildren(string namePrefix, int keepCount)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith(namePrefix)) continue;

            string suffix = child.name.Substring(namePrefix.Length);
            if (int.TryParse(suffix, out int index) && index >= keepCount)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }
    }

    private void StopCycles(ref List<Coroutine> coroutines)
    {
        if (coroutines == null) return;

        foreach (Coroutine coroutine in coroutines)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }

        coroutines = null;
    }

    // ── Animation: god rays ──────────────────────────────────────────────

    // Repeats forever: fade in, stay visible (breathing gently) for
    // godRayVisibleDuration, fade out, then disappear completely for
    // godRayHiddenDuration before fading back in. 'index' is fixed for
    // this coroutine's entire lifetime, matched against
    // godRaySettings.godRays/godRayImages as they stood when it was
    // started (rebuilds always stop these first — see StopCycles).
    private IEnumerator GodRayCycleRoutine(int index)
    {
        // Spread starts evenly across one full cycle length, based on
        // this ray's position in the list, so rays are never all
        // visible or all hidden at the same time.
        float cycleLength = godRaySettings.godRayFadeTransitionDuration * 2f + godRaySettings.godRayVisibleDuration + godRaySettings.godRayHiddenDuration;
        float startDelay = index * (cycleLength / Mathf.Max(godRaySettings.godRays.Count, 1));
        yield return new WaitForSeconds(startDelay);

        Image image = godRayImages[index];

        while (true)
        {
            yield return FadeImageAlpha(image, image.color.a, godRaySettings.godRays[index].baseAlpha, godRaySettings.godRayFadeTransitionDuration);

            float elapsed = 0f;
            while (elapsed < godRaySettings.godRayVisibleDuration)
            {
                elapsed += Time.deltaTime;

                GodRaySettings.GodRayConfig config = godRaySettings.godRays[index];
                float noise = Mathf.PerlinNoise(godRaySwaySeeds[index], Time.time * godRaySettings.godRaySwaySpeed);
                float sway = (noise - 0.5f) * 2f * godRaySettings.godRaySwayAmount;
                SetImageAlpha(image, Mathf.Clamp01(config.baseAlpha + sway));

                yield return null;
            }

            yield return FadeImageAlpha(image, image.color.a, 0f, godRaySettings.godRayFadeTransitionDuration);

            yield return new WaitForSeconds(godRaySettings.godRayHiddenDuration);
        }
    }

    // ── Animation: water sparkles ────────────────────────────────────────

    // Same shape as GodRayCycleRoutine — fade in, hold (twinkling
    // gently), fade out, vanish, repeat — just with its own (by default
    // much snappier) timing fields.
    private IEnumerator SparkleCycleRoutine(int index)
    {
        float cycleLength = sparkleFadeTransitionDuration * 2f + sparkleVisibleDuration + sparkleHiddenDuration;
        float startDelay = index * (cycleLength / Mathf.Max(waterSparkles.Count, 1));
        yield return new WaitForSeconds(startDelay);

        Image image = sparkleImages[index];

        while (true)
        {
            yield return FadeImageAlpha(image, image.color.a, waterSparkles[index].baseAlpha, sparkleFadeTransitionDuration);

            float elapsed = 0f;
            while (elapsed < sparkleVisibleDuration)
            {
                elapsed += Time.deltaTime;

                WaterSparkleConfig config = waterSparkles[index];
                float noise = Mathf.PerlinNoise(sparkleTwinkleSeeds[index], Time.time * sparkleTwinkleSpeed);
                float twinkle = (noise - 0.5f) * 2f * sparkleTwinkleAmount;
                SetImageAlpha(image, Mathf.Clamp01(config.baseAlpha + twinkle));

                yield return null;
            }

            yield return FadeImageAlpha(image, image.color.a, 0f, sparkleFadeTransitionDuration);

            yield return new WaitForSeconds(sparkleHiddenDuration);
        }
    }

    // ── Shared alpha animation ───────────────────────────────────────────

    private IEnumerator FadeImageAlpha(Image image, float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            SetImageAlpha(image, Mathf.Lerp(from, to, t));
            yield return null;
        }

        SetImageAlpha(image, to);
    }

    private void SetImageAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    // ── Textures ─────────────────────────────────────────────────────────

    // A soft, tapered light-shaft gradient: narrow and fading in near
    // the top (the "source"), widening further down, with soft left/
    // right edges throughout and a gentle fade-out toward the bottom so
    // it dissolves rather than cutting off hard. Shared by every god ray
    // (color/size/alpha applied via the Image), generated once and
    // cached.
    private Sprite GetGodRaySprite()
    {
        if (cachedGodRaySprite != null) return cachedGodRaySprite;

        Texture2D texture = new Texture2D(GodRayTextureWidth, GodRayTextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[GodRayTextureWidth * GodRayTextureHeight];
        float centerX = GodRayTextureWidth / 2f;

        for (int y = 0; y < GodRayTextureHeight; y++)
        {
            float normalizedY = y / (float)GodRayTextureHeight;
            float halfWidthFraction = Mathf.Lerp(0.08f, 0.9f, normalizedY);

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
                float normalizedX = Mathf.Abs(x - centerX) / centerX;
                float distanceRatio = halfWidthFraction > 0f ? normalizedX / halfWidthFraction : 1f;

                const float edgeSoftness = 0.35f;
                float horizontalAlpha = 1f - Mathf.SmoothStep(1f - edgeSoftness, 1f, distanceRatio);

                float alpha = Mathf.Clamp01(horizontalAlpha) * verticalAlpha;
                pixels[y * GodRayTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedGodRaySprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, GodRayTextureWidth, GodRayTextureHeight),
            new Vector2(0.5f, 1f));

        return cachedGodRaySprite;
    }

    // A four-pointed glint/asterisk shape — a small soft core with thin
    // rays extending horizontally and vertically, tapering toward their
    // tips — the classic "sunlight glinting off water" sparkle look,
    // rather than just another soft circular blob (which would read as
    // more torch-glow than sparkle). Generated once and cached.
    private Sprite GetSparkleSprite()
    {
        if (cachedSparkleSprite != null) return cachedSparkleSprite;

        Texture2D texture = new Texture2D(SparkleTextureSize, SparkleTextureSize, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[SparkleTextureSize * SparkleTextureSize];
        float center = SparkleTextureSize / 2f;

        const float coreRadius = 0.1f;     // fraction of the half-size — small, crisp core
        const float coreSoftness = 0.12f;
        const float rayThickness = 0.02f;  // thin rays
        const float rayThicknessSoftness = 0.05f;
        const float rayTaperStart = 0.05f; // rays stay full-bright very near the core, then taper

        for (int y = 0; y < SparkleTextureSize; y++)
        {
            float dy = (y + 0.5f - center) / center; // -1..1
            for (int x = 0; x < SparkleTextureSize; x++)
            {
                float dx = (x + 0.5f - center) / center; // -1..1
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float core = SoftEdge(dist, coreRadius, coreSoftness);

                float horizontalThickness = SoftEdge(Mathf.Abs(dy), rayThickness, rayThicknessSoftness);
                float horizontalLength = SoftEdge(Mathf.Abs(dx), rayTaperStart, 1f - rayTaperStart);
                float horizontalRay = horizontalThickness * horizontalLength;

                float verticalThickness = SoftEdge(Mathf.Abs(dx), rayThickness, rayThicknessSoftness);
                float verticalLength = SoftEdge(Mathf.Abs(dy), rayTaperStart, 1f - rayTaperStart);
                float verticalRay = verticalThickness * verticalLength;

                float alpha = Mathf.Clamp01(Mathf.Max(core, Mathf.Max(horizontalRay, verticalRay)));
                pixels[y * SparkleTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedSparkleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, SparkleTextureSize, SparkleTextureSize),
            new Vector2(0.5f, 0.5f));

        return cachedSparkleSprite;
    }

    // Unlike GLSL's smoothstep(edge0, edge1, x), Unity's
    // Mathf.SmoothStep(from, to, t) clamps t to 0-1 directly rather than
    // remapping it against an edge0/edge1 domain first — using it the
    // GLSL way is what once made the sparkle texture render as a solid
    // square. This does the remap explicitly instead, the same
    // technique already used for PawPrintEffect/WrongAnswerWaveEffect's
    // blob edges. Shared by every procedural texture in this file.
    private float SoftEdge(float value, float solidUntil, float softness)
    {
        float t = softness > 0f ? Mathf.Clamp01((value - solidUntil) / softness) : (value > solidUntil ? 1f : 0f);
        float smoothed = t * t * (3f - 2f * t);
        return 1f - smoothed; // 1 while value <= solidUntil, fades to 0 over the next 'softness'
    }

    // ── Building: goldfish ───────────────────────────────────────────────

    private void BuildGoldfish()
    {
        // Same reasoning as BuildGodRays/BuildWaterSparkles — stop old
        // coroutines before the lists they index into get rebuilt.
        StopCycles(ref fishCycleCoroutines);

        if (!enableGoldfish)
        {
            RemoveExtraChildren("Fish ", 0);
            fishImages = null;
            fishRects = null;
            return;
        }

        SeedExampleGoldfishIfEmpty();

        RectTransform container = (RectTransform)transform;
        fishImages = new List<Image>();
        fishRects = new List<RectTransform>();

        for (int i = 0; i < goldfish.Count; i++)
        {
            FishConfig config = goldfish[i];

            GameObject fishObject = FindOrCreateChild(container, "Fish " + i, out bool wasCreated);
            RectTransform fishRect = fishObject.GetComponent<RectTransform>();

            if (wasCreated)
            {
                // Anchored where it rests between leaps — the water's
                // surface. Starting point only — drag it into place
                // afterward; later rebuilds won't reset it.
                fishRect.anchorMin = new Vector2(0.5f, 0f);
                fishRect.anchorMax = new Vector2(0.5f, 0f);
                fishRect.pivot = new Vector2(0.5f, 0.5f);
                fishRect.anchoredPosition = config.anchoredPosition;
            }

            // Texture is wider than tall (fish-shaped) — keep that
            // aspect ratio as the config's single 'size' scales up/down.
            fishRect.sizeDelta = new Vector2(config.size, config.size * (FishTextureHeight / (float)FishTextureWidth));
            // Flip horizontally so the fish actually faces the direction
            // it leaps in, rather than always facing the same way.
            fishRect.localScale = new Vector3(config.leapRight ? -1f : 1f, 1f, 1f);

            Image fishImage = fishObject.GetComponent<Image>();
            if (fishImage == null) fishImage = fishObject.AddComponent<Image>();
            fishImage.sprite = GetFishSprite();
            fishImage.raycastTarget = false;
            // Fully visible at rest (rather than the invisible-between-
            // leaps state it'll actually be in during Play) so it can be
            // seen and positioned while editing the prefab — coroutines
            // don't tick outside Play mode, so an invisible starting
            // state would otherwise never show anything to drag.
            fishImage.color = new Color(config.color.r, config.color.g, config.color.b, 1f);

            fishImages.Add(fishImage);
            fishRects.Add(fishRect);
        }

        RemoveExtraChildren("Fish ", goldfish.Count);
    }

    // First-time convenience only: if you haven't configured any
    // goldfish yet, seeds two large placeholders leaping opposite
    // directions, so there's something obvious to find and drag onto
    // the pool, then dial down afterward.
    private void SeedExampleGoldfishIfEmpty()
    {
        if (goldfish != null && goldfish.Count > 0) return;

        Color obviousGold = new Color(1f, 0.6f, 0.15f, 1f);
        goldfish = new List<FishConfig>
        {
            new FishConfig { anchoredPosition = new Vector2(-150f, 70f), size = 70f, leapHeight = 120f, leapDistance = 80f, leapRight = true, color = obviousGold },
            new FishConfig { anchoredPosition = new Vector2(150f, 70f), size = 70f, leapHeight = 120f, leapDistance = 80f, leapRight = false, color = obviousGold },
        };
    }

    // Starts (or restarts) the goldfish leap coroutines against whatever
    // fishImages/fishRects currently hold — same reasoning as
    // StartGodRayCycles/StartSparkleCycles.
    private void StartFishCycles()
    {
        if (!enableGoldfish) return;
        if (fishImages == null) return;

        StopCycles(ref fishCycleCoroutines);

        fishCycleCoroutines = new List<Coroutine>();
        for (int i = 0; i < goldfish.Count; i++)
        {
            fishCycleCoroutines.Add(StartCoroutine(FishCycleRoutine(i)));
        }
    }

    // ── Animation: goldfish ──────────────────────────────────────────────

    // Repeats forever: sit invisible/submerged for leapHiddenDuration,
    // then leap — rising and falling through a parabolic arc with some
    // horizontal drift and a lean into the motion, fading in as it
    // breaks the surface and fading out as it dives back in — then
    // returns to its resting position and repeats. 'index' is fixed for
    // this coroutine's entire lifetime, matched against goldfish/
    // fishImages/fishRects as they stood when it was started (rebuilds
    // always stop these first — see StopCycles).
    private IEnumerator FishCycleRoutine(int index)
    {
        Image image = fishImages[index];
        RectTransform rect = fishRects[index];

        // The Edit-mode-visible resting alpha (set in BuildGoldfish) is
        // only for positioning convenience — the actual Play-mode
        // resting state is invisible, submerged.
        SetImageAlpha(image, 0f);

        // Spread starts evenly across one full cycle length, based on
        // this fish's position in the list, so they don't all leap at
        // the same time.
        float cycleLength = leapDuration + leapHiddenDuration;
        float startDelay = index * (cycleLength / Mathf.Max(goldfish.Count, 1));
        yield return new WaitForSeconds(startDelay);

        while (true)
        {
            FishConfig config = goldfish[index];
            // Read the RectTransform's own CURRENT position, not
            // config.anchoredPosition — dragging the fish in the Scene/
            // Prefab view only changes the RectTransform, not this
            // separate config value, so using the config here would
            // silently discard any manual repositioning every time a
            // leap starts.
            Vector2 restPosition = rect.anchoredPosition;
            float direction = config.leapRight ? 1f : -1f;

            float elapsed = 0f;
            while (elapsed < leapDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / leapDuration);

                // Parabolic arc: 0 at t=0 and t=1, peaks at t=0.5.
                float arcHeight = 4f * t * (1f - t);
                Vector2 offset = new Vector2(config.leapDistance * direction * t, config.leapHeight * arcHeight);
                rect.anchoredPosition = restPosition + offset;

                // Leans into the jump, peaking at the same midpoint as
                // the arc, rather than any precise tangent calculation.
                float leanAngle = direction * maxLeapTiltDegrees * Mathf.Sin(t * Mathf.PI);
                rect.localRotation = Quaternion.Euler(0f, 0f, leanAngle);

                // Fades in over the first 15% (breaking the surface) and
                // out over the last 15% (diving back in); fully visible
                // through the middle of the arc.
                float fadeIn = Mathf.Clamp01(t / 0.15f);
                float fadeOut = Mathf.Clamp01((1f - t) / 0.15f);
                SetImageAlpha(image, Mathf.Min(fadeIn, fadeOut));

                yield return null;
            }

            rect.anchoredPosition = restPosition;
            rect.localRotation = Quaternion.identity;
            SetImageAlpha(image, 0f);

            yield return new WaitForSeconds(leapHiddenDuration);
        }
    }

    // A small fish silhouette: an oval body with a tapering triangular
    // tail fin, facing left (flipped per-instance via localScale.x in
    // BuildGoldfish for fish leaping the other way). Generated once and
    // cached.
    private Sprite GetFishSprite()
    {
        if (cachedFishSprite != null) return cachedFishSprite;

        Texture2D texture = new Texture2D(FishTextureWidth, FishTextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[FishTextureWidth * FishTextureHeight];

        float bodyCenterX = FishTextureWidth * 0.38f;
        float bodyCenterY = FishTextureHeight * 0.5f;
        float bodyRadiusX = FishTextureWidth * 0.30f;
        float bodyRadiusY = FishTextureHeight * 0.42f;
        const float bodySoftness = 0.18f; // fraction of radius

        // Tail: a triangular wedge from partway through the body out to
        // the texture's right edge, narrowing to a point at the tip.
        float tailStartX = bodyCenterX + bodyRadiusX * 0.5f;
        float tailTipX = FishTextureWidth * 0.98f;
        float tailBaseHalfHeight = bodyRadiusY * 0.85f;
        float tailEdgeSoftness = FishTextureHeight * 0.06f;

        for (int y = 0; y < FishTextureHeight; y++)
        {
            for (int x = 0; x < FishTextureWidth; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                float dx = (px - bodyCenterX) / bodyRadiusX;
                float dy = (py - bodyCenterY) / bodyRadiusY;
                float bodyDist = Mathf.Sqrt(dx * dx + dy * dy);
                float bodyAlpha = SoftEdge(bodyDist, 1f, bodySoftness);

                float tailAlpha = 0f;
                if (px >= tailStartX && px <= tailTipX)
                {
                    float tailT = Mathf.Clamp01((px - tailStartX) / (tailTipX - tailStartX));
                    float allowedHalfHeight = Mathf.Lerp(tailBaseHalfHeight, 0f, tailT);
                    float distFromCenterline = Mathf.Abs(py - bodyCenterY);
                    tailAlpha = allowedHalfHeight > 0.5f ? SoftEdge(distFromCenterline, allowedHalfHeight, tailEdgeSoftness) : 0f;
                }

                float alpha = Mathf.Clamp01(Mathf.Max(bodyAlpha, tailAlpha));
                pixels[y * FishTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedFishSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, FishTextureWidth, FishTextureHeight),
            new Vector2(0.5f, 0.5f));

        return cachedFishSprite;
    }
}
