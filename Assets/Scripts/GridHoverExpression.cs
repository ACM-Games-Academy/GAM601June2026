using UnityEngine;
using UnityEngine.EventSystems;

// GridHoverExpression
//
// Attach to the GridPanel (or any UI object with an Image).
// Swaps a character's expression while the pointer hovers over the
// puzzle grid, and forces that character's portrait to full brightness
// for the whole duration the puzzle is solvable — regardless of hover
// state, speaker logic, or anything else that might otherwise dim it.
//
// Requires: the object has an Image with "Raycast Target" ticked,
// and the Canvas has a GraphicRaycaster (default on UI canvases).

public class GridHoverExpression : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PortraitManager portraitManager;
    public GridManager gridManager;

    [Header("Who and what to swap")]
    public string characterName = "Cat_Meritamun";
    public string hoverExpression = "pawraised";
    public string normalExpression = "neutral";

    // Tracks the previous frame's inputEnabled state, so we can detect
    // the exact moment the puzzle becomes solvable (false -> true) and
    // fire the brightness fix only once, rather than every frame.
    private bool wasInputEnabled = false;

    void Update()
    {
        if (gridManager == null || portraitManager == null) return;

        bool isInputEnabled = gridManager.inputEnabled;

        // Rising edge: puzzle-solving has just started this frame.
        // Force full brightness once, so it's guaranteed regardless of
        // hover state or whatever the previous dialogue line left it at.
        if (isInputEnabled && !wasInputEnabled)
        {
            portraitManager.SetBrightness(characterName, true);
        }

        wasInputEnabled = isInputEnabled;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (gridManager != null && !gridManager.inputEnabled) return;
        portraitManager.SetExpression(characterName, hoverExpression);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (gridManager != null && !gridManager.inputEnabled) return;
        portraitManager.SetExpression(characterName, normalExpression);
    }
}
