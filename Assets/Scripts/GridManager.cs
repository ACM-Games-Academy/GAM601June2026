using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// GridManager — Yarn Spinner version.
//
// All PixelCrushers code has been removed. The GridManager no longer
// talks to any dialogue system directly. Instead it:
//   1. Only accepts input while 'inputEnabled' is true
//   2. Fires the OnWordFound event when a word is solved
//
// The WordsearchDialogueBridge script listens for that event and
// handles all communication with Yarn Spinner. This keeps the
// wordsearch completely independent of whichever dialogue system
// is in use.

public class GridManager : MonoBehaviour
{
    [Header("Selection")]
    private List<Cell> selectedCells = new List<Cell>();

    // Colours for cell states
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color foundColor = Color.green;

    private bool isClearingSelection = false;

    [Header("Input Control")]
    // When false, all cell clicks are ignored.
    // The dialogue bridge turns this on when a <<wordsearch>> command
    // runs, and off again once a word has been found.
    public bool inputEnabled = false;

    [Header("Grid Settings")]
    public int gridWidth = 6;
    public int gridHeight = 6;

    // How many cells the player picks before the answer is checked
    public int wordLength = 3;

    [Header("References")]
    public GameObject cellPrefab;
    public Transform gridPanel;

    private Cell[,] grid;

    private string[] hieroglyphs = new string[]
    {
        "𓁷", "𓎟", "𓀀", "𓁐", "𓃀", "𓈖", "𓌱", "𓅓"
    };

    [System.Serializable]
    public class HieroglyphWord
    {
        public string wordName;
        public string[] symbols;
        public bool isFound;

        // The value written to the Yarn variable $selectedPath
        // when this word is found, e.g. "Path_A" or "Path_B"
        public string branchValue;
    }

    [Header("Words to Hide")]
    public HieroglyphWord[] wordsToHide;

    // ── Event fired when a word is found ─────────────────────────────
    // The WordsearchDialogueBridge subscribes to this.
    // The string parameter is the found word's branchValue.
    public event System.Action<string> OnWordFound;

    void Start()
    {
        BuildGrid();
    }

    void BuildGrid()
    {
        grid = new Cell[gridHeight, gridWidth];

        string[,] gridSymbols = new string[gridHeight, gridWidth];

        PlaceWordsInGrid(gridSymbols);

        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                if (gridSymbols[row, col] == null)
                {
                    gridSymbols[row, col] = hieroglyphs[Random.Range(0, hieroglyphs.Length)];
                }
            }
        }

        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
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
    }

    void PlaceWordsInGrid(string[,] gridSymbols)
    {
        int[,] directions = new int[,]
        {
            {  0,  1 },   // horizontal right
            {  1,  0 },   // vertical down
            {  1,  1 },   // diagonal down-right
            {  1, -1 }    // diagonal down-left
        };

        foreach (HieroglyphWord word in wordsToHide)
        {
            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < 100)
            {
                attempts++;

                int dirIndex = Random.Range(0, 4);
                int dRow = directions[dirIndex, 0];
                int dCol = directions[dirIndex, 1];

                int startRow = Random.Range(0, gridHeight);
                int startCol = Random.Range(0, gridWidth);

                int endRow = startRow + dRow * (word.symbols.Length - 1);
                int endCol = startCol + dCol * (word.symbols.Length - 1);

                if (endRow < 0 || endRow >= gridHeight) continue;
                if (endCol < 0 || endCol >= gridWidth) continue;

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
                        gridSymbols[startRow + dRow * i, startCol + dCol * i] = word.symbols[i];
                    }
                    placed = true;
                }
            }

            if (!placed)
            {
                Debug.LogWarning("Could not place word: " + word.wordName + ". Try a larger grid.");
            }
        }
    }

    public void OnCellSelected(Cell cell)
    {
        // Ignore all input unless the dialogue has activated the puzzle
        if (!inputEnabled) return;

        if (isClearingSelection) return;
        if (cell.isPartOfFoundWord) return;
        if (selectedCells.Contains(cell)) return;

        selectedCells.Add(cell);
        cell.SetHighlight(selectedColor);

        if (selectedCells.Count >= wordLength)
        {
            CheckSelection();
        }
    }

    void CheckSelection()
    {
        if (!IsStraightLine())
        {
            StartCoroutine(ClearSelectionWithDelay(0.5f));
            return;
        }

        string[] currentSequence = new string[selectedCells.Count];
        for (int i = 0; i < selectedCells.Count; i++)
        {
            currentSequence[i] = selectedCells[i].hieroglyphValue;
        }

        foreach (HieroglyphWord word in wordsToHide)
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

        StartCoroutine(ClearSelectionWithDelay(0.5f));
    }

    bool IsStraightLine()
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

    bool SequenceMatches(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    IEnumerator WordFoundSequence(HieroglyphWord foundWord)
    {
        isClearingSelection = true;

        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(foundColor);
            cell.isPartOfFoundWord = true;
        }

        // Let the player see the green highlight before dialogue resumes
        yield return new WaitForSeconds(1.0f);

        selectedCells.Clear();
        isClearingSelection = false;

        // Lock the puzzle again until the next <<wordsearch>> command
        inputEnabled = false;

        Debug.Log("Word found — branchValue: " + foundWord.branchValue);

        // Tell whoever is listening (the dialogue bridge) which
        // answer the player found
        OnWordFound?.Invoke(foundWord.branchValue);
    }

    IEnumerator ClearSelectionWithDelay(float delay)
    {
        isClearingSelection = true;

        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(Color.red);
        }

        yield return new WaitForSeconds(delay);

        ClearSelection();

        isClearingSelection = false;
    }

    void ClearSelection()
    {
        foreach (Cell cell in selectedCells)
        {
            if (!cell.isPartOfFoundWord)
            {
                cell.ResetColour();
            }
        }

        selectedCells.Clear();
    }
}
