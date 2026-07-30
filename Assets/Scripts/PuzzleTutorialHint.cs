using UnityEngine;
using UnityEngine.EventSystems;

// PuzzleTutorialHint
//
// Attach to the dialogue panel background — the same GameObject
// DialogueBoxClickToAdvance lives on.
//
// The very first time a wordsearch puzzle ever locks dialogue
// (GridManager.inputEnabled going false -> true, same rising-edge check
// GridHoverExpression already uses), this "arms" itself for exactly
// that one puzzle. While armed, hovering the pointer over the dialogue
// box shows a small tutorial tooltip reading "select this sequence of
// heiroglyphs in a row"; moving the pointer away hides it again. Once
// that first puzzle is solved (or dialogue moves on), it disarms
// permanently — no later puzzle ever shows the hint again, even if you
// hover the dialogue box during them.
//
// Requires: the dialogue panel's background Image has "Raycast Target"
// ticked (already true — DialogueBoxClickToAdvance depends on the same
// thing), and the Canvas has a GraphicRaycaster (default on UI canvases).
//
// SETUP:
// 1. Attach to the dialogue panel background GameObject.
// 2. Assign Grid Manager and Tutorial Text Object (the tooltip's root
//    GameObject — see "TutorialTooltip" in YarnViabilityTest.unity) in
//    the Inspector.

public class PuzzleTutorialHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GridManager gridManager;
    public GameObject tutorialTextObject;

    // Once true, no puzzle ever arms the hint again — set the very
    // first time a puzzle locks dialogue, regardless of whether the
    // player actually hovered to see the tooltip that time.
    private bool firstPuzzleAlreadyHandled = false;

    // True only while the CURRENT puzzle is the one-and-only puzzle the
    // hint is allowed to appear for.
    private bool isFirstPuzzleActive = false;

    private bool wasInputEnabled = false;

    void Awake()
    {
        if (tutorialTextObject != null) tutorialTextObject.SetActive(false);
    }

    void Update()
    {
        if (gridManager == null) return;

        bool isInputEnabled = gridManager.inputEnabled;

        // Rising edge: a puzzle has just locked dialogue.
        if (isInputEnabled && !wasInputEnabled && !firstPuzzleAlreadyHandled)
        {
            isFirstPuzzleActive = true;
            firstPuzzleAlreadyHandled = true;
        }

        // Falling edge: this puzzle's been solved (or dialogue's moved
        // on) — stop offering the hint and hide it if it's showing.
        if (!isInputEnabled && wasInputEnabled)
        {
            isFirstPuzzleActive = false;
            if (tutorialTextObject != null) tutorialTextObject.SetActive(false);
        }

        wasInputEnabled = isInputEnabled;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isFirstPuzzleActive) return;
        if (tutorialTextObject != null) tutorialTextObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tutorialTextObject != null) tutorialTextObject.SetActive(false);
    }
}
