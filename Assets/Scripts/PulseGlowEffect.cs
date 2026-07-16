using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// PulseGlowEffect
//
// A fully self-contained "glow" VFX for UI. It needs no sprite or
// prefab asset — it draws its own soft white radial-gradient circle
// into a Texture2D at runtime, wraps that in a Sprite, and displays
// it through an Image on its own GameObject.
//
// On Start it pulses its alpha from 0 up to maxAlpha and back to 0,
// easing in/out rather than moving linearly, once per 'pulseDuration'
// seconds, for exactly 'pulseCount' cycles — then it destroys its own
// GameObject, so callers never need to clean it up manually.
//
// Designed to be dropped onto an empty GameObject anywhere in a UI
// hierarchy (e.g. behind a character portrait) — its RectTransform
// configures itself on Start, so it displays correctly regardless of
// what it gets parented into.

[RequireComponent(typeof(RectTransform))]
public class PulseGlowEffect : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float pulseDuration = 1.5f;   // seconds for one full fade-in-fade-out cycle
    public int pulseCount = 3;          // how many cycles to play before self-destructing
    [Range(0f, 1f)] public float maxAlpha = 0.85f;    // peak opacity reached mid-cycle
    public Color glowColor = Color.white;
    public float circleDiameter = 200f;  // on-screen size in UI units, roughly a head-sized glow

    [Header("Placement")]
    // Where on the parent's rect this effect anchors to, in normalized
    // (0-1) space — (0.5, 0.5) is dead center, (0.5, 1) is top-center,
    // etc. Portrait art is rarely centered on its own RectTransform (the
    // head usually sits well above the rect's vertical middle), so this
    // plus anchoredOffset let a caller reposition the glow without
    // touching code — e.g. anchorPoint (0.5, 1) with a small negative Y
    // offset lands it right around the top of a character's head.
    public Vector2 anchorPoint = new Vector2(0.5f, 0.5f);
    public Vector2 anchoredOffset = Vector2.zero;

    private Image glowImage;
    private RectTransform rectTransform;

    // Size of the generated gradient texture — high enough for a
    // smooth-looking edge without being wasteful at UI scale.
    private const int TextureSize = 128;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();
        StartCoroutine(PulseRoutine());
    }

    // ── Setup ────────────────────────────────────────────────────────────

    // Positions this effect at anchorPoint (+ anchoredOffset) on whatever
    // it was parented into, at a fixed circleDiameter size. Deliberately
    // uses a single-point anchor (not a stretch-to-fill anchor) so the
    // glow's size never depends on how big its parent's RectTransform
    // happens to be — it always ends up as one circleDiameter-sized
    // circle sitting at that anchor point, regardless of what it's
    // parented into.
    private void SetUpRectTransform()
    {
        rectTransform = GetComponent<RectTransform>();

        rectTransform.anchorMin = anchorPoint;
        rectTransform.anchorMax = anchorPoint;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredOffset;
        rectTransform.sizeDelta = new Vector2(circleDiameter, circleDiameter);
    }

    // Adds an Image if this GameObject doesn't already have one, then
    // feeds it a procedurally generated radial-gradient sprite.
    private void SetUpImage()
    {
        glowImage = GetComponent<Image>();
        if (glowImage == null)
        {
            glowImage = gameObject.AddComponent<Image>();
        }

        glowImage.sprite = GenerateRadialGradientSprite();
        glowImage.raycastTarget = false; // it's a background visual effect, not interactive

        Color startColor = glowColor;
        startColor.a = 0f; // begin invisible; the pulse coroutine fades it in
        glowImage.color = startColor;
    }

    // Builds a soft circular gradient: fully white and opaque at the
    // center, smoothly fading to transparent at the edge.
    private Sprite GenerateRadialGradientSprite()
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
                float normalizedDistance = Mathf.Clamp01(distance / maxDistance);

                // SmoothStep gives a soft, non-linear falloff — fully
                // opaque at the center, easing down to fully transparent
                // by the edge, instead of a harsh linear ring.
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

    private IEnumerator PulseRoutine()
    {
        for (int i = 0; i < pulseCount; i++)
        {
            float elapsed = 0f;

            while (elapsed < pulseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / pulseDuration);

                SetAlpha(EaseInOutPulse(t) * maxAlpha);

                yield return null;
            }
        }

        SetAlpha(0f);
        Destroy(gameObject);
    }

    // Maps a normalized cycle position (0 -> 1) to a 0 -> 1 -> 0 curve
    // that eases in and out at every turning point, instead of moving
    // linearly. Built from two back-to-back SmoothSteps: one rising
    // over the first half of the cycle, one falling over the second.
    private float EaseInOutPulse(float t)
    {
        if (t <= 0.5f)
        {
            return Mathf.SmoothStep(0f, 1f, t / 0.5f);
        }

        return Mathf.SmoothStep(1f, 0f, (t - 0.5f) / 0.5f);
    }

    private void SetAlpha(float alpha)
    {
        Color c = glowColor;
        c.a = alpha;
        glowImage.color = c;
    }
}
