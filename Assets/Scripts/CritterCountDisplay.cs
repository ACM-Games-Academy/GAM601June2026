using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// CritterCountDisplay
//
// A small HUD readout that pops up briefly in the corner of the screen
// showing how many mice and snakes have been caught so far, then hides
// itself again after a pause — called by CritterCatchEffect every time
// a catch happens (see CritterCatchEffect.OnCritterCaught), rather than
// staying permanently on screen.
//
// Like ThoughtBubblePresenter's ThoughtBubble, this is a plain, fixed
// object already sitting in the scene (see "CritterCountDisplay" under
// the main Canvas in YarnViabilityTest.unity) rather than something
// built procedurally at runtime — so its position, size, background
// panel, and text styling are all just normal values on that object,
// editable directly in the Inspector/Scene view. It starts inactive
// (hidden) in the scene, matching its actual default runtime state; if
// you want to check its position/styling without playing, temporarily
// tick it active in the Hierarchy, then switch it back off.
//
// SETUP:
// 1. Attach to the display's own root GameObject.
// 2. Assign Mouse Count Text and Snake Count Text (its two child
//    TextMeshProUGUI labels) in the Inspector.
// 3. Assign this component to CritterCatchEffect's Count Display field.

public class CritterCountDisplay : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI mouseCountText;
    public TextMeshProUGUI snakeCountText;
    // Small icons shown next to each count — set once by
    // CritterCatchEffect (see SetIcons) using the same procedurally
    // generated mouse/snake sprites and colors the critters themselves
    // use, so the icons always match what's actually skittering around.
    public Image mouseIcon;
    public Image snakeIcon;

    [Header("Timing")]
    // How long the readout stays up before hiding itself again.
    public float displayDuration = 5.4f;

    [Header("Final Tally (end of chapter)")]
    // How much bigger this panel becomes when shown via ShowFinalTally
    // below — applied as a straight uniform scale on top of its normal
    // corner-sized RectTransform, so the icons/text/background all grow
    // together proportionally rather than needing a second, separately
    // authored "big" copy of this same UI.
    public float finalTallyScale = 3f;

    private Coroutine hideCoroutine;
    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    // Called once, e.g. from CritterCatchEffect.Awake(), to give the
    // two icons their sprite and tint — kept separate from ShowCounts
    // since the icons never change between catches, only the numbers do.
    public void SetIcons(Sprite mouseSprite, Color mouseTint, Sprite snakeSprite, Color snakeTint)
    {
        if (mouseIcon != null)
        {
            mouseIcon.sprite = mouseSprite;
            mouseIcon.color = mouseTint;
        }

        if (snakeIcon != null)
        {
            snakeIcon.sprite = snakeSprite;
            snakeIcon.color = snakeTint;
        }
    }

    // Updates both counts, shows the display, and (re)starts the timer
    // that hides it again — called fresh on every catch, so catching
    // several critters in a row just keeps extending how long it stays
    // visible rather than flickering off between them.
    public void ShowCounts(int mouseCount, int snakeCount)
    {
        if (mouseCountText != null) mouseCountText.text = "Mice: " + mouseCount;
        if (snakeCountText != null) snakeCountText.text = "Snakes: " + snakeCount;

        gameObject.SetActive(true);

        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displayDuration);
        hideCoroutine = null;
        gameObject.SetActive(false);
    }

    // Shows the SAME readout used throughout the game, but re-anchored
    // to the center of the screen and scaled up — for the end-of-chapter
    // "how did you do" reveal (see CritterJudgmentSequence). Unlike
    // ShowCounts, this doesn't auto-hide: it's meant to stay up through
    // Bes's reaction line and the chapter-ending narration that follows,
    // and nothing in the current game flow needs the corner HUD back
    // afterward.
    public void ShowFinalTally(int mouseCount, int snakeCount)
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        if (mouseCountText != null) mouseCountText.text = "Mice: " + mouseCount;
        if (snakeCountText != null) snakeCountText.text = "Snakes: " + snakeCount;

        // This object starts inactive in the scene, and Unity doesn't
        // run Awake() on an inactive GameObject's components until it's
        // activated for the first time — normally ShowCounts() above
        // does that (called on every catch), which is how rectTransform
        // usually ends up assigned before this method ever runs. But if
        // the player never catches a single critter, ShowCounts() never
        // fires, Awake() never runs, and rectTransform is still null
        // here — so it's fetched directly as a fallback instead of
        // assuming Awake() already did it.
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = new Vector3(finalTallyScale, finalTallyScale, 1f);

        gameObject.SetActive(true);
    }
}
