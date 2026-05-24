using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GridManager : MonoBehaviour
{
    [Header("Selection")]
    private List<Cell> selectedCells = new List<Cell>();

    // Tracks the direction the player is selecting in
    // e.g. (1, 0) means moving right, (0, 1) means moving down
    private int directionRow = 0;
    private int directionCol = 0;
    private bool directionLocked = false;

    // Colours for cell states
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color foundColor = Color.green;

    // Tracks whether a clear is already in progress
    private bool isClearingSelection = false;

    [Header("Grid Settings")]
    public int gridWidth = 6;
    public int gridHeight = 6;

    [Header("References")]
    public GameObject cellPrefab;
    public Transform gridPanel;

    private Cell[,] grid;

    private string[] hieroglyphs = new string[]
    {
        "𓁷", "𓎟", "𓀀","𓁐","𓃀","𓈖","𓌱","𓅓"
    };

    [System.Serializable]
    public class HieroglyphWord
    {
        public string wordName;
        public string[] symbols;
        public bool isFound;
    }

    [Header("Words to Hide")]
    public HieroglyphWord[] wordsToHide;

    void Start()
    {
        BuildGrid();
    }

    void BuildGrid()
    {
        grid = new Cell[gridHeight, gridWidth];

        string[,] gridSymbols = new string[gridHeight, gridWidth];

        PlaceWordsInGrid(gridSymbols);

        // Fill remaining empty spots with random hieroglyphs
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

        // Spawn the actual cell objects
        for (int row = 0; row < gridHeight; row++)
        {
            for (int col = 0; col < gridWidth; col++)
            {
                GameObject newCellObject = Instantiate(cellPrefab, gridPanel);
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
        // All four valid directions a word can be placed:
        // right, down, diagonal down-right, diagonal down-left
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

                // Pick a random direction from the four options
                int dirIndex = Random.Range(0, 4);
                int dRow = directions[dirIndex, 0];
                int dCol = directions[dirIndex, 1];

                // Pick a random starting position
                int startRow = Random.Range(0, gridHeight);
                int startCol = Random.Range(0, gridWidth);

                // Check if the word fits within the grid boundaries
                int endRow = startRow + dRow * (word.symbols.Length - 1);
                int endCol = startCol + dCol * (word.symbols.Length - 1);

                if (endRow < 0 || endRow >= gridHeight) continue;
                if (endCol < 0 || endCol >= gridWidth) continue;

                // Check if all positions needed are empty
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

                // If the space is clear, place the word
                if (canPlace)
                {
                    for (int i = 0; i < word.symbols.Length; i++)
                    {
                        int placeRow = startRow + dRow * i;
                        int placeCol = startCol + dCol * i;
                        gridSymbols[placeRow, placeCol] = word.symbols[i];
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
        // Block all input while the red flash delay is running
        if (isClearingSelection) return;

        // Don't allow selecting already found cells
        if (cell.isPartOfFoundWord) return;

        // Don't allow selecting the same cell twice
        if (selectedCells.Contains(cell)) return;

        // If this is the first cell, just add it — no direction to check yet
        if (selectedCells.Count == 0)
        {
            selectedCells.Add(cell);
            cell.SetHighlight(selectedColor);
            CheckForMatch();
            return;
        }

        // If this is the second cell, establish the direction
        if (selectedCells.Count == 1)
        {
            Cell firstCell = selectedCells[0];

            int rowDiff = cell.row - firstCell.row;
            int colDiff = cell.col - firstCell.col;

            // The cell must be exactly one step away in a valid direction
            // Valid: horizontal, vertical, or diagonal (max 1 step in any combination)
            bool isValidDirection =
                (Mathf.Abs(rowDiff) <= 1 && Mathf.Abs(colDiff) <= 1) &&
                !(rowDiff == 0 && colDiff == 0);

            if (!isValidDirection)
            {
                // Too far away — ignore this click
                return;
            }

            // Lock in the direction for the rest of this selection
            directionRow = rowDiff;
            directionCol = colDiff;
            directionLocked = true;

            selectedCells.Add(cell);
            cell.SetHighlight(selectedColor);
            CheckForMatch();
            return;
        }

        // For the third cell onwards, enforce the locked direction
        if (directionLocked)
        {
            Cell lastCell = selectedCells[selectedCells.Count - 1];

            int expectedRow = lastCell.row + directionRow;
            int expectedCol = lastCell.col + directionCol;

            // The new cell must be exactly the next step in the locked direction
            if (cell.row != expectedRow || cell.col != expectedCol)
            {
                // Wrong direction — flash red and clear
                StartCoroutine(ClearSelectionWithDelay(0.5f));
                return;
            }
        }

        selectedCells.Add(cell);
        cell.SetHighlight(selectedColor);
        CheckForMatch();
    }

    void CheckForMatch()
    {
        string[] currentSequence = new string[selectedCells.Count];
        for (int i = 0; i < selectedCells.Count; i++)
        {
            currentSequence[i] = selectedCells[i].hieroglyphValue;
        }

        // Check against every word
        foreach (HieroglyphWord word in wordsToHide)
        {
            if (word.isFound) continue;
            if (currentSequence.Length != word.symbols.Length) continue;

            bool isMatch = true;
            for (int i = 0; i < word.symbols.Length; i++)
            {
                if (currentSequence[i] != word.symbols[i])
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                word.isFound = true;
                MarkWordAsFound();
                return;
            }
        }

        // Find the longest word to know when to give up on a selection
        int longestWord = 0;
        foreach (HieroglyphWord word in wordsToHide)
        {
            if (word.symbols.Length > longestWord)
                longestWord = word.symbols.Length;
        }

        // If the player's selection is as long as the longest word
        // and still no match, trigger the red flash and clear
        if (selectedCells.Count >= longestWord)
        {
            StartCoroutine(ClearSelectionWithDelay(0.5f));
        }
    }

    // Flashes selected cells red, waits, then resets them
    IEnumerator ClearSelectionWithDelay(float delay)
    {
        // Lock input so the player can't select more cells during the flash
        isClearingSelection = true;

        // Flash all selected cells red to signal a wrong answer
        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(Color.red);
        }

        // Wait for the delay duration
        yield return new WaitForSeconds(delay);

        // Now reset all cells back to their original colour
        ClearSelection();

        // Unlock input again
        isClearingSelection = false;
    }

    void MarkWordAsFound()
    {
        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(foundColor);
            cell.isPartOfFoundWord = true;
        }

        // Reset direction tracking ready for the next selection
        ResetDirection();

        selectedCells.Clear();
        CheckForWin();
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

        // Reset direction tracking ready for the next selection
        ResetDirection();

        selectedCells.Clear();
    }

    void ResetDirection()
    {
        directionRow = 0;
        directionCol = 0;
        directionLocked = false;
    }

    void CheckForWin()
    {
        foreach (HieroglyphWord word in wordsToHide)
        {
            if (!word.isFound) return;
        }

        Debug.Log("You Win!");
    }
}
