using UnityEngine;
using UnityEngine.EventSystems;

// Attach to the GridPanel (or any UI object with an Image).
// Swaps a character's expression while the pointer hovers over it.
// Requires: the object has an Image with "Raycast Target" ticked,
// and the Canvas has a GraphicRaycaster (default on UI canvases).

public class GridHoverExpression : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PortraitManager portraitManager;

    [Header("Who and what to swap")]
    public string characterName = "Cat_Meritamun";
    public string hoverExpression = "pawraised";
    public string normalExpression = "neutral";

    // Only react while the puzzle is actually accepting input
    public GridManager gridManager;

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