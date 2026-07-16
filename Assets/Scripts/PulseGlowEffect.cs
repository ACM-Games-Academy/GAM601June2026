using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// PulseGlowEffect
//
// A fully self-contained "ripple" VFX for UI, styled to look like rings
// of sunlit water spreading outward from a central point. It needs no
// sprite or prefab asset — it draws its own soft ring-shaped gradient
// into a Texture2D at runtime (once, then shares it across every ripple
// it spawns), wraps that in a Sprite, and displays it through an Image.
//
// Each PulseGlowEffect instance IS a single expanding ring: on Start it
// grows from a small starting size out to circleDiameter over
// 'pulseDuration' seconds, easing out (fast start, slower finish, like
// real ripples losing energy as they spread) while fading from maxAlpha
// down to 0. Partway through that expansion (after 'rippleStagger'
// seconds) it spawns a clone of itself — same center point, one fewer
// remaining ripple — so a chain of overlapping rings radiates outward
// continuously. 'pulseCount' controls how many rings the chain produces
// in total. Every ring destroys its own GameObject once its own
// expansion finishes, so the whole chain cleans itself up with nothing
// left over.
//
// Designed to be dropped onto an empty GameObject anywhere in a UI
// hierarchy (e.g. behind a character portrait) — its RectTransform
// configures itself on Start, so it displays correctly regardless of
// what it gets parented into.

[RequireComponent(typeof(RectTransform))]
public class PulseGlowEffect : MonoBehaviour
{
    [Header("Ripple Settings")]
    public float pulseDuration = 1.5f;   // seconds for one ring to expand and fade out
    public int pulseCount = 3;          // how many rings the chain produces before stopping
    [Range(0f, 1f)] public float maxAlpha = 0.85f;    // opacity each ring starts at
    public Color glowColor = new Color(1f, 0.851f, 0.478f, 1f); // warm sunlit gold
    public float circleDiameter = 200f;  // final on-screen ring size, roughly head-sized

    [Header("Ripple Motion")]
    [Range(0f, 1f)] public float startScale = 0.15f; // ring's starting size, as a fraction of circleDiameter
    public float rippleStagger = 0.5f;   // delay before the next ring in the chain spawns

    [Header("Placement")]
    // Where on the parent's rect this effect anchors to, in normalized
    // (0-1) space — (0.5, 0.5) is dead center, (0.5, 1) is top-center,
    // etc. Portrait art is rarely centered on its own RectTransform (the
    // head usually sits well above the rect's vertical middle), so this
    // plus anchoredOffset let a caller reposition the ripple's center
    // without touching code.
    public Vector2 anchorPoint = new Vector2(0.5f, 0.5f);
    public Vector2 anchoredOffset = Vector2.zero;

    private Image glowImage;
    private RectTransform rectTransform;

    // The ring texture is identical for every ripple this effect ever
    // spawns, so it's generated once and shared rather than rebuilt
    // per-instance.
    private static Sprite cachedRingSprite;

    // Size of the generated gradient texture — high enough for a
    // smooth-looking edge without being wasteful at UI scale.
    private const int TextureSize = 128;

    // Where the bright band of the ring sits and how soft its inner/
    // outer edges are, both as fractions of the texture's radius.
    private const float RingCenter = 0.72f;
    private const float RingSoftness = 0.22f;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();

        StartCoroutine(ExpandAndFadeRoutine());

        if (pulseCount > 1)
        {
            StartCoroutine(SpawnNextRippleAfterDelay());
        }
    }

    // ── Setup ────────────────────────────────────────────────────────────

    // Positions this effect at anchorPoint (+ anchoredOffset) on whatever
    // it was parented into. Uses a single-point anchor (not a stretch-
    // to-fill anchor) and a fixed sizeDelta so the ring's size never
    // depends on how big its parent's RectTransform happens to be —
    // growth is driven separately via localScale in the ripple coroutine.
    private void SetUpRectTransform()
    {
        rectTransform = GetComponent<RectTransform>();

        rectTransform.anchorMin = anchorPoint;
        rectTransform.anchorMax = anchorPoint;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredOffset;
        rectTransform.sizeDelta = new Vector2(circleDiameter, circleDiameter);
        rectTransform.localScale = new Vector3(startScale, startScale, 1f);
    }

    // Adds an Image if this GameObject doesn't already have one, then
    // feeds it the shared procedurally generated ring sprite.
    private void SetUpImage()
    {
        glowImage = GetComponent<Image>();
        if (glowImage == null)
        {
            glowImage = gameObject.AddComponent<Image>();
        }

        if (cachedRingSprite == null)
        {
            cachedRingSprite = GenerateRingSprite();
        }

        glowImage.sprite = cachedRingSprite;
        glowImage.raycastTarget = false; // it's a background visual effect, not interactive

        Color startColor = glowColor;
        startColor.a = 0f; // begin invisible; the ripple coroutine fades it in
        glowImage.color = startColor;
    }

    // Builds a soft ring: transparent at the center, a bright band around
    // RingCenter with a smooth falloff of width RingSoftness on both
    // sides, then transparent again out to the texture's edge.
    private Sprite GenerateRingSprite()
    {
        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(TextureSize / 2f, TextureSize / 2f);
        float maxDistance = TextureSize / 2f;

        Color32[] pixels = new Color32[TextureSize * TextureSize];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float normalizedDistance = distance / maxDistance;

                float distanceFromRing = Mathf.Abs(normalizedDistance - RingCenter);
                float alpha = 1f - Mathf.SmoothStep(0f, RingSoftness, distanceFromRing);

                if (normalizedDistance > 1f) alpha = 0f;

                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f));
    }

    // ── Rippling ─────────────────────────────────────────────────────────

    // Grows this ring from startScale to full size while fading it out,
    // easing the growth so it expands quickly at first and slows down —
    // the way a real ripple loses energy as it spreads — then removes
    // itself once fully faded.
    private IEnumerator ExpandAndFadeRoutine()
    {
        float elapsed = 0f;

        while (elapsed < pulseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / pulseDuration);

            float easedGrowth = 1f - (1f - t) * (1f - t); // quadratic ease-out
            float scale = Mathf.Lerp(startScale, 1f, easedGrowth);
            rectTransform.localScale = new Vector3(scale, scale, 1f);

            float fade = Mathf.Pow(1f - t, 1.5f); // fades faster as the ring nears full size
            SetAlpha(maxAlpha * fade);

            yield return null;
        }

        Destroy(gameObject);
    }

    // Waits, then spawns the next ring in the chain — same center point,
    // one fewer ripple remaining — so overlapping rings keep radiating
    // outward for as long as pulseCount calls for.
    private IEnumerator SpawnNextRippleAfterDelay()
    {
        yield return new WaitForSeconds(rippleStagger);
        SpawnNextRipple();
    }

    private void SpawnNextRipple()
    {
        GameObject nextRing = new GameObject("PulseGlowEffect", typeof(RectTransform));
        nextRing.transform.SetParent(transform.parent, false);
        nextRing.transform.SetAsFirstSibling(); // stay behind the portrait, same as this ring

        PulseGlowEffect nextEffect = nextRing.AddComponent<PulseGlowEffect>();
        nextEffect.pulseDuration = pulseDuration;
        nextEffect.pulseCount = pulseCount - 1;
        nextEffect.maxAlpha = maxAlpha;
        nextEffect.glowColor = glowColor;
        nextEffect.circleDiameter = circleDiameter;
        nextEffect.startScale = startScale;
        nextEffect.rippleStagger = rippleStagger;
        nextEffect.anchorPoint = anchorPoint;
        nextEffect.anchoredOffset = anchoredOffset;
    }

    private void SetAlpha(float alpha)
    {
        Color c = glowColor;
        c.a = alpha;
        glowImage.color = c;
    }
}
