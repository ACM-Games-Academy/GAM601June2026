using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PixelCrushers.DialogueSystem;

public class GridManager : MonoBehaviour
{
    [Header("Selection")]
    private List<Cell> selectedCells = new List<Cell>();

    private int directionRow = 0;
    private int directionCol = 0;
    private bool directionLocked = false;

    // Colours for cell states
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color foundColor = Color.green;

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
        "𓁷", "𓎟", "𓀀", "𓁐", "𓃀", "𓈖", "𓌱", "𓅓"
    };

    [System.Serializable]
    public class HieroglyphWord
    {
        public string wordName;    // label for debugging e.g. "Path_A"
        public string[] symbols;     // the hieroglyph sequence
        public bool isFound;

        // Must match the value your conversation conditions check against
        // e.g. "Path_A", "Path_B", "Path_C"
        // Set this in the Inspector for each word
        public string branchValue;
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
        if (isClearingSelection) return;
        if (cell.isPartOfFoundWord) return;
        if (selectedCells.Contains(cell)) return;

        if (selectedCells.Count == 0)
        {
            selectedCells.Add(cell);
            cell.SetHighlight(selectedColor);
            CheckForMatch();
            return;
        }

        if (selectedCells.Count == 1)
        {
            Cell firstCell = selectedCells[0];

            int rowDiff = cell.row - firstCell.row;
            int colDiff = cell.col - firstCell.col;

            bool isValidDirection =
                (Mathf.Abs(rowDiff) <= 1 && Mathf.Abs(colDiff) <= 1) &&
                !(rowDiff == 0 && colDiff == 0);

            if (!isValidDirection) return;

            directionRow = rowDiff;
            directionCol = colDiff;
            directionLocked = true;

            selectedCells.Add(cell);
            cell.SetHighlight(selectedColor);
            CheckForMatch();
            return;
        }

        if (directionLocked)
        {
            Cell lastCell = selectedCells[selectedCells.Count - 1];
            int expectedRow = lastCell.row + directionRow;
            int expectedCol = lastCell.col + directionCol;

            if (cell.row != expectedRow || cell.col != expectedCol)
            {
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
                StartCoroutine(WordFoundSequence(word));
                return;
            }
        }

        int longestWord = 0;
        foreach (HieroglyphWord word in wordsToHide)
        {
            if (word.symbols.Length > longestWord)
                longestWord = word.symbols.Length;
        }

        if (selectedCells.Count >= longestWord)
        {
            StartCoroutine(ClearSelectionWithDelay(0.5f));
        }
    }

    IEnumerator WordFoundSequence(HieroglyphWord foundWord)
    {
        // Lock input during the found animation
        isClearingSelection = true;

        // Turn all selected cells green
        foreach (Cell cell in selectedCells)
        {
            cell.SetHighlight(foundColor);
            cell.isPartOfFoundWord = true;
        }

        // Pause briefly so the player can see the green highlight
        // before the dialogue advances
        yield return new WaitForSeconds(1.0f);

        ResetDirection();
        selectedCells.Clear();
        isClearingSelection = false;

        // ── PixelCrushers Integration ─────────────────────────────────
        //
        // Write which word was found into the PixelCrushers variable
        // "SelectedPath" so the conversation's branch conditions can
        // evaluate it. e.g. Variable["SelectedPath"] == "Path_A"
        DialogueLua.SetVariable("SelectedPath", foundWord.branchValue);

        // Also record this specific word as individually found, useful
        // for conditions in later conversations
        DialogueLua.SetVariable("Found_" + foundWord.wordName, true);

        // Send the "WordFound" message to the Dialogue System.
        // The trigger node in your conversation is paused waiting for
        // exactly this message via its Sequence field:
        //   Continue()@Message(WordFound)
        // When this fires, the conversation advances and evaluates the
        // branch conditions to pick the correct next node automatically.
        Debug.Log("WordFound sequence reached — branchValue: " + foundWord.branchValue);
        
        Sequencer.Message("WordFound");

        // ─────────────────────────────────────────────────────────────
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

        ResetDirection();
        selectedCells.Clear();
    }

    void ResetDirection()
    {
        directionRow = 0;
        directionCol = 0;
        directionLocked = false;
    }
}
