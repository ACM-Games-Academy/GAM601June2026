using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// MagicalTransformationParticles
//
// A self-contained "magical sparkle" particle effect for UI. This
// project's portraits render inside a Canvas, not world space, and a
// genuine Unity ParticleSystem doesn't composite reliably with UI
// without extra camera/rendering setup — so instead this hand-builds
// the same idea out of many small soft-glow Image children, each
// animating outward from the center with its own randomized
// trajectory, twinkle and fade. Visually it reads as a particle burst;
// under the hood it's the same kind of procedurally generated UI
// effect as everything else in this project (PulseGlowEffect,
// WrongAnswerWaveEffect) — no sprite/prefab asset required, it draws
// its own soft circular gradient into a Texture2D at runtime and
// shares it across every sparkle.
//
// Spawns 'sparkleCount' sparkles with staggered start times spread
// across 'duration', then destroys its own GameObject once the last
// one finishes its individual lifetime.
//
// Designed to be dropped in front of a character portrait — e.g. via
// PortraitManager.PlayEffectInSlot<MagicalTransformationParticles>(
//   slotName, null, inFront: true) — its RectTransform stretches to
// fill its parent so sparkles have room to scatter across the whole
// portrait area.

public class MagicalTransformationParticles : MonoBehaviour
{
    [Header("Sparkles")]
    public int sparkleCount = 30;
    public float duration = 1.6f;        // total effect lifetime — sparkles keep spawning until this runs out
    public float sparkleLifetime = 0.7f; // how long each individual sparkle lives
    public float sparkleSize = 45f;
    public float scatterRadius = 400f;   // how far sparkles can travel from center, in UI units — deliberately larger than a typical portrait so the burst reads as bigger than the character
    public float driftUpAmount = 100f;   // extra upward bias added to each sparkle's travel
    public Color sparkleColor = new Color(1f, 0.92f, 0.6f); // warm magical gold

    private static Sprite cachedSparkleSprite;
    private const int TextureSize = 64;

    void Start()
    {
        RectTransform rect = GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        StartCoroutine(SpawnSparklesRoutine());
    }

    // ── Spawning ─────────────────────────────────────────────────────────

    private IEnumerator SpawnSparklesRoutine()
    {
        float spawnInterval = duration / sparkleCount;

        for (int i = 0; i < sparkleCount; i++)
        {
            SpawnSparkle();
            yield return new WaitForSeconds(spawnInterval);
        }

        // Wait for the last sparkle spawned to finish its own lifetime
        // before cleaning up the whole effect
        yield return new WaitForSeconds(sparkleLifetime);

        Destroy(gameObject);
    }

    private void SpawnSparkle()
    {
        GameObject sparkleObject = new GameObject("Sparkle", typeof(RectTransform));
        RectTransform rect = sparkleObject.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(sparkleSize, sparkleSize);
        rect.anchoredPosition = Vector2.zero;

        if (cachedSparkleSprite == null)
        {
            cachedSparkleSprite = GenerateSparkleSprite();
        }

        Image image = sparkleObject.AddComponent<Image>();
        image.sprite = cachedSparkleSprite;
        image.raycastTarget = false;

        Color startColor = sparkleColor;
        startColor.a = 0f;
        image.color = startColor;

        StartCoroutine(AnimateSparkle(rect, image));
    }

    // ── Animation ────────────────────────────────────────────────────────

    private IEnumerator AnimateSparkle(RectTransform rect, Image image)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(scatterRadius * 0.3f, scatterRadius);
        Vector2 targetOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance
            + new Vector2(0f, Random.Range(0f, driftUpAmount));

        float startScale = Random.Range(0.5f, 1f);

        float elapsed = 0f;
        while (elapsed < sparkleLifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sparkleLifetime);

            // Ease-out travel — quick start, gentle settle, matching
            // the motion style used elsewhere in this project
            float eased = 1f - (1f - t) * (1f - t);
            rect.anchoredPosition = Vector2.Lerp(Vector2.zero, targetOffset, eased);

            // Twinkle: fade in quickly, hold briefly, fade out
            float alpha;
            if (t < 0.2f) alpha = Mathf.Lerp(0f, 1f, t / 0.2f);
            else if (t > 0.6f) alpha = Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
            else alpha = 1f;

            Color c = sparkleColor;
            c.a = alpha;
            image.color = c;

            float scale = Mathf.Lerp(startScale, startScale * 0.4f, t); // shrinks slightly as it fades
            rect.localScale = new Vector3(scale, scale, 1f);

            yield return null;
        }

        Destroy(rect.gameObject);
    }

    // Builds a small soft circular gradient, sharpened a bit more than
    // the portrait glow effects for a crisper "twinkle point" look.
    private Sprite GenerateSparkleSprite()
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
                alpha = Mathf.Pow(alpha, 1.6f); // sharpen the falloff for a crisper point

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
}
