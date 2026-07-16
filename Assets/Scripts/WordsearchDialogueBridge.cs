using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

// WordsearchDialogueBridge
//
// Connects the wordsearch puzzle to Yarn Spinner.
//
// Registers ONE Yarn command:
//
//   <<setpuzzle symbols:branchValue|symbols:branchValue>>
//
// Each word group is:
//   - A run of hieroglyphs (each glyph is a UTF-16 surrogate pair)
//   - A colon separator
//   - A plain ASCII branch value
// Multiple words are separated by |
//
// Example:
//   <<setpuzzle 𓀀𓁐𓃀:Path_A|𓁷𓎟𓅓:Path_B>>
//
// This fires synchronously (no dialogue pause) so the grid rebuilds
// while the NPC's opening lines are playing.
//
// The #wordsearch tag on the LAST hint line then locks the Continue
// button and activates grid input:
//
//   Scholar: Find 𓀀𓁐𓃀 to prove your knowledge. #wordsearch
//
// Once the player finds a word, $selectedPath is set and the dialogue
// advances automatically via RequestNextLine().
//
// SETUP:
// 1. Attach to an empty GameObject called "WordsearchBridge".
// 2. Register it in the Dialogue Runner's "Dialogue Presenters" list.
// 3. Assign Dialogue Runner, Grid Manager, Line Advancer,
//    Continue Button in the Inspector.

public class WordsearchDialogueBridge : DialoguePresenterBase
{
    [Header("References")]
    public DialogueRunner dialogueRunner;
    public GridManager gridManager;
    public LineAdvancer lineAdvancer;
    public Button continueButton;
    public PortraitManager portraitManager;

    [Header("Word Found Glow")]
    // Manual override: if set, the glow always targets this character
    // regardless of who asked the puzzle's question. Leave blank (the
    // default) to auto-target whichever character's line carried the
    // #wordsearch tag — that's what lets one puzzle flow serve every
    // NPC (Amenhotep, Ahmose, Harwa, Nitiqret, ...) correctly.
    public string glowCharacterName = "";

    // Where the glow anchors on the portrait's rect (0.5, 1 = top-center)
    // and a pixel nudge from there — tune these two in the Inspector
    // until the glow sits over the character's head, no code changes
    // needed.
    public Vector2 glowAnchorPoint = new Vector2(0.5f, 1f);
    public Vector2 glowAnchoredOffset = new Vector2(0f, -60f);

    // Final size each ripple ring grows to, in UI units.
    public float glowCircleDiameter = 350f;

    // Tracks whichever character's line most recently carried the
    // #wordsearch tag, so HandleWordFound knows who to glow without a
    // hardcoded name.
    private string currentWordsearchSpeaker;

    private const string WordsearchTag = "wordsearch";
    private const char WordSeparator = '|';
    private const char ValueSeparator = ':';

    void Awake()
    {
        // Register <<setpuzzle ...>> as a synchronous command.
        // It returns void so Yarn fires it and immediately continues
        // to the next line — no dialogue pause while the grid rebuilds.
        dialogueRunner.AddCommandHandler<string>("setpuzzle", SetPuzzleCommand);
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

    // ── <<setpuzzle>> command ─────────────────────────────────────────────

    // Parses the argument string and loads the puzzle directly into
    // GridManager — no ScriptableObject lookup needed.
    //
    // Argument format: "𓀀𓁐𓃀:Path_A|𓁷𓎟𓅓:Path_B"
    //   Split by | to get word groups: ["𓀀𓁐𓃀:Path_A", "𓁷𓎟𓅓:Path_B"]
    //   Each group splits at : to get symbols string + branch value
    //   Symbols string is parsed glyph-by-glyph using surrogate pairs

    private void SetPuzzleCommand(string arg)
    {
        if (gridManager == null)
        {
            Debug.LogWarning("WordsearchDialogueBridge: GridManager not assigned.");
            return;
        }

        string[] wordGroups = arg.Split(WordSeparator);

        if (wordGroups.Length == 0)
        {
            Debug.LogWarning("WordsearchDialogueBridge: <<setpuzzle>> received empty argument.");
            return;
        }

        List<GridManager.InlineWord> words = new List<GridManager.InlineWord>();

        for (int g = 0; g < wordGroups.Length; g++)
        {
            string group = wordGroups[g].Trim();

            // Find the LAST colon to split symbols from branch value.
            // Using LastIndexOf means any colons that somehow appear in
            // the symbol string (there shouldn't be any) won't break parsing.
            int colonIndex = group.LastIndexOf(ValueSeparator);

            if (colonIndex < 0)
            {
                Debug.LogWarning("WordsearchDialogueBridge: Word group '" + group +
                                 "' has no colon separator. Expected format: symbols:BranchValue");
                continue;
            }

            string symbolsString = group.Substring(0, colonIndex);
            string branchValue = group.Substring(colonIndex + 1);

            // Split the symbols string into individual glyphs.
            // Each Egyptian Hieroglyph is a UTF-16 surrogate pair (2 chars).
            // We step through the string taking 2 chars at a time when
            // a high surrogate is detected, or 1 char for BMP characters.
            List<string> symbols = SplitIntoGlyphs(symbolsString);

            if (symbols.Count == 0)
            {
                Debug.LogWarning("WordsearchDialogueBridge: Word group '" + group +
                                 "' produced no symbols after parsing.");
                continue;
            }

            words.Add(new GridManager.InlineWord
            {
                wordName = "Word_" + g,
                symbols = symbols.ToArray(),
                branchValue = branchValue
            });

            Debug.Log("WordsearchDialogueBridge: Parsed word " + g +
                      " — " + symbols.Count + " glyphs → branchValue: " + branchValue);
        }

        if (words.Count > 0)
        {
            gridManager.LoadInlinePuzzle(words);
        }
        else
        {
            Debug.LogWarning("WordsearchDialogueBridge: <<setpuzzle>> produced no valid words.");
        }
    }

    // Splits a string into individual glyphs, handling surrogate pairs
    // correctly so each Egyptian Hieroglyph becomes one string entry.
    private List<string> SplitIntoGlyphs(string input)
    {
        List<string> glyphs = new List<string>();
        int i = 0;

        while (i < input.Length)
        {
            if (char.IsHighSurrogate(input[i]) && i + 1 < input.Length &&
                char.IsLowSurrogate(input[i + 1]))
            {
                // Supplementary plane character — take both chars as one glyph
                glyphs.Add(input.Substring(i, 2));
                i += 2;
            }
            else
            {
                // BMP character — take one char as one glyph
                glyphs.Add(input.Substring(i, 1));
                i += 1;
            }
        }

        return glyphs;
    }

    // ── Word found ────────────────────────────────────────────────────────

    private void HandleWordFound(string branchValue)
    {
        // Pulse-glow the puzzle's asking character's portrait — runs for
        // every successful find, regardless of which branch was solved
        // or which NPC posed the question.
        string glowTarget = !string.IsNullOrEmpty(glowCharacterName)
            ? glowCharacterName
            : currentWordsearchSpeaker;

        if (portraitManager != null && !string.IsNullOrEmpty(glowTarget))
        {
            portraitManager.PlayEffectOnCharacter(glowTarget, effectObject =>
            {
                PulseGlowEffect glow = effectObject.GetComponent<PulseGlowEffect>();
                glow.anchorPoint = glowAnchorPoint;
                glow.anchoredOffset = glowAnchoredOffset;
                glow.circleDiameter = glowCircleDiameter;
            });
        }

        if (dialogueRunner != null)
        {
            dialogueRunner.VariableStorage.SetValue("$selectedPath", branchValue);
        }

        if (lineAdvancer != null)
        {
            lineAdvancer.enabled = true;
        }

        if (continueButton != null)
        {
            continueButton.interactable = true;
        }

        if (dialogueRunner != null)
        {
            dialogueRunner.RequestNextLine();
        }

        Debug.Log("WordsearchDialogueBridge: Answer registered — $selectedPath = " + branchValue);
    }

    // ── DialoguePresenterBase ─────────────────────────────────────────────

    public override YarnTask OnDialogueStartedAsync()
    {
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        return YarnTask.CompletedTask;
    }

    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        if (HasWordsearchTag(line))
        {
            if (lineAdvancer != null) lineAdvancer.enabled = false;
            if (continueButton != null) continueButton.interactable = false;
            if (gridManager != null) gridManager.inputEnabled = true;

            // Remember who's asking this puzzle's question so the glow
            // can target the right NPC when the answer is found — the
            // same wordsearch flow is reused across many different
            // characters (Amenhotep, Ahmose, Harwa, Nitiqret, ...), so
            // this can't be a single hardcoded name.
            currentWordsearchSpeaker = line.CharacterName;

            Debug.Log("WordsearchDialogueBridge: Wordsearch active — Continue locked.");
        }

        return YarnTask.CompletedTask;
    }

    private bool HasWordsearchTag(LocalizedLine line)
    {
        if (line.Metadata == null) return false;
        foreach (string tag in line.Metadata)
            if (tag == WordsearchTag) return true;
        return false;
    }

    public override YarnTask<DialogueOption> RunOptionsAsync(
        DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
    {
        return YarnTask<DialogueOption>.FromResult(null);
    }
}
