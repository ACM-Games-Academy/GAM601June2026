using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// CritterCatchEffect
//
// A "catch the critter" minigame layered into the daytime background:
// mice and snakes intermittently wander across close to the whole
// screen (never more than one at a time by default), moving in short
// darting bursts with brief freezes in between — each freeze picks a
// brand new random direction to dart off in next — rather than one
// smooth glide, so the movement reads as a small animal darting around
// rather than a UI element sliding across the screen. Clicking one
// while it's still on screen counts as a catch — Cat_Meritamun's
// thought bubble (see ThoughtBubblePresenter) pops up with a random
// gloating exclamation appropriate to whichever critter was caught.
// Never being caught just despawns it silently once its lifetime runs
// out — no penalty, no feedback, so a miss doesn't feel punishing.
//
// Built the same way as this project's other atmosphere effects
// (DayAtmosphereEffect, BackgroundManager's night ambience): finds or
// creates its own full-stretch visual layer as a sibling inserted right
// after BackgroundManager's topLayer, so it renders above the
// background but behind portraits/dialogue UI — and every critter
// sprite is generated procedurally (no art assets needed).
//
// DAY-ONLY: this component's own GameObject is meant to be toggled
// active/inactive by BackgroundManager at the same points it toggles
// its other day-only ambience (see BackgroundManager.dayCrittersEffect)
// — critters have no business skittering across a night sky. OnEnable/
// OnDisable start and stop the spawn loops and clear away anything
// currently on screen, so nothing lingers into the night scene.
//
// SETUP:
// 1. Attach to its own GameObject (e.g. "CritterCatchEffect") anywhere
//    in the scene — it doesn't need to be a child of anything visual,
//    it builds its own display layer at Awake().
// 2. Assign Background Manager (to find where to insert its display
//    layer), Thought Bubble Presenter (to show catch exclamations), and
//    Count Display (to show the running mouse/snake catch tally) in the
//    Inspector.
// 3. Assign this GameObject to BackgroundManager's Day Critters Effect
//    field, so it gets shown/hidden with the rest of the day ambience.

[DisallowMultipleComponent]
public class CritterCatchEffect : MonoBehaviour
{
    private enum CritterType { Mouse, Snake }

    [Header("References")]
    public BackgroundManager backgroundManager;
    public ThoughtBubblePresenter thoughtBubblePresenter;
    public CritterCountDisplay countDisplay;

    [Header("Exclamations")]
    // A random line from the matching list plays whenever that
    // critter type is caught.
    public List<string> mouseCaughtLines = new List<string>
    {
        "Yes! Gotcha you rodent!",
        "Ha! Nowhere left to run, little mouse.",
        "Another one for the Temple Cat.",
    };
    public List<string> snakeCaughtLines = new List<string>
    {
        "Ha! Caught a snake!",
        "One less snake slithering around here.",
        "Got you, scaly one!",
    };
    public float thoughtDisplayDuration = 2.5f;

    [Header("Skitter Timing")]
    // How many independent skitter loops run at once — this many
    // critters can potentially be on screen at the same time. 1 means
    // only ever one mouse or snake on screen, since each slot waits for
    // its current critter to finish (caught or despawned) before
    // spawning another.
    public int concurrentSlots = 1;
    public float hiddenDurationMin = 6f;
    public float hiddenDurationMax = 14f;

    [Header("Wander Movement")]
    // Units per second while actively dashing — roughly half the speed
    // this effect originally moved at (a straight edge-to-edge crossing
    // used to take 1.6s, which was too fast to click). Slower on
    // purpose: this is the main lever if catching one still feels too
    // hard or too easy.
    public float moveSpeed = 358.4f;
    // A critter darts in a straight line for a short, random burst
    // (dashDurationMin–Max), then freezes for a short random pause
    // (pauseDurationMin–Max) before picking a brand new random
    // direction and darting again — repeated "stop, look around, dart
    // off a different way" cycles, rather than one smooth glide, is
    // what reads as a small animal rather than a sliding UI element.
    public float dashDurationMin = 0.15f;
    public float dashDurationMax = 0.35f;
    public float pauseDurationMin = 0.15f;
    public float pauseDurationMax = 0.5f;
    // Total time a critter stays on screen (across all its dashes and
    // pauses combined) before it despawns on its own, if never caught.
    public float maxLifetime = 8f;

    [Header("Placement")]
    // How much of the display layer's own full height/width a critter
    // is allowed to wander into — 1 would be genuinely edge-to-edge, a
    // bit less keeps it from wandering flush against the very screen
    // edge. Fractions of the layer's actual size (which fills the whole
    // screen), not fixed pixel bands, so the wander area really does
    // span close to the whole screen regardless of resolution.
    [Range(0f, 1f)] public float verticalRangeFraction = 0.85f;
    [Range(0f, 1f)] public float horizontalRangeFraction = 0.9f;

    [Header("Appearance")]
    public float mouseSize = 70f;
    public float snakeSize = 140f; // twice mouseSize — the snake reads as too small otherwise
    public Color mouseColor = new Color(0.45f, 0.38f, 0.32f, 1f); // warm grey-brown
    public Color snakeColor = new Color(0.35f, 0.55f, 0.25f, 1f); // olive green
    // How much bigger the CLICKABLE area is than the drawn silhouette,
    // on an invisible padded hit area (see SkitterRoutine) — the
    // visible art is never resized by this, only the catchable margin
    // around it. 1.69 = another 30% on top of an earlier 30% increase.
    public float hitboxSizeMultiplier = 1.69f;

    private RectTransform container;
    private List<Coroutine> slotCoroutines;
    private float containerHalfWidth;

    void Awake()
    {
        BuildContainer();

        if (countDisplay != null)
        {
            countDisplay.SetIcons(GetMouseSprite(), mouseColor, GetSnakeSprite(), snakeColor);
        }
    }

    void OnEnable()
    {
        StartSlots();
    }

    void OnDisable()
    {
        StopSlots();
    }

    // ── Building ─────────────────────────────────────────────────────────

    // Finds (or creates, on first run) a full-stretch layer sitting just
    // above the background layers, inserted as topLayer's immediate
    // sibling — same technique as BackgroundManager.BuildNightAmbience.
    private void BuildContainer()
    {
        if (backgroundManager == null || backgroundManager.topLayer == null)
        {
            Debug.LogWarning("CritterCatchEffect: Background Manager (or its Top Layer) isn't assigned — can't find where to build the critter layer.");
            return;
        }

        Transform backgroundParent = backgroundManager.topLayer.transform.parent;
        Transform existing = backgroundParent.Find("CritterLayer");

        GameObject containerObject;
        if (existing != null)
        {
            containerObject = existing.gameObject;
        }
        else
        {
            containerObject = new GameObject("CritterLayer", typeof(RectTransform));
            RectTransform newRect = containerObject.GetComponent<RectTransform>();
            newRect.SetParent(backgroundParent, false);
            newRect.anchorMin = Vector2.zero;
            newRect.anchorMax = Vector2.one;
            newRect.offsetMin = Vector2.zero;
            newRect.offsetMax = Vector2.zero;
            newRect.SetSiblingIndex(backgroundManager.topLayer.transform.GetSiblingIndex() + 1);
        }

        container = containerObject.GetComponent<RectTransform>();
        containerHalfWidth = container.rect.width * 0.5f;
    }

    // ── Spawn loop ───────────────────────────────────────────────────────

    private void StartSlots()
    {
        if (container == null) return;

        StopSlots();

        slotCoroutines = new List<Coroutine>();
        for (int i = 0; i < concurrentSlots; i++)
        {
            slotCoroutines.Add(StartCoroutine(SlotRoutine()));
        }
    }

    private void StopSlots()
    {
        if (slotCoroutines != null)
        {
            foreach (Coroutine c in slotCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            slotCoroutines = null;
        }

        // Clear away anything currently mid-skitter so nothing lingers
        // once this layer's turned off (e.g. night falling mid-skitter).
        if (container != null)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }

    // Each slot independently loops forever: wait a random interval,
    // spawn one critter, let it skitter across (or get caught), repeat.
    // Each slot randomizes its own hidden duration, so slots naturally
    // fall out of sync with each other without needing an explicit
    // stagger the way god rays/sparkles do.
    private IEnumerator SlotRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(hiddenDurationMin, hiddenDurationMax));
            yield return StartCoroutine(SkitterRoutine());
        }
    }

    // Spawns a critter just off-screen at a random edge, then runs it
    // through repeated dart/freeze/redirect cycles (see the class
    // comment) until it's either caught, or maxLifetime runs out.
    private IEnumerator SkitterRoutine()
    {
        CritterType type = Random.value < 0.5f ? CritterType.Mouse : CritterType.Snake;
        bool entersFromRight = Random.value < 0.5f;

        float halfRangeY = (container.rect.height * 0.5f) * verticalRangeFraction;
        float halfRangeX = (container.rect.width * 0.5f) * horizontalRangeFraction;

        float critterSize = type == CritterType.Mouse ? mouseSize : snakeSize;

        float y = Random.Range(-halfRangeY, halfRangeY);
        float startX = entersFromRight ? containerHalfWidth + critterSize : -containerHalfWidth - critterSize;

        float textureAspect = type == CritterType.Mouse
            ? MouseTextureHeight / (float)MouseTextureWidth
            : SnakeTextureHeight / (float)SnakeTextureWidth;
        float visualWidth = critterSize;
        float visualHeight = critterSize * textureAspect;

        // critterObject is the click/raycast target — its rect is
        // deliberately PADDED past the visible art (hitboxSizeMultiplier)
        // so the catchable area extends beyond the drawn silhouette,
        // rather than requiring a pixel-perfect click. It has no visible
        // sprite of its own (fully transparent color), just the raycast.
        // The actual artwork lives on a separate, normal-sized child
        // (Visual) so the padding never makes the critter look bigger
        // than it's drawn.
        GameObject critterObject = new GameObject(type + " Critter", typeof(RectTransform));
        RectTransform critterRect = critterObject.GetComponent<RectTransform>();
        critterRect.SetParent(container, false);
        critterRect.anchorMin = new Vector2(0.5f, 0.5f);
        critterRect.anchorMax = new Vector2(0.5f, 0.5f);
        critterRect.pivot = new Vector2(0.5f, 0.5f);
        critterRect.sizeDelta = new Vector2(visualWidth * hitboxSizeMultiplier, visualHeight * hitboxSizeMultiplier);
        critterRect.anchoredPosition = new Vector2(startX, y);

        Image hitboxImage = critterObject.AddComponent<Image>();
        hitboxImage.color = new Color(0f, 0f, 0f, 0f);
        hitboxImage.raycastTarget = true;

        bool caught = false;
        Button critterButton = critterObject.AddComponent<Button>();
        critterButton.transition = Selectable.Transition.None;
        critterButton.onClick.AddListener(() => caught = true);

        GameObject visualObject = new GameObject("Visual", typeof(RectTransform));
        RectTransform visualRect = visualObject.GetComponent<RectTransform>();
        visualRect.SetParent(critterRect, false);
        visualRect.anchorMin = new Vector2(0.5f, 0.5f);
        visualRect.anchorMax = new Vector2(0.5f, 0.5f);
        visualRect.pivot = new Vector2(0.5f, 0.5f);
        visualRect.anchoredPosition = Vector2.zero;
        visualRect.sizeDelta = new Vector2(visualWidth, visualHeight);

        Image critterImage = visualObject.AddComponent<Image>();
        critterImage.sprite = type == CritterType.Mouse ? GetMouseSprite() : GetSnakeSprite();
        critterImage.color = type == CritterType.Mouse ? mouseColor : snakeColor;
        critterImage.raycastTarget = false;

        // The very first dash always heads inward onto the screen, so
        // it doesn't just sit at the edge — every dash after that picks
        // a completely fresh random direction, full 360 degrees.
        Vector2 direction = new Vector2(entersFromRight ? -1f : 1f, 0f);
        critterRect.localScale = new Vector3(entersFromRight ? -1f : 1f, 1f, 1f);

        float lifetime = 0f;

        while (lifetime < maxLifetime && !caught)
        {
            // ── Dart in the current direction for a short burst ──
            float dashDuration = Random.Range(dashDurationMin, dashDurationMax);
            float dashElapsed = 0f;

            while (dashElapsed < dashDuration && lifetime < maxLifetime && !caught)
            {
                float dt = Time.deltaTime;
                dashElapsed += dt;
                lifetime += dt;

                Vector2 pos = critterRect.anchoredPosition + direction * moveSpeed * dt;
                pos.x = Mathf.Clamp(pos.x, -halfRangeX, halfRangeX);
                pos.y = Mathf.Clamp(pos.y, -halfRangeY, halfRangeY);
                critterRect.anchoredPosition = pos;

                yield return null;
            }

            if (caught || lifetime >= maxLifetime) break;

            // ── Freeze in place, like it's looking around ──
            float pauseDuration = Random.Range(pauseDurationMin, pauseDurationMax);
            float pauseElapsed = 0f;

            while (pauseElapsed < pauseDuration && !caught)
            {
                float dt = Time.deltaTime;
                pauseElapsed += dt;
                lifetime += dt;
                yield return null;
            }

            if (caught) break;

            // ── Pick a brand new direction to dart off in ──
            float angle = Random.Range(0f, Mathf.PI * 2f);
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // Sprites are drawn facing right by default — flip
            // horizontally to roughly match the new direction.
            critterRect.localScale = new Vector3(direction.x >= 0f ? 1f : -1f, 1f, 1f);
        }

        if (caught)
        {
            OnCritterCaught(type);
        }

        if (critterObject != null) Destroy(critterObject);
    }

    private int mouseCaughtCount = 0;
    private int snakeCaughtCount = 0;

    private void OnCritterCaught(CritterType type)
    {
        if (type == CritterType.Mouse) mouseCaughtCount++;
        else snakeCaughtCount++;

        if (countDisplay != null)
        {
            countDisplay.ShowCounts(mouseCaughtCount, snakeCaughtCount);
        }

        if (thoughtBubblePresenter != null)
        {
            List<string> lines = type == CritterType.Mouse ? mouseCaughtLines : snakeCaughtLines;
            if (lines != null && lines.Count > 0)
            {
                string line = lines[Random.Range(0, lines.Count)];
                thoughtBubblePresenter.ShowThought(line, thoughtDisplayDuration);
            }
        }
    }

    // ── Procedural sprites ───────────────────────────────────────────────

    private static Sprite cachedMouseSprite;
    private static Sprite cachedSnakeSprite;
    private const int MouseTextureWidth = 80;
    private const int MouseTextureHeight = 48;
    private const int SnakeTextureWidth = 140;
    // Taller than the old 36px — a properly round head needs enough
    // vertical room that its radius doesn't exceed half the texture's
    // height (or it gets clipped flat by the texture edge, top and
    // bottom, which is exactly what read as "not fully round").
    private const int SnakeTextureHeight = 48;

    // A small mouse silhouette facing right: an oval body, two small
    // round ears near the head end, and a thin tapering tail trailing
    // from the back. Generated once and cached.
    private Sprite GetMouseSprite()
    {
        if (cachedMouseSprite != null) return cachedMouseSprite;

        Texture2D texture = new Texture2D(MouseTextureWidth, MouseTextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[MouseTextureWidth * MouseTextureHeight];

        float bodyCenterX = MouseTextureWidth * 0.62f;
        float bodyCenterY = MouseTextureHeight * 0.55f;
        float bodyRadiusX = MouseTextureWidth * 0.30f;
        float bodyRadiusY = MouseTextureHeight * 0.38f;
        const float bodySoftness = 0.2f;

        // Two small ears near the head (right) end, above the body's
        // own centerline.
        float earRadius = MouseTextureHeight * 0.16f;
        Vector2 earA = new Vector2(bodyCenterX + bodyRadiusX * 0.35f, bodyCenterY - bodyRadiusY * 0.75f);
        Vector2 earB = new Vector2(bodyCenterX + bodyRadiusX * 0.75f, bodyCenterY - bodyRadiusY * 0.65f);

        // Tail: a thin line trailing left from the body, tapering to a
        // fine point at the texture's left edge.
        float tailStartX = bodyCenterX - bodyRadiusX * 0.6f;
        float tailTipX = MouseTextureWidth * 0.02f;
        float tailBaseHalfHeight = MouseTextureHeight * 0.05f;
        float tailEdgeSoftness = MouseTextureHeight * 0.04f;

        for (int y = 0; y < MouseTextureHeight; y++)
        {
            for (int x = 0; x < MouseTextureWidth; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                float dx = (px - bodyCenterX) / bodyRadiusX;
                float dy = (py - bodyCenterY) / bodyRadiusY;
                float bodyDist = Mathf.Sqrt(dx * dx + dy * dy);
                float bodyAlpha = SoftEdge(bodyDist, 1f, bodySoftness);

                float earDistA = Vector2.Distance(new Vector2(px, py), earA) / earRadius;
                float earAlphaA = SoftEdge(earDistA, 1f, 0.3f);
                float earDistB = Vector2.Distance(new Vector2(px, py), earB) / earRadius;
                float earAlphaB = SoftEdge(earDistB, 1f, 0.3f);

                float tailAlpha = 0f;
                if (px <= tailStartX && px >= tailTipX)
                {
                    float tailT = Mathf.Clamp01((tailStartX - px) / (tailStartX - tailTipX));
                    float allowedHalfHeight = Mathf.Lerp(tailBaseHalfHeight, 0f, tailT);
                    float distFromCenterline = Mathf.Abs(py - bodyCenterY);
                    tailAlpha = allowedHalfHeight > 0.5f ? SoftEdge(distFromCenterline, allowedHalfHeight, tailEdgeSoftness) : 0f;
                }

                float alpha = Mathf.Clamp01(Mathf.Max(Mathf.Max(bodyAlpha, tailAlpha), Mathf.Max(earAlphaA, earAlphaB)));
                pixels[y * MouseTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedMouseSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, MouseTextureWidth, MouseTextureHeight),
            new Vector2(0.5f, 0.5f));

        return cachedMouseSprite;
    }

    // A slithering snake silhouette facing right: a wavy tapering body
    // (wide near the head, narrowing to a fine tail tip) following a
    // sine-wave centerline, with a distinct round head bulging out at
    // the fat end. Generated once and cached.
    private Sprite GetSnakeSprite()
    {
        if (cachedSnakeSprite != null) return cachedSnakeSprite;

        Texture2D texture = new Texture2D(SnakeTextureWidth, SnakeTextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[SnakeTextureWidth * SnakeTextureHeight];

        const float waveCycles = 1.5f;
        float waveAmplitude = SnakeTextureHeight * 0.22f;
        // Noticeably thinner than headRadius below, on purpose — if the
        // body's own thickness right behind the head is nearly as wide
        // as the head circle itself, the two blend into one flat-sided
        // band and the head never reads as round, just a flat-topped
        // taper with a barely-rounded tip.
        float headHalfThickness = SnakeTextureHeight * 0.21f;
        float tailHalfThickness = SnakeTextureHeight * 0.04f;
        float edgeSoftness = SnakeTextureHeight * 0.08f;

        // A distinct round head, clearly bulging wider than the body's
        // own taper, sitting right at the fat (right) end of the wavy
        // body. Kept comfortably under half of SnakeTextureHeight (24),
        // and inset from the texture's right edge by headRadius plus a
        // margin, so the circle is never clipped by the texture
        // boundary on any side — a clipped circle is exactly what reads
        // as a flat edge instead of fully round. Sized down from an
        // earlier, too-large pass.
        float headRadius = SnakeTextureHeight * 0.34f;
        Vector2 headCenter = new Vector2(SnakeTextureWidth - headRadius - 3f, SnakeTextureHeight * 0.5f);
        const float headSoftness = 0.2f;
        // How far past headCenter.x the body band takes to fade out —
        // see bodyFade below.
        float bodyFadeDistance = headRadius * 0.4f;

        for (int x = 0; x < SnakeTextureWidth; x++)
        {
            // t = 0 at the head (right, since the sprite faces right),
            // t = 1 at the tail tip (left) — both the wave's amplitude
            // and the body's thickness taper down as t increases, so
            // the wiggle and the width both shrink toward the tail.
            float t = 1f - (x / (float)(SnakeTextureWidth - 1));
            float centerlineY = (SnakeTextureHeight * 0.5f) + Mathf.Sin(t * waveCycles * Mathf.PI * 2f) * waveAmplitude * t;
            float halfThickness = Mathf.Lerp(headHalfThickness, tailHalfThickness, t);
            float px = x + 0.5f;

            // The body band's own taper (halfThickness above) never
            // actually reaches zero near the head — it just runs at
            // roughly headHalfThickness all the way to the texture's
            // true right edge, independent of where the round head
            // circle ends. Left alone, that produces a flat rectangular
            // nub poking out past the round head. Fading the body's
            // contribution out as it approaches/passes headCenter.x
            // hands that whole region over to the head circle alone,
            // which tapers smoothly on its own — no more square nub.
            float bodyFade = SoftEdge(px, headCenter.x, bodyFadeDistance);

            for (int y = 0; y < SnakeTextureHeight; y++)
            {
                float py = y + 0.5f;

                float distFromCenterline = Mathf.Abs(py - centerlineY);
                float bodyAlpha = SoftEdge(distFromCenterline, halfThickness, edgeSoftness) * bodyFade;

                float headDist = Vector2.Distance(new Vector2(px, py), headCenter) / headRadius;
                float headAlpha = SoftEdge(headDist, 1f, headSoftness);

                float alpha = Mathf.Max(bodyAlpha, headAlpha);
                pixels[y * SnakeTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        cachedSnakeSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, SnakeTextureWidth, SnakeTextureHeight),
            new Vector2(0.5f, 0.5f));

        return cachedSnakeSprite;
    }

    // Unlike GLSL's smoothstep(edge0, edge1, x), Unity's
    // Mathf.SmoothStep(from, to, t) clamps t to 0-1 directly rather than
    // remapping it against an edge0/edge1 domain first. This does the
    // remap explicitly instead — same technique used throughout this
    // project's other procedural textures (DayAtmosphereEffect,
    // SadWaveEffect).
    private float SoftEdge(float value, float solidUntil, float softness)
    {
        float t = softness > 0f ? Mathf.Clamp01((value - solidUntil) / softness) : (value > solidUntil ? 1f : 0f);
        float smoothed = t * t * (3f - 2f * t);
        return 1f - smoothed; // 1 while value <= solidUntil, fades to 0 over the next 'softness'
    }
}
