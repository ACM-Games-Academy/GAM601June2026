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
    public string[] fillHieroglyphs = new string[]
    {
        "𓁷", "𓎟", "𓀀", "𓁐", "𓃀", "𓈖", "𓌱", "𓅓"
    };

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

    private void BuildGrid(int width, int height)
    {
        grid = new Cell[height, width];

        string[,] gridSymbols = new string[height, width];

        PlaceWordsInGrid(gridSymbols, width, height);

        // Fill remaining cells with random hieroglyphs
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                if (gridSymbols[row, col] == null)
                {
                    gridSymbols[row, col] =
                        fillHieroglyphs[Random.Range(0, fillHieroglyphs.Length)];
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
