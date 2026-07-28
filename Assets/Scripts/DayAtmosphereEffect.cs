using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// DayAtmosphereEffect
//
// Self-contained "daytime background atmosphere" effect — god rays and
// water sparkles so far, with more effects meant to join it here over
// time. Lives as a PREFAB (Assets/Prefab/DayAtmosphereEffect.prefab)
// rather than being built from scratch by each scene's controller
// script, so editing the prefab (position/rotate the GodRay/Sparkle
// children, tweak this component) updates every scene that uses it —
// currently both the gameplay day background (via BackgroundManager)
// and the splash screen (via SplashScreenController).
//
// Both of those scripts just Instantiate() this prefab as a child of
// their own background layer and otherwise leave it alone — this
// component builds and animates its own children independently.
//
// SETUP:
// 1. This prefab already has its GodRay/Sparkle children built in,
//    using whatever GodRaySettings asset and Water Sparkles list are
//    assigned/configured below at the time it was last built. To
//    rebuild after changing those, either enter Prefab Mode and use the
//    ⋮ menu's "Build In Editor", or just press Play — Awake() does the
//    same build automatically.
// 2. Position the children directly in Prefab Mode if you want to move
//    them — like the original per-scene system, rebuilding only resets
//    a child's position/rotation the first time it's created, never
//    after.

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

    void Awake()
    {
        BuildGodRays();
        BuildWaterSparkles();
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
    }

    void OnDisable()
    {
        StopCycles(ref godRayCycleCoroutines);
        StopCycles(ref sparkleCycleCoroutines);
    }

    // Lets you (re)build without pressing Play — e.g. right after
    // editing the assigned GodRaySettings asset or the Water Sparkles
    // list, or while positioning children in Prefab Mode. Available
    // from the ⋮ menu on this component.
    [ContextMenu("Build In Editor")]
    private void BuildInEditor()
    {
        BuildGodRays();
        BuildWaterSparkles();

        // If invoked while already playing, OnEnable already ran once
        // and won't fire again just because we rebuilt — restart the
        // cycles explicitly so a manual rebuild mid-Play doesn't leave
        // everything static until the next activation.
        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartGodRayCycles();
            StartSparkleCycles();
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

        // Unlike GLSL's smoothstep(edge0, edge1, x), Unity's
        // Mathf.SmoothStep(from, to, t) clamps t to 0-1 directly rather
        // than remapping it against an edge0/edge1 domain first — using
        // it the GLSL way (as the previous version of this method did)
        // silently produces near-constant output across the whole
        // texture instead of an actual falloff, which is exactly what
        // made this render as a solid square. SoftEdge below does the
        // remap explicitly instead, the same technique already used for
        // PawPrintEffect/WrongAnswerWaveEffect's blob edges.
        float SoftEdge(float value, float solidUntil, float softness)
        {
            float t = softness > 0f ? Mathf.Clamp01((value - solidUntil) / softness) : (value > solidUntil ? 1f : 0f);
            float smoothed = t * t * (3f - 2f * t);
            return 1f - smoothed; // 1 while value <= solidUntil, fades to 0 over the next 'softness'
        }

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
}
