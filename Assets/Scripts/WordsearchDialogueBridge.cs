using UnityEngine;
using Yarn.Unity;

// WordsearchDialogueBridge
//
// Locks the dialogue's Continue button while the wordsearch puzzle is
// active, so the player can't click past the hint line — meaning it
// simply stays on screen exactly as displayed, with no need for any
// separate recap panel.
//
// HOW IT WORKS:
// Tag the LAST hint line before the puzzle should activate with
// #wordsearch, e.g.:
//
//     Narrator: Find these symbols 𓀀 𓁐 𓃀 to open the door of the sun.
//     Narrator: Find these symbols 𓁷 𓎟 𓅓 to open the door of the river. #wordsearch
//     <<if $selectedPath == "Path_A">>
//         ...
//
// (No <<wordsearch>> command needed any more — delete it from your
// .yarn file if it's still there.)
//
// When that tagged line is shown:
//   1. The Line Advancer is disabled — the Continue button stops
//      responding to clicks, so the line can't be dismissed.
//   2. The wordsearch grid becomes interactive.
// The line stays fully visible the whole time the player is solving
// the puzzle. Once GridManager reports a word was found:
//   3. $selectedPath is set.
//   4. The Line Advancer is re-enabled — Continue works again, and
//      the player can click through to see the branch that plays.
//
// SETUP:
// 1. Attach this script to an empty GameObject (e.g. "WordsearchBridge").
// 2. Drag your GridManager into gridManager.
// 3. Drag the Line Advancer component (found on your Dialogue System /
//    Canvas object) into lineAdvancer.
// 4. Click your Dialogue Runner / Dialogue System object, find the
//    "Dialogue Presenters" list, and add this WordsearchBridge object
//    to it (alongside Line Presenter and your PortraitSwitchers).

public class WordsearchDialogueBridge : DialoguePresenterBase
{
    [Header("References")]
    public DialogueRunner dialogueRunner;
    public GridManager gridManager;
    public LineAdvancer lineAdvancer;

    // The actual visible Continue arrow Button. Disabling the
    // LineAdvancer component alone only stops Unity's automatic
    // Update loop on it — it does NOT stop a UI Button's OnClick()
    // from firing if it's wired to call a method directly. Setting
    // Button.interactable = false is what actually blocks the click.
    public UnityEngine.UI.Button continueButton;

    [Header("Settings")]
    // The tag to look for at the end of a line, e.g. "#wordsearch"
    private const string WordsearchTag = "wordsearch";

    void OnEnable()
    {
        if (gridManager != null)
            gridManager.OnWordFound += HandleWordFound;
    }

    void OnDisable()
    {
        if (gridManager != null)
            gridManager.OnWordFound -= HandleWordFound;
    }

    // Called by GridManager's event when the player solves the puzzle
    private void HandleWordFound(string branchValue)
    {
        // Write the result so the upcoming <<if>> branches can read it
        if (dialogueRunner != null)
        {
            dialogueRunner.VariableStorage.SetValue("$selectedPath", branchValue);
        }

        // Restore normal Continue behaviour for future lines
        if (lineAdvancer != null)
        {
            lineAdvancer.enabled = true;
        }

        if (continueButton != null)
        {
            continueButton.interactable = true;
        }

        // Immediately advance the dialogue ourselves, simulating the
        // exact same click the Continue button would normally send —
        // this is the same method its OnClick() calls internally.
        // No player click needed; the branch plays the moment the
        // correct word is found.
        if (dialogueRunner != null)
        {
            dialogueRunner.RequestNextLine();
        }

        Debug.Log("Wordsearch complete — $selectedPath = " + branchValue + " — auto-advancing.");
    }

    // ── Required abstract members of DialoguePresenterBase ──────────────────

    public override YarnTask OnDialogueStartedAsync()
    {
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        return YarnTask.CompletedTask;
    }

    // Called for every line. This presenter doesn't display anything —
    // it just watches for the #wordsearch tag and locks/unlocks input
    // around it. Returns immediately either way, so it never blocks
    // the Line Presenter from doing its normal job.
    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        if (HasWordsearchTag(line))
        {
            // Lock the Continue button so this line can't be dismissed.
            // Both are set for safety: interactable=false is what
            // actually blocks a UI click; disabling the component
            // additionally stops any keyboard/global-input polling
            // LineAdvancer might also be doing.
            if (lineAdvancer != null)
            {
                lineAdvancer.enabled = false;
            }

            if (continueButton != null)
            {
                continueButton.interactable = false;
            }

            // Let the player start solving immediately while this
            // line is still showing on screen
            if (gridManager != null)
            {
                gridManager.inputEnabled = true;
            }

            Debug.Log("Wordsearch active — Continue locked until solved.");
        }

        return YarnTask.CompletedTask;
    }

    private bool HasWordsearchTag(LocalizedLine line)
    {
        if (line.Metadata == null) return false;

        foreach (string tag in line.Metadata)
        {
            if (tag == WordsearchTag) return true;
        }

        return false;
    }

    public override YarnTask<DialogueOption> RunOptionsAsync(
        DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
    {
        return YarnTask<DialogueOption>.FromResult(null);
    }
}
