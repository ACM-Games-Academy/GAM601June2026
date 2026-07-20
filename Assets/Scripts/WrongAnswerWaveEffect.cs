using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// WrongAnswerWaveEffect
//
// A fully self-contained "wrong answer" / "confusion" VFX for UI — a
// soft dark grey blob that shakes side to side behind a character's
// portrait, like a wave of sloshing confusion, with a few "?" marks
// scattered near its outer edge, then dissipates. It needs no sprite
// or prefab asset — it draws its own soft radial-gradient blob into a
// Texture2D at runtime (once, then shares it across every instance),
// wraps that in a Sprite, and displays it through an Image. The
// question marks are plain TextMeshProUGUI labels, parented under the
// same RectTransform that shakes, so they move with the blob for free.
//
// Unlike PulseGlowEffect (which chains multiple expanding rings for a
// calm, repeating success cue), this is a single one-shot burst: over
// 'swishDuration' seconds it shakes horizontally through 'shakeCount'
// left-right cycles with steadily decreasing amplitude — like the wave
// losing energy — while fading in quickly and out at the end. It then
// destroys its own GameObject, so callers never need to clean it up.
//
// Designed to be dropped onto an empty GameObject anywhere in a UI
// hierarchy (e.g. behind a character portrait) — its RectTransform
// configures itself on Start, so it displays correctly regardless of
// what it gets parented into.

[RequireComponent(typeof(RectTransform))]
public class WrongAnswerWaveEffect : MonoBehaviour
{
    [Header("Wave Settings")]
    public float swishDuration = 1.2f;   // total seconds for the shake-and-fade burst
    public int shakeCount = 3;          // how many left-right cycles the shake completes
    public float shakeDistance = 40f;    // peak horizontal displacement, in UI units
    [Range(0f, 1f)] public float maxAlpha = 0.8f;    // opacity reached during the hold
    public Color waveColor = new Color(0.25f, 0.25f, 0.25f, 1f); // dark grey
    public float circleDiameter = 260f;  // on-screen size in UI units

    [Header("Question Marks")]
    public int questionMarkCount = 3;
    public float questionMarkFontSize = 42f;
    public Color questionMarkColor = Color.white;
    // How far out the marks sit, as a fraction of the blob's radius —
    // 1.0 would put them exactly on the blob's edge, so a little under
    // that keeps them clearly inside the soft falloff while still
    // reading as "toward the outer edge".
    [Range(0f, 1f)] public float questionMarkOrbitRadius = 0.8f;

    [Header("Placement")]
    // Where on the parent's rect this effect anchors to, in normalized
    // (0-1) space — same convention as PulseGlowEffect. (0.5, 0.5) is
    // dead center, (0.5, 1) is top-center, etc.
    public Vector2 anchorPoint = new Vector2(0.5f, 0.5f);
    public Vector2 anchoredOffset = Vector2.zero;

    private Image waveImage;
    private RectTransform rectTransform;
    private Vector2 restingPosition;
    private List<TextMeshProUGUI> questionMarks = new List<TextMeshProUGUI>();

    // The blob texture is identical for every instance, so it's
    // generated once and shared rather than rebuilt per-instance.
    private static Sprite cachedBlobSprite;

    // Size of the generated gradient texture — high enough for a
    // smooth-looking edge without being wasteful at UI scale.
    private const int TextureSize = 128;

    void Start()
    {
        SetUpRectTransform();
        SetUpImage();
        SetUpQuestionMarks();

        StartCoroutine(ShakeAndFadeRoutine());
    }

    // ── Setup ────────────────────────────────────────────────────────────

    // Positions this effect at anchorPoint (+ anchoredOffset) on whatever
    // it was parented into. Uses a single-point anchor and a fixed
    // sizeDelta so the blob's size never depends on how big its parent's
    // RectTransform happens to be.
    private void SetUpRectTransform()
    {
        rectTransform = GetComponent<RectTransform>();

        rectTransform.anchorMin = anchorPoint;
        rectTransform.anchorMax = anchorPoint;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredOffset;
        rectTransform.sizeDelta = new Vector2(circleDiameter, circleDiameter);

        restingPosition = anchoredOffset;
    }

    // Adds an Image if this GameObject doesn't already have one, then
    // feeds it the shared procedurally generated blob sprite.
    private void SetUpImage()
    {
        waveImage = GetComponent<Image>();
        if (waveImage == null)
        {
            waveImage = gameObject.AddComponent<Image>();
        }

        if (cachedBlobSprite == null)
        {
            cachedBlobSprite = GenerateBlobSprite();
        }

        waveImage.sprite = cachedBlobSprite;
        waveImage.raycastTarget = false; // it's a background visual effect, not interactive

        Color startColor = waveColor;
        startColor.a = 0f; // begin invisible; the shake coroutine fades it in
        waveImage.color = startColor;
    }

    // Spawns questionMarkCount "?" labels evenly spaced in a ring
    // toward the blob's outer edge. Parented under this same
    // RectTransform, so they shake and fade along with the blob
    // automatically rather than needing their own animation.
    private void SetUpQuestionMarks()
    {
        float radius = (circleDiameter / 2f) * questionMarkOrbitRadius;

        for (int i = 0; i < questionMarkCount; i++)
        {
            float angle = (Mathf.PI * 2f / questionMarkCount) * i + Mathf.PI / 2f; // first mark points straight up
            Vector2 markPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            GameObject markObject = new GameObject("QuestionMark", typeof(RectTransform));
            RectTransform markRect = markObject.GetComponent<RectTransform>();
            markRect.SetParent(transform, false);
            markRect.anchorMin = new Vector2(0.5f, 0.5f);
            markRect.anchorMax = new Vector2(0.5f, 0.5f);
            markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.sizeDelta = new Vector2(questionMarkFontSize * 1.5f, questionMarkFontSize * 1.5f);
            markRect.anchoredPosition = markPosition;

            TextMeshProUGUI mark = markObject.AddComponent<TextMeshProUGUI>();
            mark.text = "?";
            mark.fontSize = questionMarkFontSize;
            mark.alignment = TextAlignmentOptions.Center;
            mark.fontStyle = FontStyles.Bold;
            mark.raycastTarget = false;

            Color startColor = questionMarkColor;
            startColor.a = 0f; // begin invisible; the shake coroutine fades it in with the blob
            mark.color = startColor;

            questionMarks.Add(mark);
        }
    }

    // Builds a soft blob: fully opaque at the center, smoothly fading to
    // transparent at the edge.
    private Sprite GenerateBlobSprite()
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

    // ── Shaking ──────────────────────────────────────────────────────────

    // Shakes the blob side to side through shakeCount cycles with
    // steadily decreasing amplitude (like a wave losing energy), while
    // fading it in quickly, holding, then fading out — then removes
    // itself once the burst finishes.
    private IEnumerator ShakeAndFadeRoutine()
    {
        const float fadeInFraction = 0.15f;
        const float fadeOutStartFraction = 0.7f;

        float elapsed = 0f;

        while (elapsed < swishDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / swishDuration);

            // Horizontal shake, amplitude decaying linearly to 0 as the
            // burst plays out.
            float dampenedAmplitude = shakeDistance * (1f - t);
            float oscillation = Mathf.Sin(t * shakeCount * Mathf.PI * 2f) * dampenedAmplitude;
            rectTransform.anchoredPosition = restingPosition + new Vector2(oscillation, 0f);

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

        rectTransform.anchoredPosition = restingPosition;
        Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        Color c = waveColor;
        c.a = alpha;
        waveImage.color = c;

        foreach (TextMeshProUGUI mark in questionMarks)
        {
            Color markColor = questionMarkColor;
            markColor.a = alpha;
            mark.color = markColor;
        }
    }
}
