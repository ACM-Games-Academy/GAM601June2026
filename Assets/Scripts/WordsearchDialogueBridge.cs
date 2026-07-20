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
    public SoundEffectManager soundEffectManager;

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

    // Celebratory sound played alongside the ripple glow every time a
    // correct word is found.
    public string correctAnswerSoundEffectName = "CelebratoryChoir";

    [Header("Wrong Answer Wave")]
    // Same anchor/offset/size convention as the glow settings above —
    // tune in the Inspector until the wave sits where you want it
    // relative to the portrait.
    public Vector2 waveAnchorPoint = new Vector2(0.5f, 1f);
    public Vector2 waveAnchoredOffset = new Vector2(0f, -60f);
    public float waveCircleDiameter = 260f;

    public int waveQuestionMarkCount = 3;
    public float waveQuestionMarkFontSize = 42f;
    public Color waveQuestionMarkColor = Color.white;
    [Range(0f, 1f)] public float waveQuestionMarkOrbitRadius = 0.8f;

    // Negative-feedback sound played alongside the wave every time a
    // wrong answer is selected.
    public string wrongAnswerSoundEffectName = "WrongAnswerChord";

    [Header("Cell Selection")]
    // A random variant of this effect plays every time a cell is
    // selected OR deselected during puzzle solving.
    public string cellScuffSoundEffectName = "CellScuff";

    [Header("Puzzle Backdrop")]
    // Semi-transparent panel shown behind the grid while it's unlocked,
    // to draw the player's eye to the puzzle. A plain generated Image —
    // no sprite asset required.
    public Color puzzleBackdropColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
    public float puzzleBackdropPadding = 40f; // how much larger than the grid, per side

    // Tracks whichever character's line most recently carried the
    // #wordsearch tag, so HandleWordFound knows who to glow without a
    // hardcoded name.
    private string currentWordsearchSpeaker;

    // The currently displayed puzzle backdrop, if any.
    private GameObject puzzleBackdropObject;

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
        {
            gridManager.OnWordFound += HandleWordFound;
            gridManager.OnWrongAnswer += HandleWrongAnswer;
            gridManager.OnCellSelectionChanged += HandleCellSelectionChanged;
        }
    }

    void OnDisable()
    {
        if (gridManager != null)
        {
            gridManager.OnWordFound -= HandleWordFound;
            gridManager.OnWrongAnswer -= HandleWrongAnswer;
            gridManager.OnCellSelectionChanged -= HandleCellSelectionChanged;
        }
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

    // ── Puzzle backdrop ──────────────────────────────────────────────────

    // Shows a semi-transparent square behind the grid, sized slightly
    // larger than the grid itself, to highlight that the puzzle is
    // active and waiting on the player.
    private void ShowPuzzleBackdrop()
    {
        if (gridManager == null || gridManager.gridPanel == null) return;

        RectTransform gridRect = gridManager.gridPanel as RectTransform;
        if (gridRect == null) return;

        HidePuzzleBackdrop(); // avoid stacking backdrops across puzzles

        GameObject backdropObject = new GameObject("PuzzleBackdrop", typeof(RectTransform));
        RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();

        // Mirror the grid's own rect, then pad it out on every side so
        // it reads as a slightly larger square sitting behind the grid.
        backdropRect.SetParent(gridRect.parent, false);
        backdropRect.anchorMin = gridRect.anchorMin;
        backdropRect.anchorMax = gridRect.anchorMax;
        backdropRect.pivot = gridRect.pivot;
        backdropRect.anchoredPosition = gridRect.anchoredPosition;
        backdropRect.sizeDelta = gridRect.sizeDelta + new Vector2(puzzleBackdropPadding, puzzleBackdropPadding) * 2f;
        backdropRect.localScale = gridRect.localScale;

        // uGUI always draws a parent's own graphic before its children,
        // but sibling order still governs draw order between elements
        // sharing a parent — insert directly before the grid so this
        // backdrop renders behind it instead of on top.
        backdropRect.SetSiblingIndex(gridRect.GetSiblingIndex());

        Image backdropImage = backdropObject.AddComponent<Image>();
        backdropImage.color = puzzleBackdropColor; // no sprite needed — Image renders a solid rect by default
        backdropImage.raycastTarget = false; // purely decorative, must not intercept grid cell clicks

        puzzleBackdropObject = backdropObject;
    }

    // Removes the puzzle backdrop, if one is currently showing.
    private void HidePuzzleBackdrop()
    {
        if (puzzleBackdropObject != null)
        {
            Destroy(puzzleBackdropObject);
            puzzleBackdropObject = null;
        }
    }

    // ── Wrong answer ─────────────────────────────────────────────────────

    private void HandleWrongAnswer()
    {
        // Swish a dark grey, question-mark-flecked wave behind the
        // puzzle's asking character's portrait — same targeting rule as
        // the success glow, just a different effect and settings.
        string effectTarget = ResolveEffectTarget();

        if (portraitManager != null && !string.IsNullOrEmpty(effectTarget))
        {
            portraitManager.PlayEffectOnCharacter<WrongAnswerWaveEffect>(effectTarget, wave =>
            {
                wave.anchorPoint = waveAnchorPoint;
                wave.anchoredOffset = waveAnchoredOffset;
                wave.circleDiameter = waveCircleDiameter;
                wave.questionMarkCount = waveQuestionMarkCount;
                wave.questionMarkFontSize = waveQuestionMarkFontSize;
                wave.questionMarkColor = waveQuestionMarkColor;
                wave.questionMarkOrbitRadius = waveQuestionMarkOrbitRadius;
            });
        }

        if (soundEffectManager != null)
        {
            soundEffectManager.PlaySoundEffect(wrongAnswerSoundEffectName);
        }
    }

    // ── Cell selection ───────────────────────────────────────────────────

    private void HandleCellSelectionChanged()
    {
        if (soundEffectManager != null)
        {
            soundEffectManager.PlaySoundEffect(cellScuffSoundEffectName);
        }
    }

    // Which character's portrait an effect should target: the manual
    // override if one is set, otherwise whichever character's line most
    // recently carried the #wordsearch tag.
    private string ResolveEffectTarget()
    {
        return !string.IsNullOrEmpty(glowCharacterName)
            ? glowCharacterName
            : currentWordsearchSpeaker;
    }

    // ── Word found ────────────────────────────────────────────────────────

    private void HandleWordFound(string branchValue)
    {
        // The puzzle's been solved — the highlighting backdrop has
        // served its purpose.
        HidePuzzleBackdrop();

        // Pulse-glow the puzzle's asking character's portrait — runs for
        // every successful find, regardless of which branch was solved
        // or which NPC posed the question.
        string glowTarget = ResolveEffectTarget();

        if (portraitManager != null && !string.IsNullOrEmpty(glowTarget))
        {
            portraitManager.PlayEffectOnCharacter<PulseGlowEffect>(glowTarget, glow =>
            {
                glow.anchorPoint = glowAnchorPoint;
                glow.anchoredOffset = glowAnchoredOffset;
                glow.circleDiameter = glowCircleDiameter;
            });
        }

        if (soundEffectManager != null)
        {
            soundEffectManager.PlaySoundEffect(correctAnswerSoundEffectName);
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
        // Safety net: guarantee no backdrop is left over from a
        // previous playtest or scene when a fresh conversation begins
        HidePuzzleBackdrop();
        return YarnTask.CompletedTask;
    }

    public override YarnTask OnDialogueCompleteAsync()
    {
        HidePuzzleBackdrop();
        return YarnTask.CompletedTask;
    }

    public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        if (HasWordsearchTag(line))
        {
            if (lineAdvancer != null) lineAdvancer.enabled = false;
            if (continueButton != null) continueButton.interactable = false;
            if (gridManager != null) gridManager.inputEnabled = true;

            ShowPuzzleBackdrop();

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
