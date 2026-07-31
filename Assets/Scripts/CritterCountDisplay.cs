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

    private Coroutine hideCoroutine;

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
}
