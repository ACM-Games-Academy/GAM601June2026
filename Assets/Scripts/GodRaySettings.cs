using System.Collections.Generic;
using UnityEngine;

// GodRaySettings
//
// Single shared definition of the "god ray" ambience effect (soft
// tapered light shafts hanging from the top of a daytime background),
// used by BOTH BackgroundManager (gameplay day/night backgrounds) and
// SplashScreenController (title screen) — assign the SAME asset to
// both, and tuning it once (positions, colors, timing) applies
// everywhere that references it, rather than needing to update two
// separate copies by hand.
//
// SETUP:
// 1. Assets > Create > BAST > God Ray Settings, anywhere in the
//    Project window (Assets/Data/ is a reasonable home).
// 2. Assign the resulting asset to the "God Ray Settings" field on both
//    BackgroundManager and SplashScreenController.
// 3. Tune away — every scene referencing this asset picks up the change
//    automatically, no code or per-scene edits needed.

[CreateAssetMenu(fileName = "GodRaySettings", menuName = "BAST/God Ray Settings")]
public class GodRaySettings : ScriptableObject
{
    [System.Serializable]
    public class GodRayConfig
    {
        // Anchored to the top-center of the background rect (anchorY=1)
        // — this is a horizontal offset plus a slight vertical nudge
        // from the top edge, not a free-floating anchor, since a god
        // ray only makes sense hanging from the sky.
        public Vector2 anchoredPosition;
        public float width = 260f;
        public float height = 1000f;
        // A little tilt reads as raking sunlight rather than a perfectly
        // vertical, mechanical-looking shaft.
        public float rotationDegrees = 0f;
        public Color color = new Color(1f, 0.95f, 0.75f, 1f); // warm sunlight
        [Range(0f, 1f)] public float baseAlpha = 0.35f;
    }

    public bool enableGodRays = true;

    // Empty by default — add one entry per shaft of light you want (e.g.
    // one per skylight opening visible in the day art) and position/
    // rotate it in the Inspector or by dragging in the Scene view.
    public List<GodRayConfig> godRays = new List<GodRayConfig>();

    [Header("Breathing (visible phase)")]
    public float godRaySwaySpeed = 0.4f;
    [Range(0f, 1f)] public float godRaySwayAmount = 0.2f;

    [Header("Appear / Disappear Cycle")]
    // Each ray independently cycles: fade in, stay visible (breathing)
    // for a while, fade out, then vanish completely for an extended
    // pause before repeating. Starts are spread evenly across one full
    // cycle length based on each ray's position in the list, so they're
    // never all visible, or all hidden, at the same time.
    public float godRayVisibleDuration = 6f;
    public float godRayHiddenDuration = 12f;
    public float godRayFadeTransitionDuration = 2.5f;
}
