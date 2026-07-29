using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ContinueArrowGlow
//
// A soft, continuously breathing glow that sits behind the dialogue
// box's Continue arrow. Pairs with tinting the arrow itself the same
// beige as the dialogue box's own background (see the Image color on
// this same GameObject) — with the arrow now the same color as the box
// it sits on, it reads as cut from the same material (diegetic) rather
// than a generic white UI icon. The glow is what keeps it legible as
// "this is clickable" despite blending into the background color.
//
// Pulses forever between minAlpha and maxAlpha while continueButton is
// interactable. When a wordsearch puzzle locks it (continueButton.
// interactable == false — the same flag WordsearchDialogueBridge
// already toggles, no extra wiring needed here), the glow settles down
// to nothing rather than continuing to pulse, so it greys out in step
// with the arrow's own automatic disabled-color tint from the Button
// component.
//
// Builds its own child GameObject at runtime (find-or-create, same
// convention as PulseGlowEffect/AngerPulseEffect/DayAtmosphereEffect)
// — no sprite or prefab asset required, and nothing extra to wire up in
// the scene beyond attaching this to the Continue button's own
// GameObject and assigning Continue Button in the Inspector (or leaving
// it blank to auto-find the Button on this same GameObject).
//
// BACKGROUND-ADAPTIVE BRIGHTNESS:
//
// The Continue button sits just outside the dialogue box's own beige
// panel (its Button Container is anchored 50 units past the panel's
// right edge), so what's actually behind it is whatever's currently
// rendered there — usually the day/night scene art, but sometimes a
// character portrait slides underneath it too (e.g. Meritamun's, which
// has a bright white area right where the button sits). A glow tuned
// for one of those backdrops can all but disappear against another.
//
// Rather than reading a single known sprite, this continuously
// re-composites, every frame, exactly what's layered behind the
// button's own on-screen rect: BackgroundManager's current background
// (always present, full-screen) with every PortraitManager slot's
// portrait drawn over it wherever that portrait's rect actually
// overlaps the button — using each layer's own sprite pixel color
// multiplied by its Image's current tint/alpha, the same formula uGUI
// itself uses to render an Image. That last part matters: a dimmed/
// inactive (non-speaking) portrait is tinted down by PortraitManager,
// so it correctly contributes less brightness than the same portrait
// while fully lit and speaking, rather than always reading as its raw
// sprite color regardless of state.
//
// Requires every Sprite that can end up behind the button (background
// day/night art, every character portrait) to have "Read/Write Enabled"
// checked in its import settings — without it, pixel data isn't
// available on the CPU side and that one layer falls back to a fixed
// neutral brightness rather than erroring or being skipped silently.

[RequireComponent(typeof(RectTransform))]
public class ContinueArrowGlow : MonoBehaviour
{
    [Header("References")]
    public Button continueButton;
    // Both optional — if assigned, the glow brightens or dims to match
    // whatever's actually rendered behind the button right now (scene
    // background, and any portrait currently overlapping it). If left
    // unassigned, the glow just uses minAlpha/maxAlpha as fixed values.
    public BackgroundManager backgroundManager;
    public PortraitManager portraitManager;

    [Header("Glow Look")]
    public Color glowColor = new Color(1f, 0.851f, 0.478f, 1f); // warm sunlit gold

    // Alpha range used when the sampled background is fully dark (e.g.
    // the night scene).
    [Range(0f, 1f)] public float minAlpha = 0.075f;
    [Range(0f, 1f)] public float maxAlpha = 0.3f;

    // Alpha range used when the sampled background is fully bright (e.g.
    // the sunlit day scene, or a light-colored portrait like Meritamun's
    // sitting underneath the button) — noticeably higher, so the glow
    // keeps enough contrast to still read against a light/white backdrop
    // instead of washing out into it.
    [Range(0f, 1f)] public float brightBackgroundMinAlpha = 0.925f;
    [Range(0f, 1f)] public float brightBackgroundMaxAlpha = 1f;

    // Pale gold barely registers against a light/white backdrop — a
    // richer, more saturated yellow-orange reads much better there. The
    // glow's actual color blends toward this as the sampled background
    // gets brighter, on top of the alpha ranges above; against a dark
    // background it stays glowColor unchanged.
    public Color brightBackgroundGlowColor = new Color(0.35f, 0.2275f, 0.0175f, 1f); // dark, dense burnt amber

    // The glow's SHAPE (not just its color/alpha) also adapts: a
    // diffuse, barely-there gradient against a dark background (where
    // even a faint glow already stands out and a solid one would look
    // like an obvious sticker), blending toward a dense, solid-cored
    // disc against a light background (where only a solid shape reads
    // at all). 0 = pure soft gradient, 1 = solid core covering half the
    // radius (see GetGlowSprite's coreRadius) — implemented as two
    // overlaid Image layers cross-faded by backgroundBrightness, rather
    // than regenerating texture data every frame.
    [Range(0f, 1f)] public float minShapeSolidity = 0f;
    [Range(0f, 1f)] public float maxShapeSolidity = 1f;

    // How much bigger than the arrow's own rect the glow halo is, so it
    // reads as a soft aura rather than an outline hugging the shape.
    public float glowScale = 1.8f;

    [Header("Pulse Timing")]
    // Full breathe-in/breathe-out cycles per second — kept slow and
    // gentle on purpose, this is meant to be a quiet ambient cue, not
    // something that competes for attention with the dialogue text.
    public float pulseSpeed = 0.35f;
    // How quickly the glow settles to off once the button locks, and
    // how quickly it resumes pulsing once unlocked again.
    public float lockTransitionDuration = 0.3f;

    private RectTransform arrowRect;
    private RectTransform glowRect;
    private Image glowImageSoft;
    private Image glowImageSolid;
    private float currentAlpha = 0f;

    private float backgroundBrightness = 0.5f; // 0 = fully dark, 1 = fully bright

    // Reused every frame rather than rebuilt, to avoid allocating a new
    // list 36+ times a second for something that only changes when a
    // portrait shows/hides.
    private List<Image> backgroundLayerBuffer = new List<Image>();

    // Textures we've already warned about once, so a still-unreadable
    // texture doesn't spam an exception + log line every frame forever.
    private HashSet<Texture2D> unreadableTextures = new HashSet<Texture2D>();

    private static Sprite cachedSoftGlowSprite;
    private static Sprite cachedSolidGlowSprite;
    private const int GlowTextureSize = 128;
    private const float SoftGlowCoreRadius = 0f;   // pure center-to-edge gradient
    private const float SolidGlowCoreRadius = 0.5f; // fully opaque inner half, feathered outer half

    // How finely to sample the button's own on-screen rect when
    // compositing what's behind it — a grid, not every pixel, since
    // this is a "how bright overall" estimate re-done every frame, not
    // a one-off precise read.
    private const int BrightnessSampleGridSize = 6;

    void Awake()
    {
        arrowRect = GetComponent<RectTransform>();
        if (continueButton == null) continueButton = GetComponent<Button>();

        BuildGlow();
    }

    private void BuildGlow()
    {
        Transform parent = transform.parent != null ? transform.parent : transform;

        GameObject glowObject = FindOrCreateChild(parent, "ArrowGlow", out bool wasCreated);
        glowRect = glowObject.GetComponent<RectTransform>();
        if (glowRect == null) glowRect = glowObject.AddComponent<RectTransform>();

        // uGUI draws siblings in list order — inserting this at the
        // arrow's own sibling index (pushing the arrow one later) is
        // what actually gets the glow to render BEHIND it. Parenting it
        // directly onto the arrow's GameObject instead wouldn't work: a
        // parent's own Graphic always draws before its children, no
        // matter their sibling index (see PortraitManager.
        // CreateBehindPortraitAnchor for the same issue with portraits).
        // Re-asserted every build, not just on creation, in case
        // something else reorders siblings later.
        glowRect.SetParent(parent, false);
        glowRect.SetSiblingIndex(transform.GetSiblingIndex());

        if (wasCreated)
        {
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
        }

        float diameter = Mathf.Max(arrowRect.sizeDelta.x, arrowRect.sizeDelta.y) * glowScale;
        glowRect.sizeDelta = new Vector2(diameter, diameter);

        glowImageSoft = BuildGlowLayer(glowRect, "Soft", GetGlowSprite(SoftGlowCoreRadius));
        glowImageSolid = BuildGlowLayer(glowRect, "Solid", GetGlowSprite(SolidGlowCoreRadius));
    }

    // Builds (or finds) one of the two stacked glow layers as a child of
    // the glow container, stretched to fill it exactly so both layers
    // always share the same position/size the container itself is
    // already sized/positioned to (see BuildGlow above).
    private Image BuildGlowLayer(RectTransform container, string name, Sprite sprite)
    {
        GameObject layerObject = FindOrCreateChild(container, name, out _);
        RectTransform layerRect = layerObject.GetComponent<RectTransform>();
        if (layerRect == null) layerRect = layerObject.AddComponent<RectTransform>();

        layerRect.anchorMin = Vector2.zero;
        layerRect.anchorMax = Vector2.one;
        layerRect.offsetMin = Vector2.zero;
        layerRect.offsetMax = Vector2.zero;

        Image layerImage = layerObject.GetComponent<Image>();
        if (layerImage == null) layerImage = layerObject.AddComponent<Image>();
        layerImage.sprite = sprite;
        layerImage.raycastTarget = false;
        layerImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);

        return layerImage;
    }

    void Update()
    {
        backgroundBrightness = SampleCompositeBrightness();

        // Blend between the dark-background and bright-background alpha
        // ranges based on how bright the sampled background currently
        // is, so a sunlit day scene gets a stronger glow than a dark
        // night one rather than both using the same fixed intensity.
        float effectiveMinAlpha = Mathf.Lerp(minAlpha, brightBackgroundMinAlpha, backgroundBrightness);
        float effectiveMaxAlpha = Mathf.Lerp(maxAlpha, brightBackgroundMaxAlpha, backgroundBrightness);

        bool unlocked = continueButton == null || continueButton.interactable;

        if (unlocked)
        {
            float wave = Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, Mathf.Lerp(effectiveMinAlpha, effectiveMaxAlpha, wave),
                (effectiveMaxAlpha / Mathf.Max(lockTransitionDuration, 0.01f)) * Time.deltaTime);
        }
        else
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f,
                (effectiveMaxAlpha / Mathf.Max(lockTransitionDuration, 0.01f)) * Time.deltaTime);
        }

        // Same brightness reading also warms the glow's actual color
        // toward a more saturated yellow-orange, not just its opacity —
        // pale gold at any alpha still reads weakly against a light or
        // white backdrop.
        Color c = Color.Lerp(glowColor, brightBackgroundGlowColor, backgroundBrightness);

        // ...and blends its SHAPE between the soft and solid layers the
        // same way: mostly the diffuse soft layer on a dark background
        // (so it doesn't read as an obvious sticker sitting on top of
        // the scene), mostly the dense solid layer on a light one
        // (where only a solid shape actually shows up at all).
        float shapeSolidity = Mathf.Lerp(minShapeSolidity, maxShapeSolidity, backgroundBrightness);

        Color softColor = c;
        softColor.a = currentAlpha * (1f - shapeSolidity);
        glowImageSoft.color = softColor;

        Color solidColor = c;
        solidColor.a = currentAlpha * shapeSolidity;
        glowImageSolid.color = solidColor;
    }

    // Composites every background layer (scene background + any
    // overlapping portraits) across a grid of points spanning the
    // button's own on-screen rect, and returns the average brightness
    // (0 = black, 1 = white) of the result. This is "what a player
    // would actually see behind the button right now," not just one
    // known sprite, so it naturally adapts to portraits sliding in and
    // out without any extra state tracking.
    private float SampleCompositeBrightness()
    {
        if (arrowRect == null) return backgroundBrightness;

        CollectBackgroundLayers(backgroundLayerBuffer);
        if (backgroundLayerBuffer.Count == 0) return backgroundBrightness;

        Vector3[] corners = new Vector3[4];
        arrowRect.GetWorldCorners(corners); // 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right

        float totalLuminance = 0f;
        int sampledPoints = 0;

        for (int gy = 0; gy < BrightnessSampleGridSize; gy++)
        {
            float ty = (gy + 0.5f) / BrightnessSampleGridSize;
            Vector3 left = Vector3.Lerp(corners[0], corners[1], ty);
            Vector3 right = Vector3.Lerp(corners[3], corners[2], ty);

            for (int gx = 0; gx < BrightnessSampleGridSize; gx++)
            {
                float tx = (gx + 0.5f) / BrightnessSampleGridSize;
                Vector3 worldPoint = Vector3.Lerp(left, right, tx);

                if (CompositeAtWorldPoint(worldPoint, backgroundLayerBuffer, out Color composited))
                {
                    totalLuminance += composited.r * 0.299f + composited.g * 0.587f + composited.b * 0.114f;
                    sampledPoints++;
                }
            }
        }

        return sampledPoints > 0 ? totalLuminance / sampledPoints : backgroundBrightness;
    }

    // Back-to-front: the scene background first (always covers the
    // whole screen), then every portrait slot's Image on top of it.
    // Portraits are always drawn above the background in this project's
    // canvas layering, and the Continue button sits above everything,
    // so this fixed order matches what's actually on screen.
    private void CollectBackgroundLayers(List<Image> buffer)
    {
        buffer.Clear();

        if (backgroundManager != null && backgroundManager.bottomLayer != null)
        {
            buffer.Add(backgroundManager.bottomLayer);
        }

        if (portraitManager != null)
        {
            foreach (PortraitManager.SlotConfig slot in portraitManager.slots)
            {
                if (slot.portraitImage != null) buffer.Add(slot.portraitImage);
            }
        }
    }

    // Alpha-composites every layer that covers this specific world-space
    // point, back-to-front, and returns the resulting color. Returns
    // false only if not a single layer covers the point (shouldn't
    // happen in practice, since the scene background always covers the
    // full screen).
    private bool CompositeAtWorldPoint(Vector3 worldPoint, List<Image> layers, out Color result)
    {
        result = new Color(0f, 0f, 0f, 0f);
        bool any = false;

        foreach (Image layer in layers)
        {
            if (!layer.isActiveAndEnabled || layer.sprite == null) continue;
            if (layer.color.a <= 0.001f) continue;

            if (!TrySampleLayerAtWorldPoint(layer, worldPoint, out Color sample)) continue;
            if (sample.a <= 0.001f) continue;

            // Standard "over" alpha compositing: the new layer's color
            // wins in proportion to its own alpha, with whatever's
            // already accumulated showing through the rest.
            float outAlpha = sample.a + result.a * (1f - sample.a);
            if (outAlpha > 0.0001f)
            {
                result = new Color(
                    (sample.r * sample.a + result.r * result.a * (1f - sample.a)) / outAlpha,
                    (sample.g * sample.a + result.g * result.a * (1f - sample.a)) / outAlpha,
                    (sample.b * sample.a + result.b * result.a * (1f - sample.a)) / outAlpha,
                    outAlpha);
            }
            any = true;
        }

        return any;
    }

    // Checks whether worldPoint falls within layer's own rect, and if
    // so, samples its sprite there — including layer's current Image
    // tint/alpha in the result, since that's what actually changes a
    // portrait's on-screen brightness when PortraitManager dims it.
    private bool TrySampleLayerAtWorldPoint(Image layer, Vector3 worldPoint, out Color result)
    {
        result = default;

        RectTransform layerRect = layer.rectTransform;
        Vector3 local3D = layerRect.InverseTransformPoint(worldPoint);
        Vector2 localPoint = new Vector2(local3D.x, local3D.y);

        Rect rect = layerRect.rect;
        if (!rect.Contains(localPoint)) return false;

        float u = (localPoint.x - rect.xMin) / rect.width;
        float v = (localPoint.y - rect.yMin) / rect.height;

        Color pixel = SampleSpritePixel(layer.sprite, u, v);
        result = pixel * layer.color; // same formula uGUI itself renders an Image with
        return true;
    }

    // Samples a single normalized (u, v) point on a sprite's texture.
    // Falls back to a fixed neutral grey — rather than throwing or
    // skipping the layer entirely — if the texture isn't marked "Read/
    // Write Enabled" in its import settings, since that's a wrong-but-
    // harmless guess instead of breaking the whole composite.
    private Color SampleSpritePixel(Sprite sprite, float u, float v)
    {
        Texture2D texture = sprite.texture;
        if (unreadableTextures.Contains(texture)) return new Color(0.5f, 0.5f, 0.5f, 1f);

        Rect texRect = sprite.textureRect;
        int pixelX = Mathf.Clamp(Mathf.FloorToInt(texRect.x + u * texRect.width), 0, texture.width - 1);
        int pixelY = Mathf.Clamp(Mathf.FloorToInt(texRect.y + v * texRect.height), 0, texture.height - 1);

        try
        {
            return texture.GetPixel(pixelX, pixelY);
        }
        catch (UnityException)
        {
            unreadableTextures.Add(texture);
            Debug.LogWarning("ContinueArrowGlow: '" + texture.name + "' isn't Read/Write Enabled in its " +
                              "import settings, so it can't be sampled — treating it as neutral brightness. " +
                              "Enable Read/Write Enabled on it to fix this.");
            return new Color(0.5f, 0.5f, 0.5f, 1f);
        }
    }

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

    // Round gradient with a fully-opaque core covering the inner
    // coreRadius fraction of the disc, feathering smoothly to
    // transparent across the remaining outer ring — same technique as
    // this project's other procedural glow effects (PulseGlowEffect,
    // AngerPulseEffect), extended with a solid core. coreRadius 0 gives
    // a pure center-to-edge gradient (only the exact center pixel is
    // ever fully opaque, reading as a diffuse haze); higher values read
    // as progressively more solid/dense. Each distinct coreRadius this
    // is called with is generated once and cached — currently just the
    // two fixed soft/solid variants used above.
    private Sprite GetGlowSprite(float coreRadius)
    {
        bool isSoft = coreRadius <= 0f;
        if (isSoft && cachedSoftGlowSprite != null) return cachedSoftGlowSprite;
        if (!isSoft && cachedSolidGlowSprite != null) return cachedSolidGlowSprite;

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

                float alpha;
                if (normalizedDistance <= coreRadius)
                {
                    alpha = 1f;
                }
                else
                {
                    float t = (normalizedDistance - coreRadius) / (1f - coreRadius);
                    alpha = 1f - Mathf.SmoothStep(0f, 1f, t);
                }

                pixels[y * GlowTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite created = Sprite.Create(
            texture,
            new Rect(0f, 0f, GlowTextureSize, GlowTextureSize),
            new Vector2(0.5f, 0.5f));

        if (isSoft) cachedSoftGlowSprite = created;
        else cachedSolidGlowSprite = created;

        return created;
    }
}
