using System.Collections;
using UnityEngine;
using Yarn.Unity;

// WordsearchDialogueBridge
//
// Connects the wordsearch puzzle to Yarn Spinner.
//
// It registers a custom Yarn command called "wordsearch". When a
// .yarn script reaches the line:
//
//     <<wordsearch>>
//
// the dialogue PAUSES on that command, the wordsearch becomes
// interactive, and the dialogue only resumes once the player has
// found one of the hidden words. The found word's branchValue is
// written into the Yarn variable $selectedPath, which the script
// can then branch on with <<if $selectedPath == "Path_A">>.
//
// SETUP:
// 1. Attach this to any GameObject in the scene (e.g. an empty
//    object named "WordsearchBridge").
// 2. Drag the Dialogue Runner into the dialogueRunner slot.
// 3. Drag your GridManager into the gridManager slot.
// 4. Make sure your .yarn file declares the variable:
//        <<declare $selectedPath = "">>

public class WordsearchDialogueBridge : MonoBehaviour
{
    [Header("References")]
    public DialogueRunner dialogueRunner;
    public GridManager    gridManager;

    // Set to true by the GridManager's OnWordFound event,
    // checked by the waiting coroutine below
    private bool   wordFound = false;
    private string foundBranchValue = "";

    void Awake()
    {
        // Register the <<wordsearch>> command with Yarn Spinner.
        // Because the handler returns an IEnumerator (coroutine),
        // Yarn Spinner treats it as a BLOCKING command — the dialogue
        // automatically waits until the coroutine finishes before
        // moving to the next line. No messages or timing tricks needed.
        dialogueRunner.AddCommandHandler("wordsearch", WordsearchCommand);
    }

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

    // Called by GridManager's event when the player solves a word
    private void HandleWordFound(string branchValue)
    {
        foundBranchValue = branchValue;
        wordFound = true;
    }

    // The blocking command coroutine. Yarn pauses on <<wordsearch>>
    // until this coroutine completes.
    private IEnumerator WordsearchCommand()
    {
        // Reset state for this puzzle round
        wordFound = false;
        foundBranchValue = "";

        // Unlock the puzzle for input
        gridManager.inputEnabled = true;

        Debug.Log("Wordsearch active — dialogue waiting for an answer...");

        // Wait here, frame by frame, until the GridManager reports
        // that a word has been found
        while (!wordFound)
        {
            yield return null;
        }

        // Write the result into the Yarn variable $selectedPath so
        // the script can branch on it
        dialogueRunner.VariableStorage.SetValue("$selectedPath", foundBranchValue);

        Debug.Log("Wordsearch complete — $selectedPath = " + foundBranchValue);

        // Coroutine ends here, which tells Yarn Spinner the command
        // is finished — the dialogue automatically continues to the
        // next line and evaluates the <<if>> branches.
    }
}
