using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// PawPrintEffect
//
// A self-contained "cat paw print" flash for UI — a translucent brown
// paw-print silhouette (one wide palm pad plus four smaller toe pads)
// that appears quickly and disappears quickly, stamped over a
// wordsearch cell the instant it's selected. No sprite/prefab asset
// required: the whole shape is composed from soft overlapping
// ellipses drawn into a Texture2D at runtime (once, then shared
// across every flash).
//
// Unlike this project's other effects, there's no motion here at all
// — just a quick fade in, a brief hold, and a quick fade out — matching
// "appear quickly and disappear quickly" rather than a ripple or shake.
//
// Designed to be dropped directly onto (or as a child of) a grid cell.

[RequireComponent(typeof(RectTransform))]
public class PawPrintEffect : MonoBehaviour
{
    [Header("Flash Settings")]
    public float fadeInDuration = 0.08f;
    public float holdDuration = 0.05f;
    public float fadeOutDuration = 0.12f;
    [Range(0f, 1f)] public float maxAlpha = 0.75f;
    public Color pawColor = new Color(0.4f, 0.26f, 0.13f, 1f); // translucent brown
    public float pawSize = 100f; // on-screen size in UI units

    private Image pawImage;

    // The paw texture is identical for every flash, so it's generated
    // once and shared rather than rebuilt per-instance.
    private static Sprite cachedPawSprite;

    private const int TextureSize = 128;

    // Fraction of each pad's radius spent on the soft anti-aliased edge
    // — the rest of the pad is fully solid. Fixed rather than exposed
    // as a public field, since the generated sprite is cached statically
    // and shared across every instance (changing it per-instance
    // wouldn't do anything after the first flash generates it).
    private const float PadEdgeSoftness = 0.28f;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();

        StartCoroutine(FlashRoutine());
    }

    // ── Setup ────────────────────────────────────────────────────────────

    private void SetUpRectTransform()
    {
        RectTransform rect = GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(pawSize, pawSize);
    }

    private void SetUpImage()
    {
        pawImage = GetComponent<Image>();
        if (pawImage == null)
        {
            pawImage = gameObject.AddComponent<Image>();
        }

        if (cachedPawSprite == null)
        {
            cachedPawSprite = GeneratePawSprite();
        }

        pawImage.sprite = cachedPawSprite;
        pawImage.raycastTarget = false; // it's a background visual effect, not interactive

        Color startColor = pawColor;
        startColor.a = 0f; // begin invisible; the flash coroutine fades it in
        pawImage.color = startColor;
    }

    // Draws a simple paw print silhouette: one wide oval "palm" pad
    // plus four smaller round "toe" pads arranged in an arc above it.
    // Each pad is its own soft-edged ellipse; a pixel's alpha is
    // whichever pad covers it most, so overlapping pads blend cleanly.
    private static Sprite GeneratePawSprite()
    {
        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        // Pad centers and radii in normalized (0-1) UV space.
        Vector2[] padCenters = new Vector2[]
        {
            new Vector2(0.5f, 0.30f),   // main palm pad
            new Vector2(0.26f, 0.60f),  // toe 1
            new Vector2(0.41f, 0.74f),  // toe 2
            new Vector2(0.59f, 0.74f),  // toe 3
            new Vector2(0.74f, 0.60f),  // toe 4
        };
        Vector2[] padRadii = new Vector2[]
        {
            new Vector2(0.30f, 0.24f),
            new Vector2(0.135f, 0.135f),
            new Vector2(0.135f, 0.135f),
            new Vector2(0.135f, 0.135f),
            new Vector2(0.135f, 0.135f),
        };

        Color32[] pixels = new Color32[TextureSize * TextureSize];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float u = (x + 0.5f) / TextureSize;
                float v = (y + 0.5f) / TextureSize;

                float bestAlpha = 0f;

                for (int p = 0; p < padCenters.Length; p++)
                {
                    float dx = (u - padCenters[p].x) / padRadii[p].x;
                    float dy = (v - padCenters[p].y) / padRadii[p].y;
                    float normalizedDistance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Solid through most of the pad's radius, softening
                    // only right at the boundary — fading across the
                    // whole radius (the old behavior) is what made every
                    // pad read as a hazy blob instead of a distinct shape.
                    float edgeStart = 1f - PadEdgeSoftness;
                    float edgeT = Mathf.Clamp01((normalizedDistance - edgeStart) / PadEdgeSoftness);
                    float smoothedEdgeT = edgeT * edgeT * (3f - 2f * edgeT); // cubic smoothstep, computed explicitly
                    float padAlpha = 1f - smoothedEdgeT;

                    bestAlpha = Mathf.Max(bestAlpha, padAlpha);
                }

                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(bestAlpha));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f));
    }

    // ── Flashing ─────────────────────────────────────────────────────────

    private IEnumerator FlashRoutine()
    {
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(0f, maxAlpha, Mathf.Clamp01(elapsed / fadeInDuration)));
            yield return null;
        }
        SetAlpha(maxAlpha);

        yield return new WaitForSeconds(holdDuration);

        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(maxAlpha, 0f, Mathf.Clamp01(elapsed / fadeOutDuration)));
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        Color c = pawColor;
        c.a = alpha;
        pawImage.color = c;
    }
}
