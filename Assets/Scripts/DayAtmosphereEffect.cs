using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// DayAtmosphereEffect
//
// Self-contained "daytime background atmosphere" effect — currently god
// rays, with more effects meant to join it here over time. Lives as a
// PREFAB (Assets/Prefab/DayAtmosphereEffect.prefab) rather than being
// built from scratch by each scene's controller script, so editing the
// prefab (position/rotate the GodRay children, tweak the
// DayAtmosphereEffect component) updates every scene that uses it —
// currently both the gameplay day background (via BackgroundManager)
// and the splash screen (via SplashScreenController).
//
// Both of those scripts just Instantiate() this prefab as a child of
// their own background layer and otherwise leave it alone — this
// component builds and animates its own children independently.
//
// SETUP:
// 1. This prefab already has three GodRay children built in, using
//    whatever GodRaySettings asset is assigned below at the time it was
//    last built. To rebuild after changing that asset's contents,
//    either enter Prefab Mode and use the ⋮ menu's "Build In Editor",
//    or just press Play — Awake() does the same build automatically.
// 2. Position/rotate the GodRay children directly in Prefab Mode if you
//    want to move them — like the original per-scene system, rebuilding
//    only resets a ray's position/rotation the first time it's created,
//    never after.

[RequireComponent(typeof(RectTransform))]
public class DayAtmosphereEffect : MonoBehaviour
{
    public GodRaySettings godRaySettings;

    private List<Image> godRayImages;
    private List<float> godRaySwaySeeds;
    private List<Coroutine> godRayCycleCoroutines;

    private static Sprite cachedGodRaySprite;
    private const int GodRayTextureWidth = 128;
    private const int GodRayTextureHeight = 256;

    void Awake()
    {
        BuildGodRays();
    }

    // Lets you (re)build without pressing Play — e.g. right after
    // editing the assigned GodRaySettings asset, or while positioning
    // rays in Prefab Mode. Available from the ⋮ menu on this component.
    [ContextMenu("Build In Editor")]
    private void BuildInEditor()
    {
        BuildGodRays();
    }

    // ── Building ─────────────────────────────────────────────────────────

    private void BuildGodRays()
    {
        // Stop any previously running cycle coroutines before rebuilding
        // — each one closes over a list index into godRayImages/
        // godRaySettings.godRays, which would otherwise go stale (or out
        // of range) against the fresh lists built below.
        StopGodRayCycles();

        if (godRaySettings == null)
        {
            Debug.LogWarning("DayAtmosphereEffect: God Ray Settings isn't assigned — nothing to build. " +
                              "Create/assign a GodRaySettings asset (Assets > Create > BAST > God Ray Settings).");
            return;
        }

        if (!godRaySettings.enableGodRays)
        {
            RemoveExtraChildren(0);
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

        RemoveExtraChildren(godRaySettings.godRays.Count);

        // Coroutines only actually run in Play mode — harmless to call
        // here in Edit mode too (via "Build In Editor"), they just sit
        // inert until Play starts.
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

    // Removes any leftover "GodRay N" children whose index is no longer
    // within range — e.g. you deleted an entry from godRaySettings.
    private void RemoveExtraChildren(int keepCount)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("GodRay ")) continue;

            string suffix = child.name.Substring("GodRay ".Length);
            if (int.TryParse(suffix, out int index) && index >= keepCount)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }
    }

    // ── Animation ────────────────────────────────────────────────────────

    private void StopGodRayCycles()
    {
        if (godRayCycleCoroutines == null) return;

        foreach (Coroutine coroutine in godRayCycleCoroutines)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }

        godRayCycleCoroutines = null;
    }

    // Repeats forever: fade in, stay visible (breathing gently) for
    // godRayVisibleDuration, fade out, then disappear completely for
    // godRayHiddenDuration before fading back in. 'index' is fixed for
    // this coroutine's entire lifetime, matched against
    // godRaySettings.godRays/godRayImages as they stood when it was
    // started (see StopGodRayCycles — a rebuild always stops these
    // first).
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
            yield return FadeGodRayAlpha(image, image.color.a, godRaySettings.godRays[index].baseAlpha, godRaySettings.godRayFadeTransitionDuration);

            float elapsed = 0f;
            while (elapsed < godRaySettings.godRayVisibleDuration)
            {
                elapsed += Time.deltaTime;

                GodRaySettings.GodRayConfig config = godRaySettings.godRays[index];
                float noise = Mathf.PerlinNoise(godRaySwaySeeds[index], Time.time * godRaySettings.godRaySwaySpeed);
                float sway = (noise - 0.5f) * 2f * godRaySettings.godRaySwayAmount;
                SetGodRayAlpha(image, Mathf.Clamp01(config.baseAlpha + sway));

                yield return null;
            }

            yield return FadeGodRayAlpha(image, image.color.a, 0f, godRaySettings.godRayFadeTransitionDuration);

            yield return new WaitForSeconds(godRaySettings.godRayHiddenDuration);
        }
    }

    private IEnumerator FadeGodRayAlpha(Image image, float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            SetGodRayAlpha(image, Mathf.Lerp(from, to, t));
            yield return null;
        }

        SetGodRayAlpha(image, to);
    }

    private void SetGodRayAlpha(Image image, float alpha)
    {
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

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
}
