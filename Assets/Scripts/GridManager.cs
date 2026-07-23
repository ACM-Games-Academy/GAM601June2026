using System.Collections;
using System.Collections.Generic;
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
}
