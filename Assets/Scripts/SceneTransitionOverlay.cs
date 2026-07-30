using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// SceneTransitionOverlay
//
// A single full-screen black Image that survives scene loads
// (DontDestroyOnLoad), sitting on a Canvas with a very high sorting
// order so it always draws on top of everything in either scene.
//
// Exists to close a gap that neither scene's own fade-to-black logic
// can close by itself: right at the moment a new scene activates (old
// scene's objects torn down, new scene's Awake/Start running), there's
// a brief window where the render can pick up the destination camera's
// own clear behavior, a stale serialized color, a first-time texture
// upload, etc. — sources that live entirely outside either scene's own
// fade coroutine. Rather than trying to win that race with precise
// Awake() timing (fragile, and scene-specific — see BackgroundManager's
// Awake()), this object just stays opaque black straight THROUGH the
// whole boundary, handed off from whichever scene made it opaque, and
// is only faded away once the destination scene is actually ready.
public class SceneTransitionOverlay : MonoBehaviour
{
    private static SceneTransitionOverlay instance;
    private Image overlayImage;

    public static SceneTransitionOverlay GetOrCreate()
    {
        if (instance != null) return instance;

        GameObject canvasObject = new GameObject("SceneTransitionOverlay", typeof(RectTransform));
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // above any ordinary in-scene canvas
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject imageObject = new GameObject("Overlay", typeof(RectTransform));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = imageObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f); // starts fully transparent — nothing to hide until told to
        image.raycastTarget = false;

        instance = canvasObject.AddComponent<SceneTransitionOverlay>();
        instance.overlayImage = image;
        DontDestroyOnLoad(canvasObject);

        return instance;
    }

    // Snaps straight to fully opaque black. Meant to be called only once
    // the calling scene's own local fade-to-black has already visually
    // finished, so the handoff itself is invisible — this object then
    // just keeps covering the screen through the scene load that follows.
    public void SetOpaque()
    {
        overlayImage.color = Color.black;
    }

    public void FadeOut(float duration)
    {
        StartCoroutine(FadeOutRoutine(duration));
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        float startAlpha = overlayImage.color.a;

        // Called in the same frame as BackgroundManager's own fade-in,
        // right after scene activation's synchronous Awake/Start burst
        // (procedural texture generation, ambience setup, etc.) — that
        // frame's Time.deltaTime can spike well above normal. Waiting
        // one frame here lets the spike land on a no-op frame instead
        // of being baked into this fade's first step, which otherwise
        // reads as a visible skip right as the reveal begins (and keeps
        // this in lockstep with BackgroundManager's matching fade-in).
        yield return null;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / duration));
            overlayImage.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }

        overlayImage.color = new Color(0f, 0f, 0f, 0f);
    }
}
