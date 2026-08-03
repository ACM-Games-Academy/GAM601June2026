using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// GridManager — Inline puzzle version.
//
// Puzzles are defined entirely in .yarn files using <<setpuzzle>>.
// There are no ScriptableObject assets or Inspector word lists.
// Call LoadInlinePuzzle() (via WordsearchDialogueBridge) to clear
// the current grid and build a fresh one from parsed word data.

public class GridManager : MonoBehaviour
{
    [Header("References")]
    public GameObject cellPrefab;
    public Transform gridPanel;

    [Header("Grid Settings")]
    public int defaultGridWidth = 6;
    public int defaultGridHeight = 6;

    [Header("Fill Hieroglyphs")]
    // Random hieroglyphs used to fill empty cells around the answers.
    // Reset from CanonicalFillHieroglyphs every Awake() (see below) —
    // this field is still shown in the Inspector for visibility, but
    // isn't left to whatever value Unity happened to serialize into the
    // scene, since a scene saved before this list was last expanded
    // would otherwise keep loading with the old, smaller set even
    // though the code's default has since moved on. That mismatch is
    // exactly what caused fill tiles to draw from a stale 8-glyph pool
    // while everything reading the field fresh (e.g. in code, or a
    // freshly added component) already saw the full 51.
    public string[] fillHieroglyphs;

    // The actual source of truth for the fill pool — see fillHieroglyphs
    // above for why this isn't just assigned directly as that field's
    // default.
    private static readonly string[] CanonicalFillHieroglyphs = new string[]
    {
        "𓀀",
        "𓁐",
        "𓆓",
        "𓅓",
        "𓇳",
        "𓏏",
        "𓎡",
        "𓂝",
        "𓂋",
        "𓄿",
        "𓊹",
        "𓉐",
        "𓏍",
        "𓆑",
        "𓃭",
        "𓃠",
        "𓅱",
        "𓄤",
        "𓄣",
        "𓁶",
        "𓁹",
        "𓉻",
        "𓌅",
        "𓋾",
        "𓏛",
        "𓐍",
        "𓈗",
        "𓊪",
        "𓋴",
        "𓈎",
        "𓅨",
        "𓆙",
        "𓃀",
        "𓐫",
        "𓆣",
        "𓆈",
        "𓌢",
        "𓋀",
        "𓋔",
        "𓇼",
        "𓇓",
        "𓆏",
        "𓊽",
        "𓍯",
        "𓌱",
        "𓎛",
        "𓋹",
        "𓁷",
        "𓎟",
        "𓈍"
    };

    void Awake()
    {
        fillHieroglyphs = CanonicalFillHieroglyphs;

        // Captured once, before any puzzle ever touches the layout, so
        // SetGridSize() always has the artist-authored on-screen
        // footprint to fit any future grid size into (see
        // ConfigureGridLayout) rather than compounding resizes on top
        // of whatever the previous puzzle left behind.
        if (gridPanel != null)
        {
            RectTransform panelRect = gridPanel as RectTransform;
            if (panelRect != null)
            {
                gridPanelFootprint = panelRect.sizeDelta;
            }
        }
    }

    void Start()
    {
        StartCoroutine(ScrambleLoop());
    }

    private Vector2 gridPanelFootprint;

    // ── Runtime state ─────────────────────────────────────────────────────

    private Cell[,] grid;
    private ActiveWord[] activeWords;

    private List<Cell> selectedCells = new List<Cell>();
    private bool isClearingSelection = false;

    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color foundColor = Color.green;

    [Header("Input Control")]
    public bool inputEnabled = false;

    public int wordLength = 3;

    [Header("Answer Reaction Timing")]
    // Pause between the player's final selection and the NPC-side
    // reaction (portrait effect + sound) firing, so the two don't land
    // on top of each other.
    public float wrongAnswerReactionDelay = 0.4f;
    public float correctAnswerReactionDelay = 1.0f;

    [Header("Grid Scramble")]
    // Every this many seconds while a puzzle is actively being solved
    // (inputEnabled), the grid re-shuffles into a fresh random layout —
    // the same answer word(s), just newly randomized positions and fill
    // glyphs (see PlaceWordsInGrid/BuildPuzzleGlyphPool, both of which
    // this reuses unchanged by simply calling LoadInlinePuzzle again
    // with the same word list) — a time-pressure mechanic so lingering
    // too long doesn't pay off. Set scrambleEnabled false to turn the
    // whole thing off without touching anything else.
    public bool scrambleEnabled = true;
    public float scrambleInterval = 30f;
    public float scrambleSlideDuration = 0.6f;
    // How far cells fly out/in during the scatter, in UI units.
    public float scrambleScatterDistance = 400f;

    [Header("Grid Scramble — Anticipation Wave")]
    // In the final stretch of scrambleInterval before a scramble fires,
    // the grid's columns cascade a gentle lift-and-pop, left to right —
    // like a countdown tick — repeating wavePulseCount times, evenly
    // spaced across waveLeadDuration (3 pulses over 3 seconds = once per
    // second), as a subtle "getting impatient" warning that the reset is
    // imminent. Purely cosmetic: runs on top of the existing countdown
    // without touching inputEnabled or the timing itself.
    public float waveLeadDuration = 3f;
    // How many times the ripple sweeps across during waveLeadDuration —
    // each pulse is a full left-to-right column cascade in its own right,
    // not a continuation of the last one.
    public int wavePulseCount = 3;
    // How far cells lift (in UI units) and scale up at the peak of their
    // pop. Kept small — this is meant to read as a subtle ripple, not a
    // bounce.
    public float wavePopHeight = 10f;
    public float wavePopScale = 1.05f;
    // How long a single cell's lift-and-settle takes, start to finish.
    // Longer than the pop's amplitude might suggest — the smoothing curve
    // (see PopColumn) already eases gently in and out, so a slower
    // duration is what actually reads as smooth rather than snappy. Needs
    // to comfortably fit within a single pulse's share of the lead time
    // (waveLeadDuration / wavePulseCount) or successive pulses will start
    // to overlap.
    public float wavePopDuration = 0.5f;

    [Header("Grid Scramble — Mid-Selection Grace Period")]
    // If the player has at least one cell selected when the scramble
    // timer fires, they get this many seconds of visible countdown
    // before the scramble actually happens, rather than losing an
    // in-progress selection with no warning.
    public float midSelectionGraceDuration = 3f;
    public TMP_FontAsset graceCountdownFont;
    public float graceCountdownFontSize = 160f;
    public Color graceCountdownColor = new Color(1f, 0.3f, 0.3f, 1f); // urgent red

    // The word list most recently passed to LoadInlinePuzzle — stored so
    // a scramble can rebuild the SAME puzzle (same answers) rather than
    // needing WordsearchDialogueBridge to hand it over again each time.
    private List<InlineWord> currentInlineWords;

    // Bumped every time LoadInlinePuzzle runs, so the scramble loop can
    // tell whether the puzzle it was timing has been replaced by a
    // genuinely new one (e.g. dialogue advanced to the next <<setpuzzle>>)
    // while it was mid-wait, and abandon that stale cycle rather than
    // scrambling (or grace-counting-down for) a puzzle that isn't even
    // showing anymore.
    private int puzzleGeneration = 0;

    private TextMeshProUGUI graceCountdownText;

    // Fired when a word is found — WordsearchDialogueBridge listens
    public event System.Action<string> OnWordFound;

    // Fired whenever a full-length selection fails to match any unfound
    // word — whether it wasn't a straight line at all, or was but didn't
    // spell a valid answer. WordsearchDialogueBridge listens to play a
    // wrong-answer effect.
    public event System.Action OnWrongAnswer;

    // Fired whenever a cell is selected OR deselected (not on found/
    // wrong-answer clears) — WordsearchDialogueBridge listens to play a
    // random scuff sound.
    public event System.Action OnCellSelectionChanged;

    // Fired specifically when a cell becomes selected (turns yellow) —
    // not on deselection. WordsearchDialogueBridge listens to flash a
    // paw print over the cell.
    public event System.Action<Cell> OnCellMarkedSelected;

    // ── InlineWord: passed in from WordsearchDialogueBridge ──────────────

    // Defines one answer word parsed from a <<setpuzzle>> argument.
    public class InlineWord
    {
        public string wordName;
        public string[] symbols;
        public string branchValue;
    }

    // Internal runtime copy — tracks isFound without touching the
    // source data
    private class ActiveWord
    {
        public string wordName;
        public string[] symbols;
        public string branchValue;
        public bool isFound;
    }

    // ── Public API ────────────────────────────────────────────────────────

    // Load a puzzle defined inline from a <<setpuzzle>> command.
    // Clears the existing grid and builds a fresh one.
    public void LoadInlinePuzzle(List<InlineWord> inlineWords)
    {
        currentInlineWords = inlineWords;
        puzzleGeneration++;

        ClearGrid();

        activeWords = new ActiveWord[inlineWords.Count];
        for (int i = 0; i < inlineWords.Count; i++)
        {
            activeWords[i] = new ActiveWord
            {
                wordName = inlineWords[i].wordName,
                symbols = inlineWords[i].symbols,
                branchValue = inlineWords[i].branchValue,
                isFound = false
            };
        }

        BuildGrid(defaultGridWidth, defaultGridHeight);
    }

    // Changes the puzzle grid's dimensions for every puzzle loaded from
    // here on, until changed again — e.g. a one-off <<setgridsize 9 9>>
    // before the final daytime segment's puzzles, for a harder late-game
    // grid. Takes effect the next time a puzzle is built; doesn't rebuild
    // whatever grid is currently showing.
    public void SetGridSize(int width, int height)
    {
        defaultGridWidth = width;
        defaultGridHeight = height;
    }

    // ── Grid building ─────────────────────────────────────────────────────

    private void ClearGrid()
    {
        if (gridPanel != null)
        {
            foreach (Transform child in gridPanel)
            {
                // Destroy() doesn't actually remove the child until the end
                // of the frame — until then it's still a live participant
                // in gridPanel's GridLayoutGroup. BuildGrid() instantiates
                // the replacement cells (and force-rebuilds the layout)
                // within this same frame, so without deactivating first,
                // the layout pass would see BOTH the old and new cells at
                // once and lay all of them out as one oversized grid,
                // pushing the genuinely-new cells a whole grid's-height
                // lower than the panel. Deactivating makes GridLayoutGroup
                // skip it immediately, before the actual destruction happens.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        selectedCells.Clear();
        isClearingSelection = false;
        inputEnabled = false;
        grid = null;
        activeWords = null;
    }

    // Shrinks or grows the GridLayoutGroup's cell size so exactly
    // 'width' x 'height' cells (plus its existing spacing) fill
    // gridPanelFootprint — the panel's original, artist-authored size —
    // rather than overflowing it or leaving it looking sparse. Also
    // forces exactly 'width' columns per row via FixedColumnCount,
    // rather than relying on Flexible wrapping to happen to fit that
    // many per row at the computed cell size.
    private void ConfigureGridLayout(int width, int height)
    {
        if (gridPanel == null) return;

        GridLayoutGroup layoutGroup = gridPanel.GetComponent<GridLayoutGroup>();
        if (layoutGroup == null) return;

        float cellWidth = (gridPanelFootprint.x - layoutGroup.spacing.x * (width - 1)) / width;
        float cellHeight = (gridPanelFootprint.y - layoutGroup.spacing.y * (height - 1)) / height;
        float cellSize = Mathf.Min(cellWidth, cellHeight);

        layoutGroup.cellSize = new Vector2(cellSize, cellSize);
        layoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layoutGroup.constraintCount = width;
    }

    private void BuildGrid(int width, int height)
    {
        ConfigureGridLayout(width, height);

        grid = new Cell[height, width];

        string[,] gridSymbols = new string[height, width];

        PlaceWordsInGrid(gridSymbols, width, height);

        List<string> puzzleGlyphPool = BuildPuzzleGlyphPool();

        // Fill remaining cells with random hieroglyphs drawn from this
        // puzzle's own restricted pool (see BuildPuzzleGlyphPool) rather
        // than the full canonical set — cells can and will repeat the
        // same glyph, which is the point (see that method's comment).
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                if (gridSymbols[row, col] == null)
                {
                    gridSymbols[row, col] =
                        puzzleGlyphPool[Random.Range(0, puzzleGlyphPool.Count)];
                }
            }
        }

        // Spawn cell GameObjects
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                GameObject newCellObject = Instantiate(cellPrefab, gridPanel, false);
                newCellObject.name = "Cell_" + row + "_" + col;

                Cell cellScript = newCellObject.GetComponent<Cell>();
                cellScript.SetHieroglyph(gridSymbols[row, col]);
                cellScript.row = row;
                cellScript.col = col;

                grid[row, col] = cellScript;
            }
        }

        // GridLayoutGroup doesn't reposition freshly-instantiated children
        // right away — that normally happens in a deferred layout pass
        // before the next render. On the very first puzzle load that gap
        // is invisible (several frames of fade-ins pass before the player
        // can interact), but ScrambleGrid's SlideCellsIn reads each cell's
        // anchoredPosition immediately after this method returns, in the
        // same coroutine tick — without this, it would read every cell's
        // stale pre-layout position (all identical), so they'd all slide
        // to the same point and stack on top of each other.
        LayoutRebuilder.ForceRebuildLayoutImmediate(gridPanel as RectTransform);

        Debug.Log("GridManager: Built " + width + "x" + height +
                  " grid with " + (activeWords != null ? activeWords.Length : 0) + " words.");
    }

    // Builds the restricted set of glyphs used to fill this puzzle's
    // non-answer cells: every distinct glyph the answer word(s)
    // themselves use, plus a limited number of random extras drawn from
    // fillHieroglyphs' remainder — capped so the puzzle's total distinct
    // glyph count never exceeds 150% of the answer's own distinct glyph
    // count.
    //
    // The old behaviour filled every empty cell from the full ~50-glyph
    // canonical set, which made the puzzle too easy: with that many
    // glyphs to draw from, the specific glyphs making up the answer
    // rarely turned up anywhere else in the grid, so a player could spot
    // them by uniqueness alone rather than actually tracing the word.
    // Keeping the overall pool small and rooted in the answer's own
    // glyphs forces heavy repetition — including the answer's own
    // glyphs showing up as decoys elsewhere — so the only way to solve
    // it is to actually trace the correct sequence.
    private List<string> BuildPuzzleGlyphPool()
    {
        HashSet<string> answerGlyphs = new HashSet<string>();
        if (activeWords != null)
        {
            foreach (ActiveWord word in activeWords)
            {
                foreach (string symbol in word.symbols)
                {
                    answerGlyphs.Add(symbol);
                }
            }
        }

        int maxPoolSize = Mathf.CeilToInt(answerGlyphs.Count * 1.5f);
        int extraNeeded = Mathf.Max(0, maxPoolSize - answerGlyphs.Count);

        List<string> pool = new List<string>(answerGlyphs);

        // Candidates for the extra slots: whatever's in fillHieroglyphs
        // that isn't already one of the answer's own glyphs.
        List<string> remainder = new List<string>();
        foreach (string glyph in fillHieroglyphs)
        {
            if (!answerGlyphs.Contains(glyph)) remainder.Add(glyph);
        }

        // Fisher-Yates shuffle so the extras taken from the front are a
        // random sample with no repeats among themselves.
        for (int i = remainder.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string temp = remainder[i];
            remainder[i] = remainder[j];
            remainder[j] = temp;
        }

        int actualExtra = Mathf.Min(extraNeeded, remainder.Count);
        for (int i = 0; i < actualExtra; i++)
        {
            pool.Add(remainder[i]);
        }

        // Safety net for the degenerate case (no active words, so
        // nothing to build a pool from) — fall back to the full
        // canonical set rather than leaving cells with nothing to draw
        // from.
        if (pool.Count == 0)
        {
            pool.AddRange(fillHieroglyphs);
        }

        return pool;
    }

    private void PlaceWordsInGrid(string[,] gridSymbols, int width, int height)
    {
        if (activeWords == null) return;

        int[,] directions = new int[,]
        {
            {  0,  1 },   // horizontal right
            {  1,  0 },   // vertical down
            {  1,  1 },   // diagonal down-right
            {  1, -1 }    // diagonal down-left
        };

        foreach (ActiveWord word in activeWords)
        {
            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < 100)
            {
                attempts++;

                int dirIndex = Random.Range(0, 4);
                int dRow = directions[dirIndex, 0];
                int dCol = directions[dirIndex, 1];

                int startRow = Random.Range(0, height);
                int startCol = Random.Range(0, width);

                int endRow = startRow + dRow * (word.symbols.Length - 1);
                int endCol = startCol + dCol * (word.symbols.Length - 1);

                if (endRow < 0 || endRow >= height) continue;
                if (endCol < 0 || endCol >= width) continue;

                bool canPlace = true;
                for (int i = 0; i < word.symbols.Length; i++)
                {
                    int checkRow = startRow + dRow * i;
                    int checkCol = startCol + dCol * i;

                    if (gridSymbols[checkRow, checkCol] != null)
                    {
                        canPlace = false;
                        break;
                    }
                }

                if (canPlace)
                {
                    for (int i = 0; i < word.symbols.Length; i++)
                    {
                        gridSymbols[startRow + dRow * i, startCol + dCol * i] =
                            word.symbols[i];
                    }
                    placed = true;
                }
            }

            if (!placed)
            {
                Debug.LogWarning("GridManager: Could not place word '" +
                                 word.wordName + "'. Try a larger grid.");
            }
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────

    public void OnCellSelected(Cell cell)
    {
        if (!inputEnabled) return;
        if (isClearingSelection) return;
        if (cell.isPartOfFoundWord) return;

        if (selectedCells.Contains(cell))
        {
            // Clicking an already-selected cell again deselects it
            // instead of doing nothing.
            selectedCells.Remove(cell);
            cell.ResetColour();
            OnCellSelectionChanged?.Invoke();
            return;
        }

        selectedCells.Add(cell);
        OnCellMarkedSelected?.Invoke(cell);
        cell.SetHighlight(selectedColor);
        OnCellSelectionChanged?.Invoke();

        if (selectedCells.Count >= wordLength)
        {
            CheckSelection();
        }
    }

    private void CheckSelection()
    {
        if (!IsStraightLine())
        {
            // A full-length selection that isn't even a straight line
            // can never match a word — it's still a wrong answer from
            // the player's perspective, so this counts the same as the
            // straight-line-but-no-match case below.
            StartCoroutine(ClearSelectionWithDelay(0.5f));
            StartCoroutine(FireWrongAnswerAfterDelay());
            return;
        }

        string[] currentSequence = new string[selectedCells.Count];
        for (int i = 0; i < selectedCells.Count; i++)
        {
            currentSequence[i] = selectedCells[i].hieroglyphValue;
        }

        if (activeWords != null)
        {
            foreach (ActiveWord word in activeWords)
            {
                if (word.isFound) continue;
                if (currentSequence.Length != word.symbols.Length) continue;

                if (SequenceMatches(currentSequence, word.symbols))
                {
                    word.isFound = true;
                    StartCoroutine(WordFoundSequence(word));
                    return;
                }
            }
        }

        // Straight-line selection, but it didn't match any unfound word
        StartCoroutine(ClearSelectionWithDelay(0.5f));
        StartCoroutine(FireWrongAnswerAfterDelay());
    }

    // Waits wrongAnswerReactionDelay before firing OnWrongAnswer, so the
    // NPC-side reaction doesn't land in the same instant as the player's
    // last selection click. Runs alongside (not instead of)
    // ClearSelectionWithDelay, which still handles the immediate red
    // flash on the cells themselves.
    private IEnumerator FireWrongAnswerAfterDelay()
    {
        yield return new WaitForSeconds(wrongAnswerReactionDelay);
        OnWrongAnswer?.Invoke();
    }

    private bool IsStraightLine()
    {
        if (selectedCells.Count < 2) return true;

        int stepRow = selectedCells[1].row - selectedCells[0].row;
        int stepCol = selectedCells[1].col - selectedCells[0].col;

        if (Mathf.Abs(stepRow) > 1 || Mathf.Abs(stepCol) > 1) return false;
        if (stepRow == 0 && stepCol == 0) return false;

        for (int i = 1; i < selectedCells.Count; i++)
        {
            int expectedRow = selectedCells[0].row + stepRow * i;
            int expectedCol = selectedCells[0].col + stepCol * i;

            if (selectedCells[i].row != expectedRow ||
                selectedCells[i].col != expectedCol)
            {
                return false;
            }
        }

        return true;
    }

    private bool SequenceMatches(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private IEnumerator WordFoundSequence(ActiveWord foundWord)
    {
        isClearingSelection = true;

        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(foundColor);
            cell.isPartOfFoundWord = true;
        }

        yield return new WaitForSeconds(correctAnswerReactionDelay);

        selectedCells.Clear();
        isClearingSelection = false;
        inputEnabled = false;

        Debug.Log("GridManager: Word found — branchValue: " + foundWord.branchValue);

        OnWordFound?.Invoke(foundWord.branchValue);
    }

    private IEnumerator ClearSelectionWithDelay(float delay)
    {
        isClearingSelection = true;

        foreach (Cell cell in selectedCells)
            cell.SetHighlight(Color.red);

        yield return new WaitForSeconds(delay);

        foreach (Cell cell in selectedCells)
        {
            if (!cell.isPartOfFoundWord)
                cell.ResetColour();
        }

        selectedCells.Clear();
        isClearingSelection = false;
    }

    // ── Grid scramble ────────────────────────────────────────────────────

    // Runs for the lifetime of this component: waits for a puzzle to
    // actually be active (inputEnabled), times out scrambleInterval
    // seconds, then either scrambles immediately or — if the player has
    // an in-progress selection — shows the grace countdown first. Loops
    // forever, so every puzzle loaded from here on gets the same
    // treatment with no extra wiring needed elsewhere.
    private IEnumerator ScrambleLoop()
    {
        while (true)
        {
            if (!scrambleEnabled || !inputEnabled)
            {
                yield return null;
                continue;
            }

            int generationAtStart = puzzleGeneration;
            float elapsed = 0f;
            int pulsesFired = 0;
            bool halftimeFired = false;
            float pulseInterval = wavePulseCount > 0 ? waveLeadDuration / wavePulseCount : waveLeadDuration;
            float leadStart = scrambleInterval - waveLeadDuration;
            // Derived from scrambleInterval itself rather than a fixed
            // number, so it stays at the true midpoint (e.g. 15s of a 30s
            // period) even if scrambleInterval is tuned for harder/easier
            // puzzles later.
            float halftimeMark = scrambleInterval / 2f;

            while (elapsed < scrambleInterval)
            {
                if (!scrambleEnabled || !inputEnabled || puzzleGeneration != generationAtStart) break;

                // One single ripple at the midpoint — a "still going?"
                // nudge distinct from the countdown pulses below. Guarded
                // against leadStart so it can never coincide with (or fire
                // inside) the final countdown window on a very short
                // scrambleInterval.
                if (!halftimeFired && elapsed >= halftimeMark && halftimeMark < leadStart)
                {
                    halftimeFired = true;
                    StartCoroutine(RunAnticipationWave(generationAtStart));
                }

                // Fires wavePulseCount separate ripples, evenly spaced
                // across the lead window (pulseInterval apart) rather than
                // one continuous cascade — each check is against "have we
                // reached the Nth pulse's start time yet," so this still
                // fires the right number of pulses even across frame-rate
                // hitches that skip past a boundary.
                if (pulsesFired < wavePulseCount && elapsed >= leadStart + pulsesFired * pulseInterval)
                {
                    StartCoroutine(RunAnticipationWave(generationAtStart));
                    pulsesFired++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // The puzzle this cycle was timing got solved, hidden, or
            // replaced by a new one while we were waiting — nothing to
            // scramble; go back to waiting for whatever's active now.
            if (!scrambleEnabled || !inputEnabled || puzzleGeneration != generationAtStart) continue;

            if (selectedCells.Count > 0)
            {
                yield return StartCoroutine(RunGraceCountdown());

                // Same check again — the grace period itself may have
                // resolved the selection (found/wrong answer) or the
                // puzzle may have moved on entirely while it ran.
                if (!scrambleEnabled || !inputEnabled || puzzleGeneration != generationAtStart) continue;
            }

            yield return StartCoroutine(ScrambleGrid());
        }
    }

    // One ripple: cascades a lift-and-pop across the grid one column at a
    // time, left to right. ScrambleLoop calls this once per pulse (see
    // wavePulseCount), so the columns are staggered to fit within a
    // single pulse's own share of the lead window (waveLeadDuration /
    // wavePulseCount) rather than the full lead window — otherwise this
    // pulse's cascade would still be mid-flight when the next pulse fires.
    // Each column's own pop runs independently (PopColumn) so neighboring
    // columns visibly overlap in flight, which is what actually reads as
    // a "ripple" rather than a strict one-at-a-time sequence.
    private IEnumerator RunAnticipationWave(int generationAtStart)
    {
        if (grid == null) yield break;

        float pulseInterval = wavePulseCount > 0 ? waveLeadDuration / wavePulseCount : waveLeadDuration;

        int columns = grid.GetLength(1);
        float columnStagger = columns > 1
            ? Mathf.Max(0f, (pulseInterval - wavePopDuration) / (columns - 1))
            : 0f;

        for (int col = 0; col < columns; col++)
        {
            if (!scrambleEnabled || !inputEnabled || puzzleGeneration != generationAtStart || grid == null)
                yield break;

            StartCoroutine(PopColumn(col, generationAtStart));

            if (col < columns - 1) yield return new WaitForSeconds(columnStagger);
        }
    }

    // Lifts every cell in one column up and slightly larger, then eases
    // them back down to exactly where they started. Uses sin² rather than
    // a plain sine: a plain sine already peaks smoothly, but still starts
    // and ends each pop at full speed (nonzero velocity the instant it
    // begins/ends), which is what reads as a "snap" rather than a gentle
    // ripple. Squaring it flattens the curve at both ends — zero velocity
    // at rest, easing smoothly up to the peak and back — so the motion
    // itself feels smooth, on top of the small amplitude (wavePopHeight/
    // wavePopScale) that keeps it subtle. Always restores each cell to
    // its exact resting scale/position at the end, whether it finished
    // naturally or bailed out early (puzzle solved or replaced mid-wave),
    // so a cell can never be left visibly lifted.
    private IEnumerator PopColumn(int col, int generationAtStart)
    {
        if (grid == null) yield break;

        int rows = grid.GetLength(0);
        List<RectTransform> rects = new List<RectTransform>();
        List<Vector2> basePositions = new List<Vector2>();

        for (int row = 0; row < rows; row++)
        {
            Cell cell = grid[row, col];
            if (cell == null) continue;

            RectTransform rect = cell.GetComponent<RectTransform>();
            rects.Add(rect);
            basePositions.Add(rect.anchoredPosition);
        }

        float elapsed = 0f;
        while (elapsed < wavePopDuration)
        {
            if (!scrambleEnabled || !inputEnabled || puzzleGeneration != generationAtStart) break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / wavePopDuration);
            float sine = Mathf.Sin(t * Mathf.PI);
            float lift = sine * sine;
            float scale = Mathf.Lerp(1f, wavePopScale, lift);

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i] == null) continue;
                rects[i].anchoredPosition = basePositions[i] + new Vector2(0f, wavePopHeight * lift);
                rects[i].localScale = new Vector3(scale, scale, 1f);
            }

            yield return null;
        }

        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i] == null) continue;
            rects[i].anchoredPosition = basePositions[i];
            rects[i].localScale = Vector3.one;
        }
    }

    // Counts down on screen for midSelectionGraceDuration seconds, or
    // until the player's selection resolves on its own (found, wrong
    // answer, or manually deselecting everything) — whichever comes
    // first — before the scramble in ScrambleLoop actually proceeds.
    private IEnumerator RunGraceCountdown()
    {
        EnsureGraceCountdownText();
        if (graceCountdownText == null) yield break;

        graceCountdownText.gameObject.SetActive(true);

        float remaining = midSelectionGraceDuration;
        while (remaining > 0f && selectedCells.Count > 0)
        {
            graceCountdownText.text = Mathf.CeilToInt(remaining).ToString();
            remaining -= Time.deltaTime;
            yield return null;
        }

        graceCountdownText.gameObject.SetActive(false);
    }

    // Builds (once) a big, centered countdown label directly over the
    // grid panel — a sibling inserted right after gridPanel itself
    // (rather than a child of it) so ClearGrid()'s "destroy every child"
    // sweep on the next puzzle load can never take it out along with the
    // cells.
    private void EnsureGraceCountdownText()
    {
        if (graceCountdownText != null) return;
        if (gridPanel == null) return;

        RectTransform panelRect = gridPanel as RectTransform;
        if (panelRect == null) return;

        GameObject textObject = new GameObject("ScrambleGraceCountdown", typeof(RectTransform));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(panelRect.parent, false);
        rect.anchorMin = panelRect.anchorMin;
        rect.anchorMax = panelRect.anchorMax;
        rect.pivot = panelRect.pivot;
        rect.anchoredPosition = panelRect.anchoredPosition;
        rect.sizeDelta = panelRect.sizeDelta;
        rect.SetSiblingIndex(panelRect.GetSiblingIndex() + 1); // render on top of the grid panel and all its cells

        graceCountdownText = textObject.AddComponent<TextMeshProUGUI>();
        graceCountdownText.alignment = TextAlignmentOptions.Center;
        graceCountdownText.fontSize = graceCountdownFontSize;
        graceCountdownText.color = graceCountdownColor;
        graceCountdownText.fontStyle = FontStyles.Bold;
        graceCountdownText.raycastTarget = false;
        if (graceCountdownFont != null) graceCountdownText.font = graceCountdownFont;

        textObject.SetActive(false);
    }

    // Clears any in-progress selection, slides the current cells out in
    // a random scatter, rebuilds the puzzle from the SAME word list
    // (fresh random placement, same answers — see LoadInlinePuzzle), then
    // slides the new cells in from a random scatter to settle into their
    // laid-out positions. Leaves inputEnabled true throughout except
    // while the cells are physically mid-flight, since this is a reset,
    // not an end to the puzzle.
    private IEnumerator ScrambleGrid()
    {
        if (selectedCells.Count > 0)
        {
            foreach (Cell cell in selectedCells)
            {
                if (!cell.isPartOfFoundWord) cell.ResetColour();
            }
            selectedCells.Clear();
        }

        inputEnabled = false;

        yield return StartCoroutine(SlideCellsOut());

        if (currentInlineWords != null)
        {
            LoadInlinePuzzle(currentInlineWords);
        }

        yield return StartCoroutine(SlideCellsIn());

        inputEnabled = true;
    }

    // Scatters every currently-visible cell outward from its own
    // position in a random direction, easing in (starts slow,
    // accelerates away) — the "cells flying apart" half of the scramble.
    private IEnumerator SlideCellsOut()
    {
        if (grid == null) yield break;

        List<RectTransform> cellRects = new List<RectTransform>();
        List<Vector2> startPositions = new List<Vector2>();
        List<Vector2> offsets = new List<Vector2>();

        foreach (Cell cell in grid)
        {
            if (cell == null) continue;

            RectTransform rect = cell.GetComponent<RectTransform>();
            cellRects.Add(rect);
            startPositions.Add(rect.anchoredPosition);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            offsets.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * scrambleScatterDistance);
        }

        float elapsed = 0f;
        while (elapsed < scrambleSlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scrambleSlideDuration);
            float eased = t * t;

            for (int i = 0; i < cellRects.Count; i++)
            {
                if (cellRects[i] == null) continue;
                cellRects[i].anchoredPosition = Vector2.Lerp(startPositions[i], startPositions[i] + offsets[i], eased);
            }

            yield return null;
        }
    }

    // The freshly-rebuilt grid's cells are already laid out at their
    // correct final positions the instant they're instantiated (the
    // GridLayoutGroup on gridPanel handles that) — this captures each
    // one's already-correct position as its TARGET, displaces it out to
    // a random scattered starting point instead, then eases it back —
    // the "cells flying together" half of the scramble.
    private IEnumerator SlideCellsIn()
    {
        if (grid == null) yield break;

        List<RectTransform> cellRects = new List<RectTransform>();
        List<Vector2> targetPositions = new List<Vector2>();
        List<Vector2> startPositions = new List<Vector2>();

        foreach (Cell cell in grid)
        {
            if (cell == null) continue;

            RectTransform rect = cell.GetComponent<RectTransform>();
            cellRects.Add(rect);

            Vector2 target = rect.anchoredPosition;
            targetPositions.Add(target);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 start = target + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * scrambleScatterDistance;
            startPositions.Add(start);
            rect.anchoredPosition = start;
        }

        float elapsed = 0f;
        while (elapsed < scrambleSlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scrambleSlideDuration);
            float eased = 1f - (1f - t) * (1f - t);

            for (int i = 0; i < cellRects.Count; i++)
            {
                if (cellRects[i] == null) continue;
                cellRects[i].anchoredPosition = Vector2.Lerp(startPositions[i], targetPositions[i], eased);
            }

            yield return null;
        }

        for (int i = 0; i < cellRects.Count; i++)
        {
            if (cellRects[i] != null) cellRects[i].anchoredPosition = targetPositions[i];
        }
    }
}
