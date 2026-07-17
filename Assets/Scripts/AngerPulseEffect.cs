using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// AngerPulseEffect
//
// A self-contained "anger" pulse VFX for UI — a jagged red spiky burst
// behind a character's portrait, the aggressive counterpart to
// PulseGlowEffect's calm success ripple. Same chain-of-clones
// architecture and no sprite/prefab asset required: it draws its own
// spiky radial gradient into a Texture2D at runtime (once, then shares
// it across every pulse it spawns).
//
// Each AngerPulseEffect instance IS a single pulse: on Start it grows
// from a small starting size out to circleDiameter over 'pulseDuration'
// seconds, easing out, while fading from maxAlpha down to 0. Partway
// through (after 'rippleStagger' seconds) it spawns a clone of itself —
// same center point, one fewer pulse remaining — so a rapid, punchy
// sequence of flashes fires off rather than one calm expanding ring.
// 'pulseCount' controls the total. Every pulse destroys its own
// GameObject once its own expansion finishes.
//
// Unlike PulseGlowEffect's thin ring, this is a filled spiky blob —
// solid toward the center, jagged at the edge — read as a sudden flash
// of temper rather than a gentle ripple. Tuned faster and punchier by
// default too (shorter pulseDuration, tighter rippleStagger).
//
// Designed to be dropped behind a character's portrait — e.g. via
// PortraitManager.PlayEffectOnCharacter<AngerPulseEffect>(characterName, ...).

[RequireComponent(typeof(RectTransform))]
public class AngerPulseEffect : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float pulseDuration = 0.5f;   // seconds for one pulse to flash and fade — punchier than the calm ripple
    public int pulseCount = 4;          // how many pulses the chain produces before stopping
    [Range(0f, 1f)] public float maxAlpha = 0.9f;    // opacity each pulse starts at
    public Color pulseColor = new Color(0.85f, 0.1f, 0.1f, 1f); // angry red
    public float circleDiameter = 300f;  // final on-screen pulse size

    [Header("Pulse Motion")]
    [Range(0f, 1f)] public float startScale = 0.2f; // pulse's starting size, as a fraction of circleDiameter
    public float rippleStagger = 0.18f;  // delay before the next pulse in the chain spawns — tight, for a rapid flurry

    [Header("Placement")]
    // Where on the parent's rect this effect anchors to, in normalized
    // (0-1) space — same convention as PulseGlowEffect.
    public Vector2 anchorPoint = new Vector2(0.5f, 0.5f);
    public Vector2 anchoredOffset = Vector2.zero;

    private Image pulseImage;
    private RectTransform rectTransform;

    // The spike texture is identical for every pulse this effect ever
    // spawns, so it's generated once and shared rather than rebuilt
    // per-instance.
    private static Sprite cachedSpikeSprite;

    private const int TextureSize = 128;

    // How many jagged points ring the burst, and how pronounced they
    // are (0 = perfect circle, 1 = very jagged). Fixed rather than
    // exposed as tunable fields so the shared cached sprite is always
    // valid — every instance draws the exact same shape.
    private const int SpikeCount = 10;
    private const float SpikeSharpness = 0.45f;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();

        StartCoroutine(ExpandAndFadeRoutine());

        if (pulseCount > 1)
        {
            StartCoroutine(SpawnNextPulseAfterDelay());
        }
    }

    // ── Setup ────────────────────────────────────────────────────────────

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

    private void SetUpImage()
    {
        pulseImage = GetComponent<Image>();
        if (pulseImage == null)
        {
            pulseImage = gameObject.AddComponent<Image>();
        }

        if (cachedSpikeSprite == null)
        {
            cachedSpikeSprite = GenerateSpikeSprite();
        }

        pulseImage.sprite = cachedSpikeSprite;
        pulseImage.raycastTarget = false;

        Color startColor = pulseColor;
        startColor.a = 0f;
        pulseImage.color = startColor;
    }

    // Builds a filled, jagged burst: solid toward the center, fading out
    // at an edge whose radius is modulated by angle so it reads as a
    // spiky flash rather than a smooth circle.
    private Sprite GenerateSpikeSprite()
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
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                // Modulate the effective edge radius around the circle
                // so it reads as jagged spikes instead of a smooth ring
                float spikeWave = Mathf.Sin(angle * SpikeCount) * 0.5f + 0.5f; // 0-1
                float radiusScale = (1f - SpikeSharpness) + SpikeSharpness * spikeWave;

                float normalizedDistance = Mathf.Clamp01(distance / (maxDistance * radiusScale));
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalizedDistance);

                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f));
    }

    // ── Pulsing ──────────────────────────────────────────────────────────

    // Grows this pulse from startScale to full size while fading it out,
    // easing the growth so it flashes out quickly then settles — then
    // removes itself once fully faded.
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

            float fade = Mathf.Pow(1f - t, 1.3f); // fades a touch faster than the calm ripple, for a punchier flash
            SetAlpha(maxAlpha * fade);

            yield return null;
        }

        Destroy(gameObject);
    }

    // Waits, then spawns the next pulse in the chain — same center
    // point, one fewer remaining — for a rapid flurry of flashes rather
    // than one calm expanding ring.
    private IEnumerator SpawnNextPulseAfterDelay()
    {
        yield return new WaitForSeconds(rippleStagger);
        SpawnNextPulse();
    }

    private void SpawnNextPulse()
    {
        GameObject nextPulse = new GameObject("AngerPulseEffect", typeof(RectTransform));
        nextPulse.transform.SetParent(transform.parent, false);
        nextPulse.transform.SetAsFirstSibling(); // stay behind the portrait, same as this pulse

        AngerPulseEffect nextEffect = nextPulse.AddComponent<AngerPulseEffect>();
        nextEffect.pulseDuration = pulseDuration;
        nextEffect.pulseCount = pulseCount - 1;
        nextEffect.maxAlpha = maxAlpha;
        nextEffect.pulseColor = pulseColor;
        nextEffect.circleDiameter = circleDiameter;
        nextEffect.startScale = startScale;
        nextEffect.rippleStagger = rippleStagger;
        nextEffect.anchorPoint = anchorPoint;
        nextEffect.anchoredOffset = anchoredOffset;
    }

    private void SetAlpha(float alpha)
    {
        Color c = pulseColor;
        c.a = alpha;
        pulseImage.color = c;
    }
}
