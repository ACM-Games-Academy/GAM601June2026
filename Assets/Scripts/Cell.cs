using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class Cell : MonoBehaviour
{
    public TMP_Text hieroglyphText;  // drag HieroglyphText here in Inspector
    public string hieroglyphValue;   // stores which hieroglyph this cell holds

    private Button button;
    private Image background;

    
    public int row;
    public int col;

    private Color originalColor;       // store the starting colour here
    private GridManager gridManager;

    public bool isPartOfFoundWord = false;

    void Start()
    {
        button = GetComponent<Button>();
        background = GetComponent<Image>();

        // Find the GridManager in the scene automatically
        //gridManager = FindObjectOfType<GridManager>();

        gridManager = FindAnyObjectByType<GridManager>();

        // Store the original colour the moment the game starts
        // so we always know what to revert to
        originalColor = background.color;

        button.onClick.AddListener(OnCellClicked);
    }

    public void SetHieroglyph(string symbol)
    {
        hieroglyphValue = symbol;
        hieroglyphText.text = symbol;
    }

    public void SetHighlight(Color colour)
    {
        background.color = colour;
    }

    public void ResetColour()
    {
        // Revert to the exact colour it started with
        background.color = originalColor;
    }

    void OnCellClicked()
    {
        // Tell the GridManager this cell was clicked
    gridManager.OnCellSelected(this);
    }
}