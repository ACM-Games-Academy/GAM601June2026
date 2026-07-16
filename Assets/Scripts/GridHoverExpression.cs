using UnityEngine;
using UnityEngine.EventSystems;

// GridHoverExpression
//
// Attach to the GridPanel (or any UI object with an Image).
//
// Two independent effects while the pointer hovers over the puzzle grid:
//   1. Swaps a character's portrait expression (e.g. neutral <-> pawraised)
//   2. Swaps the mouse cursor itself to a custom image (e.g. a cat paw)
//
// Both revert automatically when the pointer leaves the grid.
//
// Requires: the object has an Image with "Raycast Target" ticked,
// and the Canvas has a GraphicRaycaster (default on UI canvases).

public class GridHoverExpression : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PortraitManager portraitManager;
    public GridManager gridManager;

    [Header("Portrait Expression Swap")]
    public string characterName = "Cat_Meritamun";
    public string hoverExpression = "pawraised";
    public string normalExpression = "neutral";

    [Header("Custom Cursor")]
    // The cursor image to show while hovering over the grid.
    // Import this texture with Texture Type set to "Cursor" in Unity.
    public Texture2D hoverCursorTexture;

    // The point within the texture that acts as the actual click
    // position � for a paw print, this is usually near the top-left
    // pad or centre, adjusted to taste. (0,0) is the texture's top-left.
    public Vector2 cursorHotspot = Vector2.zero;

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

        if (hoverCursorTexture != null)
        {
            Cursor.SetCursor(hoverCursorTexture, cursorHotspot, CursorMode.Auto);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        portraitManager.SetExpression(characterName, normalExpression);

        // Passing null reverts to the system's default cursor
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
