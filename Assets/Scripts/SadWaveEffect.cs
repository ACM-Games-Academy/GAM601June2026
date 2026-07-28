using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// SadWaveEffect
//
// A self-contained "sadness" VFX for UI — a soft blue droplet that
// streams downward from behind a character's portrait, weaving left
// and right as it falls, like a quiet wave of sorrow. No sprite or
// prefab asset required: it draws its own soft elliptical gradient
// blob into a Texture2D at runtime (once, then shares it across every
// drop it spawns).
//
// Each SadWaveEffect instance IS a single falling drop: on Start it
// fades in, then flows downward by 'flowDistance' units over
// 'flowDuration' seconds while its horizontal position weaves side to
// side in a sine wave ('weaveAmplitude'/'weaveCycles'), fading out
// toward the end. Partway through (after 'dropStagger' seconds) it
// spawns a clone of itself — same starting point, one fewer drop
// remaining — so a little cascade of drops flows down one after
// another rather than a single lonely one. 'dropCount' controls how
// many drops the cascade produces in total. Every drop destroys its
// own GameObject once its own flow finishes, so the whole cascade
// cleans itself up with nothing left over.
//
// Designed to be dropped behind a character's portrait — e.g. via
// PortraitManager.PlayEffectOnCharacter<SadWaveEffect>(characterName, ...).

[RequireComponent(typeof(RectTransform))]
public class SadWaveEffect : MonoBehaviour
{
    [Header("Flow Settings")]
    public float flowDuration = 1.6f;    // seconds for one drop to flow down and fade out
    public float flowDistance = 220f;    // how far down the drop travels, in UI units
    public int dropCount = 3;           // how many drops the cascade produces before stopping
    [Range(0f, 1f)] public float maxAlpha = 0.75f;   // opacity each drop reaches during its flow
    public Color waveColor = new Color(0.25f, 0.55f, 0.95f, 1f); // soft sorrowful blue
    public float dropWidth = 70f;
    public float dropHeight = 130f;

    [Header("Weave Motion")]
    public float weaveAmplitude = 35f;   // peak horizontal displacement either side of center
    public float weaveCycles = 2f;       // how many full left-right cycles over one drop's flow
    public float dropStagger = 0.35f;    // delay before the next drop in the cascade spawns

    [Header("Placement")]
    // Where on the parent's rect this effect anchors to, in normalized
    // (0-1) space — same convention as PulseGlowEffect. (0.5, 1) is
    // top-center, a natural starting point for something flowing down.
    public Vector2 anchorPoint = new Vector2(0.5f, 1f);
    public Vector2 anchoredOffset = Vector2.zero;

    private Image waveImage;
    private RectTransform rectTransform;
    private Vector2 restingPosition;

    // The drop texture is identical for every drop this effect ever
    // spawns, so it's generated once and shared rather than rebuilt
    // per-instance.
    private static Sprite cachedDropSprite;

    private const int TextureWidth = 64;
    private const int TextureHeight = 128;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();

        StartCoroutine(FlowAndFadeRoutine());

        if (dropCount > 1)
        {
            StartCoroutine(SpawnNextDropAfterDelay());
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
        rectTransform.sizeDelta = new Vector2(dropWidth, dropHeight);

        restingPosition = anchoredOffset;
    }

    private void SetUpImage()
    {
        waveImage = GetComponent<Image>();
        if (waveImage == null)
        {
            waveImage = gameObject.AddComponent<Image>();
        }

        if (cachedDropSprite == null)
        {
            cachedDropSprite = GenerateDropSprite();
        }

        waveImage.sprite = cachedDropSprite;
        waveImage.raycastTarget = false; // it's a background visual effect, not interactive

        Color startColor = waveColor;
        startColor.a = 0f; // begin invisible; the flow coroutine fades it in
        waveImage.color = startColor;
    }

    // Builds a soft elliptical blob: fully opaque toward the center,
    // smoothly fading to transparent at the edge — taller than it is
    // wide (see dropWidth/dropHeight) so it reads as a falling droplet
    // rather than a plain circle.
    private Sprite GenerateDropSprite()
    {
        Texture2D texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(TextureWidth / 2f, TextureHeight / 2f);

        Color32[] pixels = new Color32[TextureWidth * TextureHeight];

        for (int y = 0; y < TextureHeight; y++)
        {
            for (int x = 0; x < TextureWidth; x++)
            {
                float nx = (x + 0.5f - center.x) / (TextureWidth / 2f);
                float ny = (y + 0.5f - center.y) / (TextureHeight / 2f);
                float normalizedDistance = Mathf.Sqrt(nx * nx + ny * ny);

                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedDistance));

                pixels[y * TextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureWidth, TextureHeight),
            new Vector2(0.5f, 0.5f));
    }

    // ── Flowing ──────────────────────────────────────────────────────────

    // Flows the drop downward from its resting position while weaving
    // side to side in a sine wave, fading in quickly, holding, then
    // fading out toward the end — then removes itself.
    private IEnumerator FlowAndFadeRoutine()
    {
        const float fadeInFraction = 0.15f;
        const float fadeOutStartFraction = 0.7f;

        float elapsed = 0f;

        while (elapsed < flowDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flowDuration);

            float verticalOffset = -flowDistance * t;
            float horizontalOffset = Mathf.Sin(t * weaveCycles * Mathf.PI * 2f) * weaveAmplitude;
            rectTransform.anchoredPosition = restingPosition + new Vector2(horizontalOffset, verticalOffset);

            float alpha;
            if (t < fadeInFraction)
            {
                alpha = Mathf.Lerp(0f, maxAlpha, t / fadeInFraction);
            }
            else if (t > fadeOutStartFraction)
            {
                alpha = Mathf.Lerp(maxAlpha, 0f, (t - fadeOutStartFraction) / (1f - fadeOutStartFraction));
            }
            else
            {
                alpha = maxAlpha;
            }

            SetAlpha(alpha);

            yield return null;
        }

        Destroy(gameObject);
    }

    // Waits, then spawns the next drop in the cascade — same starting
    // point, one fewer drop remaining — so a little trail of drops
    // keeps flowing down for as long as dropCount calls for.
    private IEnumerator SpawnNextDropAfterDelay()
    {
        yield return new WaitForSeconds(dropStagger);
        SpawnNextDrop();
    }

    private void SpawnNextDrop()
    {
        GameObject nextDrop = new GameObject("SadWaveEffect", typeof(RectTransform));
        nextDrop.transform.SetParent(transform.parent, false);
        nextDrop.transform.SetAsFirstSibling(); // stay behind the portrait, same as this drop

        SadWaveEffect nextEffect = nextDrop.AddComponent<SadWaveEffect>();
        nextEffect.flowDuration = flowDuration;
        nextEffect.flowDistance = flowDistance;
        nextEffect.dropCount = dropCount - 1;
        nextEffect.maxAlpha = maxAlpha;
        nextEffect.waveColor = waveColor;
        nextEffect.dropWidth = dropWidth;
        nextEffect.dropHeight = dropHeight;
        nextEffect.weaveAmplitude = weaveAmplitude;
        nextEffect.weaveCycles = weaveCycles;
        nextEffect.dropStagger = dropStagger;
        nextEffect.anchorPoint = anchorPoint;
        nextEffect.anchoredOffset = anchoredOffset;
    }

    private void SetAlpha(float alpha)
    {
        Color c = waveColor;
        c.a = alpha;
        waveImage.color = c;
    }
}
